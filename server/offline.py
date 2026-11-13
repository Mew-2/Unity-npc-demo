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
    lines = load_lines()

    # 1) 精确匹配
    for item in lines:
        if item.get("player_text") == player_text:
            return item.get("response", FALLBACK)

    # 2) 包含匹配：取最长的候选，避免“去窗边”落到兜底
    text = player_text.strip()
    candidates = [
        item
        for item in lines
        if item.get("player_text")
        and (item["player_text"] in text or text in item["player_text"])
    ]
    if candidates:
        best = max(candidates, key=lambda item: len(item["player_text"]))
        return best.get("response", FALLBACK)

    return FALLBACK
