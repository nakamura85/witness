using Godot;

namespace Witness;

/// <summary>
/// One shadow kit for the whole level, so every silhouette (player, enemy,
/// corpse, rifle, crate, bunker, testimony box) reads the same tone and the
/// same light direction. Winter sun hangs low: morning light from the east
/// (screen right) throws shadows west (left), flips after noon. Night got
/// only starlight, shadow goes soft and faint. DayT is fed by the level clock.
/// </summary>
public static class SnowShadow
{
    public static float DayT = 12f;
    public static float SunriseH = 6.533f;
    public static float SunsetH = 16.483f;

    private static readonly Color Ink = new(0.12f, 0.13f, 0.16f);

    /// <summary>light comes from right in the morning (shadow to the left), flips after midday</summary>
    public static float DirX()
    {
        float t = DayT;
        if (t < SunriseH - 0.5f || t > SunsetH + 0.5f) return 0f;
        if (t < 11.5f) return -1f;
        if (t > 13.5f) return 1f;
        return 0f;
    }

    private static float Strength()
    {
        float t = DayT;
        if (t < SunriseH - 0.5f || t > SunsetH + 0.5f) return 0.45f;
        if (t < SunriseH + 1.2f || t > SunsetH - 1.2f) return 0.72f;
        return 1f;
    }

    /// <summary>soft two-layer ground ellipse, hugs the object feet</summary>
    public static void Draw(CanvasItem ci, Vector2 ground, float radius,
        float baseAlpha = 0.26f, float stretch = 1f)
    {
        float k = Strength();
        float dx = DirX() * radius * 0.34f * stretch;
        ci.DrawSetTransform(ground + new Vector2(dx, 0f), 0, new Vector2(stretch, 0.24f));
        ci.DrawCircle(Vector2.Zero, radius, new Color(Ink.R, Ink.G, Ink.B, baseAlpha * k));
        ci.DrawSetTransform(ground + new Vector2(dx * 1.9f, 0f), 0, new Vector2(stretch * 0.6f, 0.15f));
        ci.DrawCircle(Vector2.Zero, radius * 0.72f,
            new Color(Ink.R, Ink.G, Ink.B, baseAlpha * k * 0.5f));
        ci.DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }
}
