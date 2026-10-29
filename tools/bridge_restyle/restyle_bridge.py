#!/usr/bin/env python
"""
把 bridge.png（写实 3D 渲染）转成游戏的手绘上色漫画风（InkStyle）。

目标风格基准（取自 assets/art/props/house_intact_near.png 等同级素材）：
  · 奶油纸底（透明）
  · 黑色墨线勾轮廓 + 内部结构线
  · 蓝灰色水彩平涂，少量暖褐（锈/木）
  · 留白多，饱和度低

手法（纯程序化，保留精确 alpha 轮廓与几何）：
  1. 取 RGB 的亮度 + 少量色相信息
  2. 用 Sobel 找结构边缘 -> 墨线（深墨棕）
  3. 把亮度量化为若干"平涂档位"（base/shadow/highlight），套 InkStyle 色板
  4. 加轻微水彩颗粒 + 纸纹
  5. alpha 原样保留（轮廓形状一个字不改）

用法: python restyle_bridge.py <输入.png> <输出.png>
"""

import sys
import numpy as np
from PIL import Image, ImageFilter

# —— InkStyle 色板（与 scripts/InkStyle.cs 对齐）——
INK = (0.14, 0.10, 0.08)        # 主墨线
INK_SOFT = (0.28, 0.22, 0.18)   # 次级墨线
STEEL = (0.52, 0.58, 0.64)      # NACoat 灰蓝
STEEL_SH = (0.36, 0.40, 0.46)
STEEL_HI = (0.66, 0.72, 0.78)
CONCRETE = (0.72, 0.66, 0.56)   # AdobeWall 暖土
CONCRETE_SH = (0.52, 0.46, 0.36)
CONCRETE_HI = (0.86, 0.82, 0.74)
WOOD = (0.62, 0.44, 0.24)
WOOD_SH = (0.40, 0.26, 0.14)
SNOW = (0.96, 0.97, 0.98)
SNOW_SH = (0.74, 0.78, 0.82)
RUST = (0.47, 0.34, 0.26)
RUST_SH = (0.31, 0.23, 0.18)


def srgb_to_lin(c):
    c = np.asarray(c, dtype=np.float64)
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)


def lin_to_srgb(c):
    c = np.clip(c, 0.0, 1.0)
    return np.where(c <= 0.0031308, c * 12.92, 1.055 * (c ** (1 / 2.4)) - 0.055)


def mix(a, b, t):
    a = np.asarray(a, dtype=np.float64)
    b = np.asarray(b, dtype=np.float64)
    return a * (1 - t) + b * t


def sobel(gray):
    """灰度 Sobel 梯度幅值，忽略 alpha 通道边界。"""
    p = np.pad(gray, 1, mode="edge")
    gx = (p[:-2, 2:] + 2 * p[1:-1, 2:] + p[2:, 2:]) - (p[:-2, :-2] + 2 * p[1:-1, :-2] + p[2:, :-2])
    gy = (p[2:, :-2] + 2 * p[2:, 1:-1] + p[2:, 2:]) - (p[:-2, :-2] + 2 * p[:-2, 1:-1] + p[:-2, 2:])
    return np.sqrt(gx * gx + gy * gy) / 8.0


def dilate(mask, size):
    """膨胀一个二值/灰度蒙版（用 MaxFilter 近似，比逐像素卷积快得多）。"""
    m = Image.fromarray((np.clip(mask, 0, 1) * 255).astype(np.uint8))
    m = m.filter(ImageFilter.MaxFilter(size))
    return np.asarray(m).astype(np.float64) / 255.0


def watercolor_wash(rgb_lin, alpha, lum, seed=7):
    """
    水彩质感：
      · 轻微色相漂移（同一片颜料在纸上不是纯平的）
      · 低频斑驳（水彩沉淀）
      · 纸纹颗粒
    """
    rng = np.random.default_rng(seed)
    h, w = lum.shape

    # 低频色相/明度漂移
    small = rng.normal(0, 1, (max(2, h // 24), max(2, w // 24)))
    small_img = Image.fromarray(((small * 40) + 128).clip(0, 255).astype(np.uint8))
    drift = np.asarray(small_img.resize((w, h), Image.BICUBIC)).astype(np.float64) / 255.0 - 0.5
    # 另一组独立的低频场，做冷暖分离
    small2 = rng.normal(0, 1, (max(2, h // 18), max(2, w // 18)))
    small2_img = Image.fromarray(((small2 * 40) + 128).clip(0, 255).astype(np.uint8))
    warm = np.asarray(small2_img.resize((w, h), Image.BICUBIC)).astype(np.float64) / 255.0 - 0.5

    wash = rgb_lin.copy()
    wash *= (1.0 + (drift * 0.10)[..., None])
    # 暖漂移往赭黄走，冷漂移往青蓝走
    wash[..., 0] += warm * 0.030
    wash[..., 2] -= warm * 0.022

    # 水彩沉淀：暗部多一点颜色堆积，亮部留白
    pigment = np.clip(1.0 - lum, 0, 1)[..., None]
    wash *= (1.0 + pigment * 0.10 * drift[..., None])

    # 纸纹颗粒（细密、微弱）
    grain = rng.normal(0, 1, (h, w))[..., None]
    wash += grain * 0.016

    return np.clip(wash, 0.0, 1.0)


def smooth_mask(mask, radius):
    """对材质蒙版做强平滑：写实渲染上的锈迹是碎噪点，
    直接按像素取色会得到一片喷溅，必须先糊成一整片色块。"""
    m = Image.fromarray((mask * 255).astype(np.uint8))
    m = m.filter(ImageFilter.GaussianBlur(radius))
    return np.asarray(m).astype(np.float64) / 255.0


def open_mask(mask, size=3):
    """形态学开运算（先腐蚀后膨胀）：抹掉面积太小的碎块。

    写实图上每个铆钉都是一个小亮点，雪的蒙版会把它们一个个当成雪，
    结果桥面上撒满白点。开运算只留下成片的区域。
    """
    m = Image.fromarray((np.clip(mask, 0, 1) * 255).astype(np.uint8))
    m = m.filter(ImageFilter.MinFilter(size))
    m = m.filter(ImageFilter.MaxFilter(size))
    return np.asarray(m).astype(np.float64) / 255.0


def drop_small_blobs(mask, min_area):
    """
    丢掉面积小于 min_area 的连通块。

    这一步是整个风格迁移里最关键的一环。钢桁架上密排着铆钉，
    写实渲染里每个铆钉都是一圈高光加一圈阴影——在梯度图上，
    每个铆钉都是一条完整的闭合边。直接勾线，出来的就是
    满屏芝麻大的墨点，跟房子那种一笔一笔的手绘完全不像。

    铆钉的面积只有几十像素，而桁架构件的长边是几百上千像素。
    按面积一刀切下去，铆钉全部消失，构件一根不少。
    """
    m = (np.clip(mask, 0, 1) > 0.5)
    if not m.any():
        return mask

    h, w = m.shape
    lab = np.zeros((h, w), dtype=np.int32)
    cur = 0
    areas = []
    # 4 邻接洪水填充
    idx = np.argwhere(m)
    seen = np.zeros((h, w), dtype=bool)
    for sy, sx in idx:
        if seen[sy, sx]:
            continue
        cur += 1
        stack = [(sy, sx)]
        seen[sy, sx] = True
        n = 0
        while stack:
            y, x = stack.pop()
            lab[y, x] = cur
            n += 1
            for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                ny, nx = y + dy, x + dx
                if 0 <= ny < h and 0 <= nx < w and m[ny, nx] and not seen[ny, nx]:
                    seen[ny, nx] = True
                    stack.append((ny, nx))
        areas.append(n)

    areas_arr = np.asarray(areas)
    small = np.isin(lab, np.flatnonzero(areas_arr < min_area) + 1)
    out = mask.copy()
    out[small] = 0.0
    return out


def wipe_deck_railing(rgb, alpha, lum, rows_range, x_range=None):
    """
    擦掉贴图桥面带上的检修走道栏杆。

    这是"一个桥出现了两条路"的真正来源：贴图在桥面高度上带着一整排
    立柱 + 上下横杆（检修走道），而代码又在同一个高度画了钢轨。
    两条几乎平行的长条紧挨着，眼睛就把它们读成两条路。

    真实做法是拆掉走道栏杆、把这一带重画成干净的钢桥板边。
    这里把栏杆所在的那几行直接压成均匀的钢灰，
    栏杆的"洞"（透明格）保持透明，桁架的斜杆不受影响。
    """
    out = rgb.copy()
    a = alpha.copy()
    r0, r1 = rows_range
    x0, x1 = (0, rgb.shape[1]) if x_range is None else x_range

    band = out[r0:r1, x0:x1]
    band_lum = lum[r0:r1, x0:x1]

    # 栏杆区域里，亮像素是栏杆横杆和雪，暗像素是桁架斜杆。
    # 只把"窄而亮"的横杆压掉：先在竖直方向做邻域比较，
    # 一个像素若在上下 3 行内比邻居亮很多，它是横杆；
    # 竖直的斜杆在竖直方向是连续的，不会被这个判据选中。
    up = np.roll(band_lum, 1, axis=0)
    dn = np.roll(band_lum, -1, axis=0)
    horiz = (band_lum - np.maximum(up, dn))
    rail = np.clip(horiz * 6.0, 0, 1)
    rail[0, :] = 0
    rail[-1, :] = 0
    # 栏杆横杆通常在带子的上半部分（紧贴桁架下弦）
    vpos = np.repeat(np.linspace(1.0, 0.0, r1 - r0)[:, None], x1 - x0, axis=1)
    rail *= np.clip(vpos * 1.6, 0, 1)

    # 压成钢灰（跟 InkStyle 的 NACoat 一致）
    steel = np.asarray(STEEL, dtype=np.float64)
    band[:] = band * (1 - rail[..., None] * 0.85) + steel * (rail[..., None] * 0.85)
    out[r0:r1, x0:x1] = band
    return out, a


def drop_short_strokes(mask, min_len=90):
    """
    丢掉"短笔画"，保留长线条。

    一根构件的墨线是几百像素的长条；一根缀条被抹掉后留下的墨线
    是几十像素的短截。画上手绘插画里没有一地碎墨点，
    所以按长度把短的清掉。

    长度用连通域的"对角线跨度"来量：横平竖直的长线跨度大，
    碎墨点跨度小。
    """
    m = mask > 0.5
    if not m.any():
        return mask

    h, w = m.shape
    seen = np.zeros((h, w), dtype=bool)
    out = mask.copy()
    for sy, sx in np.argwhere(m):
        if seen[sy, sx]:
            continue
        stack = [(sy, sx)]
        seen[sy, sx] = True
        cells = []
        while stack:
            y, x = stack.pop()
            cells.append((y, x))
            for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                ny, nx = y + dy, x + dx
                if 0 <= ny < h and 0 <= nx < w and m[ny, nx] and not seen[ny, nx]:
                    seen[ny, nx] = True
                    stack.append((ny, nx))
        ys = [c[0] for c in cells]
        xs = [c[1] for c in cells]
        span = max(max(ys) - min(ys), max(xs) - min(xs))
        if span < min_len:
            for y, x in cells:
                out[y, x] = 0.0
    return out


def find_enclosed_holes(alpha):
    """
    找出被构件完全包围的透明区域，并按面积分成两类。

    写实钢桁架有两种"洞"：
      · 缀条格构的小方格（写实图里斜杆内部密排的细节）
      · 主桁架的大透空（三角形的通风口，是桁架该有的通透感）

    实测 2468 个天空连通域：2424 个小于 400px（缀条格构），
    43 个大于等于 400px（主桁架大透空）。这个分界非常干净，
    可以直接用面积阈值把两者分开。
    """
    m = alpha > 0.5
    h, w = m.shape
    lab = np.zeros((h, w), np.int32)
    cur = 0
    cells = {}
    sizes = {}

    for sy in range(h):
        for sx in range(w):
            if lab[sy, sx] or m[sy, sx]:
                continue
            cur += 1
            stack = [(sy, sx)]
            lab[sy, sx] = cur
            n = 0
            pts = []
            while stack:
                y, x = stack.pop()
                n += 1
                pts.append((y, x))
                for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                    ny, nx = y + dy, x + dx
                    if 0 <= ny < h and 0 <= nx < w and not lab[ny, nx] and not m[ny, nx]:
                        lab[ny, nx] = cur
                        stack.append((ny, nx))
            sizes[cur] = n
            cells[cur] = pts

    if not sizes:
        return [], []

    external = max(sizes, key=lambda k: sizes[k])
    small = [cells[k] for k, n in sizes.items() if k != external and n < 400]
    large = [cells[k] for k, n in sizes.items() if k != external and n >= 400]
    return small, large


def hole_edges(holes, alpha):
    """
    孔洞的边界像素 = 紧贴孔洞的构件像素。

    给这些像素上一层墨线，就是"给每个小方格勾一圈边"。
    """
    edge = np.zeros(alpha.shape, dtype=np.float64)
    for pts in holes:
        for y, x in pts:
            for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                ny, nx = y + dy, x + dx
                if 0 <= ny < alpha.shape[0] and 0 <= nx < alpha.shape[1]:
                    edge[ny, nx] = 1.0
    return edge


def distance_transform(mask, max_d=48):
    """
    距离变换：每个实体像素到最近背景像素的距离。

    用反复腐蚀来算：每做一次 3x3 的 MinFilter(腐蚀)，
    距离就近一格；像素在第几轮消失，它的距离就是几。
    纯 numpy + PIL，够快（1530x812 跑 48 轮不到两秒）。
    """
    cur = (mask > 0.5)
    dist = np.zeros(mask.shape, dtype=np.float64)
    for i in range(1, max_d + 1):
        nxt = cur.copy()
        nxt[1:, :] &= cur[:-1, :]
        nxt[:-1, :] &= cur[1:, :]
        nxt[:, 1:] &= cur[:, :-1]
        nxt[:, :-1] &= cur[:, 1:]
        cur = nxt
        dist += cur
    return dist


def horizontal_run(mask):
    """
    每个实体像素所在的"水平连续段"有多长。

    累积法：左边用 np.maximum.accumulate 找本段最左的实体像素，
    右边用反向 np.minimum.accumulate 找本段最右的实体像素。
    """
    h, w = mask.shape
    idx = np.arange(w)
    row = mask
    left = np.maximum.accumulate(np.where(row, idx, -1), axis=1)
    right = np.minimum.accumulate(np.where(row, idx, w)[:, ::-1], axis=1)[:, ::-1]
    run = (right - left + 1).astype(np.float64)
    return np.where(row, run, 0.0)


def deck_top_profile(w, y0=342.0, slope=0.054, ymin=330.0, ymax=424.0):
    """
    桥面顶边在贴图里的行号（每一列一个值）。

    这条线是斜的：原图是一张透视渲染，桥从左向右往后缩，
    近端的桥面在第 342 行，远端已经落到第 413 行。
    实测值（用"连续实心 25 行"定桥面顶）：
        x=0 -> 342    x=240 -> 355   x=720 -> 382
        x=600 -> 372  x=1080 -> 400  x=1320 -> 413
    线性拟合 y = 342 + 0.054x 除了 x=480 一处局部特征外全部命中，
    所以直接用这条线，够准也够稳。
    """
    x = np.arange(w, dtype=np.float64)
    return np.clip(y0 + slope * x, ymin, ymax)


def remove_deck_railing(alpha, band_h=52, core_size=11, halo=17, guard=3, thin_max=17.0):
    """
    拆掉桥面上的检修走道栏杆——"一个桥出现两条路"的另一半根因。

    现实里，1931 年的嫩江江桥是公路桥，桥面上不会有铁路检修走道；
    而这一关的主角是铁轨。所以栏杆要去掉。

    难点是分清"栏杆"和"桁架"。颜色分不开（两者都是钢，偏色几乎一样），
    位置也分不开（它们在同一片行里交错）。能分开的是**粗细**：

      · 桁架的主构件（弦杆、斜杆、立柱）宽 25-40px，是粗的
      · 桁架腹杆的缀条 8-14px，是细的
      · 检修走道的立柱和横杆 4-10px，也是细的

    光看"细"会连缀条一起删——那没关系，缀条本来就是多余的细节，
    删掉之后它留下的洞会被后面的填孔步骤补回实心，构件还是实心的。

    真正要保护的是**粗构件本身**。做法是形态学的"厚薄分离"：

        core  = 开运算(11)     只剩粗构件的核心
        prot  = 膨胀(core)     粗构件外扩 8px 的保护区
        thin  = 实心 & 不在保护区里   立在空中的细杆

    桁架斜杆的边缘（1-3px 毛边）落在保护区里，不会被啃掉；
    栏杆立柱离任何粗构件都有 30px 以上，一定被选中。

    保护规则：桥面顶板以上 guard 行不删，免得把雪线和桥面边缘啃出缺口。
    """
    h, w = alpha.shape
    m = alpha > 0.5

    deck_top = deck_top_profile(w)
    rows = np.arange(h)[:, None]
    band = (rows >= (deck_top[None, :] - band_h)) & (rows <= (deck_top[None, :] - guard))

    # ——— 判据一：**构件的实际厚度** ———
    #
    # 距离变换给出每个实体像素到最近背景的距离，乘二就是这一处的构件厚度。
    #   桁架主构件 25-40px 厚  ->  厚
    #   桁架腹杆的缀条 8-14px  ->  薄（细节，本来就该删）
    #   检修走道的立柱横杆 4-14px  ->  薄
    #
    # 之前只用形态学开运算分粗细，门槛卡在 11px，
    # 结果栏杆那根 12px 粗的顶横杆被当成"粗构件"保住了，
    # 栅栏拆不干净。现在改成直接量厚度，门槛 8px（半宽 <= 8）。
    dist = distance_transform(m)
    thin = (2.0 * dist + 1.0) <= thin_max

    # ——— 判据二：**这一列上面还有没有桁架** ———
    #
    # 远端的桁架因为透视会变得很细（远端构件只有 6-10px 宽），
    # 单看厚度会把远端的桥也当成栏杆啃掉。
    # 所以再加一条：这一列在带子上方 20 行里，如果还连着 >= 10 像素的实体，
    # 说明有桁架构件从这里穿过去，这一列只按"厚度"删，不整列清空。
    cs = np.cumsum(m.astype(np.int32), axis=0)
    cs_pad = np.vstack([np.zeros((1, w), dtype=np.int32), cs])
    win = np.maximum(np.arange(h) - 20, 0)
    cont = (cs_pad[np.arange(h)] - cs_pad[win]) >= 10

    # 粗构件保护区：桁架主构件的边缘有 1-3px 毛边，别啃掉
    core = open_mask(m.astype(np.float64), core_size) > 0.5
    prot = dilate((core & band).astype(np.float64), halo) > 0.5
    prot &= band

    # 判据三：**长横杆**。
    #
    # 远端透视里的走道扶手会变粗（12-20px），前两条判据都漏掉了它，
    # 结果在轨道上方留下一小段悬空的细线。
    # 横杆的特征很好认：水平方向连得很长（>=40px），竖直方向却很薄。
    # 桁架在这个高度上只有斜杆和立柱，横着走的只有扶手本身。
    hrun = horizontal_run(m)
    longbar = (hrun >= 40.0) & (thin | ((2.0 * dist + 1.0) <= 24.0))

    # 删三种：
    #   A. 带子里所有"薄"的像素 —— 栏杆和缀条都归在这一类
    #   B. 整根立柱 —— 那些上方没有任何桁架结构、只在带子里露头的竖条
    #      （栏杆立杆在远处透视里最细，靠厚度判据容易漏，这里兜底）
    #   C. 长横杆 —— 粗一点的走道扶手
    delete_a = band & m & thin & ~prot
    delete_b = band & m & ~cont & ~prot
    delete_c = band & m & longbar & ~prot
    delete = delete_a | delete_b | delete_c

    out = alpha.copy()
    out[delete] = 0.0
    return out, delete


def remove_deck_railing_dbg(alpha, rgb, band_h=52, core_size=11, halo=17, guard=3, thin_max=17.0):
    """带调试输出的版本：把被删掉的像素标红画在原图上。"""
    h, w = alpha.shape
    m = alpha > 0.5
    deck_top = deck_top_profile(w)
    rows = np.arange(h)[:, None]
    band = (rows >= (deck_top[None, :] - band_h)) & (rows <= (deck_top[None, :] - guard))
    dist = distance_transform(m)
    thin = (2.0 * dist + 1.0) <= thin_max
    cs = np.cumsum(m.astype(np.int32), axis=0)
    cs_pad = np.vstack([np.zeros((1, w), dtype=np.int32), cs])
    win = np.maximum(np.arange(h) - 20, 0)
    cont = (cs_pad[np.arange(h)] - cs_pad[win]) >= 10
    core = open_mask(m.astype(np.float64), core_size) > 0.5
    prot = dilate((core & band).astype(np.float64), halo) > 0.5
    prot &= band
    hrun = horizontal_run(m)
    longbar = (hrun >= 40.0) & ((2.0 * dist + 1.0) <= 24.0)
    delete = (band & m & thin & ~prot) | (band & m & ~cont & ~prot) | (band & m & longbar & ~prot)

    out = alpha.copy()
    out[delete] = 0.0

    dbg = np.zeros((h, w, 4), dtype=np.uint8)
    a = rgb[..., 3:4]
    dbg[..., :3] = ((rgb[..., :3] * a + (1 - a)) * 255).astype(np.uint8)
    dbg[..., 3] = 255
    dbg[delete, 0] = 255
    dbg[delete, 1] = 0
    dbg[delete, 2] = 0
    return out, delete, Image.fromarray(dbg)



def restyle(src_path, dst_path, ink_strength=1.0):
    im = Image.open(src_path).convert("RGBA")
    arr = np.asarray(im).astype(np.float64) / 255.0
    rgb = arr[..., :3]
    alpha = arr[..., 3]
    h, w = alpha.shape

    # ——— 0.0 拆掉检修走道栏杆 ———
    #
    # 必须在填孔之前做。栏杆的每一格都是被立柱和横杆围住的一个"洞"，
    # 面积不到 400px，于是会被下面 0.1 步的小孔洞填充填成实心——
    # 一排栅栏变成一整条实心蓝灰色板，横在轨道正上方，
    # 眼睛立刻把它读成"第二条路"。这正是"一个桥出现两条路"的另一半根因。
    alpha, _rail_del = remove_deck_railing(alpha)

    # ——— 0.1 填掉缀条格构的小孔洞 ———
    # 写实钢桁架的斜杆是缀条格构的组合杆，杆件内部有一列列密排的
    # 透空小方格。在贴图里这些方格是"天空"（透明）。
    #
    # 这是满屏白片的根源：这些小方格在成品图里是透明的，
    # 游戏里透出浅色的背景雪原，于是梁身上撒满白点。
    #
    # 实测 2468 个天空连通域：2424 个小于 400px（缀条格构），
    # 43 个大于等于 400px（主桁架的大透空）。分界非常干净。
    # 只填小的，主桁架的透空留着——那是桁架该有的通透感，
    # 填掉的话整座桥会变成一堵墙（v16 犯过这个错）。
    small_holes, big_holes = find_enclosed_holes(alpha)
    for pts in small_holes:
        for y, x in pts:
            alpha[y, x] = 1.0

    # 孔洞里的原始 RGB 是渲染残渣（多是纯黑），填成实体后
    # 直接参与降采样会把黑色带进梁身。更要紧的是，
    # 填进去的像素会被明度分档判成"暗部"或"天空"，
    # 于是梁身上出现一块跟背景同色的斑——梁看起来就"消失"了。
    #
    # 正确的做法是让填进去的像素继承"它属于哪根梁"：
    # 用一张距离变换，把最近的实体像素的���色搬过来。
    # 这里用迭代扩散近似（每轮把邻域颜色平均推进一格），
    # 对小方格（最大 400px）来说 6 轮足够。
    orig_alpha = arr[..., 3]
    filled_mask = (orig_alpha <= 0.5) & (alpha > 0.5)
    if filled_mask.any():
        src_rgb = rgb.copy()
        for _ in range(8):
            blurred = np.asarray(
                Image.fromarray((src_rgb * 255).clip(0, 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(3))
            ).astype(np.float64) / 255.0
            src_rgb = np.where(filled_mask[..., None], blurred, src_rgb)
        rgb = src_rgb
    holes = big_holes

    # ——— 0. 结构层 / 细节层 双通道 ———
    #
    # 写实渲染和手绘插画的差别不在"有没有细节"，而在"细节的密度和性质"。
    # 这张钢桁架上密排着铆钉、节点板、斜杆内部的腹杆——每一处都是
    # 二十到四十像素的独立结构，画在纸上就是一台机器的照片。
    # 手绘插画不会画这些，它只画"这根梁在这里"。
    #
    # 所以拆成两层：
    #   · 细节层 = 原图。用来找出墨线该落在哪里（构件的边界）。
    #   · 色块层 = 降采样 x6 后的平图。所有的平涂色都从这里取，
    #     于是所有铆钉、节点板、腹杆全部消失，只剩大结构。
    # 这样出来的才是"手绘的桥"，而不是"照片降了个分辨率"。
    #
    # 关键坑：降采样必须用"预乘 alpha"的 RGB。
    # 直接 im.convert('L') 会丢掉 alpha，把透明像素里残留的黑色
    # 也算进灰度——天空区域于是变成一片噪点麻脸，
    # 后面无论怎么滤都回不去了。预乘之后天空是纯白，干净。
    detail_lum = rgb[..., 0] * 0.299 + rgb[..., 1] * 0.587 + rgb[..., 2] * 0.114

    ds = 6
    # 预乘 alpha：把颜色按覆盖率压到黑底上（透明处 = 黑）
    # 用"填了缀条孔洞"的 alpha，小方格里的垃圾 RGB 就被当成实体色处理，
    # 降采样时会被周围的构件色正确地平均进去。
    premul = Image.fromarray(
        (rgb * alpha[..., None] * 255).clip(0, 255).astype(np.uint8)
    ).convert("L")
    # 缩小后 alpha 也一起缩，避免天空的白色渗进桁架内部
    small_a = Image.fromarray((alpha * 255).astype(np.uint8)).resize((w // ds, h // ds), Image.LANCZOS)
    small_p = premul.resize((w // ds, h // ds), Image.LANCZOS)
    # 用小图 alpha 把预乘结果还原回"覆盖率"：除以 alpha 得到真实颜色
    small_a_np = np.asarray(small_a).astype(np.float64) / 255.0
    small_p_np = np.asarray(small_p).astype(np.float64) / 255.0
    safe_a = np.maximum(small_a_np, 1e-3)
    small_flat = np.clip(small_p_np / safe_a, 0.0, 1.0)
    # 天空（几乎全透明）处没有颜色可言，直接给白
    small_flat[small_a_np < 0.35] = 1.0

    # v7 关键：在"降采样空间"里做开运算，剔掉构件内部的缀条。
    #
    # 写实桁架的斜杆不是一根实心梁，而是一根缀条格构的组合杆——
    # 里面排着一列细密的交叉缀条。这些东西在原图上是 8-15px 宽的
    # 独立结构，中值滤波和连通域过滤都留得住它们（它们连着梁，不孤立）。
    #
    # 但降采样 x6 之后，比例反过来了：
    #     缀条  -> 1-2px
    #     主梁  -> 4-7px
    # 在降采样空间做一次 3x3 开运算（腐蚀再膨胀），
    # 宽度不足 3px 的缀条被整条抹掉，4px 以上的主梁一根不少。
    # 这是在正确的尺度上做正确的操作——比在原图上硬压有效得多。
    small_img = Image.fromarray((small_flat * 255).astype(np.uint8))
    small_img = small_img.filter(ImageFilter.MinFilter(3)).filter(ImageFilter.MaxFilter(3))
    small_flat = np.asarray(small_img).astype(np.float64) / 255.0
    small_flat[small_a_np < 0.35] = 1.0

    # v8: 再清一次构件内部的碎点。
    #
    # 缀条被开运算抹掉之后，它原来遮挡的那块构件内部留下了暗色小斑——
    # 在画面上就是满屏的芝麻点。这些斑的特征很明显：
    # 尺寸小（降采样后 1-3px）、且明显比周围的构件暗。
    #
    # 做法：把构件区域按"局部暗于邻域"切出来，判为脏点，
    # 然后用邻域的中位色填回去。只在构件内部做（天空不碰），
    # 所以大构件之间的明暗关系原样保留。
    #
    # 面积阈值 150（降采样空间）：实测 418 个暗块里，
    # 最大的 485 是真正的结构阴影（烧焦段、桁架内侧暗面）必须留，
    # 37-121 的一律是缀条残影，抹掉。
    small_lum = small_flat.copy()
    sm = np.asarray(
        Image.fromarray((small_lum * 255).astype(np.uint8)).filter(ImageFilter.MedianFilter(9))
    ).astype(np.float64) / 255.0
    sm[small_a_np < 0.35] = small_lum[small_a_np < 0.35]
    darker = np.clip((sm - small_lum) * 5.0, 0, 1)
    darker[small_a_np < 0.35] = 0.0
    darker = drop_small_blobs(darker, 150)
    fill = np.asarray(
        Image.fromarray((small_lum * 255).astype(np.uint8)).filter(ImageFilter.MedianFilter(5))
    ).astype(np.float64) / 255.0
    small_flat = small_flat * (1 - darker) + fill * darker
    small_flat[small_a_np < 0.35] = 1.0

    flat_lum = np.asarray(
        Image.fromarray((small_flat * 255).astype(np.uint8))
        .resize((w, h), Image.BICUBIC)
        .filter(ImageFilter.GaussianBlur(5.0))
    ).astype(np.float64) / 255.0
    # 降采样后的大结构边缘会有轻微锯齿，再补一次中值抹平
    flat_lum = np.asarray(
        Image.fromarray((np.clip(flat_lum, 0, 1) * 255).astype(np.uint8)).filter(ImageFilter.MedianFilter(5))
    ).astype(np.float64) / 255.0

    # ——— 1. 材质分区 ———
    lum = flat_lum
    opaque = alpha > 0.5
    mx = rgb.max(axis=2)
    mn = rgb.min(axis=2)
    sat = mx - mn
    warmth = rgb[..., 0] - rgb[..., 2]        # >0 偏暖

    warm_blur = np.asarray(
        Image.fromarray((np.clip((warmth + 0.5), 0, 1) * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(3.2))
    ).astype(np.float64) / 255.0 * 2 - 1

    # 雪：很亮且接近中性。开运算抹掉铆钉反光这种碎亮点，
    # 否则桥面上会撒满芝麻大的白点。
    snow_raw = (lum > 0.78) & (sat < 0.22)
    snow_mask = smooth_mask(snow_raw.astype(np.float64), 2.0)
    snow_mask = drop_small_blobs(open_mask(snow_mask, 7), 220)
    # v18: 雪只出现在该出现的地方。
    # 写实渲染里钢构件的受光面非常亮，在明度上跟雪一个级别，
    # 于是梁面上到处被判成雪，画出来就是白片。
    # 现实里雪是积在水平面上的，不会挂在竖着的斜杆上；
    # 所以桁架区（0-340 行）一律不给雪。
    snow_mask[:340, :] = 0.0
    # 桥面（340-400）可以有雪，但只有薄薄一层
    snow_mask[340:400, :] *= 0.45
    # 桥墩：只有帽梁附近（470-520）可以有雪，越往下越没有
    snow_mask[400:470, :] = 0.0
    snow_mask[470:520, :] *= np.repeat(
        (1.0 - (np.arange(470, 520) - 470) / 50.0)[:, None], w, axis=1
    )
    snow_mask[520:, :] = 0.0
    # 木/锈：偏暖。桥墩上的锈迹在写实图里是一层脏噪点，
    # 直接当成"木"会得到一片橙色喷溅。这里把暖色蒙版收得很紧，
    # 只留真正偏红褐的结构件（烧焦的桁架、锈蚀的钢梁）。
    # v23: 暖色蒙版收紧。
    # 原来 warm_blur > 0.075 太松，写实图上每一点锈迹都被抓进来，
    # 平涂之后变成一摊一摊的褐色喷溅，在手绘风里像脏点而不是锈。
    # 门槛抬到 0.105，只有真正成片的锈才留得下来。
    warm_raw = (warm_blur > 0.105) & (~snow_raw) & (lum > 0.12)
    warm_mask = smooth_mask(warm_raw.astype(np.float64), 5.0)
    # 其余按行分区判钢或混凝土
    rows = np.arange(h)[:, None]
    # 桥墩区（440 往下）偏混凝土；桥面/桁架区偏钢
    pier_zone = np.repeat((rows >= 440), w, axis=1)

    # ——— 2. 墨线 ———
    #
    # 墨线要从"色块层"上找，不是细节层。
    # 细节层上每个铆钉、每根缀条都是一条闭合边，勾出来又是满屏芝麻点；
    # 色块层（降采样 x6 + 开运算）上只剩大构件的边界——这正是手绘该勾的线。
    #
    # 但色块层的天空是纯白，如果直接求梯度，桁架和天空的交界
    # 会产生极强的一条边，而天空内部的噪点也会被当成边。
    # 所以先把亮度按 alpha 压平（天空=中性灰），只让构件内部的
    # 明暗差产生梯度，轮廓则交给 alpha 边界单独处理。
    #
    # 梯度也在降采样空间算，再放大回原尺寸——这样墨线宽度可控，
    # 而且和色块的边界严丝合缝。
    grad_small = sobel(
        np.asarray(
            Image.fromarray((np.clip(small_flat, 0, 1) * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(0.8))
        ).astype(np.float64) / 255.0
    )
    # 天空（透明）在色块层是纯白，桁架和天空的交界是一条极强的边。
    # 但外轮廓已经由 alpha 单独负责了，所以这里把透明区压平，
    # 只保留构件内部的明暗差——不然轮廓会被画两遍且位置对不上。
    grad_small = grad_small * (small_a_np > 0.35)
    grad = np.asarray(
        Image.fromarray((np.clip(grad_small, 0, 1) * 255).astype(np.uint8)).resize((w, h), Image.BICUBIC)
    ).astype(np.float64) / 255.0

    # alpha 边界 -> 外轮廓。
    # 做法是"从 alpha 边界向内收缩"，而不是向外膨胀：
    # 向外扩会把轮廓画到透明区域去（v10 的满屏芝麻点就是这么来的）。
    # 向内收则得到一条紧贴构件边缘的墨线，跟手绘勾线一个意思。
    #
    # 小孔洞（缀条格构）此时已经被填成实体了，所以 alpha 边界上
    # 只剩主桁架的大结构线——每根梁、每个节点的边界，一笔到位。
    a_img = Image.fromarray((alpha * 255).astype(np.uint8))
    a_er = np.asarray(a_img.filter(ImageFilter.MinFilter(5))).astype(np.float64) / 255.0
    outline = np.clip(alpha - a_er, 0, 1)
    outline = np.asarray(
        Image.fromarray((outline * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(0.8))
    ).astype(np.float64) / 255.0

    # 结构线阈值：梯度够大就是一根构件的边。
    # 阈值取高一些 + 梯度先模糊，只保留真正的构件边，
    # 否则写实渲染的每个铆钉都会变成一根墨线。
    grad_s = np.asarray(
        Image.fromarray((np.clip(grad, 0, 1) * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(1.1))
    ).astype(np.float64) / 255.0
    thr = max(np.percentile(grad_s[alpha > 0.5], 97) * 0.80, 0.045)
    struct = np.clip((grad_s - thr) / max(thr, 1e-4), 0, 1)
    struct = smooth_mask(struct, 0.7)
    # 粗墨线：把结构线加粗，勾线才有手绘的分量。
    # 同级素材（房子）的轮廓线接近 3px，贴图在游戏里会缩到 0.5 左右，
    # 所以贴图上得画到 5-6px 才等效。
    #
    # 顺序很重要：
    #   1. 开运算去掉孤立碎边
    #   2. 丢掉面积太小的连通块
    #   3. 丢掉短笔画 —— 缀条被色块层抹掉后留下的短墨点
    #   4. 加粗成连续的一笔
    #
    # 第 3 步是 v9 的关键。构件的墨线是长条（几百 px），
    # 缀条的墨线是短截（几十 px）。按连通域的跨度一刀切，
    # 短的全删，长的全留。手绘插画里没有一地碎墨点。
    #
    # 长度阈值在"降采样空间"里量（这里 struct 还是原尺寸，
    # 但阈值按比例给：降采样 x6，90px 的原图跨度 = 15px 的小图跨度）。
    struct = open_mask(struct, 5)
    struct = drop_small_blobs(struct, 300)
    struct = drop_short_strokes(struct, min_len=110)
    struct = dilate(struct, 5)
    struct = smooth_mask(struct, 0.8)
    struct = np.clip(struct * 1.25, 0, 1)

    # 外轮廓：alpha 边界，同样加粗
    outline = dilate(outline, 3)
    outline = np.clip(outline * 1.35, 0, 1)

    ink_mask = np.clip(struct * 0.45 + outline * 1.75, 0, 1) * ink_strength

    # v10 关键修复：墨线必须严格待在 alpha 之内。
    #
    # 轮廓线是"从 alpha 边界向外膨胀再相减"得到的，它天生会跑到
    # 透明区域去；而结构线是在色块层上算的，那里天空是纯白，
    # 桁架和天空的交界也会产生一条强边。
    #
    # 结果就是：墨线画到了本来就该是天空的地方。透明像素的 RGB 是
    # 无意义的残留值（这里是 255 白），一旦被墨线覆盖成深棕，
    # 成品图上就是满屏芝麻黑点——而且这些点还是在"透明"区域里，
    # 合成到游戏里才看得见。
    #
    # 所以墨线一律乘上 alpha 掩膜：只画在构件上，绝不越界。
    ink_mask = ink_mask * alpha

    # ——— 3. 平涂色板 ———
    # 每一档（暗/中/亮）取自 InkStyle 的三档平涂，材质不同色相不同
    def tri(dark, base, light):
        return np.stack([dark, base, light], axis=0)

    steel_pal = tri(STEEL_SH, STEEL, STEEL_HI)
    conc_pal = tri(CONCRETE_SH, CONCRETE, CONCRETE_HI)
    warm_pal = tri(WOOD_SH, WOOD, (0.78, 0.60, 0.34))
    rust_pal = tri(RUST_SH, RUST, (0.63, 0.48, 0.35))
    snow_pal = tri(SNOW_SH, SNOW, (1.0, 1.0, 1.0))

    # 亮度分档（阈值按贴图实际分布算，不写死）
    # 只统计不透明像素：天空在色块层是纯白，把它算进来会把
    # 整个阈值体系往上推，桥身的暗部就全被拉亮成中间调。
    # 用原始亮度统计——色块层已经把结构细节抹平了，
    # 阈值要按原始的明暗关系来定，明暗层次才立得住。
    src_lum_stat = rgb[..., 0] * 0.299 + rgb[..., 1] * 0.587 + rgb[..., 2] * 0.114
    lo_v = np.percentile(src_lum_stat[opaque], 8)
    mid_v = np.percentile(src_lum_stat[opaque], 46)
    hi_v = np.percentile(src_lum_stat[opaque], 80)

    band = np.zeros((h, w), dtype=np.int64)   # 0=暗 1=中 2=亮
    band[lum >= mid_v] = 1
    band[lum >= hi_v] = 2

    # v2: 明度压缩 + 整体提亮。
    # 写实渲染的动态范围比手绘水彩大得多——最暗处几乎纯黑、最亮处纯白，
    # 贴到纸上就是"脏"。同级素材（房子）的墨线负责最暗的部分，
    # 色块本身只走窄窄的一小段明度。所以这里把分档阈值往中间收，
    # 让暗部几乎全部交给墨线，明部全部留给纸底。
    # 场景本身是明亮的雪景，桥如果压得太暗就会在画面里"塌"下去。
    mid_v = mid_v * 0.86 + hi_v * 0.26
    hi_v = mid_v + (hi_v - mid_v) * 0.62
    # v16: 补回明暗层次。
    # 填掉缀条之后，所有梁都是同一个蓝灰色，干净是干净了，
    # 却像一张剪纸——没有体积。手绘插画的做法是给同一个物体
    # 分出受光面和背光面（InkStyle 的 base / shadow / highlight）。
    #
    # 这里用构件自身的原始亮度做明暗判断——但原始亮度带着铆钉噪声，
    # 会被铆钉带偏。所以先用一次大模糊把噪声摊开，
    # 剩下的就是构件整体的受光关系：贴图的光源在右上方，
    # 于是每根梁的上边缘偏亮、下边缘偏暗。
    src_lum = rgb[..., 0] * 0.299 + rgb[..., 1] * 0.587 + rgb[..., 2] * 0.114
    src_lum = np.asarray(
        Image.fromarray((np.clip(src_lum, 0, 1) * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(7))
    ).astype(np.float64) / 255.0
    # 只保留构件内部（天空不参与），天空压到中间调
    lum_vol = src_lum * opaque + 0.45 * (~opaque)
    band = np.zeros((h, w), dtype=np.int64)
    band[lum_vol >= mid_v] = 1
    band[lum_vol >= hi_v] = 2

    # 材质 id: 0=钢 1=混凝土 2=木 3=锈 4=雪
    mat = np.zeros((h, w), dtype=np.int64)
    mat[pier_zone] = 1
    mat[warm_mask > 0.66] = 2
    # 桁架区里的深色锈迹
    rust_zone = (warm_mask > 0.66) & (lum < 0.30) & (~pier_zone)
    mat[rust_zone] = 3
    mat[snow_mask > 0.5] = 4

    # 组装每种材质的调色板查找
    palettes = [steel_pal, conc_pal, warm_pal, rust_pal, snow_pal]
    out = np.zeros((h, w, 3), dtype=np.float64)
    for m_id, pal in enumerate(palettes):
        sel = mat == m_id
        if not sel.any():
            continue
        out[sel] = pal[band[sel]]

    # 混凝土/钢在雪线附近提亮一点（受光面），形成明暗分界
    out = mix(out, np.stack([SNOW_SH, SNOW, SNOW], axis=0)[band], (snow_mask * 0.30)[..., None])

    # ——— 4. 墨线上色 ———
    ink_col = np.asarray(INK, dtype=np.float64)
    ink_soft = np.asarray(INK_SOFT, dtype=np.float64)
    ink_layer = np.where(ink_mask[..., None] > 0.55, ink_col, ink_soft)
    out = mix(out, ink_layer, np.clip(ink_mask, 0, 1)[..., None] * 0.92)

    # ——— 5. 水彩质感 ———
    out = watercolor_wash(out, alpha, lum)

    # v8: 跟场景对齐色温。
    # 实机取色：场景（山/天空）均值 RGB(113,118,120)，士兵 RGB(134,137,138)。
    # 钢的灰蓝 NACoat(0.52,0.58,0.64) 蓝调偏重，贴进雪景里整座桥发青，
    # 跟旁边暖灰的士兵和建筑分家。这里把蓝通道往红通道收，
    # 并整体提亮一档，让桥站进画里而不是浮在上面。
    r, g, b = out[..., 0], out[..., 1], out[..., 2]
    b = b * 0.955 + r * 0.030
    g = g * 0.985 + r * 0.010
    out = np.stack([r, g, b], axis=2)
    # 提亮：向纸底靠一点，雪景的空气感
    out = out * 0.94 + 0.075

    # 墨线最后再压一遍，避免被水彩噪声冲淡
    out = mix(out, ink_layer, np.clip(ink_mask, 0, 1)[..., None] * 0.55)

    # ——— 6. 输出 ———
    out = np.clip(out, 0, 1)
    res = np.concatenate([out, alpha[..., None]], axis=2)
    Image.fromarray((res * 255).astype(np.uint8)).save(dst_path)
    print(f"restyled -> {dst_path}  ({w}x{h})")


if __name__ == "__main__":
    src = sys.argv[1] if len(sys.argv) > 1 else "bridge_PHOTOREAL_ORIGINAL.png"
    dst = sys.argv[2] if len(sys.argv) > 2 else "bridge_styled.png"
    strength = float(sys.argv[3]) if len(sys.argv) > 3 else 1.0
    restyle(src, dst, strength)
