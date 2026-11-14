using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 把 Kawaii 的"起步/停止"片段裁到可用的那一小段。
///
/// 为什么裁：这两个片段自带的根位移是跨房间级别的——
///   Walk01_Start 2.333s 走 1.98m（前 0.7s 只有 0.28→0.69 m/s，是抬脚）；
///   Walk01_Stop  3.000s 走 2.00m（前 1.2s 还在 1.2 m/s 走路，2.1s 后才站定）。
/// 而演示里从床到窗只有一两米，原样播就会"用起步姿势平移""站定了还在迈步"。
///
/// 做法：改 ModelImporter 的 clipAnimations 帧区间并重导，不改 FBX 本体。
///   起步 = 第 0~24 帧（0.80s，约 0.38m）—— 抬脚到迈出第一步
///   停止 = 第 60~90 帧（1.00s，约 0.04m）—— 只有收势，没有多余步幅
///
/// 菜单：Tools/DawnAndDusk/Trim Kawaii Transitions（应用）
///      Tools/DawnAndDusk/Reset Kawaii Clips（还原全段）
/// </summary>
public static class KawaiiClipTrimmer
{
    const string Root = "Assets/_LocalTest/Kawaii";
    const string StartPath = Root + "/@KA_Walk01_Start.FBX";
    const string StopPath = Root + "/@KA_Walk01_Stop.FBX";

    // 30fps 下的帧号
    const int StartFirst = 0;
    const int StartLast = 24;
    const int StopFirst = 60;
    const int StopLast = 90;

    [MenuItem("Tools/DawnAndDusk/Trim Kawaii Transitions")]
    public static void Trim()
    {
        Apply(StartPath, StartFirst, StartLast);
        Apply(StopPath, StopFirst, StopLast);
        AssetDatabase.Refresh();
        Dump();
    }

    [MenuItem("Tools/DawnAndDusk/Reset Kawaii Clips")]
    public static void ResetAll()
    {
        Reset(StartPath);
        Reset(StopPath);
        AssetDatabase.Refresh();
        Dump();
    }

    static void Apply(string path, int first, int last)
    {
        var importer = AssetImporter.GetAtPath(path) as ModelImporter;
        if (importer == null)
        {
            Debug.LogError($"[Trim] 找不到 ModelImporter：{path}");
            return;
        }

        var clips = importer.defaultClipAnimations;
        if (clips == null || clips.Length == 0)
        {
            clips = importer.clipAnimations;
        }

        if (clips == null || clips.Length == 0)
        {
            Debug.LogError($"[Trim] {Path.GetFileName(path)} 没有可用的动画片段定义");
            return;
        }

        foreach (var clip in clips)
        {
            clip.firstFrame = first;
            clip.lastFrame = last;
            clip.loopTime = false;
        }

        importer.clipAnimations = clips;
        importer.SaveAndReimport();
        Debug.Log($"[Trim] {Path.GetFileName(path)} 裁到第 {first}~{last} 帧");
    }

    static void Reset(string path)
    {
        var importer = AssetImporter.GetAtPath(path) as ModelImporter;
        if (importer == null)
        {
            return;
        }

        var clips = importer.defaultClipAnimations;
        if (clips == null || clips.Length == 0)
        {
            return;
        }

        foreach (var clip in clips)
        {
            clip.firstFrame = 0;
            clip.lastFrame = (int)clip.lastFrame; // 保留 take 原始长度
        }

        // 清空自定义区间，回到 Unity 的自动结果
        importer.clipAnimations = new ModelImporterClipAnimation[0];
        importer.SaveAndReimport();
        Debug.Log($"[Trim] {Path.GetFileName(path)} 已还原为自动片段");
    }

    static void Dump()
    {
        var sb = new StringBuilder();
        foreach (var path in new[] { StartPath, StopPath, Root + "/@KA_Walk01.FBX" })
        {
            foreach (var clip in AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                         .Where(c => !c.name.StartsWith("__preview")))
            {
                var frames = Mathf.RoundToInt(clip.length * clip.frameRate);
                var line = $"{Path.GetFileName(path)} -> '{clip.name}'  len={clip.length:F3}s  frames={frames}";
                sb.AppendLine(line);
                Debug.Log("[Trim] " + line);
            }
        }

        File.WriteAllText(Path.Combine(Application.dataPath, "../dsh-kawaii-trim.txt"), sb.ToString());
    }
}
