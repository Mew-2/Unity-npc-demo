using UnityEngine;

/// <summary>
/// 第一人称视角：按住鼠标右键拖动转视角（左键留给"摸头/拍背"）。
/// 只转不移动——玩家位置由场景固定，符合"躺在床上/站在床边"的叙事。
/// </summary>
public class PlayerLook : MonoBehaviour
{
    [Header("参数")]
    public float sensitivity = 3.2f;
    public float minPitch = -55f;
    public float maxPitch = 45f;

    [Tooltip("必须按住右键才转视角，避免和左键点击 NPC 冲突")]
    public bool requireRightButton = true;

    [Tooltip("水平旋转施加在这个物体上（玩家身体），相机只做俯仰——玩家走动时方向才对")]
    public Transform yawRoot;

    float _yaw;
    float _pitch;

    void Start()
    {
        var root = yawRoot != null ? yawRoot : transform;
        _yaw = root.eulerAngles.y;
        var euler = transform.localEulerAngles;
        _pitch = euler.x > 180f ? euler.x - 360f : euler.x;
    }

    void Update()
    {
        if (requireRightButton && !Input.GetMouseButton(1))
        {
            return;
        }

        _yaw += Input.GetAxis("Mouse X") * sensitivity;
        _pitch -= Input.GetAxis("Mouse Y") * sensitivity;
        _pitch = Mathf.Clamp(_pitch, minPitch, maxPitch);

        var root = yawRoot != null ? yawRoot : transform;
        root.rotation = Quaternion.Euler(0f, _yaw, 0f);
        transform.localRotation = Quaternion.Euler(_pitch, 0f, 0f);

        if (Input.GetMouseButtonDown(1))
        {
            GameLog.Event("look_begin");
        }
    }
}
