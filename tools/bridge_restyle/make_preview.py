#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
make_preview.py —— 生成江桥「桥 + 铁轨」离线风格预览图（无 GPU 时替代游戏内截图）。

原则（吸取教训，绝不再犯）：
  * 不画假的低多边形山、不画黑块假小人、不放混乱中英标注。
  * 背景 = 游戏同款冷灰雪原渐变 + 平涂雪山剪影（深棕描边），只作衬托。
  * 桥     = 真实 assets/art/props/bridge.png（v3.26 原版漫画桥，本次已回退还原）。
  * 铁轨   = 严格照 DrawRailway 的配色与规格用 PIL 近似绘制：
             野外段 = 道砟床 + 8x16 竖条枕木 + 双轨；
             桥面段 = 钢桥板 + 双轨，无枕木、无道砟。
  * 士兵   = 真实士兵贴图（soldier_ija.jpg），按游戏归一化身高缩放，仅作标尺。

输出（均写入 docs/screenshots/，覆盖用户一直在看的文件）：
  v55_jq_bridge_rework.png  : 全场景（雪原 + 桥 + 铁轨 + 士兵标尺）
  v55_jq_track_rework.png   : 铁轨特写（枕木/钢轨/钢桥板 + 士兵标尺）
"""
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

ROOT = "../../"
BRIDGE = ROOT + "assets/art/props/bridge.png"
SOLDIER = ROOT + "assets/art/sprites/soldier_ija.jpg"

# ---- 游戏同款调色（取自真实资产客观数据）----
SNOW_TOP = (214, 222, 230)      # 天空/远景雪，冷灰偏亮
SNOW_BOT = (232, 236, 240)      # 近景雪，更亮
MTN = (150, 160, 172)           # 雪山剪影（冷灰）
MTN_EDGE = (40, 30, 24)         # 深棕描边
INK = (36, 26, 20)              # InkStyle.InkColor 深暖棕

# ---- 铁轨（照 v3.26 DrawRailway 原版配色，已随 bridge.png 一并回退）----
BALLAST = (186, 193, 195)       # 道砟床 (0.73,0.755,0.765)
SLEEPER = (108, 84, 60)         # 枕木 褐木 (shade, shade*0.80, shade*0.60)
STEEL = (79, 87, 94)            # 钢桥板 (0.31,0.34,0.37)
RAIL = INK                      # 钢轨 = 深墨色


def vgrad(h, w, top, bot):
    t = np.linspace(0, 1, h)[:, None]        # (h,1)
    top = np.array(top, float); bot = np.array(bot, float)
    g = (top * (1 - t) + bot * t)            # (h,3)
    g = g[:, None, :].repeat(w, axis=1)      # (h,w,3)
    return g.astype(np.uint8)


def mountains(d, w, base_y, peak):
    # 两道平涂雪山剪影 + 深棕描边
    pts = [(0, base_y)]
    x = 0
    while x < w:
        pw = 120 + (x // 120) % 3 * 40
        pts.append((x + pw * 0.5, base_y - peak))
        pts.append((x + pw, base_y))
        x += pw
    pts.append((w, base_y))
    d.polygon(pts, fill=MTN)
    d.line(pts, fill=MTN_EDGE, width=2)


def draw_soldier(canvas, x, foot_y, h=150):
    """把真实士兵贴图按身高 h 贴在 (x, foot_y)。JPG 无 alpha，用白键抠掉雪白背景。"""
    try:
        s = Image.open(SOLDIER).convert("RGBA")
    except Exception:
        return
    sw, sh = s.size
    nh = int(h)
    nw = int(sw * nh / sh)
    s = s.resize((nw, nh), Image.LANCZOS)
    # 白键：近白像素（雪背景）透明，给 2px 软边避免硬边
    arr = np.asarray(s).astype(np.int16)
    lum = arr[..., :3].sum(2)
    mask = lum > 235 * 3          # 近白
    alpha = np.where(mask, 0, 255).astype(np.uint8)
    alpha = np.asarray(Image.fromarray(alpha).filter(ImageFilter.MaxFilter(3)))  # 缩 1px 去硬边
    arr[..., 3] = alpha
    s = Image.fromarray(arr.astype(np.uint8), "RGBA")
    canvas.alpha_composite(s, (int(x - nw / 2), int(foot_y - nh)))


def build_full():
    W, H = 1100, 560
    ground = 430
    arr = vgrad(H, W, SNOW_TOP, SNOW_BOT)
    canvas = Image.fromarray(arr, "RGB").convert("RGBA")
    d = ImageDraw.Draw(canvas)
    mountains(d, W, ground + 70, 150)

    # 近景雪地（比天空亮一点）
    d.rectangle([0, ground, W, H], fill=(236, 240, 244))

    # ---- 铁轨（左侧野外段 + 右侧桥面段）----
    rail_y1, rail_y2 = ground - 6, ground + 8
    field_end = 720          # 野外/桥分界（对应 BridgeDeckX）
    # 道砟床
    d.rectangle([60, rail_y1 + 4, field_end, rail_y1 + 26], fill=BALLAST)
    # 野外枕木：8x16 竖条
    x = 70
    while x < field_end - 10:
        d.rectangle([x, rail_y1 - 4, x + 8, rail_y1 + 14], fill=SLEEPER)
        d.rectangle([x, rail_y1 - 4, x + 8, rail_y1 - 2], outline=INK, width=1)
        x += 26
    # 钢桥板（桥面段）
    d.rectangle([field_end - 4, rail_y1 - 4, W - 40, rail_y1 + 16], fill=STEEL)
    d.rectangle([field_end - 4, rail_y1 - 4, W - 40, rail_y1 - 2], outline=INK, width=2)
    # 双轨
    d.line([(60, rail_y1), (W - 40, rail_y1)], fill=RAIL, width=3)
    d.line([(60, rail_y2), (W - 40, rail_y2)], fill=RAIL, width=3)

    # ---- 桥（右侧，真实贴图）----
    b = Image.open(BRIDGE).convert("RGBA")
    bw, bh = b.size
    scale = 300.0 / bh
    nbw, nbh = int(bw * scale), int(bh * scale)
    b = b.resize((nbw, nbh), Image.LANCZOS)
    bx = field_end - 30
    # 把桥的甲板行(源图 355/812)对齐到铁轨线(ground)，让轨看起来跑上桥面
    by = ground - int(nbh * 355.0 / 812.0)
    canvas.alpha_composite(b, (bx, by))

    # 士兵标尺（真实贴图，游戏身高 108px → 这里约 150px 显示）
    draw_soldier(canvas, 120, ground + 4, h=150)
    draw_soldier(canvas, 980, ground + 4, h=150)

    # 极简说明（避免中文乱码，只用英文/数字标签）
    d.text((14, 12), "Jiangqiao bridge + railway  v3.26 REVERTED (original comic bridge + original railway)", fill=(60, 50, 42))
    d.text((14, H - 22), "left soldier = 108px reference sprite (untouched)", fill=(60, 50, 42))
    return canvas


def build_track():
    W, H = 1000, 300
    ground = 210
    arr = vgrad(H, W, SNOW_TOP, SNOW_BOT)
    canvas = Image.fromarray(arr, "RGB").convert("RGBA")
    d = ImageDraw.Draw(canvas)
    d.rectangle([0, ground, W, H], fill=(236, 240, 244))

    rail_y1, rail_y2 = ground - 6, ground + 8
    field_end = 620
    # 道砟
    d.rectangle([40, rail_y1 + 4, field_end, rail_y1 + 28], fill=BALLAST)
    # 野外枕木 8x16 竖条
    x = 50
    while x < field_end - 10:
        d.rectangle([x, rail_y1 - 4, x + 8, rail_y1 + 16], fill=SLEEPER)
        d.rectangle([x, rail_y1 - 4, x + 8, rail_y1 - 2], outline=INK, width=1)
        x += 26
    # 钢桥板 + 无枕木
    d.rectangle([field_end - 4, rail_y1 - 4, W - 30, rail_y1 + 18], fill=STEEL)
    d.rectangle([field_end - 4, rail_y1 - 4, W - 30, rail_y1 - 2], outline=INK, width=2)
    # 双轨
    d.line([(40, rail_y1), (W - 30, rail_y1)], fill=RAIL, width=3)
    d.line([(40, rail_y2), (W - 30, rail_y2)], fill=RAIL, width=3)

    draw_soldier(canvas, 90, ground + 4, h=150)
    d.text((14, 12), "Railway: field sleepers 8x16 vertical strips; on-bridge = steel deck, no sleepers", fill=(60, 50, 42))
    return canvas


if __name__ == "__main__":
    out_dir = ROOT + "docs/screenshots/"
    full = build_full()
    full.save(out_dir + "v55_jq_bridge_rework.png")
    track = build_track()
    track.save(out_dir + "v55_jq_track_rework.png")
    print("wrote", out_dir + "v55_jq_bridge_rework.png", full.size)
    print("wrote", out_dir + "v55_jq_track_rework.png", track.size)
