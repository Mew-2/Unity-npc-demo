using System.Collections.Generic;
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
///
/// 卡死诊断：一旦"状态还在 Walk/WalkStart、根位移却连续为 0"，这里会打一条 `rm_stall` 全量快照。
/// 快照把 Animator 的所有可读状态（applyRootMotion / speed / 层权重 / rootPosition / bodyPosition /
/// pivotPosition / velocity / 过渡信息 / 全部参数）都记下来——目的是把
/// 「片段本身没位移」和「位移被别处吃掉」这两种完全不同的原因一次分开，不再靠猜。
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

    [Tooltip("卡死快照：根位移为 0 多久算卡死（秒）")]
    public float stallSeconds = 0.25f;

    [Tooltip("卡死期间最多记几条快照（每秒一条）")]
    public int stallMaxLogs = 5;

    Animator _animator;
    float _logTimer;
    float _deltaSum;
    int _deltaCount;

    float _zeroTimer;      // 本轮连续零位移已持续多久（未记满时的计时）
    float _stallWindow;    // 本次卡死累计持续时长
    float _lastLogAt;      // 上次快照时的 _stallWindow
    int _stallLogs;
    bool _stallActive;

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

        TrackStall(delta);

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
                "relayOn", relay ? "1" : "0",
                "aSpeed", GameLog.F(_animator.speed));
            _logTimer = 0f;
            _deltaSum = 0f;
            _deltaCount = 0;
        }

        delta.y = 0f; // 只在水平面移动，垂直由贴地逻辑负责
        target.position += delta;

        // 注意：这里不动 target.rotation——朝向由 NpcAgent 的转向逻辑控制，
        // 片段里的转身（Pivot）如果需要，可以在这里叠加 deltaRotation。
    }

    /// <summary>状态机在走路、水平位移却是 0：立刻抓一次全量快照。</summary>
    void TrackStall(Vector3 delta)
    {
        var walking = IsWalkingState();
        var flat = new Vector2(delta.x, delta.z);

        if (!walking || flat.sqrMagnitude > 1e-8f)
        {
            if (_stallActive)
            {
                GameLog.Event(
                    "rm_resume",
                    "after", GameLog.F(_stallWindow),
                    "logs", _stallLogs.ToString(),
                    "state", CurrentStateName());
            }

            _stallActive = false;
            _zeroTimer = 0f;
            _stallWindow = 0f;
            _lastLogAt = 0f;
            _stallLogs = 0;
            return;
        }

        _zeroTimer += Time.deltaTime;
        _stallWindow += Time.deltaTime;

        if (_stallLogs >= stallMaxLogs)
        {
            return;
        }

        // 第一次 0.25s 就记；之后每秒补一条，看状态有没有变化（例如过渡结束、片段时间推进）
        var due = _stallLogs == 0 ? _zeroTimer >= stallSeconds : _stallWindow - _lastLogAt >= 1f;
        if (!due)
        {
            return;
        }

        _stallActive = true;
        _stallLogs++;
        _lastLogAt = _stallWindow;

        var kv = new List<string>
        {
            "window", GameLog.F(_stallWindow),
            "n", _stallLogs.ToString(),
        };
        AppendAnimator(kv);
        GameLog.Event("rm_stall", kv.ToArray());
    }

    bool IsWalkingState()
    {
        var state = _animator.GetCurrentAnimatorStateInfo(0);
        return state.IsName("Walk") || state.IsName("WalkStart");
    }

    string CurrentStateName() => StateName(_animator.GetCurrentAnimatorStateInfo(0));

    /// <summary>把 Animator 上一切可读的证据一次性摊平——排查时不用再改代码重跑。</summary>
    void AppendAnimator(List<string> kv)
    {
        kv.Add("apply");
        kv.Add(_animator.applyRootMotion ? "1" : "0");
        kv.Add("enabled");
        kv.Add(_animator.enabled ? "1" : "0");
        kv.Add("aSpeed");
        kv.Add(GameLog.F(_animator.speed));
        kv.Add("updateMode");
        kv.Add(_animator.updateMode.ToString());
        kv.Add("culling");
        kv.Add(_animator.cullingMode.ToString());
        kv.Add("layers");
        kv.Add(_animator.layerCount.ToString());
        kv.Add("layerW");
        kv.Add(GameLog.F(_animator.layerCount > 0 ? _animator.GetLayerWeight(0) : 0f));
        kv.Add("init");
        kv.Add(_animator.isInitialized ? "1" : "0");
        kv.Add("bound");
        kv.Add(_animator.hasBoundPlayables ? "1" : "0");
        kv.Add("ctrl");
        kv.Add(_animator.runtimeAnimatorController != null ? _animator.runtimeAnimatorController.name : "null");
        kv.Add("avatar");
        kv.Add(_animator.avatar != null ? (_animator.isHuman ? "human" : "generic") : "null");

        var state = _animator.GetCurrentAnimatorStateInfo(0);
        kv.Add("st");
        kv.Add(StateName(state));
        kv.Add("nt");
        kv.Add(GameLog.F(state.normalizedTime));
        kv.Add("len");
        kv.Add(GameLog.F(state.length));
        kv.Add("stSpeed");
        kv.Add(GameLog.F(state.speed));
        kv.Add("loop");
        kv.Add(state.loop ? "1" : "0");

        var inTransition = _animator.IsInTransition(0);
        kv.Add("trans");
        kv.Add(inTransition ? "1" : "0");
        if (inTransition)
        {
            var transition = _animator.GetAnimatorTransitionInfo(0);
            var next = _animator.GetNextAnimatorStateInfo(0);
            kv.Add("tDur");
            kv.Add(GameLog.F(transition.duration));
            kv.Add("tNorm");
            kv.Add(GameLog.F(transition.normalizedTime));
            kv.Add("tAny");
            kv.Add(transition.anyState ? "1" : "0");
            kv.Add("next");
            kv.Add(StateName(next));
            kv.Add("nextNt");
            kv.Add(GameLog.F(next.normalizedTime));
        }

        // 这三组是判定"片段没位移"还是"位移被吃掉"的关键：
        // deltaPosition/velocity = 上一帧的根位移；rootPosition = 当前帧从动画算出的根位置
        kv.Add("deltaPos");
        kv.Add(V(_animator.deltaPosition));
        kv.Add("vel");
        kv.Add(V(_animator.velocity));
        kv.Add("rootPos");
        kv.Add(V(_animator.rootPosition));
        kv.Add("bodyPos");
        kv.Add(V(_animator.bodyPosition));
        kv.Add("pivot");
        kv.Add($"{_animator.pivotPosition.x:F3},{_animator.pivotPosition.z:F3}");
        kv.Add("deltaYaw");
        kv.Add(GameLog.F(_animator.deltaRotation.eulerAngles.y));

        kv.Add("timeScale");
        kv.Add(GameLog.F(Time.timeScale));
        kv.Add("dt");
        kv.Add(GameLog.F(Time.deltaTime));
        kv.Add("frame");
        kv.Add(Time.frameCount.ToString());

        // 全量参数：Sit / Bow / Pivot 之类被别的逻辑顺手触发，会在这里露出来
        var parameters = _animator.parameters;
        kv.Add("paramCount");
        kv.Add(parameters.Length.ToString());
        foreach (var parameter in parameters)
        {
            var key = "p_" + parameter.name;
            switch (parameter.type)
            {
                case AnimatorControllerParameterType.Float:
                    kv.Add(key);
                    kv.Add(GameLog.F(_animator.GetFloat(parameter.nameHash)));
                    break;
                case AnimatorControllerParameterType.Bool:
                    kv.Add(key);
                    kv.Add(_animator.GetBool(parameter.nameHash) ? "1" : "0");
                    break;
                case AnimatorControllerParameterType.Int:
                    kv.Add(key);
                    kv.Add(_animator.GetInteger(parameter.nameHash).ToString());
                    break;
                default:
                    kv.Add(key);
                    kv.Add("trigger");
                    break;
            }
        }
    }

    static string V(Vector3 value) => $"{value.x:F3},{value.y:F3},{value.z:F3}";

    static string StateName(AnimatorStateInfo state)
    {
        if (state.IsName("WalkStart"))
        {
            return "WalkStart";
        }

        if (state.IsName("Walk"))
        {
            return "Walk";
        }

        if (state.IsName("WalkStop"))
        {
            return "WalkStop";
        }

        if (state.IsName("Pivot"))
        {
            return "Pivot";
        }

        if (state.IsName("Sit"))
        {
            return "Sit";
        }

        if (state.IsName("Bow"))
        {
            return "Bow";
        }

        if (state.IsName("Idle"))
        {
            return "Idle";
        }

        return "?" + state.shortNameHash;
    }
}
