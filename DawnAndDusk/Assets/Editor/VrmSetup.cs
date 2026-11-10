using UnityEditor;
using UnityEngine;

/// <summary>
/// 把 CharacterSwitcher 的 visualPrefab 指向 VRM 资源本身。
/// 不要从场景实例另存 prefab：那样会把材质覆盖成 None（模型变灰）。
/// 菜单：Tools/DawnAndDusk/Attach DeepSeek VRM
/// </summary>
public static class VrmSetup
{
    const string VrmPath = "Assets/Characters/DeepSeekChan/DeepSeek-chan.vrm";
    const string OldPrefabPath = "Assets/Characters/DeepSeekChan/DeepSeek-chan.prefab";

    [MenuItem("Tools/DawnAndDusk/Attach DeepSeek VRM")]
    public static void Attach()
    {
        var vrmAsset = AssetDatabase.LoadAssetAtPath<GameObject>(VrmPath);
        if (vrmAsset == null)
        {
            Debug.LogError($"[VrmSetup] 读不到 VRM：{VrmPath}");
            return;
        }

        // 删掉之前从场景另存的坏 prefab（材质被覆盖成 None）
        if (AssetDatabase.LoadAssetAtPath<GameObject>(OldPrefabPath) != null)
        {
            AssetDatabase.DeleteAsset(OldPrefabPath);
        }

        // 场景里如果有临时 VRM 实例，一并清掉
        var sceneInstance = GameObject.Find("DeepSeek-chan");
        if (sceneInstance != null)
        {
            Object.DestroyImmediate(sceneInstance);
        }

        var npc = Object.FindObjectOfType<NpcAgent>();
        var director = GameObject.Find("Director");

        if (npc == null || director == null)
        {
            Debug.LogError("[VrmSetup] 场景缺少 Npc 或 Director，先跑 Build Greybox + Setup Client。");
            return;
        }

        var switcher = director.GetComponent<CharacterSwitcher>();
        if (switcher == null)
        {
            switcher = director.AddComponent<CharacterSwitcher>();
        }

        switcher.npc = npc;
        switcher.client = director.GetComponent<DirectorClient>();
        switcher.profiles.Clear();
        switcher.profiles.Add(new CharacterProfile
        {
            id = "whale",
            displayName = "鲸鲸",
            personaId = "whale",
            visualPrefab = vrmAsset,
            visualOffset = new Vector3(0f, -1f, 0f),
            groundOffset = 1f
        });
        switcher.currentIndex = 0;

        EditorUtility.SetDirty(switcher);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("[VrmSetup] visualPrefab 已指向 VRM 资源；Play 时会用原始材质实例化。");
    }
}
