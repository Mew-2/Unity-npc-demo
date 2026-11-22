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
    [Tooltip("膝枕：set npc lap / set player stand 走它")]
    public LapPillowController lap;

    public bool IsBusy { get; private set; }

    public IEnumerator Execute(DirectorClient.CommandDto[] commands)
    {
        if (commands == null)
        {
            yield break;
        }

        IsBusy = true;
        GameLog.Event("cmd_batch", "n", commands.Length.ToString());

        var index = 0;
        foreach (var command in commands)
        {
            index++;
            if (command == null || string.IsNullOrEmpty(command.op))
            {
                GameLog.Event("cmd_skip", "i", index.ToString(), "reason", "empty");
                continue;
            }

            var started = Time.time;
            GameLog.Event(
                "cmd",
                "i", index.ToString(),
                "op", command.op,
                "anchor", command.anchor ?? "",
                "target", command.target ?? "",
                "state", command.state ?? "");

            switch (command.op)
            {
                case "move":
                    if (lap != null) { lap.Exit(); }   // 任何移动都先退出膝枕

                    if (!CanMove(command))
                    {
                        continue;
                    }

                    // anchor = "player" 是动态目的地：走到玩家身边，不依赖固定锚点
                    if (command.anchor == "player")
                    {
                        npc.WalkToPlayer();
                    }
                    else
                    {
                        npc.WalkTo(command.anchor);
                    }

                    yield return new WaitUntil(() => !npc.IsMoving);
                    break;

                case "look_at":
                    if (lap != null && lap.IsActive)
                    {
                        GameLog.Event("cmd_skip", "op", "look_at", "reason", "lap_active");
                        break;
                    }

                    var target = string.IsNullOrEmpty(command.target) ? "player" : command.target;
                    if (target != "player" && !graph.Has(target))
                    {
                        Debug.LogWarning($"[CommandExecutor] look_at 目标不存在：{target}");
                        continue;
                    }

                    yield return npc.TurnTo(target);
                    break;

                case "set":
                    // 膝枕相关：npc sit/lap、player lap/stand，交给 LapPillowController
                    if (command.target == "npc" || command.target == "player")
                    {
                        if (lap == null)
                        {
                            Debug.LogWarning($"[CommandExecutor] 没有 LapPillowController，set {command.target}={command.state} 被忽略");
                            continue;
                        }

                        if (command.state == "lap" || command.state == "sit")
                        {
                            lap.Prepare();
                        }
                        else if (command.state == "stand")
                        {
                            lap.Exit();
                        }
                        else if (command.state == "sing")
                        {
                            lap.PlayLyric("♪ 窗外的天气 就像是你多变的表情 ♪\n♪ ～♪");
                        }
                        else
                        {
                            Debug.LogWarning($"[CommandExecutor] 未知的 {command.target} 状态：{command.state}");
                        }

                        break;
                    }

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

            GameLog.Event("cmd_done", "i", index.ToString(), "op", command.op, "sec", GameLog.F(Time.time - started));
            yield return new WaitForSeconds(0.2f);
        }

        IsBusy = false;
        GameLog.Event("cmd_batch_done");
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
            return true;   // 动态目的地，运行时按玩家位置算
        }

        if (!graph.Has(command.anchor))
        {
            Debug.LogWarning($"[CommandExecutor] 锚点不存在：{command.anchor}");
            return false;
        }

        return true;
    }
}
