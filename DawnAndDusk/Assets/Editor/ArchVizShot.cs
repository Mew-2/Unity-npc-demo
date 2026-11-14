using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// ArchViz 公寓场景勘察 + 出图：
///   1) 打印根对象、包围盒、相机、灯光
///   2) 用场景自带相机拍一张
///   3) 用正交顶视图拍一张平面图（看户型、找哪间卧室有窗）
/// 菜单：Tools/DawnAndDusk/Shoot ArchViz
/// </summary>
public static class ArchVizShot
{
    const string ScenePath = "Assets/_LocalTest/ArchViz/3D SCENE/AVP8_URP_Standalone.unity";
    static readonly string OutDir = Path.Combine(Application.dataPath, "../dsh-archviz-shots");
    static readonly string ReportPath = Path.Combine(Application.dataPath, "../dsh-archviz-report.txt");

    [MenuItem("Tools/DawnAndDusk/Shoot ArchViz")]
    public static void Shoot()
    {
        Directory.CreateDirectory(OutDir);
        var report = new StringBuilder();
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        report.AppendLine($"# ArchViz 场景勘察 {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}");

        var roots = scene.GetRootGameObjects();
        report.AppendLine($"\n根对象 {roots.Length} 个：");
        foreach (var r in roots)
        {
            var renderers = r.GetComponentsInChildren<Renderer>(true);
            report.AppendLine($"- {r.name,-34} 子渲染体={renderers.Length,4}  active={r.activeSelf}");
        }

        var all = Object.FindObjectsOfType<Renderer>();
        var bounds = all.Length > 0 ? all[0].bounds : new Bounds(Vector3.zero, Vector3.one);
        foreach (var r in all)
        {
            bounds.Encapsulate(r.bounds);
        }

        report.AppendLine($"\n整体包围盒: min={bounds.min} max={bounds.max} size={bounds.size}");

        report.AppendLine("\n相机：");
        foreach (var cam in Object.FindObjectsOfType<Camera>())
        {
            report.AppendLine($"- {cam.name} pos={cam.transform.position} rot={cam.transform.eulerAngles} fov={cam.fieldOfView} depth={cam.depth}");
        }

        report.AppendLine("\n灯光：");
        foreach (var light in Object.FindObjectsOfType<Light>().Take(20))
        {
            report.AppendLine($"- {light.name} type={light.type} intensity={light.intensity:F2} pos={light.transform.position}");
        }

        report.AppendLine("\n房间级渲染体（体积 > 20 立方米的，可能是墙体/地板）：");
        foreach (var r in all)
        {
            var v = r.bounds.size;
            if (v.x * v.y * v.z > 20f)
            {
                report.AppendLine($"  {r.name,-30} center={r.bounds.center} size={v}");
            }
        }

        File.WriteAllText(ReportPath, report.ToString(), new UTF8Encoding(true));
        Debug.Log(report.ToString());

        // 场景自带相机拍一张
        var cam0 = Object.FindObjectsOfType<Camera>().OrderByDescending(c => c.depth).FirstOrDefault();
        if (cam0 != null)
        {
            RenderFrom(cam0, cam0.transform.position, cam0.transform.eulerAngles, "01-scene-camera.png", 1600, 900);
        }

        // 顶视正交平面图
        var top = new GameObject("TempTopCam").AddComponent<Camera>();
        top.orthographic = true;
        top.orthographicSize = Mathf.Max(bounds.extents.x, bounds.extents.z) * 1.05f;
        top.transform.position = new Vector3(bounds.center.x, bounds.max.y + 5f, bounds.center.z);
        top.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        top.nearClipPlane = 0.1f;
        top.farClipPlane = bounds.size.y + 20f;
        RenderFrom(top, top.transform.position, top.transform.eulerAngles, "02-topdown.png", 1400, 1400);
        Object.DestroyImmediate(top.gameObject);

        Debug.Log($"[ArchViz] 图在 {OutDir}，报告在 {ReportPath}");
    }

    static void RenderFrom(Camera cam, Vector3 pos, Vector3 euler, string file, int w, int h)
    {
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
        Debug.Log("[ArchViz] " + file);
    }
}
