using System.Collections;
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
    public float enterRadius = 1.1f;

    [Header("座位（相对床）")]
    [Tooltip("从床头往床尾退多少米坐下")]
    public float seatInsetFromHead = 0.62f;

    [Tooltip("她的膝上点：沿她面朝方向往前多少米")]
    public float lapForward = 0.42f;

    [Tooltip("她的膝上点：相对她脚下多高")]
    public float lapHeight = 0.20f;

    [Tooltip("相机在膝上点上方多少米")]
    public float cameraLift = 0.12f;

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
    Vector3 _lapPoint;
    Coroutine _transition;

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
        if (_state != State.Idle)
        {
            GameLog.Event("lap_prepare", "result", "already");
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

        if (relay != null)
        {
            relay.relay = true;
        }

        var was = _state;
        _state = State.Idle;
        GameLog.Event("lap_exit", "from", was.ToString());
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
                dialogue.ShowLine($"鲸鲸：{line.Trim()}");
                GameLog.Event("lyric", "line", line.Trim());
                yield return new WaitForSeconds(2.6f);
            }
        }
    }

    void Update()
    {
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
        // 贴到座位上并朝向床尾（先让 root motion 关掉，坐着时不该被动画带着漂）
        npc.transform.position = _seat;
        var forward = Quaternion.LookRotation(Vector3.back, Vector3.up); // 面朝 -Z（床尾）
        npc.transform.rotation = forward;

        if (relay != null)
        {
            relay.relay = false;
        }

        driver.SetSitting(true);
        _state = State.Seated;
        GameLog.Event(
            "lap_seated",
            "at", $"{npc.transform.position.x:F2},{npc.transform.position.y:F2},{npc.transform.position.z:F2}",
            "state", driver.CurrentStateName);
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

        GameLog.Event(
            "lap_enter",
            "lap", $"{_lapPoint.x:F2},{_lapPoint.y:F2},{_lapPoint.z:F2}",
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
                cameraTransform.rotation = Quaternion.Slerp(fromRot, Quaternion.LookRotation(lookTarget, Vector3.up), smooth);
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

        var toHead = HeadPosition() - cameraTransform.position;
        if (toHead.sqrMagnitude > 0.0001f)
        {
            cameraTransform.rotation = Quaternion.Slerp(
                cameraTransform.rotation,
                Quaternion.LookRotation(toHead, Vector3.up),
                Time.deltaTime * 6f);
        }
    }

    Vector3 ComputeLapPoint()
    {
        var forward = npc.transform.forward;
        forward.y = 0f;
        return npc.transform.position + forward.normalized * lapForward + Vector3.up * lapHeight;
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
                return head.position + Vector3.up * 0.05f;
            }
        }

        return npc.transform.position + Vector3.up * 1.3f;
    }

    /// <summary>按床的包围盒算座位：床头往床尾退 seatInsetFromHead，坐在床面上，面朝床尾。</summary>
    bool TryComputeSeat(out Vector3 seat)
    {
        seat = default;

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
        var headZ = headboard.HasValue ? headboard.Value.max.z : bed.max.z;
        var surfaceY = mattress.HasValue ? mattress.Value.max.y : bed.max.y;

        var z = headZ - seatInsetFromHead;
        z = Mathf.Clamp(z, bed.min.z + 0.1f, bed.max.z - 0.1f);

        seat = new Vector3(bed.center.x, surfaceY + 0.02f, z);
        return true;
    }

    static float Flat(Vector3 a, Vector3 b)
    {
        var d = a - b;
        d.y = 0f;
        return d.magnitude;
    }
}
