using UnityEngine;

/// <summary>
/// 把 NpcAgent 的移动状态喂给 Animator。
/// 没有 controller（公开仓库没带授权资产）时自动不工作，模型保持 T-pose。
/// </summary>
public class NpcAnimationDriver : MonoBehaviour
{
    public NpcAgent npc;

    Animator _animator;
    float _speed;
    bool _wasMoving;

    void Awake()
    {
        _animator = GetComponentInChildren<Animator>();
        if (npc == null)
        {
            npc = GetComponentInParent<NpcAgent>();
        }
    }

    void Start()
    {
        if (_animator == null || _animator.runtimeAnimatorController == null)
        {
            enabled = false;
            return;
        }

        _animator.applyRootMotion = false;
        StartCoroutine(GroundOnce());
    }

    void Update()
    {
        if (_animator == null || npc == null)
        {
            return;
        }

        var target = npc.IsMoving ? 1f : 0f;
        _speed = Mathf.MoveTowards(_speed, target, Time.deltaTime * 6f);
        _animator.SetFloat("Speed", _speed);

        // 走到新位置停下后重新贴地，避免换锚点后脚陷进地板
        if (_wasMoving && !npc.IsMoving)
        {
            StartCoroutine(GroundOnce());
        }

        _wasMoving = npc.IsMoving;

        // 兜底：Speed 已归零但状态机卡在 Walk/WalkStart，或 WalkStop 已播完，强制回 Idle
        if (!npc.IsMoving && _speed < 0.01f)
        {
            var state = _animator.GetCurrentAnimatorStateInfo(0);
            if (state.IsName("Walk") || state.IsName("WalkStart")
                || (state.IsName("WalkStop") && state.normalizedTime >= 0.95f))
            {
                _animator.CrossFade("Idle", 0.2f);
            }
        }
    }

    public void SetSitting(bool sitting)
    {
        if (_animator != null && _animator.runtimeAnimatorController != null)
        {
            _animator.SetBool("Sit", sitting);
        }
    }

    public void PlayBow()
    {
        if (_animator != null && _animator.runtimeAnimatorController != null)
        {
            _animator.SetTrigger("Bow");
        }
    }

    public void PlayPivot()
    {
        if (_animator != null && _animator.runtimeAnimatorController != null)
        {
            _animator.SetTrigger("Pivot");
        }
    }

    System.Collections.IEnumerator GroundOnce()
    {
        // 等动画播两帧再量脚底
        yield return null;
        yield return null;

        var left = _animator.GetBoneTransform(HumanBodyBones.LeftFoot);
        var right = _animator.GetBoneTransform(HumanBodyBones.RightFoot);
        var leftToes = _animator.GetBoneTransform(HumanBodyBones.LeftToes);
        var rightToes = _animator.GetBoneTransform(HumanBodyBones.RightToes);

        var minY = float.MaxValue;

        // 烘焙当前姿势的顶点，取真实最低点（鞋底），避免用可能过期的 bounds
        foreach (var skinned in GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            if (skinned == null || !skinned.enabled || skinned.sharedMesh == null)
            {
                continue;
            }

            var hasFoot = false;
            foreach (var bone in skinned.bones)
            {
                if (bone == left || bone == right || bone == leftToes || bone == rightToes)
                {
                    hasFoot = true;
                    break;
                }
            }

            if (!hasFoot)
            {
                continue;
            }

            var mesh = new Mesh();
            skinned.BakeMesh(mesh);
            var vertices = mesh.vertices;
            for (var i = 0; i < vertices.Length; i++)
            {
                minY = Mathf.Min(minY, skinned.transform.TransformPoint(vertices[i]).y);
            }

            Destroy(mesh);
        }

        if (minY == float.MaxValue)
        {
            yield break;
        }

        var delta = -minY + 0.005f;
        transform.localPosition += Vector3.up * delta;
        Debug.Log($"[NpcAnimationDriver] 落地修正 {delta:F3}m（minY={minY:F3}）");
    }
}
