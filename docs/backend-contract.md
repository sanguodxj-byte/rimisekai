# Rimisekai 后端接口契约（给 UI agent）

后端只改数据、不建任何控件。UI 只读视图结构、调返回 `bool` 的操作，
`false` = 非法操作（直接按失败处理或禁用按钮即可）。

程序集：单一 `RimisekaiFrontend`（Godot 工程程序集），源码全在 `RimisekaiFrontend/src/`；纯逻辑模块（Hub、Character、Combat、Defs 等）不引用 Godot API，`Flow` 等是流程节点。

## 1. 启动与阶段

`Rimisekai.Flow.GameFlow`（Godot Node）：

- `State: GameState`，`Phase: FlowPhase`（Title / Hub / Quest / Combat）
- 信号 `PhaseChanged(int phase)`，具体画面挂它
- `NewGame()`：空白开局，建主角后进 Hub
- `Start(GameState state)`：带数据开局（读档/内容包入口）
- `CloseDay()`：只做日期结算。主界面日终请走 `HubSession.CloseDay`（会写日志、跑事件），不要调这个

## 2. 主界面：`Rimisekai.Hub.HubSession`

构造：`new HubSession(state)`。读屏与操作如下。

### 2.1 顶栏

`Header() -> HubHeader(Place, Season, Weather, Hour, Minute, Money)`

- 地点 `Place` 就是领地名（`Territory.Name`，空则「领地」），**不拼区域名、不拼房名**。
  区域名（`Territory.RegionName`）只出现在「去往XX」这类文案里，3×3 拼图扁平编号：
  0 中心区 / 1 北区 / 2 东区 / 3 南区 / 4 西区 / 5 西北区 / 6 东北区 / 7 西南区 / 8 东南区
- 季节 `Season`（Spring/Summer/Autumn/Winter，15 天一换），天气 `Weather`（Clear/Cloud/Rain/Snow/HeavyRain/Thunder/Wind/HeavySnow/Blizzard），时刻 24 小时，金钱 `long`

### 2.2 地图

- `Map() -> IReadOnlyList<Room>`：只返回当前 `RegionId` 的房间。`Room` 字段：`Id, Name, RegionId, X, Y, Open, OpenCost, Vacant, Links(List<int>), Permission`
- `Open == false` 即"未开拓"；`Links` 是双向通路
- `SelectRegion(id)` 只认 `Territory.IsRegionUnlocked(id)`；`Enter(roomId)` 进已开放房间（读档/开局定位用，无返回值）
- `Move(roomId) -> bool`：必须通路相连且目标开放；**被遮盖层盖住时（`MapCovered`）一定失败**
- `Develop(roomId) -> bool`：花钱开拓（扣 `OpenCost`），成功写日志
- **未开发格分两种**（2026-10-01）：① 有房间实体但 `Open == false`；② **空格子**（压根没有房间实体）。
  起始区域 5×5 里 25 格只有 5 间房，剩下 20 格全是第 ② 种。开拓第 ② 种走
  `DevelopVacantCell(regionId, x, y)`，按**越开越贵**定价（见 §4 的 `Vacant*` 常量），
  开出来是 `Name = "空房"`、`Vacant = true` 的毛坯。相邻判定用 `Territory.NearOpenAt(regionId, x, y)`。
- **安装**：`PlaceRoom(roomId, vacantRoomId)` —— 已建好的房间**只能装进空房**（`Vacant == true`），
  不能往裸格子上放；装进去时空房本体被顶替掉。`Room.Vacant` 随存档走。
- **过界**（2026-10-01）：`CrossTargetRegion(roomId) -> int` 看人站的这间房能不能去隔壁，
  能则返回目标区域 Id，否则 -1。三条同时成立才通：① 这间房在本区的**连接点**格上
  （`Territory.RegionGate(dir)` 那一格）；② 对面区域已解锁；③ **对面那块地图的对应连接点上也有房**。
  `CrossTo(regionId) -> bool` 落地：扣移动时间，`Enter` 到对面连接点房，写日志。

### 2.3 此处

- `Here() -> FixtureView(Id, Name, SeatsLeft, Occupants, PlayerHere)`：当前房间的设施
- `Use(fixtureId) -> bool`：坐下/使用，满员失败；`UsingFixtureId` 为玩家当前所用
- `GuestsHere() -> GuestCard(Id, Name, Purpose)`：同房客人

### 2.4 角色栏

- `Party() -> CharacterCard(Id, Name, IsPlayer, RoomId)`：`RoomId == -1` 表示位置未知（全名册）
- `CardsHere()`：左下角头像——主角不可选，只显示同房的其他角色；`Select` 拒绝主角与不在场者。**角色自己走开（推进时间时）或玩家走开，选中都会自动取消**（`DropSelectionIfGone`），右下角从“交流”退回“行动”，因此不会出现“交流 · XX（不在场）”
- `Select(id) -> bool`：选中交流对象；`SelectedCharacterId`
- 角色立绘/翻页后端不管，UI 自己画

### 2.5 交流 `Social(action, giftItemId = "") -> bool`

`SocialAction`：Talk / Observe / Gift / Touch / PatHead / BodyContact / Hug / Kiss / Invite。
要求对象与玩家同房，否则失败。有台词的角色说台词，没有的走内置文案（见 2.11）。

- Talk：好感 ≤ Hostile 者直接不理；冷漠/高傲者第一次不接话（只写日志）；成功涨社交经验、好感 +5、**双向自动相识**，并**自动弹出对话遮盖层**
- Observe：只写日志
- Gift：背包必须有该物品（扣 1），好感 +20
- Touch：只写日志
- PatHead / BodyContact / Hug / Kiss：要好感分别 ≥100 / 300 / 500 / 800，不够则**整次失败**（不扣时间）
- Invite：邀请同行。`AcceptsInvite()` 为真时答应（女仆无视好感直接答应，其余人要 Bond ≥ Fond）

### 2.6 行动 `Act(action, minutes = 0) -> bool`

行动分两级：**房间级**（没坐设施）与**设施级**（坐在某件设施上）。

房间级 `PlaceAction`：只有 Observe。`PlayerRoomId < 0` 时失败。

- Observe：房间内唯一的行动，推进 2 格时间并写日志。日常行动一律要坐到对应设施上

设施级 `ActAtFixture(action, minutes = 0) -> bool`：必须已经 `Use` 了某件设施，
且该设施支持这个行动，否则返回 false。行动种类 `FacilityAction`：
Meal / Sleep / Rest / Bathe / Drink / Read / Pray / Train / Meditate / Watch /
Stargaze / Lookout / Trade / Store / Tend / Pass / Leisure / View。

- `ActionsAtCurrentFixture() -> IReadOnlyList<FacilityAction>`：玩家当前所坐设施支持的行动，界面据此列按钮
- `ActionsInCurrentRoom() -> IReadOnlyList<FacilityAction>`：当前房间的行动并集
- 睡觉只认带“床”字的设施、吃饭要到能坐的设施上——**由内容包在设施的 `actions` 里声明**，不按用途推导
- **吃饭必须有食物**：库存里没有 `Foods` 表中的任一种就吃不成，`ActAtFixture` 返回 false 且不推进时间

### 2.7 时间与委派

- `PassTime(minutes) -> List<WorkLog>`：按 5 分钟一格推进时钟，委派按当前槽位结算，返回产出日志
- `Day: TerritoryClock`：可读每个委派角色状态 `Worker(CharacterId, Phase, RoomId, FacilityId, Task, Progress, Path)`，`Phase` 为 Idle / Moving / Working
- `ScheduleOf(characterId) -> Schedule`：读某人日程；`Assign(characterId, slot, assignment)` 写日程
- 槽位共 4 个，`slot = Clock.Slot`：0=0:00 / 1=6:00 / 2=12:00 / 3=18:00
- `Assignment`：`Task, WorkplaceId（-1=自动找空闲同类设施）, Fallback（做不成时的后备）, OrderItemId（制作订单）`
- `WorkTask` 数值：Free=0, Rest=1, Gather=3, Mine=4, Craft=9, Woodwork=13, Smithing=14, Alchemy=15, Cooking=16, Tailoring=17
- `WorkLog`：`CharacterId, Slot, Task, ItemId, Count, Skill, Exp, Fallback`
- 太累（疲劳≥150）或心情 ≤10 拒绝工作；特质不拒活：懒散 / 怕痛干重活（采掘、锻造）进度 -30% / -25%，且每 4 小时心情各 -1；设施按 `Capacity` 先到先得，排不上走 `Fallback`

### 2.7b 自主节律（闲时）

`Day: TerritoryClock` 上可读 `Worker(CharacterId, Phase, RoomId, FacilityId, Task, Progress, Path, Goal, WaitTicks, WantsChat)`。

- 优先级：睡眠（深夜或累垮，打断一切）> 委派工作 > 三餐 > 找玩家聊天 > 娱乐 > 闲时活动
- 这些挂点会先说口上：找人/放弃/三餐/入睡/醒来都走 `VoiceTrigger`（见 2.11），没台词才用内置文案
- 睡眠：22 点睡、6 点起（懒散 8 点起，好奇 23 点睡）；睡觉先找有 `Sleep` 行动的设施（床）；**真找不到空床，就在最近的进得去的室内（带「室内」标签）打地铺**（女仆先挑主人的房间），连室内都进不去才不睡；露天不睡；醒了回委派（2026-10-10 主人定）
- 三餐：7/12/18 点前后窗口；**要到声明了 `Meal` 的可坐设施上吃**，优先与桌子同房；同房无桌扣心情 3；没座位跳过本餐
- 找玩家：聊天欲满阈值就动身（默认 100，恋爱 70）；欲望按羁绊（好感+1/亲密+2/恋爱+3）加好奇心涨；进不去（未开放/上锁/无路）就在原地等；**只要和玩家同房就显示"XXX似乎想对你说什么"（每间房一次，跟进换房会再显示）**；玩家多人家具（容量>1）会一起坐；等不到人就放弃（好感亲密 48 格、恋爱 72 格，否则 36 格），到了才放弃会再写"XXX好像打消了念头。"
- **闲时活动 `WorkGoal.Loiter`（无委派时的日常）**：不再原地发呆，而是从三种里掷骰挑一件，各持续 6-18 格后重挑——
  `Sitting` 找可坐设施歇着 / `Wandering` 去别的房间转悠 / `Chores` 留在房里做零活。
  懒散者更常坐、好奇者更常走动；**玩家同房时不出门**（免得搭话时人已溜走）。`Worker.Loiter` 读当前种类
- 娱乐：心情低于 -20 或好奇性格时去别的公共房间逛，心情 +10；超过 2 天没娱乐每天 -5；到了会待一会儿再走
- 心情 `Affect.Mood`（0~100，50 为中值，新角色默认 50，每 4 小时向 50 漂移 1 点）
- 回心情只认三件事：有品级的食物（精致 +2 / 丰盛 +4 / 绝味 +8，普通 0）、喜欢的人交谈 +3 / 接触 +5、凯旋（任务/委托/地下城完成，参战者 +8）；普通吃饭、独睡、送礼、娱乐都不回
- 睡觉时心情向 50 移动 20%（入睡时结算一次）
- 扣心情：挤房 -15、睡地铺（每夜 -10，压心情 3 天，连睡叠加至多 -30：起床按「比昨天多压的份」掉心情，3 天内心情回归值跟着压低 `Affect.Baseline`，过期那一夜的份退掉、心情随回归慢慢回来，`Affect.SleptOnFloor`）、错过一餐 -8、过劳（疲劳≥100 干活）-1/格、体力低于 30% -1/格、厌恶的人同房 4 小时 -12、两天没娱乐每天 -5、找人失败 -5
- 起居（2026-10-10 主人定）：开局卧室那张床是主人的（`starting_area.json` 的 `masterBed`，之后跟着主人睡下的床走，`Territory.MasterBedId`）；主人的床、主人此刻躺着的床，别人好感不够同床（`Intimacy.SharesBed`：800 × 特质门槛 − 心情修正，与 `CoSleep` 同档）就不睡，另找空床，没有就打地铺。女仆与主人同屋不算挤房（不扣 -15），同床照样看好感；主人往睡着人的床上钻、对方好感不够，被踢下床（`HubSession.UseRefusal`，界面弹提示签，不进日志）。除女仆外，人一睡下就反锁所在房间（`Territory.SleeperLocks`）：醒着的人（主人也一样）请到隔壁进得去的房间（无处可去就留在原地），门外的进不来，醒了门开；只有肯与主人同床的人锁门不拦主人；会锁门的人不睡主人的房间（除非肯同床），女仆有空床先回主人的房间睡。门锁对主人一视同仁（2026-10-10 主人定，`Territory.BarsEntry`）。主人能拧锁的只有自己的房间（摆着主人的床那间，`Territory.MasterBedroomId`；房间抽屉里「拆除」位换成门锁钮，三档循环）：自动＝主人不在或睡下时锁、只放女仆；手动锁＝主人在屋里时谁都进不来、女仆也不例外，主人一出门回到自动；手动敞开＝谁都进得来。别的房间没有主人的锁，饭桌、库房永远够得着。水井只存水
- 心情是工作效率乘数：≥80 为 1.25、≥65 为 1.1、≥35 为 1.0、≥20 为 0.8、其余 0.6；≤10 拒绝工作
- 娱乐按"需求"触发：超过 1 天没娱乐，闲时就去逛（只刷新日期，不回心情）
- 好感四档：None（0+）/ Fond 好感（100）/ Close 亲密（300）/ Lover 恋爱（600）；聊天欲涨速、找人阈值、等待时长都按档走
- 主界面位置表每轮按工人实际位置同步，角色栏直接读就行
- 不传上下文调 `Step` 则自主关闭（旧行为，用于测试）

### 2.7c 玩家行动消耗（格，1 格=5 分钟，移动是 baseline）

每次行动都先推进时间再结算（世界同步活）：移动 1 / 交谈 6 / 观察 2 / 赠物 1 / 接触 2 / 摸头 2 / 身体接触 4 / 拥抱 4 / 亲吻 6 / 休息按分钟（默认 60）/ 交易 6 小时 / 开拓 1 / 用设施 1。`Act` 只剩休息（起身/环顾/日程已删，离席走移动即可）。进房/摆人/选中/写日志不花时间。验证不过的不扣时间，尝试失败（如躲开）照扣。

亲密链按好感解锁：接触（入口，无要求）→ 摸头（100）→ 身体接触（300）→ 拥抱（500）→ 亲吻（600，恋爱档）。行动不限次数，只限奖励次数：摸头/身体接触每日 3 次，拥抱/亲吻每日 1 次；超次行动照做但无奖励。奖励：好感 +5/+8/+12/+15，心情（仅喜欢的人）+4/+6/+8/+10。不够就躲开并写日志，时间照扣。

好感 -1000~1000：憎恨（-600）/ 敌对（-300）/ 嫌恶（-100）/ 无（-100~100）/ 好感（100）/ 亲密（300）/ 恋爱（600）。嫌恶交谈难度 +1（首次不接话），敌对/憎恨拒绝交谈（不涨好感、不弹对话）。

制作与建造按工作台分表 `StationTime`（格）：烹饪 6 / 通用 6 / 木工 12 / 裁缝 12 / 锻造 24 / 炼金 24，其余 12。`Craft` 按配方的工作台查表；`BuildFacility(id)` 花钱加查表，可重复调（建好的返回 false）。
- 行动绑定：交流绑定选中角色（同房）；休息必须坐在支持 `Rest` 的设施上，否则拒绝。设施行动种类新增 `View`（查看），`Free` 默认支持它——每个设施至少绑定一个行动。

### 2.8 库存 / 交易 / 制作 / 开拓

**物品只存在于两处：角色背包（`CharacterState.Bag`）或某件设施的存货（`Facility.Contents`）。没有领地级虚空库存。**

- 哪些设施能存货由内容包声明（设施的 `storage: true`）。能存的设施自带一份 `Contents`；`Territory.Storages` 列出全部
- `CountWith(who, itemId) -> int`：据点所有设施存货 + 该角色背包的总数
- `CanPayWith(who, costs) / PayWith(who, costs)`：据点级操作（建造/开拓/制作）用它——先扣角色背包，不够再从各设施存货补
- `StoreOrGive(who, roomId, itemId, count)`：NPC 采集/制作的产出优先进同房仓储，没有仓储则进产出者背包
- `FindFoodIn(who, roomId) / ConsumeFood(who, roomId)`：吃东西必须取到实物，背包优先、其次该房间设施存货
- `Stock() -> IReadOnlyDictionary<string, int>`：**玩家背包**快照（买卖、送礼都走背包）
- **买卖只在城镇商店里做（2026-10-10 主人定）**：没有坐在领地里点一下就让货物凭空进出的远程交易。人得走大地图进一座村、镇或王都，再走进聚落里带 `Territory.CityShopTag`（「商店」）标签的那一间——`AtCityShop` 为真才能 `MarketTrade` / `MarketTradeCombined`；货从玩家背包出、落进玩家背包，路程按大地图逐格耗时，成交本身不耗时。行情页在领地里照看（步进与成交压暗）。设施、房间不在城里卖。商店由 `poi_defs.json` 里聚落分区的 `shopFacilityId`（`facility_defs.json` 中 `shop: true` 的「商店」）放在一格临街的格子上，每座村/镇/王都恰有一间
- `MarketTrade(itemId, count, selling) -> bool`：在城镇商店里按当日行情买卖；报价 `MarketOffer(ItemId, BuyPrice, SellPrice, SellOnly, Stock)`。**行情每日 0 点重掷**（`Territory.RollMarketDay`）：每种 ThingDef 物品有存货（按价值分档，0=今日无货买不了但仍可卖）与价格系数（70-130）。**库存即价格**：买入压库存、卖出抬库存——买入价 = 基准 × 系数% × (100−库存×4，下限 60)%；卖出价 = 基准 × 60% × 系数% × (100−库存×5，下限 70)%（集市存货越多，玩家卖价越低）。武器为**运行时独特实例**：集市每日随机锻 3-6 件（材料/品质只取前三种，附魔低概率，不强化不祝福），在售武器买走即下架；玩家卖武器 = 价值 × 60% × 当日武器系数（`WeaponPricePercent`，70-130），卖掉即上架可被买回。设施无库存概念，直报价。显式报价表（`Territory.Market`）优先级最高
- `Craft(itemId) -> bool`：材料从背包+设施存货扣，成品进背包；主角涨对应生活经验
- 配方 `Recipe`：`ItemId, Station(工作台种类，对应 WorkTask), OutputCount, Skill, Costs[]`

### 2.9 地图遮盖层（对话/剧情/插画）

与地图区等大、完全遮盖；`MapCovered == true` 时 `Move` 被禁。

- `Overlay: MapOverlay?`，`Show / CloseOverlay()`
- `AdvanceOverlay() -> bool`：推进一句，返回是否还有内容（`false`=已关）
- `Choose(choiceId) -> bool`：分支选项；选项 `OverlayChoice(Id, Label)`，用 `Offer(...)` 挂上
- `MapOverlay.Dialogue(speaker, lines)` / `.Story(lines)` / `.Illustration(id, caption)`
- 插画层播完不自动关，UI 负责调 `CloseOverlay()`
- 交谈成功后弹的是内容包写好的台词；没写台词的角色才退回占位对话 `……`（见 2.11）

### 2.11 口上 / 地文（角色在适当时机说适当的话）

台词是数据，不进存档；进存档的只有"谁说过什么"。没有台词文件时整条链路自动旁路，
所有挂点退回原来的内置文案，游戏照常跑。

- `State.Voice: VoiceDirector`：调度。`Register(角色名, VoicePack)` 挂台词库，`World` 是通用包（旁白）
- 挂点时机 `VoiceTrigger`：Meet 进房招呼 / Talk / Observe / Gift / Touch / PatHead / BodyContact / Hug / Kiss /
  TalkRefused 不接话 / Invited / InviteRefused / BondUp 好感升档 / Seek 想找你说话 / SeekGaveUp 打消念头 /
  Meal / Sleep / Wake / DayEnd / Idle 闲时氛围
- `Say(character, trigger, overlay, giftItemId) -> bool`：让某人在此时机开口。挑不出返回 `false`，调用方走默认文案
- `SayWorld(trigger) -> bool`：说一句世界旁白，走 `World` 包
- 每句话 `VoiceLine`：`Id, Speaker, Kind(Speech/Narration), Trigger, Lines[], Weight, Gate, IllustrationId`
- `VoiceGate` 门槛（字段全可选，不写即不限制）：`FavorMin/Max`、`BondMin/Max`、`Require/ForbidRelations`、
  `DaysSinceTalkMin`、`MoodMin/Max`、`Require/ForbidTraits`、`HourMin/Max`（下限大于上限表示跨夜）、
  `Season`、`Weather`、`DayMin/Max`、`GiftItemId`、`Chance`、`Once`、`CooldownMinutes`、
  `RequireSaid` / `ForbidSaid`（必须先/后说过哪些 Id）
- 挑选用 `Weight` 加权随机；`Chance` 是百分比掷骰；命中即记账，因此同一步内不会把一次性台词说两遍
- 记忆 `CharacterState.Voice: VoiceMemory`：`SaidAt`（台词 Id→时刻）、`LastSpokeAt`（时机→时刻），随存档走
- 闲时氛围同一角色两次之间至少隔 `HubSession.AmbientGapMinutes`（默认 60 分钟）
- 内容文件格式见 `content/voice.json`，解析器 `VoicePackJson.TryParse`（纯逻辑，可脱离 Godot 测）

#### 2.11a 场景维度（同一时机，不同处境说不同的话）

- 活动 `VoiceActivity`：Idle / Moving / Working / Cooking / Mining / Farming / Crafting / Training /
  Resting / Eating / Sleeping / Seeking / Following / Playing。由 `HubSession.ActivityOf` 从角色当前状态推导，
  **干活的角色也有挂点**（边采矿边回一句）
- 角色 `VoiceRole`：Actor 动手方 / Partner 承受方 / Observer 旁观
- 阶段 `VoicePlace`：Before / Middle / After（事前事中事后）
- 情绪 `VoiceEmotion`：Any / Neutral / Happy / Excited / Sad / Lonely / Angry / Afraid / Tired / Shy / Aroused；
  `VoiceContext.EmotionOf` 从心情与疲劳推导
- 写在哪都行：`VoiceLine` 上直接写 `Activities/Roles/Places/Emotion`，
  或写进 `Gate` 的 `Activities/ForbidActivities/Roles/Places/Emotions/SameRoomAsPlayer/AtFacility/RoomIds`。两处都会判
- `CutNarration`：说完切掉同动作的通用地文（对应 eraFL 的 cutDescription）

#### 2.11b 优先级与排他

- `VoiceLine.Priority`（默认 1）：跨级准入门槛，**低于 1 视为关掉这句**
- `VoiceLine.Exclusive`：为真且通过门槛时，把所有优先级更低的候选本轮清零。
  用来表达"这个情境下必须说这句"；排他句自己门槛不过则不压别人
- `Weight` 与 `Priority` 分工：Weight 是同级之间的相对频率，Priority 是跨级准入

#### 2.11c 场景事件（多步剧情，对应 eraFL 的 DAILY）

一句台词是"说一句就走"，场景事件是"演一段"。整条做成数据，不需要写代码。

- **全存档终身仅演一次**：一旦触发开演即记入 `State.FiredEvents`（存档原生持久化集合），已演过绝不重复演出
- **触发点唯一且情境语义明确**：严禁在 `PassTime` 或进房 `Move`/`Enter` 挂全局抽签；必须在特定生命周期业务挂点（如 `CoSleep` 双人同床共寝、`EventBoard` 重大事件板）所有严苛条件同时满足时精确触发
- **严苛门槛（无极度宽松）**：`Gate` 必须精确表达情境，支持 `RoomTags`（房间标签匹配）、`LevelMin`（角色等级门槛）、`AllMembersMaxLevel`（全队满级100级）、`ReturnedFromCombat`（从战斗/副本胜利归来状态）、`Soaked`（淋湿）、`Weather/Season`、`Bond/Favor`，缺一不可
- **演出态互斥**：场景开演或对话进行时，右下角面板切换为**演出态**（展示剧本标题或发言人，隐藏交谈/接触/离开等社交按钮，点击面板推进对话），绝不与日常交互打架
- `State.Voice.Scenes: SceneLibrary` 持有事件表；`SceneRunner(library, territory)` 负责执行
- `SceneRunner.Begin(character, ctx) -> SceneRun?`：挑一个可触发的事件开演；挑不出返回 null
- `SceneRun`：`NextLine()` 推进一句、`Advance(run, ctx)` 推进一步、`Choose(run, id, ctx)` 选分支、
  `Choices` 当前可选、`Lines()` 当前步骤文本
- `SceneEvent` 字段：`Id, Title, Genre, Rate`（千分率，1000=必发）、`Characters`、
  `Gate`、`Flag/RequireFlagValue/DoneValue`（状态机）、`CooldownDays`、`Effects`、`Steps`、`Summary`
- `SceneStep`：`Id, Lines, Choices, Effects, Gate`（步骤自身可带门槛，不过则跳过）
- `SceneChoice`：`Id, Label, GotoStep`（-1=继续往下）、`Effects, Gate`
- `SceneEffectKind`：Favor / Mood / Stamina / Spirit / Fatigue / GiveItem / TakeItem /
  SetFlag / ClearFlag / SetCounter / GrantTrait / RemoveTrait / AddRelation / RemoveRelation / Log
- 状态机：`Begin` 时立刻记 `SceneLastDay` 冷却；`Flag` 留到跑完才置位，
  因此中途中断的事件下次还能接着讲
- 触发判定顺序：FiredEvents 终身去重 → 角色匹配 → Gate 全条件通过 → 状态机 → 冷却 → 掷骰

#### 2.11d LLM 生成层

静态台词库与 LLM 生成走同一条挑选与呈现链路，区别只在"正文从哪来"。
作者可以给同一时机写静态兜底，再挂一句 `Generation` 让模型接管，门槛与优先级完全一致。

- `State.Voice.Generation: VoiceGenerationHub`；`Generator` 为空则**整层旁路**
- 接入方式：实现 `IVoiceGenerator.GenerateAsync(VoiceRequest) -> IReadOnlyList<string>`
  （可远端 API / 本地模型 / 测试假件）。出错返回空列表即可，实现方不应抛异常
- `VoiceLine.Generation: VoiceGeneration`：`Instruction`（此刻状况与要说什么）、`Style`（语气）、
  `LineCount`、`MaxCharsPerLine`、`UseMemory`、`UseRecentDialogue`、`CacheMinutes`（默认 30）、`Options`（透传采样参数）
- 同步 `Speak()` 只取静态台词；异步 `SpeakAsync()` 允许挑到生成句。生成失败整句作废，不回退静态文本
- `VoiceRequest` 里引擎已自动填好 `Situation`（世界与关系状态的人话摘要）、`Memory`、`RecentDialogue`，
  实现方直接拼 prompt 即可
- 记忆 `VoiceMemory.Memories` 容量 35（同 eraFL 的 KOJO_MEMORY_SIZE）、`RecentDialogue` 容量 12，满了丢最旧
- 生成结果按 `CacheKey`（角色+时机+场景维度+好感/心情归并）缓存，连点同一个按钮只烧一次 token
- `Personas`：内容包里按角色名写固定人设，生成时随请求发出

### 2.10 日志与日终

- `Log: IReadOnlyList<LogLine>`，上限 40 条，超了丢最旧；`Write(text)` 可追加
- **日志是快照不是流水**：每次玩家操作（`BeginOperation`）清空重写，一次推进只留当前状态
- **角色行为一人一行**：`WriteActivity(characterId, text)` 同角色重复调用只替换旧行，
  因此推进时间不会把一个角色的过程堆成好几行。主界面每步据此显示“谁在做什么”
- `CloseDay(random?) -> DaySummary(SeasonChanged, Season, Weather)`：全员回满体力气力、清疲劳、雇佣天数+1、任务冷却、作物生长、日终事件；季节变化与当日天气自动写日志。**主界面日终调这个**

## 3. 角色：`Rimisekai.Character.CharacterState`

### 3.1 核心与生活

- 核心 6 项 `CoreStat`：Constitution 体质 / Dexterity 灵巧 / Intellect 智力 / Charm 魅力 / Perception 感知 / Strength 力量；`c[stat]` 读写
- 生活 8 项 `LifeSkill`：Cooking 烹饪 / Social 社交 / Mining 采掘 / Farming 种植 / Husbandry 驯兽 / Craft 手工 / Research 研究 / Performance 表演
- 对应核心：烹饪→体质，社交/驯兽/表演→魅力，采掘→力量，种植→感知，手工→灵巧，研究→智力
- 有效值 `Life(skill) = 对应核心 + 经验/100`
- 经验分成：生活经验 50% 进对应核心经验、200% 进等级经验 `LevelExp`；核心经验满 100 自动+1 点核心
- 角色等级 1–100（`XpTable`，累计 10×(n-1)²）：升级时六项核心均匀各 +1；血量 = 20 + 体质×10 + 等级×5；蓝量 = 10 + 智力×5 + 等级×3（`Vitals.Mana/MaxMana`，休息/睡眠/日终恢复）

### 3.2 战斗面板（现算，不存）

`Combat -> CombatSheet(Attack, MaxHp, Defence, Dodge, SpellPower, Threat)`：

- 攻击 = 力量 + 灵巧/2；血量 = 20 + 体质×10；防御 = 体质 + 力量/2
- 闪避 = 灵巧 + 感知/2；法术 = 智力 + 感知/2；威胁等级单独存 `Threat`

### 3.3 武器与流派

- 武器 8 种 `WeaponType`：Sword 剑 / Axe 斧 / Spear 矛 / Bow 弓 / Staff 杖 / Dagger 短剑 / Crossbow 弩 / Unarmed 格斗
- 流派 7 种 `StyleType`：OneHand 单手 / TwoHand 双手 / DualWield 双持 / Ranged 远射 / Spell 法术 / Shield 持盾 / Unarmed 格斗
- 装备：`Equip(main, off?, offShield?) -> bool`，**不许只装副手**；风格由配置推导 `DeriveStyle`：
  只主手→单手（弓弩远射、杖法术、徒手格斗）；主副手同武器→双手；不同→双持；副手盾→持盾；空手→无风格
- `ResolveStrike(panel 或 WeaponDef) -> StrikeResult(Weapon, Style, WeaponLevel, Addend, Multiplier, Total)`，`Rounded` 为取整总量：
  加数 = 面板 + 5×熟练 + 2×主属性 + 1×副属性；
  乘数 = 1 + 0.05×主属性 + 0.2×熟练 + 流派系数
- 流派系数：单手 1、双手 1.5、双持 0.6、远射 0.8、法术 0.2、持盾 0.6+0.1×体质、格斗 0.5+0.05×(力量+体质+灵巧)
- 流派主副属性：单手灵巧/力量、双手力量/体质、双持灵巧/感知、远射感知/灵巧、法术智力/感知、持盾体质/力量、格斗力量/体质
- 熟练 `Proficiency`：100 经验=1 级；武器 200% 进熟练和等级经验，流派 500% 进流派和等级经验、100% 进对应核心经验
- 命中：`WeaponHit(type, baseHit)`，每级熟练 +1%

### 3.4 素质、体力、关系

- 素质 `Trait`（有/无）：学习快慢改经验 ±50%；懒散 / 怕痛干重活变慢且扣心情（不拒干）；冷漠/高傲首次交谈失败；好奇/坦率降低交谈难度；恢复快慢影响疲劳
- 女仆 `Trait.Maid`：`RequiresWage()` 为假（不要工资），`AcceptsInvite()` 无视好感恒为真
- 体力 `Condition`：体力/气力上限 1000；`Spend/Recover/RecoverFull`；疲劳≥150 停工；好感分档 None/Fond(100)/Close(300)/Lover(600)，交谈+5、送礼+20
- 关系 `Relations`：`Acquainted 相识 / Trusted 信任 / Sworn 誓约 / Rival 敌对 / Marked 刻印`，双向存 ID 对

## 4. 领地：`Rimisekai.Housing.Territory`

- 常量：每区 5×5（`RegionSize`）、领地最多 3×3 个区域（`MaxTerritoryRegions = 9`）、房间上限 100；
  采集每步 +10、制作 +15，满 100 出货；采集/制作经验 3
- **3×3 拼图**：RegionId 是扁平编号，位置固定
  （0 中心 / 1 北 / 2 东 / 3 南 / 4 西 / 5 西北 / 6 东北 / 7 西南 / 8 东南，见 `RegionCellOf`）。
  解锁状态存在位掩码 `UnlockedRegionMask` 上（随存档走），用 `IsRegionUnlocked(id)` 查询。
  **两段式解锁**：中心区铺满（`IsRegionFull`，25 格都有房间，不看房里有没有设施）→ 开四正
  （`OrthogonalMask`）；四正里任意一块铺满 → 开四角（`DiagonalMask`）。
  `TryUnlockByFill() -> List<int>` 返回本次新开的区域；`UnlockRegion()` 是手动入口，
  只推进到下一档、不做铺满判定。POI 区域从 `MaxTerritoryRegions` 起顺延（`SetUnlockedRegions`）。
- **连接点**：每区四边正中一格（`RegionGate(dir)`：北 (2,0) / 东 (4,2) / 南 (2,4) / 西 (0,2)）。
  `RegionNeighbor(regionId, dir)` / `Opposite(dir)` / `RegionName(id)` 是拼图几何与文案。
  过界判定见 §2.2。
- `AddRoom` 要求 `IsRegionUnlocked(room.RegionId)`
- `OpenRoom / Build` 花钱（`OpenCost / BuildCost`），设施建成后按 `EffectId` 累房间效果，`Effect(id)` 查询
- 设施 `Facility`：`Id, Name, RoomId, Usage, Capacity, YieldItemId, Skill, Built, BuildCost, EffectId`
- 客人 `Guest(Id, Name, RoomId, Purpose)`；`AddGuest / RemoveGuest`
- 旧的整槽结算 `ResolveSlot(slot, roster, roll?)` 保留，日常推进请用 `HubSession.PassTime`

## 5. 时钟与世界效果

`Rimisekai.Clock.GameClock`：一天 1440 分钟，槽 360 分钟×4 段，15 天一季、全年 60 天；`Day, Minutes, Season, Week, Slot`；`Advance / SkipToNextDay / SetTime`。

`WorldEffects`：

- 天气是**按小时步进的马尔可夫演化**（`Advance(current, season, rng)`，由 `PassTime` 每跨整点调用）：每种天气有持续概率（晴约一天半、雷雨约七小时），转变只在相邻天气间走；状态空间按季隔离——冬季不下雨，非冬季不落雪。变天写日志"天气转为X。"并触发 `HubEventTrigger.Weather`。不再有每日定时重掷
- `YieldPercent`：冬天采集/烹饪减半，雨天采矿 70%，雪天采集/采矿 70%，其余 100%

耕地（`Territory` 的耕作语义 + `CropDef`）：

- 设施 `YieldItemId` 命中某 `CropDef.ProduceItemId` 即为**耕地**；其余采集设施（矿脉、果树等）保持即时抽取
- 耕作（Till）在耕地上= 播种（消耗背包/仓储里的 `SeedItemId` 一份）→ 当季每天 `SettleDay` 生长 +1 → 长满 `GrowthDays` 收获（产量沿用采集公式并套 `YieldPercent`）
- 非当季**暂停不枯**：播不了种、也不生长；换季时没长熟的留着，回来接着长
- 状态判定 `PlotState`（ReadySow/ReadyHarvest/Growing/OutOfSeason/NoSeed/NotPlot）供玩家操作前置检查与 NPC 选活过滤共用；NPC 耕完一下若地里无事可做就回决策层重挑，不会站田头空转

## 6. 存档：`Rimisekai.Save.SaveSystem`

- `Save(state, hub?) -> json`；`Load(json) -> GameState`，主界面快照在 `SaveData.Hub`，用 `new HubSession(state).Restore(data.Hub)` 恢复
- 存：时钟、金钱、声望、天气、名册（含**各自背包**）、领地（含房间/设施（含**各自存货**）/配方/日程/客人/报价）、任务通关与冷却、主界面位置与日志。旧档的领地虚空库存读入时并入玩家背包
- **不存**：任务定义（读档后重新 `Register`）、目录数据、`DayEvents` 钩子（代码注册）

## 6b. 数据表：房间 / 设施 / 设施行动

- 房间表 `RoomDef`、设施表 `FacilityDef` 放目录（`GameCatalog.Rooms/Facilities`，读档后内容侧重注册，说明文字只放表里）
- 内容包 `world.json` 的房间/设施条目已加字段（区域、权限、用途、建造、效果、产出、技能、说明，全可选，老文件照读）；组装时先注册进目录再生成运行时对象
- 设施行动**由内容包按设施声明**：`content/*.json` 里每件设施写 `actions: ["Sleep"]` 之类。
  设施与行动解耦——同为 `Rest` 用途的“床”只声明 Sleep、“沙发”声明 Rest+Meal，行为因此不同。
  不写 `actions` 才退回按用途查兜底表（`Territory.Actions`，默认厨房管饭、床管睡）；写空数组表示刻意不可交互（桌类）。
  桌类另标 `isTable: true`：自身无行动，但在有桌的房间吃饭不扣心情（无桌 -3）
- NPC 找饭/找床优先查设施自己的行动集；睡觉只认声明了 Sleep 的设施（带床字的），吃饭只认声明了 Meal 的可坐设施，且优先与桌子同房
- 内容分两层：`content/world.json` 是静态表（领地名/食物定义/5 房/5 通路/7 设施），`content/newgame.json` 是正常种子（玩家+女仆），`content/newgame_hard.json` 是困难种子（只有玩家）；开场日志不配表，按出生房间生成。读档世界重进时种子只补台词
- 开局 5 房：庭院（水井/躺椅/篝火）、客厅（沙发）、卧室（床）、森林（草药丛）、山岳（矿脉）；起始木材×10、石材×10 在玩家背包，干粮×5 放在篝火里（NPC 够得着才有饭吃）
- 开发（`HubSession`）：`AddRoomCopy/AddFacilityCopy`（照抄**可建造**类型，花材料）、`MoveRoom/MoveFacility`（空格子）、`SetRoomOpen`（开花钱/关免费）、`SetLink`（加拆通路）、`RemoveRoom/RemoveFacility`（材料按 60% 向下取整返还，拆房先挪人、级联拆设施、清通路；玩家在里面不给拆）。每次开发走 1 格时间；定义与运行时都有 `MaterialCost` + `Buildable`，存档保留
- 开拓/安装（2026-10-01）：`DevelopEmptyRoom(roomId)`（已有实体、`Open == false`，花 `OpenCost` ＋ `MaterialCost`）、`DevelopVacantCell(x, y)`（空格子，越开越贵，生成 `Vacant` 空房）、`PlaceRoom(roomId, vacantRoomId)`（已建房间**只能装进空房**，空房被顶替）。`Room.Vacant` 随存档走
- 建房与摆放（2026-10-10 主人定）：每类房间建成白送一件对口设施（`buildings.json` 房间行的 `bundledFacility`，如铁匠铺→铁砧、木工房→工作台、客卧→床、菜园→卷心菜田），**占房里 4 个设施位中的一个**，与自己建的一样能拆。设施的 `roomTag` 管摆放：家具（床、椅、桌、箱、柜、架、吧台、浴池等）只能摆在带「室内」标签的房间，田地、圈舍、资源点、井、营火、篝火、瞭望台、箭靶、操练场等只能建在「室外」；工作台、炉灶、窑、锯台这类留空，哪儿都能摆。建造页里不合的那行灰着，行尾写「只能摆在室内 / 只能建在室外」；不合规矩不扣料。房间行的 `bonusActions` 是对口工作：在这间房里干这些活进度 +20%（`Territory.RoomBonusPercent`；主人亲手干按耗时折短）。开局的躺椅挪到了卧室（庭院是室外）。铁砧 石材30＋铁矿20，缝纫台只要木材30
- 可建造名单：`Territory.BuildableRooms/BuildableFacilities`（按名去重，开发菜单直接读）。当前房间仅客厅/卧室，设施除草药丛/矿脉外全可建；地形与野外资源不可建，删了也不返材料
- 设施用途一览：水井 Gather、躺椅/床/沙发 Rest、晾衣绳 Tailoring、灶/餐桌 Cooking、工作台 Woodwork、草药 Gather、矿脉 Mine、神龛/货架 Free；`Territory.RoomActions(roomId)` 直接给某房能支撑的行动并集

## 7. 任务与战斗（骨架，UI 暂不要深入）

- `QuestRecord`：`Register / Find / IsAvailable / Start(id, party) / Complete`；任务分 Map / GraphicalMap / Dialogue；节点 `MapNode(Id, Links, EventIds)`，`MoveTo` 要求通路相连
- `Battle`：双方各半、上限 12 人，按闪避排序行动，`Act(action, resolveDamage)`，一方全灭结束
- 通用指令会话 `Session`：`Register / Available / Submit -> Rejected/Executed/ExitSession`

## 8. UI 需要内容包填的东西（后端不管）

房间名、设施名、物品名、配方、报价、敌人、台词、插画 ID、日终事件文本（实现 `IDayEvent` 后注册到 `State.DayEvents`）。

台词走 `content/voice.json`（见 2.11）；开局种子（`newgame.json` / `newgame_hard.json`）的角色项可写 `favor` 与 `traits`
（如 `"traits": ["Maid"]`）设初始好感与素质，写 `voicePath` 指向台词文件；`stock` 设**玩家背包**初始物资；设施的 `contents` 设该设施开局存货，`storage` 声明它能不能存货。
