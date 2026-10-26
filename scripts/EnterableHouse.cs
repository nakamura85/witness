using System;
using System.Collections.Generic;
using Godot;

namespace Witness;

/// <summary>
/// A farmhouse standing near the line, half ruined by the war. Player presses E
/// at the door to search inside: ammo, grenades, last letters, clue papers,
/// sometimes a locked ammo chest that needs prying. Loot ids persist in save.
/// </summary>
public partial class EnterableHouse : Node2D
{
    private readonly string _id;
    private readonly Texture2D _tex;
    private readonly float _w;
    private readonly bool _enterable;
    private readonly float _lift;

    public string Id => _id;
    public bool Enterable => _enterable;
    public float DoorX => Position.X + _w * 0.18f;
    public float HalfW => _w * 0.5f;

    private static readonly Dictionary<string, Texture2D> _texCache = new();

    public EnterableHouse(string id, Vector2 pos, string texName, float w,
        bool enterable = true, float lift = 0f)
    {
        _id = id;
        _tex = LoadTex(texName);
        _w = w;
        _enterable = enterable;
        _lift = lift;
        Position = pos;
        // lifted houses sit a step further back in the depth row
        ZIndex = lift > 0f ? -4 : -3;
    }

    private static Texture2D LoadTex(string name)
    {
        if (_texCache.TryGetValue(name, out var t)) return t;
        t = GD.Load<Texture2D>($"res://assets/art/props/{name}.png");
        _texCache[name] = t;
        return t;
    }

    public override void _Draw()
    {
        if (_tex == null) return;
        var sz = _tex.GetSize();
        float h = _w * sz.Y / sz.X;
        // local space: node Position sits ON the ground line, so ground = local y 0
        float gl = 0f;
        // far houses sink slightly into the snow haze to sell the depth row
        var fade = _lift > 0f ? new Color(1f, 1f, 1f, 0.88f) : Colors.White;

        // contact shadow along the base
        // v2.8: this was a hard rimmed DrawCircle. Stretched over the full
        // house width the rim showed up as a pale slab lying on the snow, so
        // it now uses the radial falloff sprite.
        DrawSetTransform(new Vector2(_w * 0.5f, gl - 1f), 0, new Vector2(1.15f, 0.24f));
        DrawTextureRect(SoftSnow.Blob(new Color(0.10f, 0.09f, 0.08f), 0.26f),
            new Rect2(-20f, -20f, 40f, 40f), false);
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);

        // house body, bottom sits on the snow line
        DrawTextureRect(_tex, new Rect2(0f, gl - h, _w, h), false, fade);

        if (!_enterable) return;   // scenery ruin: no door light, nothing to search

        // door: dark opening + faint warm light, marks the enterable spot
        float dx = _w * 0.18f;
        float dw = 26f;
        float dh = 52f;
        DrawRect(new Rect2(dx - dw * 0.5f, gl - dh, dw, dh), new Color(0.10f, 0.08f, 0.07f, 0.96f));
        DrawRect(new Rect2(dx - dw * 0.5f, gl - dh, dw, 5f), new Color(0.30f, 0.24f, 0.18f, 0.9f));
        DrawLine(new Vector2(dx - dw * 0.5f, gl - dh), new Vector2(dx - dw * 0.5f, gl),
            new Color(0.22f, 0.18f, 0.14f, 0.9f), 2f, true);
        DrawLine(new Vector2(dx + dw * 0.5f, gl - dh), new Vector2(dx + dw * 0.5f, gl),
            new Color(0.22f, 0.18f, 0.14f, 0.9f), 2f, true);

        // doorstep snow disturbed
        // v2.8: radial falloff, so the trodden patch has no hard rim
        DrawSetTransform(new Vector2(dx, gl - 1f), 0, new Vector2(1.4f, 0.3f));
        DrawTextureRect(SoftSnow.Blob(new Color(0.86f, 0.89f, 0.91f), 0.7f),
            new Rect2(-20f, -20f, 40f, 40f), false);
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }
}

/// <summary>
/// Interior overlay when searching a house. Paper-chip hotspots, FieldNoteUi
/// reuse for letters and clue papers, chest needs hold-to-pry and can jam.
/// </summary>
public partial class HouseInteriorUi : CanvasLayer
{
    public event Action Closed;
    public event Action<int> GotAmmo;
    public event Action<int> GotGrenade;

    private readonly string _houseId;
    private readonly string _houseTitle;
    private readonly RandomNumberGenerator _rng = new();
    private readonly HashSet<string> _looted;
    private readonly List<Spot> _spots = new();
    private Control _root;
    private Label _hint;
    private Spot _chestSpot;
    private float _pryT;
    private bool _prying;
    private bool _closed;

    public HouseInteriorUi(string houseId, string houseTitle)
    {
        _houseId = houseId;
        _houseTitle = houseTitle;
        Layer = 20;
        _rng.Seed = (ulong)(houseId.GetHashCode() & 0xffffff);
        _looted = GameState.I.S.JiangqiaoLoot;
    }

    private partial class Spot : Button
    {
        public string SpotId;
        public string Kind;        // ammo / grenade / letter / clue / chest / ash
        public string ResultText;
        public bool Done;
        public float PryProgress;  // chest only
    }

    public override void _Ready()
    {
        var dim = new ColorRect { Color = new Color(0.04f, 0.035f, 0.03f, 0.80f) };
        dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(dim);

        _root = new CenterContainer();
        _root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_root);

        var panel = new PanelContainer { CustomMinimumSize = new Vector2(880, 560) };
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.13f, 0.11f, 0.09f),
            BorderColor = new Color(0.24f, 0.20f, 0.16f),
            BorderWidthLeft = 2, BorderWidthRight = 2, BorderWidthTop = 2, BorderWidthBottom = 2,
            ContentMarginLeft = 26, ContentMarginRight = 26, ContentMarginTop = 20, ContentMarginBottom = 18,
            ShadowColor = new Color(0, 0, 0, 0.5f), ShadowSize = 16, ShadowOffset = new Vector2(6, 8),
        };
        panel.AddThemeStyleboxOverride("panel", style);
        _root.AddChild(panel);

        var box = new VBoxContainer { };
        box.AddThemeConstantOverride("separation", 12);
        panel.AddChild(box);

        var title = new Label { Text = $"{_houseTitle} · 屋内" };
        title.AddThemeFontSizeOverride("font_size", 22);
        title.AddThemeColorOverride("font_color", new Color(0.86f, 0.82f, 0.72f));
        box.AddChild(title);

        var rule = new ColorRect { Color = new Color(0.55f, 0.20f, 0.16f, 0.75f), CustomMinimumSize = new Vector2(0, 2) };
        box.AddChild(rule);

        // interior floor: item chips laid out in a wrap flow, feels like rummaging shelves
        var flow = new HFlowContainer { };
        flow.AddThemeConstantOverride("h_separation", 14);
        flow.AddThemeConstantOverride("v_separation", 14);
        flow.CustomMinimumSize = new Vector2(820, 330);
        box.AddChild(flow);

        BuildSpots();
        foreach (var s in _spots) flow.AddChild(s);

        _hint = new Label { Text = "翻找屋内的东西。按 E 离开。" };
        _hint.AddThemeFontSizeOverride("font_size", 14);
        _hint.AddThemeColorOverride("font_color", new Color(0.60f, 0.55f, 0.46f));
        box.AddChild(_hint);

        var leave = new Button { Text = "离开屋子 (E)" };
        leave.Pressed += Close;
        var lb = new StyleBoxFlat
        {
            BgColor = new Color(0.24f, 0.20f, 0.16f),
            ContentMarginLeft = 18, ContentMarginRight = 18, ContentMarginTop = 8, ContentMarginBottom = 8,
        };
        leave.AddThemeStyleboxOverride("normal", lb);
        leave.AddThemeColorOverride("font_color", new Color(0.88f, 0.84f, 0.74f));
        box.AddChild(leave);
    }

    private void BuildSpots()
    {
        // every house rolls its own loot table, chest always somewhere around
        AddSpot("ammo1", "弹药盒", "ammo", $"翻出半盒步枪弹（+{_rng.RandiRange(4, 8)} 发）");
        AddSpot("letter1", "压在灶台下的信", "letter", "");
        if (_rng.Randf() > 0.35f)
            AddSpot("grenade1", "木柄手榴弹", "grenade", $"从房梁上摸到一颗手榴弹（+1）");
        AddSpot("chest1", "上锁的弹药箱", "chest", "");
        if (_rng.Randf() > 0.5f)
            AddSpot("clue1", "烧剩一半的文件", "clue", "");
        AddSpot("ash1", "烧塌的柜子", "ash", "柜子里只剩灰和烧变形的搪瓷缸。");
    }

    private void AddSpot(string spotId, string label, string kind, string result)
    {
        string fullId = $"{_houseId}:{spotId}";
        var s = new Spot
        {
            SpotId = fullId,
            Kind = kind,
            ResultText = result,
            Done = _looted.Contains(fullId),
            Text = label,
        };
        var normal = new StyleBoxFlat
        {
            BgColor = s.Done ? new Color(0.17f, 0.15f, 0.13f) : new Color(0.905f, 0.855f, 0.735f),
            BorderColor = new Color(0.24f, 0.20f, 0.16f),
            BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1, BorderWidthBottom = 1,
            ContentMarginLeft = 14, ContentMarginRight = 14, ContentMarginTop = 8, ContentMarginBottom = 8,
        };
        s.AddThemeStyleboxOverride("normal", normal);
        var hover = new StyleBoxFlat
        {
            BgColor = s.Done ? new Color(0.20f, 0.18f, 0.15f) : new Color(0.95f, 0.91f, 0.80f),
            BorderColor = new Color(0.24f, 0.20f, 0.16f),
            BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1, BorderWidthBottom = 1,
            ContentMarginLeft = 14, ContentMarginRight = 14, ContentMarginTop = 8, ContentMarginBottom = 8,
        };
        s.AddThemeStyleboxOverride("hover", hover);
        s.AddThemeColorOverride("font_color", s.Done ? new Color(0.45f, 0.42f, 0.36f) : new Color(0.18f, 0.15f, 0.12f));
        s.AddThemeFontSizeOverride("font_size", 15);
        s.Pressed += () => OnSpot(s);
        _spots.Add(s);
        if (kind == "chest") _chestSpot = s;
        if (s.Done) s.Disabled = kind == "chest";
    }

    private void OnSpot(Spot s)
    {
        if (_closed || s.Done && s.Kind != "chest") return;
        switch (s.Kind)
        {
            case "ammo":
            {
                int n = _rng.RandiRange(4, 8);
                GotAmmo?.Invoke(n);
                s.ResultText = $"翻出半盒步枪弹（+{n} 发）";
                MarkDone(s);
                break;
            }
            case "grenade":
                GotGrenade?.Invoke(1);
                s.ResultText = "从房梁上摸到一颗手榴弹（+1）";
                MarkDone(s);
                break;
            case "letter":
                OpenNote("灶台砖下压着的一封信",
                    new[]
                    {
                        "“娘，这边冷得很，手里的枪比我的手还凉。等把桥守住了，我就回家帮您劈柴。”",
                        "“要是我回不去，别叫人把我的名字写错了。是永胜的胜，得胜的胜。”",
                    },
                    "注：人物为复合虚构，不对应单一在世者。江桥抗战官兵家书意象改写。");
                MarkDone(s);
                break;
            case "clue":
                OpenNote("烧剩一半的文件",
                    new[]
                    {
                        "纸只剩半页，字迹被火燎过：「……军列午后过江桥，皮坎子……车头挂斗三十……」",
                        "后半截烧成了黑灰。落款只看得到一个「熙」字。",
                    },
                    "线索：指向日军军列过桥的时刻表。与铁路工人孙德山的口述互相印证。");
                MarkDone(s);
                break;
            case "chest":
                if (!s.Done) StartPry(s);
                break;
            case "ash":
                MarkDone(s);
                break;
        }
    }

    private void StartPry(Spot s)
    {
        if (_prying) return;
        _prying = true;
        _pryT = 0f;
        s.Text = "用刺刀撬锁……";
        s.Disabled = true;
    }

    public override void _Process(double delta)
    {
        if (_closed) return;
        if (_prying && _chestSpot != null)
        {
            _pryT += (float)delta;
            float t = Mathf.Min(1f, _pryT / 1.4f);
            _chestSpot.Text = $"用刺刀撬锁…… {(int)(t * 100)}%";
            _chestSpot.PryProgress = t;
            if (t >= 1f)
            {
                _prying = false;
                bool open = _rng.Randf() < 0.6f;
                if (open)
                {
                    int n = _rng.RandiRange(6, 12);
                    GotAmmo?.Invoke(n);
                    GotGrenade?.Invoke(1);
                    _chestSpot.ResultText = $"锁开了：整盒步枪弹（+{n} 发）和一颗手榴弹。";
                    _chestSpot.Text = "弹药箱（已撬开）";
                    MarkDone(_chestSpot);
                    _chestSpot.Disabled = false;
                    _chestSpot.Done = true;
                }
                else
                {
                    _chestSpot.Text = "锁头冻死了，撬不开";
                    _chestSpot.ResultText = "锁芯冻死了，刺刀撬到弯也打不开。";
                    MarkDone(_chestSpot);
                }
            }
        }
    }

    private void MarkDone(Spot s)
    {
        s.Done = true;
        s.Text = string.IsNullOrEmpty(s.ResultText) ? $"{s.Text}（已搜过）" : s.ResultText;
        s.Disabled = true;
        _looted.Add(s.SpotId);
        GameState.I.Save();
        _hint.Text = s.ResultText;
    }

    private void OpenNote(string title, string[] paragraphs, string source)
    {
        var note = new FieldNoteUi(_houseTitle, title, paragraphs, source, () => { });
        AddChild(note);
    }

    private void Close()
    {
        if (_closed) return;
        _closed = true;
        Closed?.Invoke();
        QueueFree();
    }
}
