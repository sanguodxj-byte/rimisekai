# Rimisekai 图像生成画风指导规范 (Art Style Prompt Guide)

> 本文档记录 Rimisekai 项目定稿的插画与立绘生成提示词规范，涵盖配色、笔触去噪、无边框约束、角色年龄与场景透视规则。

---

## 一、画风基准与核心特征

1. **色彩基准**：
   - 纯黑底色（`#000000`） + 骨白银墨线（`#E9EFEA`），唯一画风参照物为 `assets/title_reference.png`。
   - 严禁彩色、严禁灰阶平滑渐变（no color, no smooth gradients, no grayscale fills）。
2. **笔触与去噪铁律（2026-10-01 主人定）**：
   - **严禁微密交叉排线（Dense Cross-Hatching）与点绘（Stippling）**：此类排线在远视距或缩放时会产生严重砂砾颗粒与电视雪花噪点。
   - **阴影塑造**：改由**平滑硬边块面（Smooth Solid Tonal Blocks）**与**等距疏朗平行线条（Widely-Spaced Neat Parallel Lines）**构成，确保低频画面清晰纯净。
   - **线条特征**：连续、闭合、清晰的钢笔墨线轮廓（Crisp continuous ink outlines）。
3. **边框解耦铁律（2026-10-01 主人定）**：
   - **生成阶段一律无边框（Strictly Borderless）**：提示词中严禁生成任何边框、双线或外围框角花。
   - 边框已提取为独立透明素材（`assets/portrait_frame_overlay.png`），由前端 UI 在运行时直接贴合装裱。

---

## 二、通用提示词模块库

### 1. 基础风格前缀（画风与去噪）

```text
Match the art style, stroke texture, and contrast palette of the reference image: clean graphic ink manga illustration, pure pitch black background (#000000), bone-white ink (#E9EFEA) only. STRICTLY BORDERLESS: completely borderless, clean unbordered, absolutely no frame, no border lines, no corner flourishes, no outer box, no decorative borders of any kind. The subject stands freely and completely unconstrained on a pure pitch-black negative space extending edge-to-edge. NOISE ELIMINATION AND CLEAN LINEWORK: Crisp continuous ink outlines, broad smooth solid filled tonal blocks, uncluttered surfaces. Shadows and contours are rendered with clean graphic solid shapes and widely-spaced neat parallel lines, NEVER dense scratchy hatching, NO stippling, NO cross-hatching, NO grainy textures, NO visual noise. Pure low-frequency graphic clarity, smooth anime surfaces with crisp edges.
```

### 2. 负面提示词（必备通用约束）

```text
Negative prompt: flags, banners, pennants, heraldry, coat of arms, crests, emblems, insignias, frame, border, double lines, ornamental frame, border lines, corner flourish, decorative border, outer box, rectangular border, framing, edge lines, high frequency noise, scratchy hatching, dense micro-hatching, stippling, gritty texture, messy sketch lines, film grain, speckles, dust, dirty texture, fragmented lines, rough noisy shading, gradients, airbrush, 3d render, color, colored.
```

---

## 三、角色立绘规格（9:16 竖版）

1. **画幅与分辨率**：`size: "1024x1792"`（9:16 竖幅全身像）。
2. **年龄锁定铁律（2026-10-01 主人定）**：
   - 所有角色外表年龄锁定为 **14～16 岁幼态日漫二次元美少女**：
     `young anime [职业/种族] maiden, bishoujo, junior adventurer appearance, petite youthful build, slender teenage proportions. Delicate youthful anime face, large sparkling manga eyes with eyelashes.`
   - 负面词追加：`mature, adult, elderly, tall heavy build`。
3. **姿势多样化铁律（2026-10-01 主人定）**：
   - **除了都是站姿以外，禁止姿势固定化**；
   - 严禁清一色 3/4 侧站或死板证件照，必须丰富运用：张弓搭箭步、驻剑倚盾步、轻步虚足步、背身回眸步、倾胯对立步（Contrapposto）、叉腰英姿等。

---

## 四、场景插画规格（16:9 横版）

1. **画幅与分辨率**：`size: "1792x1024"`（16:9 宽屏）。
2. **第一人称视点规则**：
   - 必须采用**第一人称平视视角（First-person eye-level perspective）**，呈现沉浸式探查感。
3. **建筑体系**：
   - 一律**西欧西幻石砌体系**（粗石砌墙、哥特尖拱、实木梁架、铸铁吊灯）。
   - 严禁和风元素（负面加：`no East Asian elements, no curved tile roofs, no paper lanterns`）。
4. **DRPG 地下城通道规则**：
   - 采用经典 3D Grid DRPG 单点透视（One-point perspective dungeon hallway，参考 Wizardry / Path of the Abyss）；
   - 方形/尖拱石砌回廊深邃延伸至消失点，石板地面网格平铺，墙侧铸铁火把托架与阴影对比。
5. **战斗场景背景铁律（2026-10-01 主人定）**：
   - **绝对严禁出现任何人体、人手、手臂、持械手掌、角色、人形雕像、生物或尸体**（`no first-person hands, no arms, no weapons held by hands, no people, no characters, no bodies, purely empty battleground environment`）。
   - 战斗场景是纯净的遭遇战空旷竞技场/环境底图（游戏前端 `InkCombatScreen` 会在上面动态渲染双方角色卡、速度轴与技能框），底图中绝不允许自带第一人称手臂或武器。

---

## 五、API 调用配置（sakiko.dev）

- **Endpoint**：`https://sakiko.dev/v1/images/edits`
- **Model**：`image/chat2api-gpt-image-2.5`
- **参考图**：`assets/title_reference.png`（通过 base64 data URL 传入，引导黑白墨线骨架与对比度）
- **并发能力**：支持多线程并发（推荐 3～5 并发），大幅提升批量生成效率。
