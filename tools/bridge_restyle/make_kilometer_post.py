"""v3.87 · 南满铁路里程标：按真实形制 + 全局钢笔淡彩风格重绘为贴图资产。

用户拍板：「如果是独有地面标记的话，麻烦你把这些东西按照游戏美术风格和真实历史里面的
再次按照整体游戏风格再次生成和做适当处理」。

诊断结论（v3.87 前）：`N918Level.DrawRailEmbankment()` 里 3811–3825 是纯 `DrawRect` /
`DrawLine` 画出来的白石碑 —— 规整白色矩形 + 3px 工整黑框 + 3 条等距横线，读作打印表格，
与旁边手绘道砟 / 枯草 / 水墨远山完全不是一支笔。它不是多余物：
`docs/V367_HISTORICAL_SCENES.md:37` 把「南满铁路里程碑」列为九一八关卡的**独有地面标记（B 级合理复原）**。

史实形制依据（场景级，不臆造具体里程数值）：
  * 公里标 / kilometer post：每一整公里设一处，位于线路左侧、距线路中心 >= 3.1 m；
    多为石制或混凝土矮方柱，白底黑字或阴刻数字（见铁路标志规范、百度百科「公里标」）。
  * 1931 年以前曾使用苏州码子阴刻里志牌，其后「阴刻被水泥抹平、其上刷阿拉伯数字」；
    野外经风雨后白灰刷面斑驳发黄、带土渍与崩角，绝不是画面里那块纯白工整牌。
  * 因此本贴图**不写任何可读的具体里程数字**：只留被水泥抹平、被风化打断的模糊刻字残痕，
    与 `V367_HISTORICAL_SCENES.md:15-17`「编号牌属场景级示意，不宣称与某件实物一一对应」一致。

风格（唯一锚点，用户 2026-11-13 亲自纠正）：
  钢笔淡彩 / 水墨手绘 —— 细而不均、随形体断续的墨线；斑驳罩染有渐层笔触；
  柔和淡墨阴影；暖米灰而非冷白。体积靠「左暗面 / 右受光棱 / 顶面斜切」的连续罩染渐层，
  不靠三档硬边平涂。底纹取自 `door_leaf_burnt.png` 同源裁片（与 v3.81 的
  `liutiaohu_sign_wood.png` 完全同一支笔），去饱和压成风化石面。运行时只 `DrawTextureRect`，
  零程序化几何。

产物：
  assets/art/props/railway_kilometer_post.png                 (入库，96x184 RGBA，4x 超采样)
  tools/bridge_restyle/previews/railway_kilometer_post_CANDIDATE.png (候选过目，不入库)
"""

from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = Path(__file__).resolve().parents[2]
SRC = ROOT / "assets/art/props/door_leaf_burnt.png"
SHOT = ROOT / "docs/screenshots/n918_04_radio_station.png"
OUT = ROOT / "assets/art/props/railway_kilometer_post.png"
PREVIEW = ROOT / "tools/bridge_restyle/previews/railway_kilometer_post_CANDIDATE.png"

# 运行时绘制目标仍是 Rect2(x, GroundY + 12, 24, 46)，故贴图按 4x 超采样（96x184，同长宽比）。
W, H = 96, 184
rng = np.random.default_rng(19310918)


def smooth(a, b, x):
    t = np.clip((x - a) / (b - a), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


def lowfreq(shape, cell, seed):
    """低频噪声：小随机图双线性放大，得到连续斑驳（不是逐像素白噪）。

    与 make_contact_shadow.py / make_burnt_paper_textures.py 共用同一套罩染算法。
    """
    r = np.random.default_rng(seed)
    h = max(2, shape[0] // cell + 2)
    w = max(2, shape[1] // cell + 2)
    small = r.random((h, w)).astype(np.float32)
    im = Image.fromarray((small * 255).astype(np.uint8), "L").resize(
        (shape[1], shape[0]), Image.BILINEAR
    )
    return np.asarray(im, dtype=np.float32) / 255.0


yy, xx = np.mgrid[0:H, 0:W].astype(np.float32)

# ---------------------------------------------------------------- 石柱轮廓
# 石制矮方柱：边线接近直线（只轻微起伏），顶部微斜排水面，底边埋进道砟。
# 加入几处小的斜切崩角，打破左右对称。
top = 9.0 + 1.9 * np.sin(xx / 23.0 + 0.5) + 0.7 * np.sin(xx / 9.0 - 1.1)
left = 9.0 + 1.3 * np.sin(yy / 34.0 + 0.4) + 0.5 * np.sin(yy / 12.0)
right = 87.0 - 1.1 * np.sin(yy / 31.0 - 0.7) - 0.5 * np.sin(yy / 13.0)
bottom = 173.0 + 1.6 * np.sin(xx / 25.0 + 1.2)

inside = (yy >= top) & (yy <= bottom) & (xx >= left) & (xx <= right)
# 风化崩角：小的斜切三角（不是圆弧撕纸边），分布在四角及右缘一处
chip_tr = (xx > 74) & (yy < 24) & ((xx - 74) * 0.95 + (24 - yy) * 0.8 > 10.0)
chip_bl = (xx < 17) & (yy > 155) & ((17 - xx) * 0.9 + (yy - 155) * 0.85 > 9.0)
chip_tl = (xx < 18) & (yy < 16) & ((18 - xx) * 0.8 + (16 - yy) * 1.0 > 7.0)
chip_r = (xx > 82) & (yy > 96) & (yy < 128) & ((xx - 82) * 1.1 + 4.0 > 7.0)
inside &= ~(chip_tr | chip_bl | chip_tl | chip_r)

mask_img = Image.fromarray((inside * 255).astype(np.uint8), "L").filter(
    ImageFilter.GaussianBlur(0.5)
)
mask = np.asarray(mask_img, dtype=np.float32) / 255.0
flake = lowfreq((H, W), 5, 71)
mask *= np.clip(0.88 + 0.18 * flake, 0.0, 1.0)  # 白灰刷面局部剥落，但整体是实石
mask = np.clip(mask, 0.0, 1.0)

# ---------------------------------------------------------------- 石面罩染
# 同源笔触：门板裁片去饱和压低强度，只借它的细密断续纹理，不读作木纹 / 纸纹。
src = Image.open(SRC).convert("RGB")
tex = np.asarray(
    src.crop((26, 36, 118, 300)).resize((W, H), Image.BILINEAR), dtype=np.float32
) / 255.0
luma = tex[..., 0] * 0.299 + tex[..., 1] * 0.587 + tex[..., 2] * 0.114
detail = (luma - luma.mean()) * 11.0

n1 = lowfreq((H, W), 19, 11)
n2 = lowfreq((H, W), 8, 29)
n3 = lowfreq((H, W), 3, 47)
wash = (n1 - 0.5) * 30.0 + (n2 - 0.5) * 13.0 + (n3 - 0.5) * 6.0

# 体积：左暗面 -> 向右渐亮 + 右缘受光窄棱 + 顶面斜切亮带（连续罩染，不是三档平涂）
u = np.clip((xx - left) / np.maximum(right - left, 1.0), 0.0, 1.0)
shade = -38.0 * (1.0 - u) + 11.0 * u
lit_edge = 22.0 * np.exp(-(((u - 0.90) / 0.06) ** 2))
top_band = 15.0 * smooth(top + 13.0, top + 1.0, yy)
volume = shade + lit_edge + top_band

soil = np.clip((yy - 130.0) / 46.0, 0.0, 1.0) ** 1.7 * 40.0
stain = np.clip(n1 - 0.62, 0.0, 1.0) ** 1.3 * 22.0

base = np.array([184.0, 171.0, 147.0], dtype=np.float32)  # 风化暖米灰石灰刷面，非冷白
rgb = (
    base[None, None, :]
    + detail[..., None] * 0.55
    + wash[..., None]
    + volume[..., None]
    - soil[..., None] * np.array([0.80, 0.95, 1.15], dtype=np.float32)
    - stain[..., None] * np.array([0.60, 0.90, 1.20], dtype=np.float32)
)
rgb += rng.normal(0.0, 1.6, (H, W, 1)).astype(np.float32)
rgb = np.clip(rgb, 0.0, 255.0)

# ---------------------------------------------------------------- 接地淡墨阴影
# 压扁椭圆 + 三频角度扰动（复用 v3.86 contact_shadow.png 的不规则手绘边算法）。
# 比石柱略宽、向两侧与下方延展，柔和不成硬线；中心下移使可见部分是柔和外圈而非实心核。
sy = (yy - 185.0) / 26.0
sx = (xx - 48.0) / 62.0
theta = np.arctan2(sy, sx)
wobble = (
    1.0
    + 0.10 * np.sin(3.0 * theta + 0.7)
    + 0.06 * np.sin(7.0 * theta - 1.2)
    + 0.03 * np.sin(11.0 * theta + 2.1)
)
r = np.sqrt(sx * sx + sy * sy) / wobble
sa = np.clip(1.0 - r, 0.0, 1.0) ** 1.3 * 0.30
sa *= 0.78 + 0.30 * n2
# 只在石柱外侧（不覆盖柱身）显影：石柱下方 / 两侧
sa *= 1.0 - smooth(150.0, 168.0, yy) * (1.0 - smooth(6.0, 14.0, xx) * (1.0 - smooth(82.0, 90.0, xx)))
shadow_rgb = np.array([58.0, 46.0, 34.0], dtype=np.float32)[None, None, :] * (
    0.90 + 0.22 * n1
)[..., None]
shadow = np.dstack(
    [np.clip(shadow_rgb, 0, 255), np.clip(sa, 0.0, 0.46) * 255.0]
).astype(np.uint8)

# ---------------------------------------------------------------- 断续墨线
ink_img = Image.new("RGBA", (W, H), (0, 0, 0, 0))
d = ImageDraw.Draw(ink_img)
INK = (38, 29, 22)


def broken_polyline(pts, seg_range=(4, 7), widths=(1, 1, 2), alphas=(170, 240), skip=0.22):
    """长段连续 + 偶发断口：把边切成数段，段内一笔连续画完，段间留缺口。

    得到「细而不均、随形体断续的墨线」，不是等距虚线、不是工整描边。
    """
    n = len(pts)
    if n < 4:
        return
    k = int(rng.integers(seg_range[0], seg_range[1] + 1))
    cuts = sorted(rng.choice(np.arange(2, n - 2), size=k - 1, replace=False).tolist())
    bounds = [0] + cuts + [n - 1]
    for s, e in zip(bounds[:-1], bounds[1:]):
        if rng.random() < skip:  # 整段跳过 = 明显断口
            continue
        a = int(rng.integers(alphas[0], alphas[1]))
        w = int(rng.choice(widths))
        jx, jy = float(rng.normal(0, 0.6)), float(rng.normal(0, 0.6))
        gap = int(rng.integers(0, 3))
        e2 = max(s + 2, e - gap)
        d.line(
            [(pts[i][0] + jx, pts[i][1] + jy) for i in range(s, e2 + 1)],
            fill=(*INK, a),
            width=w,
            joint="curve",
        )


def inner_ghost(pts, alphas=(90, 150), skip=0.45):
    """内移一道更淡更细的墨痕，制造「细而不均、随形体断续」的双层笔触感。"""
    n = len(pts)
    if n < 4:
        return
    k = int(rng.integers(3, 6))
    cuts = sorted(rng.choice(np.arange(2, n - 2), size=k - 1, replace=False).tolist())
    bounds = [0] + cuts + [n - 1]
    for s, e in zip(bounds[:-1], bounds[1:]):
        if rng.random() < skip:
            continue
        a = int(rng.integers(alphas[0], alphas[1]))
        jx, jy = float(rng.normal(0, 0.8)), float(rng.normal(0, 0.8))
        d.line(
            [(pts[i][0] + jx, pts[i][1] + jy) for i in range(s, e + 1)],
            fill=(*INK, a),
            width=1,
            joint="curve",
        )


left_pts = [
    (9.0 + 1.3 * np.sin(y / 34.0 + 0.4) + 0.5 * np.sin(y / 12.0) + 0.5, float(y))
    for y in np.linspace(12, 171, 56)
]
right_pts = [
    (87.0 - 1.1 * np.sin(y / 31.0 - 0.7) - 0.5 * np.sin(y / 13.0) - 0.5, float(y))
    for y in np.linspace(14, 171, 52)
]
top_pts = [
    (float(x), 9.0 + 1.9 * np.sin(x / 23.0 + 0.5) + 0.7 * np.sin(x / 9.0 - 1.1) + 0.4)
    for x in np.linspace(9, 86, 36)
]
broken_polyline(left_pts, (5, 8), (1, 1, 2), (180, 240), skip=0.18)    # 左缘（背光）最重
broken_polyline(right_pts, (5, 7), (1, 1), (150, 215), skip=0.22)     # 右缘（受光）淡而断
broken_polyline(top_pts, (4, 6), (1, 1, 2), (165, 230), skip=0.20)    # 斜切顶缘
inner_ghost(left_pts, (90, 150), 0.45)
inner_ghost(right_pts, (70, 120), 0.5)

# 左暗棱与右受光棱交界：两道极淡竖向水痕（不是描边）
for bx, amp, al in ((27.0, 1.7, (45, 95)), (73.0, 1.3, (35, 80))):
    pts = [
        (bx + amp * np.sin(y / 37.0 + bx), float(y)) for y in np.linspace(20, 160, 38)
    ]
    broken_polyline(pts, (4, 6), (1,), al, skip=0.30)

# 面部两道风化竖向裂纹（细墨线，断续）
for cx in (40.0, 60.0):
    pts = [
        (cx + 1.4 * np.sin(y / 41.0 + cx), float(y)) for y in np.linspace(26, 150, 40)
    ]
    broken_polyline(pts, (4, 6), (1,), (60, 120), skip=0.35)

# 底缘埋进道砟，只留几段短墨点，不画完整封口线
for x0 in (14, 30, 50, 70):
    if rng.random() < 0.75:
        d.line(
            [(x0, 171.0 + float(rng.normal(0, 1))),
             (x0 + float(rng.uniform(5, 11)), 172.0 + float(rng.normal(0, 1)))],
            fill=(*INK, int(rng.integers(70, 140))),
            width=1,
        )

# 阴刻数字残痕：只留被水泥抹平 / 风化打断的模糊笔画，绝不写可读里程数值
glyphs = [
    ((22, 54), (33, 54)), ((39, 52), (39, 70)), ((25, 74), (36, 72)),
    ((52, 58), (66, 57)), ((58, 56), (58, 76)), ((50, 80), (68, 79)),
    ((23, 102), (40, 100)), ((31, 98), (31, 116)),
]
for g in glyphs:
    if rng.random() < 0.30:
        continue
    seg = np.linspace(0.0, 1.0, 10)
    pts = [
        (g[0][0] + (g[1][0] - g[0][0]) * t + float(rng.normal(0, 0.4)),
         g[0][1] + (g[1][1] - g[0][1]) * t + float(rng.normal(0, 0.4)))
        for t in seg
    ]
    broken_polyline(pts, (2, 3), (1, 1, 2), (40, 100), skip=0.30)

ink_arr = np.array(ink_img, dtype=np.float32).copy()
erase = np.clip((n3 - 0.55) * 2.2, 0.0, 1.0)   # 高频斑驳把刻字再打断
ink_arr[..., 3] *= 1.0 - 0.55 * erase
ink_arr[..., 3] *= mask                          # 墨线只落在石面内
ink_arr = np.clip(ink_arr, 0, 255).astype(np.uint8)

# ---------------------------------------------------------------- 合成
canvas = Image.new("RGBA", (W, H), (0, 0, 0, 0))
canvas.alpha_composite(Image.fromarray(shadow, "RGBA"))
stone = np.dstack([rgb, mask * 255.0]).astype(np.uint8)
canvas.alpha_composite(Image.fromarray(stone, "RGBA"))
canvas.alpha_composite(Image.fromarray(ink_arr, "RGBA"))

OUT.parent.mkdir(parents=True, exist_ok=True)
canvas.save(OUT)
print("saved", OUT, canvas.size, "alpha max=%.3f" % (np.asarray(canvas)[..., 3].max() / 255.0))

# ---------------------------------------------------------------- 候选过目板
PREVIEW.parent.mkdir(parents=True, exist_ok=True)
BW, BH = 1000, 760
board = Image.new("RGB", (BW, BH), (238, 230, 214))
bd = ImageDraw.Draw(board)

font_path = ROOT / "assets/fonts/NotoSansCJKsc-Regular.otf"
try:
    f_title = ImageFont.truetype(str(font_path), 20)
    f_label = ImageFont.truetype(str(font_path), 15)
except Exception:
    f_title = ImageFont.load_default()
    f_label = ImageFont.load_default()

bd.text((20, 12), "v3.87 里程标重绘候选 · 改前(程序化白块) vs 改后(同源钢笔淡彩贴图)",
        fill=(52, 40, 30), font=f_title)

# 旧版复刻：严格按 N918Level.cs 原代码 4x 放大
old = Image.new("RGBA", (W, H), (0, 0, 0, 0))
od = ImageDraw.Draw(old)
od.rectangle([0, 0, W - 1, H - 1], fill=(214, 207, 186, 245))
od.rectangle([0, 128, W - 1, H - 1], fill=(148, 140, 122, 245))
od.rectangle([0, 0, W - 1, H - 1], outline=(36, 26, 20, 235), width=12)
for k in range(3):
    od.line([(24, 40 + k * 32), (72, 40 + k * 32)], fill=(36, 26, 20, 235), width=8)

zoom = 2
panels = [
    (20, "改前 · 规整白块 + 工整黑框", old, (246, 240, 228)),
    (260, "改后 · 风化石柱 / 断续墨线", canvas, (246, 240, 228)),
    (500, "改后 · 贴在暖褐地面上看融入", canvas, (176, 158, 122)),
]
for px, label, img, bg in panels:
    z = img.resize((W * zoom, H * zoom), Image.LANCZOS)
    bd.rectangle([px - 6, 46, px + W * zoom + 6, 46 + H * zoom + 6], fill=bg,
                 outline=(180, 168, 146), width=1)
    board.paste(z, (px, 46), z)
    bd.text((px, 46 + H * zoom + 12), label, fill=(70, 56, 42), font=f_label)

# 下排：真实游戏帧局部（左=改前原样，右=同位置换成新贴图）。
# 白牌在 n918_04_radio_station.png 屏幕坐标约 x∈{355,975}、y∈[520,560]；取左块裁 (320,490,460,600)。
if SHOT.exists():
    shot = Image.open(SHOT).convert("RGB")
    region = shot.crop((320, 490, 460, 600))     # 140x110，旧白牌落在本地约 (35,30)
    changed = region.copy()
    cover = canvas.resize((30, 58), Image.LANCZOS)
    changed.paste(cover, (33, 28), cover)

    y0 = 442
    for i, (im, label) in enumerate(((region, "改前 · 真实游戏帧局部（旧白牌）"),
                                     (changed, "改后 · 同位置换成贴图（含接地淡墨）"))):
        big = im.resize((220, 220), Image.LANCZOS)
        bx = 20 + i * 300
        bd.rectangle([bx - 4, y0 - 4, bx + 224, y0 + 224], outline=(180, 168, 146), width=1)
        board.paste(big, (bx, y0))
        bd.text((bx, y0 + 230), label, fill=(70, 56, 42), font=f_label)

bd.text((20, BH - 30),
        "候选仅作过目：正式资产 assets/art/props/railway_kilometer_post.png；本图不入库。",
        fill=(110, 94, 74), font=f_label)
board.save(PREVIEW)
print("saved", PREVIEW, board.size)
