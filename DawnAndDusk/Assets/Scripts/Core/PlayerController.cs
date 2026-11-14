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

    [Tooltip("起身后站定的位置（世界坐标，必须是空地）")]
    public Vector3 standUpPosition;

    [Tooltip("起身时相机高度过渡速度（米/秒）")]
    public float heightLerpSpeed = 1.6f;

    CharacterController _controller;
    float _vertical;
    bool _seated;

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
        var moving = input.sqrMagnitude > 0.01f;

        if (_seated && moving)
        {
            // 起身：挪到床边空地再打开碰撞
            transform.position = standUpPosition;
            _controller.enabled = true;
            _seated = false;
            GameLog.Event("player_stand", "at", standUpPosition.ToString());
        }

        var speed = Input.GetKey(KeyCode.LeftShift) ? runSpeed : walkSpeed;
        var direction = (transform.right * input.x + transform.forward * input.y).normalized;
        var planar = _seated ? Vector3.zero : direction * (speed * input.magnitude);

        if (_controller.isGrounded && _vertical < 0f)
        {
            _vertical = -2f;
        }

        _vertical += gravity * Time.deltaTime;
        _controller.Move((planar + Vector3.up * _vertical) * Time.deltaTime);

        if (cameraTransform != null)
        {
            var target = _seated ? seatedHeight : standingHeight;
            var p = cameraTransform.localPosition;
            p.y = Mathf.MoveTowards(p.y, target, heightLerpSpeed * Time.deltaTime);
            cameraTransform.localPosition = p;
        }
    }
}
