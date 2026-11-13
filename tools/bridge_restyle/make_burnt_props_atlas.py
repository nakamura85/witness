# -*- coding: utf-8 -*-
"""
v3.82 / 烧毁仓库专用道具图集生成器（贴图资产制作工具，不是运行时程序化绘制）

用户 2026-11-13 反馈：「这个部分美术画风是对了，但是因为你给的图片是烧坏的房子，
那些橱柜或者灶子等等物品应该有烧坏的迹象和款式，还有各自的尺寸也没有把握好。」

问题：v3.81 的 props_atlas.png 是从 residence.png / comm_outpost.png（完好住家、机房）
裁出来的，木柜 / 砖灶 / 竹箩 / 茶壶 / 电表全都完好鲜亮、还带着红砖墙背景，
叠到 burnt_warehouse.png 的焦黑废墟上就成了「亮橙色小贴纸」。

本脚本只产出 **PNG 贴图资产**，运行时仍然是 DrawTextureRectRegion：
  1. 全部裁片从同一批钢笔淡彩·水墨室内贴图取像素（同一支笔、同一套光照），
     其中灰堆 / 碎砖 / 断木梁直接取自 burnt_warehouse.png 自身 —— 零画风风险。
  2. 完好家具轮廓做「焦化后处理」：降饱和 → 压暗 → 焦褐罩染 → 斑驳烟熏 →
     边缘炭黑 → 烧穿缺角 → 不规则羽化透明底（不再有硬边方块、不再带砖墙）。
  3. 4x4 共 16 格（640x480，每格 160x120）：前 12 格 = 可拾取物件，
     后 4 格 = 家具前脸（焦黑橱柜 / 塌半边砖灶 / 断桌板 / 烧卷挂画）。

产物：
  assets/art/interior/props_atlas_burnt.png          （新增，不覆盖原 props_atlas.png）
  tools/bridge_restyle/previews/props_atlas_burnt_CANDIDATE.png   （候选，供过目）
  tools/bridge_restyle/previews/props_burnt_before_after.png       （房间模拟对照）
"""
import math
import os
import random
import shutil

from PIL import Image, ImageChops, ImageDraw, ImageEnhance, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))                 # tools/bridge_restyle
ROOT = os.path.dirname(os.path.dirname(HERE))                     # 项目根
ART = os.path.join(ROOT, "assets", "art", "interior")
PREV = os.path.join(ROOT, "tools", "bridge_restyle", "previews")
os.makedirs(PREV, exist_ok=True)

CW, CH, COLS = 160, 120, 4

atlas = Image.open(os.path.join(ART, "props_atlas.png")).convert("RGBA")
burnt = Image.open(os.path.join(ART, "burnt_warehouse.png")).convert("RGB")
com = Image.open(os.path.join(ART, "comm_outpost.png")).convert("RGB")  # 干净的砖灶（不带竹箩堆）
assert atlas.size == (640, 360), atlas.size
assert burnt.size == (1280, 800), burnt.size


def cell(i):
    """原图集第 i 格（4x3）。"""
    return atlas.crop(((i % 4) * CW, (i // 4) * CH,
                       (i % 4) * CW + CW, (i // 4) * CH + CH))


def crop_any(im, box):
    """从任意同源室内贴图裁一块，等比缩放进 160x120 格（留白透明，交给 char_burn 填焦褐底）。"""
    c = im.crop(box).convert("RGBA")
    c.thumbnail((CW, CH), Image.LANCZOS)
    base = Image.new("RGBA", (CW, CH), (0, 0, 0, 0))
    base.paste(c, ((CW - c.width) // 2, (CH - c.height) // 2))
    return base


def lowfreq_noise(w, h, rnd):
    """低频灰度噪声：模拟水墨罩染的斑驳，而不是均匀滤镜。"""
    nw, nh = 14, 11
    small = Image.new("L", (nw, nh))
    small.putdata([rnd.randint(70, 235) for _ in range(nw * nh)])
    return small.resize((w, h), Image.BICUBIC).filter(ImageFilter.GaussianBlur(5))


def feather_alpha(w, h, rnd, feather=10, nicks=True):
    """不规则软边蒙版：中心实、四周渐隐，避免硬边方块贴纸。

    v1 教训：边缘挖大块「烧穿洞」会在画面里读成贴了黑胶布，改成沿边
    磨几处小三角缺口即可；烧损感主要靠颜色与边缘炭化，不靠挖洞。
    """
    m = Image.new("L", (w, h), 0)
    ImageDraw.Draw(m).rounded_rectangle((3, 3, w - 4, h - 4), radius=44, fill=255)
    m = m.filter(ImageFilter.GaussianBlur(feather))
    d = ImageDraw.Draw(m)
    # 家具前脸（大面积）不咬缺口：真帧里缺口会读成柜门上的黑洞。
    for _ in range(rnd.randint(4, 7) if nicks else 0):
        edge = rnd.randint(0, 3)
        if edge == 0:
            cx, cy = rnd.randint(14, w - 14), rnd.randint(0, 6)
        elif edge == 1:
            cx, cy = rnd.randint(14, w - 14), rnd.randint(h - 6, h)
        elif edge == 2:
            cx, cy = rnd.randint(0, 6), rnd.randint(12, h - 12)
        else:
            cx, cy = rnd.randint(w - 6, w), rnd.randint(12, h - 12)
        rr = rnd.randint(5, 11)
        d.polygon([(cx - rr, cy), (cx + rr, cy),
                   (cx, cy + rr if edge in (0, 2) else cy - rr)], fill=0)
    return m.filter(ImageFilter.GaussianBlur(1.0))


def suppress_brick(rgb):
    """红砖抑制：把 r 明显高于 g 的鲜橙砖墙拉回焦褐灰。

    v1 教训：只做整体降饱和压暗，砖缝仍会读成「亮橙色方块」贴在焦黑废墟上。
    """
    px = rgb.load()
    for y in range(CH):
        for x in range(CW):
            r, g, b = px[x, y]
            if r - g > 22:
                px[x, y] = (min(255, int(g * 1.18 + r * 0.10)), g, int(b * 0.92))
    return rgb


def char_burn(src, seed, heavy=False, soot=0.16, ember=True, front=False, sooty=False):
    """把一格完好物件处理成「烧过、蒙灰、边缘炭化」的贴图片，保留原笔触。

    v1 教训：亮度压到 0.52 会让整片道具在焦黑背景上彻底消失 —— 用户要的是
    「看得见的烧坏家具」，不是黑剪影。现在中心保留 0.72~0.84 亮度，
    炭化只作用在四周（径向），中心笔触/木纹/砖缝仍可辨。
    """
    rnd = random.Random(seed)
    im = src.convert("RGBA")
    if im.size != (CW, CH):
        im = im.resize((CW, CH), Image.LANCZOS)
    # 从背景裁的格子留白是透明的；直接 convert("RGB") 会把透明区变纯黑条。
    # 先合成到焦褐底上，再靠羽化 alpha 让四周渐隐入背景。
    base = Image.new("RGBA", (CW, CH), (62, 48, 38, 255))
    base.alpha_composite(im)
    rgb = base.convert("RGB")

    # 0) 红砖抑制
    rgb = suppress_brick(rgb)
    # 家具前脸是大面积实体：真帧里 heavy 焦化会把它压成一块黑板，
    # 必须保留木纹 / 砖缝的可读性，只让四周轻微炭化。
    # 1) 降饱和：鲜亮木色 / 砖色退成焦褐灰
    rgb = ImageEnhance.Color(rgb).enhance(
        0.42 if sooty else (0.55 if front else (0.34 if heavy else 0.50)))
    # 2) 压暗（保留可辨识度）
    rgb = ImageEnhance.Brightness(rgb).enhance(
        0.76 if sooty else (0.87 if front else (0.72 if heavy else 0.84)))
    # 3) 焦褐罩染（往 burnt_warehouse 的墙色靠）
    rgb = Image.blend(rgb, Image.new("RGB", (CW, CH), (58, 45, 36)),
                      0.17 if sooty else (0.09 if front else soot))
    # 4) 斑驳烟熏：低频噪声做乘性暗度调制，保留笔触与渐层
    n = lowfreq_noise(CW, CH, rnd)
    amt = 0.26 if sooty else (0.14 if front else (0.30 if heavy else 0.20))
    dark = n.point(lambda v: int(255 - (255 - v) * amt))
    rgb = ImageChops.multiply(rgb, Image.merge("RGB", (dark, dark, dark)))
    # 5) 边缘炭黑（径向：中心亮、四周焦），不再整片压黑
    edge = Image.new("L", (CW, CH), 0)
    ed = ImageDraw.Draw(edge)
    for k in range(11):
        v = int(255 * (1.0 - k / 11.0) ** 1.6)
        ed.rounded_rectangle((k * 3, k * 3, CW - k * 3 - 1, CH - k * 3 - 1),
                             radius=36, outline=v, width=3)
    edge = edge.filter(ImageFilter.GaussianBlur(4))
    rgb = Image.composite(Image.new("RGB", (CW, CH), (30, 23, 19)), rgb,
                          edge.point(lambda v: int(v * (0.26 if sooty else (0.16 if front else 0.34)))))
    # 6) 零星余烬（与 burnt_warehouse 里的火星同源，点到为止）
    if ember:
        d = ImageDraw.Draw(rgb)
        for _ in range(rnd.randint(2, 4)):
            x, y = rnd.randint(18, CW - 18), rnd.randint(18, CH - 18)
            r = 1
            d.ellipse((x - r, y - r, x + r, y + r), fill=(158, 92, 46))

    out = rgb.convert("RGBA")
    out.putalpha(feather_alpha(CW, CH, rnd, 10, nicks=not front))
    return out


# ---- 16 格内容：前 12 格 = 物件，后 4 格 = 家具前脸 --------------------------
# 每格：(来源图, 焦化强度, 说明)
sources = [
    (cell(0), True,  "chest   焦黑塌架木柜/木箱"),
    (cell(1), True,  "ammo    烧变形的弹药木箱"),
    (crop_any(burnt, (830, 415, 1015, 480)), True, "grenade 熏黑断木柄 / 残件"),
    (cell(3), False, "letter  烧焦边的残信"),
    (cell(5), False, "will    没烧尽的遗书"),
    (cell(4), False, "clue    焦角便条/文件"),
    (crop_any(burnt, (800, 555, 940, 645)), False, "gauze   蒙灰的布卷/灰布（不再用完好竹箩堆）"),
    (cell(11), True, "medkit  熏黑的医药木箱"),
    (crop_any(burnt, (760, 520, 1120, 700)), False, "ash     灰烬堆 + 焦木炭"),
    (cell(9), True,  "power   烧坏的电闸/仪表"),
    (cell(10), True, "radio   砸歪熏黑的发报机"),
    (crop_any(burnt, (680, 610, 1180, 745)), False, "备用    碎砖瓦砾"),
    (cell(0), True,  "front   焦黑橱柜正面"),
    (crop_any(com, (790, 490, 940, 655)), True, "front   塌了半边的砖灶（不带竹箩堆）"),
    (crop_any(burnt, (390, 465, 800, 610)), True, "front   断腿桌板"),
    (cell(3), True,  "front   烧卷的挂画"),
]

board = Image.new("RGBA", (CW * COLS, CH * 4), (0, 0, 0, 0))
for i, (src, heavy, _label) in enumerate(sources):
    t = char_burn(src, seed=8200 + i * 137, heavy=heavy,
                  soot=0.20 if heavy else 0.13, front=(i >= 12), sooty=(i == 13))
    board.alpha_composite(t, ((i % 4) * CW, (i // 4) * CH))

out_asset = os.path.join(ART, "props_atlas_burnt.png")
board.save(out_asset)
shutil.copyfile(out_asset, os.path.join(PREV, "props_atlas_burnt_CANDIDATE.png"))
print("asset :", out_asset, board.size)
for i, (_s, _h, label) in enumerate(sources):
    print("  tile %2d  %s" % (i, label))


# ---- 房间模拟对照：左 = v3.81 现状，右 = v3.82 烧毁版 ------------------------
# 只用于给用户过目，绝不进 docs/screenshots（画廊只放真帧）。
ROOM_W, ROOM_H = 952, 612
bg = burnt.resize((ROOM_W, ROOM_H), Image.LANCZOS).convert("RGBA")

def paste_prop(canvas, src_atlas_img, tile, cx, cy, w, h, alpha=1.0):
    c = src_atlas_img.crop(((tile % 4) * CW, (tile // 4) * CH,
                            (tile % 4) * CW + CW, (tile // 4) * CH + CH))
    c = c.resize((int(w), int(h)), Image.LANCZOS)
    if alpha < 1.0:
        a = c.getchannel("A").point(lambda v: int(v * alpha))
        c.putalpha(a)
    canvas.alpha_composite(c, (int(cx - w / 2), int(cy - h / 2)))

# BurntWarehouse 的真实物件坐标（与 InteriorExplore.BuildContents 一致）
# 前景顺序：家具前脸 -> 地落物 -> Surface
fronts_old = [(470, 472, 0), (620, 480, 0), (322, 486, 8)]   # RecessRect 小贴纸
drops_old = [(410, 506, 1), (720, 506, 2)]
surface_old = [(760, 492, 0), (540, 492, 6), (470, 498, 8)]

def compose(modern):
    c = bg.copy()
    if not modern:
        # v3.81：所有物件一律 68x56 硬边方块，完好鲜亮
        for x, y, t in fronts_old:
            paste_prop(c, atlas, t, x, y, 52, 26)
        for x, y, t in drops_old:
            paste_prop(c, atlas, t, x, y, 68, 56)
        for x, y, t in surface_old:
            paste_prop(c, atlas, t, x, y, 68, 56)
    else:
        # v3.82：家具前脸按真实体量 + 物件按类型分级，全部焦化软边
        paste_prop(c, board, 14, 470, 472, 150, 34)   # 断桌板
        paste_prop(c, board, 12, 620, 480, 116, 152)  # 焦黑橱柜
        paste_prop(c, board, 13, 322, 486, 120, 92)   # 塌砖灶
        paste_prop(c, board, 1, 410, 506, 74, 54)     # ammo  M
        paste_prop(c, board, 2, 720, 506, 60, 44)     # grenade S
        paste_prop(c, board, 0, 760, 492, 108, 80)    # chest  L
        paste_prop(c, board, 6, 540, 492, 62, 46)     # gauze  M
        paste_prop(c, board, 8, 470, 498, 132, 58)    # ash    L(摊开)
    return c.convert("RGB")

left, right = compose(False), compose(True)
gap, pad = 26, 18
W = pad * 2 + ROOM_W * 2 + gap
H = ROOM_H + pad * 2 + 54
cmp = Image.new("RGB", (W, H), (24, 20, 17))
cmp.paste(left, (pad, pad + 40))
cmp.paste(right, (pad + ROOM_W + gap, pad + 40))
d = ImageDraw.Draw(cmp)
d.text((pad, 14), "BEFORE  v3.81  intact props / all 68x56 / hard edges", fill=(235, 214, 180))
d.text((pad + ROOM_W + gap, 14), "AFTER  v3.82  burnt props / per-kind sizes / feathered", fill=(235, 214, 180))
cmp_path = os.path.join(PREV, "props_burnt_before_after.png")
cmp.save(cmp_path)
print("compare:", cmp_path, cmp.size)
