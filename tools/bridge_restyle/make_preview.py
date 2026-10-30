#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
make_preview.py —— 生成「风格对照」离线预览图（无 GPU 时替代游戏内截图）。

设计原则（吸取教训）：
  * 绝对不画假的「灰蓝 low-poly 山」和「黑块小人」误导用户。
  * 背景用游戏同款暖灰雪原 + 极简平涂山（深棕描边），只作衬托。
  * 桥 = 真实的 assets/art/props/bridge.png（已重描为暖灰漫画风）。
  * 铁轨 = 按 DrawRailway 当前配色（暖灰）用 PIL 近似绘制。
  * 士兵 = 按游戏归一化身高 108px（玩家 114.5px）画的漫画立绘标尺。
  * 底部角标放【真实的房子/士兵贴图】并标注「不改动参照物」，让用户一眼看到
    桥/铁轨现在与这些参照同一套墨线+平涂风格。

输出：
  docs/screenshots/v55_jq_bridge_rework.png  全场景
  docs/screenshots/v55_jq_track_rework.png   铁轨特写
"""
import math
import numpy as np
from PIL import Image, ImageDraw

ROOT = "../.."
BRIDGE = f"{ROOT}/assets/art/props/bridge.png"
HOUSE = f"{ROOT}/assets/art/props/house_intact_near.png"
SOLDIER = f"{ROOT}/assets/art/sprites/soldier_ija.jpg"

# 墨线与暖灰调色板（对齐 InkStyle.InkColor=(0.14,0.10,0.08) 与新桥/房子）
INK = (36, 26, 20)
SNOW = (233, 231, 225)          # 暖灰雪原
SNOW_HI = (245, 244, 240)
MTN = (168, 170, 172)           # 远山平涂（低饱和灰，作衬托）
MTN_INK = (60, 54, 48)
SLEEPER = (115, 107, 97)        # SleeperWood 基色 (0.45,0.42,0.38)
BALLAST = (191, 189, 184)
PLATE = (115, 117, 112)         # 钢桥板 (0.45,0.46,0.44)
PLATE_FL = (148, 148, 143)
RAIL = INK
SHEEN = (217, 219, 214)


def vgrad(h, w, top, bot):
    t = np.linspace(0, 1, h)[:, None, None]
    top = np.array(top, dtype=np.float64)[None, None, :]
    bot = np.array(bot, dtype=np.float64)[None, None, :]
    g = top * (1 - t) + bot * t            # (H, 1, 3)
    g = np.repeat(g, w, axis=1)            # (H, W, 3)
    return g.astype(np.uint8)


def mountains(d, W, base, top):
    """极简平涂远山 + 深棕描边（只作衬托，不抢戏）。"""
    pts = [(0, base)]
    xs = np.linspace(0, W, 7)
    for i, x in enumerate(xs):
        y = top + (i % 2) * 40
        pts.append((int(x), int(y)))
    pts += [(W, base), (W, base + 10), (0, base + 10)]
    d.polygon(pts, fill=MTN)
    d.line(pts[:8] + [pts[0]], fill=MTN_INK, width=2)


def soldier(d, x, foot_y, h=114):
    """按游戏归一化身高画的漫画立绘标尺（深棕描边 + 平涂），仅用于比例/风格参照。"""
    w = int(h * 0.42)
    left = x - w // 2
    d.rectangle([left + w*0.18, foot_y - h*0.45, left + w*0.42, foot_y], fill=(70, 64, 56), outline=INK, width=2)
    d.rectangle([left + w*0.55, foot_y - h*0.45, left + w*0.80, foot_y], fill=(70, 64, 56), outline=INK, width=2)
    d.rectangle([left + w*0.12, foot_y - h*0.78, left + w*0.88, foot_y - h*0.42], fill=(92, 84, 74), outline=INK, width=2)
    d.ellipse([left + w*0.30, foot_y - h*0.96, left + w*0.70, foot_y - h*0.66], fill=(150, 132, 110), outline=INK, width=2)
    d.rectangle([left + w*0.26, foot_y - h*0.99, left + w*0.74, foot_y - h*0.86], fill=(60, 54, 48), outline=INK, width=2)


def build_full():
    W, H = 980, 560
    GROUND = 430
    canvas = Image.fromarray(vgrad(H, W, SNOW_HI, SNOW))
    d = ImageDraw.Draw(canvas)
    mountains(d, W, GROUND + 30, 150)
    d.rectangle([0, GROUND, W, H], fill=SNOW)

    railY = GROUND - 60
    bx = 470
    d.rectangle([40, railY + 6, bx, railY + 30], fill=BALLAST)
    k = 0
    for x in range(60, bx, 26):
        jy = int(round(math.sin(k * 12.9898) * 1.0))
        d.rectangle([x, railY - 3 + jy, x + 8, railY + 13 + jy], fill=SLEEPER, outline=INK, width=1)
        k += 1
    d.line([(40, railY), (bx, railY)], fill=RAIL, width=3)
    d.line([(40, railY + 12), (bx, railY + 12)], fill=RAIL, width=3)
    d.line([(40, railY - 1), (bx, railY - 1)], fill=SHEEN, width=1)
    d.rectangle([bx, railY - 4, W - 20, railY + 14], fill=PLATE, outline=INK, width=2)
    d.rectangle([bx, railY - 4, W - 20, railY - 1], fill=PLATE_FL)
    d.line([(bx, railY), (W - 20, railY)], fill=RAIL, width=3)
    d.line([(bx, railY + 12), (W - 20, railY + 12)], fill=RAIL, width=3)

    br = Image.open(BRIDGE).convert("RGBA")
    bw = 540
    bh = int(br.height * bw / br.width)
    br = br.resize((bw, bh))
    canvas.paste(br, (W - bw - 20, GROUND - bh + 30), br)

    soldier(d, 150, GROUND, 114)
    d.text((120, GROUND + 6), "士兵 114px", fill=(80, 70, 60))

    ref = Image.new("RGB", (W, 70), (210, 208, 202))
    rd = ImageDraw.Draw(ref)
    rd.text((10, 6), "不改动参照物（士兵/背景，本图仅作风格对照）:", fill=INK)
    hh = Image.open(HOUSE).convert("RGBA")
    hw = 90
    hh = hh.resize((hw, int(hh.height * hw / hh.width)))
    ref.paste(hh, (10, 28), hh)
    rd.text((10 + hw + 12, 40), "房子（暖灰漫画风）", fill=(80, 70, 60))
    try:
        ss = Image.open(SOLDIER).convert("RGBA")
        sw = 40
        ss = ss.resize((sw, int(ss.height * sw / ss.width)))
        ref.paste(ss, (260, 28), ss)
        rd.text((260 + sw + 12, 40), "士兵原贴图", fill=(80, 70, 60))
    except Exception:
        pass
    rd.text((460, 40), "-> 新桥/铁轨已改为同款：深棕描边 + 暖灰平涂", fill=(120, 60, 40))

    full = Image.new("RGB", (W, H + 70))
    full.paste(canvas, (0, 0))
    full.paste(ref, (0, H))
    return full


def build_track():
    W, H = 980, 440
    G = 300
    canvas = Image.fromarray(vgrad(H, W, SNOW_HI, SNOW))
    d = ImageDraw.Draw(canvas)
    d.rectangle([0, G, W, H], fill=SNOW)
    railY = G - 40
    d.rectangle([20, railY + 6, 520, railY + 60], fill=BALLAST)
    k = 0
    for x in range(30, 520, 26):
        jy = int(round(math.sin(k * 12.9898) * 1.0))
        d.rectangle([x, railY - 3 + jy, x + 9, railY + 16 + jy], fill=SLEEPER, outline=INK, width=1)
        k += 1
    d.line([(20, railY), (360, railY)], fill=RAIL, width=3)
    d.line([(20, railY + 13), (360, railY + 13)], fill=RAIL, width=3)
    d.line([(420, railY), (900, railY)], fill=RAIL, width=3)
    d.line([(420, railY + 13), (900, railY + 13)], fill=RAIL, width=3)
    d.line([(20, railY - 1), (360, railY - 1)], fill=SHEEN, width=1)
    d.line([(420, railY - 1), (900, railY - 1)], fill=SHEEN, width=1)
    d.line([(360, railY), (400, railY - 10)], fill=RAIL, width=3)
    d.line([(360, railY + 13), (400, railY + 22)], fill=RAIL, width=3)
    d.rectangle([560, railY - 6, W - 20, railY + 18], fill=PLATE, outline=INK, width=2)
    d.rectangle([560, railY - 6, W - 20, railY - 2], fill=PLATE_FL)
    d.line([(560, railY), (W - 20, railY)], fill=RAIL, width=3)
    d.line([(560, railY + 13), (W - 20, railY + 13)], fill=RAIL, width=3)
    soldier(d, 120, G, 114)
    d.text((120 - 30, G + 6), "士兵 114px", fill=(80, 70, 60))
    d.ellipse([380, G - 6, 440, G + 40], fill=(150, 140, 128), outline=INK, width=2)
    return canvas


if __name__ == "__main__":
    out = "../../docs/screenshots/v55_jq_bridge_rework.png"
    build_full().save(out)
    out2 = "../../docs/screenshots/v55_jq_track_rework.png"
    build_track().save(out2)
    print("wrote", out, out2)
