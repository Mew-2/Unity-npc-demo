using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 把 Bedroom 资源包（Built-in RP）并进 URP 工程：
///   1) Standard shader 的材质批量换成 URP/Lit，并做属性映射（颜色/贴图/法线/金属度/光滑度/自发光/透明）
///   2) 清掉场景里依赖 Post Processing Stack v2 的对象（我们没装那个包）
///   3) 勘察场景：房间包围盒、相机、灯光、主要家具的世界坐标
///
/// 菜单：Tools/DawnAndDusk/Prepare Bedroom
/// 批处理：-executeMethod BedroomPrep.RunAll
/// </summary>
public static class BedroomPrep
{
    const string Root = "Assets/Bedroom";
    const string ScenePath = Root + "/Scene/bedroom.unity";
    static readonly string ReportPath = Path.Combine(Application.dataPath, "../dsh-bedroom-prep.txt");

    static readonly StringBuilder Report = new StringBuilder();

    [MenuItem("Tools/DawnAndDusk/Prepare Bedroom")]
    public static void RunAll()
    {
        Report.Clear();
        Report.AppendLine("# Bedroom 资源包接入准备");
        Report.AppendLine($"# unity={Application.unityVersion}  {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}");

        ConvertMaterials();
        InspectScene();

        File.WriteAllText(ReportPath, Report.ToString(), new UTF8Encoding(true));
        Debug.Log($"[Bedroom] 报告写入 {Path.GetFullPath(ReportPath)}");
    }

    static void ConvertMaterials()
    {
        var lit = Shader.Find("Universal Render Pipeline/Lit");
        if (lit == null)
        {
            Report.AppendLine("!! 找不到 URP/Lit shader");
            return;
        }

        var mats = AssetDatabase.FindAssets("t:Material", new[] { Root })
            .Select(AssetDatabase.GUIDToAssetPath)
            .ToArray();

        var converted = 0;
        foreach (var path in mats)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null || mat.shader == null)
            {
                continue;
            }

            if (mat.shader.name.StartsWith("Universal Render Pipeline"))
            {
                continue;
            }

            var color = mat.HasProperty("_Color") ? mat.GetColor("_Color") : Color.white;
            var mainTex = mat.HasProperty("_MainTex") ? mat.GetTexture("_MainTex") : null;
            var bump = mat.HasProperty("_BumpMap") ? mat.GetTexture("_BumpMap") : null;
            var metallic = mat.HasProperty("_Metallic") ? mat.GetFloat("_Metallic") : 0f;
            var gloss = mat.HasProperty("_Glossiness") ? mat.GetFloat("_Glossiness") : 0.5f;
            var emissionMap = mat.HasProperty("_EmissionMap") ? mat.GetTexture("_EmissionMap") : null;
            var emissionColor = mat.HasProperty("_EmissionColor") ? mat.GetColor("_EmissionColor") : Color.black;
            var occlusion = mat.HasProperty("_OcclusionMap") ? mat.GetTexture("_OcclusionMap") : null;
            var cutoff = mat.HasProperty("_Cutoff") ? mat.GetFloat("_Cutoff") : 0.5f;
            var tiling = mat.HasProperty("_MainTex") ? mat.GetTextureScale("_MainTex") : Vector2.one;
            var offset = mat.HasProperty("_MainTex") ? mat.GetTextureOffset("_MainTex") : Vector2.zero;
            var alphaTest = mat.IsKeywordEnabled("_ALPHATEST_ON");
            var alphaBlend = mat.IsKeywordEnabled("_ALPHABLEND_ON");
            var emissionOn = mat.IsKeywordEnabled("_EMISSION");

            mat.shader = lit;

            mat.SetColor("_BaseColor", color);
            if (mainTex != null)
            {
                mat.SetTexture("_BaseMap", mainTex);
                mat.SetTextureScale("_BaseMap", tiling);
                mat.SetTextureOffset("_BaseMap", offset);
            }

            if (bump != null)
            {
                mat.SetTexture("_BumpMap", bump);
                mat.EnableKeyword("_NORMALMAP");
            }

            mat.SetFloat("_Metallic", metallic);
            mat.SetFloat("_Smoothness", Mathf.Clamp01(1f - gloss));

            if (occlusion != null)
            {
                mat.SetTexture("_OcclusionMap", occlusion);
            }

            mat.SetTexture("_EmissionMap", emissionMap);
            mat.SetColor("_EmissionColor", emissionColor);
            if (emissionOn && (emissionMap != null || emissionColor.maxColorComponent > 0.01f))
            {
                mat.EnableKeyword("_EMISSION");
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }

            // 透明 / 镂空
            mat.SetFloat("_Surface", alphaBlend ? 1f : 0f);
            mat.SetFloat("_Blend", 0f);
            mat.SetFloat("_AlphaClip", alphaTest ? 1f : 0f);
            mat.SetFloat("_Cutoff", cutoff);
            mat.SetFloat("_ZWrite", alphaBlend ? 0f : 1f);
            mat.renderQueue = alphaBlend ? 3000 : (alphaTest ? 2450 : -1);

            if (alphaBlend)
            {
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.EnableKeyword("_ALPHAPREMULTIPLY_ON");
                mat.SetOverrideTag("RenderType", "Transparent");
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.DisableKeyword("_ALPHATEST_ON");
            }
            else
            {
                mat.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.SetOverrideTag("RenderType", alphaTest ? "TransparentCutout" : "Opaque");
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
            }

            if (alphaTest)
            {
                mat.EnableKeyword("_ALPHATEST_ON");
            }

            EditorUtility.SetDirty(mat);
            converted++;
        }

        AssetDatabase.SaveAssets();
        Report.AppendLine($"材质转换：{converted} 个 → URP/Lit（共扫描 {mats.Length} 个）");
    }

    static void InspectScene()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        // 清掉 Post Processing Stack v2 的对象（没装这个包）
        var removed = 0;
        foreach (var root in scene.GetRootGameObjects().ToArray())
        {
            if (root.name.ToLowerInvariant().Contains("postprocess") || root.name.ToLowerInvariant().Contains("post processing"))
            {
                Object.DestroyImmediate(root);
                removed++;
            }
        }

        if (removed > 0)
        {
            EditorSceneManager.SaveScene(scene);
        }

        Report.AppendLine($"清理 PostProcessing 对象：{removed} 个");

        var roots = scene.GetRootGameObjects();
        Report.AppendLine("\n## 场景根对象");
        foreach (var root in roots)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            var bounds = new Bounds(root.transform.position, Vector3.zero);
            var first = true;
            foreach (var r in renderers)
            {
                if (first)
                {
                    bounds = r.bounds;
                    first = false;
                }
                else
                {
                    bounds.Encapsulate(r.bounds);
                }
            }

            Report.AppendLine($"- {root.name,-28} pos={root.transform.position}  renderers={renderers.Length}  bounds={(first ? "无" : $"{bounds.min} ~ {bounds.max}")}");
        }

        Report.AppendLine("\n## 相机与灯光");
        foreach (var cam in Object.FindObjectsOfType<Camera>())
        {
            Report.AppendLine($"- Camera '{cam.name}' pos={cam.transform.position} rot={cam.transform.eulerAngles} fov={cam.fieldOfView} near={cam.nearClipPlane}");
        }

        foreach (var light in Object.FindObjectsOfType<Light>())
        {
            Report.AppendLine($"- Light '{light.name}' type={light.type} intensity={light.intensity:F2} color={light.color} pos={light.transform.position} shadows={light.shadows}");
        }

        Report.AppendLine("\n## 全部渲染物体（世界位置，前 60）");
        foreach (var r in Object.FindObjectsOfType<Renderer>().Take(60))
        {
            Report.AppendLine($"  {r.name,-30} pos={r.transform.position}  size={r.bounds.size}");
        }

        Report.AppendLine("\n## 楼层尺度参考（所有 Renderer 合计包围盒）");
        var all = Object.FindObjectsOfType<Renderer>();
        if (all.Length > 0)
        {
            var b = all[0].bounds;
            foreach (var r in all)
            {
                b.Encapsulate(r.bounds);
            }

            Report.AppendLine($"  整体 bounds: min={b.min} max={b.max} size={b.size}");
        }
    }
}
