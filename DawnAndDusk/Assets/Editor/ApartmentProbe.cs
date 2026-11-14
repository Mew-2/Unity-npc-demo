using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 主卧空间探测：床/窗/门的精确包围盒 + 一间房的"可站立"栅格图（ASCII），
/// 用来把锚点放在真正站得住的地板上，而不是床里。
/// 菜单：Tools/DawnAndDusk/Probe Master Bedroom
/// </summary>
public static class ApartmentProbe
{
    const string ScenePath = "Assets/_LocalTest/ArchViz/3D SCENE/ApartmentDemo.unity";
    static readonly string ReportPath = Path.Combine(Application.dataPath, "../dsh-bedroom-probe.txt");

    [MenuItem("Tools/DawnAndDusk/Probe Master Bedroom")]
    public static void Probe()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var sb = new StringBuilder();
        var all = Object.FindObjectsOfType<Renderer>();

        Bounds? Union(string keyword, System.Func<Renderer, bool> filter = null)
        {
            var hits = all.Where(r => r.name.IndexOf(keyword, System.StringComparison.OrdinalIgnoreCase) >= 0
                                      && (filter == null || filter(r))).ToArray();
            if (hits.Length == 0)
            {
                return null;
            }

            var b = hits[0].bounds;
            foreach (var r in hits)
            {
                b.Encapsulate(r.bounds);
            }

            return b;
        }

        var bed = Union("Bed_Bedroom");
        var curtains = Union("Curtain", r => r.transform.position.x > 2f && r.transform.position.z > 3.2f);
        var door = Union("Door", r => r.transform.position.x > 1.5f && r.transform.position.x < 3.5f && r.transform.position.z > -0.5f && r.transform.position.z < 2f);
        var bedside = Union("Bedside", r => r.transform.position.x > 3f);
        var desk = Union("Desk", r => r.transform.position.x > 1.5f);

        sb.AppendLine("# 主卧探测");
        sb.AppendLine($"床: {(bed.HasValue ? $"min={bed.Value.min} max={bed.Value.max} center={bed.Value.center}" : "无")}");
        sb.AppendLine($"窗帘: {(curtains.HasValue ? $"min={curtains.Value.min} max={curtains.Value.max}" : "无")}");
        sb.AppendLine($"门: {(door.HasValue ? $"center={door.Value.center} size={door.Value.size}" : "无")}");
        sb.AppendLine($"床头柜: {(bedside.HasValue ? $"center={bedside.Value.center}" : "无")}");
        sb.AppendLine($"书桌: {(desk.HasValue ? $"center={desk.Value.center} size={desk.Value.size}" : "无")}");

        // 可站立栅格：按"渲染体包围盒是否覆盖该点"判断占用（这场景有整层碰撞体，物理检测不可用）
        const float x0 = 1.6f, x1 = 5.8f, z0 = -0.2f, z1 = 4.0f, step = 0.2f;
        var occupied = all.Where(r => r.bounds.size.y > 0.12f).Select(r => r.bounds).ToArray();
        sb.AppendLine($"\n## 可站立栅格（x {x0}~{x1}, z {z0}~{z1}，步长 {step}；'.'=空，'#'=有家具，'C'=帘，'B'=床）");

        for (var z = z1; z >= z0; z -= step)
        {
            var line = new StringBuilder();
            line.Append($"z={z,5:F2} ");
            for (var x = x0; x <= x1; x += step)
            {
                var probeLow = new Vector3(x, 0.45f, z);
                var probeHigh = new Vector3(x, 1.05f, z);

                var blockedLow = occupied.Any(b => b.Contains(probeLow));
                var blockedHigh = occupied.Any(b => b.Contains(probeHigh));
                var bedHit = bed.HasValue && bed.Value.Contains(new Vector3(x, 0.45f, z));
                var curtainHit = curtains.HasValue && curtains.Value.Contains(new Vector3(x, 1.2f, z));

                line.Append(bedHit ? 'B' : (curtainHit ? 'C' : (blockedLow || blockedHigh ? '#' : '.')));
            }

            sb.AppendLine(line.ToString());
        }

        File.WriteAllText(ReportPath, sb.ToString(), new UTF8Encoding(true));
        Debug.Log(sb.ToString());
    }
}
