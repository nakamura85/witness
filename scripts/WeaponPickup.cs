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

    private readonly float _seed;
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
        float ang = rng.RandfRange(-0.30f, 0.30f);
        bool flip = rng.Randf() > 0.5f;

        // contact shadow
        DrawSetTransform(new Vector2(0f, 2f), 0, new Vector2(1.6f, 0.28f));
        DrawCircle(Vector2.Zero, 42f, new Color(0.10f, 0.09f, 0.08f, 0.22f));
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);

        DrawSetTransform(Vector2.Zero, flip ? -ang : ang, new Vector2(flip ? -1f : 1f, 1f));
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
        DrawRect(new Rect2(-34f, 2.8f, 68f, 3f), new Color(0.93f, 0.95f, 0.96f, 0.85f));

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

        // snow settled over the stock
        DrawSetTransform(new Vector2(0f, 3f), 0, new Vector2(1.8f, 0.5f));
        DrawCircle(Vector2.Zero, 16f, new Color(0.90f, 0.93f, 0.94f, 0.85f));
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }
}
