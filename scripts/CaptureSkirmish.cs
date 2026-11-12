using System;
using System.Collections.Generic;
using Godot;

namespace Witness;

/// <summary>
/// 在 SubViewport 内绘制弹道线的图层（必须作为 _sub 的子节点，才会渲染进截图纹理）。
/// </summary>
public partial class TracerLayer : Node2D
{
    public Vector2 From;
    public Vector2 To;
    public bool On;

    public override void _Process(double delta)
    {
        // Godot 4：_Draw 必须 QueueRedraw 才重绘，否则只画一次
        if (On) QueueRedraw();
    }

    public override void _Draw()
    {
        if (!On) return;
        DrawLine(From, To, new Color(1f, 0.92f, 0.45f, 0.95f), 2.4f, true);
        DrawCircle(From, 5f, new Color(1f, 0.85f, 0.3f, 0.95f));
        DrawCircle(To, 4f, new Color(1f, 0.55f, 0.2f, 0.85f));
    }
}

/// <summary>
/// v3.69 验收专用：把九一八关卡塞进 SubViewport，把东北军友军与日军尖兵直接摆在
/// 50px 拼刺刀距离（melee 锁定后即原地交战、不离散），并持续在视口内画一条弹道线，
/// 让「真的在打、不是擦肩而过」一眼可见。友军 / 敌兵 / 弹道全是关卡里跑着的真实节点。
/// 只供开发验收，不进主流程。
/// </summary>
public partial class CaptureSkirmish : Node2D
{
    private SubViewport _sub;
    private N918Level _lvl;
    private TracerLayer _tracer;
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
        _lvl = new N918Level(new Main()) { DebugClock = 11.0f };
        _sub.AddChild(_lvl);
        _tracer = new TracerLayer { ZIndex = 200 };   // 画在关卡之上，避免被背景盖住；且是 _sub 子节点，弹道才进截图纹理
        _sub.AddChild(_tracer);
        GD.Print("CAPTURE_SKIRMISH ready");
    }

    public override void _Process(double delta)
    {
        _t += (float)delta;

        if (_stage == 0 && _t > 1.6f)
        {
            Input.ActionPress("advance");     // 关掉开场导语遮罩
            _stage = 1;
        }
        else if (_stage == 1 && _t > 2.1f)
        {
            Input.ActionRelease("advance");
            if (_lvl._player != null)
                _lvl._player.Position = new Vector2(1640f, N918Level.GroundY - 2f);

            // 两组都直接摆在 50px 拼刺刀距离：melee 锁定后就地交战不离散
            int i = 0;
            foreach (var a in _lvl.BattleFriends())
            {
                a.Position = new Vector2(i == 0 ? 1700f : 1860f, N918Level.GroundY);
                if (++i >= 2) break;
            }
            _lvl.DebugSpawnEnemy(1750f);   // 贴着友军 0（50px）-> 拼刺刀
            _lvl.DebugSpawnEnemy(1910f);   // 贴着友军 1（50px）-> 拼刺刀
            GD.Print("CAPTURE_SKIRMISH staged: 2 allies + 2 IJA at melee range");
            _stage = 2;
        }
        else if (_stage == 2)
        {
            // 把双方钉在 50px 拼刺刀位，并持续画弹道，直到 6s 后收帧
            var allies = new List<Node2D>(_lvl.BattleFriends());
            var foes = new List<Node2D>(_lvl.BattleFoes());
            if (allies.Count > 0 && foes.Count > 0)
            {
                var a0 = allies[0];
                var f0 = foes[0];
                // 每帧回钉原位，避免 AI 把单位推散
                a0.Position = new Vector2(1700f, N918Level.GroundY);
                f0.Position = new Vector2(1750f, N918Level.GroundY);
                if (allies.Count > 1 && foes.Count > 1)
                {
                    allies[1].Position = new Vector2(1860f, N918Level.GroundY);
                    foes[1].Position = new Vector2(1910f, N918Level.GroundY);
                }
                _tracer.From = a0.GlobalPosition + new Vector2(6f, -34f);
                _tracer.To = f0.GlobalPosition + new Vector2(-4f, -30f);
                _tracer.On = true;
            }
            if (_t > 8.4f)   // 约 6s 交战后收帧
            {
                SaveShot("npc_skirmish.png", () =>
                {
                    GD.Print("CAPTURE_SKIRMISH done");
                    GetTree().Quit();
                });
                _stage = 3;
            }
        }
    }

    private async void SaveShot(string name, Action onSaved)
    {
        Image img = null;
        for (int attempt = 0; attempt < 60 && img == null; attempt++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var tex = _sub.GetTexture();
            if (tex != null) img = tex.GetImage();
        }
        if (img == null)
        {
            GD.PrintErr("CAPTURE_SKIRMISH no viewport texture");
            onSaved?.Invoke();
            return;
        }
        if (!System.IO.Directory.Exists(Dir)) System.IO.Directory.CreateDirectory(Dir);
        string path = System.IO.Path.Combine(Dir, name);
        img.SavePng(path);
        GD.Print($"CAPTURE_SKIRMISH saved {path} ({img.GetWidth()}x{img.GetHeight()})");
        onSaved?.Invoke();
    }
}
