"""指令校验：服务端第一道闸。

规则来自 SPEC §2：
- op 四选一：spawn / move / look_at / set
- 锚点必须存在；player 只是伪锚点，不能作为 move 目的地
- 实体必须存在（npc 或已生成的 wisp_N）
- set 的 target/state 必须在枚举里
- 一次最多 5 条；场上灯灵最多 5 只
"""

from __future__ import annotations

from schema import WorldSnapshot

VALID_OPS = {"spawn", "move", "look_at", "set"}
MAX_COMMANDS = 5
MAX_WISPS = 5

SET_TARGETS = {
    "light": {"on", "off"},
    "curtain": {"open", "closed"},
    "outfit": {"nightgown", "daydress"},
}


def validate_commands(commands: object, world: WorldSnapshot) -> list[str]:
    """返回错误列表；空列表表示全部合法。"""
    if not isinstance(commands, list):
        return ["commands 必须是数组"]

    if len(commands) > MAX_COMMANDS:
        return [f"commands 最多 {MAX_COMMANDS} 条，收到 {len(commands)} 条"]

    anchors = {a.id for a in world.anchors}
    entities = {e.id: e for e in world.entities}
    wisp_count = sum(1 for e in world.entities if e.kind == "wisp")

    errors: list[str] = []
    spawned = 0

    for index, command in enumerate(commands):
        prefix = f"第 {index + 1} 条"

        if not isinstance(command, dict):
            errors.append(f"{prefix}不是对象")
            continue

        op = command.get("op")
        if op not in VALID_OPS:
            errors.append(f"{prefix}的 op 非法：{op!r}")
            continue

        if op == "spawn":
            anchor = command.get("anchor")
            if anchor not in anchors:
                errors.append(f"{prefix} spawn 锚点不存在：{anchor!r}")
            elif anchor == "player":
                errors.append(f"{prefix} spawn 不能用 player 伪锚点")

            spawned += 1
            if wisp_count + spawned > MAX_WISPS:
                errors.append(f"{prefix} 灯灵数量超过上限 {MAX_WISPS}")

            name = command.get("name")
            if name is not None and (not isinstance(name, str) or len(name) > 8):
                errors.append(f"{prefix} 的 name 必须是不超过 8 字的字符串")

        elif op == "move":
            entity = command.get("entity", "npc")
            anchor = command.get("anchor")

            _check_entity(prefix, entity, entities, errors)
            if anchor not in anchors:
                errors.append(f"{prefix} move 锚点不存在：{anchor!r}")
            elif anchor == "player":
                errors.append(f"{prefix} move 不能用 player 作目的地")

        elif op == "look_at":
            entity = command.get("entity", "npc")
            target = command.get("target")

            _check_entity(prefix, entity, entities, errors)
            if target != "player" and target not in anchors:
                errors.append(f"{prefix} look_at 目标不存在：{target!r}")

        elif op == "set":
            target = command.get("target")
            state = command.get("state")

            if target not in SET_TARGETS:
                errors.append(f"{prefix} set target 非法：{target!r}")
            elif state not in SET_TARGETS[target]:
                errors.append(f"{prefix} set {target} 的 state 非法：{state!r}")

    return errors


def _check_entity(prefix: str, entity: object, entities: dict, errors: list[str]) -> None:
    if entity not in entities:
        errors.append(f"{prefix} 实体不存在：{entity!r}")
