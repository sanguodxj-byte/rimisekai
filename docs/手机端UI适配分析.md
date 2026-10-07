# 手机端 UI 适配分析（2026-10-02）

取证材料全部在 `_ui_mobile/`：31 张各页面实机截图、111 张手机/平板比例合成图（`dev_<页>_<档案>.png`，
每页 3 档案、6 个关键页 6 档案）、6 张热区违规标注图、
`hits.jsonl`（483 条实际注册热区）、`analysis.txt`（本文件所有数字的原始输出）、`analyze.py`（可重跑）。

## 0. 一句话结论

**现在的界面在手机上「能显示、不能操作」。** 不是差一个缩放参数，而是物理尺度整体小 4.3 倍：
411 个可点热区里 **375 个（91.2%）短边不足 7.6mm（48dp）**，中位只有 **2.83mm**；
今天刚定的全局最小字号 26px 在手机上只有 **1.67mm / 16.4 弧分**（桌面基准 7.19mm / 41.2 弧分）。
要把热区放大到合规需放大 2.68×，那时 1920×1080 只剩 **14%** 能进屏 —— 缩放救不了，只能重排版式。

> **竖屏方案另见 §9**（主人 2026-10-02 提出）：竖屏不比横屏贵，48dp 容量反而多 19%，立绘已是 9:16 白捡；
> 代价是 16:9 插画带与「五面板同屏总览」的消失。示意图 `_ui_mobile/portrait_vs_landscape.png`。

---

## 1. 各页面截图清单（`_ui_mobile/`，1920×1080 实机出图）

| # | 屏 | 文件 | # | 屏 | 文件 |
|---|---|---|---|---|---|
| 1 | 标题画面 | `01_title` | 17 | 角色·技能页 | `17_skills` |
| 2 | 据点主界面 | `02_hub` / `03_hub_chars` | 18 | 角色·日程页 | `18_schedule` |
| 3 | 房间行动态（观察） | `04_observe` | 19 | 任务页 | `19_quest` |
| 4 | 世界层地图 | `05_world` | 20 | 战斗界面 | `20_combat` |
| 5 | 兴趣点 | `06_poi` | 21 | 战斗·出手瞬间 | `21_combat_strike` |
| 6 | 聊天层 | `07_chat_overlay` | 22 | 战斗·结束态 | `22_combat_settle` |
| 7 | 场景演出 | `08_scene` | 23 | 战斗日志页 | `23_combatlog` |
| 8 | 设施交互页 | `09_storage` | 24 | 弹窗·问卷 | `24_modal_question` |
| 9 | 改名弹窗 | `10_rename` | 25 | 弹窗·输入 | `25_modal_input` |
| 10 | 库存页 | `11_stock` | 26 | 弹窗·确认 | `26_modal_confirm` |
| 11 | 交易页 | `12_trade` | 27 | 弹窗·叙事 | `27_modal_narrative` |
| 12 | 制作页 | `13_craft`（见 §8-1） | 28 | 弹窗·战后结算 | `28_modal_settle` |
| 13 | 开发页 | `14_develop` / `15_develop_confirm` | 29-31 | 系统页 设置/存档/读档 | `29..31_system_*` |
| 14 | 角色·状态页 | `16_status` | | | |

复现：`Godot_v4.7-stable_mono_win64_console.exe --path . res://RimisekaiFrontend/src/Tools/Capture.tscn -- --page=<页> --capture=<路径>`
（批量脚本 `_ui_mobile/grab_all.sh`）。

**本机拿不到非 16:9 窗口**：`--resolution 2400x1080` 一类的请求被 Windows 拉回 16:9（实测 1920×864→1536×864、
1600×720→1280×720，宽总被削到等比）。所以手机比例图用**引擎自己的拉伸规则确定性合成**，不是手 P：
`canvas_items` + `aspect=keep` ⇒ 等比缩放 `s = min(W/1920, H/1080)`、居中、余量填黑。
该规则已实测校准：1280×720 的真实截图与 1920×1080 基线等比缩小后逐像素平均差 2.4/255。

---

## 2. 手机上实际会画成什么样（横屏，`handheld/orientation=sensor_landscape` 已配）

| 设备 | 面板 | ppi | 缩放 | mm/画布px | 1px 视角 | UI 物理尺寸 | 黑边 |
|---|---|---|---|---|---|---|---|
| iPhone SE/8（小屏下限） | 1334×750 | 326 | 0.694 | 0.0542 | 0.56′ | 104×59 mm | 0% |
| Galaxy S24 | 2340×1080 | 416 | 1.000 | 0.0611 | 0.60′ | 117×66 mm | 17.9% |
| 主流 20:9 中端机 | 2400×1080 | 395 | 1.000 | 0.0644 | 0.63′ | 124×70 mm | **20.0%** |
| iPhone 15 Pro Max | 2796×1290 | 452 | 1.194 | 0.0672 | 0.66′ | 129×73 mm | 18.0% |
| 1080p 16:9（理想） | 1920×1080 | 339 | 1.000 | 0.0749 | 0.74′ | 144×81 mm | 0% |
| iPad 10.9 | 2360×1640 | 264 | 1.229 | 0.1184 | 0.91′ | 227×128 mm | 0% |
| **24 寸 1080p（当前设计基准）** | 1920×1080 | 92 | 1.000 | 0.2767 | 1.59′ | 531×299 mm | 0% |

三点要记住：

1. **20:9 / 19.5:9 手机左右各吃掉 9～10% 宽度**（合成图 `dev_*_mid20x9.png` 清楚看到两条黑边）。
2. **手机档案之间 mm/px 几乎一样**（0.061～0.067），因为都是「1080 逻辑高 + 高 ppi」。所以适配难度不由机型决定，
   只有 iPad（0.118）和 16:9 1080p 手机（0.075）明显宽松些。
3. **设计基准与手机的差距是 4.3×**（0.2767 / 0.0644）。整套线稿语汇是按 0.28mm/px 的墨水宽度画的。

---

## 3. 三条硬约束（量化）

### 3-1 字号与宋体笔画

| 画布 px | 20:9 手机 | 视角 | 24 寸基准 | 判定 |
|---|---|---|---|---|
| 26（今天定的全局最小） | 1.67 mm | 16.4′ | 7.19 mm / 41.2′ | 手机：偏小，宋体糊 |
| 22 | 1.42 mm | 13.9′ | 6.09 mm / 34.9′ | 手机：不可读 |
| 18 | 1.16 mm | 11.4′ | 4.98 mm / 28.5′ | 手机：不可读 |
| 14 | 0.90 mm | 8.9′ | 3.87 mm / 22.2′ | 手机：不可读 |

- 参照：中文正文可读下限一般取 em ≥ 3.2mm / ≥ 20 弧分（观看距离 350mm）。
- **宋体是这套 UI 的致命处**：横画约 em/12。26px 时手机上笔画宽 ≈ 0.14mm ≈ 2.2 物理像素、视角 1.4 弧分，
  低于 2 弧分的分辨阈 —— 不是「小」，是**笔画并成灰块**。要 26px 达到 3.2mm em，需放大 **1.91×**，
  此时可见画布只剩 1004×565（**27%**）。

### 3-2 热区（483 条注册项实测，取 20:9 手机换算）

| 屏 | 有效热区 | 短边中位 | 最小 | 不达 48dp | 最小的几个 |
|---|---|---|---|---|---|
| 据点 hub | 22 | 2.96 mm | 1.67 | 91% | LogToggle 44×30=1.9mm、CharStatus/CharSkills 72×26=1.7mm |
| 观察态 | 22 | 2.96 mm | 1.67 | 91% | 同上 |
| 世界层 | 18 | 2.83 mm | 1.67 | 89% | 同上 |
| 兴趣点 | 14 | 2.96 mm | 1.67 | 93% | 同上 |
| 改名态 | 24 | 2.96 mm | 1.67 | 92% | 同上 |
| 库存页 | 25 | 2.83 mm | 1.67 | 92% | 同上 |
| 交易页 | 41 | 2.83 mm | **0.58** | 95% | ScrollThumb 9×28=0.6mm |
| 制作页 | 25 | 2.83 mm | 1.67 | 92% | 同上 |
| 开发页（进模式） | 45 | 3.73 mm | **0.58** | 96% | ScrollThumb 0.6mm、DevDemolishRoom 20×20=1.3mm |
| 状态页 | 29 | 2.83 mm | 1.67 | 93% | 同上 |
| 技能页 | 41 | 2.96 mm | 1.67 | 78% | 同上 |
| 日程页 | 37 | 2.96 mm | 1.67 | 95% | 同上 |
| 设施交互页 | 29 | **2.45 mm** | 1.67 | **96%** | StorageClose 30×30=1.9mm |
| 战斗界面 | 14 | 2.96 mm | 2.83 | 79% | CombatCategory 210×44=2.8mm |
| **合计** | **411** | **2.83 mm** | **0.58** | **91.2%** | 达 60dp 的同样只有 8.8% |

- 48dp（7.6mm）在这块屏上等于 **118 画布 px**，60dp（9.5mm）等于 **148 画布 px** —— 而整个画布高只有 1080。
- 标注图：`audit_hub_mid20x9.png`、`audit_stock_…`、`audit_status_…`、`audit_combat_…`、`audit_develop_mode_…`、
  `audit_storage_…`（红 <7.6mm、黄 <9.5mm、绿合规）。据点页只有两张角色卡是绿的，其余全红。
- **要做到热区合规需放大 2.68×，可见画布 716×403 = 14%**；60dp 则 3.35×、剩 9%。
  ⇒ 手机端不可能复用五面板同屏版式，必须**一页一屏、单栏重排**。

### 3-3 细线（这套美术的立身之本）

`Ink` 双线框外线 2px / 内线 1px `Dim`、分隔线 1px `Dim@0.5`、角花细描边：在 20:9 手机上 1px = 0.064mm，
经线性过滤后是**一条灰影而非线**；2px 也只是勉强。手机 profile 下所有线宽必须按 mm 反推（≥0.35mm ≈ 5.5 画布 px），
不能沿用 1/2px。

---

## 4. 交互模型：触摸下能不能用

> ⚠️ 本节行号是 **2026-10-02 对横版层的取证**，用于说明「为什么必须重做竖版」。其中
> `InkHubScreen`/`InkRenameRenderer` 仍在盘上（被竖版编译依赖），`InkQuestScreen`/`InkScreenRouter`/`InkRoot`
> 等入口层已于 2026-10-03 删除归档（见 §13）——**行号不可再按图索骥**，结论仍成立。

`pointing/emulate_mouse_from_touch=true` 已开，**普通点击全链路可用**（全项目零处使用 `InputEventScreenTouch`，
也没有任何地方要求真实鼠标设备）。逐条判定：

| 判定 | 事项 | 证据 |
|---|---|---|
| 阻断 | **文本输入全靠逐键 `InputEventKey`**：改名、开局命名、库存/交易搜索三处。Android 软键盘（尤其中文 IME）不会按键送 Unicode，且从不调用 `DisplayServer.KeyboardShow()`，全项目无 `LineEdit` | `InkHubScreen.cs:763`（弹窗输入）、`:798`（改名）、`:738`（搜索）；`InkRenameRenderer.cs:20` 提示语还写着「回车确定 Esc 取消」 |
| 阻断 | **任务页右键移除队员**，触摸无右键，`PartyClear` 只能一次清空 | `InkQuestScreen.cs:149-157` |
| 阻断 | **任务页滚动读 `_lastMouse.Y`**，该值只在 motion 分支赋值；纯点击（或触摸首帧）时用 (0,0) → 跳页错乱 | `InkQuestScreen.cs:193`（字段 `:41`） |
| 阻断 | 系统页把**窗口模式 / 垂直同步**当设置项给用户选，手机上无意义且改了会崩观感 | `InkSettings.cs:102-131`、`InkSystemScreen.cs:264-273`、`:290-293` |
| 退化 | **滚轮是唯一的快速滚动**（±3 行），触摸只剩 9px 宽的滑条条；滑块可拖（press+move 有效）但命中区 9×28 | `InkHubScreen.cs:670-677`、`InkLayout.cs:141`、`InkHubModel.cs:568-582` |
| 退化 | 悬停浅填是**唯一的即时反馈**（无 tooltip、无 hover-gated 控件，这点是好消息）；触摸下没有「移入即预览」，按下态需补 | `InkHubScreen.cs:659-665`、`:2459` |
| 退化 | Esc 是唯一关闭路径的界面（系统页/任务页虽有钮，但战斗态、弹窗推进靠键） | `InkHubScreen.cs:617`、`InkQuestScreen.cs:100`、`InkSystemScreen.cs:128` |
| OK | 长按推进对话（0.4s）本身就是触摸友好 | `InkHubScreen.cs:723-730`、`:349` |
| OK | 无拖放、无多点、无修饰键依赖；领地放置是「选一次 + 点一格」两拍 | `InkUiState.cs:160-163` |
| OK | 命中判定纯几何 + 注册逆序，无按层禁用 —— 与触摸不冲突 | `InkHubModel.cs:261-270` |

另：全项目**没有任何安全区/notch 代码**（`ScreenGetSafeArea` 零命中），版式 100% 是 1920×1080 常量
（`InkLayout.cs:14-15`，各屏在 `InkScreenRouter.cs:138-139` 等处硬设 Size）。顶栏地点名 y=26 → 手机上距物理上沿
1.7mm，圆角屏/notch 会切到。

---

## 5. 逐页适配判定

- **A 可直用**（只需放大 + 安全区）：标题画面、四类弹窗（800px 宽在手机上 51mm，正文重排即可）、战后结算弹窗。
- **B 需重排**（信息可全保留，版式必须改单栏/分页）：世界层、兴趣点、聊天层、场景演出、状态页、技能页、日程页、系统页、战斗日志页。
- **C 需重做**（同屏信息量超过手机一屏容量，须拆屏或改交互）：据点主界面（五面板 22 热区）、开发页（45 热区 + 20×20 拆除角 + 网格）、设施交互页（6 列小按钮 + 29 热区）、库存/交易页（双列清单 + 筛选签 100×36）、任务页（列表 + 详情 + 编成三段）、战斗界面（透视网格点击 + 210×44 类别签）。

---

## 6. 包体与运行时（已验证 `export/rimisekai.apk`，168 MB / 未压 245 MB）

| 项 | 实测 | 结论 |
|---|---|---|
| **图标** | APK 内 **0 个 `.svg`**；`InkIcon.LoadSvg` 先 `File.Exists(GlobalizePath(res://icons/x.svg))` 再 `Image.Load` | **手机端 61 个标准图标全空**。且 `export_presets.cfg` 的 `exclude_filter` 明写排除 `icons/*` —— 双重丢失 |
| 静态库 | `lib/**/*.a` **57 MB**（13 个） | 纯浪费，不该进包 |
| 开发元数据 | `assets/extension_api.json` 7 MB、`assets/RimisekaiCore/obj/*.json` | `include_filter="*.json"` 把整个仓库的 json 打进去了 |
| 字体 | `simsun.ttc` 18 MB + 导入后 `.fontdata` 11 MB = **28 MB** | 需子集化（只留实际用字 + 常用汉字表） |
| 图片 | 包内 34 MB；`assets/*.png` 14 张插画若常驻为 CPU RGBA8 ≈ **84 MB** | 中低端机内存风险 |
| 过滤 | 插画 `InkIllustration.cs:176-207` 用 `Image.LoadPngFromBuffer` + `CreateFromImage`，**不生成 mipmap**；`default_texture_filter=2` | 缩到 960×540 再缩到手机尺寸 → 闪烁/糊；图标 120→40px 已 3× 缩小，手机有效仅 17px |
| 已具备 | `gl_compatibility`（含 mobile 项）、`import_etc2_astc=true`、`arm64-v8a`、`sensor_landscape`、`immersive_mode` | 方向正确，无需重做 |

---

## 7. 三条路线

| 方案 | 内容 | 成本 | 结果 |
|---|---|---|---|
| **A 缩放补丁** | `stretch/aspect` 改 `expand` 或整体 scale ~1.9×，配分页翻 | 1–2 天 | 黑边没了，但热区仍 91% 不合规、宋体仍糊。**只能算「能演示」** |
| **B 双版式（推荐）** | 几何唯一来源已经是 `InkLayout*.cs`（partial 一组）：加一个 mobile profile，同一批 helper 按 profile 出矩形；字号与线宽改成 mm 反推常量；C 类页拆成单栏多屏 + 底部导航；文本输入改走 `LineEdit`/`DisplayServer.KeyboardShow`；接安全区 | 估 12–18 天 | 桌面基线**零影响**（default profile 数值不变），手机端可达标 |
| **C 手机优先重设计** | 以手机为第一版面，桌面当扩展档 | 30+ 天 | 最干净，但会动主人已定稿的边框/分区/白名单语汇，风险最高 |

B 案落地前必须先修的**与版式无关的阻断项**（1–2 天，收益最大）：
1. 图标进包（`icons/*` 从 `exclude_filter` 移出）+ `LoadSvg` 改成走 Godot 导入资源而非 `File.Exists`；
2. 文本输入改原生（改名/命名/搜索三处）；
3. 任务页右键与 `_lastMouse` 两处；
4. 系统页在移动端隐藏窗口模式/VSync；
5. 滑条命中区按 profile 加宽（≥120 画布 px 等效）。

**要主人点头才能动的授权域**（本分析未动任何 UI）：`InkLayout*.cs`（新增 profile 常量）、
`InkStyle.cs`（字号/线宽的 mm 反推档）、`InkDraw`/`InkFrame`（profile 化线宽）、各 Renderer 的字号字面量、
`InkHubScreen` 输入分支、`export_presets.cfg`。另需把 `tools/check_layout_isolation.py` 与
`LayoutContainerIsolationTests` 扩到**同时校验两套 profile 的 0 交叠**。

---

## 8. 顺手发现（只报告，未动手）

1. **制作页进不去**：`InkPage.Craft` 枚举还在、`InkHubModel.Build` 能正常构建出 25 个热区（`craft` 场景已验证），
   但 `DebugOpenPage` 的 switch（`InkHubScreen.cs:2060-2071`）没有 `craft` 分支，操作面板入口表
   （`InkHubModel.cs:1511`）是 `{Stock, Trade, Quest, Develop}` —— 玩家没有路径到达制作页。AGENTS.md 的页面清单仍列它。
2. `InkAction.PageScroll` 有分派（`InkHubScreen.cs:1338`）但无处注册 → 死代码。
3. 任务页 `_lastMouse` 问题（§4）即使不上手机也是隐患：桌面首次点击未产生 motion 时同样读 0。
4. `nul` 文件（67 字节）与上百张临时核对 PNG 散在仓库根，导出时靠 `exclude_filter` 挡 —— 建议归档。

## 9. 竖屏方案评估（2026-10-02 追加，主人提出）

**结论：竖屏是这套界面的正确方向，且不比横屏贵 —— 两者都要重排，竖屏反而多给 19% 的合规容量。**
但有一个前提：竖屏必须先立**竖屏基准画布**，把现有 1920×1080 直接转竖是灾难。

### 9-1 直接旋转 = 更糟

设备 1080×2400 竖持、画布仍 1920×1080 ⇒ 缩放 **0.562×**、黑边 **74.7%**、26px 字 → **0.94mm / 9.3 弧分**、
UI 物理仅 70×39mm。比横屏（20% 黑边、1.67mm）还差一档。

### 9-2 竖屏基准画布候选（黑边占比实测）

| 基准 | 1080×2400 (20:9) | 1179×2556 iPhone | 1080×2280 | 720×1520 低端 | 1080×1920 16:9 | 1640×2360 iPad 竖 |
|---|---|---|---|---|---|---|
| **1080×2340（1:2.17）** | **2.5%** | 0.1% | 2.6% | 2.6% | 17.9% | 33.6% |
| 1080×2160（1:2.00） | 10.0% | 7.7% | 5.3% | 5.3% | 11.1% | 28.0% |
| 1080×1920（1:1.78） | 20.0% | 18.0% | 15.8% | 15.8% | 0.0% | 19.1% |

⇒ **取 1080×2340**：全部手机档黑边 ≤2.6%，只有 16:9 老机型吃 18%。
**iPad 竖屏任何 1:2 基准都要 28-34% 黑边 → 平板与桌面继续走横屏/现版式**；
profile 判定不能只看 `ScreenGetOrientation()`，要按「短边 mm」阈值分档（<95mm 走竖屏 profile）。

### 9-3 同一块 1080×2400 屏的容量对比（示意图 `_ui_mobile/portrait_vs_landscape.png`）

| | 横屏（画布 1920×1080） | 竖屏（基准 1080×2340） |
|---|---|---|
| 48dp 网格 | 16×9 = **144 格** | 9×19 = **171 格**（+19%） |
| 60dp 网格 | 13×7 = 91 格 | 7×15 = **105 格** |
| 单手可达 | 屏物理 155×70mm，拇指只够右侧约 70mm → **实为双手** | 底部 80mm = 1242px = **52% 屏高、10 行**，主操作全放这里 |
| 软键盘遮挡 | 屏高 55-60% → 输入框无处放 | 屏高 40% → 上半屏仍留 1400px 放输入与列表 |
| 领地 5×5 网格 | 格 180×96 = 6.2×3.3mm（不合规） | 格 216×216 = **13.9mm 方格**（合规且更好读） |

实测各页热区数（据点 22 / 开发页 45 / 交易 41 / 技能 41 / 日程 37 / 设施页 29）：竖屏 171 格仍容得下最重的开发页，
但 9 列宽度意味着**双列对照版面（交易双列、设施页 6 列）必须改成纵向堆叠 + 滚动**。

### 9-4 素材朝向：一半白捡，一半要付

- **角色立绘 948×1659 = 0.57:1（9:16 竖构图）→ 竖屏天然合身，零成本**；`portrait_frame_overlay.png` 同理。
  聊天层/场景演出（立绘 + 名牌 + 量表 + 正文）本来就是竖构图主角，**这套界面最吃香的一块正好对上竖屏**。
- **场景/战斗/地形插画 13 张全是 1659×948 = 1.75:1**：竖屏 1080 宽铺满后高仅 617px（屏高 26%、物理 40mm），
  读作「顶部一条横带」。两条路，**要主人定**：
  ① 接受插画带（0 成本，但「插画铺满框体」的观感降级）；
  ② 重出 3:4 或 9:16 版 13 张（走现成的 `gen_scene_batch.py` / anima 管线，估 1-2 天含挑选）。

### 9-5 竖屏的三条真实代价

1. **失去「五面板同屏总览」**：AGENTS.md 的分区容器与容器隔离铁律都是围绕「同屏共存的面板」写的；
   竖屏一屏一事 ⇒ 导航层级加深一层（底部页签带），`check_layout_isolation.py` 的可检面收窄，
   跨屏一致性（同一控件在不同屏的位置记忆）要靠页签带承担。
2. **战斗界面白名单版面要重排**：现「4×4 敌阵透视网格 + 中上跑条 + 右下操作面板 + 中间按钮」是按 16:9 横向舞台定的，
   竖屏下敌阵纵深透视会被压扁。这是主人点名过的界面，**动它需要明确授权**。
3. **顶栏在竖屏是最差位置**：竖屏 notch 在上方，1080×2340 基准的顶部 26px 状态行（1.7mm）会被圆角/挖孔切，
   必须接安全区，并把状态行从「顶部」挪到「页签带上方」或做成可折叠。

### 9-6 建议

**手机端做竖屏 profile，桌面/iPad 保持现横屏基线不动**；施工仍是 §7 的 B 案（双版式），只是移动 profile 的基准从
「1920×1080 的局部」换成「1080×2340 全新版面」。顺序建议：先修 §7 那 5 个与版式无关的阻断项（含图标进包、原生输入），
再定 §9-4 的插画岔口，然后按「据点 → 角色三页 → 列表三页 → 开发页 → 战斗」逐屏出竖屏版面。

## 10. 容器隔离测试结果（2026-10-02 跑，含检测器失真复核）

结果截图：`_ui_mobile/shot_isolation_report.png`（全文）、`_ui_mobile/shot_isolation_map.png`（真实矩形重叠示意）；
原始输出：`_ui_mobile/isolation_py.txt`、`isolation_verify.txt`，脚本 `isolation_verify.py`。

### 10-1 官方两道检测：全绿

| 检测 | 结果 |
|---|---|
| `tools/check_layout_isolation.py` | 6 组全通过，退出码 0 |
| `dotnet test --filter LayoutContainerIsolationTests` | 已通过! 失败 0 / 通过 7 / 总计 7（9 ms） |

### 10-2 但两道检测比的是硬编码快照，不是 `InkLayout`

快照字面量与编译后真实矩形（反射导出 116 个 `Rect2`）已有 **20 处漂移**（两个检测器各 10 处）：

| 组 | 漂移 |
|---|---|
| trade | `TradeIllustrationRect` 快照 819,142,283,504 ≠ 真实 **780,142,360,640**；`TradeCenterPanel` 快照 734,**670**,452,**332** ≠ 真实 734,**796**,452,**206** |
| status | `StatusVitals` 502,142,432,240 ≠ 真实 **78,142,666,396**；`StatusCombat`、`StatusAttributes` 同量级漂移；`AbilityPanel` **在 InkLayout 里根本不存在** |
| schedule | `ScheduleSlot0..3` **四个名字在 InkLayout 里都不存在**（它们是方法算出来的，不是静态矩形） |

⇒ **全绿不等于真界面不重叠**：把任何一个真实面板挪到与邻居交叠，这两道检测都不会报警。

### 10-3 用真实矩形重跑

- 官方 6 组：`hub / schedule / trade / skills / list` 真实值 **0 部分交叠 ✔**；
  `status` 组报 2 处交叠（`CharacterRail × StatusVitals/StatusCombat`）—— 但这是**分组本身错配**：
  `CharacterRail` 是 `InkLayoutSystem.cs:14` 里 `SystemSidebar` 的别名，属系统页，不与状态页同屏。
  真实状态页三列 `StatusLeftColumn / MiddleColumn / RightColumn` 0 交叠 ✔。
- 补测 13 屏（官方完全没覆盖的）：据点含顶栏、角色三页、系统页、任务页、开发页、设施交互页、交易、列表详情、
  聊天层、改名弹窗 —— **11 屏 0 部分交叠 ✔**。
- 报红的只有 **战斗界面**：`CombatVerticalTimeline × 敌阵` 重叠 130×478（62140 px²）、
  `CombatLogWindow × 敌阵` 重叠 360×310（111600 px²）。敌阵舞台矩形是
  **硬编码在 `InkHubModel.cs:322/380` 的 `new Rect2(48,150,1824,552)`，不在 `InkLayout` 里**，所以检测器根本取不到它。

### 10-4 口径缺口（要主人定，本次不动手）

1. 检测器只有一种判据（相交即违规），分不清**父子嵌套**与**同屏遮挡**：`FixturePanel × FixtureContent`、
   `RenamePanel × RenameField`、`FullPagePanel × FullListPanel` 都是正常嵌套，一旦被写进同一组就会误报。
   复核脚本已按「完全包含＝嵌套，不计违规」分类（本次 13 屏共 15 对嵌套）。
2. 顶栏状态行 `HeaderSlot*`（y=100..132）**整块落在 `LogPanel`（y=78..618）外框之内** —— 这是主人定的
   「状态行压在日志标题带」设计，几何上属"包含"，但语义上不是日志面板的内容区。检测器需要
   **允许覆盖白名单**才能表达这类设计，否则补测覆盖时必然误报。
3. 建议（等下令再做）：两道检测改成**从 `InkLayout` 反射取真实矩形**（本次的 `--rects=` 导出已可直接复用），
   补齐 13 屏分组、把敌阵矩形收进 `InkLayout`、加「嵌套 / 允许覆盖」两种口径。
   否则容器隔离铁律名义上有监控，实际无人看守。

## 11. 竖屏平行实现（第一版骨架，2026-10-03 上午）

> 本节是骨架阶段的记录；全部页面接入见 §12。

**新增文件（现有文件一个没改）**：

| 文件 | 作用 |
|---|---|
| `RimisekaiFrontend/src/Ink/Portrait/PortraitLayout.cs` | 竖屏版式唯一来源：基准 1080×2340，尺寸全部由毫米反推 |
| `RimisekaiFrontend/src/Ink/Portrait/PortraitFrame.cs` | 竖屏框线语汇（双线框／分区／按钮／标题＋渐隐线／列表行） |
| `RimisekaiFrontend/src/Ink/Portrait/PortraitHubScreen.cs` | 竖屏据点：顶栏＋内容区＋底部四页签，点击在**松开**时派发，列表**拖动**滚动 |
| `RimisekaiFrontend/src/Ink/Portrait/PortraitCapture.cs` + `.tscn` | SubViewport 离屏渲染 1080×2340 出图，并导出运行时注册的热区 JSONL |
| `tools/check_portrait_isolation.py` | 竖屏硬性巡检（与 `check_layout_isolation.py` 平行） |

**版式常量**：`TouchMin=118`(7.6mm/48dp)、`TouchComfort=148`、`FontTitle/Body/Meta=60/50/44`、
`LineBold/LineHair=8/5`(0.52/0.32mm)、`Pad=40`、`RowHeight=118`、领地格 `216px`＝13.9mm 方格。
数据与文案一律走 `InkViewModel`（顶栏状态行、日志行、房间、设施、背包条目），颜色与字体一律取自 `InkStyle`。

**已打通**：地图页（插画带＋5×5 网格＋设施行）、日志页（拖动滚动＋滑条）、角色页（名册行）、
操作页 → 库存列表 / 开发列表（含返回）。
**未接**：交易、任务、日程、技能、战斗、四类弹窗、改名、系统页；文本输入（软键盘）；安全区；角花。

**出图与检测**：

```bash
# 竖屏逐屏出图 + 导出热区
Godot_v4.7-stable_mono_win64_console.exe --path . \
  res://RimisekaiFrontend/src/Ink/Portrait/PortraitCapture.tscn \
  -- --pcap=D:/123/rimisekai/_ui_mobile/port_real --pdump=D:/123/rimisekai/_ui_mobile/portrait_widgets.jsonl
python tools/check_portrait_isolation.py
```

结果：6 屏 **0 部分交叠、0 越界、全部热区 ≥48dp、字号与线宽不低于毫米下限**；
图在 `_ui_mobile/port_real_*.png`（1080×2340 真尺寸）与 `shot_portrait_real_sheet.png`。
检测器的数据源是**运行时真注册块**，不是抄写的常量——正是 §10 那两道横屏检测器失守的地方。

**桌面回归**：横屏据点页重新出图与本次改动前的基线**逐像素一致**（字节差 0），
`tools/check_layout_isolation.py` 仍 6 组全通过。

**一处规则张力，要主人定**：AGENTS.md 规定「装饰画法只准写在 `InkDraw.cs` / `InkFrame.cs`」，
但 `InkFrame` 的线宽是 1px／2px 硬编码，手机上只有 0.064～0.13mm，读不出线——所以竖屏另写了 `PortraitFrame`
（笔画仍走 `InkDraw.Ink`，颜色仍取 `InkStyle`，只是线宽按毫米给）。两条路选一：
① 给 `InkFrame.Panel/Button/Zone` 加线宽参数（默认值取现值，桌面零影响），竖屏回头复用同一套画法与角花；
② 承认 `Portrait/` 是独立画法层，把它的边界写进 AGENTS.md。
在主人定之前，竖屏**不带角花**，也不去动 `InkFrame` 一行。

---

## 12. 手机版（竖屏全接入，2026-10-03）

竖屏那一套从「一屏骨架」扩到**全部页面接入**，并且能出包。全部代码在 `RimisekaiFrontend/src/Ink/Portrait/`，
**横屏的 `InkHubScreen` / `InkLayout` / `InkPageBuilder` 一行没改**，只被调用。

### 12-1 文件与职责

| 文件 | 职责 |
|---|---|
| `PortraitRoot.cs` | 手机版根画面：调 `InkContentProvider.Install()` 装内容目录（2026-10-03 前是借 `InkRoot` 的静态构造，横版入口层删除后改直调）→ GameFlow → 标题/据点/战斗相位切换；含 `PortraitTitleView`（标题）与 `PortraitModalLayer`（通用模态） |
| `PortraitLayout.cs` | 竖屏坐标唯一来源（1080×2340，毫米反推常量） |
| `PortraitFrame.cs` | 竖屏框线语汇（双线框／按钮／标题＋渐隐线／列表行） |
| `PortraitHubScreen.cs` | 顶栏＋四页签（地图／日志／角色／操作）、领地 5×5、设施行、拖动滚动、松开派发 |
| `PortraitHubPages.cs` | 推入式页面：库存／交易／开发／日程／状态／技能／任务编成／系统（设置·存档·读档）＋设施交互页 |
| `PortraitCombatView.cs` | 竖屏战斗：两列名单＋技能行＋撤退，`Battle.CanAct/Act/StepTurn` 驱动，结算走 `CombatSettlement.Settle` |
| `PortraitCapture.cs` + `.tscn` | SubViewport 离屏 1080×2340 逐屏出图＋热区导出 |
| `Portrait.tscn` | 手机版主场景 |

**数据与文案单一来源**：列表页一律调横屏那套 `InkPageBuilder.Build` / `InkCharacterPageBuilder.Build`
拿同一个 `InkPageModel`，竖屏只改排版；顶栏状态、日志、设施、房间、背包全部走 `InkViewModel` / `HubSession`。
没有第二套文案，也没有自造字。

### 12-2 触摸取向的四处结构性改动

1. **点击在松开时派发**（横屏是按下即派发），按下后先判拖动——避免误触；
2. **列表拖动滚动**（没有滚轮），滑条走 118px 宽；
3. **文本输入交给原生 `LineEdit`**（横屏是逐键 `InputEventKey.Unicode`，软键盘中文收不到字）；
4. **安全区**：`DisplayServer.GetDisplaySafeArea()` 换算成画布像素，顶栏整体下移。

### 12-3 巡检与出图

`tools/check_portrait_isolation.py` 读的是**运行时真注册块**（不是抄写常量），四条判据：
同屏块 0 部分交叠（完全包含算嵌套放行）、可点块短边 ≥48dp、不越出 1080×2340、字号线宽不低于毫米下限。

```
Godot…_console.exe --path . res://RimisekaiFrontend/src/Ink/Portrait/PortraitCapture.tscn \
  -- --pcap=D:/123/rimisekai/_ui_mobile/phone --pdump=D:/123/rimisekai/_ui_mobile/portrait_widgets.jsonl
python tools/check_portrait_isolation.py
```

结果：**14 屏全 ✔**（标题／四页签／七个入口页／设施交互／战斗），0 交叠、0 越界、热区全部 ≥48dp。
图：`_ui_mobile/phone_*.png`（1080×2340 真尺寸）与总览 `shot_phone_all.png`。

> 这套判据当场抓出我新代码里 3 个真实几何缺陷（底部动作钮压进页签带 8px、开发页动作钮与返回行叠 24px、
> 设施页放入/取出两钮互叠 24px），已修。**这就是检测器必须读运行时数据而不是抄常量的理由。**

### 12-4 工程配置（2026-10-02 的做法，已于 2026-10-03 被 §13 推翻）

当时只在 `project.godot` 加两行 feature 覆盖，桌面值原样保留：

```ini
run/main_scene:android="res://RimisekaiFrontend/src/Ink/Portrait/Portrait.tscn"
window/handheld/orientation:android="sensor_portrait"
```

⇒ 当时的结论是「Android 竖屏手机版，Windows/编辑器仍是横屏 `Main.tscn`，互不影响」。
**这条结论已作废**：主人 2026-10-03 定全盘转竖版，主场景与 `[display]` 基准都落成了竖版，
横版入口层已删除归档，详见 §13。包：`export/rimisekai_phone.apk`。

### 12-5 已知未接（诚实清单）

- **敌人来源**：桌面版「委托→战斗」的敌人表是 `InkScreenRouter.BuildGoblinEncounter()` 私有的占位哥布林。
  **2026-10-03 起那份占位表已随 `InkScreenRouter.cs` 一起归档**（在
  `D:/123/rimisekai_backups/deleted_landscape_2026-10-03/`），竖屏从来不调它，所以现在盘上
  **没有任何假敌人表**——要打通委托战斗，敌人来源必须由主人先定（内容表还是运行时生成）。
- **战斗跑条与特效**：竖屏战斗是名单式，没有横屏的实时跑条／斩击特效（那是白名单版面，未授权重做）。
- **聊天层／场景演出／技能盘**：竖屏暂未接（角色页与技能页走列表模型，星盘是横屏专属版面）。
- **角花**：竖屏只有双线框没有四角角花——`InkFrame` 的角花与 1/2px 线宽绑死，见 §11 末尾那条待决。
- **弹窗剧情演出／战后结算的排版美化**：竖屏模态是通用排版（标题＋正文＋选项＋输入），
  结算大字号美化版式未搬。

---


## 13. 全盘转竖版落地（2026-10-03 主人定，横版入口层已删除）

主人下令：备份横版布局文件并打包 → 删除横版渲染层 → 工程基准与 `AGENTS.md` 全转竖版。
执行与实测结果：

**13-1 备份（仓库外 `D:/123/rimisekai_backups/`）**

| 包 | 内容 | 自检 |
|---|---|---|
| `landscape_ui_2026-10-03_0816.zip` | 横版 UI 层 115 个文件（`Ink/` 根 55 个 `.cs` ＋ `.uid` ＋ `Main.tscn` ＋ `tools/check_layout_isolation.py` ＋ 改动前的 `project.godot`/`AGENTS.md`）＋ sha1 `MANIFEST.txt` | 115/115 校验和相符；夹具断言 `Portrait/` 与 `.git/` 未混入 |
| `deleted_landscape_2026-10-03/` | 本轮真正删掉的 14 个文件，按原相对路径存放 ＋ `_manifest.txt` | 可原路还原 |
| `portrait_wip_2026-10-03_0846.zip` | 竖版工作区 26 个文件只读快照（竖版文件全是 git 未跟踪，出事的唯一兜底） | sha1 全符 |

**13-2 删除的极限在哪：编译器实测的依赖闭包**

在仓库外的沙箱副本里把所有横版文件移出，用 `dotnet build` 当 oracle 按缺失符号精确放回，10 轮收敛后实测：
**竖版传递依赖原 55 个横版 `Ink/` 根文件中的 50 个**（`InkDraw` 单独移出引发 794 条错、`InkFrame` 262 条、
`InkCombatRenderer` 32 条；`PortraitCombatView` 直接使 `InkCombatRenderer`/`InkCharRenderer`，
`PortraitHubPages` 直接使 `InkPageRenderer`/`InkPageBuilder`/`InkCharacterPageBuilder`）。
所以「删横版渲染层」只能删到**入口与屏幕层**，已删 14 个文件：

`Main.tscn`、`InkRoot.cs`、`InkScreenRouter.cs`、`InkTitleScreen.cs`、`InkQuestScreen.cs`、
`InkTransition.cs`、`Tools/InkCapture.cs`、`Tools/Capture.tscn`（含各自 `.uid`）。
`MobileHitAudit`、`ModalProbe`、`RealtimeCaptureProbe`、`SectorZoomProbe`、`InkChessCapture`
这些竖版还在用的探针**全部保留**。

**13-3 唯一动过的竖版文件**：`InkRoot` 静态构造里那 25 行内容 provider 抽成
`InkContentProvider.Install()`，`PortraitRoot._Ready` 第一行的
`RunClassConstructor(typeof(InkRoot))` 改为直接 `InkContentProvider.Install()`（1 行代码＋2 行注释）。

**13-4 `project.godot` 现状**：`run/main_scene = res://…/Portrait/Portrait.tscn`（`.android` 覆盖行因冗余而删）；
`[display] viewport 1080×2340`、stretch 仍 canvas_items + keep、`handheld/orientation = portrait`（`.android` 重复行删）。
上一轮为解决 F5 临时加的 `PortraitF5` 编辑器插件已退役（`addons/` 移除，文件在 `portrait_f5_retired/`）——
基准落盘后不再需要内存换靶。

**13-5 闸门数字**：`Rimisekai.sln` 0 错误 / 7 警告（6 条 xUnit 分析器 ＋ `InkPageBuilder.cs:112` CS8602，均在测试与既有代码）；
`RimisekaiFrontend.sln`（Godot F5 用的那条）0 错误；`dotnet test` **360/360**；headless 跑竖版主场景 0 错误行。

**13-6 遗留（等主人定，未动手）**

- 竖版技能盘仍从 `InkLayoutSkill.cs` 取几何（`PolygonBounds` ＋ 8 个 `SkillDisc*/TransformDisc*`），
  那文件内部仍是 1920×1080 基准；`AGENTS.md` 已标为「新增竖版版式不得再从它取数」。
- 彻底解耦那 50 个共享文件＝把竖版对 `InkDraw`/`InkPageRenderer`/`InkCombatRenderer` 等的用量重写进
  `Portrait/` 侧，是大重构且必须改竖版会话正在写的文件。
- 图标盲审导出器（`InkCapture.ExportBlindIcons()`）随 `InkCapture.cs` 一起归档，命令已断；
  重建方案两条写在 skill 的 §7，等下令。
- 流程教训（已并进 `rimisekai-ink-ui` skill §3）：在活树上跑 `-t:Rebuild` 与 `--no-incremental` 同罪，
  会清空共享 `.godot/mono/temp/bin/Debug/*.dll` 让并行会话起不来；要全量重建就去沙箱副本。

---

## 附：审计工具（本次新增，dev-only，不改任何界面）

- `RimisekaiFrontend/src/Ink/Portrait/` 与 `tools/check_portrait_isolation.py`：见 §11。

- `RimisekaiFrontend/src/Tools/MobileHitAudit.cs` + `.tscn`：headless 跑 15 个屏态，直接调
  `InkHubModel.Build(vm, ui)` 把**实际注册的每个热区**（action/index/enabled/rect/label）导成 JSONL。
  只读，不渲染，不改任何生产代码路径。
  `Godot…_console.exe --headless --path . res://RimisekaiFrontend/src/Tools/MobileHitAudit.tscn -- --audit=D:/123/rimisekai/_ui_mobile/hits.jsonl`
- `_ui_mobile/analyze.py`：设备档案换算、字号视角、热区合规统计、设备合成图与标注图（`python analyze.py` 可重跑）。
- `_ui_mobile/portrait_model.py` + `portrait_diagram.py`：竖屏基准画布黑边矩阵、48dp 容量、拇指区/键盘遮挡、
  插画带占比，输出 `portrait.txt` 与 `portrait_vs_landscape.png`。
- `MobileHitAudit.tscn -- --rects=<路径>`：反射导出 `InkLayout` 全部 116 个静态 `Rect2`（字段 + 表达式体属性），
  供隔离复核取真实矩形。
- `_ui_mobile/isolation_verify.py`：解析两道官方检测器里的硬编码快照 → 与真实矩形逐名比对（漂移表）→
  用真实矩形重跑 AABB（区分「完全包含＝嵌套」与「部分交叠＝违规」）→ 补测 13 屏。输出 `isolation_verify.txt`。
- `_ui_mobile/isolation_shots.py`：把结果出成 `shot_isolation_report.png` 与 `shot_isolation_map.png`。
