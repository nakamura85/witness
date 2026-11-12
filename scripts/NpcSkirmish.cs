using System;
using System.Collections.Generic;
using Godot;

namespace Witness;

/// <summary>
/// v3.69 · 敌我 NPC 交战数值表（全部可复算）。
///
/// 用户 2026-11-12：「己方 NPC 直接跟敌军插肩而过…明明彼此都是敌人肯定
/// 会自动交火，靠近也会拔刀搏斗」「他们互相都会死亡和倒地也会正常扣血，
/// 但是枪击和打斗那些伤害必须砍半」「也会呼叫支援，但也要打折，不然
/// 主角的游玩体验会完全没有」。
///
/// 口径：**只砍 NPC↔NPC 的伤害**，玩家↔敌兵的原数值一律不动。
/// 血量 100，步枪 34×0.5=17 → 6 枪倒地；刺刀 26×0.5=13 → 8 刀倒地。
/// 相比玩家两枪击杀敌兵，NPC 互殴慢一倍以上，战场有动静但不抢主角戏。
/// 支援概率 0.25 / 0.15（不是「每次呼叫必来」），每关每方最多 2 次，
/// 同时在场敌兵 ≤6、友军 ≤3。
/// </summary>
public static class SkirmishRules
{
    public const float NpcHp = 100f;
    /// <summary>NPC 互殴伤害系数：用户要求「砍半」。</summary>
    public const float DamageScale = 0.5f;
    public const float RifleDamageBase = 34f;
    public const float MeleeDamageBase = 26f;
    public static float RifleDamage => RifleDamageBase * DamageScale;   // 17
    public static float MeleeDamage => MeleeDamageBase * DamageScale;   // 13

    public const float MeleeRange = 74f;       // 进入这个距离就停下拼刺刀，绝不互相穿过
    public const float FireRange = 340f;       // 自动交火距离
    public const float FireInterval = 2.6f;    // 比玩家慢，避免 NPC 火力盖过主角
    public const float MeleeInterval = 1.5f;
    public const float EnemyDistractRange = 240f;
    public const float EnemyDistractInterval = 3.4f;
    public const float EnemyHitChance = 0.70f; // NPC 之间不是每枪必中

    /// <summary>呼叫支援打折：概率 + 次数双限。</summary>
    public const float ReinforceChanceEnemy = 0.25f;
    public const float ReinforceChanceAlly = 0.15f;
    public const int MaxReinforcePerSide = 2;
    public const int MaxEnemiesOnField = 6;
    public const int MaxAlliesOnField = 3;
}

public interface ISkirmishFoe
{
    bool IsDown { get; }
    float SkirmishHp { get; }
    void ApplySkirmishDamage(float damage, bool melee);
    void SkirmishAlert();
}

public interface ISkirmishAlly
{
    bool IsDown { get; }
    void ApplySkirmishDamage(float damage, bool melee);
}

/// <summary>关卡提供给交战系统的最小接口：不把 5000 行关卡逻辑塞进 NPC 里。</summary>
public interface INpcBattlefield
{
    Node2D BattleRoot { get; }
    float BattleGroundY { get; }
    float BattlePlayerX { get; }
    IEnumerable<Node2D> BattleFoes();
    IEnumerable<Node2D> BattleFriends();
    void BattleShot(Vector2 from, Vector2 to, bool fromAlly);
    void BattleClash(Vector2 at);
    void BattleCorpse(Vector2 pos, bool ally);
    void BattleAllyDown(AllySoldier a);
    void BattleReinforce(float x, bool ally);
    void BattleToast(string text);
    Node2D NearestAlly(float x, float range);
}

/// <summary>
/// 东北军友军 NPC。登场后守在主角前方，敌兵进入 340px 自动举枪交火；
/// 敌兵压到 74px 内双方**站定拼刺刀，不再擦肩而过**。中弹扣血、倒地
/// 后留一具尸体（CorpseActor），血量过半时按折扣概率呼叫一次增援。
/// </summary>
public partial class AllySoldier : Node2D, ISkirmishAlly
{
    private readonly INpcBattlefield _b;
    private SpriteActor _fig;
    private float _hp = SkirmishRules.NpcHp;
    private float _fireCd;
    private float _meleeCd;
    private float _flashT;
    private bool _down;
    private bool _calledSupport;
    private bool _meleeActive;
    private bool _loggedFire;
    private bool _loggedMelee;

    public bool IsDown => _down;
    public float SkirmishHp => _hp;

    public AllySoldier(INpcBattlefield battlefield, Vector2 pos)
    {
        _b = battlefield;
        Position = pos;
        ZIndex = 2;
        _fireCd = 0.9f + (float)GD.RandRange(0f, 0.9f);
    }

    public override void _Ready()
    {
        _fig = new SpriteActor(SpriteActor.Kind.NortheastArmy) { Facing = -1f };
        AddChild(_fig);
    }

    public override void _Process(double delta)
    {
        if (_down) return;
        float f = (float)delta;
        _flashT = Mathf.Max(0f, _flashT - f);
        _fireCd -= f;
        _meleeCd -= f;

        // 找最近的、还活着的敌兵
        ISkirmishFoe foe = null;
        Node2D foeNode = null;
        float best = float.MaxValue;
        foreach (var n in _b.BattleFoes())
        {
            if (n == null || !IsInstanceValid(n)) continue;
            if (n is not ISkirmishFoe s || s.IsDown) continue;
            float d = Mathf.Abs(n.Position.X - Position.X);
            if (d < best) { best = d; foe = s; foeNode = n; }
        }

        _meleeActive = false;
        if (foe != null && best <= SkirmishRules.FireRange)
        {
            float gap = foeNode.Position.X - Position.X;
            _fig.FacingRight = gap > 0f;
            _fig.Grounded = true;
            _fig.Speed = 0f;                 // 站定交火，不主动冲锋抢戏
            _fig.Phase = Mathf.Lerp(_fig.Phase, 0f, f * 6f);

            if (best <= SkirmishRules.MeleeRange)
            {
                // —— 拔刀搏斗：双方停住，绝不互相穿过 ——
                _meleeActive = true;
                _fig.Aim = Mathf.MoveToward(_fig.Aim, 0.35f, f * 8f);
                if (_meleeCd <= 0f)
                {
                    _meleeCd = SkirmishRules.MeleeInterval;
                    if (!_loggedMelee) { _loggedMelee = true; GD.Print("[V369] ally bayonet melee (locked, no pass-through)"); }
                    foe.ApplySkirmishDamage(SkirmishRules.MeleeDamage, true);
                    _b.BattleClash(Position + new Vector2(gap > 0f ? 18f : -18f, -52f));
                    _flashT = 0.16f;
                }
            }
            else
            {
                // —— 自动交火 ——
                _fig.Aim = Mathf.MoveToward(_fig.Aim, 1f, f * 8f);
                if (_fireCd <= 0f)
                {
                    _fireCd = SkirmishRules.FireInterval + (float)GD.RandRange(-0.3f, 0.5f);
                    if (!_loggedFire) { _loggedFire = true; GD.Print("[V369] ally opened fire on IJA"); }
                    _fig.MuzzleFlash = 1f;
                    _fig.Recoil = 1f;
                    var from = Position + new Vector2(gap > 0f ? 30f : -30f, -66f);
                    var to = foeNode.Position + new Vector2(0f, -60f);
                    _b.BattleShot(from, to, true);
                    if (GD.Randf() < SkirmishRules.EnemyHitChance)
                        foe.ApplySkirmishDamage(SkirmishRules.RifleDamage, false);
                }
            }
        }
        else
        {
            // 无敌情：慢慢跟到主角前方 210px 守着，不挡视线、不抢戏
            float hold = _b.BattlePlayerX + 210f;
            float dx = hold - Position.X;
            if (Mathf.Abs(dx) > 12f)
            {
                float step = Mathf.Clamp(dx, -42f * f, 42f * f);
                Position += new Vector2(step, 0);
                _fig.Speed = 42f;
                _fig.FacingRight = dx > 0f;
                _fig.Phase += f * 3.6f;
            }
            else
            {
                _fig.Speed = 0f;
                _fig.Phase = Mathf.Lerp(_fig.Phase, 0f, f * 4f);
            }
            _fig.Aim = Mathf.MoveToward(_fig.Aim, 0f, f * 5f);
        }
        _fig.Grounded = true;
        QueueRedraw();
    }

    public void ApplySkirmishDamage(float damage, bool melee)
    {
        if (_down) return;
        _hp -= damage;
        _fig.HurtFlash = 1f;
        Position += new Vector2(melee ? 7f : 4f, 0);
        _flashT = 0.14f;

        // 血量过半：按折扣概率呼叫一次增援（不是必来，也不是无限叫）
        if (!_calledSupport && _hp <= SkirmishRules.NpcHp * 0.5f)
        {
            _calledSupport = true;
            if (GD.Randf() < SkirmishRules.ReinforceChanceAlly)
                _b.BattleReinforce(Position.X + 240f, true);
        }

        if (_hp <= 0f)
        {
            _down = true;
            _fig.HurtFlash = 1f;
            _fig.Speed = 0f;
            _fig.Rotation = -Mathf.Pi * 0.5f;   // 先倒地，再留尸
            _b.BattleCorpse(Position, true);
            _b.BattleAllyDown(this);
            var t = GetTree().CreateTimer(0.35);
            t.Timeout += () => { if (IsInstanceValid(this)) QueueFree(); };
        }
    }

    /// <summary>拼刺刀时画一根短刺刀（粗墨线 + 钢色 + 卡通高光点）。</summary>
    public override void _Draw()
    {
        if (!_meleeActive && _flashT <= 0f) return;
        float dir = _fig.FacingRight ? 1f : -1f;
        var a = new Vector2(dir * 8f, -54f);
        var b = new Vector2(dir * 34f, -58f);
        DrawLine(a, b, InkStyle.InkColor, 4.2f, true);
        DrawLine(a, b, MangaPropArt.SteelBase, 2.2f, true);
        InkStyle.KawaiiHighlight(this, b, 1.8f, MangaPropArt.SteelHighlight);
    }
}

// =====================================================================
// 两关的战场接线（用 partial 复用关卡私有字段，不动原有 5000 行逻辑）
// =====================================================================

public partial class N918Level : INpcBattlefield
{
    private readonly List<AllySoldier> _allies = new();
    private int _allyReinforcements;
    private int _enemyReinforcements;

    public Node2D BattleRoot => _game;
    public float BattleGroundY => GroundY;
    public float BattlePlayerX => PlayerX;

    /// <summary>在关卡 _Ready() 末尾调用：先摆两名友军，形成「敌我会交火」的战场。</summary>
    public void InitSkirmish()
    {
        SpawnAlly(PlayerX + 240f);
        SpawnAlly(PlayerX + 410f);
    }

    private void SpawnAlly(float x)
    {
        if (_allies.Count >= SkirmishRules.MaxAlliesOnField) return;
        var a = new AllySoldier(this, new Vector2(x, GroundY));
        _game.AddChild(a);
        _allies.Add(a);
    }

    public IEnumerable<Node2D> BattleFoes()
    {
        foreach (var e in _enemies)
            if (e != null && IsInstanceValid(e)) yield return e;
    }

    public IEnumerable<Node2D> BattleFriends()
    {
        foreach (var a in _allies)
            if (a != null && IsInstanceValid(a) && !a.IsDown) yield return a;
    }

    public Node2D NearestAlly(float x, float range)
    {
        Node2D best = null;
        float bd = range;
        foreach (var a in _allies)
        {
            if (a == null || !IsInstanceValid(a) || a.IsDown) continue;
            float d = Mathf.Abs(a.Position.X - x);
            if (d < bd) { bd = d; best = a; }
        }
        return best;
    }

    public void BattleShot(Vector2 from, Vector2 to, bool fromAlly)
        => _fx.Shoot(from, to, fromAlly ? new Color(1f, 0.90f, 0.66f) : new Color(1f, 0.76f, 0.52f), 120f);

    public void BattleClash(Vector2 at) => _fx.Puff(at, 8f);

    public void BattleCorpse(Vector2 pos, bool ally)
        => _game.AddChild(new CorpseActor(
            ally ? CorpseActor.Faction.NortheastArmy : CorpseActor.Faction.IJA,
            new Vector2(pos.X, GroundY - 2), false, withRifle: true));

    public void BattleAllyDown(AllySoldier a) => _allies.Remove(a);

    public void BattleToast(string text) => _hud?.Toast(text);

    public void BattleReinforce(float x, bool ally)
    {
        if (ally)
        {
            if (_allyReinforcements >= SkirmishRules.MaxReinforcePerSide) return;
            if (_allies.Count >= SkirmishRules.MaxAlliesOnField) return;
            if (GD.Randf() > SkirmishRules.ReinforceChanceAlly) return;
            _allyReinforcements++;
            SpawnAlly(x);
            BattleToast("东北军少量增援赶到");
        }
        else
        {
            if (_enemyReinforcements >= SkirmishRules.MaxReinforcePerSide) return;
            if (_enemies.Count >= SkirmishRules.MaxEnemiesOnField) return;
            if (GD.Randf() > SkirmishRules.ReinforceChanceEnemy) return;
            _enemyReinforcements++;
            SpawnEnemy(Mathf.Min(x + 320f, WorldW - 120f), 0f);
            BattleToast("日军呼叫支援（少量）");
        }
    }

    /// <summary>仅供 CaptureSkirmish 验收场景摆位使用，主流程不调用。</summary>
    public void DebugSpawnEnemy(float x) => SpawnEnemy(x, 0f);

    /// <summary>敌兵 Alert() 时调用：折扣概率 + 本关次数上限。</summary>
    public void RequestEnemyReinforcement(float x)
    {
        if (GD.Randf() > SkirmishRules.ReinforceChanceEnemy) return;
        if (_enemyReinforcements >= SkirmishRules.MaxReinforcePerSide) return;
        if (_enemies.Count >= SkirmishRules.MaxEnemiesOnField) return;
        _enemyReinforcements++;
        SpawnEnemy(Mathf.Min(x + 340f, WorldW - 120f), 0f);
        BattleToast("日军呼叫支援（少量）");
    }
}

public partial class JiangqiaoLevel : INpcBattlefield
{
    private readonly List<AllySoldier> _allies = new();
    private int _allyReinforcements;
    private int _enemyReinforcements;

    public Node2D BattleRoot => _game;
    public float BattleGroundY => GroundY;
    public float BattlePlayerX => PlayerX;

    public void InitSkirmish()
    {
        SpawnAlly(PlayerX + 240f);
        SpawnAlly(PlayerX + 410f);
    }

    private void SpawnAlly(float x)
    {
        if (_allies.Count >= SkirmishRules.MaxAlliesOnField) return;
        var a = new AllySoldier(this, new Vector2(x, GroundY));
        _game.AddChild(a);
        _allies.Add(a);
    }

    public IEnumerable<Node2D> BattleFoes()
    {
        foreach (var e in _enemies)
            if (e != null && IsInstanceValid(e)) yield return e;
    }

    public IEnumerable<Node2D> BattleFriends()
    {
        foreach (var a in _allies)
            if (a != null && IsInstanceValid(a) && !a.IsDown) yield return a;
    }

    public Node2D NearestAlly(float x, float range)
    {
        Node2D best = null;
        float bd = range;
        foreach (var a in _allies)
        {
            if (a == null || !IsInstanceValid(a) || a.IsDown) continue;
            float d = Mathf.Abs(a.Position.X - x);
            if (d < bd) { bd = d; best = a; }
        }
        return best;
    }

    public void BattleShot(Vector2 from, Vector2 to, bool fromAlly)
        => _fx.Shoot(from, to, fromAlly ? new Color(1f, 0.90f, 0.66f) : new Color(1f, 0.76f, 0.52f), 120f);

    public void BattleClash(Vector2 at) => _fx.Puff(at, 8f);

    public void BattleCorpse(Vector2 pos, bool ally)
        => _game.AddChild(new CorpseActor(
            ally ? CorpseActor.Faction.NortheastArmy : CorpseActor.Faction.IJA,
            new Vector2(pos.X, GroundY - 2), false, withRifle: true));

    public void BattleAllyDown(AllySoldier a) => _allies.Remove(a);

    public void BattleToast(string text) => _hud?.Toast(text);

    public void BattleReinforce(float x, bool ally)
    {
        if (ally)
        {
            if (_allyReinforcements >= SkirmishRules.MaxReinforcePerSide) return;
            if (_allies.Count >= SkirmishRules.MaxAlliesOnField) return;
            if (GD.Randf() > SkirmishRules.ReinforceChanceAlly) return;
            _allyReinforcements++;
            SpawnAlly(x);
            BattleToast("守军少量增援赶到");
        }
        else
        {
            if (_enemyReinforcements >= SkirmishRules.MaxReinforcePerSide) return;
            if (_enemies.Count >= SkirmishRules.MaxEnemiesOnField) return;
            if (GD.Randf() > SkirmishRules.ReinforceChanceEnemy) return;
            _enemyReinforcements++;
            SpawnEnemy(Mathf.Min(x + 320f, WorldW - 120f), 0f);
            BattleToast("日军呼叫支援（少量）");
        }
    }

    public void RequestEnemyReinforcement(float x)
    {
        if (GD.Randf() > SkirmishRules.ReinforceChanceEnemy) return;
        if (_enemyReinforcements >= SkirmishRules.MaxReinforcePerSide) return;
        if (_enemies.Count >= SkirmishRules.MaxEnemiesOnField) return;
        _enemyReinforcements++;
        SpawnEnemy(Mathf.Min(x + 340f, WorldW - 120f), 0f);
        BattleToast("日军呼叫支援（少量）");
    }
}
