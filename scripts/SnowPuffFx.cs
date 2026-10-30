using Godot;
using System.Collections.Generic;

namespace Witness;

/// <summary>
/// One-shot snow burst where something lands (v3.46). Soft radial blobs that
/// expand and fade, plus a few kicked-up clods. Drawn with SoftSnow.Blob so
/// nothing here carries a hard particle edge on open snow - the burst reads
/// as powder thrown up and settling, not as confetti.
///
/// Drawn as a level child (default Z, added after the actors) so it flashes
/// over the boots for a moment and is gone.
/// </summary>
public partial class SnowPuffFx : Node2D
{
    private const float Life = 0.7f;

    private float _t;
    private readonly float _power;          // 0.6..1.6, scales size & spread
    private readonly List<Vector2> _clodVel = new();

    public SnowPuffFx(float power)
    {
        _power = Mathf.Clamp(power, 0.6f, 1.6f);
        // pre-roll clod velocities: low arcs out to both sides, none going up
        // much - snow kicked by boots travels sideways, not skyward
        var rng = new RandomNumberGenerator();
        rng.Randomize();
        int n = 6 + (int)(_power * 3f);
        for (int i = 0; i < n; i++)
        {
            float dir = i % 2 == 0 ? 1f : -1f;
            float sp = (55f + rng.Randf() * 75f) * _power;
            float vy = -(20f + rng.Randf() * 55f);
            _clodVel.Add(new Vector2(dir * sp, vy));
        }
    }

    public override void _Process(double delta)
    {
        _t += (float)delta;
        if (_t >= Life) { QueueFree(); return; }
        QueueRedraw();
    }

    public override void _Draw()
    {
        float k = _t / Life;                       // 0..1
        float grow = 0.55f + k * 1.2f;             // expansion
        float fade = (1f - k) * (1f - k);          // ease-out fade

        // three soft lobes: centre burst + two side lobes hugging the snow
        DrawLobe(new Vector2(0f, 0f), 20f * _power * grow, 0.72f * fade);
        DrawLobe(new Vector2(-15f * _power * grow, 2f), 13f * _power * grow, 0.55f * fade);
        DrawLobe(new Vector2(15f * _power * grow, 2f), 13f * _power * grow, 0.55f * fade);

        // clods: small darker flecks arcing out and dropping back to the snow
        float clodFade = 1f - k;
        if (clodFade > 0.05f)
        {
            for (int i = 0; i < _clodVel.Count; i++)
            {
                var v = _clodVel[i];
                var p = new Vector2(v.X * _t, v.Y * _t + 150f * _t * _t); // gravity
                DrawCircle(p, 1.6f, new Color(0.52f, 0.51f, 0.49f, 0.7f * clodFade));
            }
        }
    }

    private void DrawLobe(Vector2 c, float r, float a)
    {
        if (a <= 0.015f || r <= 0.5f) return;
        var tex = SoftSnow.Blob(new Color(0.96f, 0.97f, 0.97f), a);
        // SoftSnow.Blob is a 64px texture; squash vertically so the burst
        // hugs the ground instead of ballooning into a ball
        float s = r * 2f / 64f;
        DrawSetTransform(c, 0f, new Vector2(s, s * 0.6f));
        DrawTextureRect(tex, new Rect2(-32f, -32f, 64f, 64f), false);
        DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
    }
}
