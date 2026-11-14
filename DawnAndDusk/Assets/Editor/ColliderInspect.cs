using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 碰撞体盘点：玩家能不能走动，取决于这场景的 collider 是不是"实心大盒子"。
/// 菜单：Tools/DawnAndDusk/Inspect Colliders
/// </summary>
public static class ColliderInspect
{
    const string ScenePath = "Assets/_LocalTest/ArchViz/3D SCENE/ApartmentDemo.unity";

    [MenuItem("Tools/DawnAndDusk/Inspect Colliders")]
    public static void Inspect()
    {
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var sb = new StringBuilder();
        var cols = Object.FindObjectsOfType<Collider>();
        sb.AppendLine($"碰撞体总数 {cols.Length}");

        foreach (var group in cols.GroupBy(c => c.GetType().Name))
        {
            sb.AppendLine($"\n## {group.Key} × {group.Count()}");
            foreach (var c in group.Take(12))
            {
                sb.AppendLine($"  {c.name,-30} isTrigger={c.isTrigger} bounds={c.bounds.center} size={c.bounds.size} 层={LayerMask.LayerToName(c.gameObject.layer)}");
            }
        }

        // 卧室中心做一个射线/胶囊测试，判断是否是实心
        var p = new Vector3(3.3f, 1.0f, 2.0f);
        var hits = Physics.OverlapSphere(p, 0.3f);
        sb.AppendLine($"\n卧室中心 {p} 半径 0.3 的重叠物体 {hits.Length} 个：");
        foreach (var h in hits.Take(10))
        {
            sb.AppendLine($"  {h.name} ({h.GetType().Name})");
        }

        var down = Physics.RaycastAll(new Vector3(3.3f, 2.5f, 2.0f), Vector3.down, 3f);
        sb.AppendLine("\n向下射线命中：");
        foreach (var h in down)
        {
            sb.AppendLine($"  {h.collider.name} @ y={h.point.y:F2} ({h.collider.GetType().Name})");
        }

        Debug.Log(sb.ToString());
    }
}
