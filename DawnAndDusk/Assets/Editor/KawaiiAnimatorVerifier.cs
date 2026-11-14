using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 无头验证 Kawaii 动画链路：FBX 是否以 Humanoid 导入、生成的 Animator Controller 状态机是否符合预期、
/// 场景/Resources 里的运行时接线是否找得到 controller。
/// 菜单：Tools/DawnAndDusk/Verify Kawaii Animator
/// 批处理：Unity.exe -batchmode -quit -executeMethod KawaiiAnimatorVerifier.VerifyAll
/// 报告写到仓库根目录 dsh-kawaii-verify.txt（不入库）。
/// </summary>
public static class KawaiiAnimatorVerifier
{
    const string Root = "Assets/_LocalTest/Kawaii";
    const string ControllerPath = Root + "/Resources/KawaiiAnimator.controller";
    const string ScenePath = "Assets/Scenes/NightRoom.unity";
    static readonly string ReportPath = Path.Combine(Application.dataPath, "../dsh-kawaii-verify.txt");

    static readonly string[] ClipPaths =
    {
        Root + "/@KA_Idle01_breathing.FBX",
        Root + "/@KA_Walk01.FBX",
        Root + "/@KA_Walk01_Start.FBX",
        Root + "/@KA_Walk01_Stop.FBX",
        Root + "/@KA_Walk01_Pivot.FBX",
        Root + "/@KA_Sit_CrossLegs_Loop.FBX",
        Root + "/@KA_Idle44_GreetingBow.FBX",
    };

    static readonly StringBuilder Report = new StringBuilder();
    static int _fails;

    [MenuItem("Tools/DawnAndDusk/Verify Kawaii Animator")]
    public static void VerifyAll()
    {
        _fails = 0;
        Report.Clear();

        Section("0. 环境");
        Line($"unity={Application.unityVersion}  batchmode={Application.isBatchMode}  " +
             $"graphics={SystemInfo.graphicsDeviceType}  gfxNull={SystemInfo.graphicsDeviceName}");

        Section("0.1 VRM 资产");
        EnsureVrm();

        Section("1. FBX / AnimationClip");
        var clips = new Dictionary<string, AnimationClip>();
        foreach (var path in ClipPaths)
        {
            var file = Path.GetFileName(path);
            if (!File.Exists(path))
            {
                Fail($"{file}: 文件不存在");
                continue;
            }

            var all = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                .Where(c => !c.name.StartsWith("__preview")).ToArray();
            if (all.Length == 0)
            {
                Fail($"{file}: 没有导入任何 AnimationClip");
                continue;
            }

            foreach (var clip in all)
            {
                var settings = AnimationUtility.GetAnimationClipSettings(clip);
                var curves = AnimationUtility.GetCurveBindings(clip).Length;
                var frames = Mathf.RoundToInt(clip.length * clip.frameRate);
                Line($"{file} -> clip '{clip.name}'  len={clip.length:F3}s  fps={clip.frameRate:F0}  frames={frames}  " +
                     $"humanoid={clip.isHumanMotion}  loopTime={settings.loopTime}  curves={curves}");
                if (!clip.isHumanMotion)
                {
                    Fail($"{file}: clip 不是 humanoid（Rig 不是 Humanoid，重定向到 VRM 会失效）");
                }
                if (clip.length <= 0.001f)
                {
                    Fail($"{file}: clip 长度为 0");
                }
            }

            clips[file] = all[0];
        }

        Section("1.1 片段裁剪（起步/停止不应再是跨房间的长度）");
        ExpectLength(clips, "@KA_Walk01_Start.FBX", 0.800f);
        ExpectLength(clips, "@KA_Walk01_Stop.FBX", 1.000f);
        ExpectLength(clips, "@KA_Walk01.FBX", 1.333f);

        Section("2. 生成 Animator Controller");
        KawaiiAnimatorBuilder.Build();
        AssetDatabase.Refresh();

        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null)
        {
            Fail($"没有生成 {ControllerPath}");
            Flush();
            return;
        }

        Line($"controller={ControllerPath}");
        Line("parameters: " + string.Join(", ", controller.parameters.Select(p => $"{p.name}:{p.type}")));

        var machine = controller.layers[0].stateMachine;
        Line($"defaultState={machine.defaultState?.name}");
        if (machine.defaultState == null || machine.defaultState.name != "Idle")
        {
            Fail("默认状态不是 Idle");
        }

        foreach (var state in machine.states)
        {
            var s = state.state;
            var motion = s.motion as AnimationClip;
            Line($"state {s.name,-10} motion={(motion == null ? "<null>" : motion.name)}  speed={s.speed}  transitions={s.transitions.Length}");
            if (motion == null)
            {
                Fail($"状态 {s.name} 没有 motion");
            }
        }

        foreach (var state in machine.states)
        {
            foreach (var t in state.state.transitions)
            {
                var conds = string.Join(" && ", t.conditions.Select(c => $"{c.parameter} {c.mode} {c.threshold}"));
                Line($"  {state.state.name,-10} -> {(t.destinationState == null ? "<exit>" : t.destinationState.name),-10} " +
                     $"exitTime={t.hasExitTime}({t.exitTime:F2}) dur={t.duration:F2} [{conds}]");
            }
        }

        foreach (var t in machine.anyStateTransitions)
        {
            var conds = string.Join(" && ", t.conditions.Select(c => $"{c.parameter} {c.mode} {c.threshold}"));
            Line($"  Any -> {(t.destinationState == null ? "<null>" : t.destinationState.name),-10} dur={t.duration:F2} [{conds}]");
        }

        Section("3. 状态机断言");
        Expect(machine, "Idle", "WalkStart", "Speed Greater 0.5");
        Expect(machine, "Idle", "WalkStart", "Braking IfNot");
        Expect(machine, "WalkStart", "Walk", null);
        Expect(machine, "Walk", "WalkStop", "SpeedRatio Less 0.25");
        Expect(machine, "WalkStop", "Idle", null);
        Expect(machine, "WalkStart", "WalkStop", "Speed Less 0.05");
        Expect(machine, "Pivot", "Walk", "Speed Greater 0.1");
        Expect(machine, "Pivot", "Idle", "Speed Less 0.1");
        ExpectAny(machine, "Pivot", "Pivot If");
        ExpectAny(machine, "Bow", "Bow If");
        Expect(machine, "Idle", "Sit", "Sit If");
        Expect(machine, "Sit", "Idle", "Sit IfNot");

        Section("4. 场景 / Resources 接线");
        var res = Resources.Load<RuntimeAnimatorController>("KawaiiAnimator");
        Line($"Resources.Load(\"KawaiiAnimator\") = {(res == null ? "null" : res.name)}");
        if (res == null)
        {
            Fail("Resources 里找不到 KawaiiAnimator，运行时 CharacterSwitcher 会回退 T-pose");
        }

        var profile = Resources.Load<KawaiiMotionProfile>("KawaiiMotionProfile");
        Line($"Resources.Load(\"KawaiiMotionProfile\") = {(profile == null ? "null" : $"{profile.referenceSpeed:F3} m/s, tracks={(profile.tracks == null ? 0 : profile.tracks.Length)}")}");
        if (profile == null)
        {
            Fail("Resources 里找不到 KawaiiMotionProfile，走路会退回常数速度（会打滑）");
        }
        else if (profile.tracks != null)
        {
            foreach (var track in profile.tracks)
            {
                var n = track.speeds == null ? 0 : track.speeds.Length;
                var peak = 0f;
                var sum = 0f;
                if (track.speeds != null)
                {
                    foreach (var speed in track.speeds)
                    {
                        peak = Mathf.Max(peak, speed);
                        sum += speed;
                    }
                }

                var avg = n > 0 ? sum / n : 0f;
                Line($"  track {track.state,-10} frames={n,3} 平均={avg:F2} m/s 峰值={peak:F2} m/s");
            }
        }

        if (File.Exists(ScenePath))
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var roots = scene.GetRootGameObjects();
            var npcs = roots.SelectMany(r => r.GetComponentsInChildren<NpcAgent>(true)).ToArray();
            var switchers = roots.SelectMany(r => r.GetComponentsInChildren<CharacterSwitcher>(true)).ToArray();
            Line($"scene={ScenePath}  NpcAgent={npcs.Length}  CharacterSwitcher={switchers.Length}");
            if (npcs.Length == 0)
            {
                Fail("场景里没有 NpcAgent");
            }

            if (switchers.Length == 0)
            {
                Fail("场景里没有 CharacterSwitcher，Play 时不会实例化 Visual / 不会加载 Animator");
            }

            foreach (var switcher in switchers)
            {
                var profiles = switcher.profiles;
                Line($"  CharacterSwitcher on '{switcher.gameObject.name}'  profiles={profiles?.Count ?? 0}  " +
                     $"npc={(switcher.npc == null ? "<null>" : switcher.npc.name)}  client={(switcher.client == null ? "<null>" : switcher.client.name)}");
                if (switcher.npc == null)
                {
                    Fail("CharacterSwitcher.npc 为空");
                }

                if (profiles != null)
                {
                    foreach (var p in profiles)
                    {
                        Line($"    profile {p.id} personaId={p.personaId} visual={(p.visualPrefab == null ? "<null>" : p.visualPrefab.name)} " +
                             $"offset={p.visualOffset} ground={p.groundOffset} expressions={p.expressions?.Count ?? 0}");
                        if (p.visualPrefab == null)
                        {
                            Fail($"profile {p.id} 没有 visualPrefab");
                        }
                    }
                }
            }

            foreach (var npc in npcs)
            {
                var animators = npc.GetComponentsInChildren<Animator>(true);
                var driver = npc.GetComponentInChildren<NpcAnimationDriver>(true);
                Line($"  Npc '{npc.name}'  position={npc.transform.position}  Animator={animators.Length}  " +
                     $"NpcAnimationDriver={(driver == null ? "<none>" : "有")}（Play 时随 Visual 实例化）");
            }
        }
        else
        {
            Fail($"找不到场景 {ScenePath}");
        }

        Section("5. 结果");
        Line(_fails == 0 ? "PASS：Kawaii 动画链路验证通过（8 项断言 + 场景接线）" : $"FAIL：{_fails} 项不通过");
        Flush();
    }

    /// <summary>
    /// 条件写法："Speed Greater 0.1"（带阈值）或 "Sit If"（只比参数与模式）。
    /// </summary>
    static bool Match(AnimatorCondition[] conditions, string spec)
    {
        if (string.IsNullOrEmpty(spec))
        {
            // 只靠 exitTime 的转换本来就没有条件
            return true;
        }

        var parts = spec.Split(' ');
        return conditions.Any(c =>
        {
            if (c.parameter != parts[0] || c.mode.ToString() != parts[1])
            {
                return false;
            }

            return parts.Length < 3 || Mathf.Approximately(c.threshold, float.Parse(parts[2]));
        });
    }

    /// <summary>
    /// 把 NightRoom 里 Npc 的移动参数改成和片段步幅一致的值（改完保存场景）。
    /// 场景里存的是序列化值，改 C# 默认值不会影响已有实例，所以要显式写一次。
    /// </summary>
    [MenuItem("Tools/DawnAndDusk/Apply Npc Move Tuning")]
    public static void ApplyNpcTuning()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var npcs = scene.GetRootGameObjects()
            .SelectMany(r => r.GetComponentsInChildren<NpcAgent>(true))
            .ToArray();

        if (npcs.Length == 0)
        {
            Debug.LogError("[Tuning] 场景里没有 NpcAgent");
            return;
        }

        foreach (var npc in npcs)
        {
            npc.moveSpeed = 1.03f;   // 对齐 KawaiiMotionProfile.referenceSpeed（Walk 片段平均步速）
            npc.accelTime = 0.6f;
            npc.decelTime = 0.3f;
            npc.autoPivot = false;   // Pivot 片段自带 3m 位移，先关掉自动触发
            Debug.Log($"[Tuning] Npc '{npc.name}' moveSpeed={npc.moveSpeed} accel={npc.accelTime} decel={npc.decelTime} autoPivot={npc.autoPivot}");
            EditorUtility.SetDirty(npc);
        }

        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("[Tuning] 场景已保存");
    }

    /// <summary>
    /// VRM 在 -nographics 批量模式下会因拿不到 MToon shader 而导入失败（ArgumentNullException），
    /// 这里在带图形设备时强制重导一次并报告结果。
    /// </summary>
    static void EnsureVrm()
    {
        const string vrmPath = "Assets/Characters/DeepSeekChan/DeepSeek-chan.vrm";
        var main = AssetDatabase.LoadMainAssetAtPath(vrmPath);
        if (!(main is GameObject))
        {
            Line($"{vrmPath} 当前不是模型（{(main == null ? "null" : main.GetType().Name)}），强制重导…");
            AssetDatabase.ImportAsset(vrmPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            main = AssetDatabase.LoadMainAssetAtPath(vrmPath);
        }

        if (main == null)
        {
            Fail("VRM 仍然导入失败（看日志里的 ArgumentNullException 上下文）");
            return;
        }

        var go = main as GameObject;
        Line($"VRM 资产 OK：{main.GetType().Name} '{main.name}'  子物体={(go == null ? 0 : go.transform.childCount)}  " +
             $"SkinnedMeshRenderer={(go == null ? 0 : go.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length)}  " +
             $"Animator={(go == null ? 0 : go.GetComponentsInChildren<Animator>(true).Length)}");
        if (go == null)
        {
            Fail("VRM 主资产不是 GameObject");
        }
        else if (go.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length == 0)
        {
            Fail("VRM 里没有 SkinnedMeshRenderer（模型没导入成功）");
        }
    }

    static void ExpectLength(System.Collections.Generic.Dictionary<string, AnimationClip> clips, string file, float seconds)
    {
        if (!clips.TryGetValue(file, out var clip) || clip == null)
        {
            Fail($"缺少片段 {file}");
            return;
        }

        if (Mathf.Abs(clip.length - seconds) > 0.06f)
        {
            Fail($"{file} 长度 {clip.length:F3}s，预期 ≈{seconds:F3}s（没裁剪？跑一次 Tools/DawnAndDusk/Trim Kawaii Transitions）");
        }
        else
        {
            Line($"OK  {file} 长度 {clip.length:F3}s ≈ {seconds:F3}s");
        }
    }

    static void Expect(AnimatorStateMachine machine, string from, string to, string condition)
    {
        var fromState = machine.states.Select(s => s.state).FirstOrDefault(s => s.name == from);
        if (fromState == null)
        {
            Fail($"状态机里没有 {from}");
            return;
        }

        var hit = fromState.transitions.Any(t =>
            t.destinationState != null && t.destinationState.name == to && Match(t.conditions, condition));
        if (hit)
        {
            Line($"OK  {from} -> {to} [{condition ?? "无额外条件"}]");
        }
        else
        {
            Fail($"缺少转换 {from} -> {to} [{condition}]");
        }
    }

    static void ExpectAny(AnimatorStateMachine machine, string to, string condition)
    {
        var hit = machine.anyStateTransitions.Any(t =>
            t.destinationState != null && t.destinationState.name == to && Match(t.conditions, condition));
        if (hit)
        {
            Line($"OK  Any -> {to} [{condition}]");
        }
        else
        {
            Fail($"缺少 AnyState -> {to} [{condition}]");
        }
    }

    static void Section(string title)
    {
        Report.AppendLine();
        Report.AppendLine("== " + title + " ==");
        Debug.Log($"[Verify] {title}");
    }

    static void Line(string text)
    {
        Report.AppendLine(text);
        Debug.Log("[Verify] " + text);
    }

    static void Fail(string text)
    {
        _fails++;
        Report.AppendLine("FAIL: " + text);
        Debug.LogError("[Verify] FAIL: " + text);
    }

    static void Flush()
    {
        try
        {
            File.WriteAllText(ReportPath, Report.ToString(), new UTF8Encoding(true));
            Debug.Log($"[Verify] 报告已写入 {Path.GetFullPath(ReportPath)}");
        }
        catch (Exception e)
        {
            Debug.LogError("[Verify] 报告写入失败: " + e.Message);
        }
    }
}
