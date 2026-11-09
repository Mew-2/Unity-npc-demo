using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// 逐条执行服务端返回的指令。
/// 关键点：每条都要等它完成再执行下一条（否则 move + look_at 会叠在一起）。
/// spawn 已延后（v1 不做灯灵），set 交给 StateSwitcher。
/// </summary>
public class CommandExecutor : MonoBehaviour
{
    [Header("依赖")]
    public NpcAgent npc;
    public AnchorGraph graph;
    public StateSwitcher state;

    public bool IsBusy { get; private set; }

    public IEnumerator Execute(DirectorClient.CommandDto[] commands)
    {
        if (commands == null)
        {
            yield break;
        }

        IsBusy = true;

        foreach (var command in commands)
        {
            if (command == null || string.IsNullOrEmpty(command.op))
            {
                continue;
            }

            switch (command.op)
            {
                case "move":
                    if (!CanMove(command))
                    {
                        continue;
                    }

                    npc.WalkTo(command.anchor);
                    yield return new WaitUntil(() => !npc.IsMoving);
                    break;

                case "look_at":
                    var target = string.IsNullOrEmpty(command.target) ? "player" : command.target;
                    if (target != "player" && !graph.Has(target))
                    {
                        Debug.LogWarning($"[CommandExecutor] look_at 目标不存在：{target}");
                        continue;
                    }

                    yield return npc.TurnTo(target);
                    break;

                case "set":
                    if (state == null)
                    {
                        Debug.LogWarning($"[CommandExecutor] 没有 StateSwitcher，set {command.target}={command.state} 被忽略");
                        continue;
                    }

                    state.Apply(command.target, command.state);
                    break;

                case "spawn":
                    // v1 不做灯灵；schema 保留，等真需要时再接
                    Debug.Log("[CommandExecutor] spawn 已延后（v1 不做灯灵）");
                    break;

                default:
                    Debug.LogWarning($"[CommandExecutor] 未知 op：{command.op}");
                    break;
            }

            yield return new WaitForSeconds(0.2f);
        }

        IsBusy = false;
    }

    bool CanMove(DirectorClient.CommandDto command)
    {
        if (npc == null || graph == null)
        {
            Debug.LogError("[CommandExecutor] 缺少 NpcAgent 或 AnchorGraph");
            return false;
        }

        if (command.anchor == "player")
        {
            Debug.LogWarning("[CommandExecutor] player 不能作为目的地");
            return false;
        }

        if (!graph.Has(command.anchor))
        {
            Debug.LogWarning($"[CommandExecutor] 锚点不存在：{command.anchor}");
            return false;
        }

        return true;
    }
}
