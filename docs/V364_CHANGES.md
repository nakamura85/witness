# v3.64 交付说明：章节流程 / 室内画风 / 场景布置

> 全部改动均经 `dotnet build witness.csproj` 编译（`Build succeeded. 0 Error(s)`，3 个既有 warning），
> 并用 Godot 4.7.2 mono 实机截图场景验证。截图产物在 `res://_shots/`（已被 .gitignore 忽略）。

## 1. 需求 → 落点对照

| # | 用户反馈 | 主要落点 | 处理 |
|---|---|---|---|
| 1 | 点简报后不该直接指向嫩江江桥；应先九一八，通关后图标再迈向江桥 | `scripts/AsiaMap.cs`、`scripts/CaptureMap.cs` | 路线拆两段；第一章隐藏江桥地名标签与图钉；通关后第二段路线/行军圈注生长，标记到位才开放入口 |
| 2 | 室内不合格，要按整体游戏画风生成 | `scripts/EnterableHouse.cs` | `WashPaint` + `RoomBg` 重绘；v3.64b 把硬边 `DrawRect` 叠色改为逐顶点 alpha 羽化 `SoftWash`、颜料浓淡斑 `Blot`、纸纹 `Grain` 1400 点、整幅纸面晕染；主罩染浓度下调留白 |
| 3 | 外面房子不能像排练过一样规律 | `scripts/N918Level.cs`、`scripts/JiangqiaoLevel.cs` | 固定播种 `19310918` / `19311104`，位置、宽度、沉入深度、lift 错落；同关卡稳定、两关不同相 |
| 4 | 军事通信据点室内要按历史记载布局 | `scripts/EnterableHouse.cs` `DrawCommOutpost()` | 左墙电闸/手摇发电机 → 中桌电键/耳机/电报纸/油灯 → 右墙无线电发信机/波段表盘/散热栅 → 天线引入线与绝缘子、蓄电池、作战地图、地面走线 |
| 5 | 草地不该一颗两颗，要一整片 | `scripts/N918Level.cs` | 斑块化 `DrawMeadowBand()` 连续草色带（逐段凸四边形叠三层水彩）+ 加密草丛（间距 5–11px、高 14–34px）+ 前景草贴图间距 26–52px；野地占比 0.93 |
| 6 | 沙包不要生成在房子门口 | 两关 `CoverArt.SafeSandbagX()` | 所有房屋（含远景废墟）门口避让；修复注入时机后生效 |

## 2. 关键工程决策与踩坑

| 问题 | 结论 |
|---|---|
| Godot 4 C# 逐顶点颜色 | `DrawColoredPolygon(Vector2[], Color)` 只收单色；必须用 `DrawPolygon(Vector2[], Color[])` |
| 水彩柔边 | 4 层硬边 `DrawRect` 叠色仍读作平涂；改为外环 alpha=0 / 内环实色的三角带插值 |
| 小物件多边形自交 | 抖动幅度改为随尺寸缩放 `min(3.2, min(w,h)/10)`，消除 `triangulation failed` |
| 沙包避让一直失效（根因） | 实机日志证明 `CoverArt` 在**构造函数**阶段入树并先落位，而 `BuildHouses()` 在 `_Ready()` 里才执行；对象初始化器求值时 `_doorXsAll`/`_houses` 都是空。修复：注入与 `RebuildLayout()` 移到 `_Ready()` 的 `BuildHouses()` 之后，另加场景树实时读作为兜底 |
| 九一八门坐标错误 | `_doorSpecs` 只登记非 `_autumn` 贴图名，导致 autumn 房屋 `DoorX` 全 fallback 到 0.18；补齐三个 autumn 变体 |
| 大地图第一段路线 | 通关后新进大地图时 `Progress` 补成 1，避免沈阳以北凭空少一截 |
| 室内截图 | `HouseInteriorUi : CanvasLayer` 在 `SubViewport` 拍不到；新增 `scenes/CaptureRoom.tscn` 直接渲染 `RoomBg`，并用 `SaveShot(name, onSaved)` 回调消除异步竞态 |

## 3. 实机验证证据

| 场景 | 产物 | 结论 |
|---|---|---|
| `CaptureMap.tscn` | `map_04_arrows.png` | 第一章无「嫩江·江桥 1931.11」、无北上路线，只高亮沈阳九一八 |
| `CaptureMap.tscn` | `map_05_after_918.png` | 通关后才出现沈阳→嫩江第二段路线、江桥地名/图钉/圈注与「行军标记已抵达嫩江江桥」 |
| `CaptureRoom.tscn` | `room_01..04_*.png` | 四间房截图与房间一一对应；纸本水彩柔边、纸纹、浓淡斑，无破面 |
| `CaptureN918.tscn` | `n918_02_scene.png` / `n918_03_house.png` | 大屋门口与两间可进屋门口均无沙包；地面成连续野草带 |
| `Capture.tscn` | `capture_09_sandbags.png` / `capture_11_burnt.png` | 江桥房屋门口无沙包遮挡，雪原与村落正常 |

`CaptureRoom` 本轮已无 `triangulation failed`；两关各剩 1 次历史既有无害警告（前景雪檐多边形），不影响出图。

## 4. 仍存差距（如实标注，不冒充已完成）

- 室内是**程序化钢笔淡彩**，与 `assets/art` 里 AI 生成的水彩贴图相比，笔触丰富度仍有限；交互纸签仍是偏白的小矩形。
- 草地已连成片，但草丛密度与真实草原仍有距离；若需更厚重质感，下一批应改用水彩贴图铺地而非纯程序化线条。
- 多关卡扩展、东北军友军 NPC、天气史实核查后的文档整理、腾讯文档《开发游戏坑／解决方案》补录，仍未完成。
