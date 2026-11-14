using UnityEngine;

/// <summary>
/// 把 Animator 的根位移转交给 NPC 根节点。
///
/// 为什么需要：`applyRootMotion = true` 时，Unity 会把片段里的根位移按目标骨架重定向后
/// 应用到**带有 Animator 的那个物体**（这里是 Visual）。而逻辑层的 NpcAgent 挂在父节点 Npc 上，
/// 所以要把 Visual 上产生的位移转交给 Npc。
///
/// 这样身体走多远完全由动画决定，脚不会再打滑——之前用手动速度硬凑，
/// 重定向后步幅在循环内是变化的，任何常数都对不齐。
/// </summary>
[RequireComponent(typeof(Animator))]
public class RootMotionRelay : MonoBehaviour
{
    [Tooltip("位移转交给谁（通常是 Npc 根节点）")]
    public Transform target;

    [Tooltip("整体缩放：1 = 完全按动画的位移")]
    public float scale = 1f;

    [Tooltip("关掉后位移不再转交（例如切到 T-pose 回退时）")]
    public bool relay = true;

    Animator _animator;
    float _logTimer;
    float _deltaSum;
    int _deltaCount;

    void Awake()
    {
        _animator = GetComponent<Animator>();
        if (target == null)
        {
            target = transform.parent;
        }
    }

    void OnAnimatorMove()
    {
        if (!relay || target == null)
        {
            return;
        }

        var delta = _animator.deltaPosition * scale;

        // 诊断：每 0.5 秒把"这一小段收到的位移"和根位移开关状态写进日志
        _logTimer += Time.deltaTime;
        _deltaSum += delta.magnitude;
        _deltaCount++;
        if (_logTimer >= 0.5f)
        {
            GameLog.Event(
                "rootmotion",
                "apply", _animator.applyRootMotion ? "1" : "0",
                "avatar", _animator.avatar != null ? (_animator.isHuman ? "human" : "generic") : "null",
                "deltaSum", GameLog.F(_deltaSum),
                "frames", _deltaCount.ToString(),
                "relayOn", relay ? "1" : "0");
            _logTimer = 0f;
            _deltaSum = 0f;
            _deltaCount = 0;
        }

        delta.y = 0f; // 只在水平面移动，垂直由贴地逻辑负责
        target.position += delta;

        // 注意：这里不动 target.rotation——朝向由 NpcAgent 的转向逻辑控制，
        // 片段里的转身（Pivot）如果需要，可以在这里叠加 deltaRotation。
    }
}
