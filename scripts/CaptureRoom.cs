using System;
using System.IO;
using System.Collections.Generic;
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
    /// <summary>v3.84：>=0 时每帧把家具 OpenT 钉住，才能拍到开门动画的中间帧。</summary>
    private float _holdOpenT = -1f;
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
        BuildRoom(RoomType.Residence, 0, 19310918, "柳条湖边的小屋 · 屋内", 0f);
        _t = 0f;
        _stage = 0;
        GD.Print("CAPTURE_ROOM ready");
    }

    /// <summary>
    /// v3.84：open=false 拍「柜门 / 挂画都关着」的原始状态，open=true 拍「翻开后」的状态，
    /// 用来逐帧验证用户要的「先互动 → 开门动画 → 才看到里面的东西」时序。
    /// v3.71~v3.83 这里无条件把家具全设 Opened=true，藏匿时序从来没被截图验证过。
    /// </summary>
    private void BuildRoom(RoomType type, int variant, int seed, string titleText, float openT,
        IEnumerable<RoomItem> realItems = null)
    {
        // v3.71 用户诉求③：CaptureRoom 造的都是九一八的屋（夜袭），窗光设为月光
        WorldClock.Time = WorldClock.Phase.Day;
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
        // v3.84：额外验证「真实游戏路径」—— HouseInteriorUi 会把 _spots 一对一转成 items 传进来，
        // 旧版 Create 在这之后又追加示例物件，导致 NearIndex 与 _spots 错位；本帧证明两条路径一致。
        _room = realItems == null
            ? (InteriorRoom)HouseInteriorUi.CreateRoomPreview(type, variant, seed, titleText)
            : InteriorRoom.Create(type, variant, seed, titleText, realItems);
        _room.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        content.AddChild(_room);
        _room.QueueRedraw();
        // v3.84 三态：0=柜门/挂画关着（暗格与藏匿物完全不可见）、0.55=开门动画中、1=全开
        _holdOpenT = openT;
        foreach (var f in _room.Furniture) { f.Opened = openT > 0f; f.OpenT = openT; }
        // 关闭态把准星停在木柜前（柜子附近没有明面物件，NearIndex 为 -1），
        // 让「E · 拉开柜门」提示进帧；打开态停在第一件藏匿物上，让「E · 查看…」进帧。
        if (openT <= 0f && _room.Furniture.Count > 0)
        {
            var cab = _room.Furniture.Find(x => x.Type == FurnitureType.Cabinet);
            _room.PlayerX = (cab ?? _room.Furniture[0]).Center.X;
        }
        else if (_room.Items.Count > 0)
            _room.PlayerX = _room.Items[0].Pos.X;

        // v3.67b：标题由 InteriorForeground 统一绘制，这里不再重复叠一层。

    }

    public override void _Process(double delta)
    {
        _t += (float)delta;
        // v3.70：第一视角下不再让小人走动；准星固定在第一个物件上扫视
        _room?.Step((float)delta, 0f);
        // Step() 会把 OpenT 往 1 推进；钉回目标值才能稳定拍到 0 / 0.55 / 1 三态
        if (_holdOpenT >= 0f && _room != null)
            foreach (var f in _room.Furniture) f.OpenT = _holdOpenT;
        switch (_stage)
        {
            case 0 when _t > 1.0f:
                // SaveShot 是异步的：切房间必须等这一帧存完，否则会拍到下一间
                SaveShot("room_01_residence.png", () =>
                    BuildRoom(RoomType.Residence, 0, 19310918, "柳条湖边的小屋 · 屋内", 0.55f));
                _stage = 1;
                break;
            case 1 when _t > 2.0f:
                SaveShot("room_01_mid_opening.png", () =>
                    BuildRoom(RoomType.Residence, 0, 19310918, "柳条湖边的小屋 · 屋内", 1f));
                _stage = 2;
                break;
            case 2 when _t > 3.0f:
                SaveShot("room_01b_residence_opened.png", () =>
                    BuildRoom(RoomType.Residence, 1, 19310919, "柳条湖边的小屋 · 屋内（另一间）", 0f));
                _stage = 3;
                break;
            case 3 when _t > 4.0f:
                SaveShot("room_02_residence_flip.png", () =>
                    BuildRoom(RoomType.CommOutpost, 0, 19310920, "通信据点 · 无线电发信机 · 屋内", 0f));
                _stage = 4;
                break;
            case 4 when _t > 5.0f:
                SaveShot("room_03_comm_outpost.png", () =>
                    BuildRoom(RoomType.CommOutpost, 0, 19310920, "通信据点 · 无线电发信机 · 屋内", 0.55f));
                _stage = 5;
                break;
            case 5 when _t > 6.0f:
                SaveShot("room_03_mid_opening.png", () =>
                    BuildRoom(RoomType.CommOutpost, 0, 19310920, "通信据点 · 无线电发信机 · 屋内", 1f));
                _stage = 6;
                break;
            case 6 when _t > 7.0f:
                SaveShot("room_03b_comm_opened.png", () =>
                    BuildRoom(RoomType.BurntWarehouse, 0, 19310921, "焚毁的仓库 · 屋内", 1f));
                _stage = 7;
                break;
            case 7 when _t > 8.0f:
                SaveShot("room_04_burnt.png", () =>
                {
                    // 模拟 HouseInteriorUi.BuildSpots 的真实 loot（顺序 / kind / SpotId 与游戏一致）
                    var real = new List<RoomItem>
                    {
                        new() { SpotId = "house:ammo1",    Kind = "ammo",    Label = "弹药盒" },
                        new() { SpotId = "house:letter1",  Kind = "letter",  Label = "压在灶台下的信" },
                        new() { SpotId = "house:grenade1", Kind = "grenade", Label = "木柄手榴弹" },
                        new() { SpotId = "house:will1",    Kind = "will",    Label = "叠得整整齐齐的遗书" },
                        new() { SpotId = "house:chest1",   Kind = "chest",   Label = "上锁的弹药箱" },
                        new() { SpotId = "house:clue1",    Kind = "clue",    Label = "烧剩一半的文件" },
                        new() { SpotId = "house:ash1",     Kind = "ash",     Label = "烧塌的柜子" },
                    };
                    BuildRoom(RoomType.Residence, 0, 19310918,
                        "柳条湖边的小屋 · 屋内（真实搜刮路径）", 1f, real);
                });
                _stage = 8;
                break;
            case 8 when _t > 9.0f:
                SaveShot("room_09_realloot_path.png");
                _stage = 9;
                break;
            case 9 when _t > 9.9f:
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
