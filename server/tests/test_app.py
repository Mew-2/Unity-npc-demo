import os

os.environ["OFFLINE"] = "1"

from fastapi.testclient import TestClient

from app import app

client = TestClient(app)

WORLD = {
    "anchors": [
        {"id": "door", "desc": "门口"},
        {"id": "bed", "desc": "床边"},
        {"id": "window", "desc": "窗边"},
        {"id": "screen", "desc": "屏风"},
        {"id": "desk", "desc": "梳妆台"},
        {"id": "lamp", "desc": "台灯"},
        {"id": "player", "desc": "你"},
    ],
    "entities": [{"id": "npc", "kind": "npc", "at": "bed"}],
}


def test_health():
    response = client.get("/health")
    assert response.status_code == 200
    assert response.json()["offline"] is True


def test_offline_valid_turn():
    response = client.post(
        "/direct",
        json={"player_text": "关灯，然后到床边看着我", "world": WORLD},
    )
    assert response.status_code == 200
    body = response.json()
    assert body["say"]
    assert body["commands"]


def test_invalid_commands_are_dropped():
    world = {
        "anchors": [{"id": "door", "desc": "门口"}],
        "entities": [{"id": "npc", "kind": "npc", "at": "door"}],
    }
    response = client.post(
        "/direct",
        json={"player_text": "关灯，然后到床边看着我", "world": world},
    )
    body = response.json()
    assert body["commands"] == []
    assert "做不到" in body["say"]
