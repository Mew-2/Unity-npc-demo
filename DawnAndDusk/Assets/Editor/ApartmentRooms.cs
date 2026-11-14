using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 三个候选房间的空地评估：用"家具级渲染体"（横向尺寸 < 3m）的包围盒判断占用，
/// 排除墙/踢脚线/地板这类超大包围盒造成的假占用。
/// 菜单：Tools/DawnAndDusk/Score Rooms
/// </summary>
public static class ApartmentRooms
{
    const string ScenePath = "Assets/_LocalTest/ArchViz/3D SCENE/ApartmentDemo.unity";
    static readonly string ReportPath = Path.Combine(Application.dataPath, "../dsh-rooms-score.txt");

    [MenuItem("Tools/DawnAndDusk/Score Rooms")]
    public static void Score()
    {
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var all = Object.FindObjectsOfType<Renderer>();
        // 只看家具级：横向尺寸小于 3m，且高度在 0.1~2m 之间（排除地板/天花/墙）
        var furniture = all.Where(r =>
        {
            var s = r.bounds.size;
            return Mathf.Max(s.x, s.z) < 3f && s.y > 0.05f && r.bounds.min.y < 1.6f && r.bounds.max.y > 0.1f;
        }).Select(r => r.bounds).ToArray();

        var sb = new StringBuilder();
        sb.AppendLine($"# 房间空地评估（家具级包围盒 {furniture.Length} 个）");

        Score(sb, "主卧", 1.9f, 5.0f, 0.2f, 3.9f, furniture);
        Score(sb, "次卧", -5.6f, -2.4f, 0.6f, 4.0f, furniture);
        Score(sb, "客厅", -2.2f, 2.0f, -1.6f, 3.2f, furniture);

        File.WriteAllText(ReportPath, sb.ToString(), new UTF8Encoding(true));
        Debug.Log(sb.ToString());
    }

    static void Score(StringBuilder sb, string name, float x0, float x1, float z0, float z1, Bounds[] furniture)
    {
        const float step = 0.3f;
        var free = 0;
        var total = 0;
        var map = new StringBuilder();

        for (var z = z1; z >= z0; z -= step)
        {
            map.Append($"z={z,5:F1} ");
            for (var x = x0; x <= x1; x += step)
            {
                var p = new Vector3(x, 0.55f, z);
                var blocked = furniture.Any(b => b.Contains(p));
                map.Append(blocked ? '#' : '.');
                total++;
                if (!blocked)
                {
                    free++;
                }
            }

            map.AppendLine();
        }

        sb.AppendLine($"\n## {name}  x[{x0},{x1}] z[{z0},{z1}]  空地 {free}/{total} = {(total > 0 ? free * 100f / total : 0):F0}%");
        sb.Append(map);
    }
}
