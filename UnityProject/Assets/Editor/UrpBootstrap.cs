using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 一键把 Built-in 工程切换到 URP：
/// 1) 创建 UniversalRendererData 与 UniversalRenderPipelineAsset
/// 2) 赋给 GraphicsSettings 与所有 Quality Level
/// 可用菜单 Tools/Setup URP 手动执行，也可 batchmode -executeMethod UrpBootstrap.Setup。
/// </summary>
public static class UrpBootstrap
{
    const string SettingsDir = "Assets/Settings";

    [MenuItem("Tools/Setup URP")]
    public static void Setup()
    {
        if (!AssetDatabase.IsValidFolder(SettingsDir))
        {
            AssetDatabase.CreateFolder("Assets", "Settings");
        }

        string rendererPath = SettingsDir + "/UniversalRenderer.asset";
        string pipelinePath = SettingsDir + "/URP-Asset.asset";

        var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rendererPath);
        if (rendererData == null)
        {
            rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
            AssetDatabase.CreateAsset(rendererData, rendererPath);
        }

        var pipelineAsset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(pipelinePath);
        if (pipelineAsset == null)
        {
            pipelineAsset = UniversalRenderPipelineAsset.Create(rendererData);
            AssetDatabase.CreateAsset(pipelineAsset, pipelinePath);
        }

        GraphicsSettings.defaultRenderPipeline = pipelineAsset;
        for (int i = 0; i < QualitySettings.names.Length; i++)
        {
            QualitySettings.SetQualityLevel(i, false);
            QualitySettings.renderPipeline = pipelineAsset;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[UrpBootstrap] URP ready: " + pipelinePath + " assigned to Graphics + " + QualitySettings.names.Length + " quality levels.");
    }
}
