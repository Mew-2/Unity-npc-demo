using UnityEngine;

/// <summary>
/// 把 NpcAgent 的移动意图喂给 Animator，并把"当前片段在该时刻的自带步速"
/// 反向提供给 NpcAgent 当平移速度——这样脚的速度和平移速度一致，才不会打滑。
///
/// 没有 controller（公开仓库没带授权资产）时自动不工作，模型保持 T-pose。
/// </summary>
public class NpcAnimationDriver : MonoBehaviour
{
    public NpcAgent npc;
    public KawaiiMotionProfile profile;

    [Tooltip("起步片段播放倍速：0.8 秒的抬脚段原速播放观感偏慢，1.3~1.5 更像真的迈步")]
    public float startPlayback = 1.35f;

    [Tooltip("收势片段播放倍速：1.0 秒的停步段原速播放会像慢动作，1.4~1.6 更利落")]
    public float stopPlayback = 1.5f;

    Animator _animator;
    float _speed;
    float _speedScale = 1f;
    float _logTimer;
    string _lastState = "";
    Vector3? _visualBaseLocal;
    float _idleWhileMoving;

    /// <summary>当前 Animator 状态名（Idle / WalkStart / Walk / WalkStop / Pivot ...）。</summary>
    public string CurrentStateName { get; private set; } = "Idle";

    /// <summary>当前片段自带步速相对 moveSpeed 的比例。</summary>
    public float SpeedScale => _speedScale;

    /// <summary>走路片段的播放速率（1 = 原速）。</summary>
    public float SpeedRatio { get; private set; } = 1f;

    public bool HasController => _animator != null && _animator.runtimeAnimatorController != null;

    void Awake()
    {
        _animator = GetComponentInChildren<Animator>();
        if (npc == null)
        {
            npc = GetComponentInParent<NpcAgent>();
        }
    }

    void Start()
    {
        if (!HasController)
        {
            enabled = false;
            return;
        }

        if (profile == null)
        {
            profile = Resources.Load<KawaiiMotionProfile>("KawaiiMotionProfile");
        }

        // 根位移交给 RootMotionRelay（由 CharacterSwitcher 设置 applyRootMotion = true），
        // 这里不要再关掉，否则又回到"代码算速度拖着脚走"的滑步模式
        _animator.applyRootMotion = _animator.runtimeAnimatorController != null;
        GameLog.Event("driver_ready", "hasProfile", profile != null ? "1" : "0", "refSpeed", GameLog.F(profile != null ? profile.referenceSpeed : 0f));
        StartCoroutine(GroundOnce());
    }

    void Update()
    {
        if (!HasController || npc == null)
        {
            return;
        }

        var state = _animator.GetCurrentAnimatorStateInfo(0);
        CurrentStateName = NameOf(state);

        // 起步/收势都是"短位移、长时长"的过渡片段，原速播放看起来像慢动作。
        // 用 Animator.speed 提倍速（根位移同比加快，步幅不变），正常走路时恢复 1
        _animator.speed = CurrentStateName == "WalkStart"
            ? Mathf.Max(0.1f, startPlayback)
            : CurrentStateName == "WalkStop"
                ? Mathf.Max(0.1f, stopPlayback)
                : 1f;
        var normalized = Mathf.Repeat(state.normalizedTime, 1f);
        _speedScale = profile != null ? profile.Scale(CurrentStateName, normalized) : 1f;

        // 播放速率只跟"刹车"走。不要用 body/expected 反推——那会和状态机形成死锁：
        // 速率低 → 进 WalkStop → 片段步速归零 → 身体掉到保底速度 → 速率更低（实测卡在 0.08 m/s）
        // 下限从 0.15 提到 0.6：0.15 会让最后 10cm 爬 0.6 秒，看着像子弹时间
        SpeedRatio = npc.IsBraking ? Mathf.Lerp(0.6f, 1f, npc.BrakeRatio) : 1f;

        // Speed 参数表示"移动意图"：状态机靠它决定进/出走路，不再和实际速度互相依赖
        var target = npc.IsMoving ? 1f : 0f;
        _speed = Mathf.MoveTowards(_speed, target, Time.deltaTime * 8f);
        _animator.SetFloat("Speed", _speed);
        _animator.SetFloat("SpeedRatio", SpeedRatio);
        _animator.SetBool("Braking", npc.IsBraking);

        // 兜底：已经完全停下但状态机卡在走路状态。
        // root motion 模式下不能做这件事——它会把正在走路的她切回 Idle，
        // 而 Idle 没有根位移，人就"卡在半路不动"（实测踩过）
        if (!npc.useRootMotion && !npc.IsMoving && _speed < 0.01f && !_animator.IsInTransition(0))
        {
            var current = _animator.GetCurrentAnimatorStateInfo(0);
            if (current.IsName("Walk") || current.IsName("WalkStart")
                || (current.IsName("WalkStop") && current.normalizedTime >= 0.95f))
            {
                GameLog.Event("forced_idle", "from", CurrentStateName, "norm", GameLog.F(current.normalizedTime));
                // CrossFade 的时长是"归一化时间"，CrossFadeInFixedTime 才是秒
                _animator.CrossFadeInFixedTime("Idle", 0.2f);
            }
        }

        // 反向兜底：该走却在 Idle（例如被别的逻辑切走了）→ 拉回走路状态。
        // 必须等一会儿再动手：状态机自己的 Idle→WalkStart 需要 Speed 爬到 0.5（约 0.06 秒），
        // 这里如果第一帧就抢着 CrossFade，用的还是"归一化"时长——Idle 有 6.67 秒，
        // 0.1 归一化 = 0.67 秒混合，WalkStart 在混合期间已经播到 0.8 之后，退出点被跨过去，
        // 于是起步后必然出现约 0.6 秒的零位移空档（实测 zeroFrames=165）。
        var waitingInIdle = npc.useRootMotion && npc.IsMoving && CurrentStateName == "Idle" && !_animator.IsInTransition(0);
        _idleWhileMoving = waitingInIdle ? _idleWhileMoving + Time.deltaTime : 0f;
        if (_idleWhileMoving > 0.35f)
        {
            GameLog.Event("forced_walk", "from", CurrentStateName, "wait", GameLog.F(_idleWhileMoving));
            _animator.CrossFadeInFixedTime("WalkStart", 0.1f);
            _idleWhileMoving = 0f;
        }

        _logTimer += Time.deltaTime;
        if (_logTimer >= 0.1f || CurrentStateName != _lastState)
        {
            _logTimer = 0f;
            _lastState = CurrentStateName;
            var inTransition = _animator.IsInTransition(0);
            var next = inTransition ? NameOf(_animator.GetNextAnimatorStateInfo(0)) : "";

            // 直接量脚：判断"脚是否打滑"要看着地的脚在世界里有没有位移，
            // 而不是拿身体速度反推片段速度
            var lf = _animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            var rf = _animator.GetBoneTransform(HumanBodyBones.RightFoot);
            var lfPos = lf != null ? lf.position : Vector3.zero;
            var rfPos = rf != null ? rf.position : Vector3.zero;

            GameLog.Event(
                "anim",
                "state", CurrentStateName,
                "nt", GameLog.F(state.normalizedTime),
                "norm", GameLog.F(normalized),
                "scale", GameLog.F(_speedScale),
                "ratio", GameLog.F(SpeedRatio),
                "bodySpeed", GameLog.F(npc.CurrentSpeed),
                "param", GameLog.F(_speed),
                "trans", inTransition ? "1" : "0",
                "next", next,
                "moving", npc.IsMoving ? "1" : "0",
                "braking", npc.IsBraking ? "1" : "0",
                "pos", $"{npc.transform.position.x:F2},{npc.transform.position.y:F2},{npc.transform.position.z:F2}",
                "vis", $"{transform.position.x:F2},{transform.position.y:F2},{transform.position.z:F2}",
                "visLocalY", GameLog.F(transform.localPosition.y),
                "aSpeed", GameLog.F(_animator.speed),
                "apply", _animator.applyRootMotion ? "1" : "0",
                "layerW", GameLog.F(_animator.GetLayerWeight(0)),
                "len", GameLog.F(state.length),
                "stSpeed", GameLog.F(state.speed),
                "loop", state.loop ? "1" : "0",
                "cam", $"{Camera.main.transform.position.x:F2},{Camera.main.transform.position.y:F2},{Camera.main.transform.position.z:F2}",
                "lf", $"{lfPos.x:F2},{lfPos.y:F2},{lfPos.z:F2}",
                "rf", $"{rfPos.x:F2},{rfPos.y:F2},{rfPos.z:F2}");
        }
    }

    public void SetSitting(bool sitting)
    {
        if (HasController)
        {
            _animator.SetBool("Sit", sitting);
        }
    }

    public void PlayBow()
    {
        if (HasController)
        {
            _animator.SetTrigger("Bow");
        }
    }

    public void PlayPivot()
    {
        if (HasController)
        {
            _animator.SetTrigger("Pivot");
        }
    }

    /// <summary>
    /// 诊断用：NpcAgent 发现"状态在走路、身体完全不动"时，强行重启走路片段。
    /// 结果会写进日志——如果重启后恢复位移，说明根因在状态机/片段（停在零位移的那一帧）；
    /// 如果重启也不动，说明 Animator 层面根本没在产生根位移。
    /// </summary>
    public void ProbeRestartWalk(string reason)
    {
        if (!HasController)
        {
            return;
        }

        var state = _animator.GetCurrentAnimatorStateInfo(0);
        GameLog.Event(
            "stall_probe",
            "reason", reason,
            "from", CurrentStateName,
            "nt", GameLog.F(state.normalizedTime),
            "len", GameLog.F(state.length),
            "aSpeed", GameLog.F(_animator.speed),
            "trans", _animator.IsInTransition(0) ? "1" : "0");
        _animator.CrossFadeInFixedTime("WalkStart", 0.05f);
    }

    static string NameOf(AnimatorStateInfo state)
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

        return "Idle";
    }

    System.Collections.IEnumerator GroundOnce()
    {
        // 等动画播两帧再量脚底
        yield return null;
        yield return null;

        // 只在 Idle 姿势下量：WalkStart/WalkStop 里量到的是抬脚姿势，会把模型整体顶高或压底
        for (var i = 0; i < 30 && !_animator.GetCurrentAnimatorStateInfo(0).IsName("Idle"); i++)
        {
            yield return null;
        }

        if (_visualBaseLocal == null)
        {
            _visualBaseLocal = transform.localPosition;
        }

        var left = _animator.GetBoneTransform(HumanBodyBones.LeftFoot);
        var right = _animator.GetBoneTransform(HumanBodyBones.RightFoot);
        var leftToes = _animator.GetBoneTransform(HumanBodyBones.LeftToes);
        var rightToes = _animator.GetBoneTransform(HumanBodyBones.RightToes);

        var minY = float.MaxValue;

        // 烘焙当前姿势的顶点，取真实最低点（鞋底），避免用可能过期的 bounds
        foreach (var skinned in GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            if (skinned == null || !skinned.enabled || skinned.sharedMesh == null)
            {
                continue;
            }

            var hasFoot = false;
            foreach (var bone in skinned.bones)
            {
                if (bone == left || bone == right || bone == leftToes || bone == rightToes)
                {
                    hasFoot = true;
                    break;
                }
            }

            if (!hasFoot)
            {
                continue;
            }

            var mesh = new Mesh();
            skinned.BakeMesh(mesh);
            var vertices = mesh.vertices;
            for (var i = 0; i < vertices.Length; i++)
            {
                minY = Mathf.Min(minY, skinned.transform.TransformPoint(vertices[i]).y);
            }

            Destroy(mesh);
        }

        if (minY == float.MaxValue)
        {
            yield break;
        }

        var delta = Mathf.Clamp(-minY + 0.005f, -0.25f, 0.25f);

        // 关键：按"基准位置 + 偏移"绝对赋值，不要累加——累加会在每次停下时漂移，
        // 表现就是"走着走着沉下去又浮起来"
        transform.localPosition = _visualBaseLocal.Value + Vector3.up * delta;
        Debug.Log($"[NpcAnimationDriver] 落地修正 {delta:F3}m（minY={minY:F3}）");
    }
}
