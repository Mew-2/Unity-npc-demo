using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 运行时换 NPC 外观：销毁旧 Visual，实例化新 prefab，更新身高/偏移/服务端 persona。
/// 逻辑（NpcAgent）留在 Npc 根节点，不随模型更换。
/// </summary>
public class CharacterSwitcher : MonoBehaviour
{
    [Header("依赖")]
    public NpcAgent npc;
    public DirectorClient client;

    [Header("角色列表")]
    public List<CharacterProfile> profiles = new List<CharacterProfile>();
    public int currentIndex;

    Transform _visual;

    public CharacterProfile Current
    {
        get
        {
            if (profiles == null || profiles.Count == 0)
            {
                return null;
            }

            var index = Mathf.Clamp(currentIndex, 0, profiles.Count - 1);
            return profiles[index];
        }
    }

    void Start()
    {
        if (profiles.Count > 0)
        {
            Apply(currentIndex);
        }
    }

    public void SwitchTo(int index)
    {
        if (profiles == null || index < 0 || index >= profiles.Count)
        {
            Debug.LogWarning($"[CharacterSwitcher] 角色下标越界：{index}");
            return;
        }

        currentIndex = index;
        Apply(index);
    }

    public void SwitchTo(string id)
    {
        var index = profiles.FindIndex(profile => profile.id == id);
        if (index < 0)
        {
            Debug.LogWarning($"[CharacterSwitcher] 没有角色：{id}");
            return;
        }

        SwitchTo(index);
    }

    void Apply(int index)
    {
        if (npc == null)
        {
            Debug.LogError("[CharacterSwitcher] 缺少 NpcAgent");
            return;
        }

        var profile = profiles[index];

        if (_visual != null)
        {
            Destroy(_visual.gameObject);
            _visual = null;
        }

        if (profile.visualPrefab != null)
        {
            var visual = Instantiate(profile.visualPrefab, npc.transform);
            visual.name = "Visual";
            visual.transform.localPosition = profile.visualOffset;
            visual.transform.localRotation = Quaternion.identity;
            visual.transform.localScale = Vector3.one * Mathf.Max(0.1f, profile.visualScale);

            var animator = visual.GetComponentInChildren<Animator>();

            // 按目标身高自动缩放，只量“包含脚部骨骼的身体网格”，
            // 否则尾巴/头发会把包围盒撑大，导致模型被缩得过小。
            var targetHeight = profile.targetHeight > 0.1f ? profile.targetHeight : 1.69f;
            var currentHeight = EstimateHeight(visual, animator);
            if (currentHeight > 0.01f)
            {
                var scale = targetHeight / currentHeight;
                visual.transform.localScale *= scale;
                Debug.Log($"[CharacterSwitcher] 身高校准：current={currentHeight:F2}m target={targetHeight:F2}m scale×{scale:F3}");

                var head = animator != null ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
                var leftFoot = animator != null ? animator.GetBoneTransform(HumanBodyBones.LeftFoot) : null;
                Debug.Log($"[CharacterSwitcher] 身高详情：headY={(head != null ? head.position.y : -1f):F2} footY={(leftFoot != null ? leftFoot.position.y : -1f):F2} visualY={visual.transform.position.y:F2} scaleY={visual.transform.localScale.y:F2}");
            }

            if (animator != null)
            {
                // 关键：默认的 CullUpdateTransforms 会在模型被判为"不在视野"时停更骨骼，
                // 但 transform 还在被 NpcAgent 推进 —— 表现就是"动作丢了、人在平移"
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

                // 公开仓库不带授权动画：找不到 controller 就回退 T-pose
                var controller = Resources.Load<RuntimeAnimatorController>("KawaiiAnimator");
                animator.runtimeAnimatorController = controller;

                // 关键：打开根位移，让动画自己带着身体走（脚不打滑）
                animator.applyRootMotion = controller != null;

                if (controller != null)
                {
                    // OnAnimatorMove 只会回调到"和 Animator 同一个 GameObject"上的组件，
                    // 所以 relay 必须挂在 animator.gameObject 上（不一定是 visual 根节点）
                    var relay = animator.gameObject.GetComponent<RootMotionRelay>();
                    if (relay == null)
                    {
                        relay = animator.gameObject.AddComponent<RootMotionRelay>();
                    }

                    relay.target = npc.transform;
                    relay.relay = true;
                }

                // 有 LODGroup 的话强制 LOD0，避免远处换成低模后看起来"没动作"
                foreach (var lod in visual.GetComponentsInChildren<LODGroup>())
                {
                    lod.ForceLOD(0);
                }

                foreach (var skinned in visual.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    skinned.updateWhenOffscreen = true;
                }

                if (controller != null)
                {
                    var driver = visual.AddComponent<NpcAnimationDriver>();
                    driver.npc = npc;
                }
            }

            visual.AddComponent<ExpressionController>();
            _visual = visual.transform;
        }

        npc.groundOffset = profile.groundOffset;

        // 有 Visual 时隐藏胶囊本体，避免穿在模型里
        var capsule = npc.GetComponent<Renderer>();
        if (capsule != null)
        {
            capsule.enabled = profile.visualPrefab == null;
        }

        if (client != null)
        {
            client.characterId = profile.personaId;
        }

        Debug.Log($"[CharacterSwitcher] 当前角色：{profile.displayName}（{profile.id}）");
    }

    static float EstimateHeight(GameObject root, Animator animator)
    {
        // 优先用头骨 + 脚骨量：Renderer.bounds 会受头发/呆毛/绑定姿态影响，实测偏大。
        var head = animator != null ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
        var leftFoot = animator != null ? animator.GetBoneTransform(HumanBodyBones.LeftFoot) : null;
        var rightFoot = animator != null ? animator.GetBoneTransform(HumanBodyBones.RightFoot) : null;
        var leftToes = animator != null ? animator.GetBoneTransform(HumanBodyBones.LeftToes) : null;
        var rightToes = animator != null ? animator.GetBoneTransform(HumanBodyBones.RightToes) : null;

        if (head != null && (leftFoot != null || rightFoot != null || leftToes != null || rightToes != null))
        {
            var footY = float.MaxValue;
            foreach (var foot in new[] { leftFoot, rightFoot, leftToes, rightToes })
            {
                if (foot != null)
                {
                    footY = Mathf.Min(footY, foot.position.y);
                }
            }

            const float headTopOffset = 0.18f;
            return Mathf.Max(0.5f, head.position.y + headTopOffset - footY);
        }

        var hasBounds = false;
        var bounds = new Bounds();

        foreach (var skinned in root.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            if (!skinned.enabled || skinned.bones == null)
            {
                continue;
            }

            var hasFoot = false;
            foreach (var bone in skinned.bones)
            {
                if (bone == leftFoot || bone == rightFoot || bone == leftToes || bone == rightToes)
                {
                    hasFoot = true;
                    break;
                }
            }

            // 身体网格（含脚和头）才算身高；尾巴/头发/衣服单独网格跳过
            if (!hasFoot)
            {
                continue;
            }

            if (!hasBounds)
            {
                bounds = skinned.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(skinned.bounds);
            }
        }

        return hasBounds ? bounds.size.y : 0f;
    }
}
