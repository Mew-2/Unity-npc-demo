using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 无头 Play 测试：自动进 Play、按脚本触发互动、采完日志再退出。
/// 目的是把"摸头对话丢失""转过去后呼吸停"这类只在 Play 里出现的问题变成可复现数据。
///
/// 用法（Unity 必须没开）：
///   Unity.exe -batchmode -projectPath DawnAndDusk -executeMethod KawaiiPlayTest.Run
/// 或菜单：Tools/DawnAndDusk/Run Play Test
///
/// 时间线（秒，从进入 Play 起算）：
///   1.5  摸头（pat_head）
///   3.0  再摸一次（验证 cooldown 是否吞掉）
///   5.0  转过去（TurnTo bed）
///   6.0~20.0  静置，观察 Idle 是否还在推进（呼吸停=normalizedTime 不动）
///   20.0 发一句对话（若服务端在 8000 则走 /direct）
///   30.0 退出 Play 并退出编辑器
/// </summary>
[InitializeOnLoad]
public static class KawaiiPlayTest
{
    const string ArmedKey = "KawaiiPlayTest.armed";

    static KawaiiPlayTest()
    {
        if (SessionState.GetBool(ArmedKey, false))
        {
            EditorApplication.update += Tick;
        }
    }

    [MenuItem("Tools/DawnAndDusk/Run Play Test")]
    public static void Run()
    {
        SessionState.SetBool(ArmedKey, true);
        SessionState.SetString("KawaiiPlayTest.mode", "full");
        EditorSceneManager.OpenScene("Assets/Scenes/NightRoom.unity");
        Edge("start");
        EditorApplication.update += Tick;
        EditorApplication.EnterPlaymode();
    }

    /// <summary>
    /// 卡死二分用：空场景进 Play 3 秒再退出，判断"退出卡死"是不是本项目/VRM 特有。
    /// </summary>
    [MenuItem("Tools/DawnAndDusk/Run Exit Bisect")]
    public static void Bisect()
    {
        SessionState.SetBool(ArmedKey, true);
        SessionState.SetString("KawaiiPlayTest.mode", "bisect");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        Edge("bisect_start");
        EditorApplication.update += Tick;
        EditorApplication.EnterPlaymode();
    }

    static void Step(string name)
    {
        if (SessionState.GetBool("KawaiiPlayTest.step." + name, false))
        {
            return;
        }

        SessionState.SetBool("KawaiiPlayTest.step." + name, true);
        Edge("step:" + name);
    }

    static void Tick()
    {
        if (!EditorApplication.isPlaying)
        {
            return;
        }

        var t = Time.timeSinceLevelLoad;

        if (t > 1.5f && t < 1.6f)
        {
            Step("pat1");
            var reaction = Object.FindObjectOfType<ReactionController>();
            Edge("reaction_found:" + (reaction != null));
            if (reaction != null)
            {
                reaction.React("pat_head");
            }
        }

        if (t > 3.0f && t < 3.1f)
        {
            Step("pat2");
            var reaction = Object.FindObjectOfType<ReactionController>();
            if (reaction != null)
            {
                reaction.React("pat_head");   // 第二次：验证 cooldown 会不会把台词吞掉
            }
        }

        if (t > 5.0f && t < 5.1f)
        {
            Step("turn");
            var npc = Object.FindObjectOfType<NpcAgent>();
            Edge("npc_found:" + (npc != null));
            if (npc != null)
            {
                npc.TurnTo("bed");   // 转过去：之后观察 Idle 是否还在推进
            }
        }

        if (t > 20.0f && t < 20.1f)
        {
            Step("talk");
            var ui = Object.FindObjectOfType<DialogueUI>();
            Edge("ui_found:" + (ui != null));
            if (ui != null)
            {
                ui.Submit("去窗边");
            }
        }

        var mode = SessionState.GetString("KawaiiPlayTest.mode", "full");
        var endAt = mode == "bisect" ? 3.0f : 30.0f;
        if (t > endAt)
        {
            Step("finish");
            EditorApplication.update -= Tick;
            SessionState.SetBool(ArmedKey, false);
            EditorApplication.ExitPlaymode();
            EditorApplication.delayCall += () =>
            {
                GameLog.Event("playtest_end", "mode", mode);
                EditorApplication.Exit(0);
            };
        }
    }

    static void Edge(string message)
    {
        Debug.Log("[PlayTest] " + message);
        GameLog.Event("playtest", "what", message);
    }
}
