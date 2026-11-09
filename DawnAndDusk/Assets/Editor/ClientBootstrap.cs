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

        var switcher = go.GetComponent<StateSwitcher>();
        if (switcher == null)
        {
            switcher = go.AddComponent<StateSwitcher>();
        }
        switcher.graph = graph;
        switcher.npc = npc;
        executor.state = switcher;

        var ui = go.GetComponent<DialogueUI>();
        if (ui == null)
        {
            ui = go.AddComponent<DialogueUI>();
        }
        ui.client = client;
        ui.executor = executor;
        ui.npc = npc;

        Selection.activeGameObject = go;
        Debug.Log("[ClientBootstrap] 装配完成：DirectorClient + CommandExecutor + DialogueUI。");
    }
}
