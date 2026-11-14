using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 找主卧的门：列出所有名字含 Door 的对象及其世界坐标，判断哪个在卧室边界上。
/// 菜单：Tools/DawnAndDusk/Find Bedroom Door
/// </summary>
public static class DoorProbe
{
    const string ScenePath = "Assets/_LocalTest/ArchViz/3D SCENE/ApartmentDemo.unity";

    [MenuItem("Tools/DawnAndDusk/Find Bedroom Door")]
    public static void Find()
    {
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var sb = new StringBuilder();
        sb.AppendLine("# 所有 Door 对象");

        foreach (var t in Object.FindObjectsOfType<Transform>())
        {
            if (t.name.IndexOf("door", System.StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            var parent = t.parent != null ? t.parent.name : "-";
            var renderer = t.GetComponent<Renderer>();
            var size = renderer != null ? renderer.bounds.size.ToString() : "无渲染体";
            sb.AppendLine($"{t.name,-24} pos={t.position} size={size} 父={parent}");
        }

        // 卧室四壁内侧的可行走点：沿 x=1.95（西墙内）、z=0.35（南墙内）采样
        sb.AppendLine("\n# 卧室西墙 / 南墙 附近的墙内表面");
        foreach (var r in Object.FindObjectsOfType<Renderer>()
                     .Where(r => r.name.IndexOf("Wall", System.StringComparison.OrdinalIgnoreCase) >= 0
                                 && r.bounds.center.x > 1.0f && r.bounds.center.z > -0.5f && r.bounds.center.z < 4.0f))
        {
            sb.AppendLine($"  {r.name,-26} center={r.bounds.center} size={r.bounds.size}");
        }

        Debug.Log(sb.ToString());
    }
}
