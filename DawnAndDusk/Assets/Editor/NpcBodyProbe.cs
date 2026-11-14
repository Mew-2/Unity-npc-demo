using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 量鲸鲸的"体积"：模型包围盒 vs 身上所有 Collider 的包围盒，
/// 判断是不是碰撞体积明显大于模型（"胖了"）。
/// 菜单：Tools/DawnAndDusk/Measure Npc Body
/// </summary>
public static class NpcBodyProbe
{
    const string ScenePath = "Assets/_LocalTest/ArchViz/3D SCENE/ApartmentDemo.unity";
    const string VrmPath = "Assets/Characters/DeepSeekChan/DeepSeek-chan.vrm";

    [MenuItem("Tools/DawnAndDusk/Measure Npc Body")]
    public static void Measure()
    {
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var vrm = AssetDatabase.LoadAssetAtPath<GameObject>(VrmPath);
        if (vrm == null)
        {
            Debug.LogError("找不到 VRM");
            return;
        }

        // 按真实玩法摆：Npc 根在 (2.05, 1, 1.35)，Visual 是子物体且带 -1 偏移、0.872 缩放
        var root = new GameObject("ProbeNpc");
        root.transform.position = new Vector3(2.05f, 1f, 1.35f);

        var visual = Object.Instantiate(vrm, root.transform);
        visual.transform.localPosition = new Vector3(0f, -1f, 0f);
        visual.transform.localScale = Vector3.one * 0.872f;

        var sb = new StringBuilder();
        sb.AppendLine("# 鲸鲸体积测量");

        var renderers = visual.GetComponentsInChildren<Renderer>();
        var b = renderers[0].bounds;
        foreach (var r in renderers)
        {
            b.Encapsulate(r.bounds);
        }

        sb.AppendLine($"模型包围盒: center={b.center} size={b.size}  （宽 {b.size.x:F2} × 高 {b.size.y:F2} × 厚 {b.size.z:F2}）");

        var colliders = visual.GetComponentsInChildren<Collider>();
        sb.AppendLine($"\n身上 Collider 共 {colliders.Length} 个");
        if (colliders.Length > 0)
        {
            var cb = colliders[0].bounds;
            foreach (var c in colliders)
            {
                cb.Encapsulate(c.bounds);
            }

            sb.AppendLine($"Collider 合并包围盒: center={cb.center} size={cb.size}");
            foreach (var g in colliders.GroupBy(c => c.GetType().Name))
            {
                sb.AppendLine($"  {g.Key} × {g.Count()}");
            }

            foreach (var c in colliders.OrderByDescending(c => c.bounds.size.magnitude).Take(8))
            {
                sb.AppendLine($"  最大: {c.name,-22} {c.GetType().Name,-18} 世界半径≈{Mathf.Max(c.bounds.extents.x, c.bounds.extents.z):F3}m  center={c.bounds.center}");
            }
        }

        sb.AppendLine("\n对比：角色控制器常用半径 0.22~0.25m；若 Collider 半径接近这个量级，说明胖的是碰撞体不是模型");

        Object.DestroyImmediate(root);
        Debug.Log(sb.ToString());
    }
}
