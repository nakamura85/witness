# 解谜设计清单 · 嫩江江桥 1931.11.4 关卡

> **目标**：4 种不同类型的解谜（Valiant Hearts / Patrick's Parabox / Viewfinder 风格），玩家完成后才能通关。每种解谜有**条件 / 失败 / 奖励**明确边界。
> 代码入口：`scripts/JiangqiaoLevel.cs` 新增 4 个 partial class（每个解谜一个），主关卡组装时挂载。

---

## 0. 关卡结构总览

```
关卡从西向东（左→右）推进，WorldW ≈ 5000px

起点 (500, GroundY)
 ├─ 【解谜 1】物证拾起匹配（t1 之前，x ≈ 700）
 ├─ 证词点 t1 (720, GroundY)
 ├─ 【解谜 2】推箱子上高台（x ≈ 1600）
 ├─ 证词点 t2 (2220, GroundY)
 ├─ 【解谜 3】潜行绕过巡逻兵（x ≈ 2800-3200）
 ├─ 证词点 t3 (3480, GroundY)
 ├─ 【解谜 4】时间线板归位（关卡末 x ≈ 4200）
 └─ 终点 (4700, GroundY)
```

**通关条件**：3 份完整证言 + 时间线板放对 → 触发 Viewfinder 时刻 + 通关档案。

---

## 1. 物证拾起匹配（Evidence Pickup & Match）

> **类型**：Viewfinder 式（先拾起才能完整记录证言）
> **目的**：让玩家不是"走到就按 E"，而是**先找齐物证**才能完整记录。

### 关卡位置
- **物证 1**：歪把子机枪零件（x=620），埋在雪里（只露一角，走近才看得清）→ 匹配证词 1（机枪手赵永生）
- **物证 2**：带血绷带卷（x=1900），在战壕里 → 匹配证词 2（卫生员李桂兰）
- **物证 3**：铁轨碎段（x=3300），断在路边 → 匹配证词 3（铁路工人孙德山）

### 交互
- 走近时出现 **E 提示**：「拾起歪把子零件」
- 按 E 拾起 → HUD 右下角**背包栏**显示（+1，显示物品图）
- 未拾起匹配物证就走到证词点按 E：**证言显示「关键细节缺失」**，HUD 显示 `× 不完整`，**进度环停在 70% 就不动了**
- 拾起匹配物证后再到证词点按 E：**进度环走完 100%**，证言完整显示 + 存档

### 失败状态
- 物证可以**永久丢失**吗？不——玩家不会丢，会一直留在背包里直到用完
- 如果**物证被埋得太深**没看见？不会——走近 50px 内自动**轻微发光**（黄色点闪烁）

### 奖励
- 完整记录 → HUD 进度环 100% + 证词完整文本存档 + 解锁该证词的史料卡
- 不完整 → 进度环停在 70% + 证言显示"缺失"

### 代码结构
```csharp
partial class EvidencePickup : Node2D
{
    string Id { get; }       // "gun_parts" / "bandage" / "rail_chunk"
    string TestimonyId { get; } // "t1" / "t2" / "t3"
    bool PickedUp { get; private set; }
    void DrawBase() { /* 手绘漫画风道具 */ }
    void DrawGlow() { /* 走近时黄色闪烁发光 */ }
}
```

---

## 2. 推箱子垫脚（Push Crate to Climb）

> **类型**：Valiant Hearts 经典环境解谜
> **目的**：高台（铁丝网后面）上有**隐藏物证**（望远镜零件），默认跳不上。必须推箱子垫脚。

### 关卡位置
- 木箱在 **x=1580**（地面），玩家可以推
- 高台在 **x=1680**，高度 **140px**（> 跳跃高度 100px，默认跳不上）
- 隐藏物证在高台上（x=1700）：望远镜零件 → 可选解锁额外史料卡

### 交互
- 靠近木箱 → 按 E **推**（木箱跟着玩家一起走，速度减半）
- 推到高台墙边 → 按 E **攀爬木箱** → 爬上木箱 → 再按 E **翻过高台**
- 翻过高台 → 走到隐藏物证按 E 拾起
- 返回来时可以直接从高台跳下（跳落有伤害，< 3 格高度无伤害）

### 失败状态
- 推错方向？可以推回来（没有锁定或单向门）
- 箱子被推到悬崖？本关卡不会发生（地面是平的）

### 奖励
- 拿到隐藏物证 → 解锁**额外史料卡 04**（日军侦察战术）
- 没拿到也可以通关（不算硬性条件）

### 代码结构
```csharp
partial class PushCrate : Node2D
{
    Rect2 Hitbox { get; }
    bool BeingPushed { get; private set; }
    Vector2 Velocity { get; private set; }
    void DrawCrate() { /* 手绘漫画风木箱 + 落地阴影 */ }
}
```

---

## 3. 潜行绕过巡逻兵（Stealth & Patrol）

> **类型**：潜行 + 声引（Valiant Hearts 巡逻兵视野锥 + 声引机制）
> **目的**：关卡中段有两名关东军巡逻兵，**视野锥 + 视线遮挡 + 声引**。必须蹲下绕 / 扔石头引开。

### 关卡位置
- **巡逻兵 A**：x=2800 到 x=3000 之间来回走，面朝方向在 x=2900 反向
- **巡逻兵 B**：x=3100 到 x=3250 之间来回走
- 两个巡逻兵之间有**铁丝网遮挡**（部分遮挡视线，绕到铁丝网后面可以躲）
- **石块堆**在 x=2700 地上（可以拾起扔）

### 交互
| 动作 | 效果 |
|------|------|
| **S 蹲下** | 走路速度减半，但**视野检测概率 -60%**（更低的"被看见"概率） |
| **按 E + 方向键扔石头** | 石头飞向远处 → 落地声 → 巡逻兵会**离岗去查看 6 秒** |
| **视野锥** | 巡逻兵前视 120px × 60°，半径内有玩家（非蹲下）→ 报警射击 |
| **视线遮挡** | 铁丝网 / 掩体 / 墙可以**完全挡住**巡逻兵视野（玩家在遮挡物后完全隐形） |

### 失败状态
- 被发现 → **巡逻兵射击** → 玩家生命 -1 → 巡逻兵继续回到岗位
- **累计被发现 3 次** → HUD 显示「被发现 3 次：部分证词证据被销毁」→ **t3 证词只能录 50%**
- **累计被发现 5 次** → 玩家死亡 → **Game Over** → 从头开始（不自动存档）

### 奖励
- 潜行通过 0 次被发现 → **额外史料卡 05**（731 部队早期活动记录）
- 潜行通过 ≤2 次 → 正常通关
- 潜行通过 3+ 次 → t3 证词残缺

### 代码结构
```csharp
partial class PatrolEnemy : Node2D
{
    float SightRadius { get; } = 120f;
    float SightAngle { get; } = Mathf.DegToRad(60f);
    bool Alerted { get; private set; }
    Vector2 AlertSource { get; private set; }  // 石头落地位置
    bool CanSeePlayer { get; }  // 每帧计算
    void DrawSightCone() { /* 黄色半透明扇形，粗墨描边 */ }
}

partial class RockPickup : Node2D
{
    bool PickedUp { get; private set; }
    void Throw(Vector2 target) { /* 抛物线动画 + 落地声 */ }
}
```

---

## 4. 时间线板归位（Timeline Board Matching）

> **类型**：Patrick's Parabox 式归位 + Viewfinder 时刻
> **目的**：关卡末尾有一块时间线板（大地图缩影），3 个纸片必须拖到正确年份凹槽。放对后触发**Viewfinder 时刻**。

### 关卡位置
- 时间线板在关卡末 x=4200（靠近终点）
- 3 个纸片在板旁边散放：
  - 纸片 A：「1895 马关条约」
  - 纸片 B：「1910 日韩合并」
  - 纸片 C：「1931 江桥抗战」
- 3 个凹槽（时间线板上 3 个空位）：
  - 凹槽 1：1895 位置
  - 凹槽 2：1910 位置
  - 凹槽 3：1931 位置

### 交互
1. 走到时间线板前 → 按 E **打开时间线板**
2. 时间线板弹出全屏浮层（手绘漫画风）
3. 点击纸片 → 纸片**贴在光标上跟随**
4. 拖到凹槽 → 松手
   - **放对** → 纸片嵌入凹槽 + 轻微震动动画 + 绿色勾选
   - **放错** → 纸片弹回原位 + 抖动动画 + 红色×标记
5. 3 张全部放对 → **触发 Viewfinder 时刻**：
   - 时间线板淡出
   - 手绘大地图渐显（与 AsiaMap 一致）
   - 日军行军线**动画生长**：从汉城 → 鸭绿江 → 沈阳 → 嫩江江桥，逐段画出来
   - 完成后 → 通关档案卡 → 史料馆解锁
6. 放错 ≥3 次 → 弹提示「仔细想想时间线」，但不会锁定玩家

### 失败状态
- 放错不会锁死纸片 → 可以无限重试
- 必须**全部放对**才能触发 Viewfinder 时刻 + 通关

### 奖励
- Viewfinder 时刻动画 + 通关档案卡
- 史料馆解锁全部 5 张史料卡 + 通关 100%

### 代码结构
```csharp
partial class TimelineBoardUi : CanvasLayer
{
    CanvasLayer Layer = 22;
    Vector2[] Slots { get; }     // 3 个凹槽位置
    FragmentPanel[] Fragments { get; }  // 3 个可拖动纸片
    bool[] Correct { get; private set; }  // 哪些放对了
    void Show() { /* 弹出面板 */ }
    void Hide() { /* 收起面板 */ }
    void TriggerViewfinder() { /* 播放 Viewfinder 时刻 */ }
}

partial class FragmentPanel : Control
{
    string Year { get; }         // "1895" / "1910" / "1931"
    string Content { get; }
    Vector2 Slot { get; private set; }
    bool IsCorrect { get; private set; }
}
```

---

## 5. 解谜整合进关卡流程

### 标准流程（从起点到通关）
```
1. 起点：字幕黑屏 → "1931.11.4 嫩江江桥"
2. 【解谜 1】找 3 件物证（歪把子零件 / 绷带 / 铁轨碎段） → 分别记录 3 份证词
   ↑（物证没拾到就走到证词点会显示"缺失"，必须回头找）
3. 【解谜 2】推箱子垫脚爬上高台 → 可选隐藏物证（望远镜零件）
4. 【解谜 3】潜行绕过 2 名巡逻兵 → 被发现 3 次以上 t3 证词残缺
5. 【解谜 4】时间线板 3 张纸片归位 → 触发 Viewfinder 时刻 → 通关
```

### HUD 状态显示
- 右上角：**背包栏**（显示已拾起的物证图标 + 数字）
- 左上角：**已记录 X / 3 份证词**
- 证词点前：**缺物证** / **可记录** 状态徽章
- 潜行时：**潜行中** 红色徽章（被发现时变「暴露！」）
- 巡逻兵附近：**黄色感叹号**（被看见时变红色）

### 全局失败条件（只一个）
- 累计被发现 5 次 → **Game Over → Restart Level**（自动存档重置）
- 没有 Game Over 不惩罚：其他解谜都可以无限重试

---

## 6. 技术实现要点

| 模块 | 文件 | 关键实现 |
|------|------|----------|
| 物证 | `scripts/EvidencePickup.cs`（新增） | Node2D + 手绘漫画风道具 + 走近发光 + 背包状态机 |
| 推箱 | `scripts/PushCrate.cs`（新增） | Node2D + RigidBody2D 替代（用自定义物理，Godot RB 太重）|
| 巡逻 | `scripts/PatrolEnemy.cs`（重写 Patrol.cs） | 视野锥计算 `PointInPolygon` + 遮挡物 RayCast2D |
| 石头 | `scripts/RockPickup.cs`（新增） | Area2D + 抛物线投掷 + 落地声 + 吸引巡逻兵 |
| 时间线板 | `scripts/TimelineBoardUi.cs`（新增） | CanvasLayer 全屏浮层 + 自定义拖拽 + Viewfinder 动画 |
| 主关卡组装 | `scripts/JiangqiaoLevel.cs`（重写） | 组装上述全部 + 通关流程 + Viewfinder 触发 |

### 关键数据
```csharp
public record Evidence(string Id, string TestimonyId, Vector2 WorldPos, string Name);
public record PatrolPath(float StartX, float EndX, float Speed, bool FaceRight);
public record TimelineFragment(string Year, string SlotYear, string Content);
```

---

## 7. 后续扩展（留给下一版）

这版先做 **嫩江江桥 1931** 的 4 种解谜，后续章节可以复用：

| 章节 | 主要解谜类型 | 变体 |
|------|-------------|------|
| 哈尔滨 731（1940） | 推箱 + 时间线板 | 黑暗走廊里推手术台 + 拼实验记录 |
| 南京 1937 | 潜行 + 物证匹配 | 魏特琳日记散页藏在难民营各处 |
| 知知港 1942 | 潜行 + 时间线板 | 马来亚丛林 + 拼图还原惨案经过 |
| 终战 1945 | 时间线板 | 6 个投降签字事件拼时间线 |
