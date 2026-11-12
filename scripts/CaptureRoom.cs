using System;
using System.IO;
using Godot;

namespace Witness;

/// <summary>
/// 可探索屋内实机截图（v3.67）。CanvasLayer 的 HouseInteriorUi 在 SubViewport
/// 里合成不出来，所以直接把 InteriorRoom（普通 Control：漫画风房间 + 实体物件
/// + 玩家小人）装进 SubViewport，读 GetTexture() 出真图，用来核对格局与画风。
/// 运行：Godot --path . --rendering-driver opengl3 --resolution 1280x720 scenes/CaptureRoom.tscn
/// 产物：res://_shots/room_*.png
/// </summary>
public partial class CaptureRoom : Node
{
    private SubViewport _sub;
    private Control _holder;
    private InteriorRoom _room;   // v3.67 让小人在预览里走动，展示可探索
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
        // v3.68: 与真实 HouseInteriorUi 一致 —— 深墨棕底，不再套米黄 UI 卡片框
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = InkStyle.InkColor,
            BorderColor = InkStyle.InkColor,
            BorderWidthLeft = 3, BorderWidthRight = 3, BorderWidthTop = 3, BorderWidthBottom = 3,
            ContentMarginLeft = 14, ContentMarginRight = 14, ContentMarginTop = 14, ContentMarginBottom = 14,
        });
        var center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _holder.AddChild(center);
        center.AddChild(panel);

        var content = new Control();
        content.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        panel.AddChild(content);

        // v3.67：CreateRoomPreview 现在返回 InteriorRoom（背景 + 实体物件 + 玩家小人），
        // 不再由本脚本手画一排假标签按钮冒充室内 UI。
        _room = (InteriorRoom)HouseInteriorUi.CreateRoomPreview(type, variant, seed, titleText);
        _room.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        content.AddChild(_room);
        _room.QueueRedraw();
        // v3.70：第一视角验证 —— 把准星停在第一个物件上，截图能同时看到
        // 「准星扫视」+ 手绘漫画风物件 + 靠近高亮，不再让小人走动漂移。
        if (_room.Items.Count > 0) _room.PlayerX = _room.Items[0].Pos.X;

        // v3.67b：标题由 InteriorForeground 统一绘制，这里不再重复叠一层。

    }

    public override void _Process(double delta)
    {
        _t += (float)delta;
        // v3.70：第一视角下不再让小人走动；准星固定在第一个物件上扫视
        _room?.Step((float)delta, 0f);
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
