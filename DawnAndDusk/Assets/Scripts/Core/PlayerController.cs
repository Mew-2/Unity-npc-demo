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
    public float seatedHeight = 1.02f;
    public float standingHeight = 1.58f;

    [Tooltip("开局是否躺在床上（碰撞体先关掉，按移动键时瞬移到床边并启用碰撞）")]
    public bool startSeated = true;

    [Tooltip("是否允许走动。房间没有站位时可关掉，只保留环视")]
    public bool allowMovement = true;

    [Tooltip("起身后站定的位置（世界坐标，必须是空地）")]
    public Vector3 standUpPosition;

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
}
