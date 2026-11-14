using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 从 ArchViz 公寓场景生成 demo 场景：主卧为主舞台。
/// 产出 Assets/_LocalTest/ArchViz/3D SCENE/ApartmentDemo.unity（_LocalTest 已 gitignore）。
///
/// 菜单：Tools/DawnAndDusk/Build Apartment Demo（顺带渲染两张预览图）
/// </summary>
public static class ApartmentSceneBuilder
{
    const string SourceScene = "Assets/_LocalTest/ArchViz/3D SCENE/AVP8_URP_Standalone.unity";
    const string OutputScene = "Assets/_LocalTest/ArchViz/3D SCENE/ApartmentDemo.unity";
    const string VrmPath = "Assets/Characters/DeepSeekChan/DeepSeek-chan.vrm";
    static readonly string OutDir = Path.Combine(Application.dataPath, "../dsh-apartment-shots");

    // 主卧空地是绕床的 U 形：南条带 z0.3~0.9、西条带 x1.9~2.2、北条带 z3.3~3.9
    // 机位放在床上（躺着醒来的视角），鲸鲸站在窗边/床脚，中间隔着床——正好是"侍寝/唤醒"的构图
    static readonly Vector3 PlayerPos = new Vector3(3.30f, 1.32f, 1.15f);
    static readonly Vector3 PlayerEuler = new Vector3(8f, 12f, 0f);

    struct AnchorSpec
    {
        public string id;
        public string name;
        public string desc;
        public Vector3 pos;
    }

    static readonly AnchorSpec[] Specs =
    {
        new AnchorSpec { id = "front",  name = "身前",   desc = "你面前，近距离说话的位置",       pos = new Vector3(3.35f, 0f, 0.70f) },
        new AnchorSpec { id = "bed",    name = "床边",   desc = "铺着灰色被子的双人床",           pos = new Vector3(2.05f, 0f, 1.70f) },
        new AnchorSpec { id = "desk",   name = "书桌边", desc = "靠西墙的书桌和一排书",           pos = new Vector3(2.05f, 0f, 2.70f) },
        new AnchorSpec { id = "window", name = "窗边",   desc = "北面整墙的落地窗，挂着深色窗帘", pos = new Vector3(3.65f, 0f, 3.55f) },
        new AnchorSpec { id = "door",   name = "门口",   desc = "通往走廊的门",                   pos = new Vector3(2.30f, 0f, 0.55f) },
    };

    static readonly (string a, string b)[] Edges =
    {
        ("door", "front"), ("door", "bed"), ("bed", "desk"), ("desk", "window"),
    };

    [MenuItem("Tools/DawnAndDusk/Build Apartment Demo")]
    public static void Build()
    {
        var scene = EditorSceneManager.OpenScene(SourceScene, OpenSceneMode.Single);

        // 0) 压掉后处理里过强的色散（录屏时边缘会发彩虹边）
        foreach (var volume in Object.FindObjectsOfType<UnityEngine.Rendering.Volume>())
        {
            var profile = volume.sharedProfile;
            if (profile != null && profile.TryGet<UnityEngine.Rendering.Universal.ChromaticAberration>(out var ca))
            {
                ca.intensity.Override(0.05f);
            }
        }

        // 1) 处理场景自带的第一人称控制器：保留相机，去掉控制脚本（我们的相机由玩家位置固定）
        var controllers = scene.GetRootGameObjects()
            .FirstOrDefault(r => r.name.Contains("CONTROLLER"));
        if (controllers != null)
        {
            foreach (var behaviour in controllers.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour != null && behaviour.GetType().Name.Contains("FirstPerson"))
                {
                    Object.DestroyImmediate(behaviour);
                }
            }
        }

        var camera = Object.FindObjectsOfType<Camera>().OrderByDescending(c => c.depth).FirstOrDefault();
        if (camera == null)
        {
            var go = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            camera = go.GetComponent<Camera>();
        }

        camera.transform.position = PlayerPos;
        camera.transform.rotation = Quaternion.Euler(PlayerEuler);
        camera.nearClipPlane = 0.05f;
        camera.farClipPlane = 60f;
        camera.tag = "MainCamera";
        if (camera.GetComponent<PlayerLook>() == null)
        {
            camera.gameObject.AddComponent<PlayerLook>();
        }

        // 2) 玩法根
        var root = new GameObject("Game");

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
            var a = go.AddComponent<Anchor>();
            a.id = spec.id;
            a.displayName = spec.name;
            a.description = spec.desc;
            anchorList.Add(a);
        }

        graph.anchors = anchorList;
        graph.edges = Edges.Select(e => new AnchorGraph.Edge { a = e.a, b = e.b }).ToList();

        // 自检：锚点不能落在床上（之前 front 就踩进去过）
        var bedBounds = new Bounds();
        var hasBed = false;
        foreach (var r in Object.FindObjectsOfType<Renderer>()
                     .Where(r => r.name.IndexOf("Bed_Bedroom", System.StringComparison.OrdinalIgnoreCase) >= 0))
        {
            if (!hasBed)
            {
                bedBounds = r.bounds;
                hasBed = true;
            }
            else
            {
                bedBounds.Encapsulate(r.bounds);
            }
        }

        if (hasBed)
        {
            foreach (var spec in Specs)
            {
                if (bedBounds.Contains(new Vector3(spec.pos.x, 0.5f, spec.pos.z)))
                {
                    Debug.LogError($"[Apartment] 锚点 {spec.id} @ {spec.pos} 落在床的包围盒里（床 {bedBounds.min}~{bedBounds.max}）");
                }
            }
        }

        // 3) NPC
        var npcGo = new GameObject("Npc");
        npcGo.transform.SetParent(root.transform, false);
        npcGo.transform.position = new Vector3(2.20f, 1f, 1.70f);
        var npc = npcGo.AddComponent<NpcAgent>();
        npc.graph = graph;
        npc.moveSpeed = 1.03f;
        npc.accelTime = 0.6f;
        npc.decelTime = 0.3f;
        npc.groundOffset = 1f;
        npc.autoPivot = false;

        // 4) Director
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
            .Where(l => l.type != LightType.Directional)
            .Take(8)
            .ToArray();
        state.curtain = Object.FindObjectsOfType<Transform>()
            .FirstOrDefault(t => t.name.StartsWith("Curtain") && t.position.x > 2f && t.position.z > 3.4f);

        var switcher = directorGo.AddComponent<CharacterSwitcher>();
        switcher.npc = npc;
        switcher.client = client;

        var vrm = AssetDatabase.LoadAssetAtPath<GameObject>(VrmPath);
        switcher.profiles = new List<CharacterProfile>
        {
            new CharacterProfile
            {
                id = "whale",
                displayName = "鲸鲸",
                personaId = "whale",
                visualPrefab = vrm,
                visualOffset = new Vector3(0f, -1f, 0f),
                visualScale = 0.82f,
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

        EditorSceneManager.SaveScene(scene, OutputScene);
        AssetDatabase.Refresh();

        Debug.Log($"[Apartment] 已生成 {OutputScene}\n" +
                  $"  锚点={anchorList.Count} 边={graph.edges.Count} VRM={(vrm != null ? vrm.name : "<null>")}\n" +
                  $"  窗帘引用={(state.curtain != null ? state.curtain.name : "<null>")} 灯={state.roomLights.Length} 盏");

        Shoot(camera);
    }

    static void Shoot(Camera camera)
    {
        Directory.CreateDirectory(OutDir);
        Render(camera, PlayerPos, PlayerEuler, "01-player-view.png");

        // 把鲸鲸临时摆到锚点，验证构图（VRM 枢轴在脚底，直接放 y=0）
        var vrm = AssetDatabase.LoadAssetAtPath<GameObject>(VrmPath);
        GameObject standIn = null;
        if (vrm != null)
        {
            foreach (var id in new[] { "front", "window" })
            {
                var spec = Specs.First(s => s.id == id);
                standIn = Object.Instantiate(vrm);
                standIn.transform.position = new Vector3(spec.pos.x, 0f, spec.pos.z);
                standIn.transform.localScale = Vector3.one * 0.82f;
                standIn.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
                Render(camera, PlayerPos, PlayerEuler, $"02-npc-at-{id}.png");
                Object.DestroyImmediate(standIn);
            }
        }

        Render(camera, new Vector3(2.6f, 2.3f, 0.5f), new Vector3(18f, 28f, 0f), "04-room-wide.png");
        Render(camera, new Vector3(3.6f, 1.6f, 3.1f), new Vector3(2f, 195f, 0f), "05-window-wall.png");
        Debug.Log("[Apartment] 预览图在 " + OutDir);
    }

    static void Render(Camera cam, Vector3 pos, Vector3 euler, string file)
    {
        const int w = 1600;
        const int h = 900;
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        var texture = new Texture2D(w, h, TextureFormat.RGB24, false);

        cam.transform.position = pos;
        cam.transform.rotation = Quaternion.Euler(euler);
        cam.targetTexture = rt;
        cam.Render();

        RenderTexture.active = rt;
        texture.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        texture.Apply();
        RenderTexture.active = null;
        cam.targetTexture = null;

        File.WriteAllBytes(Path.Combine(OutDir, file), texture.EncodeToPNG());
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(texture);
    }
}
