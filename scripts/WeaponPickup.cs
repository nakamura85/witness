using Godot;
using System.Collections.Generic;

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
    private Texture2D _rifleTex;

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
        float buried = sink < 0.45f ? 0f : (sink < 0.80f ? 0.42f : 0.85f);

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

        // v2.9: the old burial depth was 5 pixels, while the whole rifle is
        // only about 10 pixels tall, and the snow blob followed the rifle
        // down instead of staying on the surface. Now the drift does the
        // covering and the rifle barely sinks at all - just enough to sit
        // inside the mound instead of on top of it.
        float sinkDy = buried * 2.5f;

        DrawSetTransform(new Vector2(ox, groundY + sinkDy), rot, new Vector2(sgn, sc));

        // generated watercolor rifle texture - the old polygon gun (a brown
        // slab + grey bar) stuck out like a stick figure among the hand-drawn
        // props. World length still matches the old tip/butt span so the
        // burial drift math below lines up the same.
        _rifleTex ??= GD.Load<Texture2D>("res://assets/art/props/rifle.png");
        var tsz = _rifleTex.GetSize();
        float bodyLen = tipX - buttX;                       // 80 local px
        float bodyH = bodyLen * tsz.Y / tsz.X;
        // broken guns read darker and colder
        var tint = Condition == Kind.Broken
            ? new Color(0.70f, 0.69f, 0.74f, 0.97f)
            : new Color(1f, 1f, 1f, 0.98f);
        DrawTextureRect(_rifleTex, new Rect2(buttX, -bodyH * 0.55f, bodyLen, bodyH), false, tint);

        // frost crust on broken guns, kena the cold hard
        if (Condition == Kind.Broken)
        {
            DrawLine(new Vector2(-6f, -4.6f), new Vector2(14f, -3.4f),
                new Color(0.88f, 0.92f, 0.95f, 0.7f), 2.0f, true);
        }

        // thin settled-snow band along the body: soft falloff blob squashed
        // into a line, placed by hand because DrawSetTransform replaces the
        // whole transform, then back onto the rifle's own transform
        {
            var bandLocal = new Vector2(-2f, 3.2f);
            var bandScaled = new Vector2(bandLocal.X * sgn, bandLocal.Y * sc);
            var bandOffset = bandScaled.Rotated(rot);
            DrawSetTransform(
                new Vector2(ox, groundY + sinkDy) + bandOffset,
                rot,
                new Vector2(sgn * 1.5f, sc * 0.16f));
            DrawTextureRect(SoftSnow.Blob(new Color(0.84f, 0.87f, 0.88f), 0.42f),
                new Rect2(-20f, -20f, 40f, 40f), false);
            DrawSetTransform(new Vector2(ox, groundY + sinkDy), rot, new Vector2(sgn, sc));
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
            // v2.9 rewrite of the burial.
            //
            // The old code sank the rifle a few pixels and drew ONE flat blob
            // over its middle. Two things were wrong with that:
            //   - the rifle is only ~10px tall, so a 5px sink under a 10px
            //     flat blob covered almost nothing. All three tiers looked
            //     identical, which is exactly what the player reported.
            //   - the blob followed the rifle down as it sank, so the cover
            //     never actually got deeper.
            //
            // What a half-buried rifle actually looks like: the snow piles
            // up against the STOCK and the receiver, and only the barrel
            // points out of the drift. So the drift is laid along the
            // rifle's own axis, starting at the butt and reaching toward
            // the muzzle, and how far it reaches is what the burial depth
            // means.
            //
            // The drift stays in the ground snow tone (about 0.74 in
            // daylight, measured off the level), so it merges with the plain
            // instead of looking like a pale blob dropped on it.
            float coverFrac = buried;              // 0.42 = up to the receiver
            float coverPx = buried * 12f;          // how tall the mound gets

            // butt and muzzle in world space, so the drift can follow the
            // rifle no matter which way it is turned
            var origin = new Vector2(ox, groundY + sinkDy);
            Vector2 ButtWorld = origin + new Vector2(buttX * sgn * sc, 0f).Rotated(rot);
            Vector2 MuzzleWorld = origin + new Vector2(tipX * sgn * sc, 0f).Rotated(rot);

            const int steps = 5;
            for (int k = 0; k < steps; k++)
            {
                float t = k / (steps - 1f);
                float along = t * coverFrac;
                Vector2 p = ButtWorld.Lerp(MuzzleWorld, along);
                // the mound is tallest at the butt and thins toward the
                // muzzle, where the barrel finally comes out
                float shrink = 1f - t * 0.45f;
                float h = Mathf.Lerp(0.50f, 0.22f, t);
                float a = Mathf.Lerp(0.55f, 0.26f, t);
                float tone = Mathf.Lerp(0.76f, 0.82f, t);
                DrawSetTransform(new Vector2(p.X, p.Y + coverPx * 0.22f), 0,
                    new Vector2(0.95f * shrink, h));
                DrawTextureRect(SoftSnow.Blob(new Color(tone, tone * 1.02f, tone * 1.02f), a),
                    new Rect2(-20f, -20f, 40f, 40f), false);
            }
            // lit crest along the top of the drift
            Vector2 crest = ButtWorld.Lerp(MuzzleWorld, coverFrac * 0.45f);
            DrawSetTransform(new Vector2(crest.X, crest.Y - coverPx * 0.18f), 0,
                new Vector2(0.85f, 0.20f));
            DrawTextureRect(SoftSnow.Blob(new Color(0.84f, 0.87f, 0.88f), 0.34f),
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
