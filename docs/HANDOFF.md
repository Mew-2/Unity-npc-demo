# 交接：换机器 / 出租屋继续（更新 2026-11-13）

> 代码在 GitHub：https://github.com/Mew-2/Unity-npc-demo
> 最新提交：`756e01c`（Kawaii 动画 + structlog + 聊天滚动）

## 0. 当前状态

- **服务端**：FastAPI + `/direct` + 校验 + 离线回放 + `structlog`（JSONL + `trace_id`）；`pytest 11 passed`
- **Unity**：鲸鲸 VRM、表情（Shift+1~5）、摸头/拍背、`front` 近景锚点、Kawaii 动画（Idle / Walk Start-Loop-Stop / Pivot / Sit / Bow）
- **公开仓库不含授权动画**：clone 后 `Resources.Load` 找不到 controller，自动回退 T-pose
- **未做**：膝枕相机、两幕状态、真实 LLM key

## 1. 必须带（不在 GitHub）

| 项 | 路径 | 大小 |
|---|---|---|
| Kawaii 原包 | `C:\baidunetdiskdownload\KAWAII ANIMATIONS 100 v1.10.3.unitypackage` | 78.5 MB |
| **可选**：已抽好的动画 | `DawnAndDusk/Assets/_LocalTest/` | 约 8 MB |
| **可选**：调试日志 | `server/logs/turns.jsonl` | 很小 |

只带 unitypackage 也能恢复：按下面第 3 步重抽 7 个 FBX。

## 2. 环境（不是文件）

- Unity **2022.3.62f3c1** + Personal 许可证（家里要登录）
- Python **3.12**（pymanager 或 WSL）
- GitHub 加速（Watt Toolkit 等）
- Git 身份：`user.name` + noreply 邮箱 `48088212+Mew-2@users.noreply.github.com`

## 3. 换机步骤

```powershell
cd C:\YZ
git clone https://github.com/Mew-2/Unity-npc-demo.git
cd Unity-npc-demo
```

1. Unity Hub 打开 `DawnAndDusk`（2022.3.62f3c1）
2. **动画恢复**（二选一）：
   - 带了 `_LocalTest`：直接跑菜单 **Tools → DawnAndDusk → Build Kawaii Animator**
   - 只带 unitypackage：把包导入任意 Unity 工程，抽出以下 **7 个 FBX + 同名 `.meta`**，放到 `DawnAndDusk/Assets/_LocalTest/Kawaii/`，再跑上面的菜单
3. 服务端：

```powershell
cd server
py -3.12 -m venv .venv
.venv\Scripts\python -m pip install -r requirements.txt -i https://pypi.tuna.tsinghua.edu.cn/simple
.venv\Scripts\python -m pytest -q     # 预期 11 passed
$env:OFFLINE="1"
.venv\Scripts\python app.py
```

4. 验证：
   - 浏览器 `http://127.0.0.1:8000/health`
   - Unity Play：`过来`、`转过去`、`去窗边`、摸头/拍背
   - 走路应为 `WalkStart → Walk → WalkStop`，拐弯触发 `Pivot`

## 3.1 需要抽的 7 个 FBX

```
Assets/KAWAII_ANIMATIOMS_100/Assets/Animations/
  Idle/@KA_Idle01_breathing.FBX
  Idle/@KA_Idle44_GreetingBow.FBX
  Locomotion/Walk/@KA_Walk01.FBX
  Locomotion/Walk/@KA_Walk01_Start.FBX
  Locomotion/Walk/@KA_Walk01_Stop.FBX
  Locomotion/Walk/@KA_Walk01_Pivot.FBX
  Sit/@KA_Sit_CrossLegs_Loop.FBX
```

（`KawaiiAnimatorBuilder` 里写死了这些路径；文件名不能改。）

## 4. 不用带

- `DawnAndDusk/Library/`、`Temp/`、`Logs/`（Unity 自动重建）
- `server/.venv/`（路径绑死，重建）
- `UnityTest` 预览工程（只为看动作）

## 5. 不要做

- 不要升级 Unity 版本
- 不要把 Kawaii FBX / `_LocalTest` 提交到公开仓库
- 不要在 README 写“已购买授权”（0.1 元来源说不清）
- 不要把 `LLM_API_KEY` 写进仓库

## 6. 下一步

1. 验 Kawaii 的 Walk01 Start/Loop/Stop + Pivot
2. Unity 加 `GameLog`（收到 say/commands、显示、Animator 状态），定位：
   - 摸头对话丢失
   - 转过去后呼吸停
3. 膝枕相机（PoseCamera）
4. 两幕状态（夜/晨）
5. M2 录屏（12-12）

## 7. 2026-11-14 追加：真实场景是本地资产，怎么在另一台机器复原

demo 现在跑在**真实公寓场景**上，但两个场景包都按授权政策放进 `_LocalTest/`（gitignore），
所以 clone 下来只有灰盒 `NightRoom`。复原步骤：

| 资产 | 路径 | 大小 |
|---|---|---|
| Kawaii 动画包 | `C:\baidunetdiskdownload\KAWAII ANIMATIONS 100 v1.10.3.unitypackage` | 78.5 MB |
| 公寓场景包（MR 变体用这个） | `ArchVizPRO Interior Vol8 URP 1.0.unitypackage` | 3.1 GB |

```powershell
# 1) 动画（详见第 3 节，抽 7 个 FBX 到 Assets/_LocalTest/Kawaii/）
# 2) 公寓：包解开后把 Assets/ArchVizPRO_Interior_Vol.8_URP/ 整个放进
#    DawnAndDusk/Assets/_LocalTest/ArchViz/，保留 .meta
```

导入后在 Unity 里跑菜单：

```text
Tools → DawnAndDusk → Build Kawaii Animator     # 生成 controller + 步速表
Tools → DawnAndDusk → Build Apartment Demo      # 生成 ApartmentDemo.unity（主卧机位 + 锚点 + 组件）
```

`ApartmentDemo.unity` 的机位、锚点、组件全在 `ApartmentSceneBuilder.cs` 里用代码声明，
改坐标直接改那个文件再跑一次菜单即可（锚点落进床的包围盒会报错提示）。

> 注意：`ProjectSettings` 里的质量档当前是 **High Fidelity**（URP-HighFidelity，MSAA 4x）；
> Game 视图记得取消 `Low Resolution Aspect Ratios`，否则画面和 UI 都会糊。

## 8. 2026-11-15 动画接缝专项（结论 + 待办）

### 8.1 走路卡死的根因（已修）

`KawaiiAnimatorBuilder.SetLoop()` 用的是 `AnimationUtility.SetAnimationClipSettings`，
对 **FBX 导入片段只改内存对象、不写回 `.meta`**，所以 Idle/Walk 播一遍就钳在末帧：
Walk 停住 = 根位移归零、人卡在半路；Idle 停住 = "转过去后呼吸停"。
已改为直接写 `ModelImporter.clipAnimations[].loopTime` + `SaveAndReimport()`，
`KawaiiAnimatorVerifier` 新增 2.1 节断言（Idle/Walk/Sit 必须 loop，Start/Stop 必须不 loop）。

同批修复：`Animator.CrossFade(name, duration)` 的 duration 是**归一化时间**（Idle 6.67 秒 ⇒ 0.1 实为 0.67 秒），
`WalkStart` 因此常常进场就跨过 `exitTime=0.8`，起步片段播到末帧才切——改用 `CrossFadeInFixedTime`。

### 8.2 接缝多拖位移（已量化，修法已上）

实测每次 `WalkStart → Walk` 会多走约 **0.10 米**，且与混合时长无关：

| 混合时长 | 接缝窗口位移 | 峰值 |
|---|---|---|
| 0.30 s | 0.358 / 0.373 m | 1.5~1.6 m/s |
| 0.15 s | 0.225 / 0.227 m | 1.9~2.48 m/s |

用「固定偏移 + 步速 × 窗口」拟合得 F ≈ 0.095 m、v ≈ 1.08 m/s（两个窗口解出同一组值）⇒
**两段片段的根偏移不闭合，混合时这段差值被当成位移拖过去**（不是两段叠加）。
峰值 ≈ `F/窗口 + 步速`，所以混合越短越"猛"。

当前措施：`RootMotionRelay.maxSpeed = 1.35`（限速削峰，少走的 0.10m 由到站吸附兜住）。
**根治**：跑 `Tools → DawnAndDusk → Analyze Kawaii Clips`，看各片段"首帧/末帧根平面偏移"，
把 `WaveStart` 的裁剪末帧挪到与 `Walk` 首帧偏移一致的位置，然后可把混合调回 0.15~0.2。

### 8.3 调试工具

| 工具 | 用途 |
|---|---|
| `Logs/game.jsonl` | 10 Hz 结构化日志（anim / rootmotion / seam / rm_stall / move_stall / walk_end…） |
| `tools/walk-excess.ps1` | 对比路径折线与身体累计位移，输出超量 / 百分比 / 峰值 |
| `seam` 事件 | 过渡期间每 0.05s 的根位移窗口（cur/next/tNorm/sum/v） |
| `rm_clamp` 事件 | 限速削掉了多少（cut） |
| `rm_stall` 事件 | 走路状态且根位移为 0 时的 Animator 全量快照 |

> 注意：`GameLog` 的时间戳键是 `t`，业务字段不要再用 `t`（曾把时间戳覆盖成"走了几秒"，已改 `walkSec`）。
### 8.4 片段根偏移实测（`Tools → Analyze Kawaii Clips`，2026-11-15 02:56）

| 片段 | 首帧 (x,z) | 末帧 (x,z) | 位移 |
|---|---|---|---|
| WalkStart | (0.000, 0.005) | (-0.021, 0.404) | 0.399 m |
| Walk | (-0.016, 1.959) | (-0.016, 3.365) | 1.406 m（一个循环） |
| WalkStop | (0.001, 5.235) | (0.000, 5.331) | 0.097 m |
| Pivot | (-0.012, 2.090) | (-0.007, 1.178) | -0.912 m |
| Idle / Sit | — | — | 0（原地） |

**注意**：这几个是作者分别提供的独立 FBX（不是从一条 take 里切出来的），
每个文件的根坐标基准不同（Start 0.005 / Walk 1.959 / Stop 5.235）——那是导出保留的源场景绝对位置，
Unity 进入状态时会把根位置归零，因此「末帧减首帧」不能直接当接缝偏移用。

已知事实只有：`WalkStart → Walk` 每次多走 ≈0.095 m，与混合时长无关；
`maxSpeed=1.35` 只削峰（2.48→1.35），不减少总量（0.358→0.332）。
下一个可做的实验：把 `startToLoop.exitTime` 从 0.8 降到 0.62（等价于把 Start 提前收尾），
若超量随「收尾时剩余的根位移」等比下降 ⇒ 偏移驱动；若不变 ⇒ 另有机制。
### 8.5 决策：不裁剪片段（2026-11-15）

结论：**不采用"裁 Start 尾帧闭合偏移"的方案。**

理由：
1. 素材是作者**分别提供的四个文件**（`@KA_Walk01.FBX` 循环、`_Start`、`_Stop`、`_Pivot`），
   本来就是配套设计——2026-11-13 那晚已确认过"只用循环"还是"起止也加上"，最终选了全用。
   作者本身的衔接是好的：Start 末帧根速 0.90、Walk 首帧 0.96，只差 6%。
   接缝那 0.095m 更可能来自**交叉淡入时的姿态混合（髋部）**，不是原动画的缺陷。
   （注：三个文件的根坐标基准不同——Start 0.005、Walk 1.959、Stop 5.235——
   这是导出保留的源场景绝对位置，Unity 进入状态时会归零，不构成接缝偏移。
   所以不能拿"末帧减首帧"当接缝偏移用。）
2. 要消掉 0.095m，按 Start 平均根速 0.46 m/s 算，得砍掉约 0.2 秒（24 帧里的 6 帧，占 25%），
   正好是抬脚蹬地那一段——砍掉后起步会变成"突然开始滑行"，反而更糟。

因此保留现状：
- `RootMotionRelay.maxSpeed = 1.35` 限制接缝峰值（2.48 → 1.35），观感上"猛"的问题已解决；
- 剩余 0.095m 约等于 3m 路程的 3%，摊在 0.25 秒里，不构成可见问题。

若将来要求"零瑕疵"，正确方向不是裁帧，而是**去掉三状态拆分**：
用 `Idle + Walk` 的 1D Blend Tree，按 `Speed` 或归一化速度混合，
让起步/收势由混合权重自然产生（这是 locomotion 的标准做法）。
代价：状态机重建 + Start/Stop 两个片段退居不用，需要重新调手感。