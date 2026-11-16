using System.Linq;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 先验证：给公寓烘一张 NavMesh，并测试床边/门口/窗边之间是否连通。
/// 菜单：Tools/DawnAndDusk/Bake House NavMesh
/// </summary>
public static class NavMeshBakeSetup
{
    const int AgentTypeId = 0;

    [MenuItem("Tools/DawnAndDusk/Bake House NavMesh")]
    public static void Bake()
    {
        var surface = Object.FindObjectsOfType<NavMeshSurface>().FirstOrDefault();
        if (surface == null)
        {
            var go = new GameObject("NavMeshSurface");
            surface = go.AddComponent<NavMeshSurface>();
            Undo.RegisterCreatedObjectUndo(go, "Create NavMeshSurface");
        }

        surface.collectObjects = CollectObjects.All;
        surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
        surface.layerMask = ~0;
        surface.agentTypeID = AgentTypeId;

        // 不让 NPC / 玩家自己的碰撞体烘进导航网格
        IgnoreFromBuild("Npc");
        IgnoreFromBuild("Player");

        surface.BuildNavMesh();
        EditorSceneManager.MarkSceneDirty(surface.gameObject.scene);

        var tri = NavMesh.CalculateTriangulation();
        var triangles = tri.indices.Length / 3;
        Debug.Log($"[NavMesh] baked: vertices={tri.vertices.Length} triangles={triangles}");

        TestPair("bed", "door");
        TestPair("bed", "window");
        TestPair("door", "window");
    }

    static void IgnoreFromBuild(string objectName)
    {
        var go = GameObject.Find(objectName);
        if (go == null)
        {
            return;
        }

        var modifier = go.GetComponent<NavMeshModifier>();
        if (modifier == null)
        {
            modifier = go.AddComponent<NavMeshModifier>();
        }

        modifier.ignoreFromBuild = true;
    }

    static void TestPair(string fromId, string toId)
    {
        var anchors = Object.FindObjectsOfType<Anchor>();
        var from = anchors.FirstOrDefault(a => a.id == fromId);
        var to = anchors.FirstOrDefault(a => a.id == toId);
        if (from == null || to == null)
        {
            Debug.LogWarning($"[NavMesh] 找不到锚点：{fromId} / {toId}");
            return;
        }

        var path = new NavMeshPath();
        var ok = NavMesh.CalculatePath(from.transform.position, to.transform.position, NavMesh.AllAreas, path);
        var length = 0f;
        for (var i = 1; i < path.corners.Length; i++)
        {
            length += Vector3.Distance(path.corners[i - 1], path.corners[i]);
        }

        Debug.Log($"[NavMesh] {fromId} -> {toId}: ok={ok} status={path.status} corners={path.corners.Length} length={length:F2}m");
    }
}
