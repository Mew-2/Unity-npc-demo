using UnityEngine;

/// <summary>
/// 第一人称走动：WASD 移动（Shift 快走），CharacterController 负责碰撞与重力。
/// 开局是"坐在床边"的低视角，一旦开始走就站起身（相机高度平滑过渡）。
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    [Header("移动")]
    public float walkSpeed = 2.0f;
    public float runSpeed = 3.6f;
    public float gravity = -12f;

    [Header("视角高度")]
    public Transform cameraTransform;
    [Tooltip("躺/坐在床上时的眼高（相对床面）")]
    public float seatedHeight = 0.35f;
    public float standingHeight = 1.63f;

    [Tooltip("开局是否躺在床上（碰撞体先关掉，按移动键时瞬移到床边并启用碰撞）")]
    public bool startSeated = true;

    [Tooltip("是否允许走动。房间没有站位时可关掉，只保留环视")]
    public bool allowMovement = true;

    [Tooltip("起身后站定的位置（世界坐标，必须是空地）")]
    public Vector3 standUpPosition;

    [Header("坐床边出生（覆盖场景坐标）")]
    public bool useSeatedSpawn = true;
    public Vector3 seatedSpawnPosition = new Vector3(3.45f, 0.55f, 2.65f);
    public Vector3 seatedLookTarget = new Vector3(2.85f, 1.25f, 3.35f);
    public Vector3 seatedStandPosition = new Vector3(3.45f, 0.05f, 1.55f);

    [Tooltip("起身时相机高度过渡速度（米/秒）")]
    public float heightLerpSpeed = 1.6f;

    CharacterController _controller;
    float _vertical;
    bool _seated;
    float _logTimer;

    void Awake()
    {
        _controller = GetComponent<CharacterController>();
        _seated = startSeated;

        // 旧场景序列化的是 1.02 / 0.95 坐姿，按躺姿 0.35 迁移；站姿 1.58 → 1.63。
        if (Mathf.Approximately(seatedHeight, 1.02f) || Mathf.Approximately(seatedHeight, 0.95f))
        {
            seatedHeight = 0.35f;
        }

        if (Mathf.Approximately(standingHeight, 1.58f))
        {
            standingHeight = 1.63f;
        }

        if (_seated && useSeatedSpawn)
        {
            var spawn = seatedSpawnPosition;
            var lookTarget = seatedLookTarget;
            var hasBed = TryFindBedBounds(out var bed);

            // 床的长度方向是 X：枕头在 max.x 侧，宽度方向是 Z
            if (hasBed)
            {
                var spawnX = bed.max.x - 0.35f;
                var spawnZ = bed.center.z;
                spawn = new Vector3(spawnX, bed.center.y, spawnZ);
                standUpPosition = FindStandPosition(spawn, bed);

                var bedAnchor = GameObject.Find("Anchor_bed");
                if (bedAnchor != null)
                {
                    lookTarget = bedAnchor.transform.position + Vector3.up * 1.2f;
                }
            }

            // 从上方往下打，落在床面而不是固定 y，避免床高变化后悬空/陷进去
            if (Physics.Raycast(new Vector3(spawn.x, spawn.y + 2f, spawn.z), Vector3.down, out var hit, 4f, ~0, QueryTriggerInteraction.Ignore))
            {
                spawn.y = hit.point.y + 0.02f;
            }

            transform.position = spawn;
            if (!hasBed)
            {
                standUpPosition = seatedStandPosition;
            }

            var look = lookTarget - (spawn + Vector3.up * seatedHeight);
            look.y = 0f;
            if (look.sqrMagnitude > 0.001f)
            {
                transform.rotation = Quaternion.LookRotation(look.normalized, Vector3.up);
            }
        }

        if (_seated)
        {
            // 躺在床里时关掉碰撞，否则会被床的碰撞体顶出去
            _controller.enabled = false;
        }

        if (cameraTransform != null)
        {
            var p = cameraTransform.localPosition;
            cameraTransform.localPosition = new Vector3(p.x, _seated ? seatedHeight : standingHeight, p.z);
        }

        LogSpawn("spawn");
    }

    /// <summary>出生/起身时记录一次：到关键物体的距离 + 正前方射线命中了什么。</summary>
    void LogSpawn(string tag)
    {
        var cam = cameraTransform != null ? cameraTransform : transform;

        string DistanceTo(string objectName)
        {
            var go = GameObject.Find(objectName);
            return go == null ? "-" : GameLog.F(Vector3.Distance(transform.position, go.transform.position));
        }

        var camCamera = cam.GetComponent<Camera>();
        var front = "-";
        if (Physics.Raycast(cam.position, cam.forward, out var hit, 8f))
        {
            front = $"{hit.collider.name}@{GameLog.F(hit.distance)}";
        }

        GameLog.Event(
            tag,
            "pos", $"{transform.position.x:F2},{transform.position.y:F2},{transform.position.z:F2}",
            "camPos", $"{cam.position.x:F2},{cam.position.y:F2},{cam.position.z:F2}",
            "yaw", GameLog.F(cam.eulerAngles.y),
            "fov", camCamera != null ? GameLog.F(camCamera.fieldOfView) : "-",
            "dDoor", DistanceTo("Anchor_door"),
            "dChair", DistanceTo("Chair_Norraryd"),
            "dWindow", DistanceTo("Anchor_window"),
            "front", front,
            "camLocal", $"{cam.localPosition.x:F2},{cam.localPosition.y:F2},{cam.localPosition.z:F2}",
            "camParent", cam.parent != null ? cam.parent.name : "-",
            "playerYaw", GameLog.F(transform.eulerAngles.y));
    }

    void Update()
    {
        var input = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
        var moving = allowMovement && input.sqrMagnitude > 0.01f;

        if (_seated && moving)
        {
            // 起身：挪到床边空地再打开碰撞
            transform.position = standUpPosition;
            _controller.enabled = true;
            _vertical = -2f;   // 关键：清掉坐姿期间累积的重力速度
            _seated = false;
            GameLog.Event("player_stand", "at", standUpPosition.ToString());
            LogSpawn("spawn_after_stand");
        }

        var speed = Input.GetKey(KeyCode.LeftShift) ? runSpeed : walkSpeed;
        var direction = (transform.right * input.x + transform.forward * input.y).normalized;
        var planar = _seated ? Vector3.zero : direction * (speed * input.magnitude);

        // 坐着的时候控制器是关的，Move 不生效——但重力不能继续累积：
        // 坐 60 秒 = -720 m/s，起身那一帧 Move 会往下扎两米，CharacterController 只能把人往床面上/天花板上弹，
        // 表现就是"随便一走就被床顶起来""起身比门还高"。
        if (!_seated && _controller.enabled)
        {
            if (_controller.isGrounded && _vertical < 0f)
            {
                _vertical = -2f;
            }

            _vertical += gravity * Time.deltaTime;
            _controller.Move((planar + Vector3.up * _vertical) * Time.deltaTime);
        }
        else
        {
            _vertical = 0f;
        }

        if (cameraTransform != null)
        {
            var target = _seated ? seatedHeight : standingHeight;
            var p = cameraTransform.localPosition;
            p.y = Mathf.MoveTowards(p.y, target, heightLerpSpeed * Time.deltaTime);
            cameraTransform.localPosition = p;
        }

        // 玩家自身状态 1Hz：位置/垂直速度/是否着地/控制器开关——
        // 以后判断"被顶起来"是不是自己造成的，不用再猜
        _logTimer += Time.deltaTime;
        if (_logTimer >= 1f)
        {
            _logTimer = 0f;
            GameLog.Event(
                "player",
                "pos", $"{transform.position.x:F2},{transform.position.y:F2},{transform.position.z:F2}",
                "v", GameLog.F(_vertical),
                "grounded", _controller.isGrounded ? "1" : "0",
                "ctrl", _controller.enabled ? "1" : "0",
                "seated", _seated ? "1" : "0",
                "camY", cameraTransform != null ? GameLog.F(cameraTransform.position.y) : "-");
        }
    }

    static bool TryFindBedBounds(out Bounds bed)
    {
        bed = default;

        var hasBase = false;
        var baseVolume = 0f;
        var baseBounds = default(Bounds);
        var baseName = string.Empty;

        var hasSurface = false;
        var surfaceVolume = 0f;
        var surfaceBounds = default(Bounds);
        var surfaceName = string.Empty;

        var anyVolume = 0f;
        var anyBounds = default(Bounds);
        var anyName = string.Empty;

        foreach (var renderer in FindObjectsOfType<Renderer>())
        {
            if (!renderer.enabled || !renderer.gameObject.activeInHierarchy)
            {
                continue;
            }

            var name = renderer.gameObject.name;
            var lower = name.ToLowerInvariant();
            if (lower.IndexOf("bed_bedroom", System.StringComparison.Ordinal) < 0)
            {
                continue;
            }

            var b = renderer.bounds;
            var volume = b.size.x * b.size.z;

            // 只认床垫 Base；没有 Base 才退回 Cover/Mattress
            if (lower.Contains("base") && volume > baseVolume)
            {
                hasBase = true;
                baseVolume = volume;
                baseBounds = b;
                baseName = name;
            }

            var isSurface = lower.Contains("cover") || lower.Contains("mattress");
            if (isSurface && volume > surfaceVolume)
            {
                hasSurface = true;
                surfaceVolume = volume;
                surfaceBounds = b;
                surfaceName = name;
            }

            if (volume > anyVolume)
            {
                anyVolume = volume;
                anyBounds = b;
                anyName = name;
            }
        }

        if (hasBase)
        {
            bed = baseBounds;
            Debug.Log($"[Player] bed base={baseName} center={bed.center} size={bed.size}");
            return true;
        }

        if (hasSurface)
        {
            bed = surfaceBounds;
            Debug.Log($"[Player] bed surface={surfaceName} center={bed.center} size={bed.size}");
            return true;
        }

        if (anyVolume > 0.01f)
        {
            bed = anyBounds;
            Debug.Log($"[Player] bed fallback={anyName} center={bed.center} size={bed.size}");
            return true;
        }

        return false;
    }

    static Vector3 FindStandPosition(Vector3 seated, Bounds bed)
    {
        // 床的长度方向是 X：先从“宽度两侧”（Z 方向）下床，其次床头/床尾
        var candidates = new[]
        {
            new Vector3(seated.x, 0f, bed.min.z - 0.45f),
            new Vector3(seated.x, 0f, bed.max.z + 0.45f),
            new Vector3(bed.min.x - 0.45f, 0f, seated.z),
            new Vector3(bed.max.x + 0.45f, 0f, seated.z),
        };

        foreach (var candidate in candidates)
        {
            var p = candidate;
            p.y = GroundY(p.x, p.z, 0.05f) + 0.05f;
            if (IsStandFree(p))
            {
                Debug.Log($"[Player] stand position={p.x:F2},{p.y:F2},{p.z:F2}");
                return p;
            }
        }

        Debug.LogWarning("[Player] 床边没有空位，回退 seatedStandPosition");
        return new Vector3(seated.x, GroundY(seated.x, seated.z, 0.05f) + 0.05f, seated.z);
    }

    static bool IsStandFree(Vector3 position)
    {
        var bottom = position + Vector3.up * 0.3f;
        var top = position + Vector3.up * 1.45f;
        return !Physics.CheckCapsule(bottom, top, 0.28f, ~0, QueryTriggerInteraction.Ignore);
    }

    static float GroundY(float x, float z, float fallback)
    {
        if (Physics.Raycast(new Vector3(x, 3f, z), Vector3.down, out var hit, 6f, ~0, QueryTriggerInteraction.Ignore))
        {
            return hit.point.y;
        }

        return fallback;
    }
}
