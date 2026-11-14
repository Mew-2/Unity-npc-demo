using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 主卧地面杂物 + 书桌椅子盘查：找出"摆在地上挡路"的小物件，以及书桌整组的层级。
/// 菜单：Tools/DawnAndDusk/Probe Clutter
/// </summary>
public static class ClutterProbe
{
    const string ScenePath = "Assets/_LocalTest/ArchViz/3D SCENE/ApartmentDemo.unity";

    [MenuItem("Tools/DawnAndDusk/Probe Clutter")]
    public static void Probe()
    {
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var sb = new StringBuilder();
        var area = new Bounds(new Vector3(3.4f, 1.2f, 1.95f), new Vector3(3.6f, 2.4f, 3.9f));

        var renderers = Object.FindObjectsOfType<Renderer>()
            .Where(r => area.Contains(r.bounds.center))
            .OrderBy(r => r.bounds.min.y)
            .ToArray();

        sb.AppendLine("# 主卧物件（按最低点高度排序）");
        foreach (var r in renderers)
        {
            var chain = new StringBuilder();
            var t = r.transform;
            for (var i = 0; i < 4 && t != null; i++)
            {
                chain.Append(t.name).Append(i < 3 ? " < " : string.Empty);
                t = t.parent;
            }

            sb.AppendLine($"y={r.bounds.min.y,6:F2} ~ {r.bounds.max.y,5:F2}  x[{r.bounds.min.x,5:F2},{r.bounds.max.x,5:F2}] z[{r.bounds.min.z,5:F2},{r.bounds.max.z,5:F2}]  {r.name,-26} {chain}");
        }

        // 桌子/椅子/镜子这类"可搬"整组
        sb.AppendLine("\n# 含 Desk / Chair / Mirror / Table 的根对象");
        foreach (var root in Object.FindObjectsOfType<Transform>())
        {
            if (!root.gameObject.activeInHierarchy)
            {
                continue;
            }

            var n = root.name.ToLowerInvariant();
            if ((n.Contains("desk") || n.Contains("chair") || n.Contains("mirror")) && root.parent != null)
            {
                var rs = root.GetComponentsInChildren<Renderer>();
                if (rs.Length == 0)
                {
                    continue;
                }

                var b = rs[0].bounds;
                foreach (var r in rs)
                {
                    b.Encapsulate(r.bounds);
                }

                sb.AppendLine($"  {root.name,-28} 父={root.parent.name,-20} 子渲染={rs.Length,2} center={b.center} size={b.size}");
            }
        }

        Debug.Log(sb.ToString());
    }
}
