using System.Collections.Generic;
using Godot;

namespace Witness;

/// <summary>
/// Corpse sprite actor. Body now uses generated watercolor corpse sprites
/// (same art style as the uniform character sprites), the old code-drawn
/// body kena removed because it read like a flat color blob on screen.
/// Kept from old version: contact shadow, dropped rifle, snow drift over feet.
/// Poses: prone (face-down, 70%), supine (face-up, 25%), side lying (5%, reuse prone tex).
/// </summary>
public partial class CorpseActor : Node2D
{
    public enum Faction { IJA, NortheastArmy, Refugee }
    private enum Pose { Prone, Supine, SideLying }

    private readonly Faction _faction;
    private readonly Pose _pose;
    private readonly float _seed;
    private readonly bool _headRight;
    private readonly float _rotJitter;

    private static readonly Dictionary<string, Texture2D> _texCache = new();

    // world body length per texture, hand-tuned so both factions read same scale
    private static readonly Dictionary<string, float> _texLen = new()
    {
        ["corpse_na_prone"] = 128f,
        ["corpse_na_supine"] = 122f,
        ["corpse_ija_prone"] = 138f,
        ["corpse_ija_supine"] = 122f,
    };

    public CorpseActor(Faction faction, Vector2 position, bool facingRight, float rotJitter = -1f)
    {
        _faction = faction;
        _headRight = facingRight;
        var rng = new RandomNumberGenerator();
        rng.Randomize();
        _seed = rng.Randf();

        float r = rng.Randf();
        if (r < 0.70f) _pose = Pose.Prone;
        else if (r < 0.95f) _pose = Pose.Supine;
        else _pose = Pose.SideLying;

        // pre-placed corpses pass fixed jitter so level layout is stable, deaths roll random
        _rotJitter = rotJitter >= 0f ? rotJitter : rng.RandfRange(-0.26f, 0.26f);

        Position = position;
        ZIndex = 0;
        Modulate = new Color(1f, 0.96f, 0.94f, 1f);
        QueueRedraw();
    }

    private string TexName()
    {
        string prefix = _faction == Faction.IJA ? "corpse_ija" : "corpse_na";
        return _pose == Pose.Supine ? $"{prefix}_supine" : $"{prefix}_prone";
    }

    private Texture2D Tex()
    {
        string n = TexName();
        if (_texCache.TryGetValue(n, out var t)) return t;
        t = GD.Load<Texture2D>($"res://assets/art/sprites/{n}.png");
        _texCache[n] = t;
        return t;
    }

    public override void _Draw()
    {
        var t = Tex();
        if (t == null) return;
        var sz = t.GetSize();
        float len = _texLen[TexName()];
        float h = len * sz.Y / sz.X;
        DrawContactShadow(len);
        // small tilt only, head side flipped by scale X, so the body always lies on the ground line
        DrawSetTransform(Vector2.Zero, -_rotJitter, new Vector2(HeadSign(), 1f));
        DrawTextureRect(t, new Rect2(0f, -h + 3f, len, h), false);
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);

        DrawDroppedRifle();
        DrawSnowDrift();
    }

    private void DrawContactShadow(float len)
    {
        var center = new Vector2(HeadSign() * len * 0.45f, 1f);
        DrawSetTransform(center, 0, new Vector2(1.9f, 0.30f));
        DrawCircle(Vector2.Zero, len * 0.42f, new Color(0.10f, 0.09f, 0.08f, 0.20f));
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }

    private float HeadSign() => _headRight ? 1f : -1f;

    // rifle drops near the torso side, half buried in snow, fixed per seed
    private void DrawDroppedRifle()
    {
        if (_faction == Faction.Refugee) return;
        var rng = new RandomNumberGenerator();
        rng.Seed = (ulong)(Mathf.RoundToInt(_seed * 1000000f) + 7);

        float dropDist = rng.RandfRange(30f, 100f);
        float side = rng.Randf() > 0.30f ? 1f : 0.4f;
        float rx = HeadSign() * dropDist * side;
        float ry = rng.RandfRange(4f, 13f);
        float ang = rng.RandfRange(-0.56f, 0.56f);

        DrawSetTransform(new Vector2(rx, ry), ang, Vector2.One);
        var wood = new Color(0.34f, 0.25f, 0.17f, 0.96f);
        var metal = new Color(0.24f, 0.26f, 0.29f, 0.96f);
        var stock = new[]
        {
            new Vector2(-36f, 0f), new Vector2(-14f, -4.6f), new Vector2(-8f, -1.2f),
            new Vector2(-10f, 3.4f), new Vector2(-34f, 4.6f),
        };
        DrawColoredPolygon(stock, wood);
        DrawRect(new Rect2(-8f, -3.4f, 46f, 3.8f), metal);
        DrawRect(new Rect2(8f, -5.2f, 10f, 2.4f), metal);
        DrawCircle(new Vector2(1.5f, 0.8f), 1.7f, metal);
        DrawRect(new Rect2(-32f, 2.6f, 64f, 2.8f), new Color(0.93f, 0.95f, 0.96f, 0.82f));
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }

    // snow drift covering the feet, body looks like lying there for a while already
    private void DrawSnowDrift()
    {
        var drift = new Vector2(HeadSign() * 12f, 2f);
        DrawSetTransform(drift, 0, new Vector2(2.1f, 0.55f));
        DrawCircle(Vector2.Zero, 15f, new Color(0.90f, 0.93f, 0.94f, 0.92f));
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }
}
