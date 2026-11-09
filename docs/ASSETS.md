# 资产台账（ASSETS）

> 只记录来源与状态，不等于已采用。换角色前必须逐项确认授权。

## v1 采用

| 资产 | 格式 | 来源 | 授权 | 状态 |
|---|---|---|---|---|
| DeepSeek-chan VRM（鲸鱼娘） | VRM 1.0 | [tegnike/deepseek-chan-vrm](https://github.com/tegnike/deepseek-chan-vrm) | CC BY-NC-SA 4.0：免费、非商用、需署名、改后同协议 | **v1 采用**（2026-11-09 定） |

- 规格：18 个预设表情 + 23 个部件表情、40 根摇摆骨、约 9.2 万三角面、MToon
- 署名：溟月（上善无形）、女仆版（ZipZipPipe）、3D（ニケ / tegnike）
- 注意：同人形象，非 DeepSeek 官方

## v2 换角色候选

| 资产 | 格式 | 来源 | 授权 | 状态 |
|---|---|---|---|---|
| Firefly Maid（Geffol） | VRM | [模之屋 / AplayBox](https://www.aplaybox.com/details/model/r8e5EPvmcLzM) | **待确认**（站内逐模型授权不同） | 候选，先记下 |
| Geffol 其他角色（Ellen Joe / Anie Dead Maid / Arisu Maid 等） | VRM | 同上作者页 | 待确认 | 候选 |

- 用途：验证“可更换角色”架构（`CharacterProfile` + `Npc/Visual`）
- 前提：授权允许非商用公开 demo；风格与场景匹配（女仆/夜间）

## 看过但不采用

| 资产 | 格式 | 原因 |
|---|---|---|
| DeepSeek_Reset_4K_Lite（sdn1111） | `.blend`（Blender 5.2.1） | 渲染工程，非游戏绑定；骨架/材质/表情需 4–8h 转 VRM，v1 不做 |
| BOOTH「大肥鱼 DeepSeek」¥5,000 | 恋活角色卡 | 不是 VRM；作者禁止再配布 |
| Sketchfab「Deep Seek」 | GLB（logo） | 只是标志，不是角色 |
| Meshy「deepseek chan」 | GLB（静态） | 无骨骼、无表情 |
| MakerWorld 系列 | 3D 打印 | 不可绑定 |

## 2D 素材图库

| 图库 | 内容 | 授权 | 用途 |
|---|---|---|---|
| [DeepSeek 鲸鱼娘角色图集](https://iphsiibl.github.io/deepseek-whale-girl-gallery/#p-sheets) | **165 张 / 5 分区**：三视图设定、萌娘百科归档、7 套换装三视图、社区二创 147 张、壁纸 | **CC BY-NC-SA 4.0**（署名、禁商用、衍生同协议） | 角色参考、立绘、img2img / ControlNet 垫图、UI 素材 |

- 上游资产库：[TreapGoGo/deepseek-whale-girl](https://github.com/TreapGoGo/deepseek-whale-girl)；每张图的作者与出处见其 `ATTRIBUTION.md`
- 授权链：原型「溟月」上善无形 → 女仆版 ZipZipPipe → 社区二创
- **只引用链接，不把图片提交进仓库**（版权 + 体积）
- 适合：给 VRoid / Charakuru / 图像模型做角色一致性参考；做 UI 立绘时按 CC 要求署名

## 相关链接

- [awesome-whale-girl](https://github.com/wytyKen/awesome-whale-girl)：鲸鱼娘资源总表
- [AI 萌化百科](https://selinlee.github.io/ai-moe-atlas/zh/models/)：各 AI 拟人形象索引
