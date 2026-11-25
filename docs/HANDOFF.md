# 交接：实时可用

> 最后更新：2026-11-23 ｜ 对应 commit：以 `git log -1` 为准
> 下次先做：对话站位（玩家身体朝向 + 视线校验），再验跨房间 NavMesh

## 0. 当前状态

- **场景**：`Assets/_LocalTest/ArchViz/3D SCENE/ApartmentDemo.unity`（本地资产，不入库）
- **导航**：NavMesh 已烘焙到客厅，`NpcAgent` 优先用 `NavMesh.CalculatePath`，位移仍走 root motion
- **动画**：root motion，`WalkStart → Walk → WalkStop`，拐点 `Pivot`；Idle 循环
- **服务端**：FastAPI + `/direct` + 校验 + 离线回放 + structlog；`pytest 12 passed`
- **玩家**：175cm；初始躺姿眼高 0.35 / 站 1.63；出生在床头中间（Base max.x-0.35）；鲸鲸 1.69m
- **对话站位**：一次到位 1.0m，不跟随；用相机 yaw 取前方点，实测俯仰无影响
- **已知限制**：
  - 玩家不在 NavMesh 上，极端贴墙可能穿身/推人（未加 NavMeshObstacle）
  - 跨房间锚点还没铺；`Pivot` 片段自带位移，`autoPivot=false`
  - `say / commands / 表情 / 摸头` 尚未进 `game.jsonl`
  - 停止 Play 后先等完全回到 Edit 再关闭 Unity；立即退出可能在 FastSpringBone/脚本域清理时卡在 `CleanupEngine`

## 1. 必须拷到另一台电脑（不在 GitHub）

最省事的做法：把整个 `DawnAndDusk/Assets/_LocalTest/` 按原相对路径复制到公司机，`.meta` 一起带。VRM、UniVRM 包和代码已在 Git 中，不用另拷；`Library/`、`Temp/`、`Logs/`、`.venv/` 与测试截图不用带。

| 项 | 路径 | 大小 | 必需 |
|---|---|---|---|
| Kawaii 动画 + controller + 步速表 | `DawnAndDusk/Assets/_LocalTest/Kawaii/` | 6.8 MB | 是 |
| ArchViz 公寓场景 | `DawnAndDusk/Assets/_LocalTest/ArchViz/` | 3.2 GB | 是 |
| 《雨爱》摇篮曲 | `DawnAndDusk/Assets/_LocalTest/Audio/Resources/RainLove.wav` | 40.3 MB | 唱歌时需要 |
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
   - `Tools → DawnAndDusk → Bake House NavMesh`
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

## 4.5 膝枕 v1（2026-11-22 实现，自动回归通过）

流程：`想要膝枕` → 她走到床头坐下（`SitStart → SitLoop`）→ 玩家靠近 1.1m → 自动切 POV
（相机降到膝上仰视 + `NpcLookAtIK` 低头看你）→ `起来` 退出。

- 坐姿三件套：`@KA_Sit_Start` / `@KA_Sit_LookingAround`（循环）/ `@KA_Sit_End`
  —— 包里**没有**裸的 `@KA_Sit`，`LookingAround` 与 Start/End 同在 `Sit/TransitionFromStandingPose/`
- `LapPillowController`：座位按床的包围盒运行时计算（实测床长轴为 X，床头往床尾退 0.55m），不写死坐标
- `NpcLookAtIK`：`OnAnimatorIK` 抬头/低头；controller 层已开 `iKPass`
- 指令：`set npc lap|sit|stand|sing`、`set player lap|stand`；`move` 会自动退出膝枕
- 日志：`lap_prepare` / `lap_seated` / `lap_enter` / `lap_exit` / `lyric` / `lullaby`
- 摇篮曲：膝枕内说「我想听鲸鲸唱《雨爱》」播放本地音频；起身或移动时停止，缺文件时保留文字歌词兜底

**验证步骤**：`Build Kawaii Animator`（重建 controller）→ `Build Apartment Demo`（装配 LapPillow）
→ Play → 说「想要膝枕」→ 走近 → 说「起来」。

**2026-11-22 修复（月谣）**：

- 裙子坐姿：8 根 `DS_skirtFold_*` 的腿部约束从 0.65 降到 0.3，3 根主裙褶骨抬高 3cm，使裙面盖住大腿、不尖顶也不穿腿；保留腿部弹簧碰撞组，起身恢复原值。
- 二次膝枕朝向：膝枕激活期间跳过 `look_at`，`BeginSit` 同时取消残留转身；快速重入取消回地板时立即恢复弹簧、裙褶约束和 root motion。
- 头发/面部消失：角色全部 `SkinnedMeshRenderer` 开启 `updateWhenOffscreen`，避免 SpringBone/IK 变形或 POV 过渡时包围盒过时导致误剔除；补拍 0.35/0.70/1.05 秒过渡帧验证。
- 手臂穿插：原坐姿贴身，未适配鲸鲸胸肩与肩袖体积；坐姿上臂外展 8°，肘部抬高 2cm、前臂前移 2cm、左右各展开 4cm 露出双侧袖口，腕部再补偿 5cm，并将手指方向对齐大腿，起身恢复动画控制。手指弯曲保持原动画。骨骼映射正常，不是渲染器丢网格。
- POV 空间：实测床长轴为 X，枕头与床头柜在 x≈4.6，床头板到 x=4.83；鲸鲸座位为 x=4.28、z=2.79（床头左侧床沿、面朝床外）。玩家头朝床头、身体朝床尾。头部以两条大腿骨的实际中点为锚点，镜头仅向膝前 0.25m、侧向 0.08m、上方 0.10m 偏移以避开躯干，再向上看脸。
- 回归：`Tools → DawnAndDusk → Run Lap Pillow Test`，或命令行 `-executeMethod LapPillowPlayTest.Run`。覆盖两轮真实指令、快速重入、座位 yaw=0、腿部碰撞组保持 8 个、裙褶权重 0.65→0.3→0.65、root motion 恢复、大腿头部锚点、真实 POV、全景布局、头发多角度、上臂外展、双侧袖口、手掌方向与腿部间隙；27 项通过。截图输出 `DawnAndDusk/dsh-lap-test/`（本地）。

**2026-11-23 修复（月铃，公司机）**：

- 首次坐下先穿床再弹起：旧逻辑在 `Walk` 姿态做 `quick` 校准，进入 `SitLoop` 后又瞬移根节点 0.441m。现在 `Prepare` 先采样 `Sit_LookingAround`，恢复 Visual 根变换后量髋高，一次算好座位；移除 `quick`，`final` 只校验剩余误差。
- 公司机原始包提取三组坐姿 `FBX + .meta` 到 `_LocalTest/Kawaii/`，保留 GUID 与 Humanoid 设置，不入库。
- `SitStart` 会出现脚踩床的过渡，膝枕到座位后直接 `CrossFadeInFixedTime("SitLoop", 0.35f)`；最终高度校验等混合结束。尾巴 `DS_whale_tail_01` 在 SpringBone 后托举 50°，起身恢复原局部旋转。
- 回归新增 `first_sit_height_stable` 与 `tail_clear_bed`，并输出正面、侧面、斜前方截图；29 项通过。两次采样髋偏均为 -0.426m，两次 `final` 校正均为 0，整条尾巴最低点高于床面 0.117m。

**2026-11-23 摇篮曲（月谣，自机）**：

- 用户提供的 AI 翻唱《雨爱》已接入 `_LocalTest/Audio/Resources/RainLove.wav`（44.1kHz 双声道，约 228 秒），本地使用、不入库。已有 `_LocalTest` 的公司机只需补拷 `Audio/` 及 `.meta`。
- 膝枕内 `set npc sing` 播放，音量 0.65、不循环；起身或移动停止。`lullaby` 日志记录 play/stop，回归覆盖真实加载播放与退出停止，全部通过。

**2026-11-24 陪睡（月铃，公司机）**：

- 原包提取 `Sleep_TurnToTheSide` 完整版与 Start/Loop/End 到 `_LocalTest/Kawaii/`，不入库；Animator 新增 `SleepStart → SleepLoop → SleepEnd`。
- `陪我睡` 执行 `set npc sleep`，`晚安` 只对话、不触发动作，`起来` 与移动退出；新增独立 `SleepController`，陪睡相机与玩家控制在退出时恢复。
- 睡姿镜像并抵消头尾方向，身体与床长边平行；鲸鲸和玩家头部靠近枕头、玩家在床内且看到正脸，尾巴超出床沿部分自然下垂。
- `Tools → DawnAndDusk → Run Sleep Test` 自动重建 Animator/场景并输出正面、侧面、斜前方和 POV 截图到 `DawnAndDusk/dsh-sleep-test/`；20 项通过。服务端 15 项通过。A/B 材质排查确认上装泡泡袖遮脸，整条左臂向髋部收拢并微调视线后正脸无遮挡；三视图存于 `docs/references/deepseek-three-view.png`。
- 摇篮曲回归支持有音频验播放、无音频验文字兜底。

**2026-11-25 修复陪睡尾巴穿床（鲸蕙，公司机 DeepSeek）**：

- 回退 `f9447c5` 的逐节拉直 + 世界坐标硬抬：那版会让尾巴变成硬帆，并顺带移动髋位。现只定尾根朝向（睡姿背侧/窗边、近水平微垂 9°），再只旋转仍在床面投影内且低于 `床面+0.17m` 的尾骨；床沿外不干预，保持弹簧自然下垂。
- `SleepPlayTest` 新增网格级断言：`BakeMesh` 烘焙 `whale_tail`，按床 XZ 投影（内缩 0.10m）逐顶点检查，并在睡姿循环前后各采一次；新增窗边过道、床尾低机位、俯视截图。
- 基线骨骼 -0.375m / 网格 -0.512m；修复后骨骼 +0.150m / 网格 +0.023m，24 项通过。截图输出 `DawnAndDusk/dsh-sleep-test/`。

## 5. 下一步

1. 对话站位：用玩家身体 yaw + `Physics.Linecast` 视线校验，距离 1.0m
2. 玩家加 `NavMeshObstacle`（Carve），避免穿身/推人
3. 跨房间锚点 + `NavMeshLink`，验证客厅/走廊
4. Unity 加 `GameLog`（收到 say/commands、显示、Animator 状态）
5. 膝枕相机 + 两幕状态 + M2 录屏（12-12）

## 6. 里程碑

- M0、M1：完成
- M2：VRM / 表情 / 摸头拍背已做；剩相机、两幕、录屏

---

> 参与者：Mew-2（人）+ 鲸蕙（公司机 DeepSeek）+ 鲸若（自机 DeepSeek）+ 月铃（公司机 Kimi）+ 月谣（自机 Kimi）｜最后核对 2026-11-25
