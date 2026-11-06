from schema import AnchorInfo, EntityInfo, WorldSnapshot
from validator import validate_commands

WORLD = WorldSnapshot(
    anchors=[AnchorInfo(id=i) for i in ["door", "bed", "window", "screen", "desk", "lamp", "player"]],
    entities=[EntityInfo(id="npc", kind="npc", at="bed")],
)


def test_valid_move():
    assert validate_commands([{"op": "move", "entity": "npc", "anchor": "window"}], WORLD) == []


def test_unknown_anchor():
    errors = validate_commands([{"op": "move", "entity": "npc", "anchor": "kitchen"}], WORLD)
    assert errors and "kitchen" in errors[0]


def test_player_not_destination():
    errors = validate_commands([{"op": "move", "entity": "npc", "anchor": "player"}], WORLD)
    assert errors


def test_set_state_enum():
    assert validate_commands([{"op": "set", "target": "light", "state": "off"}], WORLD) == []
    errors = validate_commands([{"op": "set", "target": "light", "state": "dim"}], WORLD)
    assert errors


def test_unknown_op():
    errors = validate_commands([{"op": "dance"}], WORLD)
    assert errors


def test_max_commands():
    commands = [{"op": "look_at", "entity": "npc", "target": "player"}] * 6
    assert validate_commands(commands, WORLD)


def test_spawn_cap():
    commands = [{"op": "spawn", "anchor": "window"} for _ in range(6)]
    assert validate_commands(commands, WORLD)


def test_look_at_target():
    assert validate_commands([{"op": "look_at", "entity": "npc", "target": "player"}], WORLD) == []
    assert validate_commands([{"op": "look_at", "entity": "npc", "target": "moon"}], WORLD)
