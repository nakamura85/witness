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
    public enum Pose { Prone, Supine, SideLying }

    private readonly Faction _faction;
    private readonly Pose _pose;
    private readonly float _seed;
    private readonly bool _headRight;
    private readonly float _rotJitter;
    private readonly bool _withRifle;

    private static readonly Dictionary<string, Texture2D> _texCache = new();

    // world body length per texture, hand-tuned so both factions read same scale
    // v3.17: a body lying down should read about as long as the standing
    // soldier is tall (108px), not longer. At 122-138 a body next to anything
    // smaller than it read as a giant. Trimmed about 10%.
    // v3.19: the soldier is 94px now, so the bodies come down with him.
    private static readonly Dictionary<string, float> _texLen = new()
    {
        ["corpse_na_prone"] = 101f,
        ["corpse_na_supine"] = 96f,
        ["corpse_ija_prone"] = 108f,
        ["corpse_ija_supine"] = 97f,
    };

    /// <summary>
    /// v3.19: the body paintings are not horizontal. Measuring the bottom
    /// contour of each texture gives -4.1 degrees for the northeast army
    /// supine and +10.7 for the japanese army supine - the illustrator drew
    /// them lying diagonally, which reads fine as a standalone picture but
    /// wrong in the scene, where a body has to lie flat on the snow. On top
    /// of that the game added its own random tilt of up to 0.22 rad, so the
    /// two stacked and the body looked like it was sliding down a slope.
    ///
    /// v3.21: these are the measured body axis angles, and the rotation is
    /// applied with the opposite sign (and flipped when the sprite is
    /// mirrored, because mirroring swaps which end is the head).
    ///
    /// v3.19 tried to cancel this and got it wrong twice over: the numbers
    /// came from a bottom contour scan instead of the body axis, and the
    /// sign was inverted, so the compensation was ADDED to the angle the
    /// painting already carries. The japanese army supine body is drawn at
    /// 18 degrees and came out rendered at nearly 29 - a casualty propped
    /// up on one elbow instead of lying in the snow.
    /// </summary>
    private static readonly Dictionary<string, float> _texTilt = new()
    {
        ["corpse_na_supine"] = 1.07f,
        ["corpse_na_prone"] = 1.04f,
        ["corpse_ija_prone"] = 3.30f,
        ["corpse_ija_supine"] = 18.10f,
    };

    public CorpseActor(Faction faction, Vector2 position, bool facingRight, float rotJitter = -1f,
        bool withRifle = true, Pose? fixedPose = null)
    {
        _faction = faction;
        _headRight = facingRight;
        _withRifle = withRifle;
        var rng = new RandomNumberGenerator();
        rng.Randomize();
        _seed = rng.Randf();

        // v3.21: a corpse placed by the level designer has to keep the pose
        // it was placed in. Rolling it at random meant the same body showed
        // up face up on one run and face down on the next, so a spot that
        // looked right in one playthrough looked broken in the next.
        if (fixedPose.HasValue)
        {
            _pose = fixedPose.Value;
        }
        else
        {
            float r = rng.Randf();
            if (r < 0.70f) _pose = Pose.Prone;
            else if (r < 0.95f) _pose = Pose.Supine;
            else _pose = Pose.SideLying;
        }

        // pre-placed corpses pass fixed jitter so level layout is stable, deaths roll random.
        // small tilt only + pivot at body center, else one end floats 30px above the snow
        // v3.19: the jitter used to reach 0.22 rad on its own. On top of the
        // angle the paintings already carry that was what made the bodies
        // look like they were sliding downhill. It is now a gentle +-3.5
        // degrees, enough to keep the field from looking stamped.
        _rotJitter = rotJitter >= 0f ? rotJitter : rng.RandfRange(-0.06f, 0.06f);

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
        string tn = TexName();
        float len = _texLen[tn];
        float h = len * sz.Y / sz.X;
        DrawContactShadow(len);
        // v3.21: the pivot is the body centre. It used to sit at the head
        // end, so every degree of tilt swung the whole body like a see-saw
        // and lifted the feet clear of the snow.
        //
        // The rotation cancels the angle the painting itself carries: a
        // positive Godot angle swings the right hand end down, so the
        // measured axis angle goes in with the opposite sign, and it flips
        // again when the sprite is mirrored because that swaps which end
        // holds the head.
        float rot = -Mathf.DegToRad(_texTilt[tn]) * HeadSign() + _rotJitter;
        DrawSetTransform(new Vector2(0f, -h * 0.5f + 6f), rot, new Vector2(HeadSign(), 1f));
        DrawTextureRect(t, new Rect2(-len * 0.5f, -h * 0.5f, len, h), false);
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);

        if (_withRifle) DrawDroppedRifle();
        DrawSnowDrift(len);
    }

    private void DrawContactShadow(float len)
    {
        SnowShadow.Draw(this, new Vector2(HeadSign() * len * 0.12f, 1f), len * 0.34f, 0.22f);
    }

    private float HeadSign() => _headRight ? 1f : -1f;

    // rifle drops near the torso side, half buried in snow, fixed per seed
    private void DrawDroppedRifle()
    {
        if (_faction == Faction.Refugee) return;
        var rng = new RandomNumberGenerator();
        rng.Seed = (ulong)(Mathf.RoundToInt(_seed * 1000000f) + 7);

        float dropDist = rng.RandfRange(34f, 110f);
        float side = rng.Randf() > 0.30f ? 1f : 0.4f;
        float rx = HeadSign() * dropDist * side;
        float ry = rng.RandfRange(4f, 15f);
        // v3.18: the tilt was up to 0.85 rad, almost 50 degrees. A dropped
        // rifle lying in snow does not stand up like a stake - it lies flat
        // or leans a little, and the steep angle made it read as a pole
        // jammed into the ground. The angle is now a shallow 22 degrees at
        // the most, so the weapon always reads as lying on the snow.
        float ang = rng.RandfRange(-0.38f, 0.38f);

        // v2.9: the rifle next to a body gets the same three burial tiers
        // as a loose pickup, so the two kinds of dropped weapon read as
        // one system instead of two different props.
        float sink = rng.Randf();
        float buried = sink < 0.45f ? 0f : (sink < 0.80f ? 0.42f : 0.85f);

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
        // v2.9 root fix. This was a plain DrawRect: a hard white bar 64px
        // long and 2.8px wide. It tilts with the rifle, so on flat snow it
        // read as a strip of white paper taped to the weapon. The same bug
        // was already fixed on the loose pickups in v2.8, but this second
        // copy on the corpse was missed. It is now the soft falloff band.
        DrawSetTransform(new Vector2(rx, ry), ang, new Vector2(1.4f, 0.15f));
        DrawTextureRect(SoftSnow.Blob(new Color(0.84f, 0.87f, 0.88f), 0.42f),
            new Rect2(-20f, -20f, 40f, 40f), false);
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);

        // v2.9: the drift over the buried part, laid along the rifle's axis
        // from the stock toward the muzzle, the same way the loose pickups
        // do it.
        if (buried > 0f)
        {
            var origin = new Vector2(rx, ry);
            Vector2 butt = origin + new Vector2(-36f, 0f).Rotated(ang);
            Vector2 muzzle = origin + new Vector2(38f, 0f).Rotated(ang);
            const int steps = 5;
            for (int k = 0; k < steps; k++)
            {
                float t = k / (steps - 1f);
                Vector2 p = butt.Lerp(muzzle, t * buried);
                float shrink = 1f - t * 0.45f;
                float h = Mathf.Lerp(0.50f, 0.22f, t);
                float a = Mathf.Lerp(0.55f, 0.26f, t);
                float tone = Mathf.Lerp(0.76f, 0.82f, t);
                DrawSetTransform(new Vector2(p.X, p.Y + buried * 2.6f), 0,
                    new Vector2(0.95f * shrink, h));
                DrawTextureRect(SoftSnow.Blob(new Color(tone, tone * 1.02f, tone * 1.02f), a),
                    new Rect2(-20f, -20f, 40f, 40f), false);
            }
            Vector2 crest = butt.Lerp(muzzle, buried * 0.45f);
            DrawSetTransform(new Vector2(crest.X, crest.Y - buried * 2.2f), 0,
                new Vector2(0.85f, 0.20f));
            DrawTextureRect(SoftSnow.Blob(new Color(0.84f, 0.87f, 0.88f), 0.34f),
                new Rect2(-20f, -20f, 40f, 40f), false);
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        }
    }

    // two low drifts hugging the body (head end + legs end) so the corpse
    // reads as sunk into the open snowfield, not laid on top of a white pad
    private void DrawSnowDrift(float len)
    {
        // legs end: the deeper drift (body slid feet-first into the snow)
        // v2.8: radial falloff instead of a hard rimmed circle, so the drift
        // melts into the snow with no edge to catch the light
        var drift = new Vector2(HeadSign() * len * 0.30f, 5f);
        DrawSetTransform(drift, 0, new Vector2(1.9f, 0.50f));
        DrawTextureRect(SoftSnow.Blob(new Color(0.90f, 0.93f, 0.94f), 0.85f),
            new Rect2(-20f, -20f, 40f, 40f), false);
        DrawSetTransform(drift + new Vector2(HeadSign() * 6f, 2f), 0, new Vector2(1.2f, 0.38f));
        DrawTextureRect(SoftSnow.Blob(new Color(0.94f, 0.96f, 0.96f), 0.75f),
            new Rect2(-20f, -20f, 40f, 40f), false);
        // head end: a lighter dusting
        DrawSetTransform(new Vector2(-HeadSign() * len * 0.34f, 6f), 0, new Vector2(1.3f, 0.36f));
        DrawTextureRect(SoftSnow.Blob(new Color(0.91f, 0.94f, 0.94f), 0.68f),
            new Rect2(-20f, -20f, 40f, 40f), false);
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }
}
