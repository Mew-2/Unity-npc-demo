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

    const string IdlePath = Root + "/@KA_Idle01_breathing.FBX";
    const string WalkPath = Root + "/@KA_Walk01.FBX";
    const string WalkStartPath = Root + "/@KA_Walk01_Start.FBX";
    const string WalkStopPath = Root + "/@KA_Walk01_Stop.FBX";
    const string WalkPivotPath = Root + "/@KA_Walk01_Pivot.FBX";
    const string SitPath = Root + "/@KA_Sit_CrossLegs_Loop.FBX";
    const string BowPath = Root + "/@KA_Idle44_GreetingBow.FBX";

    [MenuItem("Tools/DawnAndDusk/Build Kawaii Animator")]
    public static void Build()
    {
        var idle = LoadClip(IdlePath, "Idle01_breathing");
        var walk = LoadClip(WalkPath, "KA_Walk01");
        var walkStart = LoadClip(WalkStartPath, "KA_Walk01_Start");
        var walkStop = LoadClip(WalkStopPath, "KA_Walk01_Stop");
        var pivot = LoadClip(WalkPivotPath, "KA_Walk01_Pivot");
        var sit = LoadClip(SitPath, "Sit_CrossLegs_Loop");
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
            startToLoop.exitTime = 0.8f;
            startToLoop.duration = 0.15f;

            if (stopState != null)
            {
                var startToStop = startState.AddTransition(stopState);
                startToStop.AddCondition(AnimatorConditionMode.Less, 0.05f, "Speed");
                startToStop.duration = 0.1f;
                startToStop.hasExitTime = false;
            }
        }

        // 收势只在"真的要停了"时进入：边走边刹车时留在 Walk 里放慢步频，
        // 否则 WalkStop 一进去片段步速就接近 0，人还差半米却原地静止（实测踩过）
        var outOfWalk = walkState.AddTransition(stopState != null ? stopState : idleState);
        outOfWalk.AddCondition(AnimatorConditionMode.Less, 0.25f, "SpeedRatio");
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

        if (sit != null)
        {
            var sitState = machine.AddState("Sit");
            sitState.motion = sit;

            var toSit = idleState.AddTransition(sitState);
            toSit.AddCondition(AnimatorConditionMode.If, 0f, "Sit");
            toSit.duration = 0.2f;
            toSit.hasExitTime = false;

            var outSit = sitState.AddTransition(idleState);
            outSit.AddCondition(AnimatorConditionMode.IfNot, 0f, "Sit");
            outSit.duration = 0.2f;
            outSit.hasExitTime = false;
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
        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = loop;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        EditorUtility.SetDirty(clip);
    }
}
