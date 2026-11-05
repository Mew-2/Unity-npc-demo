using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 一键生成卧室灰盒 + 7 个语义锚点 + 锚点图 + 相机 + NPC 占位。
/// 菜单：Tools/DawnAndDusk/Build Greybox
/// 只用方块，方便后面替换美术；锚点位置按 SPEC §1。
/// </summary>
public static class GreyboxBuilder
{
    [MenuItem("Tools/DawnAndDusk/Build Greybox")]
    public static void Build()
    {
        if (GameObject.Find("Greybox") != null)
        {
            Debug.LogWarning("[Greybox] 场景里已有 Greybox，先手动删掉再重建。");
            return;
        }

        var root = new GameObject("Greybox");

        var room = new GameObject("Room").transform;
        room.SetParent(root.transform);

        CreateBox(room, "Floor", new Vector3(0f, -0.05f, 0f), new Vector3(5f, 0.1f, 6f));
        CreateBox(room, "WallNorth", new Vector3(0f, 1.4f, 3f), new Vector3(5f, 2.8f, 0.1f));
        CreateBox(room, "WallSouth", new Vector3(0f, 1.4f, -3f), new Vector3(5f, 2.8f, 0.1f));
        CreateBox(room, "WallEast", new Vector3(2.5f, 1.4f, 0f), new Vector3(0.1f, 2.8f, 6f));
        CreateBox(room, "WallWest", new Vector3(-2.5f, 1.4f, 0f), new Vector3(0.1f, 2.8f, 6f));

        var graphGo = new GameObject("AnchorGraph");
        graphGo.transform.SetParent(root.transform);
        var graph = graphGo.AddComponent<AnchorGraph>();

        var bed = AddAnchor(graph, root.transform, "bed", "床边", "大床，被子叠着，床头一盏灯（女仆默认位）", new Vector3(0f, 0f, 2f));
        AddAnchor(graph, root.transform, "window", "窗边", "落地窗，夜里有月亮，清晨有阳光", new Vector3(-1.8f, 0f, 2.3f));
        AddAnchor(graph, root.transform, "screen", "屏风", "三折屏风，换衣服的地方", new Vector3(1.9f, 0f, 0.8f));
        AddAnchor(graph, root.transform, "desk", "梳妆台", "镜子、梳子，台灯是主光源", new Vector3(1.9f, 0f, -1.2f));
        AddAnchor(graph, root.transform, "door", "门口", "房门，门外是走廊", new Vector3(0f, 0f, -2.3f));
        AddAnchor(graph, root.transform, "lamp", "台灯", "床头灯，可开可关", new Vector3(0.8f, 0f, 2.2f));
        AddAnchor(graph, root.transform, "player", "你", "玩家位置（伪锚点，只作 look_at 目标）", new Vector3(0f, 1.6f, 0f));

        graph.edges = new List<AnchorGraph.Edge>
        {
            new AnchorGraph.Edge { a = "door", b = "bed" },
            new AnchorGraph.Edge { a = "bed", b = "window" },
            new AnchorGraph.Edge { a = "bed", b = "screen" },
            new AnchorGraph.Edge { a = "screen", b = "desk" }
        };

        if (Camera.main == null)
        {
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(root.transform);
            camGo.transform.position = new Vector3(0f, 1.6f, -2.2f);
            camGo.transform.rotation = Quaternion.Euler(5f, 0f, 0f);
            camGo.AddComponent<Camera>();
            if (Object.FindObjectOfType<AudioListener>() == null)
            {
                camGo.AddComponent<AudioListener>();
            }
        }

        var npc = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        npc.name = "Npc";
        npc.transform.SetParent(root.transform);
        npc.transform.position = bed.transform.position + Vector3.up;

        var agent = npc.AddComponent<NpcAgent>();
        agent.graph = graph;

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Selection.activeGameObject = root;
        Debug.Log("[Greybox] 完成：房间 + 7 个锚点 + 锚点图 + 相机 + NPC 占位。");
    }

    static void CreateBox(Transform parent, string name, Vector3 position, Vector3 scale)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent);
        go.transform.position = position;
        go.transform.localScale = scale;
    }

    static Anchor AddAnchor(AnchorGraph graph, Transform parent, string id, string displayName, string description, Vector3 position)
    {
        var go = new GameObject("Anchor_" + id);
        go.transform.SetParent(parent);
        go.transform.position = position;

        var anchor = go.AddComponent<Anchor>();
        anchor.id = id;
        anchor.displayName = displayName;
        anchor.description = description;
        graph.anchors.Add(anchor);
        return anchor;
    }
}
