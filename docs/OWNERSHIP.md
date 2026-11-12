# 归属记录（Ownership Log）

> 规则：AI 可以代写代码，但每个模块交付时用户必须通过“3 问自测”。
> 答不上来就不往下写；答得上来才算真正“拥有”这段代码。
> 这份记录是面试前复习提纲，也是“独立落地”的证据。

## 已交付模块

| 模块 | 谁写 | 用户改了什么 | 用户验证了什么 | 3 问状态 |
|---|---|---|---|---|
| `Anchor` / `AnchorGraph` | AI | 仅阅读 | 灰盒生成后 7 个锚点出现在场景里 | 待问 |
| `NpcAgent` | AI | 仅阅读 | bed→window 走通；重跑灰盒后锚点坐标正确 | **Q1 半对（已补讲）/ Q2 通过 / Q3 通过** |
| `GreyboxBuilder` | AI | 决定重跑 | 生成房间 + 锚点 + 相机 + NPC | 待问 |
| `server/`（app / validator / prompt / llm / offline） | AI | 指定“离线优先” | `/health`、`/direct` 正常；pytest 11 passed | 待问 |
| `DirectorClient` / `DialogueUI` / `CommandExecutor` | AI | 无 | 端到端：输入 → `/direct` → `move` / `look_at` 生效 | 待问 |
| `ExpressionController` | AI | 无 | Play 中 Shift+1~5 切换表情；近景可见 | 待问 |
| `PlayerActionDetector` / `ReactionController` | AI | 无 | 摸头 / 拍背 / 轻触触发表情、台词、转身 | 待问 |

## 2026-11-06 自测结果

- **Q1**：用户指出 bed→window 本来就是直连边，看不出区别——观察正确，是提问选例不好。
  补讲：锚点图的价值在“没有直连边的两点”（如 door→desk 会经 bed、screen 拐弯）、拓扑约束与可验证性，不是动态避障。
- **Q2**：通过。`FindPath` 返回空 → 日志 + return，NPC 原地不动，`CurrentAnchorId` 不变；后续由 CommandExecutor 反馈“做不到”。
- **Q3**：通过。改锚点坐标，不改胶囊半径；半径属于角色体型/碰撞，布点规则是离墙留 0.6m。

## NpcAgent 三问（2026-11-06 已答）

1. 为什么用 `AnchorGraph.FindPath` 走最短路径，而不是直接朝目标点 `MoveTowards`？
2. 如果 `FindPath` 返回空列表（目标不可达），代码会怎么走？玩家会看到什么？
3. 胶囊贴墙了，应该改哪一处（锚点坐标 / 胶囊半径 / 别的）？为什么改那里？

> 答案记录见上方“自测结果”。

## 服务端三问（待答）

1. 客户端已经校验一遍了，为什么服务端还要校验？
2. 校验发现一条指令不合法，为什么整批丢弃，而不是执行合法的部分？
3. 离线回放（`OFFLINE=1`）在录屏时解决什么问题？为什么不直接调模型？

## 客户端三问（待答）

1. 为什么 `move` 要等 `!npc.IsMoving` 才开始下一条，而不是一口气发完？
2. 客户端已经知道锚点，为什么还要把世界快照发给服务端？
3. 这里用 `JsonUtility` 而不是 Newtonsoft，取舍是什么？

## 后续模块（占位）

> 2026-11-06 用户调整：AI 优先全写，用户只在卡住/出问题时介入；3 问保留，作为面试复习。

| 模块 | 计划归属 | 备注 |
|---|---|---|
| `DirectorClient` | AI | Unity 调 `/direct`、超时、离线兜底 |
| `CommandExecutor` | AI | 4 条指令执行 + 全量校验 |
| `DialogueUI` | AI | 输入框 / 气泡 / 打字机 |
| `StateSwitcher` | AI | 灯 / 窗帘 / 换装 |
