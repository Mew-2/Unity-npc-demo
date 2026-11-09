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

    [Header("移动")]
    public float moveSpeed = 1.2f;                 // m/s，SPEC 里定的
    public float turnSpeedDegPerSec = 720f;        // 转身速度
    public float arriveDistance = 0.02f;           // 到达判定
    public float groundOffset = 1f;                // 胶囊中心离地高度（胶囊高 2，所以是 1）

    /// <summary>当前所在锚点 id，用于下一次 FindPath 的起点。</summary>
    public string CurrentAnchorId { get; private set; } = "bed";

    public bool IsMoving { get; private set; }

    Coroutine _moveRoutine;

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

    /// <summary>走到指定锚点。path = 锚点图上的 BFS 最短路。</summary>
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

        var path = graph.FindPath(CurrentAnchorId, anchorId);
        if (path.Count < 2)
        {
            Debug.LogWarning($"[NpcAgent] 到不了 {anchorId}（不可达或已经在原地）");
            return;
        }

        // 新指令覆盖旧指令：停掉上一次移动。
        if (_moveRoutine != null)
        {
            StopCoroutine(_moveRoutine);
        }

        _moveRoutine = StartCoroutine(MoveAlong(path));
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

    IEnumerator MoveAlong(List<string> path)
    {
        IsMoving = true;

        // path[0] 是起点，从 path[1] 开始逐段走。
        for (var i = 1; i < path.Count; i++)
        {
            var target = AnchorPosition(path[i]);
            if (!target.HasValue)
            {
                continue;
            }

            while (Vector3.Distance(transform.position, target.Value) > arriveDistance)
            {
                var toTarget = target.Value - transform.position;
                toTarget.y = 0f; // 只在水平面移动，不上下飘

                if (toTarget.sqrMagnitude > 0.0001f)
                {
                    // 1) 位置：朝目标匀速靠近（帧率无关）
                    transform.position = Vector3.MoveTowards(
                        transform.position,
                        target.Value,
                        moveSpeed * Time.deltaTime);

                    // 2) 朝向：平滑转向移动方向
                    var look = Quaternion.LookRotation(toTarget.normalized, Vector3.up);
                    transform.rotation = Quaternion.RotateTowards(
                        transform.rotation,
                        look,
                        turnSpeedDegPerSec * Time.deltaTime);
                }

                yield return null; // 等下一帧
            }

            transform.position = target.Value;
            CurrentAnchorId = path[i];
        }

        IsMoving = false;
        _moveRoutine = null;
        Debug.Log($"[NpcAgent] 到达 {CurrentAnchorId}");
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
