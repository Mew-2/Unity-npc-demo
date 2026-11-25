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
    Vector3 _drapeDirection;
    Vector3 _savedCamLocalPos;
    Quaternion _savedCamLocalRot;
    Coroutine _enterRoutine;
    Coroutine _cameraRoutine;
    Coroutine _returnRoutine;
    Transform _tailRoot;
    Quaternion _tailInitialLocalRotation;
    readonly List<Transform> _tailBones = new List<Transform>();
    readonly List<Quaternion> _tailInitialLocalRotations = new List<Quaternion>();

    const float SleepRootYawOffset = 180f;
    const float SleepArmBackOffset = 0.25f;
    const float SleepArmDownOffset = 0.12f;
    // 尾巴只朝床沿外轻垂：先近水平越过床沿，越过之后才靠弹簧自然下坠。
    const float TailDrapeDownDegrees = 9f;
    // 床面投影内的尾骨离床面的最小余量；尾鳍薄而宽，骨点在鳍内部，要留出鳍厚。
    const float TailBedClearance = 0.17f;

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
            RestLeftArmForSleep();
        }
        if (_state == State.InSleep && _cameraRoutine == null && cameraTransform != null)
        {
            HoldCamera();
        }
    }

    void LayTailForSleep()
    {
        EnsureTailBones();

        var animator = npc.GetComponentInChildren<Animator>();
        var head = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
        if (_tailRoot == null || head == null)
        {
            return;
        }

        if (_tailRoot.childCount == 0)
        {
            return;
        }

        var drape = _drapeDirection;
        drape.y = 0f;
        if (drape.sqrMagnitude < 0.0001f)
        {
            var hips = animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Hips) : null;
            drape = hips != null
                ? ComputeDrapeDirection(hips.position, head.position, head.forward)
                : Vector3.forward;
        }
        drape.Normalize();

        // 只定尾根第一节的方向：近水平朝床沿外，略向下；不再逐节拉直。
        var down = Mathf.Sin(TailDrapeDownDegrees * Mathf.Deg2Rad);
        var flat = Mathf.Cos(TailDrapeDownDegrees * Mathf.Deg2Rad);
        var desired = drape * flat + Vector3.down * down;
        var segment = _tailRoot.GetChild(0).position - _tailRoot.position;
        if (segment.sqrMagnitude > 0.0001f && desired.sqrMagnitude > 0.0001f)
        {
            _tailRoot.rotation = Quaternion.FromToRotation(segment.normalized, desired.normalized) * _tailRoot.rotation;
        }

        // 兜底：只把仍在床面投影内、且低于床面的尾骨旋转抬出床垫；
        // 不写世界坐标、不拉直、不动床沿外的段落，保留弹簧的自然下垂。
        LiftTailOutOfMattress(drape);
    }

    void EnsureTailBones()
    {
        if (_tailBones.Count > 0)
        {
            return;
        }

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

        var current = _tailRoot;
        while (current != null)
        {
            if (current.name.StartsWith("DS_whale_tail", System.StringComparison.Ordinal))
            {
                _tailBones.Add(current);
                _tailInitialLocalRotations.Add(current.localRotation);
            }
            current = current.childCount > 0 ? current.GetChild(0) : null;
        }
    }

    Vector3 ComputeDrapeDirection(Vector3 hipsPosition, Vector3 headPosition, Vector3 headForward)
    {
        // 睡姿下相机在脸的正前方，尾巴应朝"背侧"越过床沿；同时避开枕头那侧。
        var back = headForward;
        back.y = 0f;
        if (back.sqrMagnitude < 0.0001f)
        {
            back = headPosition - _bedBounds.center;
            back.y = 0f;
        }
        if (back.sqrMagnitude < 0.0001f)
        {
            back = Vector3.forward;
        }
        back.Normalize();

        var pillowEdge = NearestEdgeDirection(headPosition);
        var candidates = new[]
        {
            (dir: Vector3.right, distance: _bedBounds.max.x - hipsPosition.x),
            (dir: Vector3.left, distance: hipsPosition.x - _bedBounds.min.x),
            (dir: Vector3.forward, distance: _bedBounds.max.z - hipsPosition.z),
            (dir: Vector3.back, distance: hipsPosition.z - _bedBounds.min.z),
        };

        var best = Vector3.zero;
        var bestDistance = float.PositiveInfinity;
        foreach (var candidate in candidates)
        {
            if (candidate.distance <= 0.02f || candidate.dir == pillowEdge)
            {
                continue;
            }
            if (Vector3.Dot(candidate.dir, back) <= 0.05f)
            {
                continue;
            }
            if (candidate.distance < bestDistance)
            {
                bestDistance = candidate.distance;
                best = candidate.dir;
            }
        }

        if (best == Vector3.zero)
        {
            foreach (var candidate in candidates)
            {
                if (candidate.distance > 0.02f && candidate.dir != pillowEdge
                    && candidate.distance < bestDistance)
                {
                    bestDistance = candidate.distance;
                    best = candidate.dir;
                }
            }
        }
        if (best.sqrMagnitude < 0.0001f)
        {
            best = Vector3.forward;
        }
        return best.normalized;
    }

    Vector3 NearestEdgeDirection(Vector3 point)
    {
        var best = Vector3.forward;
        var bestDistance = float.PositiveInfinity;
        var candidates = new[]
        {
            (dir: Vector3.right, distance: _bedBounds.max.x - point.x),
            (dir: Vector3.left, distance: point.x - _bedBounds.min.x),
            (dir: Vector3.forward, distance: _bedBounds.max.z - point.z),
            (dir: Vector3.back, distance: point.z - _bedBounds.min.z),
        };
        foreach (var candidate in candidates)
        {
            if (candidate.distance < bestDistance)
            {
                bestDistance = candidate.distance;
                best = candidate.dir;
            }
        }
        return best;
    }

    void LiftTailOutOfMattress(Vector3 drape)
    {
        if (_bedBounds.size.sqrMagnitude <= 0f || _tailBones.Count < 2)
        {
            return;
        }

        var targetY = _surfaceY + TailBedClearance;
        var fallbackAxis = Vector3.Cross(drape, Vector3.up);
        for (var i = 0; i < _tailBones.Count - 1; i++)
        {
            var bone = _tailBones[i];
            var child = _tailBones[i + 1];
            if (bone == null || child == null)
            {
                continue;
            }

            var childPosition = child.position;
            if (!IsOverBed(childPosition) || childPosition.y >= targetY)
            {
                continue;
            }

            var pivot = bone.position;
            var segment = childPosition - pivot;
            var length = segment.magnitude;
            if (length < 0.0001f)
            {
                continue;
            }

            var horizontal = new Vector3(segment.x, 0f, segment.z);
            var horizontalMagnitude = horizontal.magnitude;
            var currentAngle = Mathf.Atan2(segment.y, horizontalMagnitude);
            var requiredRise = Mathf.Clamp(targetY - pivot.y, -length, length);
            var desiredAngle = Mathf.Asin(requiredRise / length);
            var delta = desiredAngle - currentAngle;
            if (delta <= 0.0001f)
            {
                continue;
            }

            var axis = horizontalMagnitude > 0.0001f
                ? Vector3.Cross(horizontal / horizontalMagnitude, Vector3.up)
                : fallbackAxis;
            if (axis.sqrMagnitude < 0.0001f)
            {
                continue;
            }

            bone.rotation = Quaternion.AngleAxis(delta * Mathf.Rad2Deg, axis.normalized) * bone.rotation;
        }
    }

    bool IsOverBed(Vector3 position)
    {
        return position.x > _bedBounds.min.x && position.x < _bedBounds.max.x
            && position.z > _bedBounds.min.z && position.z < _bedBounds.max.z;
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
        ComputeDrapeFromCurrentPose();
        GameLog.Event("sleep_loop", "result", "ready");
        EnterSleep();
        _enterRoutine = null;
    }

    void ComputeDrapeFromCurrentPose()
    {
        var animator = npc.GetComponentInChildren<Animator>();
        var head = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
        var hips = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Hips) : null;
        if (head == null || hips == null || _bedBounds.size.sqrMagnitude <= 0f)
        {
            return;
        }

        _drapeDirection = ComputeDrapeDirection(hips.position, head.position, head.forward);
        GameLog.Event(
            "sleep_tail_drape",
            "dir", _drapeDirection.ToString("F2"),
            "headFwd", head.forward.ToString("F2"),
            "hips", hips.position.ToString("F2"),
            "bedMin", _bedBounds.min.ToString("F2"),
            "bedMax", _bedBounds.max.ToString("F2"));
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

        if (_tailBones.Count > 0)
        {
            for (var i = 0; i < _tailBones.Count; i++)
            {
                if (_tailBones[i] != null)
                {
                    _tailBones[i].localRotation = _tailInitialLocalRotations[i];
                }
            }
        }
        else if (_tailRoot != null)
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
        var targetHips = new Vector3(bed.center.x + 0.25f, _surfaceY + hipsSleepOffset, bed.max.z - 0.55f);
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
