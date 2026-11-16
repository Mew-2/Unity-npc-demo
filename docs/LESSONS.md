# LESSONS — 隐性知识

> 只收“踩过、花过时间、可能再踩”的结论；不记改了什么，提交记录可查。
> 每条带来源与验证时间；每两个月或每个里程碑梳理一次，过期/被取代就删改。
> 来源写法：`人` / `鲸蕙(DSH-C)` / `鲸若(DSH-P)` / `人+鲸蕙` / `人+鲸若`。
> 鲸蕙 = 公司电脑（DSH-C）；鲸若 = 自己的笔记本（DSH-P）。

## 动画 / Unity

### 优先用引擎的权威数据

- **现象**：按片段采样步速表驱动位移，走 5.29m 时脚仍滑 3.52m（67%）。
- **根因**：把动画压成一个标量速度，丢了循环内逐帧变化和相位。
- **结论**：有 root motion / `deltaPosition` 就用它；手搓速度表只在没有权威来源时成立，且应让动画跟随速度，而不是反过来。
- **边界**：跨端同步必须代码驱动时，另做钳制。
- 来源：Mew-2（人）+ 鲸若（DSH-P，DeepSeek）｜验证：2026-11-15

### 中间层必须能逐帧验证

- **现象**：步速表每次不对只能改参数重跑，一轮 3 分钟，一晚几十轮。
- **结论**：不可观测的抽象层会把调试成本放大十倍；要么能逐帧校验，要么别造。
- 来源：Mew-2（人）+ 鲸若（DSH-P，DeepSeek）｜验证：2026-11-15

### `loopTime` 必须写进 FBX 的 `.meta`

- **现象**：Idle 播一遍就停（“呼吸丢失”）、Walk 播完卡住。
- **根因**：`AnimationUtility.SetAnimationClipSettings` 只改内存对象，不写回导入器设置。
- **结论**：改 `ModelImporter.clipAnimations[].loopTime` 后 `SaveAndReimport()`；Start/Stop 必须不循环。
- 来源：Mew-2（人）+ 鲸若（DSH-P，DeepSeek）｜验证：2026-11-15

### `Animator.CrossFade` 的时长是归一化时间

- **现象**：`CrossFade("WalkStart", 0.1f)` 在 6.67s 的 Idle 上实际跨了 0.67s，起步片段常被跳过。
- **结论**：要按秒就用 `CrossFadeInFixedTime`；过渡时长用 `hasFixedDuration`。
- 来源：Mew-2（人）+ 鲸若（DSH-P，DeepSeek）｜验证：2026-11-15

### 拍完预览图必须恢复相机

- **现象**：构建后直接 Play，视角停在“窗边朝门口”，连续几小时误判成玩法问题。
- **结论**：`Shoot()` 临时移动主相机后必须还原；磁盘场景对、内存场景脏，先查观测链路。
- 来源：Mew-2（人）+ 鲸若（DSH-P，DeepSeek）｜验证：2026-11-15

### 坐姿期间不要累积重力

- **现象**：起身被弹到床面。
- **结论**：坐姿状态清 `_vertical`，起身再恢复；状态切换要显式处理竖直速度。
- 来源：Mew-2（人）+ 鲸若（DSH-P，DeepSeek）｜验证：2026-11-15

### 接缝位移的取舍

- **结论**：`WalkStart → Walk` 每次多约 0.095m，来自两段根偏移不闭合；已用 `maxSpeed=1.35` 削峰 + 到站吸附，**决定不裁剪片段**。
- **边界**：要根治需把 WalkStart 末帧根偏移对齐 Walk 首帧，再调混合时长。
- 来源：Mew-2（人）+ 鲸若（DSH-P，DeepSeek）｜验证：2026-11-15

### 自动栅格不要从全场景 Renderer 生成

- **现象**：`grid_built free=17/1332`，A* 永远失败，`过来` 回退到 `anchor:west`。
- **根因**：把 741 个 Renderer（书、杯子、摆件）都当障碍，再按 0.32m 膨胀，房间被封死。
- **结论**：小空间/多房间优先 NavMesh；若坚持栅格，只收显式 Layer 的 Collider，并做最小尺寸 + 侵蚀过滤。
- **边界**：换场景/换家具布局后，栅格输入要重新验证 `free` 数量。
- 来源：Mew-2（人）+ 鲸蕙（DSH-C，DeepSeek）｜验证：2026-11-16

## 环境 / 工具

### 先分层，再换变量

- **方法**：`ls-remote` 验 TLS/认证 → `clone --depth=1` 验 pack → 单独测文件写权限。
- **结论**：小请求能过不代表大 pack 能过；一次只改一个变量（SSL backend、HTTP 版本、代理、镜像）。
- 来源：Mew-2（人）+ 鲸蕙（DSH-C，DeepSeek）｜验证：2026-11-16

### Git for Windows 的 schannel 可能拿不到凭据

- **现象**：`schannel: AcquireCredentialsHandle failed: SEC_E_NO_CREDENTIALS`。
- **结论**：可试 `git config http.sslBackend openssl`；切换后先 `ls-remote` 复测。
- **边界**：这是工具/环境问题，不是所有机器必现。
- 来源：Mew-2（人）+ 鲸蕙（DSH-C，DeepSeek）｜验证：2026-11-16

### HTTP/2 配合本地代理可能卡 pack

- **现象**：`ls-remote` 正常，`clone/fetch` 卡 `Receiving objects`。
- **结论**：可试 `http.version=HTTP/1.1`；仍卡就换镜像或直连，不要只怪 DNS。
- 来源：Mew-2（人）+ 鲸蕙（DSH-C，DeepSeek）｜验证：2026-11-16

### 浅克隆不要直接 merge

- **现象**：`fetch --depth=1` 后 merge 报 `refusing to merge unrelated histories`。
- **结论**：先 `git fetch --unshallow`；失败就重新 clone，别在浅仓库上反复 deepen。
- 来源：Mew-2（人）+ 鲸蕙（DSH-C，DeepSeek）｜验证：2026-11-16

### 第三方镜像只用于公开仓库

- **结论**：镜像能看到流量；不要带 token，不要用于私有库。
- **优先**：官方 SSH 443 > 镜像 > 本地代理。
- 来源：Mew-2（人）+ 鲸蕙（DSH-C，DeepSeek）｜验证：2026-11-16

### DSH 沙箱的 ACL 坑

- **结论**：workspace-write 只保证工作区；子进程仍可能受 ACL 限制；`.git/FETCH_HEAD` 可能不可写；目录被 cwd 占用时 rename 会失败；移动目录保留原 ACL。
- **做法**：先切父目录、显式授权；不要用 Python 批量同步，权限中断会半同步。
- 来源：Mew-2（人）+ 鲸蕙（DSH-C，DeepSeek）｜验证：2026-11-16

### 环境结论不进 HANDOFF

- **结论**：换机器、换网络、换工具版本，任一会变 → 只留本地记录，不写进共享交接。
- 来源：Mew-2（人）｜验证：2026-11-16

## 方法 / 协作

### 参数调三次还不准，问题在模型

- **结论**：同一参数反复调仍不对，通常是模型少一个自由度；换机制比继续调参便宜。
- 来源：Mew-2（人）+ 鲸若（DSH-P，DeepSeek）｜验证：2026-11-15

### 换掉机制后，剩余问题会回到机制本身的文档化行为

- **结论**：`loopTime`、过渡时长、`CrossFade` 归一化都是 Unity 有文档的行为；自造方案剩下的坑才是自己发明的语义。
- 来源：Mew-2（人）+ 鲸若（DSH-P，DeepSeek）｜验证：2026-11-15

### 先确认“你看到的就是玩家看到的”

- **结论**：数据与画面不一致时，先验观测链路（相机是不是玩家的、日志字段是不是它声称的那个），再查业务。
- 来源：Mew-2（人）+ 鲸若（DSH-P，DeepSeek）｜验证：2026-11-15

---

> 维护：Mew-2（人）+ 鲸蕙（DSH-C）/ 鲸若（DSH-P）｜最后梳理 2026-11-16
