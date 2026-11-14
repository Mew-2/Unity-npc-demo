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

    // 玩家躺在床上（床身 z 0.90~3.14 的南半部），视线高度 0.90m 更像躺着
    static readonly Vector3 PlayerPos = new Vector3(3.53f, 0.05f, 1.50f);
    // 起身落点：床的包围盒是 x 2.23~4.83 / z 0.90~3.14，原来的 PlayerPos 在床里，
    // 一起身就被 CharacterController 顶到床面上，眼睛高过门框。改到门口空地。
    static readonly Vector3 StandUpPos = new Vector3(2.05f, 0.05f, 0.55f);
    static readonly Vector3 PlayerEuler = new Vector3(-2f, 0f, 0f);

    /// <summary>实验开关：把床整组藏掉（含床架），验证"她过不来"到底是床挡的还是逻辑问题。</summary>
    // 2026-11-15 03:40：床与椅子都还原（false），观察她会不会被椅子/床脚挡住
    const bool HideBedForTest = false;

    /// <summary>目标身高（米）：玩家 175cm → 站立视线 163cm（0.93×身高）；鲸鲸 162cm。</summary>
    const float PlayerEyeHeight = 1.63f;
    const float NpcHeight = 1.62f;

    struct AnchorSpec
    {
        public string id;
        public string name;
        public string desc;
        public Vector3 pos;
    }

    static readonly AnchorSpec[] Specs =
    {
    // 锚点全部放在"窗带"（床北侧 z≈3.2~3.5 的那条带，宽约 0.5m、长 2m）——
    // 绕床走位在物理上不成立（家具比房间还宽），所以改用"她本来就在窗前"的设定
        new AnchorSpec { id = "front",  name = "床沿",   desc = "你头侧的床沿，探身说话的位置",   pos = new Vector3(3.53f, 0f, 3.20f) },
        new AnchorSpec { id = "bed",    name = "床边",   desc = "床侧的空地",                     pos = new Vector3(2.85f, 0f, 3.35f) },
        new AnchorSpec { id = "desk",   name = "书桌旁", desc = "书桌旁的空地（离桌沿 0.4m）",     pos = new Vector3(2.50f, 0f, 3.50f) },
        new AnchorSpec { id = "window", name = "窗前",   desc = "落地窗中间",                     pos = new Vector3(3.20f, 0f, 3.45f) },
        // 去门口的中转点：放在空地中间，离书桌东沿(2.03)和床沿(2.23)都留出 0.4m 以上
        new AnchorSpec { id = "west",   name = "房中",   desc = "房间中部的空地",                 pos = new Vector3(2.60f, 0f, 1.70f) },
        new AnchorSpec { id = "door",   name = "门口",   desc = "主卧门，西墙靠南",               pos = new Vector3(2.20f, 0f, 0.70f) },
    };

    static readonly (string a, string b)[] Edges =
    {
        ("desk", "bed"), ("bed", "window"), ("window", "front"),
        ("desk", "west"), ("west", "door"),
    };

    /// <summary>
    /// 把主卧里挡路的物件"摆回去"，而不是删掉：
    ///   椅子 → 收进桌下；竹篮 → 也收到桌下；掉在地上的靠垫 → 放回床上。
    /// 只动位置，不动原资源；场景重新生成时会重复执行，所以每步都按"目标点"而不是"相对偏移"。
    /// </summary>
    static void ArrangeBedroomProps()
    {
        // 2026-11-15 03:55：床、椅子、地面靠垫全部还原，场景不再做任何"收起"处理。
        // 保留这段结构是为了以后还能按名字临时收起某个物件（填进来即可）。
        var hide = new string[0];
        var hidden = 0;
        foreach (var t in Object.FindObjectsOfType<Transform>())
        {
            if (!t.gameObject.activeInHierarchy || !InBedroom(t))
            {
                continue;
            }

            if (hide.Any(h => t.name.StartsWith(h, System.StringComparison.OrdinalIgnoreCase)))
            {
                t.gameObject.SetActive(false);
                hidden++;
            }
        }

        Debug.Log($"[Apartment] 收起地面靠垫：{hidden} 个");

        if (HideBedForTest)
        {
            // 只关"床体本身"：名字含 Bed（排除 Bedside），且包围盒横向跨度小于 2.8m（地板/天花板跨度更大）、
            // 竖向范围落在床铺高度内——之前用地板中心判据把整块地板都关掉了
            var bedBox = new Bounds(new Vector3(3.5f, 0.5f, 2.0f), new Vector3(2.8f, 1.6f, 2.5f));
            // Chair 也要排除：椅子高度 0.9m、中心常落在床铺包围盒里，之前被这条"床体"判据连带关掉了渲染器
            var skipWords = new[] { "Floor", "Ceiling", "Skirting", "Carpet", "Rug", "Wall", "Chair" };
            var hiddenRenderers = 0;
            var hiddenNames = new System.Collections.Generic.List<string>();
            foreach (var r in Object.FindObjectsOfType<Renderer>().Where(r => r.enabled))
            {
                if (!InBedroom(r.transform))
                {
                    continue;
                }

                var name = r.name;
                if (skipWords.Any(w => name.IndexOf(w, System.StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    continue;
                }

                var b = r.bounds;
                var byName = name.IndexOf("Bed", System.StringComparison.OrdinalIgnoreCase) >= 0
                             && name.IndexOf("Bedside", System.StringComparison.OrdinalIgnoreCase) < 0;
                var byBounds = bedBox.Contains(b.center)
                               && Mathf.Max(b.size.x, b.size.z) < 2.8f
                               && b.max.y > 0.3f;

                if (byName || byBounds)
                {
                    r.enabled = false;
                    hiddenRenderers++;
                    if (hiddenNames.Count < 12)
                    {
                        hiddenNames.Add(name);
                    }
                }
            }

            // 只关"床自己"的碰撞体：地板碰撞体是零厚度的薄片，用体积条件排除掉
            var hiddenColliders = 0;
            foreach (var c in Object.FindObjectsOfType<Collider>().Where(c => c.enabled))
            {
                if (!InBedroom(c.transform))
                {
                    continue;
                }

                if (c.name.IndexOf("Chair", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    continue;
                }

                var b = c.bounds;
                if (bedBox.Contains(b.center) && b.size.y > 0.2f && Mathf.Max(b.size.x, b.size.z) < 2.8f)
                {
                    c.enabled = false;
                    hiddenColliders++;
                }
            }

            Debug.LogWarning($"[Apartment] 实验模式：床已隐藏（{hiddenRenderers} 个渲染器 / {hiddenColliders} 个碰撞体）\n" +
                             $"  被关掉的渲染器：{string.Join(", ", hiddenNames)}");
        }
    }

    /// <summary>向上三层查父级名字里有没有 BEDROOM（家具的层级是 ..._LOD &lt; BEDROOM）。</summary>
    static bool InBedroom(Transform t)
    {
        for (var i = 0; i < 4 && t != null; i++)
        {
            if (t.name.IndexOf("BEDROOM", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            t = t.parent;
        }

        return false;
    }

    [MenuItem("Tools/DawnAndDusk/Build Apartment Demo")]
    public static void Build()
    {
        var scene = EditorSceneManager.OpenScene(SourceScene, OpenSceneMode.Single);

        ArrangeBedroomProps();

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

        // 资材包自带的演示脚本（CameraZoom / Trigger_Volume / Controller_Switch 等）依赖它自己的相机与 UI，
        // 在我们的场景里会每帧抛 NullReferenceException —— 全部关掉
        var disabledPackScripts = 0;
        foreach (var behaviour in Object.FindObjectsOfType<MonoBehaviour>())
        {
            var typeName = behaviour.GetType().Name;
            if (typeName == "CameraZoom" || typeName == "FirstPersonAIO" || typeName == "Trigger_Volume"
                || typeName == "Controller_Switch" || typeName == "rotateonmouse")
            {
                behaviour.enabled = false;
                disabledPackScripts++;
            }
        }

        Debug.Log($"[Apartment] 关闭资材包演示脚本：{disabledPackScripts} 个");

        var camera = Object.FindObjectsOfType<Camera>().OrderByDescending(c => c.depth).FirstOrDefault();
        if (camera == null)
        {
            var go = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            camera = go.GetComponent<Camera>();
        }

        // 玩家本体：CharacterController 负责碰撞，相机挂成子物体
        var playerGo = new GameObject("Player");
        playerGo.transform.position = new Vector3(3.30f, 0.05f, 0.62f);
        playerGo.transform.rotation = Quaternion.identity;

        var controller = playerGo.AddComponent<CharacterController>();
        controller.height = 1.62f;
        controller.radius = 0.24f;
        controller.center = new Vector3(0f, 0.81f, 0f);
        controller.skinWidth = 0.02f;

        camera.transform.SetParent(playerGo.transform, false);
        camera.transform.localPosition = new Vector3(0f, 1.00f, 0f);
        camera.transform.localRotation = Quaternion.Euler(-1f, 0f, 0f);
        camera.nearClipPlane = 0.05f;
        camera.farClipPlane = 60f;
        camera.tag = "MainCamera";

        var look = camera.GetComponent<PlayerLook>() ?? camera.gameObject.AddComponent<PlayerLook>();
        look.yawRoot = playerGo.transform;

        var player = playerGo.AddComponent<PlayerController>();
        player.cameraTransform = camera.transform;
        player.startSeated = true;
        player.allowMovement = true;
        player.seatedHeight = 0.90f;
        player.standingHeight = PlayerEyeHeight;
        // 床已还原：不能再"原地起身"（PlayerPos 在床的包围盒里，会被顶到床面上）
        player.standUpPosition = StandUpPos;

        // 2) 玩法根
        var root = new GameObject("Game");
        var stamp = root.AddComponent<SceneStamp>();
        stamp.buildId = System.DateTime.Now.ToString("MMdd-HHmmss");

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

        // 连线净空检查：走过椅子/桌子/床会表现为"卡一下然后穿过去"
        var furniture = Object.FindObjectsOfType<Renderer>()
            .Where(r =>
            {
                var s = r.bounds.size;
                return r.enabled && r.gameObject.activeInHierarchy
                       && Mathf.Max(s.x, s.z) < 3f && s.y > 0.15f && r.bounds.min.y < 1.5f && r.bounds.max.y > 0.15f;
            })
            .ToArray();

        foreach (var edge in Edges)
        {
            var from = Specs.First(s => s.id == edge.a).pos;
            var to = Specs.First(s => s.id == edge.b).pos;
            var steps = Mathf.CeilToInt(Vector3.Distance(from, to) / 0.15f);
            string blocker = null;

            for (var i = 1; i < steps; i++)
            {
                var p = Vector3.Lerp(from, to, i / (float)steps);
                var probe = new Vector3(p.x, 0.55f, p.z);
                foreach (var r in furniture)
                {
                    if (r.bounds.Contains(probe))
                    {
                        blocker = r.name;
                        break;
                    }
                }

                if (blocker != null)
                {
                    break;
                }
            }

            if (blocker != null)
            {
                Debug.LogWarning($"[Apartment] 连线 {edge.a} -> {edge.b} 穿过 '{blocker}'（会被挡/穿模）");
            }
            else
            {
                Debug.Log($"[Apartment] 连线 {edge.a} -> {edge.b} 净空 OK");
            }
        }

        // 3) NPC
        var npcGo = new GameObject("Npc");
        npcGo.transform.SetParent(root.transform, false);
        npcGo.transform.position = new Vector3(2.55f, 1f, 3.30f);
        var npc = npcGo.AddComponent<NpcAgent>();
        npc.graph = graph;
        npc.moveSpeed = 1.03f;
        npc.accelTime = 0.6f;
        npc.decelTime = 0.3f;
        npc.groundOffset = 1f;
        npc.autoPivot = false;

        // 栅格寻路：扫描这间房里的家具，按她半宽 0.32m 膨胀
        var grid = npcGo.AddComponent<GridPathfinder>();
        grid.min = new Vector2(1.42f, 0.05f);
        grid.max = new Vector2(4.95f, 3.72f);
        grid.cell = 0.10f;
        grid.agentRadius = 0.30f;
        npc.pathfinder = grid;

        // 点击判定需要 collider（PlayerActionDetector 靠 Raycast 命中她）；
        // Npc 根在 y=1，脚底在 y=0，所以胶囊中心要下移 1m 再取一半身高
        var npcCollider = npcGo.AddComponent<CapsuleCollider>();
        npcCollider.center = new Vector3(0f, -0.19f, 0f);
        npcCollider.height = 1.62f;
        npcCollider.radius = 0.22f;
        npcCollider.direction = 1;

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
        var npcScale = 1f;
        if (vrm != null)
        {
            // 按模型真实高度换算缩放，目标 162cm
            var probe = Object.Instantiate(vrm);
            var renderers = probe.GetComponentsInChildren<Renderer>();
            if (renderers.Length > 0)
            {
                var b = renderers[0].bounds;
                foreach (var r in renderers)
                {
                    b.Encapsulate(r.bounds);
                }

                var rawHeight = b.size.y;
                npcScale = rawHeight > 0.1f ? NpcHeight / rawHeight : 1f;
                Debug.Log($"[Apartment] VRM 原始高度 {rawHeight:F2}m → 缩放 {npcScale:F3}（目标 {NpcHeight:F2}m）");
            }

            Object.DestroyImmediate(probe);
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
                visualScale = npcScale,
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
        var seatedEye = new Vector3(PlayerPos.x, 1.02f, PlayerPos.z);
        Render(camera, seatedEye, new Vector3(-2f, 0f, 0f), "01-player-view.png");

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
                Render(camera, seatedEye, new Vector3(-4f, 0f, 0f), $"02-npc-at-{id}.png");
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
