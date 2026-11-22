using UnityEngine;

/// <summary>
/// 让她"低头看玩家"：膝枕时相机在膝上，用 LookAt IK 把头和上半身转过去。
/// 需要 Animator 的 layer 打开 IK Pass（KawaiiAnimatorBuilder 里已设 iKPass）。
/// 挂在和 Animator 同一个 GameObject 上（通常是 Visual）。
/// </summary>
[RequireComponent(typeof(Animator))]
public class NpcLookAtIK : MonoBehaviour
{
    [Tooltip("看向的目标（通常是玩家相机）")]
    public Transform target;

    [Tooltip("是否生效。膝枕之外应保持关闭，避免影响走路/待机")]
    public bool active;

    [Header("权重")]
    [Range(0f, 1f)] public float bodyWeight = 0.15f;
    [Range(0f, 1f)] public float headWeight = 0.85f;
    [Range(0f, 1f)] public float eyesWeight = 0.6f;
    [Range(0f, 1f)] public float clampWeight = 0.55f;

    Animator _animator;

    void Awake()
    {
        _animator = GetComponent<Animator>();
    }

    void OnAnimatorIK(int layerIndex)
    {
        if (_animator == null)
        {
            return;
        }

        if (!active || target == null)
        {
            _animator.SetLookAtWeight(0f);
            return;
        }

        _animator.SetLookAtPosition(target.position);
        _animator.SetLookAtWeight(1f, bodyWeight, headWeight, eyesWeight, clampWeight);
    }

    /// <summary>外部（膝枕控制器）设置目标并开关。</summary>
    public void SetTarget(Transform lookTarget, bool on)
    {
        target = lookTarget;
        active = on;
        // 注意：不要在 OnAnimatorIK 之外调用 SetLookAtWeight/SetLookAtPosition，
        // Unity 会报 "Setting and getting ... should only be done in OnAnimatorIK"。
        // 关闭时 OnAnimatorIK 里会自己把权重归零。
    }
}
