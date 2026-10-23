# -*- coding: utf-8 -*-
"""
尸体立绘姿势改造：立正站姿直接旋转躺平会变成「双腿并拢笔直的圆木」，
不符合人体工学。本脚本对 *_corpse.jpg 做两处微旋转：
  1. 头部绕颈部旋转（脸歪向一边）
  2. 双腿整体绕髋部反向旋转（倒地时腿甩向一侧）
切割线用羽化蒙版过渡，白底填充（ChromaKey 抠图容差不受影响）。
固定随机参数，两张立绘扭转方向不同，结果可复现。
"""
from PIL import Image
import math, sys, os

ROOT = r"e:\witness\assets\art\sprites"

# (文件, 头部角度deg, 腿部角度deg) —— 头/腿反向制造倒地扭转感
JOBS = [
    ("soldier_ija_corpse.jpg", -11.0, 13.0),
    ("soldier_na_corpse.jpg",   9.0, -15.0),
]

def centroid_x(img, y0, y1):
    """区域内非白像素的水平质心（人物中线）"""
    px = img.load()
    sx, n = 0.0, 0
    for y in range(y0, y1, 2):
        for x in range(0, img.width, 2):
            r, g, b = px[x, y][:3]
            if r < 235 or g < 235 or b < 235:
                sx += x; n += 1
    return sx / n if n else img.width / 2

def feather_mask(size, top_fade, bottom_fade):
    """垂直羽化蒙版：顶部 top_fade px 从 0→255，底部 bottom_fade px 从 255→0"""
    w, h = size
    m = Image.new("L", size, 255)
    mp = m.load()
    for y in range(h):
        v = 255
        if y < top_fade:
            v = int(255 * y / top_fade)
        elif y > h - bottom_fade:
            v = int(255 * (h - y) / bottom_fade)
        for x in range(w):
            mp[x, y] = v
    return m

def rotate_region(img, y0, y1, cx_in_region, deg):
    """旋转 [y0,y1) 横带，旋转中心在切割线上的 (cx, y1-y0) 处，白底填充"""
    region = img.crop((0, y0, img.width, y1))
    rot = region.rotate(deg, resample=Image.BICUBIC,
                        center=(cx_in_region, region.height),  # 中心放在切割线上
                        fillcolor=(255, 255, 255))
    return region, rot

def process(path, head_deg, leg_deg):
    img = Image.open(path).convert("RGB")
    W, H = img.size
    cx = int(centroid_x(img, 0, H))

    # 切割线：颈部 ≈ 0.16H（头占顶部 16%），髋部 ≈ 0.58H（腿占底部 42%）
    cut_head = int(H * 0.16)
    cut_leg = int(H * 0.58)

    # —— 腿部：绕髋部旋转，区域顶部羽化与躯干衔接 ——
    # 旋转中心在切割线上：region 内坐标 (cx, H - cut_leg)
    leg_rot = img.crop((0, cut_leg, W, H)).rotate(
        leg_deg, resample=Image.BICUBIC, center=(cx, H - cut_leg),
        fillcolor=(255, 255, 255))
    leg_mask = feather_mask(leg_rot.size, 22, 0)   # 顶部 22px 羽化（贴躯干处），底部到底
    img.paste(leg_rot, (0, cut_leg), leg_mask)

    # —— 头部：绕颈部旋转，区域底部羽化与躯干衔接 ——
    head_rot = img.crop((0, 0, W, cut_head)).rotate(
        head_deg, resample=Image.BICUBIC, center=(cx, cut_head),
        fillcolor=(255, 255, 255))
    head_mask = feather_mask(head_rot.size, 0, 22)  # 底部 22px 羽化（颈部衔接）
    img.paste(head_rot, (0, 0), head_mask)

    img.save(path, quality=95)
    print(f"OK {os.path.basename(path)} {W}x{H} cx={cx} head={head_deg} leg={leg_deg}")

for fn, hd, ld in JOBS:
    process(os.path.join(ROOT, fn), hd, ld)
print("done")
