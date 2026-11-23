using System.Collections;
using System.Collections.Generic;
using UniVRM10;
using UnityEngine;

/// <summary>
/// 膝枕（POV）：说「想要膝枕」→ 她走到床头坐下（Sit_Start → Sit_Loop 等你）
/// → 玩家走近 → 相机降到她膝上仰视、她低头看你 → 说「起来」退出。
///
/// 座位是运行时按床的包围盒算的，不写死坐标——床的尺寸/朝向改了她会跟着变。
/// 全程写 GameLog：lap_prepare / lap_seated / lap_enter / lap_exit，便于按日志调几何。
/// </summary>
[DefaultExecutionOrder(11100)]
public class LapPillowController : MonoBehaviour
{
    [Header("依赖")]
    public NpcAgent npc;
    public NpcAnimationDriver driver;
    public RootMotionRelay relay;
    public PlayerController player;
    public PlayerLook playerLook;
    public Transform cameraTransform;
    public NpcLookAtIK lookAt;
    public DialogueUI dialogue;

    [Header("触发")]
    [Tooltip("玩家离她多近时自动进入膝枕视角（米）")]
    public float enterRadius = 1.1f;   // 玩家走到 1.1m 内才切 POV（保持"走到跟前"的设计）

    [Header("座位（相对床）")]
    [Tooltip("从床头往床尾退多少米坐下")]
    public float seatInsetFromHead = 0.55f;

    // 故意不序列化：这个字段之前在场景里存了 180，之后把代码默认值改成 0 也不生效，
    // 表现就是"改完代码，第一次坐下反而更歪"。一次性校准参数写死在代码里最稳。
    // 实测这台 VRM 的正面就是 +Z（modelYawOffset = 0 时面朝窗户）；F5 可现场翻转对照。
    [System.NonSerialized]
    public float modelYawOffset;

    [Tooltip("诊断用：坐下时隐藏裙子/连衣裙网格，确认鼓包是不是裙子本身")]
    public bool hideSkirtWhileSeated;

    [Tooltip("勾上则用下面的手写座位（Play 里直接拖 Npc 到满意位置，抄 Inspector 的坐标进来）")]
    public bool useManualSeat;

    [Tooltip("手写座位（世界坐标，根节点位置）")]
    public Vector3 manualSeat = new Vector3(3.42f, 0.85f, 1.95f);

    [Tooltip("坐好后髋骨应落在床面上方多少米（自校准的目标值）")]
    public float hipsSitOffset = 0.06f;

    [System.NonSerialized]
    public float lapForward = 0.35f;

    [Tooltip("她的膝上点：相对她脚下多高")]
    public float lapHeight = 0.04f;

    [System.NonSerialized]
    public float cameraLift = 0.10f;

    [Header("构图（非对称）")]
    [System.NonSerialized]
    public float viewYawOffset = 6f;

    [System.NonSerialized]
    public float viewPitchOffset = 0f;

    [Tooltip("进入/退出镜头过渡时长（秒）")]
    public float transitionSeconds = 1.2f;

    enum State
    {
        Idle,
        Walking,
        Seated,
        InLap,
    }

    State _state = State.Idle;
    Vector3 _savedCamLocalPos;
    Quaternion _savedCamLocalRot;
    Vector3 _seat;
    float _surfaceY;
    float _hipOffset = float.NaN;
    Vector3 _floorSpot;             // 传送上床边之前她站的位置（起身后回到这里）
    bool _hasFloorSpot;
    Vector3 _lapPoint;
    Coroutine _transition;
    Coroutine _returnRoutine;

    public const float SeatedSkirtFoldWeight = 0.3f;
    public const float SeatedArmOutwardDegrees = 8f;
    public const float SeatedElbowLift = 0.02f;
    public const float SeatedForearmForward = 0.02f;
    public const float SeatedArmSpread = 0.04f;
    public const float SeatedHandLift = 0.05f;
    public const float SeatYaw = 0f;
    public const float SeatSideInset = 0.35f;
    public const float CameraForwardOffset = 0.25f;
    public const float CameraSideOffset = 0.08f;
    public const float SeatedSkirtLift = 0.03f;
    public const float SeatedTailLiftDegrees = 50f;

    Vrm10Instance _vrm;
    Transform _tailRoot;
    Quaternion _tailInitialLocalRotation;
    readonly List<VRM10SpringBoneCollider> _legColliders = new List<VRM10SpringBoneCollider>();
    readonly List<Vrm10InstanceSpringBone.Spring> _skirtSprings = new List<Vrm10InstanceSpringBone.Spring>();
    readonly List<List<VRM10SpringBoneColliderGroup>> _skirtColliderGroups = new List<List<VRM10SpringBoneColliderGroup>>();
    readonly List<Vrm10RotationConstraint> _skirtFoldConstraints = new List<Vrm10RotationConstraint>();
    readonly List<float> _skirtFoldWeights = new List<float>();
    readonly List<Transform> _skirtFoldRoots = new List<Transform>();
    readonly List<Vector3> _skirtFoldRootPositions = new List<Vector3>();

    public bool IsActive => _state != State.Idle;

    void Awake()
    {
        EnsureRefs();
    }

    /// <summary>
    /// 依赖大多是运行时生成的（VRM 实例、Animator、Relay），所以延时解析：
    /// 找不到 NpcLookAtIK 就现场挂一个，避免要求场景里预置。
    /// </summary>
    void EnsureRefs()
    {
        if (npc == null)
        {
            return;
        }

        var animator = npc.GetComponentInChildren<Animator>();
        if (driver == null && animator != null)
        {
            driver = animator.GetComponent<NpcAnimationDriver>();
        }

        if (driver == null)
        {
            driver = npc.GetComponentInChildren<NpcAnimationDriver>();
        }

        if (animator == null && driver != null)
        {
            animator = driver.GetComponent<Animator>();
        }

        if (animator == null)
        {
            return;
        }

        if (relay == null)
        {
            relay = animator.GetComponent<RootMotionRelay>();
        }

        if (lookAt == null)
        {
            lookAt = animator.GetComponent<NpcLookAtIK>();
            if (lookAt == null)
            {
                lookAt = animator.gameObject.AddComponent<NpcLookAtIK>();
            }
        }
    }

    /// <summary>「想要膝枕」：算座位、让她走过去、坐下等你。</summary>
    public void Prepare()
    {
        if (_returnRoutine != null)
        {
            StopCoroutine(_returnRoutine);
            _returnRoutine = null;
            RestoreAfterSeat();
            GameLog.Event("lap_prepare", "cancelled", "pending_return");
        }

        if (_state != State.Idle)
        {
            GameLog.Event("lap_prepare", "result", "already", "state", _state.ToString());
            return;
        }

        EnsureRefs();

        if (npc == null || driver == null)
        {
            GameLog.Event("lap_prepare", "result", "missing_refs");
            Debug.LogWarning("[LapPillow] 缺少 npc / driver 引用");
            return;
        }

        if (!useManualSeat && !TrySampleSitLoopHipOffset(out _hipOffset))
        {
            GameLog.Event("lap_prepare", "result", "no_sit_pose");
            Debug.LogWarning("[LapPillow] 无法采样 SitLoop 髋骨位置，膝枕不可用");
            return;
        }

        if (!TryComputeSeat(out _seat))
        {
            GameLog.Event("lap_prepare", "result", "no_bed");
            Debug.LogWarning("[LapPillow] 找不到床，膝枕不可用");
            return;
        }

        _state = State.Walking;
        GameLog.Event(
            "lap_prepare",
            "result", "walk",
            "seat", $"{_seat.x:F2},{_seat.y:F2},{_seat.z:F2}",
            "npc", $"{npc.transform.position.x:F2},{npc.transform.position.z:F2}");

        npc.WalkToWorld(_seat, "lap_seat", 0.02f);
    }

    /// <summary>说「起来」/ 任何 move：退出膝枕。</summary>
    public void Exit()
    {
        if (_state == State.Idle)
        {
            return;
        }

        if (_transition != null)
        {
            StopCoroutine(_transition);
            _transition = null;
        }

        if (cameraTransform != null)
        {
            cameraTransform.localPosition = _savedCamLocalPos;
            cameraTransform.localRotation = _savedCamLocalRot;
        }

        if (player != null)
        {
            player.enabled = true;
            player.allowMovement = true;
        }

        if (playerLook != null)
        {
            playerLook.enabled = true;
        }

        if (lookAt != null)
        {
            lookAt.SetTarget(null, false);
        }

        if (driver != null)
        {
            driver.SetSitting(false);
        }

        var was = _state;
        _state = State.Idle;
        GameLog.Event("lap_exit", "from", was.ToString());

        // 站起来之后把她放回床边地板（否则会在床沿上站着），再恢复 root motion
        _returnRoutine = StartCoroutine(ReturnToFloor());
    }

    IEnumerator ReturnToFloor()
    {
        yield return new WaitForSeconds(1.0f);
        _returnRoutine = null;

        if (_state != State.Idle)
        {
            // 已经又开始新的膝枕了，别再把人拽回地板
            GameLog.Event("lap_return", "skipped", _state.ToString());
            yield break;
        }

        if (_hasFloorSpot && npc != null)
        {
            npc.transform.position = _floorSpot;
            GameLog.Event(
                "lap_return",
                "at", $"{_floorSpot.x:F2},{_floorSpot.y:F2},{_floorSpot.z:F2}",
                "state", driver != null ? driver.CurrentStateName : "-");
        }

        RestoreAfterSeat();
    }

    void RestoreAfterSeat()
    {
        HideSkirt(false);
        SetSkirtLegCollisions(true);
        SetSkirtFoldLegConstraints(false);
        SetSkirtFoldLift(false);
        if (_tailRoot != null)
        {
            _tailRoot.localRotation = _tailInitialLocalRotation;
        }

        if (relay != null)
        {
            relay.relay = true;
        }
    }

    /// <summary>诊断用：坐下时隐藏裙子网格，确认"帐篷"是不是裙子本身（蒙皮/弹簧）而不是别的东西。</summary>
    void HideSkirt(bool hide)
    {
        if (!hideSkirtWhileSeated)
        {
            return;
        }

        var names = new List<string>();
        foreach (var r in npc.GetComponentsInChildren<Renderer>(true))
        {
            var n = r.gameObject.name.ToLowerInvariant();
            if (!(n.Contains("skirt") || n.Contains("dress") || n.Contains("onepiece") || n.Contains("clothes")))
            {
                continue;
            }

            r.enabled = !hide;
            names.Add(r.gameObject.name);
        }

        GameLog.Event("lap_skirt", "hidden", hide ? "1" : "0", "count", names.Count.ToString(),
            "names", string.Join(",", names.GetRange(0, System.Math.Min(names.Count, 4))));
    }
    /// <summary>唱一句（改自歌词占位）；后续接 AI 音色时替换成音频播放。</summary>
    public void PlayLyric(string text)
    {
        if (_state != State.InLap || dialogue == null || string.IsNullOrEmpty(text))
        {
            return;
        }

        StartCoroutine(LyricRoutine(text));
    }

    IEnumerator LyricRoutine(string text)
    {
        foreach (var line in text.Split('\n'))
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                dialogue.ShowLine(line.Trim());
                GameLog.Event("lyric", "line", line.Trim());
                yield return new WaitForSeconds(2.6f);
            }
        }
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.F5))
        {
            // 调试：现场翻转模型朝向，配合日志找出正确的 modelYawOffset
            modelYawOffset = Mathf.Repeat(modelYawOffset + 180f, 360f);
            if (_state != State.Idle && npc != null)
            {
                npc.transform.rotation = Quaternion.Euler(0f, SeatYaw - modelYawOffset, 0f);
            }

            GameLog.Event("lap_flip", "modelYawOffset", GameLog.F(modelYawOffset), "headFwd", HeadForward());
        }

        if (_state != State.Idle && Input.GetKeyDown(KeyCode.Escape))
        {
            GameLog.Event("lap_exit", "by", "escape");
            Exit();
            return;
        }

        switch (_state)
        {
            case State.Walking:
                if (!npc.IsMoving)
                {
                    BeginSit();
                }

                break;

            case State.Seated:
                if (player != null && Flat(player.transform.position, npc.transform.position) <= enterRadius)
                {
                    EnterLap();
                }

                break;

            case State.InLap:
                // 她低头看相机 + 相机保持在膝上（她若被 IK 带得轻微移动，镜头跟着）
                HoldCamera();
                break;
        }
    }

    void LateUpdate()
    {
        if (_state != State.Seated && _state != State.InLap)
        {
            return;
        }

        AlignUpperArmOutward(true);
        AlignUpperArmOutward(false);
        LiftForearm(true);
        LiftForearm(false);
        AlignHandToThigh(true);
        AlignHandToThigh(false);
        LiftTailForSeat();
    }

    void LiftTailForSeat()
    {
        if (_tailRoot == null)
        {
            foreach (var bone in npc.GetComponentsInChildren<Transform>(true))
            {
                if (bone.name == "DS_whale_tail_01")
                {
                    _tailRoot = bone;
                    _tailInitialLocalRotation = bone.localRotation;
                    break;
                }
            }
        }

        if (_tailRoot != null)
        {
            _tailRoot.rotation = Quaternion.AngleAxis(SeatedTailLiftDegrees, npc.transform.right) * _tailRoot.rotation;
        }
    }

    void AlignUpperArmOutward(bool left)
    {
        var animator = npc != null ? npc.GetComponentInChildren<Animator>() : null;
        if (animator == null || !animator.isHuman)
        {
            return;
        }

        var upperArm = animator.GetBoneTransform(left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
        var chest = animator.GetBoneTransform(HumanBodyBones.Chest);
        if (upperArm == null || chest == null)
        {
            return;
        }

        var side = Vector3.Dot(upperArm.position - chest.position, npc.transform.right) >= 0f ? 1f : -1f;
        upperArm.rotation = Quaternion.AngleAxis(
            side * SeatedArmOutwardDegrees,
            npc.transform.forward) * upperArm.rotation;
    }

    void LiftForearm(bool left)
    {
        var animator = npc != null ? npc.GetComponentInChildren<Animator>() : null;
        if (animator == null || !animator.isHuman)
        {
            return;
        }

        var lowerArm = animator.GetBoneTransform(left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
        var upperArm = animator.GetBoneTransform(left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
        var chest = animator.GetBoneTransform(HumanBodyBones.Chest);
        if (lowerArm != null)
        {
            var side = upperArm != null && chest != null
                ? (Vector3.Dot(upperArm.position - chest.position, npc.transform.right) >= 0f ? 1f : -1f)
                : 0f;
            lowerArm.position += Vector3.up * SeatedElbowLift
                + FacingDirection() * SeatedForearmForward
                + npc.transform.right * (side * SeatedArmSpread);
        }
    }

    void AlignHandToThigh(bool left)
    {
        var animator = npc != null ? npc.GetComponentInChildren<Animator>() : null;
        if (animator == null || !animator.isHuman)
        {
            return;
        }

        var hand = animator.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
        var finger = animator.GetBoneTransform(left ? HumanBodyBones.LeftMiddleProximal : HumanBodyBones.RightMiddleProximal);
        var upperLeg = animator.GetBoneTransform(left ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg);
        var knee = animator.GetBoneTransform(left ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg);
        if (hand == null || finger == null || upperLeg == null || knee == null)
        {
            return;
        }

        var fingers = finger.position - hand.position;
        var thigh = knee.position - upperLeg.position;
        if (fingers.sqrMagnitude < 0.000001f || thigh.sqrMagnitude < 0.000001f)
        {
            return;
        }

        hand.rotation = Quaternion.FromToRotation(fingers.normalized, thigh.normalized) * hand.rotation;
        hand.position += Vector3.up * SeatedHandLift;
    }

    void BeginSit()
    {
        // 记住传送前她站的地板位置，起身后回到这里（不然会站在床沿上）
        _floorSpot = npc.transform.position;
        _hasFloorSpot = true;

        // 贴到座位上（先让 root motion 关掉，坐着时不该被动画带着漂）
        npc.StopTurning();
        npc.transform.position = _seat;
        // 坐在床头左侧床沿，面朝床外；玩家从床内侧躺到她的大腿上。
        npc.transform.rotation = Quaternion.Euler(0f, SeatYaw - modelYawOffset, 0f);

        if (relay != null)
        {
            relay.relay = false;
        }

        HideSkirt(true);

        SetSkirtLegCollisions(true);
        SetSkirtFoldLegConstraints(true);
        SetSkirtFoldLift(true);

        driver.SetSitting(true);
        _state = State.Seated;
        GameLog.Event(
            "lap_seated",
            "at", $"{npc.transform.position.x:F2},{npc.transform.position.y:F2},{npc.transform.position.z:F2}",
            "yaw", GameLog.F(npc.transform.eulerAngles.y),
            "manual", useManualSeat ? "1" : "0",
            "headFwd", HeadForward(),
            "yawOffset", GameLog.F(modelYawOffset),
            "state", driver.CurrentStateName);

        if (!useManualSeat)
        {
            StartCoroutine(AlignSeatHeight());
        }
    }

    /// <summary>
    /// 座位高度已在 Prepare 时按 SitLoop 采样；这里只校验 Animator 实际播放后的剩余误差。
    /// </summary>
    IEnumerator AlignSeatHeight()
    {
        var animator = npc != null ? npc.GetComponentInChildren<Animator>() : null;
        var deadline = Time.time + 8f;
        while (driver != null && (driver.CurrentStateName != "SitLoop" || animator.IsInTransition(0)) && Time.time < deadline)
        {
            yield return null;
        }

        yield return null;
        yield return null;
        ApplySeatHeight("final");
    }

    /// <summary>
    /// 设置裙子弹簧链的腿部碰撞组并重建缓冲；坐姿需要保留碰撞来承托裙面。
    /// FastSpringBoneBufferFactory 不检查 collider.enabled，需要改碰撞组引用时不能只改开关。
    /// </summary>
    void SetSkirtLegCollisions(bool on)
    {
        if (_vrm == null)
        {
            _vrm = npc != null ? npc.GetComponentInChildren<Vrm10Instance>() : null;
        }

        if (_vrm == null)
        {
            return;
        }

        if (_legColliders.Count == 0)
        {
            foreach (var collider in npc.GetComponentsInChildren<VRM10SpringBoneCollider>(true))
            {
                var n = collider.gameObject.name.ToLowerInvariant();
                if (n.Contains("leg") || n.Contains("thigh") || n.Contains("shin")
                    || n.Contains("knee") || n.Contains("foot") || n.Contains("toe") || n.Contains("hips"))
                {
                    _legColliders.Add(collider);
                }
            }
        }

        if (_skirtSprings.Count == 0)
        {
            foreach (var spring in _vrm.SpringBone.Springs)
            {
                var isSkirt = spring.Name != null
                    && spring.Name.IndexOf("skirt", System.StringComparison.OrdinalIgnoreCase) >= 0;
                if (!isSkirt)
                {
                    continue;
                }

                _skirtSprings.Add(spring);
                _skirtColliderGroups.Add(new List<VRM10SpringBoneColliderGroup>(spring.ColliderGroups));
            }
        }

        if (_skirtSprings.Count == 0)
        {
            GameLog.Event("lap_spring", "result", "no_skirt_springs");
            return;
        }

        var groupCount = 0;
        for (var i = 0; i < _skirtSprings.Count; i++)
        {
            if (on)
            {
                _skirtSprings[i].ColliderGroups = new List<VRM10SpringBoneColliderGroup>(_skirtColliderGroups[i]);
            }
            else
            {
                _skirtSprings[i].ColliderGroups = _skirtColliderGroups[i]
                    .FindAll(group => group != null && !group.Colliders.Exists(_legColliders.Contains));
            }

            groupCount += _skirtSprings[i].ColliderGroups.Count;
        }

        var rebuilt = false;
        try
        {
            if (_vrm.Runtime?.SpringBone != null)
            {
                rebuilt = _vrm.Runtime.SpringBone.ReconstructSpringBone();
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[LapPillow] 重建弹簧失败：{e.Message}");
        }

        GameLog.Event(
            "lap_spring",
            "springs", _skirtSprings.Count.ToString(),
            "groups", groupCount.ToString(),
            "legColliders", _legColliders.Count.ToString(),
            "rebuilt", rebuilt ? "1" : "0");
    }
    void SetSkirtFoldLegConstraints(bool seated)
    {
        if (_skirtFoldConstraints.Count == 0)
        {
            foreach (var constraint in npc.GetComponentsInChildren<Vrm10RotationConstraint>(true))
            {
                if (constraint.gameObject.name.IndexOf("skirtFold", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _skirtFoldConstraints.Add(constraint);
                    _skirtFoldWeights.Add(constraint.Weight);
                }
            }
        }

        for (var i = 0; i < _skirtFoldConstraints.Count; i++)
        {
            _skirtFoldConstraints[i].Weight = seated ? SeatedSkirtFoldWeight : _skirtFoldWeights[i];
        }

        GameLog.Event(
            "lap_fold",
            "constraints", _skirtFoldConstraints.Count.ToString(),
            "weight", seated ? GameLog.F(SeatedSkirtFoldWeight) : "original");
    }

    void SetSkirtFoldLift(bool seated)
    {
        if (_skirtFoldRoots.Count == 0)
        {
            foreach (var constraint in _skirtFoldConstraints)
            {
                if (constraint.transform.parent != null
                    && constraint.transform.parent.name.Equals("hips", System.StringComparison.OrdinalIgnoreCase))
                {
                    _skirtFoldRoots.Add(constraint.transform);
                    _skirtFoldRootPositions.Add(constraint.transform.localPosition);
                }
            }
        }

        for (var i = 0; i < _skirtFoldRoots.Count; i++)
        {
            var root = _skirtFoldRoots[i];
            var lift = seated && root.parent != null
                ? root.parent.InverseTransformDirection(Vector3.up * SeatedSkirtLift)
                : Vector3.zero;
            root.localPosition = _skirtFoldRootPositions[i] + lift;
        }

        GameLog.Event("lap_fold_lift", "roots", _skirtFoldRoots.Count.ToString(), "meters", seated ? GameLog.F(SeatedSkirtLift) : "0");
    }

    bool TrySampleSitLoopHipOffset(out float hipOffset)
    {
        hipOffset = float.NaN;
        var animator = npc != null ? npc.GetComponentInChildren<Animator>() : null;
        var hips = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Hips) : null;
        var controller = animator != null ? animator.runtimeAnimatorController : null;
        if (hips == null || controller == null)
        {
            return false;
        }

        AnimationClip sitLoop = null;
        foreach (var clip in controller.animationClips)
        {
            if (clip != null && clip.name.IndexOf("Sit_LookingAround", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                sitLoop = clip;
                break;
            }
        }

        if (sitLoop == null)
        {
            return false;
        }

        var visual = animator.transform;
        var localPosition = visual.localPosition;
        var localRotation = visual.localRotation;
        var localScale = visual.localScale;
        try
        {
            sitLoop.SampleAnimation(animator.gameObject, 0f);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[LapPillow] 采样 SitLoop 失败：{e.Message}");
            return false;
        }
        finally
        {
            visual.localPosition = localPosition;
            visual.localRotation = localRotation;
            visual.localScale = localScale;
        }

        hipOffset = hips.position.y - npc.transform.position.y;
        GameLog.Event("lap_seat_sample", "clip", sitLoop.name, "hipOffset", GameLog.F(hipOffset));
        return !float.IsNaN(hipOffset) && !float.IsInfinity(hipOffset);
    }

    /// <summary>量髋骨、把根节点对到"床面 + hipsSitOffset"，并缓存髋高供下次直接摆放。</summary>
    void ApplySeatHeight(string phase)
    {
        var animator = npc != null ? npc.GetComponentInChildren<Animator>() : null;
        var hips = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Hips) : null;
        if (hips == null)
        {
            GameLog.Event("lap_seat_align", "phase", phase, "result", "no_hips");
            return;
        }

        var target = _surfaceY + hipsSitOffset;
        var delta = target - hips.position.y;
        if (Mathf.Abs(delta) > 0.005f)
        {
            npc.transform.position += Vector3.up * delta;
        }

        _seat = npc.transform.position;
        _hipOffset = hips.position.y - npc.transform.position.y;

        GameLog.Event(
            "lap_seat_align",
            "phase", phase,
            "hipsY", GameLog.F(hips.position.y),
            "target", GameLog.F(target),
            "delta", GameLog.F(delta),
            "seatY", GameLog.F(npc.transform.position.y),
            "hipOffset", GameLog.F(_hipOffset),
            "state", driver != null ? driver.CurrentStateName : "-");
    }
    void EnterLap()
    {
        if (cameraTransform == null || player == null)
        {
            return;
        }

        _savedCamLocalPos = cameraTransform.localPosition;
        _savedCamLocalRot = cameraTransform.localRotation;

        player.allowMovement = false;
        player.enabled = false;
        if (playerLook != null)
        {
            playerLook.enabled = false;
        }

        _state = State.InLap;
        _lapPoint = ComputeLapPoint();

        if (_transition != null)
        {
            StopCoroutine(_transition);
        }

        _transition = StartCoroutine(TransitionTo(ComputeLapCameraPose()));

        if (lookAt != null)
        {
            lookAt.SetTarget(cameraTransform, true);
        }

        var camPos = ComputeLapCameraPose();
        var headPos = HeadPosition();
        var toHead = headPos - camPos;
        var pitch = Mathf.Atan2(toHead.y, new Vector2(toHead.x, toHead.z).magnitude) * Mathf.Rad2Deg;

        GameLog.Event(
            "lap_enter",
            "lap", $"{_lapPoint.x:F2},{_lapPoint.y:F2},{_lapPoint.z:F2}",
            "cam", $"{camPos.x:F2},{camPos.y:F2},{camPos.z:F2}",
            "head", $"{headPos.x:F2},{headPos.y:F2},{headPos.z:F2}",
            "camToHead", GameLog.F(toHead.magnitude),
            "pitch", GameLog.F(pitch),
            "headViewport", ViewportOf(headPos),
            "faceDot", GameLog.F(Vector3.Dot(FacingDirection(), (camPos - headPos).normalized)),
            "player", $"{player.transform.position.x:F2},{player.transform.position.z:F2}",
            "dist", GameLog.F(Flat(player.transform.position, npc.transform.position)));
    }

    IEnumerator TransitionTo(Vector3 targetPos)
    {
        var fromPos = cameraTransform.position;
        var fromRot = cameraTransform.rotation;
        var elapsed = 0f;
        var duration = Mathf.Max(0.05f, transitionSeconds);

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            var t = Mathf.Clamp01(elapsed / duration);
            var smooth = t * t * (3f - 2f * t);
            cameraTransform.position = Vector3.Lerp(fromPos, targetPos, smooth);
            var lookTarget = HeadPosition() - cameraTransform.position;
            if (lookTarget.sqrMagnitude > 0.0001f)
            {
                cameraTransform.rotation = Quaternion.Slerp(fromRot, LapCameraRotation(cameraTransform.position), smooth);
            }

            yield return null;
        }

        _transition = null;
    }

    void HoldCamera()
    {
        if (cameraTransform == null)
        {
            return;
        }

        var targetPos = ComputeLapCameraPose();
        cameraTransform.position = Vector3.Lerp(cameraTransform.position, targetPos, Time.deltaTime * 6f);
        cameraTransform.rotation = Quaternion.Slerp(
            cameraTransform.rotation,
            LapCameraRotation(cameraTransform.position),
            Time.deltaTime * 6f);
    }

    /// <summary>
    /// 膝枕构图：不是把她摆正中，而是"歪在一边"。
    /// 真实姿势里后脑枕在一侧大腿上，抬头看时她的人在斜上方、另一半画面是天花板。
    /// 这里在世界朝向下叠加 yaw/pitch 偏移：yaw 正 = 视线右转 → 她的脸落到画面左侧。
    /// </summary>
    Quaternion LapCameraRotation(Vector3 camPos)
    {
        var toHead = HeadPosition() - camPos;
        if (toHead.sqrMagnitude < 0.0001f)
        {
            return cameraTransform != null ? cameraTransform.rotation : Quaternion.identity;
        }

        var baseRot = Quaternion.LookRotation(toHead, Vector3.up);
        return baseRot * Quaternion.Euler(-viewPitchOffset, viewYawOffset, 0f);
    }

    /// <summary>头部在画面里的归一化坐标（0~1）。x<0.5 表示在左半边，y<0.5 表示偏下。</summary>
    string ViewportOf(Vector3 world)
    {
        var cam = cameraTransform != null ? cameraTransform.GetComponent<Camera>() : null;
        if (cam == null)
        {
            return "-";
        }

        var v = cam.WorldToViewportPoint(world);
        return $"{v.x:F2},{v.y:F2},{(v.z > 0f ? "front" : "behind")}";
    }
    Vector3 HipsPosition()
    {
        var animator = npc != null ? npc.GetComponentInChildren<Animator>() : null;
        if (animator != null && animator.isHuman)
        {
            var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            if (hips != null)
            {
                return hips.position;
            }
        }

        return npc != null ? npc.transform.position + Vector3.up * 0.75f : Vector3.zero;
    }

    /// <summary>头骨的世界朝向（x,z）。用来判定"她脸朝哪"，不受根节点/动画旋转的干扰。</summary>
    string HeadForward()
    {
        var animator = npc != null ? npc.GetComponentInChildren<Animator>() : null;
        if (animator == null || !animator.isHuman)
        {
            return "-";
        }

        var head = animator.GetBoneTransform(HumanBodyBones.Head);
        if (head == null)
        {
            return "-";
        }

        var f = head.forward;
        return $"{f.x:F2},{f.z:F2}";
    }
    /// <summary>她真正面向的方向（模型正面），不是根节点 forward。</summary>
    Vector3 FacingDirection()
    {
        var f = npc.transform.forward;
        f = Quaternion.Euler(0f, modelYawOffset, 0f) * f;
        f.y = 0f;
        return f.sqrMagnitude > 0.0001f ? f.normalized : Vector3.forward;
    }

    Vector3 ComputeLapPoint()
    {
        var animator = npc != null ? npc.GetComponentInChildren<Animator>() : null;
        if (animator != null && animator.isHuman)
        {
            var leftUpper = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
            var rightUpper = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            var leftLower = animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
            var rightLower = animator.GetBoneTransform(HumanBodyBones.RightLowerLeg);
            if (leftUpper != null && rightUpper != null && leftLower != null && rightLower != null)
            {
                var leftThigh = (leftUpper.position + leftLower.position) * 0.5f;
                var rightThigh = (rightUpper.position + rightLower.position) * 0.5f;
                return (leftThigh + rightThigh) * 0.5f + Vector3.up * lapHeight;
            }
        }

        return HipsPosition() + FacingDirection() * lapForward + Vector3.up * lapHeight;
    }

    Vector3 ComputeLapCameraPose()
    {
        _lapPoint = ComputeLapPoint();
        return _lapPoint
            + Vector3.up * cameraLift
            + FacingDirection() * CameraForwardOffset
            + npc.transform.right * CameraSideOffset;
    }

    Vector3 HeadPosition()
    {
        var animator = npc != null ? npc.GetComponentInChildren<Animator>() : null;
        if (animator != null && animator.isHuman)
        {
            var head = animator.GetBoneTransform(HumanBodyBones.Head);
            if (head != null)
            {
                // 瞄脸而不是头顶：从大腿处仰视时，头顶会让画面全是下巴/额头
                return head.position - Vector3.up * 0.10f;
            }
        }

        return npc.transform.position + Vector3.up * 1.3f;
    }

    /// <summary>按床的包围盒算座位：床头左侧床沿，从床头往床尾退 seatInsetFromHead。</summary>
    bool TryComputeSeat(out Vector3 seat)
    {
        seat = default;

        if (useManualSeat)
        {
            _surfaceY = manualSeat.y - 0.75f;   // 仅用于日志，手动模式不走校准
            seat = manualSeat;
            return true;
        }

        var headboard = (Bounds?)null;
        var mattress = (Bounds?)null;
        var mattressVolume = 0f;

        foreach (var r in Object.FindObjectsOfType<Renderer>())
        {
            if (!r.enabled || !r.gameObject.activeInHierarchy)
            {
                continue;
            }

            var n = r.gameObject.name.ToLowerInvariant();
            if (!n.Contains("bed_bedroom"))
            {
                continue;
            }

            var b = r.bounds;
            if (n.Contains("base"))
            {
                headboard = b;
            }

            if (n.Contains("mattress") || n.Contains("cover"))
            {
                var v = b.size.x * b.size.z;
                if (v > mattressVolume)
                {
                    mattressVolume = v;
                    mattress = b;
                }
            }
        }

        if (!headboard.HasValue && !mattress.HasValue)
        {
            return false;
        }

        var bed = mattress ?? headboard.Value;
        var headX = headboard.HasValue ? headboard.Value.max.x : bed.max.x;
        var surfaceY = mattress.HasValue ? mattress.Value.max.y : bed.max.y;

        var x = headX - seatInsetFromHead;
        var lengthBounds = headboard ?? bed;
        x = Mathf.Clamp(x, lengthBounds.min.x + 0.45f, lengthBounds.max.x - 0.45f);
        var z = bed.max.z - SeatSideInset;

        _surfaceY = surfaceY;
        var seatY = surfaceY + hipsSitOffset - _hipOffset;
        seat = new Vector3(x, seatY, z);
        return true;
    }

    static float Flat(Vector3 a, Vector3 b)
    {
        var d = a - b;
        d.y = 0f;
        return d.magnitude;
    }
}
