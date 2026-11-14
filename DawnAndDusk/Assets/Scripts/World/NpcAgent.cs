using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 沿锚点图移动的 NPC。
///
/// 为什么不用 NavMesh：
///   50h 里 Unity 零基础，NavMesh 要烘焙 + 调参 + 排错；
///   锚点图是"预设点 + 预设边 + 直线插值"，确定、可调试，
///   也符合"LLM 只在语义锚点里选"的设计（见 SPEC §1）。
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
    public float moveSpeed = 1.07f;                // m/s，对齐 Walker 循环片段步幅（1.421m / 1.333s）
    public float turnSpeedDegPerSec = 720f;        // 转身速度
    public float arriveDistance = 0.02f;           // 到达判定
    public float groundOffset = 1f;                // 胶囊中心离地高度（胶囊高 2，所以是 1）

    [Header("加减速")]
    [Tooltip("从静止到满速的时间（秒），对齐起步片段的抬脚节奏")]
    public float accelTime = 0.6f;

    [Tooltip("到锚点前开始减速的时间（秒），让停下时不突兀")]
    public float decelTime = 0.3f;

    [Tooltip("用动画的根位移驱动移动（脚不打滑）。关掉则回到代码算速度的旧模式")]
    public bool useRootMotion = true;

    [Tooltip("步幅校准：VRM 是重定向到 Kawaii 片段的，腿长不同导致实际步幅小于片段烘焙值。\n" +
             "1 = 完全按片段根位移；Play 时拖这个值直到脚看起来钉在地上（实测约 0.65~0.75）")]
    [Range(0.3f, 1.2f)]
    public float strideScale = 0.7f;

    [Tooltip("走路拐弯时是否播 Pivot 片段。该片段自带 3m 位移，与代码直线位移冲突，先关掉")]
    public bool autoPivot;

    /// <summary>当前所在锚点 id，用于下一次 FindPath 的起点。</summary>
    public string CurrentAnchorId { get; private set; } = "bed";

    public bool IsMoving { get; private set; }

    /// <summary>当前实际速度占 moveSpeed 的比例（0~1），供动画驱动用。</summary>
    public float SpeedNormalized => moveSpeed > 0.0001f ? Mathf.Clamp01(_currentSpeed / moveSpeed) : 0f;

    /// <summary>当前实际平移速度（m/s），写日志用。</summary>
    public float CurrentSpeed => _currentSpeed;

    /// <summary>是否正在刹车（快到终点），状态机用它切收势片段。</summary>
    public bool IsBraking { get; private set; }

    Coroutine _moveRoutine;
    NpcAnimationDriver _animationDriver;
    float _currentSpeed;   // 当前实际速度 m/s
    float _elapsed;        // 本次移动已经走了多久

    void Awake()
    {
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
        var source = "grid";
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

    /// <summary>walk 入口在 MoveAlongPoints 开头分流：useRootMotion 时走根位移版本。</summary>
    IEnumerator MoveAlongPointsDispatch(List<Vector3> points, string targetAnchor)
    {
        if (useRootMotion)
        {
            yield return MoveAlongRootMotion(points, targetAnchor);
        }
        else
        {
            yield return MoveAlongPoints(points, targetAnchor);
        }
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
                // root motion 模式下不能用"还剩 0.3m 就刹车"——一进 WalkStop 就没有位移了，
                // 会停在离目标半米的地方。只在最后 12cm 才收势
                IsBraking = decelTime > 0.001f && remaining < 0.12f;

                // 实际速度用位移反推，供动画驱动和日志使用
                var moved = FlatDistance(transform.position, lastPosition);
                if (Time.deltaTime > 0.0001f)
                {
                    _currentSpeed = Mathf.Lerp(_currentSpeed, moved / Time.deltaTime, 0.5f);
                }

                lastPosition = transform.position;

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
                if (guard > 12f)
                {
                    GameLog.Event("walk_timeout", "anchor", targetAnchor, "remain", GameLog.F(remaining));
                    break;
                }

                yield return null;
            }

            CurrentAnchorId = targetAnchor;
        }

        // 收尾吸附：根位移不可控，走到最后一小段时直接对齐目标，避免"停在离目标 20cm 处"
        if (FlatDistance(transform.position, goal) < 0.35f)
        {
            transform.position = new Vector3(goal.x, transform.position.y, goal.z);
        }

        _currentSpeed = 0f;
        IsBraking = false;
        IsMoving = false;
        _moveRoutine = null;
        GameLog.Event("walk_end", "anchor", CurrentAnchorId, "elapsed", GameLog.F(_elapsed));
        Debug.Log($"[NpcAgent] 到达 {CurrentAnchorId}");
    }

    IEnumerator MoveAlongPoints(List<Vector3> points, string targetAnchor)
    {
        IsMoving = true;
        _elapsed = 0f;
        _currentSpeed = 0f;
        IsBraking = false;

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
                GameLog.Event("waypoint", "i", i.ToString(), "t", GameLog.F(_elapsed), "speed", GameLog.F(_currentSpeed));
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
