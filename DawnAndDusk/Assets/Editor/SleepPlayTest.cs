using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class SleepPlayTest
{
    const string ArmedKey = "SleepPlayTest.armed";
    static readonly string OutDir = Path.Combine(Application.dataPath, "../dsh-sleep-test");

    static SleepPlayTest()
    {
        if (SessionState.GetBool(ArmedKey, false))
        {
            EditorApplication.update += Tick;
        }
    }

    [MenuItem("Tools/DawnAndDusk/Run Sleep Test")]
    public static void Run()
    {
        KawaiiAnimatorBuilder.Build();
        ApartmentSceneBuilder.Build();
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
        executor.StartCoroutine(CheckSleep(executor));
    }

    static IEnumerator CheckSleep(CommandExecutor executor)
    {
        var failures = 0;
        var sleep = UnityEngine.Object.FindObjectOfType<SleepController>();
        var npc = UnityEngine.Object.FindObjectOfType<NpcAgent>();
        var driver = npc != null ? npc.GetComponentInChildren<NpcAnimationDriver>() : null;
        var animator = npc != null ? npc.GetComponentInChildren<Animator>() : null;
        var relay = animator != null ? animator.GetComponent<RootMotionRelay>() : null;
        var initialCamPos = Camera.main.transform.localPosition;
        var initialCamRot = Camera.main.transform.localRotation;

        void Check(bool condition, string name, string detail = "")
        {
            if (!condition)
            {
                failures++;
            }
            Debug.Log($"[SleepTest] {(condition ? "PASS" : "FAIL")} {name} {detail}");
            GameLog.Event("sleep_test", "check", name, "pass", condition ? "1" : "0", "detail", detail);
        }

        Check(sleep != null && npc != null && driver != null && relay != null, "refs");
        if (sleep == null || npc == null || driver == null || relay == null)
        {
            Finish(1);
            yield break;
        }

        LogBedRenderers();

        yield return Execute(executor, new[]
        {
            new DirectorClient.CommandDto { op = "set", target = "npc", state = "sleep" },
        });
        yield return WaitFor(() => sleep.IsInSleep, 30f);
        Check(sleep.IsInSleep, "first_sleep_enter", driver.CurrentStateName);
        Check(driver.CurrentStateName == "SleepLoop", "sleep_loop", driver.CurrentStateName);
        Check(!relay.relay, "sleep_root_motion_off");
        Check(!sleep.player.enabled, "sleep_player_locked");
        var head = animator.GetBoneTransform(HumanBodyBones.Head);
        var face = -head.forward;
        face.y = 0f;
        var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
        Check(head.position.x > hips.position.x, "sleep_head_at_bed_head",
            $"{head.position.x:F2},{hips.position.x:F2}");
        GameLog.Event(
            "sleep_face_probe",
            "head", head.position.ToString("F3"),
            "hips", hips.position.ToString("F3"),
            "fwd", head.forward.ToString("F3"),
            "up", head.up.ToString("F3"),
            "right", head.right.ToString("F3"),
            "root", npc.transform.eulerAngles.ToString("F3"),
            "bed", BedCenter().ToString("F3"));
        var rootY = npc.transform.position.y;
        yield return new WaitForSeconds(1.3f);
        Check(Mathf.Abs(npc.transform.position.y - rootY) < 0.03f, "sleep_root_stable");
        var toPlayer = Camera.main.transform.position - head.position;
        toPlayer.y = 0f;
        Check(Vector3.Dot(face.normalized, toPlayer.normalized) > 0.85f, "player_sees_face",
            GameLog.F(Vector3.Dot(face.normalized, toPlayer.normalized)));
        var bed = BedBounds();
        Check(bed.max.x - head.position.x < 1.0f && Mathf.Abs(Camera.main.transform.position.x - head.position.x) < 0.5f,
            "heads_near_pillows", $"{head.position.x:F2},{Camera.main.transform.position.x:F2}");
        var playerPos = Camera.main.transform.position;
        Check(playerPos.x > bed.min.x && playerPos.x < bed.max.x && playerPos.z > bed.min.z && playerPos.z < bed.max.z,
            "player_lies_on_bed", playerPos.ToString("F2"));
        var body = head.position - hips.position;
        body.y = 0f;
        Check(Mathf.Abs(Vector3.Dot(body.normalized, Vector3.right)) > 0.95f, "body_aligned_with_bed",
            GameLog.F(Vector3.Dot(body.normalized, Vector3.right)));
        Check(hips.position.z > bed.center.z && hips.position.z < bed.max.z, "npc_on_own_bed_half",
            hips.position.ToString("F2"));
        Transform tailRoot = null;
        Transform tailTip = null;
        var tailBones = new List<Transform>();
        foreach (var bone in npc.GetComponentsInChildren<Transform>(true))
        {
            if (bone.name == "DS_whale_tail_01") { tailRoot = bone; }
            if (bone.name == "DS_whale_tail_05_CharakitEnd") { tailTip = bone; }
            if (bone.name.StartsWith("DS_whale_tail", StringComparison.Ordinal)) { tailBones.Add(bone); }
        }
        var outside = tailTip != null && (tailTip.position.z < bed.min.z || tailTip.position.z > bed.max.z
            || tailTip.position.x < bed.min.x || tailTip.position.x > bed.max.x);
        Check(tailRoot != null && tailTip != null && outside && tailTip.position.y < tailRoot.position.y,
            "tail_droops_off_bed", tailTip != null ? tailTip.position.ToString("F2") : "missing");

        GameLog.Event("sleep_tail_probe",
            "root", tailRoot != null ? tailRoot.position.ToString("F3") : "missing",
            "tip", tailTip != null ? tailTip.position.ToString("F3") : "missing",
            "bedMin", bed.min.ToString("F3"),
            "bedMax", bed.max.ToString("F3"));

        // 骨骼点：床面投影内不得低于床面
        var boneInside = false;
        var worstBone = string.Empty;
        var worstBoneClearance = float.PositiveInfinity;
        foreach (var bone in tailBones)
        {
            var p = bone.position;
            if (!InsideXZ(p, bed)) { continue; }
            var clearance = p.y - bed.max.y;
            if (clearance < worstBoneClearance)
            {
                worstBoneClearance = clearance;
                worstBone = $"{bone.name}@{p:F2}";
            }
            if (clearance < -0.005f) { boneInside = true; }
        }
        Check(!boneInside, "tail_not_inside_bed",
            float.IsPositiveInfinity(worstBoneClearance) ? "none-over-bed" : $"{worstBone} c={worstBoneClearance:F3}");

        // 网格顶点：薄而宽的尾鳍只看骨骼会漏检（上一版就是这样过的）
        var tailRenderer = WhaleTailRenderer(npc);
        Check(tailRenderer != null, "tail_mesh_found",
            tailRenderer != null ? tailRenderer.gameObject.name : "missing");
        var meshClearance = TailMeshClearance(npc, bed, out var worstVertex, out var vertexCount);
        GameLog.Event("sleep_tail_mesh",
            "renderer", tailRenderer != null ? tailRenderer.gameObject.name : "missing",
            "vertsOverBed", vertexCount.ToString(),
            "inset", "0.10",
            "clearance", GameLog.F(meshClearance),
            "worst", worstVertex);
        Check(!float.IsNaN(meshClearance) && meshClearance >= -0.01f, "tail_mesh_clear_bed",
            float.IsNaN(meshClearance) ? "missing" : $"c={meshClearance:F3} {worstVertex}");
        var lowerArm = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
        Check(lowerArm.position.y < head.position.y - 0.12f, "sleep_sleeve_lowered",
            $"{lowerArm.position.y:F2},{head.position.y:F2}");
        CaptureViews(npc, animator, "01-sleep");
        CaptureView(Camera.main, "01-sleep-pov.png");

        // 睡姿循环往后走 2 秒再采一次，确认呼吸/弹簧不会把尾鳍压回床里
        yield return new WaitForSeconds(2f);
        var lateMeshClearance = TailMeshClearance(npc, bed, out var lateWorst, out _);
        GameLog.Event("sleep_tail_mesh_late",
            "clearance", GameLog.F(lateMeshClearance),
            "worst", lateWorst);
        Check(!float.IsNaN(lateMeshClearance) && lateMeshClearance >= -0.01f, "tail_mesh_clear_bed_late",
            float.IsNaN(lateMeshClearance) ? "missing" : $"c={lateMeshClearance:F3} {lateWorst}");

        yield return Execute(executor, new[]
        {
            new DirectorClient.CommandDto { op = "set", target = "player", state = "stand" },
        });
        yield return WaitFor(() => sleep.player.enabled && driver.CurrentStateName == "Idle", 12f);
        Check(sleep.player.enabled, "sleep_player_restored");
        Check(relay.relay, "sleep_root_motion_restored");
        Check(Vector3.Distance(Camera.main.transform.localPosition, initialCamPos) < 0.03f
            && Quaternion.Angle(Camera.main.transform.localRotation, initialCamRot) < 1f, "sleep_camera_restored");

        yield return Execute(executor, new[]
        {
            new DirectorClient.CommandDto { op = "set", target = "npc", state = "sleep" },
        });
        yield return WaitFor(() => sleep.IsInSleep, 30f);
        Check(sleep.IsInSleep, "second_sleep_enter", driver.CurrentStateName);

        yield return Execute(executor, new[]
        {
            new DirectorClient.CommandDto { op = "move", entity = "npc", anchor = "bed" },
        });
        Check(!sleep.IsActive, "move_exits_sleep");
        Check(sleep.player.enabled && relay.relay, "move_restores_control");
        Finish(failures == 0 ? 0 : 1);
    }

    static IEnumerator Execute(CommandExecutor executor, DirectorClient.CommandDto[] commands)
    {
        executor.StartCoroutine(executor.Execute(commands));
        while (executor.IsBusy)
        {
            yield return null;
        }
    }

    static IEnumerator WaitFor(Func<bool> predicate, float timeout)
    {
        var deadline = Time.time + timeout;
        while (!predicate() && Time.time < deadline)
        {
            yield return null;
        }
    }

    static Vector3 BedCenter() => BedBounds().center;

    static Bounds BedBounds()
    {
        Bounds? mattress = null;
        var volume = 0f;
        foreach (var renderer in UnityEngine.Object.FindObjectsOfType<Renderer>())
        {
            var name = renderer.gameObject.name.ToLowerInvariant();
            if (!name.Contains("bed_bedroom") || !(name.Contains("mattress") || name.Contains("cover")))
            {
                continue;
            }
            var v = renderer.bounds.size.x * renderer.bounds.size.z;
            if (v > volume)
            {
                volume = v;
                mattress = renderer.bounds;
            }
        }
        return mattress ?? new Bounds(Vector3.zero, Vector3.zero);
    }

    static void LogBedRenderers()
    {
        foreach (var renderer in UnityEngine.Object.FindObjectsOfType<Renderer>())
        {
            var name = renderer.gameObject.name;
            if (name.IndexOf("Bed_Bedroom", StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }
            GameLog.Event("sleep_bed_renderer",
                "name", name,
                "min", renderer.bounds.min.ToString("F2"),
                "max", renderer.bounds.max.ToString("F2"));
        }
    }

    static bool InsideXZ(Vector3 p, Bounds bed, float inset = 0f)
    {
        return p.x > bed.min.x + inset && p.x < bed.max.x - inset
            && p.z > bed.min.z + inset && p.z < bed.max.z - inset;
    }

    static SkinnedMeshRenderer WhaleTailRenderer(NpcAgent npc)
    {
        foreach (var renderer in npc.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var mesh = renderer.sharedMesh;
            if (mesh == null) { continue; }
            var meshName = mesh.name.ToLowerInvariant();
            var goName = renderer.gameObject.name.ToLowerInvariant();
            if (meshName.Contains("body_00004") || meshName.Contains("whale_tail") || goName.Contains("whale_tail"))
            {
                return renderer;
            }
        }
        return null;
    }

    static float TailMeshClearance(NpcAgent npc, Bounds bed, out string worstVertex, out int vertexCount)
    {
        worstVertex = string.Empty;
        vertexCount = 0;
        var renderer = WhaleTailRenderer(npc);
        if (renderer == null)
        {
            return float.NaN;
        }

        var mesh = new Mesh();
        try
        {
            renderer.BakeMesh(mesh);
            var vertices = mesh.vertices;
            var matrix = renderer.transform.localToWorldMatrix;
            var clearance = float.PositiveInfinity;
            for (var i = 0; i < vertices.Length; i++)
            {
                var p = matrix.MultiplyPoint3x4(vertices[i]);
                if (!InsideXZ(p, bed))
                {
                    continue;
                }

                vertexCount++;
                var c = p.y - bed.max.y;
                if (!InsideXZ(p, bed, 0.10f))
                {
                    continue;
                }
                if (c < clearance)
                {
                    clearance = c;
                    worstVertex = $"{renderer.name}@{p:F3}";
                }
            }
            return clearance;
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(mesh);
        }
    }

    static void CaptureViews(NpcAgent npc, Animator animator, string file)
    {
        Directory.CreateDirectory(OutDir);
        var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
        var target = hips != null ? hips.position + Vector3.up * 0.15f : npc.transform.position;
        var cameraGo = new GameObject("SleepTestCamera");
        var camera = cameraGo.AddComponent<Camera>();
        camera.CopyFrom(Camera.main);
        var poses = new[]
        {
            new Vector3(0f, 0.8f, -1.6f),
            new Vector3(-1.6f, 0.8f, -0.3f),
            new Vector3(1.1f, 1.0f, -1.1f),
        };
        var names = new[] { "front", "side", "oblique" };
        for (var i = 0; i < poses.Length; i++)
        {
            camera.transform.position = npc.transform.position + poses[i];
            camera.transform.rotation = Quaternion.LookRotation(target - camera.transform.position, Vector3.up);
            CaptureView(camera, $"{file}-{names[i]}.png");
        }

        var bed = BedBounds();
        var hipsPos = hips != null ? hips.position : npc.transform.position;

        // 窗边过道斜机位（+Z / 床尾角）：看尾巴越过床沿后的自然下垂
        var aisleZ = Mathf.Min(bed.max.z + 0.15f, 3.30f);
        camera.transform.position = new Vector3(bed.min.x - 0.45f, bed.max.y + 1.05f, aisleZ);
        camera.transform.rotation = Quaternion.LookRotation(target - camera.transform.position, Vector3.up);
        CaptureView(camera, $"{file}-aisle.png");

        // 床尾低机位（-X 侧）：看尾鳍是否压进床垫
        camera.transform.position = new Vector3(bed.min.x - 0.95f, bed.max.y + 0.45f, hipsPos.z);
        camera.transform.rotation = Quaternion.LookRotation(target - camera.transform.position, Vector3.up);
        CaptureView(camera, $"{file}-foot.png");

        // 俯视：看整条尾巴在床面投影内的走向
        camera.transform.position = new Vector3(hipsPos.x, bed.max.y + 2.2f, hipsPos.z);
        camera.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        CaptureView(camera, $"{file}-top.png");

        UnityEngine.Object.Destroy(cameraGo);
    }

    static void CaptureView(Camera camera, string file)
    {
        Directory.CreateDirectory(OutDir);
        var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        var texture = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        camera.targetTexture = rt;
        camera.Render();
        RenderTexture.active = rt;
        texture.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
        texture.Apply();
        RenderTexture.active = null;
        camera.targetTexture = null;
        File.WriteAllBytes(Path.Combine(OutDir, file), texture.EncodeToPNG());
        UnityEngine.Object.Destroy(rt);
        UnityEngine.Object.Destroy(texture);
    }

    static void Finish(int exitCode)
    {
        Debug.Log($"[SleepTest] {(exitCode == 0 ? "PASS" : "FAIL")} finished");
        GameLog.Event("sleep_test", "result", exitCode == 0 ? "pass" : "fail");
        SessionState.SetBool(ArmedKey, false);
        SessionState.SetBool(ArmedKey + ".started", false);
        EditorApplication.update -= Tick;
        EditorApplication.ExitPlaymode();
        EditorApplication.delayCall += () => EditorApplication.Exit(exitCode);
    }
}
