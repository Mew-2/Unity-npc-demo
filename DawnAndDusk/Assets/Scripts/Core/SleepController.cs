using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(11100)]
public class SleepController : MonoBehaviour
{
    public NpcAgent npc;
    public NpcAnimationDriver driver;
    public RootMotionRelay relay;
    public PlayerController player;
    public PlayerLook playerLook;
    public Transform cameraTransform;

    [Tooltip("躺下后髋骨应高于床面多少米")]
    public float hipsSleepOffset = 0.14f;

    [Tooltip("陪睡相机离头部多少米")]
    public float cameraDistance = 0.85f;

    public float cameraLift = 0.24f;
    public float transitionSeconds = 1.2f;

    enum State
    {
        Idle,
        Walking,
        Preparing,
        InSleep,
    }

    State _state;
    Vector3 _sleepPosition;
    Quaternion _sleepRotation;
    Vector3 _floorSpot;
    Vector3 _bedCenter;
    float _surfaceY;
    float _hipOffset;
    Bounds _bedBounds;
    Vector3 _savedCamLocalPos;
    Quaternion _savedCamLocalRot;
    Coroutine _enterRoutine;
    Coroutine _cameraRoutine;
    Coroutine _returnRoutine;
    Transform _tailRoot;
    Quaternion _tailInitialLocalRotation;
    readonly List<Transform> _tailBones = new List<Transform>();

    const float SleepRootYawOffset = 180f;
    const float SleepArmBackOffset = 0.25f;
    const float SleepArmDownOffset = 0.12f;

    public bool IsActive => _state != State.Idle;
    public bool IsInSleep => _state == State.InSleep;

    void Awake()
    {
        EnsureRefs();
    }

    void EnsureRefs()
    {
        if (npc == null)
        {
            return;
        }

        if (driver == null)
        {
            driver = npc.GetComponentInChildren<NpcAnimationDriver>();
        }

        var animator = npc.GetComponentInChildren<Animator>();
        if (relay == null && animator != null)
        {
            relay = animator.GetComponent<RootMotionRelay>();
        }
    }

    public void Prepare()
    {
        if (_returnRoutine != null)
        {
            StopCoroutine(_returnRoutine);
            _returnRoutine = null;
            if (relay != null)
            {
                relay.relay = true;
            }
        }

        if (_state != State.Idle)
        {
            GameLog.Event("sleep_prepare", "result", "already", "state", _state.ToString());
            return;
        }

        EnsureRefs();
        if (npc == null || driver == null || !TryComputeSleepPose())
        {
            GameLog.Event("sleep_prepare", "result", "unavailable");
            Debug.LogWarning("[Sleep] 缺少角色、睡姿动画或床，陪睡不可用");
            return;
        }

        _state = State.Walking;
        GameLog.Event(
            "sleep_prepare", "result", "walk",
            "pose", $"{_sleepPosition.x:F2},{_sleepPosition.y:F2},{_sleepPosition.z:F2}",
            "yaw", GameLog.F(_sleepRotation.eulerAngles.y));
        npc.WalkToWorld(_sleepPosition, "sleep_bed", 0.02f);
    }

    void Update()
    {
        if (_state == State.Walking && !npc.IsMoving)
        {
            BeginSleep();
        }
    }

    void LateUpdate()
    {
        if (_state == State.Preparing || _state == State.InSleep)
        {
            LayTailForSleep();
            ClampTailInsideBed();
            RestLeftArmForSleep();
        }
        if (_state == State.InSleep && _cameraRoutine == null && cameraTransform != null)
        {
            HoldCamera();
        }
    }

    void LayTailForSleep()
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

        var animator = npc.GetComponentInChildren<Animator>();
        var head = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
        var hips = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Hips) : null;
        if (_tailRoot == null || head == null)
        {
            return;
        }

        if (_tailRoot.childCount == 0)
        {
            return;
        }

        // 尾巴应朝床尾（头→髋的反方向）越过脚侧床沿，而不是朝窗/墙那侧。
        var outward = hips != null ? hips.position - head.position : head.forward;
        outward.y = 0f;
        if (outward.sqrMagnitude < 0.0001f)
        {
            outward = -head.forward;
            outward.y = 0f;
        }

        var segment = _tailRoot.GetChild(0).position - _tailRoot.position;
        // 先朝床沿外、略向下；床面内的段落由 ClampTailInsideBed 抬住，床沿外自然下垂。
        var desired = outward.normalized * 0.95f + Vector3.down * 0.2f;
        if (segment.sqrMagnitude > 0.0001f && desired.sqrMagnitude > 0.0001f)
        {
            _tailRoot.rotation = Quaternion.FromToRotation(segment.normalized, desired.normalized) * _tailRoot.rotation;
        }

        StraightenTailForSleep(outward);
    }

    void StraightenTailForSleep(Vector3 outward)
    {
        if (_tailRoot == null)
        {
            return;
        }

        if (_tailBones.Count == 0)
        {
            foreach (var bone in _tailRoot.GetComponentsInChildren<Transform>(true))
            {
                if (bone.name.StartsWith("DS_whale_tail", System.StringComparison.Ordinal))
                {
                    _tailBones.Add(bone);
                }
            }
        }

        if (_tailBones.Count < 2)
        {
            return;
        }

        var dir = outward;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f)
        {
            return;
        }

        dir.Normalize();
        for (var i = 1; i < _tailBones.Count; i++)
        {
            var bone = _tailBones[i];
            if (bone == null || bone.childCount == 0)
            {
                continue;
            }

            var child = bone.GetChild(0);
            var segment = child.position - bone.position;
            if (segment.sqrMagnitude < 0.0001f)
            {
                continue;
            }

            // 越靠尾部越向下，形成自然下垂；床面内的部分会被 ClampTailInsideBed 抬住
            var droop = 0.15f * i / Mathf.Max(1, _tailBones.Count - 1);
            var targetDir = (dir + Vector3.down * droop).normalized;
            bone.rotation = Quaternion.FromToRotation(segment.normalized, targetDir) * bone.rotation;
        }
    }

    void ClampTailInsideBed()
    {
        if (_tailBones.Count == 0 && _tailRoot != null)
        {
            foreach (var bone in _tailRoot.GetComponentsInChildren<Transform>(true))
            {
                if (bone.name.StartsWith("DS_whale_tail", System.StringComparison.Ordinal))
                {
                    _tailBones.Add(bone);
                }
            }
        }

        if (_bedBounds.size.sqrMagnitude <= 0f || _tailBones.Count == 0)
        {
            return;
        }

        const float clearance = 0.035f;
        var surface = _bedBounds.max.y + clearance;
        foreach (var bone in _tailBones)
        {
            var p = bone.position;
            var insideXZ = p.x > _bedBounds.min.x && p.x < _bedBounds.max.x
                        && p.z > _bedBounds.min.z && p.z < _bedBounds.max.z;
            if (insideXZ && p.y < surface)
            {
                bone.position = new Vector3(p.x, surface, p.z);
            }
        }
    }

    void RestLeftArmForSleep()
    {
        var animator = npc.GetComponentInChildren<Animator>();
        var head = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
        var hips = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Hips) : null;
        var upperArm = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.LeftUpperArm) : null;
        var lowerArm = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.LeftLowerArm) : null;
        var hand = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.LeftHand) : null;
        if (head == null || hips == null)
        {
            return;
        }

        var towardHips = hips.position - head.position;
        towardHips.y = 0f;
        var offset = towardHips.normalized * SleepArmBackOffset + Vector3.down * SleepArmDownOffset;
        if (upperArm != null)
        {
            upperArm.position += offset;
        }
        if (lowerArm != null)
        {
            lowerArm.position += offset;
        }
        if (hand != null)
        {
            hand.position += offset;
        }
    }

    void BeginSleep()
    {
        _floorSpot = npc.transform.position;
        npc.StopTurning();
        npc.transform.position = _sleepPosition;
        npc.transform.rotation = _sleepRotation;
        if (relay != null)
        {
            relay.relay = false;
        }

        driver.SetSleeping(true);
        _state = State.Preparing;
        GameLog.Event("sleep_begin", "state", driver.CurrentStateName);
        _enterRoutine = StartCoroutine(WaitForSleepLoop());
    }

    IEnumerator WaitForSleepLoop()
    {
        var animator = npc.GetComponentInChildren<Animator>();
        var deadline = Time.time + 20f;
        while (_state == State.Preparing
            && (driver.CurrentStateName != "SleepLoop" || animator.IsInTransition(0))
            && Time.time < deadline)
        {
            yield return null;
        }

        if (_state != State.Preparing || driver.CurrentStateName != "SleepLoop")
        {
            GameLog.Event("sleep_loop", "result", "timeout", "state", driver.CurrentStateName);
            _enterRoutine = null;
            yield break;
        }

        yield return null;
        ApplySleepHeight();
        GameLog.Event("sleep_loop", "result", "ready");
        EnterSleep();
        _enterRoutine = null;
    }

    void EnterSleep()
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

        _state = State.InSleep;
        ComputeCameraPose(out var position, out var rotation);
        _cameraRoutine = StartCoroutine(MoveCamera(position, rotation));
        GameLog.Event("sleep_enter");
    }

    IEnumerator MoveCamera(Vector3 targetPos, Quaternion targetRot)
    {
        var startPos = cameraTransform.position;
        var startRot = cameraTransform.rotation;
        var elapsed = 0f;
        while (elapsed < transitionSeconds && _state == State.InSleep)
        {
            elapsed += Time.deltaTime;
            var t = Mathf.SmoothStep(0f, 1f, elapsed / transitionSeconds);
            cameraTransform.position = Vector3.Lerp(startPos, targetPos, t);
            cameraTransform.rotation = Quaternion.Slerp(startRot, targetRot, t);
            yield return null;
        }

        _cameraRoutine = null;
    }

    void HoldCamera()
    {
        ComputeCameraPose(out var position, out var rotation);
        cameraTransform.position = Vector3.Lerp(cameraTransform.position, position, Time.deltaTime * 6f);
        cameraTransform.rotation = Quaternion.Slerp(cameraTransform.rotation, rotation, Time.deltaTime * 6f);
    }

    void ComputeCameraPose(out Vector3 position, out Quaternion rotation)
    {
        var animator = npc.GetComponentInChildren<Animator>();
        var head = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
        var target = head != null ? head.position : npc.transform.position + Vector3.up * 0.4f;
        var playerSide = head != null ? -head.forward : -npc.transform.forward;
        playerSide.y = 0f;
        if (playerSide.sqrMagnitude < 0.0001f)
        {
            playerSide = _bedCenter - target;
            playerSide.y = 0f;
        }

        position = target + playerSide.normalized * cameraDistance + Vector3.up * cameraLift;
        position.x = (position.x + target.x) * 0.5f;
        rotation = Quaternion.LookRotation(target + Vector3.up * 0.04f - position, Vector3.up);
    }

    public void Exit(bool immediate = false)
    {
        if (_state == State.Idle)
        {
            return;
        }

        if (_enterRoutine != null)
        {
            StopCoroutine(_enterRoutine);
            _enterRoutine = null;
        }
        if (_cameraRoutine != null)
        {
            StopCoroutine(_cameraRoutine);
            _cameraRoutine = null;
        }
        if (_returnRoutine != null)
        {
            StopCoroutine(_returnRoutine);
            _returnRoutine = null;
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

        if (_tailRoot != null)
        {
            _tailRoot.localRotation = _tailInitialLocalRotation;
        }
        driver.SetSleeping(false);
        GameLog.Event("sleep_exit", "immediate", immediate ? "1" : "0");
        _state = State.Idle;
        if (immediate)
        {
            npc.transform.position = _floorSpot;
            if (relay != null)
            {
                relay.relay = true;
            }
            return;
        }

        _returnRoutine = StartCoroutine(ReturnToFloor());
    }

    IEnumerator ReturnToFloor()
    {
        var animator = npc.GetComponentInChildren<Animator>();
        var deadline = Time.time + 10f;
        while ((driver.CurrentStateName != "Idle" || animator.IsInTransition(0)) && Time.time < deadline)
        {
            yield return null;
        }

        npc.transform.position = _floorSpot;
        if (relay != null)
        {
            relay.relay = true;
        }
        GameLog.Event("sleep_return", "state", driver.CurrentStateName);
        _returnRoutine = null;
    }

    bool TryComputeSleepPose()
    {
        var animator = npc != null ? npc.GetComponentInChildren<Animator>() : null;
        var controller = animator != null ? animator.runtimeAnimatorController : null;
        var hips = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Hips) : null;
        var head = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
        if (hips == null || head == null || controller == null)
        {
            return false;
        }

        AnimationClip sleepLoop = null;
        foreach (var clip in controller.animationClips)
        {
            if (clip != null && clip.name.IndexOf("Sleep_TurnToTheSide_Loop", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                sleepLoop = clip;
                break;
            }
        }
        if (sleepLoop == null)
        {
            return false;
        }

        Bounds? mattress = null;
        var volume = 0f;
        foreach (var renderer in Object.FindObjectsOfType<Renderer>())
        {
            var name = renderer.gameObject.name.ToLowerInvariant();
            if (!name.Contains("bed_bedroom") || !(name.Contains("mattress") || name.Contains("cover")))
            {
                continue;
            }

            var v = renderer.bounds.size.x * renderer.bounds.size.z;
            if (v > volume)
            {
                volume = v;
                mattress = renderer.bounds;
            }
        }
        if (!mattress.HasValue)
        {
            return false;
        }

        var visual = animator.transform;
        var localPosition = visual.localPosition;
        var localRotation = visual.localRotation;
        var localScale = visual.localScale;
        try
        {
            sleepLoop.SampleAnimation(animator.gameObject, 0f);
        }
        finally
        {
            visual.localPosition = localPosition;
            visual.localRotation = localRotation;
            visual.localScale = localScale;
        }

        var body = head.position - hips.position;
        body.y = 0f;
        if (body.sqrMagnitude < 0.0001f)
        {
            return false;
        }

        var yawDelta = Vector3.SignedAngle(body.normalized, Vector3.right, Vector3.up) + SleepRootYawOffset;
        _sleepRotation = Quaternion.Euler(0f, npc.transform.eulerAngles.y + yawDelta, 0f);
        var hipOffset = Quaternion.Euler(0f, yawDelta, 0f) * (hips.position - npc.transform.position);
        var bed = mattress.Value;
        _bedCenter = bed.center;
        _bedBounds = bed;
        _surfaceY = bed.max.y;
        var targetHips = new Vector3(bed.center.x - 0.2f, _surfaceY + hipsSleepOffset, bed.max.z - 0.55f);
        _sleepPosition = targetHips - hipOffset;
        _hipOffset = hipOffset.y;
        GameLog.Event(
            "sleep_pose", "hipOffset", GameLog.F(_hipOffset),
            "surface", GameLog.F(_surfaceY));
        return true;
    }

    void ApplySleepHeight()
    {
        var animator = npc.GetComponentInChildren<Animator>();
        var hips = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Hips) : null;
        if (hips == null)
        {
            return;
        }

        var target = _surfaceY + hipsSleepOffset;
        var delta = target - hips.position.y;
        if (Mathf.Abs(delta) > 0.005f)
        {
            npc.transform.position += Vector3.up * delta;
        }
        _sleepPosition = npc.transform.position;
        GameLog.Event("sleep_align", "delta", GameLog.F(delta), "hipsY", GameLog.F(hips.position.y));
    }
}
