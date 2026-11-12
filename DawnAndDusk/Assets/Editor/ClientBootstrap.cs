using UnityEditor;
using UnityEngine;

/// <summary>
/// 一键装配客户端：生成 Director 物体，挂上 DirectorClient / CommandExecutor / DialogueUI 并连线。
/// 菜单：Tools/DawnAndDusk/Setup Client
/// </summary>
public static class ClientBootstrap
{
    [MenuItem("Tools/DawnAndDusk/Setup Client")]
    public static void Setup()
    {
        var graph = Object.FindObjectOfType<AnchorGraph>();
        var npc = Object.FindObjectOfType<NpcAgent>();

        if (graph == null || npc == null)
        {
            Debug.LogError("[ClientBootstrap] 场景缺少 AnchorGraph 或 NpcAgent，先跑 Build Greybox。");
            return;
        }

        var go = GameObject.Find("Director");
        if (go == null)
        {
            go = new GameObject("Director");
        }

        var client = go.GetComponent<DirectorClient>();
        if (client == null)
        {
            client = go.AddComponent<DirectorClient>();
        }
        client.graph = graph;

        var executor = go.GetComponent<CommandExecutor>();
        if (executor == null)
        {
            executor = go.AddComponent<CommandExecutor>();
        }
        executor.npc = npc;
        executor.graph = graph;

        var stateSwitcher = go.GetComponent<StateSwitcher>();
        if (stateSwitcher == null)
        {
            stateSwitcher = go.AddComponent<StateSwitcher>();
        }
        stateSwitcher.graph = graph;
        stateSwitcher.npc = npc;
        executor.state = stateSwitcher;

        var ui = go.GetComponent<DialogueUI>();
        if (ui == null)
        {
            ui = go.AddComponent<DialogueUI>();
        }
        ui.client = client;
        ui.executor = executor;
        ui.npc = npc;

        var characterSwitcher = go.GetComponent<CharacterSwitcher>();
        if (characterSwitcher == null)
        {
            characterSwitcher = go.AddComponent<CharacterSwitcher>();
        }
        characterSwitcher.npc = npc;
        characterSwitcher.client = client;

        var reactions = go.GetComponent<ReactionController>();
        if (reactions == null)
        {
            reactions = go.AddComponent<ReactionController>();
        }
        reactions.npc = npc;
        reactions.dialogue = ui;

        var detector = go.GetComponent<PlayerActionDetector>();
        if (detector == null)
        {
            detector = go.AddComponent<PlayerActionDetector>();
        }
        detector.playerCamera = Camera.main;
        detector.npc = npc;
        detector.reactions = reactions;

        Selection.activeGameObject = go;
        Debug.Log("[ClientBootstrap] 装配完成：DirectorClient + CommandExecutor + DialogueUI + 反应系统。");
    }
}
