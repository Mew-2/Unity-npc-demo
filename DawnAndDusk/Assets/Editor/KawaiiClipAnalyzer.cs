using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 量 Kawaii 片段"到底什么时候真的在走"：读 humanoid 的根位移曲线（RootT/RootQ），
/// 按帧累计位移，输出每 10% 时间窗走了多少米、瞬时速度多大。
/// 用来决定 WalkStart / WalkStop 的 exitTime，以及 NpcAgent 的加减速曲线。
/// 菜单：Tools/DawnAndDusk/Analyze Kawaii Clips
/// 报告：仓库根 dsh-kawaii-clips.txt（不入库）
/// </summary>
public static class KawaiiClipAnalyzer
{
    const string Root = "Assets/_LocalTest/Kawaii";
    static readonly string ReportPath = Path.Combine(Application.dataPath, "../dsh-kawaii-clips.txt");

    static readonly string[] Paths =
    {
        Root + "/@KA_Walk01_Start.FBX",
        Root + "/@KA_Walk01.FBX",
        Root + "/@KA_Walk01_Stop.FBX",
        Root + "/@KA_Walk01_Pivot.FBX",
        Root + "/@KA_Idle01_breathing.FBX",
        Root + "/@KA_Sit_CrossLegs_Loop.FBX",
    };

    [MenuItem("Tools/DawnAndDusk/Analyze Kawaii Clips")]
    public static void Analyze()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Kawaii 片段节奏分析");
        sb.AppendLine($"# unity={Application.unityVersion}  {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine("# 说明：displacement 是根位移曲线（RootT）累计值；窗口 = 片段的 10 等分");

        foreach (var path in Paths)
        {
            if (!File.Exists(path))
            {
                sb.AppendLine($"\n## {Path.GetFileName(path)}  文件不存在");
                continue;
            }

            var clips = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                .Where(c => !c.name.StartsWith("__preview")).ToArray();
            foreach (var clip in clips)
            {
                AnalyzeClip(clip, Path.GetFileName(path), sb);
            }
        }

        File.WriteAllText(ReportPath, sb.ToString(), new UTF8Encoding(true));
        Debug.Log($"[ClipAnalyze] 报告写入 {Path.GetFullPath(ReportPath)}");
        Debug.Log(sb.ToString());
    }

    static void AnalyzeClip(AnimationClip clip, string file, StringBuilder sb)
    {
        sb.AppendLine();
        sb.AppendLine($"## {file} -> '{clip.name}'  len={clip.length:F3}s  fps={clip.frameRate:F0}  humanoid={clip.isHumanMotion}");

        var bindings = AnimationUtility.GetCurveBindings(clip);
        var rootX = Find(bindings, clip, "RootT.x");
        var rootY = Find(bindings, clip, "RootT.y");
        var rootZ = Find(bindings, clip, "RootT.z");

        if (rootX == null && rootZ == null)
        {
            sb.AppendLine("  没有 RootT 根位移曲线（原地动画），只能靠速度和位移同步来对拍");
            var sample = bindings.Where(b => b.path == "").Select(b => b.propertyName).Distinct().Take(12);
            sb.AppendLine("  根节点上的曲线属性样例：" + string.Join(", ", sample));
            return;
        }

        const int WindowCount = 10;
        var windowMeters = new double[WindowCount];
        var windowSeconds = new double[WindowCount];
        Vector3 prev = Vector3.zero;
        var total = 0f;
        var frames = Mathf.Max(1, Mathf.RoundToInt(clip.length * clip.frameRate));

        for (var f = 0; f <= frames; f++)
        {
            var t = Mathf.Min(clip.length, f / clip.frameRate);
            var p = new Vector3(Eval(rootX, t), Eval(rootY, t), Eval(rootZ, t));
            if (f > 0)
            {
                var d = Vector3.Distance(p, prev);
                total += d;
                var w = Mathf.Min(WindowCount - 1, (int)(t / clip.length * WindowCount));
                windowMeters[w] += d;
                windowSeconds[w] += 1.0 / clip.frameRate;
            }

            prev = p;
        }

        sb.AppendLine($"  片段总位移（根曲线，水平 XZ）= {total:F3} m；平均速度 = {total / Mathf.Max(0.001f, clip.length):F2} m/s");
        sb.AppendLine("  窗口 起点s  时长s  位移m  该窗平均速度m/s");
        var maxSpeed = 0.0;
        for (var i = 0; i < WindowCount; i++)
        {
            var v = windowSeconds[i] > 0 ? windowMeters[i] / windowSeconds[i] : 0;
            maxSpeed = Math.Max(maxSpeed, v);
            sb.AppendLine($"  {i * 10,3}% {i * clip.length / WindowCount,6:F2} {windowSeconds[i],6:F2} {windowMeters[i],6:F3} {v,8:F2}");
        }

        sb.AppendLine($"  峰值速度 {maxSpeed:F2} m/s（拿它和 NpcAgent.moveSpeed 对一下就知道要不要调）");
    }

    static AnimationCurve Find(EditorCurveBinding[] bindings, AnimationClip clip, string prop)
    {
        var b = bindings.FirstOrDefault(x => x.path == "" && x.propertyName == prop);
        return b.propertyName == null ? null : AnimationUtility.GetEditorCurve(clip, b);
    }

    static float Eval(AnimationCurve c, float t) => c == null ? 0f : c.Evaluate(t);
}
