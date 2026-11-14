using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 勘察公寓场景里主卧的家具坐标，用来定锚点位置。
/// 菜单：Tools/DawnAndDusk/Inspect Apartment
/// </summary>
public static class ApartmentInspect
{
    const string ScenePath = "Assets/_LocalTest/ArchViz/3D SCENE/AVP8_URP_Standalone.unity";
    static readonly string ReportPath = Path.Combine(Application.dataPath, "../dsh-apartment-inspect.txt");

    [MenuItem("Tools/DawnAndDusk/Inspect Apartment")]
    public static void Inspect()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var sb = new StringBuilder();
        sb.AppendLine($"# 公寓场景勘察 {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}");

        var all = Object.FindObjectsOfType<Renderer>();
        var bounds = all[0].bounds;
        foreach (var r in all)
        {
            bounds.Encapsulate(r.bounds);
        }

        sb.AppendLine($"\n整体: min={bounds.min} max={bounds.max} size={bounds.size}");
        sb.AppendLine($"渲染体总数={all.Length}  根对象={scene.GetRootGameObjects().Length}");

        sb.AppendLine("\n## 根对象");
        foreach (var root in scene.GetRootGameObjects())
        {
            var rs = root.GetComponentsInChildren<Renderer>(true);
            sb.AppendLine($"- {root.name,-32} 子渲染={rs.Length,4} active={root.activeSelf}");
        }

        // 按名字聚合（同一件家具可能有多个渲染体，比如床+被子+枕头）
        sb.AppendLine("\n## 家具/构件（按名字聚合，取第一个渲染体位置）");
        var groups = all
            .Where(r => r.name.Length > 1)
            .GroupBy(r => r.name.Trim().Split('_')[0])
            .OrderBy(g => g.Key);

        foreach (var g in groups)
        {
            var first = g.First();
            var b = first.bounds;
            sb.AppendLine($"{g.Key,-26} n={g.Count(),3}  pos={first.transform.position}  bounds中心={b.center} size={b.size}");
        }

        sb.AppendLine("\n## 目标：主卧（Bed_Bedroom）附近 6 米内的渲染体");
        var bedMaster = all.FirstOrDefault(r => r.name.IndexOf("Bed_Bedroom", System.StringComparison.OrdinalIgnoreCase) >= 0);
        var bedGuest = all.FirstOrDefault(r => r.name.IndexOf("Guestroom", System.StringComparison.OrdinalIgnoreCase) >= 0);
        sb.AppendLine($"主卧床: {(bedMaster == null ? "<null>" : $"{bedMaster.name} @ {bedMaster.transform.position} size={bedMaster.bounds.size}")}");
        sb.AppendLine($"次卧床: {(bedGuest == null ? "<null>" : $"{bedGuest.name} @ {bedGuest.transform.position} size={bedGuest.bounds.size}")}");

        if (bedMaster != null)
        {
            var c = bedMaster.bounds.center;
            var near = all.Where(r => (r.bounds.center - c).sqrMagnitude < 36f)
                .GroupBy(r => r.name.Trim().Split('_')[0])
                .OrderBy(g => (g.First().bounds.center - c).magnitude);

            foreach (var g in near)
            {
                var r = g.First();
                sb.AppendLine($"  {g.Key,-24} pos={r.transform.position} 中心={r.bounds.center} size={r.bounds.size} 距床={(r.bounds.center - c).magnitude:F2}m");
            }
        }

        File.WriteAllText(ReportPath, sb.ToString(), new UTF8Encoding(true));
        Debug.Log(sb.ToString());
    }
}
