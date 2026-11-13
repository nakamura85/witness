# -*- coding: utf-8 -*-
"""
v3.83 纸张贴图生成器（离线，产出 PNG 贴图资产，运行时由 DrawTextureRectRegion 绘制）

用户 2026-11-13 反馈：
  「那些留言或者纸条的或者纸张的我都希望那个风格可以参考第二张这样的美术画风哦
    跟你理解的烧坏的纸张不同」

  · 被否定的旧理解（v3.68~v3.82, BurntPaperArt.cs）：
      规整矩形 + 3px 均匀粗墨框 + 三档平涂 + 若干「圆形烧穿孔」+ 暗红横线
      ⇒ 读起来像「打印表格 / 证书」，不是被火烤过的旧纸。
  · 用户参考图（第二张）的质感：
      米黄→浅褐的斑驳渐层、满纸揉皱交叉折痕、
      **四周一圈不规则锯齿状焦边**（纸色 → 浅褐 → 深褐 → 炭黑）、
      没有硬边框、没有规整圆孔、没有卷角三角。

★ 版权说明：参考图是带水印的图库素材，本脚本**不复制、不抠图、不重绘该图**，
  只用数学噪声 + 随机折痕自绘一张原创的「手绘旧纸」纹理，取其质感与色调。
  产物是 PNG 贴图；Godot 侧只做 DrawTextureRectRegion 九宫格采样，
  没有任何运行时程序化几何色块画场景实体。

九宫格约定（C# 侧常量必须一致）：
  · paper_burnt_v{1,2,3}.png : 384×384, 边距 E=80, 中心格 C=224（可无缝平铺）
  · paper_slip.png           : 256×256, 边距 E=32, 中心格 C=192

产物：
  assets/art/ui/paper_burnt_v1.png / v2.png / v3.png / paper_slip.png
  tools/bridge_restyle/previews/paper_texture_candidates.png  （拼版候选，仅供过目）

运行：python tools/bridge_restyle/make_burnt_paper_textures.py
"""
import math
import os
import random

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
ART = os.path.join(ROOT, "assets", "art", "ui")
PREV = os.path.join(ROOT, "tools", "bridge_restyle", "previews")
os.makedirs(ART, exist_ok=True)
os.makedirs(PREV, exist_ok=True)


# ---------------------------------------------------------------- 无缝纸纹
def periodic_noise(size, cells, seed):
    """可无缝平铺的低频噪声（0..1）。3×3 tile 后放大再取中心块，保证左右/上下接得上。"""
    r = random.Random(seed)
    k = max(2, int(cells))
    small = np.asarray([r.random() for _ in range(k * k)], dtype=np.float32).reshape(k, k)
    tiled = np.tile(small, (3, 3))
    im = Image.fromarray(np.clip(tiled * 255, 0, 255).astype(np.uint8), "L")
    im = im.resize((size * 3, size * 3), Image.BICUBIC)
    return np.asarray(im.crop((size, size, size * 2, size * 2)), dtype=np.float32) / 255.0


def paper_rgb(size, seed, stain_strength=1.0):
    """米黄斑驳渐层 + 揉皱交叉折痕的手绘纸纹（不透明 RGB，float32）。"""
    n1 = periodic_noise(size, 7, seed + 1)     # 大块水渍
    n2 = periodic_noise(size, 23, seed + 2)    # 中斑
    n3 = periodic_noise(size, 71, seed + 3)    # 细纤维颗粒

    yy, xx = np.mgrid[0:size, 0:size]
    d = np.minimum.reduce([xx, size - 1 - xx, yy, size - 1 - yy]).astype(np.float32)
    # 旧纸：中心略亮、四周泛黄发暗
    vign = 1.0 - 0.13 * (1.0 - np.clip(d / (size * 0.5), 0, 1)) ** 1.4

    # 加大中低频权重，纸面要有明显深浅不匀的块面，不是干净的平涂
    tone = (n1 - 0.5) * 42 + (n2 - 0.5) * 30 + (n3 - 0.5) * 10
    base = np.zeros((size, size, 3), np.float32)
    base[..., 0] = (236 + tone) * vign
    base[..., 1] = (219 + tone * 0.95) * vign
    base[..., 2] = (181 + tone * 0.85) * vign

    # 茶渍 / 水痕：低频噪声高值处压出成片褐晕，不画规则斑点
    stain = np.clip((n1 - 0.54) * 2.6, 0, 1) ** 1.5 * stain_strength
    base[..., 0] -= stain * 22
    base[..., 1] -= stain * 26
    base[..., 2] -= stain * 30

    im = Image.fromarray(np.clip(base, 0, 255).astype(np.uint8), "RGB").convert("RGBA")

    # 揉皱折痕：不是尺子画的直线，而是「2~5 段、方向逐段偏转」的弯曲短皱褶，
    # 每条画一侧亮 / 一侧暗的细平行线，9 宫偏移保证平铺无缝。
    layer = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    dl = ImageDraw.Draw(layer)
    rnd = random.Random(seed + 77)

    def crumple(x, y, ang, length, segs):
        pts = [(x, y)]
        a = ang
        for _ in range(segs):
            a += rnd.uniform(-0.55, 0.55)
            s = length / segs
            pts.append((pts[-1][0] + math.cos(a) * s,
                        pts[-1][1] + math.sin(a) * s))
        return pts

    # 长度做「横向拉伸预补偿」：九宫格中心格 224px 会拉到 ~860px（≈3.8×），
    # 源图折痕必须画得短，实机里才读作短皱褶而不是被拉长的木纹弧线。
    creases = []
    for _ in range(380):
        creases.append((crumple(rnd.uniform(0, size), rnd.uniform(0, size),
                                rnd.uniform(0, math.tau), rnd.uniform(6, 22),
                                rnd.randint(2, 4)), rnd.uniform(0.5, 1.0), False))
    for _ in range(3):     # 极少量长折痕带明暗块面，避免整张纸只有细线
        creases.append((crumple(rnd.uniform(0, size), rnd.uniform(0, size),
                                rnd.uniform(0, math.tau), size * 0.14,
                                rnd.randint(3, 5)), 1.0, True))

    for (pts, a, wide) in creases:
        for i in range(len(pts) - 1):
            x, y = pts[i]
            ex, ey = pts[i + 1]
            nx, ny = -(ey - y), (ex - x)
            n = math.hypot(nx, ny) or 1.0
            ux, uy = nx / n, ny / n
            for ox in (-size, 0, size):
                for oy in (-size, 0, size):
                    if wide:    # 宽明暗带：折痕两侧的块面起伏
                        dl.line((x + ox - ux * 3, y + oy - uy * 3,
                                 ex + ox - ux * 3, ey + oy - uy * 3),
                                fill=(112, 86, 56, int(17 * a)), width=7)
                        dl.line((x + ox + ux * 3, y + oy + uy * 3,
                                 ex + ox + ux * 3, ey + oy + uy * 3),
                                fill=(255, 249, 228, int(18 * a)), width=5)
                    dl.line((x + ox + ux * 0.8, y + oy + uy * 0.8,
                             ex + ox + ux * 0.8, ey + oy + uy * 0.8),
                            fill=(255, 250, 232, int(36 * a)), width=1)
                    dl.line((x + ox - ux * 0.8, y + oy - uy * 0.8,
                             ex + ox - ux * 0.8, ey + oy - uy * 0.8),
                            fill=(96, 72, 46, int(34 * a)), width=1)
    layer = layer.filter(ImageFilter.GaussianBlur(0.55))
    im = Image.alpha_composite(im, layer).convert("RGB")
    return np.asarray(im, dtype=np.float32)


# ---------------------------------------------------------------- 不规则焦边
def burn_depth(n, seed, amp):
    """一条边的「被火烧掉深度」剖面：多尺度噪声 + 几处深啃口，周期可首尾相接。"""
    r = random.Random(seed)

    def band(cells):
        k = max(2, int(cells))
        v = np.tile(np.asarray([r.random() for _ in range(k)], dtype=np.float32), 3)
        im = Image.fromarray(np.clip(v * 255, 0, 255).astype(np.uint8), "L")
        im = im.resize((n * 3, 1), Image.BICUBIC)
        return np.asarray(im.crop((n, 0, n * 2, 1)), dtype=np.float32).reshape(n) / 255.0

    # 高频权重加大 → 边缘更碎、更像火啃出来的锯齿，而不是平缓波浪
    v = band(4) * 0.34 + band(13) * 0.26 + band(41) * 0.25 + band(97) * 0.15
    spikes = np.zeros(n, np.float32)
    idx = np.arange(n)
    for _ in range(r.randint(4, 8)):
        c = r.randint(0, n - 1)
        w = r.randint(4, 15)
        off = (idx - c + n // 2) % n - n // 2
        spikes += np.exp(-(off / float(w)) ** 2) * r.uniform(0.4, 1.0)
    return 1.5 + v * amp + np.clip(spikes, 0, 1) * amp * 0.60


def make_sheet(size, seed, amp, soot=1.0, stain=1.0):
    """返回 RGBA：完整的一张纸（中心纸纹 + 四周焦边 + 锯齿透明外沿）。"""
    rgb = paper_rgb(size, seed, stain)
    yy, xx = np.mgrid[0:size, 0:size]
    yy = yy.astype(np.float32)
    xx = xx.astype(np.float32)

    pT = burn_depth(size, seed + 11, amp) * soot
    pB = burn_depth(size, seed + 12, amp) * soot
    pL = burn_depth(size, seed + 13, amp) * soot
    pR = burn_depth(size, seed + 14, amp) * soot
    # 四条边各自算「还剩多少纸」，再取最小 —— 连续距离场，
    # 四角自然融合，不会出现 argmin 那种 X 形硬对角接缝。
    t = np.min(np.stack([
        yy - pT[xx.astype(int)],
        (size - 1 - yy) - pB[xx.astype(int)],
        xx - pL[yy.astype(int)],
        (size - 1 - xx) - pR[yy.astype(int)],
    ]), axis=0)                      # <0 已被烧掉；>=0 仍在纸上

    # 焦色渐层收窄：炭黑 → 深褐 → 浅褐 → 纸色。
    # 参考图的焦边只占纸宽 4~6%，绝不能做成一圈发光晕影。
    # 九宫格会把源边格 80px 压到目标 ~40px（0.5×），所以源图里炭黑段必须
    # 留够 ~18px，否则实机里只剩 1~2px 细线，读不出「焦黑卷边」。
    keys_t = [0.0, 9.0, 18.0, 30.0, 48.0]
    keys_c = [(20, 12, 9), (38, 22, 13), (96, 55, 29),
              (176, 128, 76), (230, 211, 172)]
    char = np.stack([np.interp(t, keys_t, [c[ch] for c in keys_c])
                     for ch in range(3)], axis=-1).astype(np.float32)
    w = np.clip((t - 10.0) / 38.0, 0, 1)[..., None]    # 越往里越透出真实纸纹
    out = char * (1 - w) + rgb * w
    # 焦边内侧一圈极淡的烟熏，让焦色不突兀地断开
    smoke = (np.clip((58.0 - t) / 58.0, 0, 1) * 0.10)[..., None]
    out = out * (1 - smoke)

    # 焦边上的零碎炭点：参考图边缘不是一条平滑渐变带
    rnd = random.Random(seed + 555)
    fleck = np.zeros((size, size), np.float32)
    for _ in range(150):
        cx, cy = rnd.randint(0, size - 1), rnd.randint(0, size - 1)
        if 0 <= t[cy, cx] < 16:
            r = rnd.choice([1, 1, 2])
            fleck[max(0, cy - r):cy + r + 1, max(0, cx - r):cx + r + 1] = 1.0
    out = out * (1 - fleck[..., None] * 0.45)

    alpha = np.where(t < 0, 0, np.where(t < 1, 205, 255)).astype(np.uint8)
    rgba = np.dstack([np.clip(out, 0, 255).astype(np.uint8), alpha])
    return Image.fromarray(rgba, "RGBA")


def main():
    sheets = []
    specs = [
        ("paper_burnt_v1.png", 384, 9101, 13.0, 0.92, 0.85),
        ("paper_burnt_v2.png", 384, 9202, 14.5, 1.00, 1.00),
        ("paper_burnt_v3.png", 384, 9303, 12.0, 1.10, 1.15),
        ("paper_slip.png",     256, 9404,  7.0, 1.00, 0.90),
    ]
    for name, size, seed, amp, soot, stain in specs:
        im = make_sheet(size, seed, amp, soot, stain)
        im.save(os.path.join(ART, name))
        im.save(os.path.join(PREV, name))
        sheets.append((name, im))
        print("saved %-24s %s" % (name, im.size))

    # 拼版候选：把 3 张主纸按比例摆到纸上，方便一眼看焦边差异
    board = Image.new("RGB", (1180, 460), (38, 32, 28))
    x = 20
    for name, im in sheets[:3]:
        shown = im.resize((360, 360), Image.LANCZOS)
        board.paste(shown, (x, 50), shown)
        ImageDraw.Draw(board).text((x + 4, 20), name, fill=(232, 214, 178))
        x += 380
    slip = sheets[3][1].resize((360, 360), Image.LANCZOS)
    board.paste(slip, (20, 420), slip) if board.height > 780 else None
    board.save(os.path.join(PREV, "paper_texture_candidates.png"))

    # 单独给矮纸条一张横向拉伸的预览（实际运行时高只有 32~56px）
    sl = sheets[3][1].resize((720, 96), Image.LANCZOS)
    prev = Image.new("RGB", (760, 136), (38, 32, 28))
    prev.paste(sl, (20, 20), sl)
    prev.save(os.path.join(PREV, "paper_slip_preview.png"))
    print("candidate board ->", os.path.join(PREV, "paper_texture_candidates.png"))


if __name__ == "__main__":
    main()
