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

            var animator = visual.GetComponentInChildren<Animator>();
            if (animator != null)
            {
                // 动画等淘宝 FBX 到位再接；现在保持 T-pose
                animator.runtimeAnimatorController = null;
                animator.applyRootMotion = false;
            }

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
}
