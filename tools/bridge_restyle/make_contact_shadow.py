"""v3.86 · 生成「淡墨软阴影」贴图资产（离线自绘，零运行时程序化几何）。

用户拍板：「淡墨软阴影用程序化椭圆是否合规／不要程式化感觉会怪怪」。
v3.85 的 DrawContactShadow 是运行时三层 DrawFlatEllipse —— 完美椭圆 + 规整递减，
正是用户说的「程式化」。本脚本把它换成一张 PNG 贴图资产：

  * 形状仍是压扁椭圆，但半径带 3 个不同频率的角度扰动（手绘晕染的不规则边）；
  * alpha 是连续软衰减，不是三层硬叠；叠多尺度低频噪声做墨色浓淡；
  * 叠细颗粒做纸纹，避免读成「高斯模糊圆」；
  * 颜色取屋内木器/墙面的暖褐墨（不是纯黑）。

运行时只 DrawTextureRect(_shadowTex, dst, false, modulate)，
不再调用任何 DrawColoredPolygon / DrawFlatEllipse。

产物：
  assets/art/interior/contact_shadow.png          （入库，192x64 RGBA）
  tools/bridge_restyle/previews/contact_shadow_candidate.png （候选过目，不入库）
"""

from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "assets/art/interior/contact_shadow.png"
PREVIEW = ROOT / "tools/bridge_restyle/previews/contact_shadow_candidate.png"

W, H = 192, 64
rng = np.random.default_rng(19311104)


def lowfreq(shape, cell, seed):
    """低频噪声：小随机图双线性放大，得到连续斑驳（不是逐像素白噪）。"""
    r = np.random.default_rng(seed)
    h = max(2, shape[0] // cell + 2)
    w = max(2, shape[1] // cell + 2)
    small = r.random((h, w)).astype(np.float32)
    im = Image.fromarray((small * 255).astype(np.uint8), "L").resize(
        (shape[1], shape[0]), Image.BILINEAR
    )
    return np.asarray(im, dtype=np.float32) / 255.0


yy, xx = np.mgrid[0:H, 0:W].astype(np.float32)
cx, cy = W * 0.5, H * 0.5
nx = (xx - cx) / (W * 0.485)
ny = (yy - cy) / (H * 0.470)
theta = np.arctan2(ny, nx)

# 手绘不规则边：三个不同频率叠加，避免「完美椭圆」的程式感
wobble = (
    1.0
    + 0.092 * np.sin(3.0 * theta + 0.7)
    + 0.052 * np.sin(7.0 * theta - 1.2)
    + 0.028 * np.sin(11.0 * theta + 2.1)
    + 0.015 * np.sin(17.0 * theta - 0.4)
)
r = np.sqrt(nx * nx + ny * ny) / wobble

# alpha：中心 0.40 的淡墨，向外连续软衰减到 0
a = np.clip(1.0 - r, 0.0, 1.0) ** 1.45 * 0.40

# 墨色浓淡（斑驳罩染）+ 细颗粒（纸纹笔触）
n1 = lowfreq((H, W), 13, 11)
n2 = lowfreq((H, W), 5, 29)
grain = rng.random((H, W)).astype(np.float32) - 0.5
a *= 0.72 + 0.42 * n1 + 0.18 * n2
# 边缘带做局部「墨断」，消掉均匀渐变的圆润感
rim = np.clip((r - 0.55) / 0.45, 0.0, 1.0)
a *= 1.0 - rim * 0.35 * (1.0 - n2)
a += grain * 0.014 * np.clip(1.0 - r, 0.0, 1.0)
a = np.clip(a, 0.0, 0.44)

# 暖褐淡墨：中心稍深、外缘稍淡（不是纯黑）
k = np.clip(1.0 - r, 0.0, 1.0)
base = np.array([30.0, 23.0, 17.0], dtype=np.float32)
rgb = base * (0.86 + 0.22 * k + 0.10 * n1)[..., None]
rgb = np.clip(rgb, 0, 255)

rgba = np.dstack([rgb, a * 255.0]).astype(np.uint8)
OUT.parent.mkdir(parents=True, exist_ok=True)
Image.fromarray(rgba, "RGBA").save(OUT)

# ---------------- 候选预览（白底 + 真实室内地板局部叠加） ----------------
PREVIEW.parent.mkdir(parents=True, exist_ok=True)
board = Image.new("RGB", (W * 2 + 60, H * 3 + 96), (238, 230, 214))
d = ImageDraw.Draw(board)

# 第一行：纸白底上单看 alpha 形状
for i in range(2):
    board.paste(Image.fromarray(rgba, "RGBA"), (20 + i * (W + 20), 20),
                Image.fromarray(rgba, "RGBA"))
d.text((20, H + 28), "contact_shadow.png  on paper-white", fill=(60, 48, 36))

# 第二/三行：贴到真实 comm_outpost 地板局部，看是否融入背景
bg_path = ROOT / "assets/art/interior/comm_outpost.png"
if bg_path.exists():
    src = Image.open(bg_path).convert("RGB")
    # 取更亮的墙面 / 地面交界，淡墨才看得出来（凳子木色太深会吃掉阴影）
    crop = src.crop((120, 600, 120 + W * 2, 600 + H * 2)).resize((W * 2, H * 2))
    board.paste(crop, (20, H + 58))
    for j in range(2):
        sh = Image.fromarray(rgba, "RGBA").resize((W, H), Image.BILINEAR)
        board.paste(sh, (20 + j * W, H + 58 + H), sh)
    d.text((20, H * 3 + 66), "on real comm_outpost floor (same ink tone)", fill=(60, 48, 36))

board.save(PREVIEW)
print("saved", OUT)
print("saved", PREVIEW)
print("alpha max=%.3f  mean(nonzero)=%.3f" % (a.max(), a[a > 0].mean()))
