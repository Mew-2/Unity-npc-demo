using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 修场景接线：把"摸头/拍背"链路补回 NightRoom。
///
/// 背景：ReactionController 与 PlayerActionDetector 在场景里一个引用都没有（场景 guid 命中 0 次），
/// 所以点 NPC 不会触发任何反应——"摸头对话丢失"的真相是没有监听者，不是对话被吞。
///
/// 菜单：Tools/DawnAndDusk/Fix Scene Wiring
/// </summary>
public static class SceneWiringFix
{
    const string ScenePath = "Assets/Scenes/NightRoom.unity";

    [MenuItem("Tools/DawnAndDusk/Fix Scene Wiring")]
    public static void Fix()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var npc = Object.FindObjectOfType<NpcAgent>();
        var ui = Object.FindObjectOfType<DialogueUI>();
        var camera = Camera.main;
        var host = ui != null ? ui.gameObject : (npc != null ? npc.gameObject : null);

        if (npc == null || host == null)
        {
            Debug.LogError("[Wiring] 找不到 NpcAgent 或宿主对象，放弃");
            return;
        }

        var reaction = host.GetComponent<ReactionController>() ?? host.AddComponent<ReactionController>();
        reaction.npc = npc;
        reaction.dialogue = ui;
        EditorUtility.SetDirty(reaction);

        var detector = host.GetComponent<PlayerActionDetector>() ?? host.AddComponent<PlayerActionDetector>();
        detector.playerCamera = camera;
        detector.npc = npc;
        detector.reactions = reaction;
        EditorUtility.SetDirty(detector);

        EditorSceneManager.SaveScene(scene);
        Debug.Log($"[Wiring] 已挂载到 '{host.name}'：ReactionController(npc={npc.name}, dialogue={(ui != null ? ui.name : "<null>")})  " +
                  $"PlayerActionDetector(camera={(camera != null ? camera.name : "<null>")})");
        GameLogFixup();
    }

    static void GameLogFixup()
    {
        // 只在编辑器里输出提示，实际日志由运行时 GameLog 负责
        Debug.Log("[Wiring] 完成；进 Play 后点鲸鲸的头/背应触发反应并写入 Logs/game.jsonl");
    }
}
