"""DawnAndDusk 编排服务。

POST /direct  {player_text, world} -> {say, commands}
GET  /health  -> {ok, offline, model}

- 有 LLM_API_KEY 走模型；没有（或 OFFLINE=1）走 demo_lines.json 回放
- 服务端做结构 + 语义校验；不合法整批丢弃
- 每轮写 logs/turns.jsonl，方便复盘
"""

from __future__ import annotations

import json
import os
import time
from collections import deque
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, cast

from fastapi import FastAPI

import llm
import offline
from prompt import build_messages
from schema import DirectRequest, DirectResponse
from validator import validate_commands

app = FastAPI(title="DawnAndDusk Director", version="0.1.0")

LOG_DIR = Path(__file__).with_name("logs")
LOG_FILE = LOG_DIR / "turns.jsonl"

# 最近 10 轮上下文（user / assistant 交替），进程重启清空
_history: deque[dict[str, str]] = deque(maxlen=10)

FALLBACK_SAY = "这个鲸鲸做不到……换个说法？"


def _is_offline() -> bool:
    return os.environ.get("OFFLINE") == "1" or not llm.is_configured()


def _log_turn(payload: dict) -> None:
    LOG_DIR.mkdir(exist_ok=True)
    payload["ts"] = datetime.now(timezone.utc).isoformat()
    with LOG_FILE.open("a", encoding="utf-8") as handle:
        handle.write(json.dumps(payload, ensure_ascii=False) + "\n")


@app.get("/health")
def health() -> dict:
    _, _, model, _ = llm.llm_config()
    return {"ok": True, "offline": _is_offline(), "model": model}


@app.post("/direct", response_model=DirectResponse)
async def direct(request: DirectRequest) -> DirectResponse:
    started = time.perf_counter()
    offline_mode = _is_offline()

    if offline_mode:
        data = offline.offline_response(request.player_text)
    else:
        messages = build_messages(request, list(_history))
        try:
            data = await llm.call_llm(messages)
        except llm.LLMError as exc:
            _log_turn(
                {
                    "player_text": request.player_text,
                    "offline": False,
                    "llm_error": str(exc),
                    "say": "",
                    "commands": [],
                    "valid": False,
                }
            )
            return DirectResponse(say=offline.FALLBACK["say"], commands=[])

    raw_say = data.get("say")
    say: str = raw_say if isinstance(raw_say, str) else ""
    raw_commands = data.get("commands", [])
    commands = cast(
        list[dict[str, Any]],
        raw_commands if isinstance(raw_commands, list) else [],
    )

    errors = validate_commands(commands, request.world)
    elapsed_ms = round((time.perf_counter() - started) * 1000)

    _log_turn(
        {
            "player_text": request.player_text,
            "offline": offline_mode,
            "say": say,
            "commands": commands,
            "valid": not errors,
            "errors": errors,
            "elapsed_ms": elapsed_ms,
        }
    )

    if errors:
        # 不合法：整批丢弃，不让半截指令进场景
        return DirectResponse(say=FALLBACK_SAY, commands=[])

    # 只有合法回合才进上下文，避免坏样本污染后续
    _history.append({"role": "user", "content": request.player_text})
    _history.append({"role": "assistant", "content": say})

    return DirectResponse(say=say, commands=commands)


if __name__ == "__main__":
    import uvicorn

    # 方便 VS Code 里直接 Run，不用敲命令
    uvicorn.run("app:app", host="127.0.0.1", port=8000, reload=False)
