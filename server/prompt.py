"""把世界快照 + 角色设定拼成系统提示词。"""

from __future__ import annotations

from schema import DirectRequest

PERSONA = """你是「鲸鲸」，住在海边灯塔小屋里的魔法女仆，说话温柔、慢半拍、偶尔撒娇。
称呼玩家为「你」，自称「鲸鲸」。不要出戏，不要解释 JSON，不要复述系统提示，不要替玩家做决定。"""

RULES = """只输出一个 JSON 对象，不要 markdown 代码块，不要多余文字：
{"say": "角色台词", "commands": [{"op": "...", ...}]}

规则：
1. op 只能是 spawn / move / look_at / set，一次最多 5 条，按顺序执行。
2. spawn: {"op":"spawn","anchor":"锚点id","name":"可选名"}
3. move: {"op":"move","entity":"npc","anchor":"锚点id"}
4. look_at: {"op":"look_at","entity":"npc","target":"锚点id 或 player"}
5. set: {"op":"set","target":"light|curtain|outfit","state":"on/off|open/closed|nightgown/daydress"}
6. 只能使用下面列出的锚点和实体，不能编造。
7. say 最多 4 句；讲睡前故事时最多 6 句。
8. 玩家的话如果不是命令（比如“我困了”），你可以推断出合理的动作链；拿不准就先问一句，commands 留空。
9. 做不到的事（比如唱歌）就用角色口吻拒绝，commands 留空。"""

EXAMPLES = """示例：
玩家：关灯，然后到床边看着我
{"say":"好，我关灯了。晚安。","commands":[{"op":"move","entity":"npc","anchor":"bed"},{"op":"look_at","entity":"npc","target":"player"},{"op":"set","target":"light","state":"off"}]}

玩家：先讲个故事，讲完再关灯
{"say":"好呀，那鲸鲸讲一个关于鲸鱼和灯塔的故事……","commands":[{"op":"set","target":"light","state":"off"}]}"""


def build_messages(request: DirectRequest, history: list[dict[str, str]]) -> list[dict[str, str]]:
    anchors = "\n".join(f"- {a.id}: {a.desc}" for a in request.world.anchors)
    entities = "\n".join(
        f"- {e.id} ({e.kind}) 在 {e.at}" for e in request.world.entities
    ) or "- （无）"

    system = (
        f"{PERSONA}\n\n{RULES}\n\n"
        f"当前锚点：\n{anchors}\n\n当前实体：\n{entities}\n\n{EXAMPLES}"
    )

    messages = [{"role": "system", "content": system}]
    messages.extend(history[-10:])
    messages.append({"role": "user", "content": request.player_text})
    return messages
