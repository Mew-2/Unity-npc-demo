using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// 用 _LocalTest 里的 Kawaii Humanoid FBX 生成 Animator Controller。
/// 走路：Walk01_Start -> Walk01(Loop) -> Walk01_Stop，拐点触发 Walk01_Pivot。
/// 产物在 _LocalTest（gitignore），公开仓库不会带授权资产。
/// 菜单：Tools/DawnAndDusk/Build Kawaii Animator
/// </summary>
public static class KawaiiAnimatorBuilder
{
    const string Root = "Assets/_LocalTest/Kawaii";
    const string ResourcesDir = Root + "/Resources";
    const string ControllerPath = ResourcesDir + "/KawaiiAnimator.controller";

    /// <summary>
    /// A/B 实验参数：WalkStart → Walk 的混合时长（秒）。
    /// 基线 0.15 时实测接缝多走 0.19m（≈ Start 末帧根偏移 0.38m 的一半）；
    /// 对照组 0.30：若超量保持不变 ⇒ 姿态/偏移泄漏；若随窗口变大 ⇒ 根位移叠加。
    /// </summary>
    const float StartToWalkBlend = 0.30f;

    const string IdlePath = Root + "/@KA_Idle01_breathing.FBX";
    const string WalkPath = Root + "/@KA_Walk01.FBX";
    const string WalkStartPath = Root + "/@KA_Walk01_Start.FBX";
    const string WalkStopPath = Root + "/@KA_Walk01_Stop.FBX";
    const string WalkPivotPath = Root + "/@KA_Walk01_Pivot.FBX";
    const string SitStartPath = Root + "/@KA_Sit_Start.FBX";
    const string SitLoopPath = Root + "/@KA_Sit_LookingAround.FBX";
    const string SitEndPath = Root + "/@KA_Sit_End.FBX";
    const string BowPath = Root + "/@KA_Idle44_GreetingBow.FBX";

    [MenuItem("Tools/DawnAndDusk/Build Kawaii Animator")]
    public static void Build()
    {
        // 关键：循环必须在**导入设置**里落地，否则 Idle/Walk 播一遍就停在末帧
        // （Walk 停住 = 根位移归零、人卡半路；Idle 停住 = 站着不呼吸）
        EnsureLoopSettings();

        var idle = LoadClip(IdlePath, "Idle01_breathing");
        var walk = LoadClip(WalkPath, "KA_Walk01");
        var walkStart = LoadClip(WalkStartPath, "KA_Walk01_Start");
        var walkStop = LoadClip(WalkStopPath, "KA_Walk01_Stop");
        var pivot = LoadClip(WalkPivotPath, "KA_Walk01_Pivot");
        var sitStart = LoadClip(SitStartPath, "Sit_Start");
        var sitLoop = LoadClip(SitLoopPath, "Sit_LookingAround");
        var sitEnd = LoadClip(SitEndPath, "Sit_End");
        var bow = LoadClip(BowPath, "Idle44_GreetingBow");

        if (idle == null || walk == null)
        {
            Debug.LogError("[Kawaii] 找不到 idle/walk 的 AnimationClip；先确认 FBX 已导入且 Rig=Humanoid。");
            return;
        }

        SetLoop(idle, true);
        SetLoop(walk, true);
        if (walkStart != null) SetLoop(walkStart, false);
        if (walkStop != null) SetLoop(walkStop, false);
        if (pivot != null) SetLoop(pivot, false);
        if (sit != null) SetLoop(sit, true);
        if (bow != null) SetLoop(bow, false);

        if (!AssetDatabase.IsValidFolder(ResourcesDir))
        {
            if (!AssetDatabase.IsValidFolder(Root))
            {
                Debug.LogError($"[Kawaii] 目录不存在：{Root}");
                return;
            }

            AssetDatabase.CreateFolder(Root, "Resources");
        }

        if (AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath) != null)
        {
            AssetDatabase.DeleteAsset(ControllerPath);
        }

        var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
        controller.AddParameter("SpeedRatio", AnimatorControllerParameterType.Float);
        controller.AddParameter("Braking", AnimatorControllerParameterType.Bool);
        controller.AddParameter("Sit", AnimatorControllerParameterType.Bool);
        controller.AddParameter("Pivot", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("Bow", AnimatorControllerParameterType.Trigger);

        // 打开 IK Pass：膝枕时用 OnAnimatorIK 让她低头看玩家（NpcLookAtIK）
        var layers = controller.layers;
        if (layers.Length > 0)
        {
            layers[0].iKPass = true;
            controller.layers = layers;
        }

        var machine = controller.layers[0].stateMachine;

        var idleState = machine.AddState("Idle");
        idleState.motion = idle;
        machine.defaultState = idleState;

        var walkState = machine.AddState("Walk");
        walkState.motion = walk;
        // 走路播放速率跟实际速度走：刹车时步频一起慢下来，脚才不会比地面快
        walkState.speedParameterActive = true;
        walkState.speedParameter = "SpeedRatio";

        AnimatorState startState = null;
        if (walkStart != null)
        {
            startState = machine.AddState("WalkStart");
            startState.motion = walkStart;
        }

        AnimatorState stopState = null;
        if (walkStop != null)
        {
            stopState = machine.AddState("WalkStop");
            stopState.motion = walkStop;
        }

        var intoWalk = idleState.AddTransition(startState != null ? startState : walkState);
        // 门槛要高：停下瞬间 Speed 参数会从 1 往 0 衰减，若门槛低（0.1）会在这条尾巴上
        // 闪一下 WalkStart，表现为"站定了脚又动一下"（实测抓到 forced_idle from=WalkStart）
        intoWalk.AddCondition(AnimatorConditionMode.Greater, 0.5f, "Speed");
        // 刹车中不许再进起步：否则 WalkStop 回 Idle 后立刻又被 Speed 拉回 WalkStart，
        // 表现为"到点了脚又动几下"（实测日志里 WalkStop->Idle->WalkStart 反复触发）
        intoWalk.AddCondition(AnimatorConditionMode.IfNot, 0f, "Braking");
        intoWalk.duration = 0.1f;
        intoWalk.hasExitTime = false;

        if (startState != null)
        {
            // Start 已被裁成"抬脚"段（约 0.8s），播到接近末尾再融进循环
            var startToLoop = startState.AddTransition(walkState);
            startToLoop.hasExitTime = true;
            // 实测：exitTime 0.62 与 0.80 的接缝超量几乎一样（0.30 vs 0.33，且在限速削掉 0.04 之后），
            // 说明超量主要由「两段末/首帧的姿态差」决定，而不是收尾时还剩多少位移。
            // 所以用作者给的值 0.8，不要在这个旋钮上折腾。
            startToLoop.exitTime = 0.8f;
            startToLoop.duration = StartToWalkBlend;

            if (stopState != null)
            {
                var startToStop = startState.AddTransition(stopState);
                startToStop.AddCondition(AnimatorConditionMode.Less, 0.05f, "Speed");
                startToStop.duration = 0.1f;
                startToStop.hasExitTime = false;
            }
        }

        // 收势只看"移动意图"（Speed），不看播放速率（SpeedRatio）。
        // 实测：root motion 模式下 SpeedRatio 常驻 1，到站后 Speed 已经掉到 0，
        // 状态机却一直留在 Walk（日志里 nt 从 1.19 一路涨到 4.2 都没退出），
        // 片段一循环就变成"原地踏步"。改成 Speed<0.3 收势，到站必进 WalkStop。
        var outOfWalk = walkState.AddTransition(stopState != null ? stopState : idleState);
        outOfWalk.AddCondition(AnimatorConditionMode.Less, 0.3f, "Speed");
        outOfWalk.duration = 0.15f;
        outOfWalk.hasExitTime = false;

        if (stopState != null)
        {
            // Stop 已被裁成"收势"段（约 1s，几乎无位移），播完回 Idle
            var stopToIdle = stopState.AddTransition(idleState);
            stopToIdle.hasExitTime = true;
            stopToIdle.exitTime = 0.9f;
            stopToIdle.duration = 0.15f;
        }

        if (pivot != null)
        {
            var pivotState = machine.AddState("Pivot");
            pivotState.motion = pivot;

            var anyPivot = machine.AddAnyStateTransition(pivotState);
            anyPivot.AddCondition(AnimatorConditionMode.If, 0f, "Pivot");
            anyPivot.duration = 0.1f;
            anyPivot.hasExitTime = false;

            var pivotToWalk = pivotState.AddTransition(walkState);
            pivotToWalk.AddCondition(AnimatorConditionMode.Greater, 0.1f, "Speed");
            pivotToWalk.hasExitTime = true;
            pivotToWalk.exitTime = 0.85f;
            pivotToWalk.duration = 0.1f;

            var pivotToIdle = pivotState.AddTransition(idleState);
            pivotToIdle.AddCondition(AnimatorConditionMode.Less, 0.1f, "Speed");
            pivotToIdle.hasExitTime = true;
            pivotToIdle.exitTime = 0.85f;
            pivotToIdle.duration = 0.1f;
        }

        if (sitLoop != null)
        {
            // 膝枕三段：坐下（Start）→ 坐着等（Loop）→ 起身（End）。
            // Loop 用 @KA_Sit_LookingAround —— 包里没有裸的 @KA_Sit，
            // 这个文件和 Sit_Start / Sit_End 同在 TransitionFromStandingPose 下，是配套的循环段。
            AnimatorState sitStartState = null;
            if (sitStart != null)
            {
                sitStartState = machine.AddState("SitStart");
                sitStartState.motion = sitStart;
            }

            var sitLoopState = machine.AddState("SitLoop");
            sitLoopState.motion = sitLoop;

            AnimatorState sitEndState = null;
            if (sitEnd != null)
            {
                sitEndState = machine.AddState("SitEnd");
                sitEndState.motion = sitEnd;
            }

            var sitEntry = sitStartState != null ? sitStartState : sitLoopState;
            var toSit = idleState.AddTransition(sitEntry);
            toSit.AddCondition(AnimatorConditionMode.If, 0f, "Sit");
            toSit.duration = 0.15f;
            toSit.hasExitTime = false;

            if (sitStartState != null)
            {
                var startToLoop = sitStartState.AddTransition(sitLoopState);
                startToLoop.hasExitTime = true;
                startToLoop.exitTime = 0.85f;
                startToLoop.duration = 0.15f;
            }

            var loopExit = sitEndState != null ? (AnimatorState)sitEndState : idleState;
            var loopToEnd = sitLoopState.AddTransition(loopExit);
            loopToEnd.AddCondition(AnimatorConditionMode.IfNot, 0f, "Sit");
            loopToEnd.duration = 0.15f;
            loopToEnd.hasExitTime = false;

            if (sitEndState != null)
            {
                var endToIdle = sitEndState.AddTransition(idleState);
                endToIdle.hasExitTime = true;
                endToIdle.exitTime = 0.9f;
                endToIdle.duration = 0.15f;
            }
        }

        if (bow != null)
        {
            var bowState = machine.AddState("Bow");
            bowState.motion = bow;

            var anyBow = machine.AddAnyStateTransition(bowState);
            anyBow.AddCondition(AnimatorConditionMode.If, 0f, "Bow");
            anyBow.duration = 0.1f;
            anyBow.hasExitTime = false;

            var bowBack = bowState.AddTransition(idleState);
            bowBack.hasExitTime = true;
            bowBack.exitTime = 0.9f;
            bowBack.duration = 0.2f;
        }

        // Unity 里 duration 默认按"归一化时间"解释（hasFixedDuration=false）。
        // 实测：Idle→WalkStart 写 0.1，实际混了约 0.6 秒——长到把 WalkStart 的退出时间点
        // （0.8）直接跨过去：进场时 nt 已经是 0.829，exit time 永远不会被"穿过"，
        // 于是起步片段一路播到末帧才切 Walk（= 起步那 0.6 秒里根位移先有后无）。
        // 统一改成固定时长（单位秒），让 duration 就是秒。
        foreach (var state in machine.states)
        {
            foreach (var transition in state.state.transitions)
            {
                transition.hasFixedDuration = true;
            }
        }

        foreach (var transition in machine.anyStateTransitions)
        {
            transition.hasFixedDuration = true;
        }

        BuildProfile(walk, walkStart, walkStop, pivot);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log(
            $"[Kawaii] 已生成 {ControllerPath}\n" +
            $"  Idle={idle.name}\n" +
            $"  Walk={walk.name} (Start={walkStart != null} Stop={walkStop != null} Pivot={pivot != null})\n" +
            $"  Sit={sit != null} Bow={bow != null}");
    }

    /// <summary>
    /// 把每个片段的根位移曲线采成"瞬时步速表"，运行时供 NpcAgent 跟随。
    /// 这是消掉打滑的关键：身体速度 = 片段脚速，而不是拍脑袋的常数。
    /// </summary>
    static void BuildProfile(AnimationClip walk, AnimationClip start, AnimationClip stop, AnimationClip pivot)
    {
        var asset = ScriptableObject.CreateInstance<KawaiiMotionProfile>();
        var walkTrack = SampleTrack("Walk", walk);
        asset.referenceSpeed = AverageSpeed(walkTrack, walk);
        asset.tracks = new[]
        {
            SampleTrack("WalkStart", start),
            walkTrack,
            SampleTrack("WalkStop", stop),
            SampleTrack("Pivot", pivot),
        };

        const string profilePath = ResourcesDir + "/KawaiiMotionProfile.asset";
        if (AssetDatabase.LoadAssetAtPath<KawaiiMotionProfile>(profilePath) != null)
        {
            AssetDatabase.DeleteAsset(profilePath);
        }

        AssetDatabase.CreateAsset(asset, profilePath);
        Debug.Log($"[Kawaii] 已生成 {profilePath}（参考步速 {asset.referenceSpeed:F3} m/s）");
    }

    static KawaiiMotionProfile.Track SampleTrack(string state, AnimationClip clip)
    {
        var track = new KawaiiMotionProfile.Track { state = state };
        if (clip == null || clip.length <= 0.0001f)
        {
            track.speeds = new float[0];
            return track;
        }

        var frames = Mathf.Max(2, Mathf.RoundToInt(clip.length * clip.frameRate));
        var speeds = new float[frames];
        var bindings = AnimationUtility.GetCurveBindings(clip);
        var x = RootCurve(bindings, clip, "RootT.x");
        var z = RootCurve(bindings, clip, "RootT.z");

        if (x == null && z == null)
        {
            // 原地片段：步速 0，身体不该被带着走
            track.speeds = speeds;
            return track;
        }

        var previous = Vector3.zero;
        for (var i = 0; i < frames; i++)
        {
            var t = i / clip.frameRate;
            var point = new Vector3(x != null ? x.Evaluate(t) : 0f, 0f, z != null ? z.Evaluate(t) : 0f);
            speeds[i] = i == 0 ? 0f : Vector3.Distance(point, previous) * clip.frameRate;
            previous = point;
        }

        track.speeds = speeds;
        return track;
    }

    static AnimationCurve RootCurve(EditorCurveBinding[] bindings, AnimationClip clip, string property)
    {
        foreach (var binding in bindings)
        {
            if (binding.path == "" && binding.propertyName == property)
            {
                return AnimationUtility.GetEditorCurve(clip, binding);
            }
        }

        return null;
    }

    static float AverageSpeed(KawaiiMotionProfile.Track track, AnimationClip clip)
    {
        if (track.speeds == null || track.speeds.Length == 0 || clip == null || clip.length <= 0.0001f)
        {
            return 1f;
        }

        var total = 0f;
        foreach (var speed in track.speeds)
        {
            total += speed;
        }

        var average = total / track.speeds.Length;
        return average > 0.0001f ? average : 1f;
    }

    static AnimationClip LoadClip(string path, string preferredName)
    {
        var clips = AssetDatabase.LoadAllAssetsAtPath(path)
            .OfType<AnimationClip>()
            .Where(c => !c.name.StartsWith("__preview"))
            .ToArray();

        Debug.Log($"[Kawaii] {System.IO.Path.GetFileName(path)} clips: {string.Join(", ", clips.Select(c => c.name))}");

        var exact = clips.FirstOrDefault(c => c.name == preferredName);
        if (exact != null) return exact;

        return clips.FirstOrDefault(c =>
            c.name.IndexOf(preferredName, System.StringComparison.OrdinalIgnoreCase) >= 0
            && !c.name.Contains("Start")
            && !c.name.Contains("Stop")
            && !c.name.Contains("Pivot")) ?? clips.FirstOrDefault();
    }

    static AnimationClip LoadExact(string path, string name)
    {
        return AssetDatabase.LoadAllAssetsAtPath(path)
            .OfType<AnimationClip>()
            .FirstOrDefault(c => c.name == name);
    }

    static void SetLoop(AnimationClip clip, bool loop)
    {
        // 注意：对 FBX 导入的片段，这句只改内存里的对象，**不会写回 .meta**，
        // 重新导入就丢。真正的持久化在 EnsureLoopSettings()（改 ModelImporter）。
        // 这里保留是为了当前这次生成立刻生效。
        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = loop;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        EditorUtility.SetDirty(clip);
    }

    /// <summary>
    /// 把循环标记写进 FBX 的导入设置（ModelImporter.clipAnimations）并重新导入。
    /// 这是唯一能在重启 Unity 后依然生效的做法——`AnimationClip.loopTime` 对导入片段是易失的。
    /// </summary>
    static void EnsureLoopSettings()
    {
        EnsureLoop(IdlePath, true);
        EnsureLoop(WalkPath, true);
        EnsureLoop(WalkStartPath, false);
        EnsureLoop(WalkStopPath, false);
        EnsureLoop(WalkPivotPath, false);
        EnsureLoop(SitStartPath, false);
        EnsureLoop(SitLoopPath, true);
        EnsureLoop(SitEndPath, false);
        EnsureLoop(BowPath, false);
    }

    static void EnsureLoop(string path, bool loop)
    {
        if (!(AssetImporter.GetAtPath(path) is ModelImporter importer))
        {
            Debug.LogWarning($"[Kawaii] 不是 ModelImporter，跳过循环设置：{path}");
            return;
        }

        var clips = importer.clipAnimations;
        if (clips == null || clips.Length == 0)
        {
            clips = importer.defaultClipAnimations;
        }

        if (clips == null || clips.Length == 0)
        {
            Debug.LogWarning($"[Kawaii] 导入设置里没有片段，跳过：{path}");
            return;
        }

        var changed = false;
        foreach (var clip in clips)
        {
            if (clip.loopTime == loop)
            {
                continue;
            }

            clip.loopTime = loop;
            changed = true;
        }

        if (!changed)
        {
            return;
        }

        importer.clipAnimations = clips;
        importer.SaveAndReimport();
        Debug.Log($"[Kawaii] 导入设置 loop={loop} → {path}");
    }
}
