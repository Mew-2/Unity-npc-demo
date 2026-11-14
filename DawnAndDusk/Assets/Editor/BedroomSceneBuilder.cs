using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 把真实卧室场景接上整套 demo 玩法：
///   Player(第一人称相机) + Npc(NpcAgent + VRM) + Anchors(7 个语义锚点) + Director(导演/UI/执行器)
///
/// 产物：Assets/_LocalTest/Bedroom/Scene/BedroomDemo.unity（_LocalTest 已 gitignore，
/// 公开仓库仍只有灰盒 NightRoom，clone 后照样能跑）。
///
/// 菜单：Tools/DawnAndDusk/Build Bedroom Demo Scene
/// </summary>
public static class BedroomSceneBuilder
{
    const string SourceScene = "Assets/_LocalTest/Bedroom/Scene/bedroom.unity";
    const string OutputScene = "Assets/_LocalTest/Bedroom/Scene/BedroomDemo.unity";
    const string VrmPath = "Assets/Characters/DeepSeekChan/DeepSeek-chan.vrm";

    struct AnchorSpec
    {
        public string id;
        public string name;
        public string desc;
        public Vector3 pos;
    }

    // 房间 x[-2.45, 2.37] z[-2.21, 1.67] y[0, 3.0]；床占 x[-1.19,1.19] z[-0.94,1.65]
    static readonly AnchorSpec[] Specs =
    {
        new AnchorSpec { id = "bed",    name = "床边",   desc = "铺着深色被子的双人床，床头有两盏小灯", pos = new Vector3(-0.95f, 0f, -1.20f) },
        new AnchorSpec { id = "window", name = "窗边",   desc = "左侧靠墙的落地窗，挂着厚窗帘",         pos = new Vector3(-1.90f, 0f, 0.55f) },
        new AnchorSpec { id = "desk",   name = "梳妆台", desc = "右侧床头柜和小台灯",                   pos = new Vector3(1.55f, 0f, 0.45f) },
        new AnchorSpec { id = "door",   name = "门口",   desc = "房间唯一的门，门边有把手",             pos = new Vector3(1.55f, 0f, -1.35f) },
        new AnchorSpec { id = "screen", name = "柜前",   desc = "电视和矮柜前面",                       pos = new Vector3(-1.05f, 0f, -1.70f) },
        new AnchorSpec { id = "lamp",   name = "床头灯", desc = "床头的一盏暖光小灯",                   pos = new Vector3(-1.60f, 0f, 0.95f) },
        new AnchorSpec { id = "front",  name = "身前",   desc = "你面前，近距离说话的位置",             pos = new Vector3(0f, 0f, -1.25f) },
    };

    // 手工连边（房间里床占中间，自动射线连边会穿过被褥，所以显式指定）
    static readonly (string a, string b)[] Edges =
    {
        ("bed", "screen"), ("bed", "front"), ("bed", "lamp"),
        ("screen", "door"), ("door", "front"), ("door", "desk"),
        ("lamp", "window"),
        ("front", "bed"),
    };

    [MenuItem("Tools/DawnAndDusk/Build Bedroom Demo Scene")]
    public static void Build()
    {
        if (!System.IO.File.Exists(SourceScene.Replace("Assets", Application.dataPath)))
        {
            Debug.LogError($"[Bedroom] 找不到源场景 {SourceScene}（先导入 Bedroom 资源包到 _LocalTest）");
            return;
        }

        var scene = EditorSceneManager.OpenScene(SourceScene, OpenSceneMode.Single);

        // 1) 相机：用场景自带的 Camera，挪到床边第一人称位置
        var camera = Object.FindObjectsOfType<Camera>().FirstOrDefault();
        if (camera == null)
        {
            var camGo = new GameObject("Main Camera");
            camera = camGo.AddComponent<Camera>();
            camGo.AddComponent<AudioListener>();
        }

        camera.transform.position = new Vector3(0f, 1.55f, -2.02f);
        camera.transform.rotation = Quaternion.Euler(2f, 0f, 0f);
        camera.nearClipPlane = 0.05f;
        camera.farClipPlane = 30f;
        camera.tag = "MainCamera";

        // 2) 玩法根节点
        var root = new GameObject("Game");

        // 3) 锚点
        var anchorsGo = new GameObject("Anchors");
        anchorsGo.transform.SetParent(root.transform, false);
        var graph = anchorsGo.AddComponent<AnchorGraph>();
        graph.autoConnect = false;

        var anchorList = new List<Anchor>();
        foreach (var spec in Specs)
        {
            var go = new GameObject("Anchor_" + spec.id);
            go.transform.SetParent(anchorsGo.transform, false);
            go.transform.position = spec.pos;
            var anchor = go.AddComponent<Anchor>();
            anchor.id = spec.id;
            anchor.displayName = spec.name;
            anchor.description = spec.desc;
            anchorList.Add(anchor);
        }

        graph.anchors = anchorList;
        graph.edges = Edges.Select(e => new AnchorGraph.Edge { a = e.a, b = e.b }).ToList();

        // 4) NPC
        var npcGo = new GameObject("Npc");
        npcGo.transform.SetParent(root.transform, false);
        npcGo.transform.position = new Vector3(-0.95f, 1f, -1.20f);
        var npc = npcGo.AddComponent<NpcAgent>();
        npc.graph = graph;
        npc.moveSpeed = 1.03f;
        npc.accelTime = 0.6f;
        npc.decelTime = 0.3f;
        npc.groundOffset = 1f;
        npc.autoPivot = false;

        // 5) Director：网络 / UI / 执行器 / 角色切换 / 状态切换 / 交互
        var directorGo = new GameObject("Director");
        directorGo.transform.SetParent(root.transform, false);

        var client = directorGo.AddComponent<DirectorClient>();
        client.graph = graph;

        var executor = directorGo.AddComponent<CommandExecutor>();
        executor.npc = npc;
        executor.graph = graph;

        var state = directorGo.AddComponent<StateSwitcher>();
        state.graph = graph;
        state.npc = npc;
        state.buildProps = false;
        state.roomLights = Object.FindObjectsOfType<Light>()
            .Where(l => l.type == LightType.Rectangle || l.type == LightType.Point)
            .ToArray();

        var switcher = directorGo.AddComponent<CharacterSwitcher>();
        switcher.npc = npc;
        switcher.client = client;

        var vrm = AssetDatabase.LoadAssetAtPath<GameObject>(VrmPath);
        if (vrm == null)
        {
            Debug.LogError($"[Bedroom] 找不到 VRM：{VrmPath}");
        }

        switcher.profiles = new List<CharacterProfile>
        {
            new CharacterProfile
            {
                id = "whale",
                displayName = "鲸鲸",
                personaId = "whale",
                visualPrefab = vrm,
                visualOffset = new Vector3(0f, -1f, 0f),
                groundOffset = 1f,
                headBone = "J_Bip_C_Head",
                backBone = "J_Bip_C_Spine",
            }
        };

        var dialogue = directorGo.AddComponent<DialogueUI>();
        dialogue.client = client;
        dialogue.executor = executor;
        dialogue.npc = npc;

        var reaction = directorGo.AddComponent<ReactionController>();
        reaction.npc = npc;
        reaction.dialogue = dialogue;

        var detector = directorGo.AddComponent<PlayerActionDetector>();
        detector.playerCamera = camera;
        detector.npc = npc;
        detector.reactions = reaction;

        executor.state = state;

        // 6) 保存为新场景（保留原 bedroom.unity 不动）
        EditorSceneManager.SaveScene(scene, OutputScene);
        AssetDatabase.Refresh();

        Debug.Log($"[Bedroom] 已生成 {OutputScene}\n" +
                  $"  锚点 {anchorList.Count} 个、边 {graph.edges.Count} 条\n" +
                  $"  Npc@(-0.95,1,-1.20) 相机@(0,1.55,-2.02) VRM={(vrm != null ? vrm.name : "<null>")}\n" +
                  $"  房间灯 {state.roomLights.Length} 盏");
    }
}
