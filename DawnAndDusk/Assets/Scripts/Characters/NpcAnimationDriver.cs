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

    Animator _animator;
    float _speed;
    float _speedScale = 1f;
    float _logTimer;
    string _lastState = "";

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

        _animator.applyRootMotion = false;
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
        var normalized = Mathf.Repeat(state.normalizedTime, 1f);
        _speedScale = profile != null ? profile.Scale(CurrentStateName, normalized) : 1f;

        // 播放速率只跟"刹车"走。不要用 body/expected 反推——那会和状态机形成死锁：
        // 速率低 → 进 WalkStop → 片段步速归零 → 身体掉到保底速度 → 速率更低（实测卡在 0.08 m/s）
        SpeedRatio = npc.IsBraking
            ? Mathf.Clamp(npc.CurrentSpeed / Mathf.Max(npc.moveSpeed, 0.01f), 0.15f, 1f)
            : 1f;

        // Speed 参数表示"移动意图"：状态机靠它决定进/出走路，不再和实际速度互相依赖
        var target = npc.IsMoving ? 1f : 0f;
        _speed = Mathf.MoveTowards(_speed, target, Time.deltaTime * 8f);
        _animator.SetFloat("Speed", _speed);
        _animator.SetFloat("SpeedRatio", SpeedRatio);
        _animator.SetBool("Braking", npc.IsBraking);

        // 兜底：已经完全停下但状态机卡在走路状态。
        // 注意必须避开"过渡中"——每帧都 CrossFade 会把过渡不断重置，状态永远退不出去（实测踩过）。
        if (!npc.IsMoving && _speed < 0.01f && !_animator.IsInTransition(0))
        {
            var current = _animator.GetCurrentAnimatorStateInfo(0);
            if (current.IsName("Walk") || current.IsName("WalkStart")
                || (current.IsName("WalkStop") && current.normalizedTime >= 0.95f))
            {
                GameLog.Event("forced_idle", "from", CurrentStateName, "norm", GameLog.F(current.normalizedTime));
                _animator.CrossFade("Idle", 0.2f);
            }
        }

        _logTimer += Time.deltaTime;
        if (_logTimer >= 0.1f || CurrentStateName != _lastState)
        {
            _logTimer = 0f;
            _lastState = CurrentStateName;
            var inTransition = _animator.IsInTransition(0);
            var next = inTransition ? NameOf(_animator.GetNextAnimatorStateInfo(0)) : "";
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
                "pos", $"{npc.transform.position.x:F2},{npc.transform.position.z:F2}",
                "vis", $"{transform.position.x:F2},{transform.position.z:F2}",
                "cam", $"{Camera.main.transform.position.x:F2},{Camera.main.transform.position.z:F2}");
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

        var delta = -minY + 0.005f;
        transform.localPosition += Vector3.up * delta;
        Debug.Log($"[NpcAnimationDriver] 落地修正 {delta:F3}m（minY={minY:F3}）");
    }
}
