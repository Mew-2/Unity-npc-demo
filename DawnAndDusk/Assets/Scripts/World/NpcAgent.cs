using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 沿锚点图移动的 NPC。
///
/// 路径优先用 NavMesh；没有烘焙数据时回退锚点图/栅格。
/// 位移仍由 root motion 驱动，NavMesh 只负责算折线，避免滑步。
///
/// 为什么用协程：
///   移动要跨多帧，逐帧 MoveTowards；协程能中途被打断（新指令覆盖旧指令），
///   比在 Update 里维护状态机简单。
/// </summary>
public class NpcAgent : MonoBehaviour
{
    [Header("依赖")]
    [Tooltip("留空会自动在场景里找 AnchorGraph")]
    public AnchorGraph graph;

    [Tooltip("留空会自动在场景里找 GridPathfinder；有它就优先用栅格 A* 绕开家具")]
    public GridPathfinder pathfinder;

    [Header("移动")]
    public float turnSpeedDegPerSec = 720f;        // 转身速度
    public float arriveDistance = 0.02f;           // 精确到达判定（root motion 下主要靠"冲过头"判定 + 吸附收尾）
    public float groundOffset = 1f;                // 胶囊中心离地高度（胶囊高 2，所以是 1）

    [Tooltip("离锚点多近就直接吸附过去（米）。根位移不受控，避免停在离目标十几厘米的地方")]
    public float snapDistance = 0.35f;

    [Tooltip("walk_end 判定「到达」的容差（米）")]
    public float reachedTolerance = 0.4f;

    [Tooltip("走到玩家身边时希望保持的贴身距离（米）；只在路径末端已超过它时才回退")]
    public float playerStopGap = 1.0f;

    [Tooltip("有 NavMesh 时优先用 NavMesh 算路径；没有则回退栅格/锚点图")]
    public bool useNavMesh = true;

    [Tooltip("单段路最长走多久（秒），超时放弃该段")]
    public float segmentTimeout = 12f;

    [Header("回退模式（没有 Animator Controller 时自动启用）")]
    [Tooltip("用动画的根位移驱动移动（脚不打滑）。没有 controller（公开仓库不带授权资产）时自动回退到代码驱动")]
    public bool useRootMotion = true;

    [Tooltip("代码驱动模式下的巡航速度（m/s）。root motion 模式下不参与")]
    public float moveSpeed = 1.07f;

    [Tooltip("代码驱动模式下的步幅校准。\n" +
             "注意：root motion 模式下这个值无效，真正缩放根位移的是 Visual 上 RootMotionRelay.scale")]
    [Range(0.3f, 1.2f)]
    public float strideScale = 0.7f;

    [Tooltip("代码驱动模式下从静止到满速的时间（秒）。root motion 模式由 WalkStart 片段自带")]
    public float accelTime = 0.6f;

    [Tooltip("代码驱动模式下终点前的减速时间（秒）。root motion 模式由 WalkStop 片段完成")]
    public float decelTime = 0.3f;

    [Tooltip("走路拐弯时是否播 Pivot 片段。该片段自带 3m 位移，与代码直线位移冲突，先关掉")]
    public bool autoPivot;

    [Tooltip("诊断用：检测到「状态在走路但身体完全不动」时重启走路片段并写日志。\n" +
             "根因（loopTime / CrossFade 归一化时长）已修，平时建议关掉")]
    public bool probeStallRecovery;

    [Tooltip("多少秒没有位移算卡死（秒），仅诊断用")]
    public float stallSeconds = 0.4f;

    /// <summary>当前所在锚点 id，用于下一次 FindPath 的起点。</summary>
    public string CurrentAnchorId { get; private set; } = "bed";

    public bool IsMoving { get; private set; }

    /// <summary>当前实际平移速度（m/s），写日志用。</summary>
    public float CurrentSpeed => _currentSpeed;

    /// <summary>是否正在刹车（只有代码驱动模式会置位），状态机用它挡一下起步</summary>
    public bool IsBraking { get; private set; }

    Coroutine _moveRoutine;
    NpcAnimationDriver _animationDriver;
    float _currentSpeed;   // 当前实际速度 m/s
    float _elapsed;        // 本次移动已经走了多久
    float _stallTimer;     // 连续无位移计时
    bool _stallLogged;
    float _stallStartAt;
    float _stallProbedAt = -1f;

    // 顿挫量化：一次走路里有多少帧是"几乎没动"的、最快冲到了多少
    int _zeroFrames;
    int _speedFrames;
    float _speedMax;
    float _speedSum;

    void Awake()
    {
        // 旧场景序列化的是 0.55m，运行时迁移到新的对话距离。
        if (playerStopGap < 0.95f)
        {
            playerStopGap = 1.0f;
        }

        if (graph == null)
        {
            graph = FindObjectOfType<AnchorGraph>();
        }

        if (graph == null)
        {
            Debug.LogError("[NpcAgent] 场景里找不到 AnchorGraph，移动不会生效。");
            return;
        }

        // 初始站在 bed 锚点上（而不是胶囊摆放的原始位置）。
        var start = AnchorPosition(CurrentAnchorId);
        if (start.HasValue)
        {
            transform.position = start.Value;
        }
    }

    /// <summary>走到指定锚点。优先用栅格 A*（会自动绕开家具），失败时退回锚点图最短路。</summary>
    /// <summary>
    /// 走到玩家身边（"过来"语义）。目的地是动态的：玩家相机前方 0.75 米处，
    /// 不再依赖固定锚点——床被还原后 front 锚点离玩家可能有 3 米，说"过来"结果只挪了 37cm。
    /// </summary>
    public void WalkToPlayer()
    {
        var cam = Camera.main != null ? Camera.main.transform : null;
        if (cam == null)
        {
            Debug.LogWarning("[NpcAgent] 找不到玩家相机，WalkToPlayer 忽略");
            return;
        }

        var toPlayer = cam.position;
        toPlayer.y = transform.position.y;

        var fromPlayer = toPlayer - transform.position;
        fromPlayer.y = 0f;
        var dir = fromPlayer.sqrMagnitude > 0.0001f ? fromPlayer.normalized : transform.forward;

        // 标准做法：先对"玩家所在位置"求路径，再把路径末端按 stopDistance 截断。
        // 不要反过来先算一个偏移点再寻路——偏移点可能正好落在床里（实测 0.75m 就落在床体内），
        // 而截断是在已经绕开障碍的路径上退，永远不会退到障碍里。
        if (pathfinder == null)
        {
            pathfinder = FindObjectOfType<GridPathfinder>();
        }

        const float stopDistance = 0.9f;
        var fullPath = new List<Vector3>();
        if (useNavMesh && TryNavMeshPathToPlayer(toPlayer, out var navPoints, out var navEndDistance))
        {
            var gap = playerStopGap > 0.05f ? playerStopGap : 0.75f;
            var trim = Mathf.Max(0f, navEndDistance - gap);
            var trimmed = TrimPathTail(navPoints, trim);
            if (trimmed.Count >= 2)
            {
                var stop = trimmed[trimmed.Count - 1];
                GameLog.Event("walk_target", "to", "player",
                    "via", $"navmesh+trim@{trim:F2}",
                    "endDist", GameLog.F(navEndDistance),
                    "stopDist", GameLog.F(FlatDistance(stop, toPlayer)),
                    "point", $"{stop.x:F2},{stop.z:F2}");
                StartMove(trimmed, "player", "navmesh");
                return;
            }

            GameLog.Event("walk_target", "to", "player", "via", "navmesh_already_close",
                "dist", GameLog.F(FlatDistance(transform.position, toPlayer)));
            TurnTo("player");
            return;
        }

        if (pathfinder != null && pathfinder.TryFindPath(transform.position, toPlayer, fullPath) && fullPath.Count >= 2)
        {
            var trimmed = TrimPathTail(fullPath, stopDistance);
            if (trimmed.Count >= 2)
            {
                GameLog.Event("walk_target", "to", "player", "via", $"grid+trim@{stopDistance:F2}",
                    "point", $"{trimmed[trimmed.Count - 1].x:F2},{trimmed[trimmed.Count - 1].z:F2}");
                StartMove(trimmed, "player");
                return;
            }

            // 已经比 stopDistance 更近：原地转身面对玩家即可
            GameLog.Event("walk_target", "to", "player", "via", "already_close",
                "dist", GameLog.F(FlatDistance(transform.position, toPlayer)));
            TurnTo("player");
            return;
        }

        // 栅格不可用（房间被家具挤到 free=17 时很常见）：退到"离玩家最近的锚点"，走锚点图，
        // 至少和「去门口」一样可靠。
        var bestAnchor = NearestAnchorTo(toPlayer);
        if (!string.IsNullOrEmpty(bestAnchor))
        {
            GameLog.Event("walk_target", "to", "player", "via", "anchor:" + bestAnchor);
            WalkTo(bestAnchor);
            return;
        }

        GameLog.Event("walk_target", "to", "player", "via", "none");
        Debug.LogWarning("[NpcAgent] 过来：找不到任何可行目的地");
    }

    /// <summary>用 NavMesh 算到某个世界点的完整折线；没有烘焙数据时返回 false。</summary>
    bool TryNavMeshPath(Vector3 target, List<Vector3> points)
    {
        points.Clear();
        if (!NavMesh.SamplePosition(target, out var hit, 2f, NavMesh.AllAreas))
        {
            return false;
        }

        var path = new NavMeshPath();
        if (!NavMesh.CalculatePath(transform.position, hit.position, NavMesh.AllAreas, path))
        {
            return false;
        }

        if (path.status != NavMeshPathStatus.PathComplete || path.corners.Length < 2)
        {
            return false;
        }

        points.AddRange(path.corners);
        return true;
    }

    /// <summary>算到玩家身边；优先取玩家面朝方向前方，避免从身后穿过玩家。</summary>
    bool TryNavMeshPathToPlayer(Vector3 playerPos, out List<Vector3> points, out float endDistance)
    {
        points = new List<Vector3>();
        endDistance = 0f;

        var gap = playerStopGap > 0.05f ? playerStopGap : 0.75f;

        // 玩家不在 NavMesh 上（坐床/站床边），SamplePosition(playerPos) 可能落到身后。
        // 先取“玩家视线前方 gap 米”的点：她应该出现在玩家正面，而不是穿过去。
        var cam = Camera.main;
        var forward = cam != null ? cam.transform.forward : transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.001f)
        {
            forward = playerPos - transform.position;
            forward.y = 0f;
        }

        var desired = forward.sqrMagnitude > 0.001f
            ? playerPos + forward.normalized * gap
            : playerPos;

        var found = NavMesh.SamplePosition(desired, out var hit, 1.2f, NavMesh.AllAreas);
        if (!found)
        {
            // 前方被家具挡住：退回玩家最近可走点，但太贴脸就不用，避免穿模
            if (!NavMesh.SamplePosition(playerPos, out hit, 3f, NavMesh.AllAreas))
            {
                return false;
            }

            if (FlatDistance(hit.position, playerPos) < gap * 0.8f)
            {
                return false;
            }
        }

        var path = new NavMeshPath();
        if (!NavMesh.CalculatePath(transform.position, hit.position, NavMesh.AllAreas, path))
        {
            return false;
        }

        if (path.status != NavMeshPathStatus.PathComplete || path.corners.Length < 2)
        {
            return false;
        }

        points.AddRange(path.corners);
        endDistance = FlatDistance(hit.position, playerPos);
        return true;
    }

    /// <summary>把折线路径的末端按 distance 截断（从终点往回退），返回新的折线。</summary>
    static List<Vector3> TrimPathTail(List<Vector3> path, float distance)
    {
        var result = new List<Vector3>(path);
        if (distance <= 0.001f)
        {
            return result;
        }

        var remaining = distance;
        while (result.Count >= 2)
        {
            var last = result[result.Count - 1];
            var prev = result[result.Count - 2];
            var segment = FlatDistance(last, prev);
            if (segment > remaining)
            {
                var dir = (prev - last);
                dir.y = 0f;
                var cut = last + dir.normalized * remaining;
                cut.y = last.y;
                result[result.Count - 1] = cut;
                return result;
            }

            remaining -= segment;
            result.RemoveAt(result.Count - 1);
        }

        return result;
    }

    /// <summary>直接按世界折线开始移动（"过来"这类动态目的地用）。</summary>
    void StartMove(List<Vector3> points, string label, string source = "grid")
    {
        if (_animationDriver == null)
        {
            _animationDriver = GetComponentInChildren<NpcAnimationDriver>();
        }

        GameLog.Event(
            "walk_accept",
            "from", CurrentAnchorId,
            "to", label,
            "source", source,
            "points", points.Count.ToString(),
            "route", string.Join(";", points.ConvertAll(p => $"{p.x:F2},{p.z:F2}")));

        if (_moveRoutine != null)
        {
            StopCoroutine(_moveRoutine);
        }

        _moveRoutine = StartCoroutine(MoveAlongPointsDispatch(points, label));
    }
    /// <summary>找离某个世界坐标最近的锚点（用锚点距离，不用寻路距离）。</summary>
    string NearestAnchorTo(Vector3 position)
    {
        if (graph == null)
        {
            return null;
        }

        var best = string.Empty;
        var bestDistance = float.MaxValue;
        foreach (var anchor in graph.anchors)
        {
            if (anchor == null || string.IsNullOrEmpty(anchor.id))
            {
                continue;
            }

            var p = AnchorPosition(anchor.id);
            if (!p.HasValue)
            {
                continue;
            }

            var d = FlatDistance(p.Value, position);
            if (d < bestDistance)
            {
                bestDistance = d;
                best = anchor.id;
            }
        }

        return best;
    }

    public void WalkTo(string anchorId)
    {
        if (graph == null)
        {
            return;
        }

        if (!graph.Has(anchorId))
        {
            Debug.LogWarning($"[NpcAgent] 锚点不存在：{anchorId}");
            return;
        }

        var target = AnchorPosition(anchorId);
        if (!target.HasValue)
        {
            return;
        }

        if (pathfinder == null)
        {
            pathfinder = FindObjectOfType<GridPathfinder>();
        }

        var points = new List<Vector3>();
        var source = "navmesh";
        if (!(useNavMesh && TryNavMeshPath(target.Value, points)))
        {
            source = "grid";
            if (pathfinder != null && pathfinder.TryFindPath(transform.position, target.Value, points) && points.Count >= 2)
            {
                // 栅格寻路的点已经是世界坐标
            }
            else
            {
                source = "anchor";
                points.Clear();
                var path = graph.FindPath(CurrentAnchorId, anchorId);
                if (path.Count < 2)
                {
                    GameLog.Event("walk_reject", "from", CurrentAnchorId, "to", anchorId, "pathLen", path.Count.ToString(), "source", source);
                    Debug.LogWarning($"[NpcAgent] 到不了 {anchorId}（不可达或已经在原地）");
                    return;
                }

                foreach (var id in path)
                {
                    var p = AnchorPosition(id);
                    if (p.HasValue)
                    {
                        points.Add(p.Value);
                    }
                }
            }
        }

        if (points.Count < 2)
        {
            GameLog.Event("walk_reject", "from", CurrentAnchorId, "to", anchorId, "reason", "no_points");
            return;
        }

        GameLog.Event(
            "walk_accept",
            "from", CurrentAnchorId,
            "to", anchorId,
            "source", source,
            "points", points.Count.ToString(),
            "route", string.Join(";", points.ConvertAll(p => $"{p.x:F2},{p.z:F2}")));

        // 新指令覆盖旧指令：停掉上一次移动。
        if (_moveRoutine != null)
        {
            StopCoroutine(_moveRoutine);
        }

        _moveRoutine = StartCoroutine(MoveAlongPointsDispatch(points, anchorId));
    }

    /// <summary>
    /// 分流：有 Animator Controller（= 有授权动画资产）就走根位移，没有就回退到代码驱动。
    /// 公开仓库不带授权资产，clone 下来没有 controller；这条回退保证「她仍然会走」（T-pose 平移），
    /// 而不是原地站 12 秒再报超时。
    /// </summary>
    IEnumerator MoveAlongPointsDispatch(List<Vector3> points, string targetAnchor)
    {
        if (_animationDriver == null)
        {
            _animationDriver = GetComponentInChildren<NpcAnimationDriver>();
        }

        var hasAnimation = _animationDriver != null && _animationDriver.HasController;
        if (useRootMotion && hasAnimation)
        {
            yield return MoveAlongRootMotion(points, targetAnchor);
            yield break;
        }

        if (useRootMotion)
        {
            GameLog.Event("walk_fallback", "anchor", targetAnchor, "reason", "no_controller");
        }

        yield return MoveAlongPoints(points, targetAnchor);
    }

    /// <summary>0.5 秒内转向目标（锚点名或 player）。</summary>
    public Coroutine TurnTo(string targetId)
    {
        return StartCoroutine(TurnRoutine(targetId));
    }

    IEnumerator TurnRoutine(string targetId)
    {
        Vector3 targetPosition;

        if (targetId == "player" && Camera.main != null)
        {
            targetPosition = Camera.main.transform.position;
        }
        else
        {
            var anchor = graph != null ? graph.Get(targetId) : null;
            if (anchor == null)
            {
                yield break;
            }

            targetPosition = anchor.transform.position + Vector3.up * groundOffset;
        }

        var direction = targetPosition - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f)
        {
            yield break;
        }

        var targetRotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
        var startRotation = transform.rotation;
        const float duration = 0.5f;
        var elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            transform.rotation = Quaternion.Slerp(startRotation, targetRotation, Mathf.Clamp01(elapsed / duration));
            yield return null;
        }

        transform.rotation = targetRotation;
    }

    /// <summary>
    /// 用动画的根位移驱动移动：身体走多远由片段决定，脚不打滑。
    /// 代码只负责"朝哪个方向走"和"什么时候停"。
    /// </summary>
    IEnumerator MoveAlongRootMotion(List<Vector3> points, string targetAnchor)
    {
        IsMoving = true;
        _elapsed = 0f;
        _currentSpeed = 0f;
        IsBraking = false;

        _zeroFrames = 0;
        _speedFrames = 0;
        _speedMax = 0f;
        _speedSum = 0f;

        if (_animationDriver == null)
        {
            _animationDriver = GetComponentInChildren<NpcAnimationDriver>();
        }

        var goal = points[points.Count - 1];
        var lastPosition = transform.position;

        for (var i = 1; i < points.Count; i++)
        {
            var target = points[i];
            var lastDistance = float.MaxValue;
            var guard = 0f;

            while (true)
            {
                var remaining = FlatDistance(transform.position, goal);

                // 这里原本有一段"最后 0.3m 把播放速率收到 0.6"的刹车逻辑。
                // 实测（decelTime 置 0 / 1 各跑一次）观感没有区别：0.45m 的窗口只影响约 0.4 秒，
                // 而收势本身由 WalkStop 片段完成，再叠一层减速属于双重减速，已删除。
                IsBraking = false;

                // 实际速度用位移反推，供动画驱动和日志使用
                var moved = FlatDistance(transform.position, lastPosition);
                if (Time.deltaTime > 0.0001f)
                {
                    _currentSpeed = Mathf.Lerp(_currentSpeed, moved / Time.deltaTime, 0.5f);
                }

                lastPosition = transform.position;

                // 卡死检测：状态机在走路、身体却不动。以前这种卡死只能等 12 秒超时才发现，
                // 现在 0.4 秒就抓现场，并主动重启片段——用"重启后动不动"来判定根因在哪一层
                // 用瞬时速度判定（而不是单帧位移），高帧率下也不会误报
                var frameSpeed = moved / Mathf.Max(Time.deltaTime, 0.0001f);
                _speedFrames++;
                _speedSum += frameSpeed;
                if (frameSpeed > _speedMax)
                {
                    _speedMax = frameSpeed;
                }

                if (frameSpeed < 0.05f)
                {
                    _zeroFrames++;
                }
                if (frameSpeed < 0.06f)
                {
                    _stallTimer += Time.deltaTime;
                    if (_stallTimer >= stallSeconds && !_stallLogged)
                    {
                        _stallLogged = true;
                        _stallStartAt = Time.time;
                        GameLog.Event(
                            "move_stall",
                            "anchor", targetAnchor,
                            "seg", i.ToString(),
                            "dist", GameLog.F(FlatDistance(transform.position, target)),
                            "remain", GameLog.F(remaining),
                            "yaw", GameLog.F(transform.eulerAngles.y),
                            "state", _animationDriver != null ? _animationDriver.CurrentStateName : "-",
                            "bodySpeed", GameLog.F(_currentSpeed),
                            "walkSec", GameLog.F(_elapsed));

                        if (probeStallRecovery && _animationDriver != null)
                        {
                            _stallProbedAt = Time.time;
                            _animationDriver.ProbeRestartWalk("move_stall:" + targetAnchor);
                        }
                    }
                }
                else
                {
                    if (_stallLogged)
                    {
                        GameLog.Event(
                            "move_resume",
                            "anchor", targetAnchor,
                            "afterProbe", GameLog.F(_stallProbedAt > 0f ? Time.time - _stallProbedAt : -1f),
                            "stalled", GameLog.F(Time.time - _stallStartAt),
                            "walkSec", GameLog.F(_elapsed));
                        _stallLogged = false;
                        _stallProbedAt = -1f;
                    }

                    _stallTimer = 0f;
                }

                var distance = FlatDistance(transform.position, target);
                if (distance <= arriveDistance)
                {
                    break;
                }

                // 冲过头（根位移不受控，可能一步迈过去）：距离先减后增就换下一段
                if (distance > lastDistance + 0.02f && lastDistance < 0.9f)
                {
                    break;
                }

                lastDistance = distance;

                // 只转向，不位移——位移由 RootMotionRelay 从 Animator 转交过来
                var direction = target - transform.position;
                direction.y = 0f;
                if (direction.sqrMagnitude > 0.0001f)
                {
                    var look = Quaternion.LookRotation(direction.normalized, Vector3.up);
                    transform.rotation = Quaternion.RotateTowards(
                        transform.rotation,
                        look,
                        turnSpeedDegPerSec * Time.deltaTime);
                }

                _elapsed += Time.deltaTime;
                guard += Time.deltaTime;
                if (guard > segmentTimeout)
                {
                    GameLog.Event(
                        "walk_timeout",
                        "anchor", targetAnchor,
                        "remain", GameLog.F(remaining),
                        "seg", i.ToString(),
                        "dist", GameLog.F(FlatDistance(transform.position, target)),
                        "state", _animationDriver != null ? _animationDriver.CurrentStateName : "-",
                        "moved", GameLog.F(moved),
                        "stallLogged", _stallLogged ? "1" : "0");
                    break;
                }

                yield return null;
            }

            CurrentAnchorId = targetAnchor;
        }

        // 收尾吸附：根位移不可控，走到最后一小段时直接对齐目标，避免"停在离目标 20cm 处"
        if (FlatDistance(transform.position, goal) < snapDistance)
        {
            transform.position = new Vector3(goal.x, transform.position.y, goal.z);
        }

        _currentSpeed = 0f;
        IsBraking = false;
        IsMoving = false;
        _moveRoutine = null;

        // 只有真正到达才说"到达"；超时/冲过头要如实记录
        var reached = FlatDistance(transform.position, goal) < reachedTolerance;
        GameLog.Event(
            "walk_end",
            "anchor", CurrentAnchorId,
            "elapsed", GameLog.F(_elapsed),
            "reached", reached ? "1" : "0",
            "zeroFrames", _zeroFrames.ToString(),
            "frames", _speedFrames.ToString(),
            "vMax", GameLog.F(_speedMax),
            "vMean", GameLog.F(_speedFrames > 0 ? _speedSum / _speedFrames : 0f));
        Debug.Log(reached ? $"[NpcAgent] 到达 {CurrentAnchorId}" : $"[NpcAgent] 未到达 {CurrentAnchorId}");
    }

    IEnumerator MoveAlongPoints(List<Vector3> points, string targetAnchor)
    {
        IsMoving = true;
        _elapsed = 0f;
        _currentSpeed = 0f;
        IsBraking = false;

        _zeroFrames = 0;
        _speedFrames = 0;
        _speedMax = 0f;
        _speedSum = 0f;

        if (_animationDriver == null)
        {
            _animationDriver = GetComponentInChildren<NpcAnimationDriver>();
        }

        var goal = points[points.Count - 1];
        GameLog.Event(
            "walk_begin",
            "from", CurrentAnchorId,
            "to", targetAnchor,
            "points", points.Count.ToString(),
            "driver", _animationDriver != null && _animationDriver.HasController ? "1" : "0");

        // 栅格路径点是 (x, 0, z) 的平面坐标，直接三维 MoveTowards 会把 root 拽到 y=0
        // （表现为"走到一半沉下去、到终点又浮起来"）。统一压到当前高度，只走 XZ。
        var walkHeight = transform.position.y;
        var flat = new List<Vector3>(points.Count);
        foreach (var p in points)
        {
            flat.Add(new Vector3(p.x, walkHeight, p.z));
        }

        points = flat;

        // points[0] 是起点，从 points[1] 开始逐段走。
        for (var i = 1; i < points.Count; i++)
        {
            var target = points[i];

            // 拐点：下一段方向与当前朝向夹角较大时，播 Pivot（脚步倒重心）
            var segment = target - transform.position;
            segment.y = 0f;
            if (autoPivot && segment.sqrMagnitude > 0.0001f && Vector3.Angle(transform.forward, segment.normalized) > 45f)
            {
                if (_animationDriver != null)
                {
                    _animationDriver.PlayPivot();
                }
            }

            while (FlatDistance(transform.position, target) > arriveDistance)
            {
                // 最后一小段直接吸附：避免"收势片段脚不动、身体还以保底速度爬"造成的滑步
                if (FlatDistance(transform.position, target) < 0.12f)
                {
                    transform.position = target;
                    break;
                }

                var toTarget = target - transform.position;
                toTarget.y = 0f; // 只在水平面移动，不上下飘

                if (toTarget.sqrMagnitude > 0.0001f)
                {
                    _elapsed += Time.deltaTime;
                    var remaining = Vector3.Distance(transform.position, goal);
                    var brakeDistance = Mathf.Max(0.05f, moveSpeed * decelTime);
                    IsBraking = decelTime > 0.001f && remaining < brakeDistance;
                    _currentSpeed = DesiredSpeed(_elapsed, remaining, brakeDistance);

                    // 1) 位置：按加减速后的速度靠近（帧率无关）
                    transform.position = Vector3.MoveTowards(
                        transform.position,
                        target,
                        _currentSpeed * Time.deltaTime);

                    // 2) 朝向：平滑转向移动方向
                    var look = Quaternion.LookRotation(toTarget.normalized, Vector3.up);
                    transform.rotation = Quaternion.RotateTowards(
                        transform.rotation,
                        look,
                        turnSpeedDegPerSec * Time.deltaTime);
                }

                yield return null; // 等下一帧
            }

            transform.position = target;
            CurrentAnchorId = targetAnchor;

            if (i < points.Count - 1)
            {
                // 途经点：记录一下，用来查"拐弯时停顿"
                GameLog.Event("waypoint", "i", i.ToString(), "walkSec", GameLog.F(_elapsed), "speed", GameLog.F(_currentSpeed));
            }
        }

        _currentSpeed = 0f;
        IsBraking = false;
        IsMoving = false;
        _moveRoutine = null;
        GameLog.Event("walk_end", "anchor", CurrentAnchorId, "elapsed", GameLog.F(_elapsed));
        Debug.Log($"[NpcAgent] 到达 {CurrentAnchorId}");
    }

    static float FlatDistance(Vector3 a, Vector3 b)
    {
        var dx = a.x - b.x;
        var dz = a.z - b.z;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    /// <summary>
    /// 速度优先级：动画片段的实际步速（避免打滑）> 兜底的加速斜坡；再叠加终点前的刹车限制。
    /// </summary>
    float DesiredSpeed(float elapsed, float remaining, float brakeDistance)
    {
        var speed = moveSpeed;

        if (_animationDriver != null && _animationDriver.HasController)
        {
            // 片段自带步速 = 脚在身体坐标里后退的快慢，身体按它走才不会滑
            // strideScale 修正"重定向后实际步幅 < 片段烘焙步幅"的差
            speed *= Mathf.Clamp(_animationDriver.SpeedScale, 0f, 1.3f) * Mathf.Max(0.1f, strideScale);
        }
        else if (accelTime > 0.001f)
        {
            speed *= Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / accelTime));
        }

        if (IsBraking && remaining < brakeDistance)
        {
            speed = Mathf.Min(speed, Mathf.Max(0.12f * moveSpeed, moveSpeed * remaining / brakeDistance));
        }

        // 兜底：别因为片段步速是 0 就卡死在原地（收势段步速接近 0，还要走完最后几厘米）
        return Mathf.Max(speed, 0.08f);
    }

    Vector3? AnchorPosition(string id)
    {
        var anchor = graph != null ? graph.Get(id) : null;
        return anchor != null ? anchor.transform.position + Vector3.up * groundOffset : (Vector3?)null;
    }

    [ContextMenu("Test: walk to window")]
    void TestWalkToWindow()
    {
        if (Application.isPlaying)
        {
            WalkTo("window");
        }
        else
        {
            Debug.Log("[NpcAgent] 先进入 Play 模式再测试。");
        }
    }

    [ContextMenu("Test: walk to bed")]
    void TestWalkToBed()
    {
        if (Application.isPlaying)
        {
            WalkTo("bed");
        }
        else
        {
            Debug.Log("[NpcAgent] 先进入 Play 模式再测试。");
        }
    }

    [ContextMenu("Test: walk to door")]
    void TestWalkToDoor()
    {
        if (Application.isPlaying)
        {
            WalkTo("door");
        }
        else
        {
            Debug.Log("[NpcAgent] 先进入 Play 模式再测试。");
        }
    }

    [ContextMenu("Test: walk to desk")]
    void TestWalkToDesk()
    {
        if (Application.isPlaying)
        {
            WalkTo("desk");
        }
        else
        {
            Debug.Log("[NpcAgent] 先进入 Play 模式再测试。");
        }
    }

    [ContextMenu("Test: walk to screen")]
    void TestWalkToScreen()
    {
        if (Application.isPlaying)
        {
            WalkTo("screen");
        }
        else
        {
            Debug.Log("[NpcAgent] 先进入 Play 模式再测试。");
        }
    }

    void OnDrawGizmosSelected()
    {
        // 选中时把当前所在锚点画出来，方便调试。
        if (graph != null && graph.Get(CurrentAnchorId) != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(graph.Position(CurrentAnchorId) + Vector3.up * 0.1f, 0.2f);
        }
    }
}
