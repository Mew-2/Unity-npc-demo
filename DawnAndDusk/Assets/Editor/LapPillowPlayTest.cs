using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UniVRM10;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class LapPillowPlayTest
{
    const string ArmedKey = "LapPillowPlayTest.armed";
    static readonly string OutDir = Path.Combine(Application.dataPath, "../dsh-lap-test");

    static LapPillowPlayTest()
    {
        if (SessionState.GetBool(ArmedKey, false))
        {
            EditorApplication.update += Tick;
        }
    }

    [MenuItem("Tools/DawnAndDusk/Run Lap Pillow Test")]
    public static void Run()
    {
        SessionState.SetBool(ArmedKey, true);
        EditorSceneManager.OpenScene("Assets/_LocalTest/ArchViz/3D SCENE/ApartmentDemo.unity");
        EditorApplication.update += Tick;
        EditorApplication.EnterPlaymode();
    }

    static void Tick()
    {
        if (!EditorApplication.isPlaying || Time.timeSinceLevelLoad < 1.5f)
        {
            return;
        }

        if (SessionState.GetBool(ArmedKey + ".started", false))
        {
            return;
        }

        SessionState.SetBool(ArmedKey + ".started", true);
        var executor = UnityEngine.Object.FindObjectOfType<CommandExecutor>();
        if (executor == null)
        {
            Finish(1);
            return;
        }

        executor.StartCoroutine(CheckLapTwice(executor));
    }

    static IEnumerator CheckLapTwice(CommandExecutor executor)
    {
        var failures = 0;
        var lap = UnityEngine.Object.FindObjectOfType<LapPillowController>();
        var npc = UnityEngine.Object.FindObjectOfType<NpcAgent>();
        var driver = npc != null ? npc.GetComponentInChildren<NpcAnimationDriver>() : null;
        var animator = npc != null ? npc.GetComponentInChildren<Animator>() : null;
        var relay = animator != null ? animator.GetComponent<RootMotionRelay>() : null;
        var vrm = npc != null ? npc.GetComponentInChildren<Vrm10Instance>() : null;

        void Check(bool condition, string name, string detail = "")
        {
            if (!condition)
            {
                failures++;
            }

            Debug.Log($"[LapTest] {(condition ? "PASS" : "FAIL")} {name} {detail}");
            GameLog.Event("lap_test", "check", name, "pass", condition ? "1" : "0", "detail", detail);
        }

        Check(lap != null && npc != null && driver != null && relay != null && vrm != null, "refs");
        if (lap == null || npc == null || driver == null || relay == null || vrm == null)
        {
            Finish(1);
            yield break;
        }

        var originalGroups = SkirtGroupCount(vrm);
        var foldConstraints = GetFoldConstraints(npc);
        var originalFoldWeights = foldConstraints.ConvertAll(constraint => constraint.Weight);
        Check(originalGroups > 0, "skirt_groups_found", originalGroups.ToString());
        Check(foldConstraints.Count == 8, "skirt_fold_constraints_found", foldConstraints.Count.ToString());
        Check(CharacterBoundsUpdateEnabled(npc), "character_bounds_update");

        yield return Execute(executor, LapCommands());
        var seated = false;
        var maxSitRootShift = 0f;
        yield return WaitForSitLoopTrackingHeight(
            npc, driver, 20f, result => seated = result, shift => maxSitRootShift = shift);
        Check(seated, "first_sit", driver.CurrentStateName);
        Check(maxSitRootShift <= 0.03f, "first_sit_height_stable", GameLog.F(maxSitRootShift));
        Check(npc.transform.position.x > 4f && npc.transform.position.z > 2.3f, "seat_at_head_left",
            $"{npc.transform.position.x:F2},{npc.transform.position.z:F2}");
        if (!seated)
        {
            Finish(1);
            yield break;
        }

        yield return new WaitForSeconds(1.5f);
        Check(SkirtGroupCount(vrm) == originalGroups, "first_skirt_leg_collisions_on", SkirtGroupCount(vrm).ToString());
        Check(FoldWeightsMatch(foldConstraints, LapPillowController.SeatedSkirtFoldWeight), "first_skirt_fold_weight");
        Check(!relay.relay, "first_root_motion_off");
        LogArmPose(animator);
        Check(ArmsOutward(animator), "arms_clear_torso");
        Check(HandsFollowThigh(animator), "hands_follow_thigh");
        Check(HandsClearThigh(animator), "hands_clear_thigh");
        Check(HandsSeparated(animator), "hands_separated");
        var tailClearance = TailClearance(npc);
        Check(!float.IsNaN(tailClearance) && tailClearance >= 0.08f, "tail_clear_bed", GameLog.F(tailClearance));
        Capture(npc, animator, "01-first-sit.png");
        MovePlayerToLap(lap, npc);
        yield return new WaitForSeconds(0.35f);
        CaptureView(Camera.main, "01t-transition-0.png");
        yield return new WaitForSeconds(0.35f);
        CaptureView(Camera.main, "01t-transition-1.png");
        yield return new WaitForSeconds(0.35f);
        CaptureView(Camera.main, "01t-transition-2.png");
        yield return new WaitForSeconds(0.45f);
        Check(!lap.player.enabled, "first_pov_entered");
        Check(CameraOnThigh(lap, animator), "first_pov_head_on_thigh");
        CaptureView(Camera.main, "01p-first-lap-pov.png");
        CaptureLayout("01o-layout.png");
        CaptureHairAngles(npc, animator);
        yield return Execute(executor, SingCommands());
        Check(lap.lullabyClip == null ? !lap.LullabyPlaying : lap.LullabyPlaying, "lullaby_play_or_text_fallback");

        var exitedAt = Time.time;
        yield return Execute(executor, StandCommands());
        Check(!lap.IsActive, "exit");
        Check(!lap.LullabyPlaying, "lullaby_stop_on_exit");

        executor.StartCoroutine(executor.Execute(LapCommands()));
        yield return null;
        Check(Time.time - exitedAt < 1f, "fast_reentry", GameLog.F(Time.time - exitedAt));
        Check(!npc.IsMoving || relay.relay, "reentry_root_motion_restored", npc.IsMoving ? "walking" : "already_seated");
        while (executor.IsBusy)
        {
            yield return null;
        }

        seated = false;
        yield return WaitForSitLoop(driver, 25f, result => seated = result);
        Check(seated, "second_sit", driver.CurrentStateName);
        if (!seated)
        {
            Finish(1);
            yield break;
        }

        yield return new WaitForSeconds(0.8f);
        var yawError = Mathf.Abs(Mathf.DeltaAngle(npc.transform.eulerAngles.y, LapPillowController.SeatYaw));
        Check(yawError < 0.1f, "second_sit_yaw_locked", GameLog.F(yawError));
        Check(SkirtGroupCount(vrm) == originalGroups, "second_skirt_leg_collisions_on", SkirtGroupCount(vrm).ToString());
        Check(FoldWeightsMatch(foldConstraints, LapPillowController.SeatedSkirtFoldWeight), "second_skirt_fold_weight");
        Capture(npc, animator, "02-second-sit.png");
        MovePlayerToLap(lap, npc);
        yield return new WaitForSeconds(1.5f);
        Check(!lap.player.enabled, "second_pov_entered");
        Check(CameraOnThigh(lap, animator), "second_pov_head_on_thigh");
        CaptureView(Camera.main, "02p-second-lap-pov.png");

        yield return Execute(executor, StandCommands());
        yield return new WaitForSeconds(1.2f);
        Check(SkirtGroupCount(vrm) == originalGroups, "skirt_groups_restored", SkirtGroupCount(vrm).ToString());
        Check(FoldWeightsMatch(foldConstraints, originalFoldWeights), "skirt_fold_weights_restored");
        Check(relay.relay, "root_motion_restored");
        Finish(failures == 0 ? 0 : 1);
    }

    static DirectorClient.CommandDto[] LapCommands()
    {
        return new[]
        {
            new DirectorClient.CommandDto { op = "set", target = "npc", state = "lap" },
            new DirectorClient.CommandDto { op = "look_at", entity = "npc", target = "player" },
        };
    }

    static DirectorClient.CommandDto[] SingCommands()
    {
        return new[]
        {
            new DirectorClient.CommandDto { op = "set", target = "npc", state = "sing" },
        };
    }

    static DirectorClient.CommandDto[] StandCommands()
    {
        return new[]
        {
            new DirectorClient.CommandDto { op = "set", target = "player", state = "stand" },
        };
    }

    static IEnumerator Execute(CommandExecutor executor, DirectorClient.CommandDto[] commands)
    {
        executor.StartCoroutine(executor.Execute(commands));
        while (executor.IsBusy)
        {
            yield return null;
        }
    }

    static IEnumerator WaitForSitLoop(NpcAnimationDriver driver, float timeout, Action<bool> done)
    {
        var deadline = Time.time + timeout;
        while (driver.CurrentStateName != "SitLoop" && Time.time < deadline)
        {
            yield return null;
        }

        done(driver.CurrentStateName == "SitLoop");
    }

    static IEnumerator WaitForSitLoopTrackingHeight(
        NpcAgent npc, NpcAnimationDriver driver, float timeout, Action<bool> done, Action<float> maxShift)
    {
        var tracking = false;
        var initialY = 0f;
        var max = 0f;
        var capturedStart = false;
        var animator = npc.GetComponentInChildren<Animator>();

        void Track()
        {
            var enteringSit = animator.IsInTransition(0) && animator.GetNextAnimatorStateInfo(0).IsName("SitLoop");
            if (enteringSit && !capturedStart)
            {
                capturedStart = true;
                Capture(npc, animator, "01s-sit-start.png");
            }

            if (driver.CurrentStateName != "SitStart" && driver.CurrentStateName != "SitLoop")
            {
                return;
            }

            if (!tracking)
            {
                tracking = true;
                initialY = npc.transform.position.y;
            }

            max = Mathf.Max(max, Mathf.Abs(npc.transform.position.y - initialY));
        }

        var deadline = Time.time + timeout;
        while (driver.CurrentStateName != "SitLoop" && Time.time < deadline)
        {
            Track();
            yield return null;
        }

        deadline = Time.time + 5f;
        while (animator.IsInTransition(0) && Time.time < deadline)
        {
            Track();
            yield return null;
        }

        for (var i = 0; i < 3; i++)
        {
            Track();
            yield return null;
        }

        maxShift(max);
        done(driver.CurrentStateName == "SitLoop");
    }

    static float TailClearance(NpcAgent npc)
    {
        var surfaceY = float.NaN;
        var mattressVolume = 0f;
        foreach (var renderer in UnityEngine.Object.FindObjectsOfType<Renderer>())
        {
            var name = renderer.gameObject.name.ToLowerInvariant();
            if (!name.Contains("bed_bedroom") || !(name.Contains("mattress") || name.Contains("cover")))
            {
                continue;
            }

            var volume = renderer.bounds.size.x * renderer.bounds.size.z;
            if (volume > mattressVolume)
            {
                mattressVolume = volume;
                surfaceY = renderer.bounds.max.y;
            }
        }

        if (float.IsNaN(surfaceY))
        {
            return float.NaN;
        }

        var clearance = float.NaN;
        foreach (var bone in npc.GetComponentsInChildren<Transform>(true))
        {
            if (!bone.name.StartsWith("DS_whale_tail_", StringComparison.Ordinal))
            {
                continue;
            }

            var boneClearance = bone.position.y - surfaceY;
            clearance = float.IsNaN(clearance) ? boneClearance : Mathf.Min(clearance, boneClearance);
        }

        return clearance;
    }

    static bool IsSkirt(Vrm10InstanceSpringBone.Spring spring)
    {
        return spring.Name != null && spring.Name.StartsWith("skirt", StringComparison.OrdinalIgnoreCase);
    }

    static int SkirtGroupCount(Vrm10Instance vrm)
    {
        var count = 0;
        foreach (var spring in vrm.SpringBone.Springs)
        {
            if (IsSkirt(spring))
            {
                count += spring.ColliderGroups.Count;
            }
        }

        return count;
    }

    static List<Vrm10RotationConstraint> GetFoldConstraints(NpcAgent npc)
    {
        var result = new List<Vrm10RotationConstraint>();
        foreach (var constraint in npc.GetComponentsInChildren<Vrm10RotationConstraint>(true))
        {
            if (constraint.gameObject.name.IndexOf("skirtFold", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                result.Add(constraint);
            }
        }

        return result;
    }

    static bool FoldWeightsMatch(List<Vrm10RotationConstraint> constraints, float expected)
    {
        foreach (var constraint in constraints)
        {
            if (Mathf.Abs(constraint.Weight - expected) > 0.0001f)
            {
                return false;
            }
        }

        return true;
    }

    static bool FoldWeightsMatch(List<Vrm10RotationConstraint> constraints, List<float> expected)
    {
        if (constraints.Count != expected.Count)
        {
            return false;
        }

        for (var i = 0; i < constraints.Count; i++)
        {
            if (Mathf.Abs(constraints[i].Weight - expected[i]) > 0.0001f)
            {
                return false;
            }
        }

        return true;
    }

    static bool CameraOnThigh(LapPillowController lap, Animator animator)
    {
        var leftUpper = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
        var rightUpper = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
        var leftLower = animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
        var rightLower = animator.GetBoneTransform(HumanBodyBones.RightLowerLeg);
        if (leftUpper == null || rightUpper == null || leftLower == null || rightLower == null)
        {
            return false;
        }

        var thigh = (leftUpper.position + rightUpper.position + leftLower.position + rightLower.position) * 0.25f;
        var expected = thigh
            + Vector3.up * (lap.lapHeight + lap.cameraLift)
            + lap.npc.transform.forward * LapPillowController.CameraForwardOffset
            + lap.npc.transform.right * LapPillowController.CameraSideOffset;
        return Camera.main != null && Vector3.Distance(Camera.main.transform.position, expected) < 0.03f;
    }

    static void MovePlayerToLap(LapPillowController lap, NpcAgent npc)
    {
        var player = lap.player.transform;
        player.position = new Vector3(npc.transform.position.x - 0.65f, player.position.y, npc.transform.position.z);
    }

    static void Capture(NpcAgent npc, Animator animator, string file)
    {
        Directory.CreateDirectory(OutDir);
        var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
        var target = hips != null ? hips.position + Vector3.up * 0.2f : npc.transform.position;
        var cameraGo = new GameObject("LapTestCamera");
        var camera = cameraGo.AddComponent<Camera>();
        if (Camera.main != null)
        {
            camera.CopyFrom(Camera.main);
        }

        camera.transform.position = npc.transform.position
            + npc.transform.forward * 0.85f
            + npc.transform.right * 0.1f
            + Vector3.up * 0.35f;
        camera.transform.rotation = Quaternion.LookRotation(target - camera.transform.position, Vector3.up);
        CaptureView(camera, file);
        camera.transform.position = npc.transform.position
            - npc.transform.right * 0.85f
            + npc.transform.forward * 0.15f
            + Vector3.up * 0.35f;
        camera.transform.rotation = Quaternion.LookRotation(target - camera.transform.position, Vector3.up);
        CaptureView(camera, Path.GetFileNameWithoutExtension(file) + "-side.png");
        camera.transform.position = npc.transform.position
            - npc.transform.right * 0.65f
            + npc.transform.forward * 0.65f
            + Vector3.up * 0.45f;
        camera.transform.rotation = Quaternion.LookRotation(target - camera.transform.position, Vector3.up);
        CaptureView(camera, Path.GetFileNameWithoutExtension(file) + "-oblique.png");
        UnityEngine.Object.Destroy(cameraGo);
    }

    static bool ArmsOutward(Animator animator)
    {
        foreach (var side in new[] { "Left", "Right" })
        {
            var upper = animator.GetBoneTransform(side == "Left" ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
            var elbow = animator.GetBoneTransform(side == "Left" ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
            if (upper == null || elbow == null)
            {
                return false;
            }

            var outward = Mathf.Abs(Vector3.Dot((elbow.position - upper.position).normalized, animator.transform.right));
            if (outward < 0.03f)
            {
                return false;
            }
        }

        return true;
    }

    static bool HandsSeparated(Animator animator)
    {
        var left = animator.GetBoneTransform(HumanBodyBones.LeftHand);
        var right = animator.GetBoneTransform(HumanBodyBones.RightHand);
        return left != null && right != null && Vector3.Distance(left.position, right.position) >= 0.08f;
    }

    static bool HandsClearThigh(Animator animator)
    {
        foreach (var side in new[] { "Left", "Right" })
        {
            var hand = animator.GetBoneTransform(side == "Left" ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
            var knee = animator.GetBoneTransform(side == "Left" ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg);
            if (hand == null || knee == null || hand.position.y - knee.position.y < 0.08f)
            {
                return false;
            }
        }

        return true;
    }

    static bool HandsFollowThigh(Animator animator)
    {
        foreach (var side in new[] { "Left", "Right" })
        {
            var hand = animator.GetBoneTransform(side == "Left" ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
            var finger = animator.GetBoneTransform(side == "Left" ? HumanBodyBones.LeftMiddleProximal : HumanBodyBones.RightMiddleProximal);
            var upper = animator.GetBoneTransform(side == "Left" ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg);
            var knee = animator.GetBoneTransform(side == "Left" ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg);
            if (hand == null || finger == null || upper == null || knee == null)
            {
                return false;
            }

            var alignment = Vector3.Dot((finger.position - hand.position).normalized, (knee.position - upper.position).normalized);
            if (alignment < 0.98f)
            {
                return false;
            }
        }

        return true;
    }

    static void LogArmPose(Animator animator)
    {
        var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
        if (hips == null)
        {
            return;
        }

        foreach (var side in new[] { "Left", "Right" })
        {
            var upper = animator.GetBoneTransform(side == "Left" ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
            var lower = animator.GetBoneTransform(side == "Left" ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
            var hand = animator.GetBoneTransform(side == "Left" ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
            var knee = animator.GetBoneTransform(side == "Left" ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg);
            if (upper == null || lower == null || hand == null || knee == null)
            {
                continue;
            }

            GameLog.Event(
                "lap_arm_pose",
                "side", side,
                "upperY", GameLog.F(upper.position.y - hips.position.y),
                "elbowY", GameLog.F(lower.position.y - hips.position.y),
                "handY", GameLog.F(hand.position.y - hips.position.y),
                "handAboveKnee", GameLog.F(hand.position.y - knee.position.y),
                "elbowToHand", GameLog.F(Vector3.Distance(lower.position, hand.position)),
                "upperOutward", GameLog.F(Mathf.Abs(Vector3.Dot((lower.position - upper.position).normalized, animator.transform.right))));
        }
    }

    static bool CharacterBoundsUpdateEnabled(NpcAgent npc)
    {
        var count = 0;
        foreach (var renderer in npc.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            count++;
            if (!renderer.updateWhenOffscreen)
            {
                return false;
            }
        }

        return count > 0;
    }

    static void CaptureHairAngles(NpcAgent npc, Animator animator)
    {
        var head = animator.GetBoneTransform(HumanBodyBones.Head);
        if (head == null)
        {
            return;
        }

        var offsets = new[]
        {
            new Vector3(0f, 0.08f, 0.7f),
            new Vector3(0.6f, 0.12f, 0.3f),
            new Vector3(-0.6f, 0.12f, 0.3f),
        };
        for (var i = 0; i < offsets.Length; i++)
        {
            var cameraGo = new GameObject("HairTestCamera");
            var camera = cameraGo.AddComponent<Camera>();
            if (Camera.main != null)
            {
                camera.CopyFrom(Camera.main);
            }

            camera.transform.position = head.position + npc.transform.rotation * offsets[i];
            camera.transform.rotation = Quaternion.LookRotation(head.position - camera.transform.position, Vector3.up);
            CaptureView(camera, $"01h-hair-{i}.png");
            UnityEngine.Object.Destroy(cameraGo);
        }
    }

    static void CaptureLayout(string file)
    {
        var cameraGo = new GameObject("LapLayoutCamera");
        var camera = cameraGo.AddComponent<Camera>();
        if (Camera.main != null)
        {
            camera.CopyFrom(Camera.main);
        }

        camera.transform.position = new Vector3(2f, 3.1f, 0.1f);
        camera.transform.rotation = Quaternion.LookRotation(new Vector3(4f, 0.65f, 2.5f) - camera.transform.position, Vector3.up);
        CaptureView(camera, file);
        UnityEngine.Object.Destroy(cameraGo);
    }

    static void CaptureView(Camera camera, string file)
    {
        const int width = 1280;
        const int height = 720;
        var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
        camera.targetTexture = rt;
        camera.Render();
        RenderTexture.active = rt;
        texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        texture.Apply();
        RenderTexture.active = null;
        camera.targetTexture = null;
        File.WriteAllBytes(Path.Combine(OutDir, file), texture.EncodeToPNG());
        UnityEngine.Object.Destroy(rt);
        UnityEngine.Object.Destroy(texture);
    }

    static void Finish(int exitCode)
    {
        Debug.Log($"[LapTest] {(exitCode == 0 ? "PASS" : "FAIL")} finished");
        GameLog.Event("lap_test", "result", exitCode == 0 ? "pass" : "fail");
        SessionState.SetBool(ArmedKey, false);
        SessionState.SetBool(ArmedKey + ".started", false);
        EditorApplication.update -= Tick;
        EditorApplication.ExitPlaymode();
        EditorApplication.delayCall += () => EditorApplication.Exit(exitCode);
    }
}
