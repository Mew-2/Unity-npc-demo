# AI NPC 生态调研（2026-11-04）

> 目的：找灵感、找能白嫖的轮子、判断场景。star 数为 2026-11-04 快照。

## 一句话结论

1. **轮子够用，但没有"整套"可白嫖**：推理/结构化输出/记忆/配音各有成熟件；把"LLM 输出结构化指令 → 场景状态变化"串成完整小件的开源项目几乎没有——这正好是我们的差异点。
2. **B 站流量在"AI 二次元陪伴"**（Live2D + TTS + 聊天），工程向的"游戏内 NPC 行为"很少。
3. **场景要服从 3 条指令**：你提的"关灯/更衣"不在 spawn/move/look_at 里，硬做要加第 4 个动词（范围变更）；建议 v1 换成"夜营"。

---

## 一、B 站现状

主流形态是"AI 二次元小姐姐"：Live2D 形象 + TTS 语音 + LLM 聊天，教程集中在 Unity 接 GPT-SoVITS / 讯飞星火 / 本地 Ollama。

| 视频 | 内容 |
|---|---|
| [Unity+GPT-SoVITS](https://www.bilibili.com/video/av411698606/) | 自训练语音模型驱动二次元角色，接近"游戏原声" |
| [Live2D+ChatGPT+Unity 纸片人老婆](https://www.bilibili.com/video/BV1KM411g7X2/) | 手把手把 ChatGPT 接进 Live2D |
| [Unity+讯飞星火](https://www.bilibili.com/video/BV1vFMEzyEc6/) | 国产模型驱动聊天 |
| [Unity+Ollama 本地部署](https://www.bilibili.com/video/BV1Vb421a7oa/) | 断网也能跑 |
| [Unity 官方 AI Planner 直播](https://www.bilibili.com/video/BV1bJ411P7GG/) | 用 AI 规划角色行为，偏行为生成 |

**观察**：流量在"陪伴/嘴替"，不在"游戏内 NPC 行为"。做陪伴向容易有播放量，但和 JD 的"引擎原理 + Agent 管线"距离远；工程向作品少，反而稀缺。

## 二、GitHub 轮子（可复用清单）

| 项目 | ★ | License | 能替我们做什么 | 建议 |
|---|---|---|---|---|
| [LLMUnity](https://github.com/undreamai/LLMUnity) | 1.7k | Apache-2.0 | Unity 内跑本地 gguf、RAG、grammar/function calling、聊天存档、远程模式；已测 Unity 6 | **备用离线方案**，不替换 FastAPI |
| [OpenGameAgent](https://github.com/EricSun0218/OpenGameAgent) | 59 | MIT | TS runtime：durable actions、memory、goals、MCP tools、Unity 6 C# 包 | 架构参考；不引入 Node 栈 |
| [SkyrimNet](https://github.com/MinLL/SkyrimNet-GamePlugin) | 367 | NOASSERTION（待核） | 说话/记忆/反应/行动/实时配音闭环 | 设计参考（action+memory loop） |
| [Spatial_AI_NPC](https://github.com/xbowen125-commits/Spatial_AI_NPC) | 32 | MIT | Unity/VRM + 空间感知 + 关系记忆 + 情绪 | 参考，小项目慎接 |
| [chasm](https://github.com/chasmlol/chasm) | 79 | 无 | 游戏无关的 NPC 后端（LLM+声音+记忆+行动） | **不接**（无许可证） |
| [Real-Agents](https://github.com/AkiKurisu/Real-Agents) | 60 | MIT | Unity 生成式代理规划框架 | 参考（2025-01 后未更新） |
| [Open-LLM-VTuber](https://github.com/Open-LLM-VTuber/Open-LLM-VTuber) | 14.5k | NOASSERTION | 免手语音打断 + Live2D + 记忆 | 参考（非 Unity） |
| [generative_agents](https://github.com/joonspk-research/generative_agents) / [ai-town](https://github.com/a16z-infra/ai-town) | 22k / 10.7k | MIT | memory stream / reflection / 多 agent | 记忆设计参考 |
| [GPT-SoVITS](https://github.com/RVC-Boss/GPT-SoVITS) | 64.8k | MIT | 中文 TTS/音色克隆 | v1.5 配音可选 |
| [piper](https://github.com/rhasspy/piper) | 11.3k | MIT | 本地快速 TTS | 可选 |
| [mem0](https://github.com/mem0ai/mem0) | 68k | Apache-2.0 | 记忆层 | v2 |
| [outlines](https://github.com/dottxt-ai/outlines) / [instructor](https://github.com/567-labs/instructor) | 16k / 14k | Apache-2.0 / MIT | 强制结构化输出 | 我们已有校验，可选增强 |
| [uLipSync](https://github.com/hecomi/uLipSync) / [UniVRM](https://github.com/vrm-c/UniVRM) | 1.7k / 3.4k | MIT | 口型同步 / VRM 形象 | 有真人口型再上 |
| [fish-speech](https://github.com/fishaudio/fish-speech) | 33.6k | NOASSERTION | 语音合成 | 商用需核对许可 |

**结论**：核心链路没有"现成整套"可白嫖。最接近的 OpenGameAgent 自带 Node + SQLite 栈，接进来比手搓我们那 150–200 行服务更重。推理层如果要做离线兜底，LLMUnity 是唯一值得考虑的（Apache-2.0、Unity 6 已测、grammar 能直接约束 JSON）。

## 三、架构上值得偷的四件事

1. **SkyrimNet**：talk → remember → react → act → voice 一条闭环；行动由规则层裁决，不是模型直接改世界。
2. **OpenGameAgent**：游戏保留权威状态与权限，Agent 只发 action 请求；approval / memory / goals 分层。
3. **Generative Agents**：memory stream + reflection + planning，解决"记得住、想得起"。
4. **Spatial_AI_NPC**：空间感知 + 关系记忆 + 情绪，NPC 记得"你站在哪、之前聊过什么"。

我们的架构已经同构（LLM 出 JSON → 客户端校验 → 执行 → 日志）；v2 可把"10 轮上下文"升级为"事件摘要 + 关系状态"。

## 四、场景候选 vs 3 条指令

| 场景 | 3 指令覆盖 | 视觉 | 叙事 | 风险 |
|---|---|---|---|---|
| **夜营**（推荐） | 生火/搭帐篷=spawn；去溪边/帐篷=move；看星星/看我=look_at | 火、星、帐篷，强 | 一起冒险的第一夜 | 户外资产略多 |
| 灯塔小屋（现草案） | 添灯=spawn 灯灵；去窗边=move；看海=look_at | 暖、安静 | 守灯人 | "关灯"不支持 |
| 卧室/女仆 | 引上床=move；看我=look_at；讲故事=say；**关灯/更衣不支持** | 单一 | 亲密 | 需加 set/equip；公开叙事风险 |
| 庭院/花园 | 种花/放灯=spawn；去秋千=move；看月=look_at | 中等 | 温柔 | "冒险"弱 |

## 五、对"女仆侍寝"的判断

情感钩子强，B 站也证明有受众；但两个硬伤：

1. **动作不匹配**：关灯、更衣需要新动词，现在做 = 范围变更（+2–3h 且要多一轮联调）。
2. **公开风险**：仓库 public、录屏会被面试官和平台看到；"侍寝"这个词的收益 < 风险。

折中：角色保留女仆/旅伴设定，场景换成"深夜驿站/露营"，把"更衣"换成"披毯子"、"关灯"换成"生火/守夜"。真想拍这个题材，留作个人向 v2，不进这个 repo。

## 六、建议

- **v1 场景：夜营**（资产超时则退回灯塔小屋）。
- **轮子策略**：核心架构不动；LLMUnity 只做离线兜底备选；TTS/口型留 v1.5。
- **下一步**：场景拍板 → 改 SPEC §1（锚点与指令映射）→ 四问定稿。
