"""从 wall_niche.png 抠出「纯洞体」：去掉外围那一整圈浅米墙底纸，只留墙上凿出的洞
（深色透视斜面 + 洞口勾边 + 底部窗台横条），使洞直接嵌进游戏真实墙、读作成一体的墙洞。

原理：浅米纸底亮度约 105-180，洞体（深灰斜面/近黑洞底）亮度约 60-100，分离明显。
按亮度把纸底 alpha 归零，保留洞体；2px erode 去残留米边，再轻羽化。零外部依赖。
"""
from PIL import Image, ImageFilter
import numpy as np

SRC = "assets/art/interior/wall_niche.png"
OUT = "tools/bridge_restyle/previews/wall_niche_hole_only.png"

im = Image.open(SRC).convert("RGBA")
a = np.asarray(im).astype(np.int16)
rgb, alpha = a[:, :, :3], a[:, :, 3]
bright = rgb.mean(2)

# 浅米纸底：亮度>=104 一律抠掉（洞体深色/灰斜面保留）
paper = (bright >= 104) & (alpha > 20)
out = np.asarray(im).copy()
out[paper, 3] = 0

# 向外腐蚀 2px（把洞体边缘粘连的米色去掉）
keep = out[:, :, 3] > 20
erode = keep.copy()
for dy in (-2, -1, 1, 2):
    for dx in (-2, -1, 1, 2):
        if dx == 0 and dy == 0:
            continue
        shifted = np.zeros_like(keep)
        y0, y1 = max(0, dy), min(keep.shape[0], keep.shape[0] + dy)
        x0, x1 = max(0, dx), min(keep.shape[1], keep.shape[1] + dx)
        shifted[y0:y1, x0:x1] = keep[y0 - dy:y1 - dy, x0 - dx:x1 - dx]
        erode &= shifted
out[~erode, 3] = 0

res = Image.fromarray(out)
res = res.filter(ImageFilter.GaussianBlur(1.0))
res.save(OUT)

al = np.asarray(res)[:, :, 3]
ys, xs = np.where(al > 60)
print("src", (im.size), "->", OUT)
print("kept frac=%.3f  bbox x[%d..%d] y[%d..%d]" % ((al > 60).mean(), xs.min(), xs.max(), ys.min(), ys.max()))
