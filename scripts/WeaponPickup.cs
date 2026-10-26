using Godot;

namespace Witness;

/// <summary>
/// Rifle dropped by a dead soldier, lying in the snow. Player walks near and
/// presses E to loot. Real-war flavor: some rifles still got a few rounds,
/// some kena frost/bullet damage and cannot fire at all.
/// </summary>
public partial class WeaponPickup : Node2D
{
    public enum Kind { Usable, Broken }

    public Kind Condition { get; }
    public int Ammo { get; }              // rounds left in the looted rifle
    public bool Looted { get; private set; }

    private float _seed;
    private float _glintT;

    public WeaponPickup(Kind kind, int ammo)
    {
        Condition = kind;
        Ammo = ammo;
        ZIndex = 1;
        var rng = new RandomNumberGenerator();
        rng.Randomize();
        _seed = rng.Randf();
    }

    /// <summary>
    /// Fixes the seed, so the angle, the slide and the burial depth are
    /// reproducible. The verification scene uses this to lay out the same
    /// rifles over and over.
    /// </summary>
    public void SetSeed(float seed)
    {
        _seed = seed;
        QueueRedraw();
    }

    public void MarkLooted()
    {
        Looted = true;
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        // soft glint so the player notices loot in the snow, stops once taken
        if (Looted) return;
        _glintT += (float)delta;
        if ((int)(_glintT * 2f) != (int)((_glintT - (float)delta) * 2f))
            QueueRedraw();
    }

    public override void _Draw()
    {
        var rng = new RandomNumberGenerator();
        rng.Seed = (ulong)(Mathf.RoundToInt(_seed * 1000000f) + 31);
        // a dropped rifle lands messy: big random angle, random slide offset, random scale
        float ang = rng.RandfRange(-0.85f, 0.85f);
        bool flip = rng.Randf() > 0.5f;
        float ox = rng.RandfRange(-13f, 13f);
        float oy = rng.RandfRange(-2f, 4f);
        float sc = rng.RandfRange(0.90f, 1.08f);

        // v2.7: some guns sit half buried in the snow, some lie flat on it.
        // In Nenjiang in November 1931 the snow was already deep, so a
        // dropped rifle usually sinks in up to the receiver and only shows
        // a bit of barrel. The depth comes off a fixed seed, so it never
        // jitters frame to frame.
        float sink = rng.Randf();
        // 0 = lying flat on the snow, 1 = sunk until only the barrel shows
        float buried = sink < 0.45f ? 0f : (sink < 0.80f ? 0.35f : 0.68f);

        // v2.7 floating gun root fix:
        // the old way rotated +-0.85 rad around the geometric centre (ox, oy),
        // so at a big angle the muzzle end lifted into the air and the stock
        // hung in the void - it looked like a rifle stuck in the snow badly.
        // now: rotate first, then align the **lowest point** of the gun with
        // the snow, so the gun really lies on the ground. The buried ones
        // then sink down as a whole.
        float rot = flip ? -ang : ang;
        float sgn = flip ? -sc : sc;

        // key points on the gun body (local space, before rotation):
        // muzzle +42, stock -38, lower edge +5
        const float tipX = 42f, buttX = -38f, bottomY = 5f;
        // y of each point after the rotation (relative to the rotation centre)
        float tipY = Mathf.Sin(rot) * tipX;
        float buttY = Mathf.Sin(rot) * buttX;
        float bottomRotY = Mathf.Sin(rot) * 0f + Mathf.Cos(rot) * bottomY;
        float lowest = Mathf.Max(Mathf.Max(tipY, buttY), bottomRotY);
        // drop the lowest point onto y = +4 (a bit below the centre line)
        float groundY = 4f - lowest;

        // contact shadow: sits right under the gun, on the snow
        SnowShadow.Draw(this, new Vector2(ox, groundY + 1f), 40f * sc, 0.24f);

        // v2.8: a buried rifle sits lower in the snow - the drift is drawn on
        // top of it, so the rifle has to sink for the cover to reach the
        // receiver. Without this the gun stayed at ground level and the
        // snow blob was only a puff next to it.
        float sinkDy = buried * 5f;

        DrawSetTransform(new Vector2(ox, groundY + sinkDy), rot, new Vector2(sgn, sc));
        var wood = Condition == Kind.Broken
            ? new Color(0.28f, 0.21f, 0.15f, 0.96f)
            : new Color(0.36f, 0.27f, 0.18f, 0.96f);
        var metal = Condition == Kind.Broken
            ? new Color(0.20f, 0.21f, 0.23f, 0.96f)
            : new Color(0.26f, 0.28f, 0.31f, 0.96f);

        // stock + barrel, same silhouette as the corpse rifle so it reads as one
        var stock = new[]
        {
            new Vector2(-38f, 0f), new Vector2(-15f, -5f), new Vector2(-8f, -1.2f),
            new Vector2(-11f, 3.6f), new Vector2(-36f, 5f),
        };
        DrawColoredPolygon(stock, wood);
        DrawRect(new Rect2(-8f, -3.6f, 50f, 4f), metal);
        DrawRect(new Rect2(8f, -5.6f, 11f, 2.6f), metal);
        DrawCircle(new Vector2(2f, 0.8f), 1.8f, metal);

        // v2.8 root fix: the snow lying on the rifle used to be a plain
        // DrawRect - a hard white bar 68px long and 3px wide. On the flat
        // snow that read as a strip of paper taped to the rifle, and it was
        // the bright tilted slab the player kept pointing at.
        // It is now a soft falloff blob, squashed into a thin band, so it
        // has no rim and fades out at both ends like settled snow.
        //
        // DrawSetTransform replaces the transform instead of adding to it,
        // so the band has to be placed by hand: the offset is scaled and
        // rotated the same way the rifle itself is, then the whole thing
        // goes back through the rifle's own transform.
        {
            var bandLocal = new Vector2(-2f, 3.2f);
            var bandScaled = new Vector2(bandLocal.X * sgn, bandLocal.Y * sc);
            var bandOffset = bandScaled.Rotated(rot);
            DrawSetTransform(
                new Vector2(ox, groundY + sinkDy) + bandOffset,
                rot,
                new Vector2(sgn * 1.5f, sc * 0.16f));
            DrawTextureRect(SoftSnow.Blob(new Color(0.93f, 0.95f, 0.96f), 0.75f),
                new Rect2(-20f, -20f, 40f, 40f), false);
            // back to the rifle's own transform for the rest of the body
            DrawSetTransform(new Vector2(ox, groundY + sinkDy), rot, new Vector2(sgn, sc));
        }

        // frost crust on broken guns, kena the cold hard
        if (Condition == Kind.Broken)
        {
            DrawLine(new Vector2(-6f, -4.6f), new Vector2(14f, -3.4f),
                new Color(0.88f, 0.92f, 0.95f, 0.8f), 2.2f, true);
            DrawLine(new Vector2(24f, -3.2f), new Vector2(38f, -6.5f),
                new Color(0.26f, 0.20f, 0.15f, 0.9f), 2.6f, true); // bent barrel tip
        }
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);

        // glint blink
        float blink = Mathf.Sin(_glintT * 4.2f);
        if (blink > 0.55f)
        {
            var g = new Color(1f, 0.98f, 0.85f, (blink - 0.55f) * 1.6f);
            DrawLine(new Vector2(-10f, -10f), new Vector2(-2f, -2f), g, 2f, true);
            DrawLine(new Vector2(6f, -12f), new Vector2(9f, -9f), g, 1.6f, true);
        }

        // v2.7: burying.
        // A buried gun gets snow drifted over its lower half, a gun lying
        // flat only gets a thin layer of snow resting on it.
        //
        // v2.8 root fix. The first attempt drew a filled polygon in the
        // gun's own rotated space, and the second attempt drew the same
        // polygon in world space. Both failed the same way: the fill is a
        // hard-edged rectangle, so on the flat snow it read as a sheet of
        // white paper lying next to the rifle, not as snow. The filled
        // bottom edge also crossed the darker ground bands and cut them.
        //
        // What actually works is the same trick the rest of the scene uses
        // (SoftSnow): a radial falloff sprite has no rim, so it melts into
        // whatever it is drawn over. Two blobs:
        //   - a wide one in the ground snow colour, which veils the buried
        //     half of the rifle and blends with the snow underneath;
        //   - a smaller brighter one on top, the lit crest of the drift.
        // Both are drawn in world space and stay horizontal - snow lies flat,
        // it does not tilt with the rifle.
        if (buried > 0f)
        {
            // how deep the snow sits over the gun: the deeper it is, the
            // higher up the barrel the drift reaches
            float depth = buried * 7f;
            float cx = ox;

            // the drift sits on the snow line and reaches up over the lower
            // part of the rifle. It must not swallow the whole gun - a rifle
            // buried past recognition is not a rifle any more.
            float cy = groundY + sinkDy - depth * 0.10f;

            // the drift itself, in the colour of the snow on the ground
            DrawSetTransform(new Vector2(cx, cy), 0, new Vector2(1.05f, 0.24f));
            DrawTextureRect(SoftSnow.Blob(new Color(0.855f, 0.875f, 0.875f), 0.85f),
                new Rect2(-20f, -20f, 40f, 40f), false);
            // the lit crest on top of the drift
            DrawSetTransform(new Vector2(cx, cy - depth * 0.16f), 0, new Vector2(0.80f, 0.14f));
            DrawTextureRect(SoftSnow.Blob(new Color(0.93f, 0.95f, 0.96f), 0.55f),
                new Rect2(-20f, -20f, 40f, 40f), false);
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        }
        else
        {
            // gun lying flat on the snow: the thin layer of snow on its body
            // is the soft band drawn with the rifle above, so nothing extra
            // is needed here. A rifle on flat snow just needs its contact
            // shadow, which is already drawn.
        }
    }
}
