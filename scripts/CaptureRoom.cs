using System;
using System.IO;
using Godot;

namespace Witness;

/// <summary>
/// 室内布局实机截图（v3.64）。CanvasLayer 的 HouseInteriorUi 在 SubViewport
/// 里合成不出来，所以这个场景直接把 RoomBg（普通 Control）+ 纸签装进
/// SubViewport，读 GetTexture() 出真图，用来核对水彩画风与史实布局。
/// 运行：Godot --path . --rendering-driver opengl3 --resolution 1280x720 scenes/CaptureRoom.tscn
/// 产物：res://_shots/room_*.png
/// </summary>
public partial class CaptureRoom : Node
{
    private SubViewport _sub;
    private Control _holder;
    private float _t;
    private int _stage;
    private static string Dir => ProjectSettings.GlobalizePath("res://_shots");

    public override void _Ready()
    {
        _sub = new SubViewport
        {
            Size = new Vector2I(1280, 720),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };
        AddChild(_sub);
        _holder = new Control();
        _holder.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _sub.AddChild(_holder);
        BuildRoom(RoomType.Residence, 0, 19310918, "柳条湖边的小屋 · 屋内");
        _t = 0f;
        _stage = 0;
        GD.Print("CAPTURE_ROOM ready");
    }

    private void BuildRoom(RoomType type, int variant, int seed, string titleText)
    {
        var kids = _holder.GetChildren();
        for (int i = kids.Count - 1; i >= 0; i--)
        {
            Node c = kids[i];
            _holder.RemoveChild(c);
            c.QueueFree();
        }

        var dim = new ColorRect { Color = new Color(0.06f, 0.05f, 0.04f, 0.88f) };
        dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _holder.AddChild(dim);

        var panel = new PanelContainer { CustomMinimumSize = new Vector2(1000, 640) };
        // v3.65: 与真实 HouseInteriorUi 同一套漫画分格框
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.14f, 0.10f, 0.08f),
            BorderColor = new Color(0.92f, 0.85f, 0.68f),
            BorderWidthLeft = 4, BorderWidthRight = 4, BorderWidthTop = 4, BorderWidthBottom = 4,
            ContentMarginLeft = 24, ContentMarginRight = 24, ContentMarginTop = 24, ContentMarginBottom = 24,
        });
        var center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _holder.AddChild(center);
        center.AddChild(panel);

        var content = new Control();
        content.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        panel.AddChild(content);

        var bg = HouseInteriorUi.CreateRoomPreview(type, variant, seed);
        bg.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        content.AddChild(bg);
        bg.QueueRedraw();

        var title = new Label { Text = titleText, HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 24);
        title.AddThemeColorOverride("font_color", type == RoomType.BurntWarehouse
            ? new Color(0.94f, 0.88f, 0.76f) : new Color(0.14f, 0.10f, 0.08f));
        title.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        title.OffsetTop = 6;
        content.AddChild(title);

        // 模拟漫画风描边标签，核对标签与室内平涂背景的对比
        string[] labels = type == RoomType.CommOutpost
            ? new[] { "墙边的电闸与手摇发电机", "墙角的无线电发信机", "桌上的信封", "上锁的弹药箱", "烧剩一半的文件" }
            : type == RoomType.BurntWarehouse
                ? new[] { "残墙下压着的信", "一摊烧焦的灰烬", "上锁的弹药箱", "烧剩一半的文件" }
                : new[] { "压在灶台下的信", "叠得整整齐齐的遗书", "上锁的弹药箱", "半卷干净的纱布", "烧塌的柜子" };
        Vector2[] spots = type == RoomType.CommOutpost
            ? new[] { new Vector2(24, 272), new Vector2(690, 154), new Vector2(430, 360), new Vector2(250, 360), new Vector2(250, 200) }
            : type == RoomType.BurntWarehouse
                ? new[] { new Vector2(200, 360), new Vector2(470, 470), new Vector2(620, 400), new Vector2(430, 200) }
                : new[] { new Vector2(796, 426), new Vector2(120, 320), new Vector2(430, 360), new Vector2(640, 300), new Vector2(856, 470) };
        for (int i = 0; i < labels.Length; i++)
        {
            var b = new Button
            {
                Text = labels[i],
                Position = spots[i],
                Size = new Vector2(labels[i].Length > 10 ? 224f : 188f, 42f),
                PivotOffset = new Vector2(94f, 21f),
                Rotation = Mathf.DegToRad((i % 2 == 0 ? 1f : -1f) * (0.8f + i * 0.4f)),
            };
            // v3.65: 与 AddSpot() 同款漫画风标签（米黄土实色 + 3px 深墨棕粗框 + 硬投影）
            b.AddThemeStyleboxOverride("normal", new StyleBoxFlat
            {
                BgColor = new Color(0.92f, 0.85f, 0.68f),
                BorderColor = new Color(0.14f, 0.10f, 0.08f),
                BorderWidthLeft = 3, BorderWidthRight = 3, BorderWidthTop = 3, BorderWidthBottom = 3,
                ShadowColor = new Color(0.10f, 0.08f, 0.06f, 0.55f), ShadowSize = 3,
                ShadowOffset = new Vector2(4, 4),
                ContentMarginLeft = 12, ContentMarginRight = 12, ContentMarginTop = 6, ContentMarginBottom = 6,
            });
            b.AddThemeColorOverride("font_color", new Color(0.14f, 0.10f, 0.08f));
            b.AddThemeFontSizeOverride("font_size", 15);
            content.AddChild(b);
        }

        var leave = new Button { Text = "离开屋子 (E)", Alignment = HorizontalAlignment.Center };
        leave.AddThemeStyleboxOverride("normal", new StyleBoxFlat
        {
            BgColor = new Color(0.20f, 0.15f, 0.11f),
            BorderColor = new Color(0.92f, 0.85f, 0.68f),
            BorderWidthLeft = 3, BorderWidthRight = 3, BorderWidthTop = 3, BorderWidthBottom = 3,
            ShadowColor = new Color(0.08f, 0.06f, 0.05f, 0.55f), ShadowSize = 3, ShadowOffset = new Vector2(4, 4),
            ContentMarginLeft = 18, ContentMarginRight = 18, ContentMarginTop = 8, ContentMarginBottom = 8,
        });
        leave.AddThemeColorOverride("font_color", new Color(0.98f, 0.91f, 0.75f));
        leave.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
        leave.OffsetBottom = -10;
        content.AddChild(leave);
    }

    public override void _Process(double delta)
    {
        _t += (float)delta;
        switch (_stage)
        {
            case 0 when _t > 1.0f:
                // SaveShot 是异步的：切房间必须等这一帧存完，否则会拍到下一间
                SaveShot("room_01_residence.png", () =>
                    BuildRoom(RoomType.Residence, 1, 19310919, "柳条湖边的小屋 · 屋内（另一间）"));
                _stage = 1;
                break;
            case 1 when _t > 2.0f:
                SaveShot("room_02_residence_flip.png", () =>
                    BuildRoom(RoomType.CommOutpost, 0, 19310920, "通信据点 · 无线电发信机 · 屋内"));
                _stage = 2;
                break;
            case 2 when _t > 3.0f:
                SaveShot("room_03_comm_outpost.png", () =>
                    BuildRoom(RoomType.BurntWarehouse, 0, 19310921, "焚毁的仓库 · 屋内"));
                _stage = 3;
                break;
            case 3 when _t > 4.0f:
                SaveShot("room_04_burnt.png");
                _stage = 4;
                break;
            case 4 when _t > 4.9f:
                GD.Print("CAPTURE_ROOM done");
                GetTree().Quit();
                break;
        }
    }

    private async void SaveShot(string name, Action onSaved = null)
    {
        Image img = null;
        for (int i = 0; i < 20 && img == null; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var tex = _sub.GetTexture();
            if (tex != null) img = tex.GetImage();
        }
        if (img == null) { GD.PrintErr("CAPTURE_ROOM skip " + name); return; }
        if (!Directory.Exists(Dir)) Directory.CreateDirectory(Dir);
        string path = Path.Combine(Dir, name);
        img.SavePng(path);
        GD.Print("CAPTURE_ROOM saved " + path);
        onSaved?.Invoke();
    }
}
