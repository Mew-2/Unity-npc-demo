# 交接：周末在家继续（2026-11-07–08）

> 换机器只认 GitHub，不认本地文件。先 `git pull`，再按本文做。

## 0. 当前状态（2026-11-06 收工）

- 服务端：完成，`pytest` 11 passed，离线 HTTP 验证通过
- Unity：工程 + 灰盒 + 7 锚点 + `NpcAgent` 完成；客户端三件套（DirectorClient / DialogueUI / CommandExecutor）**未写**
- VRM：**未开始**（只有胶囊占位）
- 目标：12-12

## 1. 克隆仓库

```powershell
cd C:\YZ        # 或你机器上的英文无空格目录
git clone https://github.com/Mew-2/Unity-npc-demo.git
cd Unity-npc-demo
git log --oneline -3
```

GitHub 打不开时，先启动 Watt Toolkit。

## 2. 装 Unity（周末的主任务）

1. 装 **Unity Hub 中国版**，登录 Unity ID，激活 **Personal**。
2. 装 **Unity 2022.3.62f3c1**，模块只勾：
   - ✅ Windows Build Support (IL2CPP)
   - ✅ 简体中文（语言包）
   - ❌ WebGL / 文档 / VS 2022
3. 打开 `DawnAndDusk` 工程（Hub → 项目 → 添加）。
4. 首次导入 2–3 分钟，检查：
   - 场景 `Assets/Scenes/NightRoom.unity` 有房间、7 个锚点、Npc 胶囊
   - Console 无红字
   - 菜单 **Tools → DawnAndDusk → Build Greybox** 存在

## 3. 跑服务端

### A. Windows（推荐）

```powershell
cd server
py -3.12 -m venv .venv
.venv\Scripts\python -m pip install -r requirements.txt -i https://pypi.tuna.tsinghua.edu.cn/simple
.venv\Scripts\python -m pytest -q        # 预期 11 passed
$env:OFFLINE="1"
.venv\Scripts\python app.py
```

浏览器开 `http://127.0.0.1:8000/health`，看到 `ok: true` 即可。

### B. WSL（备选）

```bash
cd /mnt/c/YZ/Unity-npc-demo/server
python3 -m venv .venv
.venv/bin/pip install -r requirements.txt -i https://pypi.tuna.tsinghua.edu.cn/simple
OFFLINE=1 .venv/bin/python app.py
```

## 4. VRM / VRoid（可选，能做多少做多少）

1. 装 **VRoid Studio**（免费）。
2. 按 `docs/CHARACTER.md` 捏鲸鲸：蓝色长发、头鳍、大尾巴、深蓝白女仆装。
3. 导出 `.vrm`，先放着；**不要在 Unity 里导入**，等客户端代码就绪后我带你接。
4. 许可：自捏模型，非商用公开 demo 可用；不要使用二创原图。

## 5. 不要做

- 不要升级 Unity 版本（两端必须 `2022.3.62f3c1`）。
- 不要提交 `Library/`、`.venv/`、`logs/`（`.gitignore` 已处理）。
- 不要把女仆二创图、公司资料放进仓库。
- 不要在本机手改 `.git` 或强推。

## 6. 下周一回来

- 我写 `DirectorClient` / `DialogueUI` / `CommandExecutor`
- 你只需：打开工程、启动服务、看 Console
- 联调目标：Unity 里输入「去窗边」→ 鲸鲸走到窗边并回话
