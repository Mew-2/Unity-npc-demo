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
