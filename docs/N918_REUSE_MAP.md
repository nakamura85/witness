# 九一八事变关 · 复用骨架与接缝报告

> 只读分析产物。仓库：`/c/Users/ChangFee/WorkBuddy/2026-11-11-09-59-11/witness`
> 目标：新增轻量「九一八」关，作为 `JiangqiaoLevel.cs` 的兄弟版，复用美术/玩法管线，并把大地图流向改为
> 「大地图 → 九一八 → 江桥」，两关均加入「进屋找无线电发信机才能提交通关」机制。
> 所有行号引用基于本次只读快照（Godot 4.7.2 / C#）。

---

## 0. 一句话关键结论（先读）

⚠️ **最重要勘误**：`JiangqiaoLevel` **并不继承** `scripts/Level.cs`。
`scripts/JiangqiaoLevel.cs:124` 声明为 `public partial class JiangqiaoLevel : Node2D`，
而 `scripts/Level.cs:13` 的 `Level` 是「南京 1937」关，**也是独立的 `Node2D`**。
二者是**并列的两套关**，仅共用一批「helper 类」（FieldPlayer / EnemySoldier / WeaponPickup /
Testimony / EnterableHouse / Hud / VitalsBar / AmmoBar / IntroOverlay / LevelEndUi /
FieldNoteUi / Backdrop / SkyScreen / SmokeFx / TracerFx / SnowScreen 等），
但江桥把它自己的**全部美术管线（DrawFar/Mid/Near/Ground/Fore、Backdrop、SkyScreen、GroundArt）作为私有嵌套类写死在文件内**。

因此「九一八」最干净的落地方式是：**新建独立 `N918Level.cs`，从 `JiangqiaoLevel.cs` 复制后改文案/坐标/数据，而不是塞进现有 254KB 文件**（见第 6 节风险点）。

---

## 1. JiangqiaoLevel.cs 结构地图

文件：`scripts/JiangqiaoLevel.cs`（5269 行，单文件塞了关卡 + 全部美术嵌套类）

### 1.1 类声明与关键字段

| 行号 | 内容 | 说明 |
|---|---|---|
| 124 | `public partial class JiangqiaoLevel : Node2D` | **注意：基类是 Node2D，不是 Level.cs** |
| 126 | `private readonly Main _main;` | 主程序引用（仅结算/返回地图时用到） |
| 128–131 | `WorldW=5200f` / `GroundY=655f` | 世界宽度、地面线 |
| 149–167 | `_player / _game / _hud / _vitals / _ammoBar / _cam / _enemies / _testimonies / _pickups / _houses / _houseUi / _grenades` | 运行时核心集合 |
| 158 | `Backdrop[] _layers;` | 视差四层（Far/Mid/Near/Fore/Ground） |
| 159 | `SkyScreen _sky;` | 天空/昼夜屏 |
| 176–180 | `_smokeMid / _fireFarIn / _fireFarOut / _fireNearIn / _fireNearOut` | 烟雾/火焰分层 |
| 181 | `const float MidFactor = 0.24f` | 中景视差系数 |
| 196–213 | `PersonH / WallH / BridgeH` | 人物/房屋/桥的人物尺度体系（美术基准） |
| 233 | `_checkpoint` | 出生/复活点（默认 `(220, GroundY-2)`） |
| 235–244 | `SunriseH / SunsetH / ClockRate / _clock / DebugClock` | 昼夜系统（真实天文数据） |
| 255 | `public JiangqiaoLevel(Main main)` | 构造：仅存 `_main`，**不读存档** |

### 1.2 `_Ready()` 流程（`scripts/JiangqiaoLevel.cs:262`）

顺序构建（262–273）：
1. `BuildSky()` (264 → 305) — 天空层
2. `BuildBackdrop()` (265 → 830) — **视差四层**
3. `BuildMidSmoke()` (266 → 742) — 远景炊烟
4. `BuildTerrain()` (267 → 855) — 地形/桥/铁轨/河冰
5. `BuildHouses()` (268 → 319) — **房屋实例化（含可进屋）**
6. `BuildPlayer()` (269 → 990) — **玩家生成 + 相机**
7. `BuildPickups()` (270 → 798) — 武器/弹药掉落
8. `BuildTestimonies()` (271 → 1040) — **证言点布置**
9. `BuildHud()` (272 → 1074) — HUD/目标/血条
10. `BuildScreenFx()` (273 → 1095) — 闪屏/黑场/雪花
11. 之后挂 `IntroOverlay`（279）开场导语，回调里 `_started=true` 并显示教程（285–300）。

### 1.3 视差/背景初始化（Far/Mid/Near/Fore 四层）

- 常量定义：`scripts/JiangqiaoLevel.cs:2161` `public const int KindFar = 0, KindMid = 1, KindNear = 2, KindFore = 3, KindGround = 4;`
- 四层在此实例化：`scripts/JiangqiaoLevel.cs:830–844`
  ```csharp
  new Backdrop(this, Backdrop.KindFar, 0.06f, -40),
  new Backdrop(this, Backdrop.KindMid, MidFactor, -30),   // MidFactor=0.24f
  new Backdrop(this, Backdrop.KindNear, 0.55f, -20),
  new Backdrop(this, Backdrop.KindGround, 1f, -5),
  new Backdrop(this, Backdrop.KindFore, 1.32f, 20),
  ```
- `Backdrop` 是**私有嵌套类**（`scripts/JiangqiaoLevel.cs:2167` 起），各自 `_Process` 按 `factor` 跟随相机做视差；`DrawFar/DrawMid/DrawNear/DrawGroundBand/DrawFore` 在 2256/2427/2583/2617/2878 各自绘制。
- 烟雾/火分层在 849–891 创建（`_smokeMid`/四组 `_fire*`），Z 序见注释 88–93。

### 1.4 玩家生成与出生点

- `BuildPlayer()`：`scripts/JiangqiaoLevel.cs:990–1038`
- 出生点：`_player = new FieldPlayer { Position = new Vector2(230, GroundY - 2) };`（994）
- 缩放 `1.06f`（1000）；事件接线 `Fired/Hit/Down/DryFired/Landed`（1001–1008）
- 相机 `_cam`：`LimitLeft/Right = ±100000`（手动平滑，不用内置 smoothing，见 1025–1031），`MakeCurrent()`（1034）
- `__checkpoint` 初始 `(220, GroundY-2)`（233），记录证言时前移（1588）。

### 1.5 敌人 / 日军生成

- 集合：`internal readonly List<EnemySoldier> _enemies`（162）
- `SpawnEnemy(float x, float lane)`：`scripts/JiangqiaoLevel.cs:1712`
- `EnemyScaleForLane(float lane)`：`1705`
- 波次：`CheckWaves()`：`1651`；敌人投掷：`EnemyThrowGrenade()`：`1385`
- 受伤/死亡：`OnPlayerHit()` 1822、`OnPlayerDown()` 1854、`EnemyDied()` 1806、`EnemyFire()` 1868
- 注意：江桥的敌人/波次逻辑也是**文件内私有实现**，无独立 EnemyManager。

### 1.6 EnterableHouse 实例化 与 屋内 UI 接线

- 实例化入口：`AddHouse(...)`（`scripts/JiangqiaoLevel.cs:594–608`）
  ```csharp
  var h = new EnterableHouse(id, new Vector2(x, GroundY + 8f), tex, w, enterable, lift);
  _game.AddChild(h);
  if (enterable) _houses.Add(h);   // 仅 enterable 进 _houses 列表
  ```
- 可进屋列表：`internal readonly List<EnterableHouse> _houses`（165）；`BuildHouses()` 里 `enterable=true` 的有 h1/h2/h3（338–353）。
- 交互触发：`NearestTargets()`（1255）+ `UpdateInteractions()`（1286）→ 走到门前按 E 调 `OpenHouse()`。
- **屋内 UI 接线（核心）`OpenHouse(EnterableHouse h)`：`scripts/JiangqiaoLevel.cs:1436–1464`**
  ```csharp
  var ui = new HouseInteriorUi(h.Id, title);
  ui.CanHeal = () => _player.Wounded;
  ui.GotAmmo  += n => { int took = _player.AddAmmo(n); ... };   // 1446
  ui.GotGrenade+= n => _player.AddGrenade(n);                    // 1451
  ui.GotHeal  += n => { ... _player.Heal(n); ... };              // 1452
  ui.Closed   += () => _houseUi = null;
  AddChild(ui); _houseUi = ui;
  ```
  - 这 4 个事件/委托（`GotAmmo/GotGrenade/GotHeal/CanHeal/Closed`）**就是「屋内结果回灌关卡」的全部接缝**。
  - 同理，九一八若要加「发信机提交」，应在此新增 `ui.RadioSent += ...` / `ui.HasEnvelope = () => ...` 之类的接线（见第 2 节）。

### 1.7 证言点（EvidencePoint）布置与记录逻辑

- 江桥用的是自有的 `Testimony` 类（**私有嵌套类**，非南京的 `EvidencePoint`），`scripts/JiangqiaoLevel.cs:4976` 起。
- 布置：`BuildTestimonies()`（1040–1065）调 `AddTestimony(...)`（1067）放 t1/t2/t3 三份口述；集合 `_testimonies`（163）。
- 记录：`Record(Testimony t)`（1583–1598）
  ```csharp
  t.Recorded = true;
  GameState.I.S.JiangqiaoTestimony.Add(t.Id);   // 1586 落存档
  GameState.I.Save();
  _checkpoint = ...;                            // 1588 更新复活点
  // 弹 FieldNoteUi 显示口述
  ```
- 提示/目标：`UpdateObjective()`（1600，显示 `已记录 n/3`）、`UpdatePrompts()`（1607，按距离显示「按住 E 记录口述」）。
- 与南京 `EvidencePoint` 的区别：南京用 `Level.cs:371 AddEvidence()` + `Level.cs:437 FindEvidence()` + `EvidenceStatus` 状态机；江桥用简化版 `JiangqiaoTestimony` 计数。九一八建议**沿用江桥的简化 `Testimony` 模式**（不要引入南京那套难民/证据系统，用户也明确说九一八可简化）。

### 1.8 通关 / 胜利判定与返回大地图

- 判定：`CheckEnding()`（`scripts/JiangqiaoLevel.cs:1733–1756`）
  - 前置：玩家到达 `PlayerX < 4780f` 返回（1736）；`JiangqiaoTestimony.Count < 3` 则 Toast 提示 incomplete（1737–1745）。
  - 通关动作：
    ```csharp
    _ended = true;
    GameState.I.S.JiangqiaoDone = true;   // 1748 关键 flag
    GameState.I.Save();                    // 1749
    _player.ControlLocked = true;
    ...
    var end = new LevelEndUi(_main); AddChild(end);   // 1754
    ```
- 结算 UI：`LevelEndUi`（**私有嵌套类**，`scripts/JiangqiaoLevel.cs:5189` 起）
  - 「返回东亚地图」按钮：`map.Pressed += () => _main.ShowAsiaMap();`（5253）← **这就是返回大地图的出口**
  - 「重玩本关」：`GameState.I.S.JiangqiaoTestimony.Clear(); ... _main.StartJiangqiao();`（5259–5264）
- **九一八需要的改动**：`CheckEnding` 不应只看 `Testimony.Count`，还要加 `N918RadioSent`（见第 2/4 节）；`LevelEndUi` 按钮应 `ShowAsiaMap`（回地图后由地图解锁江桥入口，见第 3 节）。

### 1.9 与 `scripts/Level.cs` 的关系（重要澄清）

- `JiangqiaoLevel : Node2D`，**不继承 `Level`**。二者关系＝**并列关卡 + 共享 helper 类**。
- `Level.cs`（1334 行）是「南京 1937」关，内容：`Refugee` 系统（`Level.cs:95/222/380`）、`EvidencePoint` 系统（`Level.cs:94/207/371/437`）、`PuzzleLogic` 解谜（翻越/木箱/石块/蒸汽）、`GroundY(x)` 横版地形（`Level.cs:104` 等）。这些**江桥都没用**，江桥自带了一套更轻量的实现。
- 共享、可安全复用的 helper（不在 JiangqiaoLevel 内、独立文件）：`FieldPlayer` / `EnemySoldier` / `WeaponPickup` / `EnterableHouse`（含 `HouseInteriorUi`）/ `Hud` / `VitalsBar` / `AmmoBar` / `IntroOverlay` / `LevelEndUi`？不——`LevelEndUi` 是江桥私有嵌套，需复制。`FieldNoteUi` 独立文件（`OpenNote` 通用）。
- **结论**：九一八不该去继承 `Level.cs`（那会把南京难民/证据系统整套带进来，与「轻量」背道而驰）；也不该依赖 `JiangqiaoLevel` 的私有嵌套。最干净是**新建 `N918Level.cs`，复制江桥整文件后改**（详见第 6 节）。

---

## 2. EnterableHouse.cs 扩展点（加 radio / 最终提交）

文件：`scripts/EnterableHouse.cs`（597 行）

### 2.1 现有 HouseInteriorUi 的 spot 种类

- 种类定义（注释）：`scripts/EnterableHouse.cs:307`
  ```csharp
  public string Kind;  // ammo / grenade / letter / will / clue / gauze / medkit / chest / ash
  ```
- 实际生成：`BuildSpots()`（374–393）逐屋 RNG 摇表：
  - `ammo1`(ammo)、`letter1`(letter)、`will1`(will, 50%)、`grenade1`(grenade, 65%)、
    `gauze1`(gauze, 18%)、`medkit1`(medkit, 5%)、`chest1`(chest)、`clue1`(clue, 50%)、`ash1`(ash)。
- 行为分发：`OnSpot(Spot s)`（430–526）按 `s.Kind` 分支：`ammo/grenade/gauze/medkit` 走 `GotAmmo/GotGrenade/GotHeal` 事件；
  `letter/will/clue` 走 `OpenNote(...)`；`chest` 走 `_Process` 里的 `StartPry` 撬锁（528–572）；`ash` 仅 `MarkDone`。
- 持久化：`_looted = GameState.I.S.JiangqiaoLoot;`（**301，硬写死**）。`MarkDone()`（574–582）把 `s.SpotId` 加入 `_looted` 并 `GameState.I.Save()`。

### 2.2 在哪里加 "radio" spot 最干净

1. **构造函数解耦 loot 集合（必改）**：当前 `HouseInteriorUi` 在 301 行硬绑定 `JiangqiaoLoot`。
   改为由关卡注入，例如构造参数或属性：
   ```csharp
   // scripts/EnterableHouse.cs:295 附近
   public HouseInteriorUi(string houseId, string houseTitle, HashSet<string> loot,
                          Func<bool> hasEnvelope = null, Action onRadioSent = null)
   { _houseId = houseId; _houseTitle = houseTitle; _looted = loot;
     _hasEnvelope = hasEnvelope; _onRadioSent = onRadioSent; }
   ```
   江桥调用处（`scripts/JiangqiaoLevel.cs:1444`）改为 `new HouseInteriorUi(h.Id, title, GameState.I.S.JiangqiaoLoot)`；
   九一八改为传 `GameState.I.S.N918Loot`。
2. **加一个 radio 种类的 spot**（最干净的位置＝`BuildSpots()` 末尾，紧挨 `ash1`，`scripts/EnterableHouse.cs:392` 之后）：
   ```csharp
   AddSpot("radio1", "角落里的无线电发信机", "radio", "");
   ```
   并在 `OnSpot` 的 `switch`（433 起）增加 `case "radio":`：
   ```csharp
   case "radio":
       if (_hasEnvelope != null && !_hasEnvelope())
       { s.ResultText = "没有能发出去的信——机器空转，什么也传不出去。"; _hint.Text = s.ResultText; break; }
       _onRadioSent?.Invoke();          // 通关提交：由关卡决定后续
       s.ResultText = "你对着话筒，把信里的话一句句发了出去。电波穿过雪夜。";
       MarkDone(s);
       break;
   ```
3. 新增字段（类内 283–293 附近）：
   ```csharp
   private Func<bool> _hasEnvelope;   // 玩家是否持有信（信封）
   private Action _onRadioSent;       // 发报完成 → 关卡解锁通关
   ```

### 2.3 「玩家持有信后操作发信机提交 = 通关」如何挂接

推荐用**和现有 `GotAmmo/GotHeal` 完全一致的「事件/Func 委托」模式**，不要引入新机制：

- **持有信的信封状态**：谁负责置位？两种可选，均轻量：
  - 方案 A（推荐，最省）：信的 spot 仍是 `letter` 种类；在 `OnSpot` 的 `case "letter":`（`scripts/EnterableHouse.cs:489`）`MarkDone(s)` 之后，额外调一个 `_onGotLetter?.Invoke()` 让关卡把 `_hasLetter=true` 落到 `N918Loot`（或在 `WitnessState` 加 `N918HasLetter`）。
  - 方案 B：直接复用持久化——radio 的 `case` 里判断 `GameState.I.S.N918Loot.Contains($"{houseId}:letter1")`。但「信可能在另一间屋」时方案 A 更稳。
- **最终提交动作**：`HouseInteriorUi` 暴露 `Action onRadioSent`（或 `event Action RadioSent`），关卡在 `OpenHouse` 接线处（`scripts/JiangqiaoLevel.cs:1446` 同款位置）写：
  ```csharp
  ui.HasEnvelope = () => GameState.I.S.N918Loot.Contains("<含 letter 的 spot fullId>") || _level.HasLetter;
  ui.RadioSent  += () => { _radioSent = true; GameState.I.S.N918RadioSent = true; GameState.I.Save(); };
  ```
  然后 `CheckEnding()`（1733）把 `&& N918RadioSent` 作为通关前置之一（与 `Testimony.Count` 并列）。
- 这样**九一八与江桥共用同一份 `EnterableHouse.cs`**，只是传参不同，零拷贝风险。

---

## 3. 大地图流向（scripts/AsiaMap.cs）

文件：`scripts/AsiaMap.cs`（1278 行）

### 3.1 关键定位（用户给的行号基本准确）

| 行号 | 内容 |
|---|---|
| 143 | `Tag("沈阳", 123.4f, 41.8f, -48, -4, 11);` 沈阳城市 pin（在 `BuildLabels()` 内） |
| 195–213 | `_enter` 按钮创建与样式；**`_enter.Pressed += () => EnterJiangqiao();`（213）** |
| 243 | 时间线行 `("1931.9　沈阳 · 九一八事变", null),`（在 `BuildChapters()` 内） |
| 365 | `BriefRead => GameState.I.S.JiangqiaoDone \|\| _briefOpened;` |
| 369–412 | `Refresh()`：根据 `KoreaRead/TaiwanRead/RouteOn/BriefRead` 控制纸条/路线/按钮可见性 |
| 388 | `_enter.Visible = RouteOn && BriefRead;` ← 入口按钮显隐门控 |
| 519–544 | `EnterJiangqiao()`：首次弹「九一八档案」(`MapNote918`) 后 `_main.StartJiangqiao()`；已读过则直接进 |

### 3.2 现有显隐门控（要改的核心）

- `RouteOn => KoreaRead && TaiwanRead`（364）：读完两张源流纸条才显路线。
- `_enter` 显隐：`RouteOn && BriefRead`（388）。`BriefRead` 当前绑定 `JiangqiaoDone`（365）。
- `_hint` 文案（395–399）与 `_status`（390–393）都硬编码「江桥」字样。

### 3.3 改成「大地图 → 九一八 → 江桥」的最小改法

1. **新增入口方法** `EnterN918()`（仿 `EnterJiangqiao` 519 结构），逻辑：
   - 若 `!GameState.I.S.MapNote918` → 先弹一次「九一八档案」Note，关闭回调里置 `MapNote918=true` 并 `Save()`，再 `_main.StartN918()`；
   - 否则直接 `_main.StartN918()`。
   - 把 `_enter.Pressed`（213）从 `EnterJiangqiao()` 改为 `EnterN918()`。
2. **门控拆分**（关键）：把「进九一八」和「进江桥」拆成两阶段。建议：
   - `BriefRead` 改成「九一八已读/已通关」：`BriefRead => GameState.I.S.N918Done || _n918Opened;`
   - 九一八通关后（`N918Done=true`）地图显示**第二个进入按钮**（或一个按钮文案变「进入嫩江江桥」），其 `Pressed` 才调 `EnterJiangqiao()`。
   - 即：沈阳 pin / `_enter` 第一阶 → 九一八；九一八 `LevelEndUi` 返回地图后，`_enter`（或新按钮）第二阶 → 江桥。
3. **文本/状态**（`_hint`/`_status`/时间线 243）相应改为两关文案；时间线 243 的 `("1931.9 沈阳·九一八事变", null)` 可保留或加 `("1931.11 嫩江江桥", null)`。
4. **沈阳 pin 交互（可选增强）**：当前 143 只是静态标签 `Tag(...)`。若想「点沈阳直接进九一八」，可在 `BuildLabels`/`Pin` 阶段给沈阳 pin 绑 `Pressed += EnterN918`（需确认 `Tag`/`Pin` 是否返回可点击节点；见 `DrawPins` 1043、`Pin` 1050）。若 pin 不可点，则保留 `_enter` 按钮为唯一入口更稳。

### 3.4 StartNewGame() 是否需要改（Main.cs:116）

- `scripts/Main.cs:116–120`：
  ```csharp
  public void StartNewGame() { GameState.I.Reset(); ShowAsiaMap(); }
  ```
- **结论：StartNewGame 本身不需要改**（仍先进大地图）。是否改动取决于「新游戏是否默认已解锁江桥」——
  因为现在江桥入口门控在 `N918Done`，新档 `N918Done=false`，自然先走九一八，无需动 `StartNewGame`。
- 真正要加的是 `Main.cs:113` 之后新增 `public void StartN918() => Swap(new N918Level(this));`（见第 5 节）。

---

## 4. WitnessState.cs 需要新增什么

文件：`scripts/core/WitnessState.cs`（注意真实路径在 `scripts/core/`，不是 `scripts/WitnessState.cs`）

### 4.1 现有相关字段（`scripts/core/WitnessState.cs:40–46`）

```csharp
public bool MapNote918;                                  // 40 九一八档案已读（进江桥前弹一次）
public bool MapOrdered;                                  // 41 三纸条已归档
public bool JiangqiaoDone;                                // 44 江桥是否通关
public HashSet<string> JiangqiaoTestimony = new();        // 45 已记录江桥证言 id
public HashSet<string> JiangqiaoLoot = new();             // 46 已搜刮屋子/箱子（按 id 持久）
```
（南京/难民/证据系统：`Evidence/Choice1/Choice2/Refugees/Food/.../Finished`，属南京关，**九一八不要继承**）

### 4.2 九一八需要新增的字段（建议定义）

在 `scripts/core/WitnessState.cs:46` 之后新增（保持与江桥同构、纯 C# 可 headless 序列化）：

```csharp
// ===== 九一八事变关（轻量兄弟版，沿用江桥简化模型） =====
public bool N918Done;                       // 是否通关 1931 沈阳·九一八（替代原 MapNote918 的"带过"语义）
public HashSet<string> N918Testimony = new(); // 已记录的九一八口述/证言 id
public HashSet<string> N918Loot = new();      // 已搜刮屋子/箱子（供 HouseInteriorUi 注入）
public bool N918HasLetter;                   // 是否已在屋内取得"信"（信封状态，radio 提交的前置）
public bool N918RadioSent;                   // 是否已用无线电发信机把信发出（通关前置之一）
```

> 说明：
> - `MapNote918`（现有 42 行）建议**保留**作为「大地图九一八档案弹窗是否展示过」的开关（见第 3.3 节 `EnterN918`），不要再把它当通关 flag；真正的通关 flag 用新增的 `N918Done`。
> - 若想让「持有信」更稳健，可不用 `N918HasLetter` 而直接复用 `N918Loot.Contains("<houseId>:letter1")`（见 2.3 方案 B）。但若信与发信机可能在不同屋，单独的 `N918HasLetter` 更清晰。
> - 九一八**不需要** `Refugees/Choice1/Choice2/Evidence` 那套（南京道德选择系统），用户已明确可简化。

### 4.3 序列化兼容性

`WitnessState` 用字段序列化（`SmokeTest.cs:149` `JsonSerializerOptions { IncludeFields = true }`）。
新增字段带默认值（`false` / `new HashSet`）**不会破坏旧存档反序列化**（缺字段的 JSON 反序列化时取默认值）。可在 `tests/SmokeTest.cs` 的「存档往返」段（148–173）补一条针对 `N918*` 字段的往返断言（见第 7 节）。

---

## 5. Main.cs 路由（新增 Start918 / N918Level 入口）

文件：`scripts/Main.cs`

### 5.1 现有路由（`scripts/Main.cs:104–126`）

```csharp
public void ShowAsiaMap() => Swap(new AsiaMap(this));        // 107
public void StartJiangqiao() => Swap(new JiangqiaoLevel(this)); // 113
public void StartNanjing() => Swap(new Level(this));         // 112
public void StartNewGame() { GameState.I.Reset(); ShowAsiaMap(); } // 116
```
`Swap(Node)`（56–102）负责黑场切换 + `AddChild` + `FullRect` 预设，所有关都走它。

### 5.2 需要新增的入口

在 `scripts/Main.cs:113` 之后加一行（与 `StartJiangqiao` 完全同构）：

```csharp
public void StartN918() => Swap(new N918Level(this));
```

- `N918Level` 类：**新建 `scripts/N918Level.cs`**，作为 `JiangqiaoLevel` 的兄弟版（`public partial class N918Level : Node2D`），从 `JiangqiaoLevel.cs` 复制整文件后：
  - 类名、`Main` 构造、`_Ready` 文案（开场导语 282–284、HUD 日期 1078、结算 UI 5244 等）改为九一八背景；
  - 世界坐标/房屋布局/证言坐标（1042–1064）按九一八重排（沈阳北大营、柳条湖等意象，注意尺度仍是 `PersonH/WallH` 体系）；
  - 把 `Jiangqiao*` 状态引用全部替换为 `N918*`（`JiangqiaoDone→N918Done`、`JiangqiaoTestimony→N918Testimony`、`JiangqiaoLoot→N918Loot`，以及新增的 `N918HasLetter/N918RadioSent`）；
  - `CheckEnding`（1733）增加 `&& GameState.I.S.N918RadioSent` 前置；
  - `LevelEndUi` 按钮（5253）`ShowAsiaMap` 不变（返回地图后由 `N918Done` 解锁江桥入口，见 3.3）。

### 5.3 从 AsiaMap 调用

`scripts/AsiaMap.cs:213` 的 `_enter.Pressed` 改为 `EnterN918()`（新建方法，仿 519），九一八通关后地图再暴露江桥入口调 `EnterJiangqiao()`。无需在 `Main` 之外额外接线。

---

## 6. 实现九一八关的最小接缝清单（按图施工）

### 6.1 可「复制后改」的部分（来自 JiangqiaoLevel.cs）

| 来源（JiangqiaoLevel.cs 行） | 动作 |
|---|---|
| 整文件 124–5269（类 + 全部私有嵌套美术类） | **整文件复制到 `N918Level.cs`**，改类名与文案/坐标/状态字段 |
| `BuildBackdrop` 830 / `Backdrop` 2167 / `SkyScreen` 1929 / `GroundArt` 3092 | 随文件一起复制，**直接复用，无需重写**（九一八美术与江桥同源/同管线） |
| `BuildPlayer` 990、`SpawnEnemy` 1712、`CheckWaves` 1651 | 复制后按需调参（敌人密度、波次） |
| `BuildTestimonies` 1040 / `Testimony` 4976 | 复制后改三份口述文案与坐标 |
| `CheckEnding` 1733 / `LevelEndUi` 5189 | 复制后加 `N918RadioSent` 门控；按钮保留 `ShowAsiaMap` |
| `OpenHouse` 1436 | 复制后加 `ui.HasEnvelope` / `ui.RadioSent` 接线 |

### 6.2 需要「新建 / 修改」的部分

| 文件 | 改动 |
|---|---|
| `scripts/N918Level.cs` | **新建**（见 5.2 / 6.1） |
| `scripts/core/WitnessState.cs`（46 之后） | 新增 `N918Done/N918Testimony/N918Loot/N918HasLetter/N918RadioSent`（见 4.2） |
| `scripts/EnterableHouse.cs` | ① 构造函数解耦 loot 集合（301 硬绑定 → 注入参数）；② 加 `radio` spot（`BuildSpots` 392 后）+ `OnSpot` 的 `case "radio"`（433 后）；③ 加 `_hasEnvelope`/`_onRadioSent` 字段与委托（283–293 附近） |
| `scripts/Main.cs`（113 后） | 新增 `public void StartN918() => Swap(new N918Level(this));` |
| `scripts/AsiaMap.cs` | ① 213 改 `EnterN918()`；② 新增 `EnterN918()`（仿 519）；③ `BriefRead`(365)/`_enter.Visible`(388)/`_hint`(395)/`_status`(390) 改为两阶段门控（先九一八后江桥）；④ 时间线 243、`Tag("沈阳")` 143 文案适配 |

### 6.3 风险点（务必注意）

1. **🔴 体量风险**：`JiangqiaoLevel.cs` 已 5269 行且把美术管线写成**私有嵌套类**。若把九一八塞进同一文件，嵌套类命名冲突、可读性崩坏。**务必新建 `N918Level.cs` 独立文件**（用户已点明）。复制后记得全局替换 `Jiangqiao` 前缀状态名，否则会误写江桥存档。
2. **🔴 EnterableHouse.cs 的硬绑定**：`scripts/EnterableHouse.cs:301` 写死 `JiangqiaoLoot`。**不先解耦就直接给九一八用，九一八的搜刮会污染江桥存档**（或反之）。先改构造函数注入 loot 集合，再谈新增 radio。
3. **🟠 复用而非继承**：不要图省事让 `N918Level : JiangqiaoLevel` —— 江桥大量 `private` 字段/嵌套类对子类不可见，且会连带江桥专属坐标。保持「并列 Node2D + 复制」最稳。也**不要继承 `Level.cs`**（南京难民/证据系统会整套入侵，违背「轻量」）。
4. **🟠 场景流转复用**：两关都靠 `Main.Swap`（56）与 `LevelEndUi` 的 `ShowAsiaMap`（5253）回地图，无需新场景文件；但 `N918Level` 若引用了江桥私有嵌套类的实例（如某处 `new JiangqiaoLevel.Xxx()`），复制后必须改成 `N918Level.Xxx()`。
5. **🟠 昼夜/尺度常量**：`SunriseH/SunsetH/MoonLit`（237–239）、`PersonH/WallH/BridgeH`（196–213）是 `public const`，复制后九一八若想换时段（如 9.18 夜）改 `_clock` 初值（240）与 `DebugClock` 即可，无需动美术。
6. **🟡 回归风险**：改 `EnterableHouse.cs` 会影响**江桥现有屋内体验**，需同时跑江桥冒烟/截图（见第 7 节）确认未破坏 `ammo/letter/will/clue/chest` 等行为。

---

## 7. 测试 / 验收钩子

### 7.1 现有 tests 目录

- `tests/SmokeTest.cs`（184 行）：**headless 纯逻辑冒烟测试**（无画面）。运行：
  `Godot_v4.7-stable_mono_win64.exe --headless --path . --script res://tests/SmokeTest.cs`
  覆盖：语言归一化、九语资源完整性、**WitnessState 证据状态机/结算/存档往返**、源头关逻辑、横版地形 `Level.GroundY`、解谜 `PuzzleLogic`、史料卡数、JSON 序列化往返（148–173）。
  ⚠️ 当前 **没有任何针对 JiangqiaoLevel 运行时或 EnterableHouse 的测试**（它们依赖 Godot 节点，headless 难直接实例化；`SmokeTest` 只测纯 C# 状态机）。

### 7.2 自动化截图脚本

- `scripts/Capture.cs`（345 行）：直接 `new JiangqiaoLevel(new Main())` 塞进 `SubViewport`（`Capture.cs:38`），按时间轴分阶段模拟输入（`Input.ActionPress`）、冻结玩家坐标、调 `SaveShot()` 写 `res://_shots/*.png`。**江桥运行期不依赖 `_main`**（仅结算回调用），故可传占位 `new Main()`。九一八可照抄一个 `Capture918.cs`（把 `JiangqiaoLevel` → `N918Level`、坐标换成九一八点位）。
- `scripts/CaptureMap.cs`（157 行）：驱动 `TitleScreen` → `AsiaMap` 流程截图（`CaptureMap.cs:35/82`），自动点纸条按钮（`FindButton`+`EmitSignal`，92/107）。新增九一八后，应在此脚本里把「`_enter` 按钮 → `EnterN918` → 九一八结算 → 回地图 → 江桥解锁」这条新链路也截一帧验证。注意它会在退出时**还原 `MapNoteKorea/MapNoteTaiwan`**（130–132），新脚本也要还原 `N918*`/相关 flag 以免污染真实存档。

### 7.3 新增关卡要加的冒烟 / 验收

1. **逻辑层（改 `tests/SmokeTest.cs`）**
   - 在「存档往返」段（148–173）补：序列化一个含 `N918Done/N918Testimony/N918Loot/N918HasLetter/N918RadioSent` 的 `WitnessState`，反序列化后字段值正确（验证新字段不破坏兼容）。
   - 可加一个纯逻辑用例：模拟「先 record 三份九一八口述 + 取得信 + radio 发报」后 `N918Done` 置位的条件判断（若把门控抽成可单测的 helper）。
2. **屋内机制（改 `EnterableHouse.cs` 后必加）**
   - 验证：未持信时 `radio` spot 不触发发报（`_hasEnvelope()==false` 分支）；持信后触发 `_onRadioSent`；loot 注入到 `N918Loot` 而非 `JiangqiaoLoot`（解耦回归）。江桥侧原有 `ammo/letter/will/clue/chest` 行为需断言未被破坏。
3. **流程层（改 `scripts/AsiaMap.cs` 后）**
   - `CaptureMap.cs` 增加阶段：读两张纸条 → `_enter` 进九一八 → 九一八 `LevelEndUi` 返回 → 验证地图出现「进江桥」入口（`_enter` 第二阶可见）。
4. **关卡层（新增 `Capture918.cs`）**
   - 仿 `Capture.cs` 对九一八跑一轮坐标/房屋/敌人/屋内 UI 截图，确认美术管线复制无误、radio spot 可见。

---

## 8. 总结（一句话）

九一八关应**新建独立 `N918Level.cs`（从 `JiangqiaoLevel.cs` 整文件复制后改文案/坐标/状态字段，不要继承也不要塞进现有文件）**，先把 `EnterableHouse.cs` 的 loot 集合解耦并加 `radio` spot + `HasEnvelope/RadioSent` 委托（与现有 `GotAmmo` 同款事件模式），再在 `WitnessState.cs` 加 `N918*` 字段、`Main.cs` 加 `StartN918()`、`AsiaMap.cs` 把 `_enter` 入口拆成「九一八 →（通关后）江桥」两阶段，即可复用同一套美术/玩法管线落地。
