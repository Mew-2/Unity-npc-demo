using System.Collections;
using System.Collections.Generic;
using UniVRM10;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 膝枕（POV）：说「想要膝枕」→ 她走到床头坐下（Sit_Start → Sit_Loop 等你）
/// → 玩家走近 → 相机降到她膝上仰视、她低头看你 → 说「起来」退出。
///
/// 座位是运行时按床的包围盒算的，不写死坐标——床的尺寸/朝向改了她会跟着变。
/// 全程写 GameLog：lap_prepare / lap_seated / lap_enter / lap_exit，便于按日志调几何。
/// </summary>
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

    [Tooltip("模型正面相对根节点 forward 的偏航（VRM 常见 180：模型朝 -Z，根节点 forward 是 +Z）")]
    public float modelYawOffset;   // 实测这台 VRM 是 0（正面 = +Z）；F5 可现场翻转对照

    [Tooltip("诊断用：坐下时隐藏裙子/连衣裙网格，确认鼓包是不是裙子本身")]
    public bool hideSkirtWhileSeated;

    [Tooltip("勾上则用下面的手写座位（Play 里直接拖 Npc 到满意位置，抄 Inspector 的坐标进来）")]
    public bool useManualSeat;

    [Tooltip("手写座位（世界坐标，根节点位置）")]
    public Vector3 manualSeat = new Vector3(3.42f, 0.85f, 1.95f);

    [Tooltip("坐好后髋骨应落在床面上方多少米（自校准的目标值）")]
    public float hipsSitOffset = 0.06f;

    [Tooltip("她的膝上点：沿她面朝方向往前多少米")]
    public float lapForward = 0.62f;

    [Tooltip("她的膝上点：相对她脚下多高")]
    public float lapHeight = 0.04f;

    [Tooltip("相机在膝上点上方多少米")]
    public float cameraLift = 0.06f;

    [Header("构图（非对称）")]
    [Tooltip("视线向右偏多少度：正值 = 画面右转 → 她的人偏到画面左侧，另一边是天花板")]
    public float viewYawOffset = 26f;

    [Tooltip("视线再抬高多少度：正值 = 更仰，天花板占更多画面")]
    public float viewPitchOffset = 6f;

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
    float _hipOffset = -0.09f;   // 髋骨相对根节点的偏移：先用实测过的估值（上次 0.76-0.85），坐下后再精确校一次并更新
    Vector3 _floorSpot;             // 传送上床边之前她站的位置（起身后回到这里）
    bool _hasFloorSpot;
    Vector3 _lapPoint;
    Coroutine _transition;
    Coroutine _returnRoutine;

    Vrm10Instance _vrm;
    readonly List<VRM10SpringBoneCollider> _legColliders = new List<VRM10SpringBoneCollider>();
    readonly List<bool> _legColliderStates = new List<bool>();

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
            // 上一次起身的"回地板"还在排队，必须取消：否则它会在这次坐下之后把人拽回地板
            StopCoroutine(_returnRoutine);
            _returnRoutine = null;
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

        HideSkirt(false);

        SetLegSpringColliders(true);

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
                npc.transform.rotation = Quaternion.Euler(0f, -modelYawOffset, 0f);
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

    void BeginSit()
    {
        // 记住传送前她站的地板位置，起身后回到这里（不然会站在床沿上）
        _floorSpot = npc.transform.position;
        _hasFloorSpot = true;

        // 贴到座位上（先让 root motion 关掉，坐着时不该被动画带着漂）
        npc.transform.position = _seat;
        // 面朝窗户（+Z）。注意模型正面不一定等于根节点 forward：
        // 实测：modelYawOffset = 0 时她背对镜头、面朝窗户（F5 翻转验证过）。
        npc.transform.rotation = Quaternion.Euler(0f, -modelYawOffset, 0f);

        if (relay != null)
        {
            relay.relay = false;
        }

        HideSkirt(true);

        SetLegSpringColliders(false);

        driver.SetSitting(true);
        _state = State.Seated;
        GameLog.Event(
            "lap_seated",
            "at", $"{npc.transform.position.x:F2},{npc.transform.position.y:F2},{npc.transform.position.z:F2}",
            "yaw", GameLog.F(npc.transform.eulerAngles.y),
            "manual", useManualSeat ? "1" : "0",
            "headFwd", HeadForward(),
            "state", driver.CurrentStateName);

        if (!useManualSeat)
        {
            StartCoroutine(AlignSeatHeight());
        }
    }

    /// <summary>
    /// 用髋骨实测把座位高度对齐到床面：不再靠"髋高 0.2 还是 0.25"这种常数猜。
    /// 坐下后等 SitLoop 生效，读 Hips 世界 Y，把根节点整体上移/下移到 床面 + hipsSitOffset。
    /// </summary>
    IEnumerator AlignSeatHeight()
    {
        // 第一遍：刚坐下就按估值对一次，避免"坐在半空再掉下来"
        yield return null;
        yield return null;
        ApplySeatHeight("quick");

        // 第二遍：等 SitLoop 稳定后精确校一次（换片段时也自动适配），并把髋高缓存下来
        var deadline = Time.time + 8f;
        while (driver != null && driver.CurrentStateName != "SitLoop" && Time.time < deadline)
        {
            yield return null;
        }

        yield return null;
        yield return null;
        ApplySeatHeight("final");
    }

    /// <summary>
    /// 坐下时把"腿上的 SpringBone 碰撞体"关掉并重建弹簧缓冲。
    /// 裙子是 spring bone 链，腿折叠后这些碰撞体跑到裙子内部，会把布往外顶——
    /// 表现为坐下 1~2 秒后裙子鼓成一个帐篷。关掉腿部碰撞体，裙子只受重力自然垂落。
    /// </summary>
    void SetLegSpringColliders(bool on)
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
                    _legColliderStates.Add(collider.enabled);
                }
            }
        }

        if (_legColliders.Count == 0)
        {
            GameLog.Event("lap_spring", "result", "no_leg_colliders");
            return;
        }

        for (var i = 0; i < _legColliders.Count; i++)
        {
            if (_legColliders[i] != null)
            {
                _legColliders[i].enabled = on ? _legColliderStates[i] : false;
            }
        }

        try
        {
            _vrm.Runtime?.SpringBone?.ReconstructSpringBone();
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[LapPillow] 重建弹簧失败：{e.Message}");
        }

        GameLog.Event("lap_spring", "colliders", _legColliders.Count.ToString(), "enabled", on ? "1" : "0");
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
        // 关键：以髋骨为基准，不是根节点。坐姿下根节点比髋骨高 ~0.4m，
        // 用根节点会把"大腿点"算到头顶上去（实测相机因此跑到她头上方俯视）。
        return HipsPosition() + FacingDirection() * lapForward + Vector3.up * lapHeight;
    }

    Vector3 ComputeLapCameraPose()
    {
        _lapPoint = ComputeLapPoint();
        return _lapPoint + Vector3.up * cameraLift;
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

    /// <summary>按床的包围盒算座位：床头往床尾退 seatInsetFromHead，坐在床面上，面朝床尾。</summary>
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

        // 以床垫为准：床头的包围盒（Base）经常比床垫长，直接用它会把人放到床沿外面（实测 seat z=2.49，床垫只到 2.06）
        var bed = mattress ?? headboard.Value;
        var headZ = mattress.HasValue ? mattress.Value.max.z : bed.max.z;
        var surfaceY = mattress.HasValue ? mattress.Value.max.y : bed.max.y;

        var z = headZ - seatInsetFromHead;
        z = Mathf.Clamp(z, bed.min.z + 0.45f, bed.max.z - 0.45f);

        // 根节点约定是"地面 + groundOffset"（胶囊中心），不是脚底：
        // 站立时 anchor.y + 1.0 才是根节点高度。先按"床面当地面"给初值，
        // 真正的贴合由 BeginSit 之后的 AlignSeatHeight() 用髋骨实测校准。
        _surfaceY = surfaceY;
        float seatY;
        if (!float.IsNaN(_hipOffset))
        {
            // 已知坐姿髋高：直接把根节点放到"髋骨 = 床面 + hipsSitOffset"的高度，不再落地后才补
            seatY = surfaceY + hipsSitOffset - _hipOffset;
        }
        else
        {
            var rootOffset = npc != null ? npc.groundOffset : 1f;
            seatY = surfaceY + rootOffset;
        }

        seat = new Vector3(bed.center.x, seatY, z);
        return true;
    }

    static float Flat(Vector3 a, Vector3 b)
    {
        var d = a - b;
        d.y = 0f;
        return d.magnitude;
    }
}
