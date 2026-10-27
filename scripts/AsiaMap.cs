using System;
using Godot;

namespace Witness;

/// <summary>
/// 东亚大地图（新流程的起点）：
/// 标题 → 这张地图 → 先读 1895 割台 / 1910 日韩合并两张纸条 →
/// 日军北上箭头亮起 → 1931.11 嫩江江桥关卡。
///
/// 地图本身是一层一层画出来的（纸张 → 网格 → 陆地 → 殖民地区 → 河流铁路 →
/// 航线与进军箭头 → 图钉 → 纸条），所有元素保持前后顺序，不靠整张贴图。
/// </summary>
public partial class AsiaMap : Control
{
    private readonly Main _main;

    private MapArt _paper;
    private MapArt _art;
    private MapArt _ink;
    private Label _status;
    private Label _hint;
    private Label _toast;
    private Button _noteKorea;
    private Button _noteTaiwan;
    private Button _note1931;
    private Button _enter;
    private float _arrowT;
    private bool _arrowStarted;
    private float _toastT;

    public AsiaMap(Main main)
    {
        _main = main;
    }

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        // 桌面底色（旧木桌）
        var desk = new ColorRect { Color = new Color(0.115f, 0.095f, 0.075f) };
        desk.SetAnchorsPreset(LayoutPreset.FullRect);
        desk.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(desk);

        _paper = new MapArt(MapLayer.Paper);
        _paper.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_paper);

        _art = new MapArt(MapLayer.Geo);
        AddChild(_art);

        _ink = new MapArt(MapLayer.Ink);
        _ink.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_ink);

        BuildHeader();
        BuildLabels();
        BuildNotes();
        BuildFooter();

        Refresh();
    }

    // ================= 界面 =================

    private void BuildHeader()
    {
        var title = new Label
        {
            Text = "记忆档案馆 · 东亚 1895—1945",
        };
        title.AddThemeFontSizeOverride("font_size", 28);
        title.AddThemeColorOverride("font_color", new Color(0.90f, 0.85f, 0.73f));
        title.Position = new Vector2(54, 22);
        title.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(title);

        var sub = new Label
        {
            Text = "战争不是从 1937 年开始的。先把源头读完，再走进 1931 的东北。",
        };
        sub.AddThemeFontSizeOverride("font_size", 14);
        sub.AddThemeColorOverride("font_color", new Color(0.68f, 0.63f, 0.53f));
        sub.Position = new Vector2(56, 56);
        sub.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(sub);
    }

    private void BuildLabels()
    {
        void Tag(string text, float lon, float lat, float dx, float dy,
                 int size = 13, float rot = 0f, Color? ink = null)
        {
            var v = MapArt.P(lon, lat);
            var l = new Label
            {
                Text = text,
                Position = new Vector2(v.X + dx, v.Y + dy),
                Rotation = Mathf.DegToRad(rot),
            };
            l.AddThemeFontSizeOverride("font_size", size);
            l.AddThemeColorOverride("font_color", ink ?? new Color(0.22f, 0.19f, 0.15f, 0.55f));
            l.MouseFilter = MouseFilterEnum.Ignore;
            AddChild(l);
        }

        // 地理名：位置全部由经纬度派生，不再手工摆
        Tag("苏联", 118.5f, 50.2f, -18, -6, 15, -2f);
        Tag("蒙古", 114.6f, 45.5f, 2, -4, 12, 1.5f);
        // v2.8: the country name used to sit on top of the red Korean colony
        // hatching and on the coastline, so half the characters were hard to
        // read. It now sits inland over empty ground, fully clear of both.
        Tag("中国（中华民国时代）", 114.4f, 27.5f, -8, 0, 19, -1f);
        Tag("满洲", 131.6f, 48.0f, 2, -4, 15, 2f);
        // v2.7: Korea was already a colony of the Japanese Empire from 1910,
        // so a 1931 war map should not call it "Korean Peninsula" - that is
        // a modern geographic name and it does not fit the period.
        // v2.8: was at lon 125.0 which put the text straight across the
        // coastline and over the colony hatching. Now it sits out in the
        // sea south-east of the peninsula, where nothing else is.
        Tag("朝鮮（1910 起被吞并）", 126.5f, 29.5f, 4, 0, 10, -1f);
        Tag("大日本帝国", 140.6f, 36.5f, 4, -6, 16, 0f);
        // v2.8: the Taiwan label was overlapping the red colony hatching.
        // Moved south-west, over open water.
        Tag("台湾地区", 120.3f, 23.2f, -40, 6, 12, -2f);
        Tag("华北", 115.4f, 38.5f, -10, -6, 13, -1f);

        // 进军路线上的落脚城镇（标签向路线外侧错开，避免压在粗线上）
        Tag("汉城", 127f, 37.5f, 13, 7, 11, 0f, new Color(0.16f, 0.13f, 0.10f, 0.92f));
        Tag("平壤", 125.7f, 39f, -38, -4, 10);
        Tag("沈阳", 123.4f, 41.8f, -48, -4, 11);
        Tag("长春", 125.3f, 43.9f, -46, -4, 11);
        // v2.7 timeline order fix:
        // 9.18 (1931.9) -> Jiangqiao (1931.11) -> 731 predecessor (1932) ->
        // 731 formed (1936) -> first combat (1940)
        // Before this it said "Harbin / 731 Unit 1932", which reads as if the
        // unit already existed in 1932, and the map put it south of Jiangqiao
        // so people read it as "731 first, Jiangqiao later".
        // Now it says the 1932 institute (the real predecessor), and the
        // formation + combat dates live in the chapter list on the right.
        // v2.9: the bridge pin used to sit at lon 125.2 / lat 49.2 - that is
        // Nenjiang city way up north, not the battle site. The real Jiangqiao
        // (-Taolai county) is SOUTH of Qiqihar, ~123.35E 46.45N, where the
        // railway crosses the lower Nen River. With the pin up north the map
        // read "Harbin 731 first, Jiangqiao later on the Soviet border".
        Tag("哈尔滨 · 石井四郎细菌战研究所 1932（731 前身）", 126.6f, 45.8f, 12, -3, 9);
        Tag("齐齐哈尔", 123.9f, 47.3f, -62, -4, 10);
        Tag("嫩江 · 江桥 1931.11", 123.35f, 46.45f, -26, 16, 11, 0f, new Color(0.62f, 0.21f, 0.16f, 0.90f));
        // v2.8: "南京 1937" used to sit right on the coastline and the sea
        // cut the characters in half. It now sits inland, to the west.
        Tag("南京 1937", 118.8f, 32f, -76, -8, 10);
        // v2.8: this label used to sit at lon 116.0 / lat 41.4, which is right
        // on top of the "沈阳" city tag - the red text ran straight through the
        // grey characters and both became unreadable. It now sits in the empty
        // land west of the railway, inside the map frame. The year sits on its
        // own line underneath, so neither line reaches the railway line or
        // the red colony hatching.
        Tag("关东军北上", 114.6f, 42.6f, 0, 0, 12, -3f,
            new Color(0.62f, 0.21f, 0.16f, 0.62f));
        Tag("1931", 114.6f, 41.5f, 0, 0, 10, -3f,
            new Color(0.62f, 0.21f, 0.16f, 0.52f));
    }

    private void BuildNotes()
    {
        // 右半张纸是档案栏：纸条竖排，右侧是尚未开放的章节
        _noteKorea = MakeSlip("纸条 01 · 汉城\n《日韩合并条约》1910\n点击了解 ›", new Vector2(640, 148), 1.4f);
        _noteKorea.Pressed += () => OpenNote("korea");
        AddChild(_noteKorea);

        _noteTaiwan = MakeSlip("纸条 02 · 台北\n《马关条约》割台 1895\n点击了解 ›", new Vector2(640, 278), -1.6f);
        _noteTaiwan.Pressed += () => OpenNote("taiwan");
        AddChild(_noteTaiwan);

        _note1931 = MakeSlip("作战简报 · 1931\n关东军北上\n点击了解 ›", new Vector2(640, 408), 1.1f);
        _note1931.Pressed += () => OpenNote("1931");
        _note1931.Visible = false;
        AddChild(_note1931);

        BuildChapters();

        // 进入关卡的正式按钮（读完简报后出现）
        _enter = new Button
        {
            Text = "进入关卡 · 嫩江江桥 1931.11.4  ›",
            Visible = false,
            CustomMinimumSize = new Vector2(540, 52),
        };
        _enter.AddThemeFontSizeOverride("font_size", 18);
        _enter.Position = new Vector2(640, 538);
        var enterStyle = new StyleBoxFlat
        {
            BgColor = new Color(0.55f, 0.20f, 0.16f),
            BorderColor = new Color(0.20f, 0.15f, 0.12f),
            BorderWidthLeft = 2, BorderWidthRight = 2, BorderWidthTop = 2, BorderWidthBottom = 2,
            CornerRadiusTopLeft = 2, CornerRadiusTopRight = 2, CornerRadiusBottomLeft = 2, CornerRadiusBottomRight = 2,
            ShadowColor = new Color(0, 0, 0, 0.35f), ShadowSize = 5, ShadowOffset = new Vector2(3, 4),
        };
        _enter.AddThemeStyleboxOverride("normal", enterStyle);
        _enter.AddThemeColorOverride("font_color", new Color(0.94f, 0.89f, 0.77f));
        _enter.Pressed += () => _main.StartJiangqiao();
        AddChild(_enter);
    }

    /// <summary>档案栏右列：后面几章要经过的地方（现在只登记，不开放）。</summary>
    private void BuildChapters()
    {
        var head = new Label { Text = "章节顺序 · 时间线" };
        head.AddThemeFontSizeOverride("font_size", 13);
        head.AddThemeColorOverride("font_color", new Color(0.30f, 0.26f, 0.21f, 0.60f));
        head.Position = new Vector2(980, 138);
        head.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(head);

        // v2.9: full chronological timeline. The old list started at 1932
        // (731 predecessor), so the column read "731 first, Jiangqiao later".
        // Now 9.18 and Jiangqiao lead the list and the bridge row is the
        // highlighted current chapter, so the order is fixed: 9.18 ->
        // Jiangqiao (1931.11) -> 731 predecessor (1932) -> formed (1936) ->
        // combat use (1940).
        (string, Color?)[] rows =
        {
            ("1931.9　沈阳 · 九一八事变", null),
            ("1931.11　嫩江江桥 · 当前战役", new Color(0.62f, 0.21f, 0.16f, 0.85f)),
            ("1932　哈尔滨 · 细菌战研究所（731 前身）", null),
            ("1936　关东军防疫给水部（731 成军）", null),
            ("1937　南京", null),
            ("1938　平房基地 · 人体实验", null),
            ("1940　细菌战投入实战", null),
            ("1942　马来亚 · 知知港", null),
            ("1942　新加坡", null),
            ("1945　终战", null),
        };
        for (int i = 0; i < rows.Length; i++)
        {
            var l = new Label { Text = rows[i].Item1 };
            l.AddThemeFontSizeOverride("font_size", 11);
            l.AddThemeColorOverride("font_color", rows[i].Item2 ?? new Color(0.26f, 0.22f, 0.18f, 0.52f));
            l.Position = new Vector2(980, 162 + i * 24);
            l.MouseFilter = MouseFilterEnum.Ignore;
            AddChild(l);
        }

        var note = new Label
        {
            Text = "每一章对应一次真实惨案。\n先做 1931 嫩江江桥这一关。",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(224, 0),
        };
        note.AddThemeFontSizeOverride("font_size", 11);
        note.AddThemeColorOverride("font_color", new Color(0.32f, 0.27f, 0.22f, 0.40f));
        note.Position = new Vector2(980, 414);
        note.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(note);
    }

    private Button MakeSlip(string text, Vector2 pos, float rotDeg)
    {
        var b = new Button
        {
            Text = text,
            CustomMinimumSize = new Vector2(212, 104),
            PivotOffset = new Vector2(106, 52),
            Rotation = Mathf.DegToRad(rotDeg),
            Position = pos,
        };
        b.AddThemeFontSizeOverride("font_size", 15);
        var paper = new StyleBoxFlat
        {
            BgColor = new Color(0.92f, 0.87f, 0.74f),
            BorderColor = new Color(0.35f, 0.28f, 0.21f),
            BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1, BorderWidthBottom = 1,
            CornerRadiusTopLeft = 1, CornerRadiusTopRight = 1, CornerRadiusBottomLeft = 1, CornerRadiusBottomRight = 1,
            ShadowColor = new Color(0, 0, 0, 0.32f),
            ShadowSize = 6,
            ShadowOffset = new Vector2(3, 4),
            ContentMarginLeft = 10, ContentMarginRight = 8, ContentMarginTop = 8, ContentMarginBottom = 8,
        };
        var hover = (StyleBoxFlat)paper.Duplicate();
        hover.BgColor = new Color(0.96f, 0.91f, 0.78f);
        hover.BorderColor = new Color(0.55f, 0.20f, 0.16f);
        b.AddThemeStyleboxOverride("normal", paper);
        b.AddThemeStyleboxOverride("hover", hover);
        b.AddThemeStyleboxOverride("pressed", hover);
        b.AddThemeColorOverride("font_color", new Color(0.20f, 0.17f, 0.14f));
        b.AddThemeColorOverride("font_hover_color", new Color(0.16f, 0.13f, 0.10f));
        // 纸片的“钉线”：画在按钮之上的细线由 MapArt 负责
        b.SetMeta("anchorPoint", pos);
        return b;
    }

    private void BuildFooter()
    {
        var bar = new ColorRect { Color = new Color(0.08f, 0.065f, 0.05f, 0.55f) };
        bar.Position = new Vector2(0, 664);
        bar.Size = new Vector2(1280, 56);
        bar.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(bar);

        _status = new Label();
        _status.AddThemeFontSizeOverride("font_size", 15);
        _status.AddThemeColorOverride("font_color", new Color(0.82f, 0.77f, 0.66f));
        _status.Position = new Vector2(56, 680);
        _status.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(_status);

        _hint = new Label();
        _hint.AddThemeFontSizeOverride("font_size", 14);
        _hint.AddThemeColorOverride("font_color", new Color(0.62f, 0.58f, 0.49f));
        _hint.Position = new Vector2(452, 680);
        _hint.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(_hint);

        _toast = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            CustomMinimumSize = new Vector2(720, 0),
        };
        _toast.AddThemeFontSizeOverride("font_size", 13);
        _toast.AddThemeColorOverride("font_color", new Color(0.55f, 0.20f, 0.16f));
        _toast.Position = new Vector2(280, 618);
        _toast.MouseFilter = MouseFilterEnum.Ignore;
        _toast.Modulate = new Color(1, 1, 1, 0);
        AddChild(_toast);

        var back = new Button { Text = "返回标题" };
        back.AddThemeFontSizeOverride("font_size", 15);
        back.CustomMinimumSize = new Vector2(132, 36);
        back.Position = new Vector2(1108, 678);
        back.Pressed += () => _main.ShowTitle();
        AddChild(back);
    }

    // ================= 状态 =================

    private bool KoreaRead => GameState.I.S.MapNoteKorea;
    private bool TaiwanRead => GameState.I.S.MapNoteTaiwan;
    private bool RouteOn => KoreaRead && TaiwanRead;
    private bool BriefRead => GameState.I.S.JiangqiaoDone || _briefOpened;

    private bool _briefOpened;

    private void Refresh()
    {
        foreach (var a in new[] { _art, _ink })
        {
            a.KoreaRead = KoreaRead;
            a.TaiwanRead = TaiwanRead;
            a.RouteOn = RouteOn;
            a.BriefRead = BriefRead;
            a.QueueRedraw();
        }

        _noteKorea.Text = KoreaRead
            ? "纸条 01 · 汉城\n《日韩合并条约》1910\n已读 · 重读 ›"
            : "纸条 01 · 汉城\n《日韩合并条约》1910\n点击了解 ›";
        _noteTaiwan.Text = TaiwanRead
            ? "纸条 02 · 台北\n《马关条约》割台 1895\n已读 · 重读 ›"
            : "纸条 02 · 台北\n《马关条约》割台 1895\n点击了解 ›";

        _note1931.Visible = RouteOn;
        _enter.Visible = RouteOn && BriefRead;

        _status.Text =
            $"源头档案  {(KoreaRead ? 1 : 0) + (TaiwanRead ? 1 : 0)} / 2"
            + (RouteOn ? "      ·      路线已显影" : "")
            + (GameState.I.S.JiangqiaoDone ? "      ·      江桥已见证" : "");

        _hint.Text = !KoreaRead || !TaiwanRead
            ? "先读右侧两张纸条：1895 台湾，1910 大韩帝国。"
            : !BriefRead
                ? "路线显影了：日军自汉城北上。读作战简报，进入江桥阵地。"
                : "阵地已在等待。点下方按钮，进入 1931 年 11 月 4 日。";

        if (RouteOn)
        {
            if (!_arrowStarted) { _arrowStarted = true; _arrowT = 0f; _art.Progress = 0f; _ink.Progress = 0f; }
        }
        else
        {
            _arrowStarted = false;
            _arrowT = 0f;
            _art.Progress = 0f;
            _ink.Progress = 0f;
        }
    }

    public override void _Process(double delta)
    {
        float f = (float)delta;
        if (_arrowStarted && _arrowT < 1f)
        {
            _arrowT = Mathf.Min(1f, _arrowT + f / 2.4f);
            _art.Progress = _arrowT;
            _ink.Progress = _arrowT;
            _ink.QueueRedraw();
        }
        if (_toastT > 0f)
        {
            _toastT -= f;
            if (_toastT <= 0f) _toast.Modulate = new Color(1, 1, 1, 0);
        }
    }

    private void Toast(string text)
    {
        _toast.Text = text;
        _toast.Modulate = new Color(1, 1, 1, 1);
        _toastT = 2.8f;
    }

    // ================= 纸条 / 档案浮层 =================

    private void OpenNote(string which)
    {
        FieldNoteUi ui;
        switch (which)
        {
            case "korea":
                ui = new FieldNoteUi(
                    "纸条 01 · 1910 年 8 月 22 日",
                    // v2.7: the proper name of the country in 1910 was the
                    // "Korean Empire" (renamed by Emperor Gojong in 1897), and it
                    // only stopped existing when the treaty took effect on
                    // 29 Aug 1910. The old title used a modern geographic name
                    // to talk about 1910, which is not accurate.
                    "《日韩合并条约》：大韩帝国是怎样被吞下的",
                    new[]
                    {
                        "1905 年，大日本帝国以《乙巳条约》夺走大韩帝国外交权，设“统监府”；1910 年 8 月 22 日，大韩帝国内阁总理李完用与日本统监寺内正毅签订《日韩合并条约》，8 月 29 日生效，大韩帝国就此灭亡，朝鲜总督府成立。",
                        "大日本帝国要朝鲜，不只是为了土地：它是通往大陆的跳板，是粮食、矿产与劳动力的来源，也是面对俄国与中国的战略缓冲。1910—1918 年的土地调查，把大量公有地与农民耕地并入总督府和大日本帝国公司名下。",
                        "殖民统治并不安静：1919 年三一运动被镇压；1938 年起强制征用劳工；1940 年“创氏改名”；1944 年起在朝鲜征兵。1910 年的这张纸，是此后三十五年一切的起点。",
                    },
                    "依据：《日韩合并条约》文本（1910）；《朝鲜总督府官报》；大韩帝国国史编纂委员会公开史料。",
                    () => MarkRead("korea"));
                break;

            case "taiwan":
                ui = new FieldNoteUi(
                    "纸条 02 · 1895 年 4 月 17 日",
                    "《马关条约》割台：五十年殖民的开端",
                    new[]
                    {
                        "甲午战争后，清廷与大日本帝国在下关签订《马关条约》。第二条写明：台湾全岛及所有附属岛屿、澎湖列岛，永远割让与大日本帝国。",
                        "大日本帝国为什么一定要台湾：它是南进的跳板，是控制巴士海峡与南海航道的据点；岛上的糖、樟脑、煤与稻米，是本土工业与军需的补给；基隆、高雄又天然适合做军港。",
                        "1895 年 5 月起，台湾民主国与各地义军抵抗，10 月台南陷落。此后五十年：1896 年《六三法》赋予总督近乎专断的立法权；1905 年土地调查；1915 年西来庵事件；1937 年皇民化运动；1942 年志愿兵制度，1945 年全面征兵。",
                    },
                    "依据：《马关条约》文本（1895）；台湾总督府档案；周婉窈《台湾历史图说》。",
                    () => MarkRead("taiwan"));
                break;

            default:
                ui = new FieldNoteUi(
                    "作战简报 · 1931 年 11 月 4 日",
                    "关东军北上：嫩江江桥",
                    new[]
                    {
                        "1931 年 9 月 18 日夜，柳条湖铁路被炸，关东军进攻沈阳。不到两个月，辽宁、吉林大部沦陷。11 月，日军与伪军沿嫩江铁路北上，目标齐齐哈尔。",
                        "嫩江江桥是铁路跨江的咽喉。黑龙江省代主席马占山拒绝投降，率部在江桥北岸布防。11 月 4 日，战斗打响——这是九一八之后，中国军队第一次有组织的抵抗。",
                        "你将作为一名东北军通信兵进入阵地：不指挥胜负，只记录阵地上还活着的人说过的话。三份口述记录，必须在部队撤退前送出去。",
                        "操作：A / D 移动 · 空格 跳跃 · S 蹲下 · Ctrl 匍匐 · E 记录/互动 · 鼠标左键 射击 · Esc 返回地图",
                    },
                    "依据：马占山《江桥抗战经过》；《黑龙江省志·军事志》；大日本帝国关东军作战记录（公开档案）。本关人物为复合虚构，口述内容依据公开口述史改写。",
                    () =>
                    {
                        _briefOpened = true;
                        Refresh();
                    });
                break;
        }
        ui.Closed += Refresh;
        AddChild(ui);
    }

    private void MarkRead(string which)
    {
        if (which == "korea") GameState.I.S.MapNoteKorea = true;
        if (which == "taiwan") GameState.I.S.MapNoteTaiwan = true;
        GameState.I.Save();
        Refresh();
        bool both = GameState.I.S.MapNoteKorea && GameState.I.S.MapNoteTaiwan;
        Toast(both
            ? "两张纸读完了：1895 台湾 → 1910 大韩帝国。1931 年 11 月，关东军沿铁路北上。"
            : which == "korea" ? "已读：1910 日韩合并条约" : "已读：1895 马关条约·割台");
    }

    // ================= 地图绘制（逐层） =================

    /// <summary>地图的三个绘制层，按前后顺序叠放。</summary>
    private enum MapLayer
    {
        Paper,   // 桌面 + 纸 + 网格 + 海域 + 图框（在所有陆地之下）
        Geo,     // 陆地、殖民地、河流铁路（裁切在图框内）
        Ink,     // 航线、进军箭头、城镇、图钉、注记（在所有陆地之上）
    }

    /// <summary>
    /// 地图绘制器：一个类，三种图层角色。同一套坐标与绘图工具，
    /// 分别放在“纸张层 → 地理层 → 墨迹层”三个节点上，形成前后有厚度的层次。
    /// </summary>
    private partial class MapArt : Control
    {
        public bool KoreaRead, TaiwanRead, RouteOn, BriefRead;
        public float Progress;

        private readonly MapLayer _mode;

        private static readonly Color LandC = new(0.945f, 0.912f, 0.815f);
        private static readonly Color Ink = new(0.17f, 0.145f, 0.115f);
        private static readonly Color InkSoft = new(0.30f, 0.255f, 0.20f);
        private static readonly Color Colony = new(0.55f, 0.20f, 0.16f);
        private static readonly Color Route = new(0.62f, 0.21f, 0.16f);
        private static readonly Color Paper = new(0.905f, 0.855f, 0.735f);
        private static readonly Color Sea = new(0.788f, 0.812f, 0.795f);

        // —— 等距圆柱投影 ——
        // 窗口取战区：北起外兴安岭，南到台湾海峡，西起华北，东到大日本帝国本州。
        // 不是全球地图，而是一张作战档案图 —— 1931 的北上路线要看得清。
        private const float Lon0 = 113.4f, Lat0 = 19.6f;
        private const float SX = 15.8f, SY = 16.42f;
        private const float X0 = 58f, Y0 = 608f;

        // 图框（世界坐标）
        private static readonly Rect2 Frame = new(57, 97, 534, 514);

        public static Vector2 P(float lon, float lat)
            => new(X0 + (lon - Lon0) * SX, Y0 - (lat - Lat0) * SY);

        /// <summary>经纬度点串 → 画面点串。</summary>
        private static Vector2[] L(params (float Lon, float Lat)[] geo)
        {
            var r = new Vector2[geo.Length];
            for (int i = 0; i < geo.Length; i++)
                r[i] = P(geo[i].Lon, geo[i].Lat);
            return r;
        }

        /// <summary>确定性伪随机：同一位置每次画出来一样，不会闪。</summary>
        private static float Hash(float n, float seed)
            => Mathf.Abs(Mathf.Sin(n * 12.9898f + seed * 78.233f) * 43758.5453f) % 1f;

        public MapArt(MapLayer mode)
        {
            _mode = mode;
            MouseFilter = MouseFilterEnum.Ignore;
            if (mode == MapLayer.Geo)
            {
                // 只有地理层用裁切画布：贴在图框上，超出的海岸线自动切掉
                Position = Frame.Position;
                Size = Frame.Size;
                ClipContents = true;
            }
        }

        public override void _Draw()
        {
            switch (_mode)
            {
                case MapLayer.Paper:
                    DrawPaper();
                    DrawGrid();
                    DrawWaves();
                    DrawFrame();
                    break;

                case MapLayer.Geo:
                    // 画布已挪到图框左上角，这里把坐标系还原成整屏坐标
                    DrawSetTransform(-Frame.Position, 0f, Vector2.One);
                    DrawLand();
                    DrawColonies();
                    DrawRiversAndRail();
                    break;

                case MapLayer.Ink:
                    DrawColonialRoutes();
                    DrawAdvanceRoute();
                    DrawCities();
                    DrawPins();
                    DrawAnnotations();
                    break;
            }
        }

        // —— 第 1 层：纸（桌面木纹由底下的 ColorRect 负责）——
        private void DrawPaper()
        {
            var sheet = new Rect2(30, 74, 1220, 566);
            DrawRect(new Rect2(sheet.Position + new Vector2(7, 9), sheet.Size), new Color(0, 0, 0, 0.34f));
            DrawRect(sheet, Paper);

            // 纸纤维与污渍
            for (int i = 0; i < 240; i++)
            {
                var p = new Vector2(34 + Hash(i, 1.3f) * 1212, 78 + Hash(i, 9.7f) * 558);
                DrawCircle(p, 0.7f + Hash(i, 4.1f) * 1.3f,
                    new Color(0.42f, 0.34f, 0.22f, 0.03f + 0.045f * Hash(i, 2.2f)));
            }

            // 一道横向折痕
            DrawLine(new Vector2(30, 452), new Vector2(1250, 448), new Color(0.35f, 0.28f, 0.18f, 0.07f), 1.4f, true);
            DrawLine(new Vector2(30, 453), new Vector2(1250, 449), new Color(1f, 0.98f, 0.92f, 0.10f), 1f, true);

            // 海域铺底：陆地随后盖在上面
            DrawRect(Frame, Sea);
            DrawRect(new Rect2(Frame.Position, new Vector2(Frame.Size.X, 120)),
                new Color(0.42f, 0.50f, 0.52f, 0.05f));
        }

        // —— 第 2 层：经纬网（淡淡的，只在图框内）——
        private void DrawGrid()
        {
            var g = new Color(Ink, 0.055f);
            for (float lon = 120f; lon <= 145f; lon += 10f)
            {
                float x = P(lon, 0f).X;
                DrawLine(new Vector2(x, Frame.Position.Y), new Vector2(x, Frame.End.Y), g, 1f, true);
            }
            for (float lat = 20f; lat <= 50f; lat += 10f)
            {
                float y = P(0f, lat).Y;
                DrawLine(new Vector2(Frame.Position.X, y), new Vector2(Frame.End.X, y), g, 1f, true);
            }
        }

        // —— 第 2 层：海面波纹（被陆地遮盖，只留在海上）——
        private void DrawWaves()
        {
            var c = new Color(0.40f, 0.46f, 0.46f, 0.20f);
            for (int i = 0; i < 150; i++)
            {
                var p = new Vector2(Frame.Position.X + 6 + Hash(i, 13.1f) * (Frame.Size.X - 12),
                                    Frame.Position.Y + 6 + Hash(i, 27.7f) * (Frame.Size.Y - 12));
                DrawLine(p, p + new Vector2(4f, -1.5f), c, 1f, true);
                DrawLine(p + new Vector2(4f, -1.5f), p + new Vector2(8f, 0f), c, 1f, true);
            }
        }

        // —— 第 2 层：图框 ——
        private void DrawFrame()
        {
            DrawRect(Frame.Grow(1f), new Color(Ink, 0.58f), false, 1.5f);
            DrawRect(Frame.Grow(4f), new Color(Ink, 0.24f), false, 1f);
        }

        // —— 第 3 层：陆地（Natural Earth 50m 海岸线，按经纬度投影）——
        private static Vector2[][] _landPts;
        private static Rect2[] _landBox;

        /// <summary>把海岸线投影一次并缓存，供扫描线填色与描边共用。</summary>
        private static void EnsureLandCache()
        {
            if (_landPts != null) return;
            var rings = GeoData.Land;
            _landPts = new Vector2[rings.Length][];
            _landBox = new Rect2[rings.Length];
            for (int k = 0; k < rings.Length; k++)
            {
                var flat = rings[k];
                int n = flat.Length / 2;
                var pts = new Vector2[n];
                float minX = float.MaxValue, maxX = float.MinValue;
                float minY = float.MaxValue, maxY = float.MinValue;
                for (int i = 0; i < n; i++)
                {
                    var p = P(flat[i * 2], flat[i * 2 + 1]);
                    pts[i] = p;
                    minX = Mathf.Min(minX, p.X); maxX = Mathf.Max(maxX, p.X);
                    minY = Mathf.Min(minY, p.Y); maxY = Mathf.Max(maxY, p.Y);
                }
                _landPts[k] = pts;
                _landBox[k] = new Rect2(minX, minY, maxX - minX, maxY - minY);
            }
        }

        private void DrawLand()
        {
            EnsureLandCache();

            // 填色用扫描线（偶数奇偶规则）：大块大陆三角化会失败，
            // 逐行求交最稳，而且内陆湖与内海能自动留空。
            var xs = new System.Collections.Generic.List<float>(64);
            float bottom = Frame.End.Y;
            for (float y = Frame.Position.Y; y < bottom; y += 1f)
            {
                xs.Clear();
                for (int k = 0; k < _landPts.Length; k++)
                {
                    var box = _landBox[k];
                    if (y < box.Position.Y || y > box.End.Y) continue;
                    var pts = _landPts[k];
                    for (int i = 0; i < pts.Length; i++)
                    {
                        var a = pts[i];
                        var b = pts[(i + 1) % pts.Length];
                        if ((a.Y <= y && b.Y > y) || (b.Y <= y && a.Y > y))
                            xs.Add(a.X + (y - a.Y) / (b.Y - a.Y) * (b.X - a.X));
                    }
                }
                xs.Sort();
                for (int k = 0; k + 1 < xs.Count; k += 2)
                    DrawLine(new Vector2(xs[k], y), new Vector2(xs[k + 1], y), LandC, 1.3f, true);
            }

            // 海岸线：先一层淡影，再压一条实线
            var coastEdge = new Color(0.34f, 0.30f, 0.24f, 0.88f);
            for (int k = 0; k < _landPts.Length; k++)
            {
                var pts = _landPts[k];
                if (pts.Length < 3) continue;
                var closed = new Vector2[pts.Length + 1];
                Array.Copy(pts, closed, pts.Length);
                closed[pts.Length] = pts[0];

                bool big = _landBox[k].Size.X > 26f || _landBox[k].Size.Y > 26f;
                if (big)
                    DrawPolyline(closed, new Color(0.38f, 0.42f, 0.44f, 0.16f), 4.2f, true);
                DrawPolyline(closed, coastEdge, big ? 1.5f : 1.0f, true);
            }

            // 山脊符号：只点在几条真实山脉上，给地形一点起伏
            Monts(117.5f, 30.5f, 9, 4.0f, 2.6f);   // 秦岭—淮河一带
            Monts(126.2f, 44.3f, 6, 2.0f, 2.0f);   // 长白
            Monts(120.5f, 47.8f, 7, 2.4f, 2.4f);   // 大兴安岭
            Monts(128.2f, 41.4f, 4, 1.0f, 1.4f);   // 朝鲜北部
        }

        /// <summary>去掉裁剪产生的重合点与共线点，免得三角化失败。</summary>
        private static Vector2[] Clean(Vector2[] src)
        {
            var list = new System.Collections.Generic.List<Vector2>(src.Length);
            foreach (var p in src)
                if (list.Count == 0 || list[^1].DistanceTo(p) > 0.7f) list.Add(p);
            if (list.Count > 2 && list[0].DistanceTo(list[^1]) <= 0.7f) list.RemoveAt(list.Count - 1);

            var res = new System.Collections.Generic.List<Vector2>(list.Count);
            for (int i = 0; i < list.Count; i++)
            {
                var a = list[(i - 1 + list.Count) % list.Count];
                var b = list[i];
                var c = list[(i + 1) % list.Count];
                float cross = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
                if (Mathf.Abs(cross) > 0.35f) res.Add(b);
            }
            return res.ToArray();
        }

        /// <summary>在山脉所在经纬度框内画 ∧ 形山脊符号。</summary>
        private void Monts(float lon, float lat, int n, float spanLon, float spanLat)
        {
            var c = new Color(Ink, 0.26f);
            for (int i = 0; i < n; i++)
            {
                var p = P(lon + (Hash(i * 2 + 1, 3f) - 0.5f) * spanLon,
                          lat + (Hash(i * 2 + 2, 5f) - 0.5f) * spanLat);
                DrawLine(p + new Vector2(-3.2f, 2.2f), p, c, 1f, true);
                DrawLine(p, p + new Vector2(3.2f, 2.2f), c, 1f, true);
            }
        }

        // —— 第 4 层：殖民地着色（1895 台湾 / 1910 朝鲜 / 1931 满洲）——
        private void DrawColonies()
        {
            ColonyRing(GeoData.Korea, 6f);
            ColonyRing(GeoData.Taiwan, 5f);

            // 未读时，两块殖民地在微微发亮
            if (!KoreaRead)
                DrawCircle(P(127f, 37.5f), 15f, new Color(0.55f, 0.20f, 0.16f, 0.10f));
            if (!TaiwanRead)
                DrawCircle(P(121f, 23.6f), 11f, new Color(0.55f, 0.20f, 0.16f, 0.10f));
        }

        /// <summary>一处殖民地：淡底 + 斜线 + 轮廓。太小的岛直接填实，不然斜线会一根都落不进去。</summary>
        private void ColonyRing(float[][] rings, float step)
        {
            foreach (var ring in rings)
            {
                var pts = RingOf(ring);
                if (pts.Length < 3) continue;

                float minY = float.MaxValue, maxY = float.MinValue;
                foreach (var p in pts) { minY = Mathf.Min(minY, p.Y); maxY = Mathf.Max(maxY, p.Y); }

                if (maxY - minY < step * 1.8f)
                    DrawColoredPolygon(pts, new Color(Colony, 0.46f));
                else
                {
                    DrawColoredPolygon(pts, new Color(Colony, 0.26f));
                    Hatch(pts, new Color(Colony, 0.30f), step);
                }
                Outline(pts, new Color(Colony, 0.82f), 1.6f);
            }
        }

        private static Vector2[] RingOf(float[] flat)
        {
            int n = flat.Length / 2;
            var pts = new Vector2[n];
            for (int i = 0; i < n; i++) pts[i] = P(flat[i * 2], flat[i * 2 + 1]);
            return pts;
        }

        /// <summary>在任意多边形内部画斜线（扫描线求交，偶数奇偶规则）。</summary>
        private void Hatch(Vector2[] poly, Color color, float step)
        {
            float minY = float.MaxValue, maxY = float.MinValue;
            foreach (var p in poly) { minY = Mathf.Min(minY, p.Y); maxY = Mathf.Max(maxY, p.Y); }

            var xs = new System.Collections.Generic.List<float>();
            for (float y = minY + step * 0.6f; y <= maxY; y += step)
            {
                xs.Clear();
                for (int i = 0; i < poly.Length; i++)
                {
                    var a = poly[i];
                    var b = poly[(i + 1) % poly.Length];
                    if ((a.Y <= y && b.Y > y) || (b.Y <= y && a.Y > y))
                        xs.Add(a.X + (y - a.Y) / (b.Y - a.Y) * (b.X - a.X));
                }
                xs.Sort();
                for (int k = 0; k + 1 < xs.Count; k += 2)
                    DrawLine(new Vector2(xs[k], y), new Vector2(xs[k + 1], y - step * 0.75f), color, 1f, true);
            }
        }

        private static Vector2[] Close(Vector2[] pts)
        {
            var c = new Vector2[pts.Length + 1];
            Array.Copy(pts, c, pts.Length);
            c[pts.Length] = pts[0];
            return c;
        }

        private void Outline(Vector2[] pts, Color color, float w)
            => DrawPolyline(Close(pts), color, w, true);

        // —— 第 5 层：河流与铁路（全部按经纬度落点）——
        private void DrawRiversAndRail()
        {
            var river = new Color(0.34f, 0.40f, 0.45f, 0.42f);

            Geo(river, 1.7f,                                                  // 黑龙江
                (121f, 53.4f), (123f, 52.6f), (125f, 51.5f), (127f, 50.2f), (129f, 49f),
                (131.5f, 48.2f), (133.5f, 48.6f), (135.4f, 48.4f));
            Geo(new Color(0.34f, 0.40f, 0.45f, 0.34f), 1.2f,                   // 嫩江（下游到江桥渡口）
                (126f, 50.4f), (125.4f, 49.4f), (124.7f, 48.4f), (124.2f, 47.7f), (123.9f, 47.3f),
                (123.6f, 46.9f), (123.35f, 46.45f));
            Geo(river, 1.4f,                                                  // 黄河（含河套）
                (96.5f, 35.2f), (101f, 35.8f), (103.8f, 36.1f), (106.5f, 39f), (108.2f, 40.3f),
                (110.6f, 37.6f), (112.5f, 35.2f), (114.5f, 34.9f), (116.5f, 36.2f), (118.6f, 37.6f));
            Geo(new Color(0.34f, 0.40f, 0.45f, 0.48f), 1.9f,                   // 长江
                (97f, 32.8f), (100f, 30.4f), (103.5f, 29.6f), (106.8f, 30.6f), (109.5f, 30.3f),
                (112f, 30.6f), (115.5f, 30.9f), (118f, 31.6f), (120.5f, 32.2f), (121.8f, 31.6f));

            // 中东铁路 / 南满铁路：满洲里—海拉尔—齐齐哈尔—哈尔滨—长春—沈阳—大连
            Rail((117.4f, 49.6f), (120.5f, 48.6f), (123.9f, 47.3f), (126.6f, 45.8f),
                 (125.3f, 43.9f), (123.4f, 41.8f), (121.6f, 38.9f));
            // 朝鲜铁路：釜山—汉城—平壤—新义州
            Rail((129f, 35.1f), (127f, 37.5f), (125.7f, 39f), (124.4f, 40.1f));
        }

        private void Geo(Color color, float width, params (float Lon, float Lat)[] geo)
            => DrawPolyline(L(geo), color, width, true);

        /// <summary>铁路：一条细线 + 沿线等距枕木。</summary>
        private void Rail(params (float Lon, float Lat)[] geo)
        {
            var pts = L(geo);
            var c = new Color(0.22f, 0.18f, 0.14f, 0.58f);
            DrawPolyline(pts, c, 1.5f, true);

            float travel = 0f;
            for (int i = 0; i < pts.Length - 1; i++)
            {
                var a = pts[i];
                var b = pts[i + 1];
                float len = a.DistanceTo(b);
                if (len < 0.01f) continue;
                var dir = (b - a) / len;
                var nrm = new Vector2(-dir.Y, dir.X) * 2.1f;
                for (float t = 0f; t < len; t += 1f)
                {
                    if (Mathf.PosMod(travel + t, 7f) > 1.2f) continue;
                    var p = a + dir * t;
                    DrawLine(p - nrm, p + nrm, c, 1f, true);
                }
                travel += len;
            }
        }

        // —— 第 6 层：殖民航线（读纸条后逐条出现）——
        private void DrawColonialRoutes()
        {
            var sea = new Color(0.20f, 0.26f, 0.32f, 0.72f);
            var tokyo = P(139.7f, 35.7f);

            if (TaiwanRead)
            {
                var end = P(121.5f, 25.2f);
                var p = Curved(tokyo, (tokyo + end) * 0.5f + new Vector2(46f, 22f), end);
                DrawDashed(p, sea, 1.8f, 9f, 6f);
                DrawArrowHead(p[^1], (p[^1] - p[^2]).Normalized(), new Color(sea, 0.9f), 7f);
                DrawCircle(p[^1], 3f, new Color(sea, 0.95f));
            }
            if (KoreaRead)
            {
                var end = P(127f, 37.5f);
                var p = Curved(tokyo, (tokyo + end) * 0.5f + new Vector2(42f, -16f), end);
                DrawDashed(p, sea, 1.8f, 9f, 6f);
                DrawArrowHead(p[^1], (p[^1] - p[^2]).Normalized(), new Color(sea, 0.9f), 7f);
                DrawCircle(p[^1], 3f, new Color(sea, 0.95f));
            }
        }

        // —— 第 7 层：1931 进军箭头（两页档案读完后动画生长）——
        private void DrawAdvanceRoute()
        {
            if (!RouteOn) return;

            // 史实顺序：汉城 → 平壤 → 安东 → 沈阳 → 长春 → 哈尔滨 → 齐齐哈尔 → 江桥
            // v2.9: the terminus is the real bridge site south of Qiqihar
            // (Taolai, 123.35E 46.45N), not Nenjiang city up north.
            var key = new[]
            {
                P(127f, 37.5f), P(125.7f, 39f), P(124.4f, 40.1f), P(123.4f, 41.8f),
                P(125.3f, 43.9f), P(126.6f, 45.8f), P(123.9f, 47.3f), P(123.35f, 46.45f),
            };
            var path = CurvedPath(key, 0.14f);
            int count = Mathf.Clamp((int)(path.Count * Progress), 2, path.Count);
            var part = new Vector2[count];
            Array.Copy(path.ToArray(), part, count);

            // 战区走廊：宽而淡的朱红带，先让人觉得“这里在推进”
            DrawPolyline(part, new Color(Route, 0.07f), 19f, true);
            DrawPolyline(part, new Color(Route, 0.11f), 10f, true);

            // 墨影 + 纸缝留白 + 朱红主线（三层，制造“压在纸上的厚度”）
            DrawPolyline(part, new Color(0.10f, 0.08f, 0.06f, 0.30f), 9f, true);
            DrawPolyline(part, new Color(0.96f, 0.92f, 0.82f, 0.62f), 5.6f, true);
            DrawPolyline(part, Route, 3.8f, true);

            // 沿途站点：已抵达的画成实心圆，像作战图上的脚印
            foreach (var p in key)
            {
                DrawCircle(p + new Vector2(1f, 1.6f), 3.6f, new Color(0, 0, 0, 0.20f));
                DrawCircle(p, 3.2f, new Color(0.96f, 0.92f, 0.82f, 0.95f));
                DrawCircle(p, 2.2f, new Color(0.42f, 0.14f, 0.11f, 0.92f));
            }
            if (count > 3)
            {
                var dir = (part[count - 1] - part[count - 3]).Normalized();
                DrawArrowHead(part[count - 1], dir, Route, 12f);
            }
        }

        // —— 第 8 层：路线的落脚城镇 + 图钉 ——
        private void DrawCities()
        {
            // 主城
            foreach (var p in new[] { P(127f, 37.5f), P(123.4f, 41.8f), P(125.3f, 43.9f), P(123.9f, 47.3f) })
                City(p, 5.2f, 3.0f);
            // 小站
            foreach (var p in new[] { P(125.7f, 39f), P(124.4f, 40.1f), P(126.6f, 45.8f) })
                City(p, 3.4f, 2.0f);
        }

        private void City(Vector2 p, float r1, float r2)
        {
            DrawCircle(p, r1, new Color(0.96f, 0.92f, 0.82f, 0.85f));
            DrawCircle(p, r2, new Color(Ink, 0.80f));
        }

        private void DrawPins()
        {
            Pin(P(123.35f, 46.45f), GameState.I.S.JiangqiaoDone, true); // 嫩江 · 江桥（泰来）
            Pin(P(126.6f, 45.8f), false, false);                        // 哈尔滨 · 731
            Pin(P(118.8f, 32f), false, false);                          // 南京
        }

        private void Pin(Vector2 p, bool done, bool key)
        {
            var c = key ? new Color(0.62f, 0.21f, 0.16f) : new Color(Ink, 0.45f);
            DrawCircle(p + new Vector2(1.5f, 2.5f), 5f, new Color(0, 0, 0, 0.25f));
            DrawCircle(p, 4.2f, c);
            DrawCircle(p, 1.6f, new Color(0.93f, 0.88f, 0.76f));
            if (done)
                DrawArc(p, 8f, 0, Mathf.Tau, 20, new Color(0.42f, 0.55f, 0.35f), 1.6f, true);
        }

        // —— 第 9 层：注记（罗盘、比例尺、手写标记）——
        private void DrawAnnotations()
        {
            // 罗盘：放在右下海域（不压陆地）
            var c = P(142.8f, 23.2f);
            DrawCircle(c, 22, new Color(0.40f, 0.34f, 0.26f, 0.10f));
            DrawArc(c, 21, 0, Mathf.Tau, 40, new Color(Ink, 0.32f), 1.2f, true);
            DrawLine(c + new Vector2(0, -17), c + new Vector2(0, 17), new Color(Ink, 0.42f), 1.1f, true);
            DrawLine(c + new Vector2(-17, 0), c + new Vector2(17, 0), new Color(Ink, 0.42f), 1.1f, true);
            DrawColoredPolygon(new[]
            {
                c + new Vector2(0, -19), c + new Vector2(5, -3), c + new Vector2(-5, -3),
            }, new Color(0.55f, 0.20f, 0.16f, 0.72f));

            // 比例尺与图例：压在图框内的空海域上，像真的作战图
            var s = new Vector2(72, 588);
            DrawLine(s, s + new Vector2(120, 0), new Color(Ink, 0.46f), 1.3f, true);
            for (int i = 0; i <= 4; i++)
                DrawLine(s + new Vector2(i * 30, -3.5f), s + new Vector2(i * 30, 3.5f), new Color(Ink, 0.46f), 1.1f, true);
            DrawString(ThemeDB.FallbackFont, s + new Vector2(126, 4), "500 km",
                HorizontalAlignment.Left, -1, 10, new Color(Ink, 0.44f));

            // 图例：已并入大日本帝国版图（着色 + 斜线）
            var lr = new Rect2(240, 581, 18, 9);
            DrawRect(lr, new Color(Colony, 0.32f));
            DrawRect(lr, new Color(Colony, 0.88f), false, 1.1f);
            DrawLine(lr.Position + new Vector2(2, 7), lr.Position + new Vector2(16, 2), new Color(Colony, 0.55f), 1f);
            DrawString(ThemeDB.FallbackFont, new Vector2(264, 589), "1895 / 1910 已并入大日本帝国版图",
                HorizontalAlignment.Left, -1, 10, new Color(Ink, 0.44f));

            // 手写圈注：1931 的终点（嫩江江桥）
            if (RouteOn)
            {
                float pulse = 0.55f + 0.2f * Mathf.Sin((float)Time.GetTicksMsec() / 420f);
                DrawArc(P(123.35f, 46.45f), 15f + pulse * 3f, 0, Mathf.Tau, 28,
                    new Color(Route, 0.55f), 1.4f, true);
            }
        }

        // ================= 绘制工具 =================

        private static Vector2[] Curved(Vector2 a, Vector2 control, Vector2 b, int steps = 24)
        {
            var pts = new Vector2[steps + 1];
            for (int i = 0; i <= steps; i++)
            {
                float t = (float)i / steps;
                pts[i] = a.Lerp(control, t).Lerp(control.Lerp(b, t), t);
            }
            return pts;
        }

        private static System.Collections.Generic.List<Vector2> CurvedPath(Vector2[] key, float bend)
        {
            var all = new System.Collections.Generic.List<Vector2>();
            for (int i = 0; i < key.Length - 1; i++)
            {
                var a = key[i];
                var b = key[i + 1];
                var mid = (a + b) * 0.5f;
                var dir = (b - a).Normalized();
                var perp = new Vector2(-dir.Y, dir.X) * (bend * a.DistanceTo(b));
                var seg = Curved(a, mid + perp, b, 8);
                for (int j = 0; j < seg.Length - 1; j++) all.Add(seg[j]);
            }
            all.Add(key[^1]);
            return all;
        }

        private void DrawDashed(Vector2[] pts, Color color, float width, float dash, float gap)
        {
            float travel = 0f;
            bool on = true;
            for (int i = 0; i < pts.Length - 1; i++)
            {
                var a = pts[i];
                var b = pts[i + 1];
                float len = a.DistanceTo(b);
                float t = 0f;
                while (t < len)
                {
                    float step = Mathf.Min(on ? dash : gap, len - t);
                    if (on)
                        DrawLine(a + (b - a) * (t / len), a + (b - a) * ((t + step) / len), color, width, true);
                    t += step;
                    on = !on;
                    travel += step;
                }
            }
            _ = travel;
        }

        private void DrawArrowHead(Vector2 tip, Vector2 dir, Color color, float size)
        {
            var perp = new Vector2(-dir.Y, dir.X);
            var a = tip - dir * size + perp * size * 0.55f;
            var b = tip - dir * size - perp * size * 0.55f;
            DrawColoredPolygon(new[] { tip, a, b }, color);
            DrawPolyline(new[] { a, tip, b }, new Color(0.12f, 0.09f, 0.07f, 0.5f), 1.2f, true);
        }
    }
}

/// <summary>
/// 纸条档案浮层：旧纸面板 + 条约/殖民动机/代价 + 出处。
/// 读完后关闭，回到地图时由回调刷新状态。
/// </summary>
public partial class FieldNoteUi : CanvasLayer
{
    public event Action Closed;

    private readonly string _date;
    private readonly string _title;
    private readonly string[] _paragraphs;
    private readonly string _source;
    private readonly Action _onClose;

    public FieldNoteUi(string date, string title, string[] paragraphs, string source, Action onClose)
    {
        _date = date;
        _title = title;
        _paragraphs = paragraphs;
        _source = source;
        _onClose = onClose;
        Layer = 20;
    }

    public override void _Ready()
    {
        var dim = new ColorRect { Color = new Color(0.04f, 0.035f, 0.03f, 0.72f) };
        dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(dim);

        var center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(center);

        var panel = new PanelContainer { CustomMinimumSize = new Vector2(940, 0) };
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.905f, 0.855f, 0.735f),
            BorderColor = new Color(0.24f, 0.20f, 0.16f),
            BorderWidthLeft = 2, BorderWidthRight = 2, BorderWidthTop = 2, BorderWidthBottom = 2,
            ContentMarginLeft = 34, ContentMarginRight = 34, ContentMarginTop = 26, ContentMarginBottom = 24,
            ShadowColor = new Color(0, 0, 0, 0.45f), ShadowSize = 14, ShadowOffset = new Vector2(6, 8),
        };
        panel.AddThemeStyleboxOverride("panel", style);
        panel.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        panel.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        center.AddChild(panel);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 14);
        panel.AddChild(box);

        var date = new Label { Text = _date };
        date.AddThemeFontSizeOverride("font_size", 14);
        date.AddThemeColorOverride("font_color", new Color(0.48f, 0.43f, 0.35f));
        box.AddChild(date);

        var title = new Label { Text = _title };
        title.AddThemeFontSizeOverride("font_size", 25);
        title.AddThemeColorOverride("font_color", new Color(0.16f, 0.14f, 0.12f));
        title.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        title.CustomMinimumSize = new Vector2(860, 0);
        box.AddChild(title);

        var rule = new ColorRect
        {
            Color = new Color(0.55f, 0.20f, 0.16f, 0.75f),
            CustomMinimumSize = new Vector2(0, 2),
        };
        box.AddChild(rule);

        foreach (var p in _paragraphs)
        {
            var l = new Label { Text = p };
            l.AddThemeFontSizeOverride("font_size", 16);
            l.AddThemeColorOverride("font_color", new Color(0.22f, 0.19f, 0.16f));
            l.AddThemeConstantOverride("line_spacing", 4);
            l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            l.CustomMinimumSize = new Vector2(860, 0);
            box.AddChild(l);
        }

        var source = new Label { Text = _source };
        source.AddThemeFontSizeOverride("font_size", 13);
        source.AddThemeColorOverride("font_color", new Color(0.45f, 0.40f, 0.33f));
        source.AddThemeConstantOverride("line_spacing", 3);
        source.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        source.CustomMinimumSize = new Vector2(860, 0);
        box.AddChild(source);

        var close = new Button { Text = "收起纸条" };
        close.AddThemeFontSizeOverride("font_size", 16);
        close.CustomMinimumSize = new Vector2(170, 38);
        close.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd;
        close.Pressed += Close;
        box.AddChild(close);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e.IsActionPressed("pause"))
        {
            Close();
            GetViewport().SetInputAsHandled();
        }
    }

    private void Close()
    {
        _onClose?.Invoke();
        Closed?.Invoke();
        QueueFree();
    }
}
