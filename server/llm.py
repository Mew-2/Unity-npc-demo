"""调用 OpenAI 兼容接口（DeepSeek / 通义 / OpenAI 都可）。"""

from __future__ import annotations

import json
import os
import re
from typing import Any

import httpx

DEFAULT_BASE_URL = "https://api.deepseek.com/v1"
DEFAULT_MODEL = "deepseek-chat"


class LLMError(RuntimeError):
    pass


def llm_config() -> tuple[str, str, str, str]:
    return (
        os.environ.get("LLM_BASE_URL", DEFAULT_BASE_URL).rstrip("/"),
        os.environ.get("LLM_API_KEY", ""),
        os.environ.get("LLM_MODEL", DEFAULT_MODEL),
        os.environ.get("LLM_TIMEOUT", "20"),
    )


def is_configured() -> bool:
    return bool(os.environ.get("LLM_API_KEY"))


def _extract_json(text: str) -> dict[str, Any]:
    """模型偶尔会包 ```json，这里剥掉；解析失败就抛错。"""
    cleaned = text.strip()

    fence = re.match(r"^```(?:json)?\s*(.*?)\s*```$", cleaned, re.DOTALL)
    if fence:
        cleaned = fence.group(1).strip()

    try:
        data = json.loads(cleaned)
    except json.JSONDecodeError as exc:
        raise LLMError(f"JSON 解析失败：{exc}") from exc

    if not isinstance(data, dict):
        raise LLMError("模型返回的不是 JSON 对象")

    return data


async def call_llm(messages: list[dict[str, str]]) -> dict[str, Any]:
    """调一次模型；带一次“解析失败后纠正”的重试。"""
    base_url, api_key, model, timeout = llm_config()
    if not api_key:
        raise LLMError("没有配置 LLM_API_KEY")

    payload = {
        "model": model,
        "messages": messages,
        "temperature": 0.7,
        "response_format": {"type": "json_object"},
    }
    headers = {"Authorization": f"Bearer {api_key}"}

    last_error: Exception | None = None

    async with httpx.AsyncClient(timeout=float(timeout)) as client:
        for attempt in range(2):
            try:
                response = await client.post(
                    f"{base_url}/chat/completions",
                    headers=headers,
                    json=payload,
                )
                response.raise_for_status()
                content = response.json()["choices"][0]["message"]["content"]
                return _extract_json(content)
            except LLMError as exc:
                last_error = exc
                # 把错误回喂给模型，再试一次
                payload["messages"] = messages + [
                    {"role": "assistant", "content": "（上次输出无法解析）"},
                    {"role": "user", "content": f"你上次的输出无法解析：{exc}。请只返回合法 JSON。"},
                ]
            except httpx.HTTPError as exc:
                raise LLMError(f"请求模型失败：{exc}") from exc

    raise LLMError(f"模型连续两次输出无法解析：{last_error}")
