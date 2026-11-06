# DawnAndDusk 编排服务

Unity 只负责“执行指令”，所有 LLM 调用、校验、重试、日志都在这里。

## 环境

- Python **3.12.10**（`server/.venv`，已建好；由 Python install manager 安装）
- 依赖：fastapi / uvicorn / httpx / pytest

重建环境（一般不需要）：

```powershell
cd server
py -3.12 -m venv .venv
.venv\Scripts\python -m pip install -r requirements.txt -i https://pypi.tuna.tsinghua.edu.cn/simple
```

## 运行

**VS Code（推荐）**：打开仓库根目录，Run and Debug 选 `Server: offline (8000)`，按 F5。

**命令行**：

```powershell
cd server
$env:OFFLINE = "1"
.venv\Scripts\python app.py
```

接真实模型时去掉 `OFFLINE`，并设置：

```powershell
$env:LLM_API_KEY = "sk-..."
$env:LLM_BASE_URL = "https://api.deepseek.com/v1"   # 可换 OpenAI / 通义兼容地址
$env:LLM_MODEL = "deepseek-chat"
```

## 接口

- `GET /health` → `{ok, offline, model}`
- `POST /direct` → 请求 `{player_text, world}`，响应 `{say, commands}`

`world` 是 Unity 传来的世界快照（锚点 + 实体）。服务端会做两层校验：
JSON 解析失败重问一次；指令不合法则整批丢弃，返回“做不到”的兜底话术。

## 测试

```powershell
cd server
.venv\Scripts\python -m pytest -q
```

当前：**11 passed**。

## 日志

每轮写 `logs/turns.jsonl`（输入 / 输出 / 是否合法 / 耗时），`logs/` 不入库。
