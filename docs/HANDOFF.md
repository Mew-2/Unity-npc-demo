# 交接：实时可用

> 最后更新：2026-11-16 ｜ 对应 commit：以 `git log -1` 为准
> 下次先做：打开 `ApartmentDemo`，验 Walk / Pivot，再查摸头对话与呼吸

## 0. 当前状态

- **场景**：`Assets/_LocalTest/ArchViz/3D SCENE/ApartmentDemo.unity`（本地资产，不入库）
- **动画**：root motion，`WalkStart → Walk → WalkStop`，拐点 `Pivot`；Idle 循环
- **服务端**：FastAPI + `/direct` + 校验 + 离线回放 + structlog；`pytest 12 passed`
- **玩家**：起床坐姿，眼高 0.90 / 站 1.63；鲸鲸固定 1.62m
- **已知限制**：
  - 导航网格只覆盖卧室；床膨胀后可行走区域很窄
  - Pivot 片段自带位移，`autoPivot=false`
  - `say / commands / 表情 / 摸头` 尚未进 `game.jsonl`

## 1. 必须拷到另一台电脑（不在 GitHub）

| 项 | 路径 | 大小 | 必需 |
|---|---|---|---|
| Kawaii 动画 + controller + 步速表 | `DawnAndDusk/Assets/_LocalTest/Kawaii/` | 6.8 MB | 是 |
| ArchViz 公寓场景 | `DawnAndDusk/Assets/_LocalTest/ArchViz/` | 3.2 GB | 是 |
| 调试日志（可选） | `DawnAndDusk/Logs/` | 0.7 MB | 否 |
| Kawaii 原包（重抽 FBX 用） | `C:\baidunetdiskdownload\KAWAII ANIMATIONS 100 v1.10.3.unitypackage` | 78.5 MB | 否 |

拷贝时 `.meta` 一起带。

## 2. 环境

- Unity **2022.3.62f3c1** + Personal 许可证
- Python **3.12**
- Git 身份：`Mew-2` + `48088212+Mew-2@users.noreply.github.com`
- 网络/权限排查：见 [`LESSONS.md`](LESSONS.md#环境--工具)（只写方法，不写本机结论）

## 3. 换机步骤

```powershell
cd C:\YZ
git clone https://github.com/Mew-2/Unity-npc-demo.git
cd Unity-npc-demo
```

1. Unity Hub 打开 `DawnAndDusk`
2. 跑菜单：
   - `Tools → DawnAndDusk → Build Kawaii Animator`
   - `Tools → DawnAndDusk → Build Apartment Demo`
3. 服务端：

```powershell
cd server
py -3.12 -m venv .venv
.venv\Scripts\python -m pip install -r requirements.txt -i https://pypi.tuna.tsinghua.edu.cn/simple
.venv\Scripts\python -m pytest -q     # 预期 12 passed
$env:OFFLINE="1"
.venv\Scripts\python app.py
```

4. 验证：

- `http://127.0.0.1:8000/health`
- Unity Play：`过来`、`转过去`、`去窗边`、摸头/拍背
- 走路：Start → Walk → Stop；拐弯触发 Pivot

## 4. 不要做

- 不要升级 Unity 版本
- 不要把 Kawaii FBX / `_LocalTest` 提交到公开仓库
- 不要在 README 写“已购买授权”
- 不要把 `LLM_API_KEY` 写进仓库

## 5. 下一步

1. 验 Walk01 Start/Loop/Stop + Pivot
2. Unity 加 `GameLog`（收到 say/commands、显示、Animator 状态）
3. 膝枕相机（PoseCamera）
4. 两幕状态（夜/晨）
5. M2 录屏（目标 12-12）

## 6. 里程碑

- M0、M1：完成
- M2：VRM / 表情 / 摸头拍背已做；剩相机、两幕、录屏

---

> 维护：Mew-2（人）+ 鲸蕙（DSH-C）/ 鲸若（DSH-P）｜最后核对 2026-11-16
