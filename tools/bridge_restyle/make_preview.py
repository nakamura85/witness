#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Offline style-preview generator for the Jiangqiao (江桥) rework.

Why this exists: the build machine has no GPU, so Godot headless cannot
read back the SubViewport texture -> we cannot produce an in-engine
screenshot here. This script composites a FAITHFUL STYLE PREVIEW using
the REAL new bridge.png and the REAL new railway palette (from
JiangqiaoLevel.cs DrawRailway), plus a scale marker for the soldier
height, so the user can judge style consistency on GitHub.

This is NOT an engine render. Proportions are approximate; palette and
ink lines are taken from the actual assets/code.
"""
import math
import os
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
BRIDGE = os.path.join(ROOT, "assets/art/props/bridge.png")
OUT_FULL = os.path.join(ROOT, "docs/screenshots/v55_jq_bridge_rework.png")
OUT_TRACK = os.path.join(ROOT, "docs/screenshots/v55_jq_track_rework.png")

# ---- palette (0..255) taken from JiangqiaoLevel.cs DrawRailway / InkStyle ----
INK = (36, 26, 20)            # InkStyle.InkColor (0.14,0.10,0.08)
SLEEPER = (107, 107, 110)      # SleeperWood base (0.42,0.42,0.43) neutral gray
SLEEPER_SH = (56, 56, 59)      # sleeper shadow (0.22,0.22,0.23) neutral dark
BALLAST = (184, 186, 191)     # ballast fill (0.72,0.73,0.75) cool neutral
BALLAST_EDGE = (221, 224, 228)
PLATE = (102, 105, 107)       # steel deck plate (0.40,0.41,0.42) neutral
PLATE_FL = (138, 140, 143)    # plate top flange (0.54,0.55,0.56) cool
SHEEN = (224, 230, 235)       # rail steel sheen (0.88,0.90,0.92) cool
GRAVEL = (153, 156, 161)
SNOW = (245, 247, 247)

SKY_TOP = (199, 209, 219)
SKY_HOR = (224, 230, 235)
MTN = (150, 160, 175)
SNOWGRD = (235, 237, 240)

S = 0.96                     # world->screen scale for the preview
GROUND = 540                 # screen y of GroundY (rail line / bridge deck base)


def vgrad(h, top, hor):
    arr = np.zeros((h, 1, 3), np.uint8)
    for i in range(h):
        t = i / max(1, h - 1)
        arr[i, 0] = [int(top[j] + (hor[j] - top[j]) * t) for j in range(3)]
    return arr.repeat(1280, axis=1)


def mountains(draw, w, base):
    rng = np.random.default_rng(7)
    pts = [(0, base)]
    x = 0
    while x < w:
        x += int(rng.uniform(60, 130))
        y = base - int(rng.uniform(20, 90))
        pts.append((min(x, w), y))
    pts += [(w, base), (0, base)]
    draw.polygon(pts, fill=MTN)


def snow_ground(img, w, h, base):
    arr = np.array(img).astype(np.int16)
    for y in range(base, h):
        t = (y - base) / max(1, h - base)
        col = [int(SNOWGRD[j] + (215 - SNOWGRD[j]) * t) for j in range(3)]
        arr[y, :] = col
    # subtle speckle
    rng = np.random.default_rng(3)
    n = rng.integers(-6, 6, (h - base, w, 3))
    arr[base:, :] = np.clip(arr[base:, :] + n, 0, 255)
    return Image.fromarray(arr.astype(np.uint8))


def draw_bridge(canvas, bx):
    img = Image.open(BRIDGE).convert("RGBA")
    W, H = img.size                       # 1530 x 812
    deck_row = 355
    bw = int(791.5 * S)                  # bridge world width * S
    trussH = int(293.5 * S)              # top region displayed height
    pierH = int(236.3 * S)              # bottom region displayed height
    top = img.crop((0, 0, W, deck_row)).resize((bw, trussH), Image.LANCZOS)
    bot = img.crop((0, deck_row, W, H)).resize((bw, pierH), Image.LANCZOS)
    truss_top = GROUND - trussH
    canvas.paste(top, (int(bx), int(truss_top)), top)
    canvas.paste(bot, (int(bx), int(GROUND)), bot)
    return bx, bw


def track_field(draw, x0, x1, crater_x=None):
    # ballast bed (wavy top)
    ballast = [(x0, GROUND + 2)]
    xx = x0
    while xx <= x1:
        yy = GROUND + 2 + math.sin(xx * 0.021) * 1.6 - math.sin(xx * 0.053) * 0.8
        ballast.append((xx, yy))
        xx += 12
    ballast += [(x1, GROUND + 22), (x0, GROUND + 22)]
    draw.polygon(ballast, fill=BALLAST)
    # sleepers (vertical bars) + rails
    k = 0
    x = x0
    while x < x1:
        if crater_x and abs(x - crater_x) < 26:
            x += 25
            k += 1
            continue
        jy = math.sin(k * 12.9898) * 1.0
        sx = int(x)
        # shadow
        draw.rectangle([sx + 1, GROUND - 3 + jy + 1, sx + 1 + 8, GROUND - 3 + jy + 1 + 16],
                       fill=SLEEPER_SH)
        # body
        draw.rectangle([sx, GROUND - 3 + jy, sx + 8, GROUND - 3 + jy + 16],
                       outline=INK, width=1, fill=SLEEPER)
        # snow streak
        if math.sin(k * 7.31) > -0.2:
            draw.line([(sx + 1, GROUND - 3 + jy + 7), (sx + 7, GROUND - 3 + jy + 7)],
                      fill=SNOW, width=2)
        x += 25
        k += 1
    # rails (broken at crater)
    for ry in (GROUND - 1, GROUND + 11):
        seg = x0
        xx = x0
        while xx <= x1:
            broken = crater_x and abs(xx - crater_x) < 9
            if broken:
                if xx - seg > 4:
                    draw.line([(seg, ry), (xx, ry)], fill=INK, width=3)
                    draw.line([(seg, ry), (xx, ry)], fill=SHEEN, width=1)
                seg = xx + 10
            xx += 10
        if x1 - seg > 4:
            draw.line([(seg, ry), (x1, ry)], fill=INK, width=3)
            draw.line([(seg, ry), (x1, ry)], fill=SHEEN, width=1)


def steel_plate(draw, x0, x1):
    # plate body
    draw.polygon([(x0, GROUND - 2), (x1, GROUND - 2),
                  (x1, GROUND - 2 + 17), (x0, GROUND - 2 + 17)], fill=PLATE)
    # top flange
    draw.polygon([(x0, GROUND - 2), (x1, GROUND - 2),
                  (x1, GROUND + 1), (x0, GROUND + 1)], fill=PLATE_FL)
    # closed ink outline
    draw.line([(x0, GROUND - 2), (x1, GROUND - 2), (x1, GROUND + 15),
               (x0, GROUND + 15), (x0, GROUND - 2)], fill=INK, width=2)
    # rails across the plate
    for ry in (GROUND - 1, GROUND + 11):
        draw.line([(x0, ry), (x1, ry)], fill=INK, width=3)
        draw.line([(x0, ry), (x1, ry)], fill=SHEEN, width=1)


def soldier_marker(draw, x, base_y, h):
    """Stylised standing soldier silhouette for scale (player/ija ~114px)."""
    head_r = h * 0.11
    cx = x + h * 0.18
    top = base_y - h
    # body
    draw.polygon([(cx - h * 0.10, top + h * 0.30),
                  (cx + h * 0.10, top + h * 0.30),
                  (cx + h * 0.16, base_y - h * 0.28),
                  (cx - h * 0.16, base_y - h * 0.28)], fill=(90, 84, 76))
    # legs
    draw.rectangle([cx - h * 0.14, base_y - h * 0.30, cx - h * 0.02, base_y], fill=(70, 64, 58))
    draw.rectangle([cx + h * 0.02, base_y - h * 0.30, cx + h * 0.14, base_y], fill=(70, 64, 58))
    # head
    draw.ellipse([cx - head_r, top, cx + head_r, top + head_r * 2], fill=(120, 110, 98))
    # ink outline
    draw.ellipse([cx - head_r, top, cx + head_r, top + head_r * 2], outline=INK, width=1)
    draw.line([(cx - h * 0.16, base_y - h * 0.28), (cx + h * 0.16, base_y - h * 0.28)],
              fill=INK, width=1)


def banner(draw, w, text):
    draw.rectangle([0, 0, w, 26], fill=(20, 18, 16))
    try:
        from PIL import ImageFont
        f = ImageFont.load_default()
        draw.text((10, 6), text, fill=(235, 235, 230), font=f)
    except Exception:
        pass


def build_full():
    W, H = 1280, 720
    canvas = Image.new("RGB", (W, H))
    arr = vgrad(H, SKY_TOP, SKY_HOR)
    canvas.paste(Image.fromarray(arr))
    d = ImageDraw.Draw(canvas)
    mountains(d, W, 470)
    canvas = snow_ground(canvas, W, H, GROUND + 18)
    d = ImageDraw.Draw(canvas)
    # track on the field (left of bridge)
    bx, bw = draw_bridge(canvas, 430)
    d = ImageDraw.Draw(canvas)
    track_field(d, 70, bx - 4, crater_x=300)
    steel_plate(d, bx - 4, bx + 400)
    # low snow drift integrated at the bridge base (subtle, not a floating blob)
    d.ellipse([bx + 10, GROUND + 14, bx + 150, GROUND + 40], fill=(236, 238, 240))
    # scale marker soldier (114.5 * S tall) at far left
    soldier_marker(d, 40, GROUND + 20, int(114.5 * S))
    # caption
    banner(d, W, "OFFLINE STYLE PREVIEW - 非引擎渲染，仅供风格核对 (no GPU build env)")
    d.text((44, GROUND + 22), "玩家/日军 站姿 ≈114px ≈亚洲男 160-180cm", fill=(40, 36, 32))
    canvas.save(OUT_FULL)
    print("wrote", OUT_FULL, canvas.size)


def build_track():
    global GROUND
    W, H = 960, 420
    GROUND = 300
    canvas = Image.new("RGB", (W, H))
    arr = vgrad(H, SKY_TOP, SKY_HOR)
    canvas.paste(Image.fromarray(arr))
    d = ImageDraw.Draw(canvas)
    mountains(d, W, 250)
    # ground
    a = np.array(canvas)
    a[GROUND + 18:, :] = np.clip(a[GROUND + 18:, :].astype(np.int16) + 6, 0, 255).astype(np.uint8)
    canvas = Image.fromarray(a)
    d = ImageDraw.Draw(canvas)
    track_field(d, 30, 560, crater_x=300)
    steel_plate(d, 560, 920)
    # crater pit at the rail break (x=300): dark concentric pit, rails already stop here
    d.ellipse([272, GROUND - 6, 330, GROUND + 30], fill=(120, 116, 110))
    d.ellipse([278, GROUND - 1, 324, GROUND + 25], fill=(74, 70, 64))
    d.ellipse([284, GROUND + 4, 318, GROUND + 21], fill=(42, 40, 36))
    banner(d, W, "TRACK CLOSE-UP - 枕木暖灰竖条 / 钢桥板 / 断轨 (新配色)")
    canvas.save(OUT_TRACK)
    print("wrote", OUT_TRACK, canvas.size)


if __name__ == "__main__":
    build_full()
    build_track()
