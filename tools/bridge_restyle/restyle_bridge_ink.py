#!/usr/bin/env python3
"""
restyle_bridge_ink.py —— 写实数字绘桥 → InkStyle 钢笔线稿+平涂水彩
对齐 witness 全局 InkStyle：InkColor=(0.14,0.10,0.08) 深暖棕描边，
平涂硬边、暖色调阶梯（对齐 house_intact_near 的暖灰/米色基调），
低饱和、轻微水彩颗粒、抗锯齿剪影。

硬约束（JiangqiaoLevel.DrawBridge 的几何假设）：
  * 输出保持 1530x812（桁架/桥面/桥墩行号对齐）。
  * 保留原 alpha 剪影形状，只改内部填充与墨线。
  * 剪影边缘平滑（抗锯齿），不出现撕裂/锯齿。

用法:
  python3 restyle_bridge_ink.py <in.png> <out.png> [levels] [vfloor] [ink_th] [sigma] [grain] [warm]
"""
import sys
import numpy as np
from PIL import Image, ImageFilter

# 严格对齐 scripts/InkStyle.cs 的 InkColor（深暖棕）
INK_COL = np.array([0.14, 0.10, 0.08])

# 暖色阶梯：直接映射到 house_intact_near 的同款「低饱和暖灰」基调
# （每档 sat≈0.10、暖 R≥G≥B、value 拉开明暗，不用原始色相避免过饱和）。
# 这样桥的色阶分布与房子落在同一区间，风格才统一。
WARM_RAMP = [
    (0.28, 0.266, 0.252),   # 暗部（暖灰，区别于近黑墨线）
    (0.42, 0.399, 0.378),   # 暗中调
    (0.56, 0.532, 0.504),   # 中调
    (0.70, 0.665, 0.630),   # 亮中调
    (0.84, 0.798, 0.756),   # 高光暖灰
]


def posterize_channel(x, levels):
    return np.round(x * (levels - 1)) / (levels - 1)


def main():
    src = sys.argv[1]
    out = sys.argv[2]
    levels = int(sys.argv[3]) if len(sys.argv) > 3 else 5
    vfloor = float(sys.argv[4]) if len(sys.argv) > 4 else 0.30   # 暗部抬升（防纯黑，留白）
    ink_th = float(sys.argv[5]) if len(sys.argv) > 5 else 0.07    # 平滑亮度 Sobel 墨线阈值
    sigma = float(sys.argv[6]) if len(sys.argv) > 6 else 2.0      # 平滑半径
    grain = float(sys.argv[7]) if len(sys.argv) > 7 else 0.03     # 水彩颗粒
    warm = float(sys.argv[8]) if len(sys.argv) > 8 else 1.0       # 暖度：1=纯暖灰阶梯(对齐房子),不加原始色相

    im = Image.open(src).convert("RGBA")
    arr = np.asarray(im).astype(np.float64) / 255.0
    h, w = arr.shape[0], arr.shape[1]
    rgb = arr[..., :3]
    alpha = arr[..., 3]

    # ---- 1. 平滑剪影掩码（抗锯齿）----
    a8 = (alpha * 255).astype(np.uint8)
    ablur = np.asarray(Image.fromarray(a8).filter(ImageFilter.GaussianBlur(sigma))).astype(np.float64) / 255.0
    amask = ablur > 0.5
    amask_f = amask.astype(np.float64)

    # ---- 2. 暖色阶梯平涂（按源亮度映射到暖色 ramp，对齐房子暖灰基调）----
    lum = 0.299 * rgb[..., 0] + 0.587 * rgb[..., 1] + 0.114 * rgb[..., 2]
    lq = posterize_channel(lum, levels)
    lq = vfloor + (1.0 - vfloor) * lq                  # 抬升暗部
    ramp = np.array(WARM_RAMP, dtype=np.float64)
    fi = np.clip((lq * (len(ramp) - 1)).round().astype(int), 0, len(ramp) - 1)
    flat = ramp[fi]                                    # (h,w,3) 暖色平涂
    # 暖度混合：warm=1 用纯阶梯；warm<1 保留部分源色相/明度
    flat = flat * warm + rgb * (1.0 - warm)
    flat = np.clip(flat, 0, 1)
    flat = np.asarray(Image.fromarray((flat * 255).astype(np.uint8)).filter(ImageFilter.MedianFilter(3))).astype(np.float64) / 255.0
    if grain > 0:
        rng = np.random.default_rng(20260430)
        n = (rng.random((h, w, 1)).astype(np.float64) - 0.5) * 2.0 * grain
        flat = np.clip(flat + n, 0, 1)

    # ---- 3. 墨线：平滑亮度 Sobel + 开运算 + 高斯抗锯齿 + 轻膨胀 ----
    limg = Image.fromarray((lum * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(sigma))
    lum2 = np.asarray(limg).astype(np.float64) / 255.0
    lx = np.zeros((h, w)); ly = np.zeros((h, w))
    lx[1:-1, 1:-1] = (lum2[1:-1, 2:] - lum2[1:-1, :-2]) * 0.5
    ly[1:-1, 1:-1] = (lum2[2:, 1:-1] - lum2[:-2, 1:-1]) * 0.5
    grad = np.sqrt(lx ** 2 + ly ** 2)
    ink = (grad > ink_th).astype(np.float64)
    inkimg = Image.fromarray((ink * 255).astype(np.uint8))
    inkimg = inkimg.filter(ImageFilter.MinFilter(3))
    inkimg = inkimg.filter(ImageFilter.MaxFilter(3))
    inkimg = inkimg.filter(ImageFilter.GaussianBlur(1.2))
    ink = (np.asarray(inkimg).astype(np.float64) / 255.0 > 0.5).astype(np.float64)
    ink = (np.asarray(Image.fromarray((ink * 255).astype(np.uint8)).filter(ImageFilter.MaxFilter(3))).astype(np.float64) / 255.0 > 0.5).astype(np.float64)

    # ---- 4. 剪影外描边（深暖棕，加粗，抗锯齿）----
    ad = amask_f.copy()
    for _ in range(3):
        ad2 = ad.copy()
        ad2[1:, :] = np.maximum(ad2[1:, :], ad[:-1, :])
        ad2[:-1, :] = np.maximum(ad2[:-1, :], ad[1:, :])
        ad2[:, 1:] = np.maximum(ad2[:, 1:], ad[:, :-1])
        ad2[:, :-1] = np.maximum(ad2[:, :-1], ad[:, 1:])
        ad = ad2
    edge = ((ad > 0.5) & (~amask)).astype(np.float64)
    edge = (np.asarray(Image.fromarray((edge * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(1.2))).astype(np.float64) / 255.0 > 0.5).astype(np.float64)
    ink = np.maximum(ink, edge)
    ink = ink * amask_f                                 # 不外溢

    out_rgb = np.where(ink[..., None] > 0.5, INK_COL, flat)
    out_rgb = np.clip(out_rgb, 0, 1)

    res = np.zeros((h, w, 4))
    res[..., :3] = out_rgb * 255.0
    res[..., 3] = amask_f * 255.0
    Image.fromarray(res.astype(np.uint8), "RGBA").save(out)
    print(f"wrote {out} ({w}x{h}) levels={levels} vfloor={vfloor} ink_th={ink_th} "
          f"sigma={sigma} warm={warm} inkpx={int((ink>0.5).sum())}")


if __name__ == "__main__":
    main()
