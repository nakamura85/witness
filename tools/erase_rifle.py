# v3：生成尸体专用立绘（整条枪带无条件垂直填充）。
# 枪+双臂横穿胸前，任何选择性擦除都会留残迹；干脆整条带用「带下边缘」色逐列填充：
# 立绘最终在游戏内只有 ~108px 高且躺倒后中部被血迹覆盖，填充痕迹不可见。
from PIL import Image

# 768x1024 逻辑坐标（原图 1680x2240，执行时按比例缩放）
BAND_X0, BAND_X1 = 130, 768
BAND_Y0, BAND_Y1 = 404, 508
D_Y1 = 514  # 两手之间的机匣下段多涂几像素

JOBS = [
    ("assets/art/sprites/soldier_ija.jpg", "assets/art/sprites/soldier_ija_corpse.jpg"),
    ("assets/art/sprites/soldier_na.jpg", "assets/art/sprites/soldier_na_corpse.jpg"),
]

for src, dst in JOBS:
    im = Image.open(src).convert("RGB")
    w, h = im.size
    sx, sy = w / 768.0, h / 1024.0
    px = im.load()
    x0, x1 = int(BAND_X0 * sx), min(w, int(BAND_X1 * sx))
    y0, y1 = int(BAND_Y0 * sy), int(BAND_Y1 * sy)
    d_y1 = int(D_Y1 * sy)
    src_y = min(h - 1, int(552 * sy))  # 腰带以下的纯色军装区，避免把腰带黑边拉成竖线

    for x in range(x0, x1):
        fill = px[x, src_y]
        bottom = d_y1 if 430 <= x / sx <= 520 else y1
        for y in range(y0, bottom):
            px[x, y] = fill
    im.save(dst, quality=95)
    print("saved", dst)
