"""离线回放：录屏兜底，不依赖网络和模型。"""

from __future__ import annotations

import json
from pathlib import Path

DATA_FILE = Path(__file__).with_name("demo_lines.json")
FALLBACK = {"say": "……鲸鲸没听清，再说一遍好吗？", "commands": []}


def load_lines() -> list[dict]:
    if not DATA_FILE.exists():
        return []
    data = json.loads(DATA_FILE.read_text(encoding="utf-8"))
    # 文件结构是 {"lines": [...]}，兼容直接是数组的情况
    if isinstance(data, dict):
        return data.get("lines", [])
    return data


def offline_response(player_text: str) -> dict:
    for item in load_lines():
        if item.get("player_text") == player_text:
            return item.get("response", FALLBACK)
    return FALLBACK
