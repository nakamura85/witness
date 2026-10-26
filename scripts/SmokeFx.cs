using Godot;
using System.Collections.Generic;

namespace Witness;

/// <summary>
/// War smoke and fire, animated. Nothing here is a frozen painting: every
/// column of smoke keeps breathing, drifting and puffing, and the fires in
/// the ruins flicker and spit embers.
///
/// Art direction: hand drawn cel style like the rest of the game. Smoke puffs
/// are flat light blobs with a dark ink rim and a shaded belly, flames get a
/// dark outer tongue, an orange middle and a pale core, plus a warm glow on
/// the snow. Palette is desaturated winter grey-blue for smoke so it never
/// fights the characters.
/// </summary>
public partial class SmokeFx : Node2D
{
    public struct Source
    {
        public Vector2 Pos;        // where the column comes from
        public float Size;         // puff size multiplier
        public float Strength;     // 0..1 how thick the smoke is
        public float Fire;         // 0..1 flame burning at the base
        public float Rise;         // how high the column climbs
        public float Drift;        // wind push, positive = to the right
        public float Phase;        // per source offset so columns are not in sync
        public Color Tint;         // smoke colour, lighter for domestic fire
    }

    private readonly List<Source> _sources = new();
    private float _t;

    public void Add(Source s) => _sources.Add(s);

    public override void _Process(double delta)
    {
        _t += (float)delta;
        QueueRedraw();
    }

    public override void _Draw()
    {
        foreach (var s in _sources)
        {
            if (s.Fire > 0.01f) DrawFire(s);
            if (s.Strength > 0.01f) DrawColumn(s);
        }
    }

    // ================= smoke =================

    private void DrawColumn(Source s)
    {
        const int puffs = 11;
        var pxy = new Vector2[puffs];
        var pr = new float[puffs];
        var pa = new float[puffs];
        var pu = new float[puffs];

        for (int i = 0; i < puffs; i++)
        {
            float u = Mathf.PosMod(_t * 0.11f + i / (float)puffs + s.Phase, 1f);
            float wob = Mathf.Sin((_t + i * 2.3f) * 0.8f) * 2.2f;
            pxy[i] = new Vector2(s.Pos.X + s.Drift * u * u * 34f + wob, s.Pos.Y - u * s.Rise);
            pr[i] = (6f + u * 22f) * s.Size;
            pa[i] = Mathf.Min(1f, s.Strength * Mathf.Sin(Mathf.Pi * Mathf.Clamp(u * 1.12f, 0f, 1f)) * 1.15f);
            pu[i] = u;
        }

        // draw order: oldest (highest, faintest) first so fresh puffs sit on top
        var order = new int[puffs];
        for (int i = 0; i < puffs; i++) order[i] = i;
        System.Array.Sort(order, (a, b) => pu[b].CompareTo(pu[a]));

        var body = new Color(s.Tint.R, s.Tint.G, s.Tint.B);
        var shade = new Color(s.Tint.R * 0.74f, s.Tint.G * 0.77f, s.Tint.B * 0.83f);
        var ink = new Color(0.22f, 0.21f, 0.20f);
        var lite = new Color(Mathf.Min(1f, s.Tint.R + 0.09f), Mathf.Min(1f, s.Tint.G + 0.09f),
            Mathf.Min(1f, s.Tint.B + 0.09f));

        // pass 1: every puff gets its ink rim first, so the rims of the puffs
        // buried inside the column get covered by the bodies afterwards. Only
        // the outer silhouette keeps the line - that is the cel look.
        for (int k = 0; k < puffs; k++)
        {
            int i = order[k];
            if (pa[i] <= 0.02f) continue;
            DrawSetTransform(pxy[i], 0f, new Vector2(1f, 0.86f));
            DrawCircle(Vector2.Zero, pr[i] + 1.6f, new Color(ink.R, ink.G, ink.B, pa[i] * 0.55f));
        }
        DrawSetTransform(Vector2.Zero, 0f, Vector2.One);

        // pass 2: flat bodies
        for (int k = 0; k < puffs; k++)
        {
            int i = order[k];
            if (pa[i] <= 0.02f) continue;
            DrawSetTransform(pxy[i], 0f, new Vector2(1f, 0.86f));
            DrawCircle(Vector2.Zero, pr[i] * 0.94f, new Color(body.R, body.G, body.B, pa[i]));
        }
        DrawSetTransform(Vector2.Zero, 0f, Vector2.One);

        // pass 3: shaded belly, plus a lit edge only on the crown of the plume.
        // Giving every puff its own highlight turned the column into a string
        // of pearls, so only the top few puffs catch the light.
        for (int k = 0; k < puffs; k++)
        {
            int i = order[k];
            if (pa[i] <= 0.02f) continue;
            float r = pr[i];
            DrawCircle(pxy[i] + new Vector2(-r * 0.20f, r * 0.22f), r * 0.62f,
                new Color(shade.R, shade.G, shade.B, pa[i] * 0.5f));
            if (pu[i] < 0.62f) continue;
            DrawCircle(pxy[i] + new Vector2(r * 0.22f, -r * 0.26f), r * 0.26f,
                new Color(lite.R, lite.G, lite.B, pa[i] * 0.45f));
        }
    }

    // ================= fire =================

    private void DrawFire(Source s)
    {
        // warm light pooling on the snow
        DrawSetTransform(new Vector2(s.Pos.X, s.Pos.Y + 1f), 0, new Vector2(1.7f, 0.40f));
        DrawCircle(Vector2.Zero, 20f * s.Size, new Color(1f, 0.52f, 0.16f, 0.22f * s.Fire));
        DrawCircle(Vector2.Zero, 10f * s.Size, new Color(1f, 0.72f, 0.32f, 0.26f * s.Fire));
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);

        for (int k = 0; k < 3; k++)
        {
            float ph = _t * 5.4f + k * 2.3f + s.Phase * 11f;
            float h = (13f + Mathf.Sin(ph) * 4.5f + k * 3.2f) * s.Size;
            float w = (5.2f + Mathf.Cos(ph * 0.77f) * 1.5f) * s.Size;
            float x = s.Pos.X + (k - 1) * 7.5f * s.Size + Mathf.Sin(ph * 0.5f) * 1.4f;
            float y0 = s.Pos.Y;

            var tongue = new[]
            {
                new Vector2(x - w, y0),
                new Vector2(x - w * 0.46f, y0 - h * 0.52f),
                new Vector2(x + Mathf.Sin(ph * 1.31f) * 2.4f, y0 - h),
                new Vector2(x + w * 0.54f, y0 - h * 0.48f),
                new Vector2(x + w, y0),
            };
            DrawColoredPolygon(tongue, new Color(0.48f, 0.14f, 0.04f, 0.80f * s.Fire));

            var mid = new[]
            {
                new Vector2(x - w * 0.62f, y0),
                new Vector2(x, y0 - h * 0.68f),
                new Vector2(x + w * 0.62f, y0),
            };
            DrawColoredPolygon(mid, new Color(1f, 0.42f, 0.10f, 0.92f * s.Fire));

            var core = new[]
            {
                new Vector2(x - w * 0.30f, y0),
                new Vector2(x, y0 - h * 0.40f),
                new Vector2(x + w * 0.30f, y0),
            };
            DrawColoredPolygon(core, new Color(1f, 0.88f, 0.52f, 0.95f * s.Fire));
        }

        // embers drifting up and dying out
        for (int k = 0; k < 6; k++)
        {
            float u = Mathf.PosMod(_t * 0.42f + k / 6f + s.Phase * 3.1f, 1f);
            float x = s.Pos.X + Mathf.Sin((_t + k * 3.7f) * 1.1f) * (4f + u * 16f);
            float y = s.Pos.Y - u * 34f * s.Size;
            float a = s.Fire * (1f - u) * 0.9f;
            if (a <= 0.03f) continue;
            DrawCircle(new Vector2(x, y), Mathf.Max(0.7f, 1.5f * (1f - u) * s.Size),
                new Color(1f, 0.70f - u * 0.35f, 0.28f, a));
        }
    }
}
