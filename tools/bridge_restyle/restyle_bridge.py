#!/usr/bin/env python
"""
restyle_bridge_v3.py —— 写实江桥 → InkStyle 手绘上色风（第 3 版管线）

前两版（给写实图换色）失败的教训，都有数据支撑：

    指标            房子(参照)   v1 成品     问题
    墨线占比         ~12%         2.3%        只有色块，没有线
    桁架构件段数      16 段       4 段        find_enclosed_holes 把镂空填实了
    彩度 p50         0.035       0.090       明显不是同一个游戏
    墨线 RGB      近黑中性      暖褐         色调不对

v3 的做法
---------
1. **几何一个像素都不填**。只做两件事：拆掉检修走道栏杆（"两个路"的另一半根因）、
   补上铆钉冲孔这种 1-2px 的破洞（大面积镂空是桁架的形状，动不得）。
2. **颜色按材质分**，而不是按亮度一刀切：
   锈褐主梁 / 暗板岩腹杆 / 中性钢桥面 / 暖灰混凝土 / 纸白雪。
   原图的钢构本来就是暖褐的，v1/v2 给它染成蓝灰，是"不像同一个游戏"的主因。
3. **平涂用硬边**。分档之后不再做高斯软过渡，色块边界干脆，才像"画"出来的。
4. **墨线** = alpha 内缩轮廓(2px) + 长走向暗缝。铆钉是圆点（走向短），
   缝是长线（走向长），用走向长度就能分开，噪点自然消失。
5. **雪只给极亮且几乎无彩的地方**（纸白透出来），桥墩上的阳光亮斑不算雪。

用法: python restyle_bridge_v3.py <输入.png> <输出.png>
"""

import os
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFilter



# ————————————————————————————————————————————————————————————
#  形态学工具（来自 v1，已验证有效；此处内联，仓库里单文件可跑）
# ————————————————————————————————————————————————————————————

def dilate(mask, size):
    """膨胀一个二值/灰度蒙版（用 MaxFilter 近似，比逐像素卷积快得多）。"""
    m = Image.fromarray((np.clip(mask, 0, 1) * 255).astype(np.uint8))
    m = m.filter(ImageFilter.MaxFilter(size))
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


# ————————————————————————————————————————————————————————————
#  InkStyle 色板
#  参照实测：房子墨线 (0.096,0.095,0.090) 近黑；亮度 p50 0.493；彩度 p50 0.035
# ————————————————————————————————————————————————————————————
INK = (0.092, 0.088, 0.082)        # 主墨线：近黑中性
PAPER = (0.905, 0.910, 0.906)      # 纸底 / 雪（比背景雪山略暗，免得跳出来）

# 桁架：锈褐主梁（暖，但克制——房子暖度 +0.063，尸体 +0.212，木头才是暖的）
CHORD_DK = (0.220, 0.186, 0.158)   # 锈褐·暗
CHORD_MID = (0.348, 0.298, 0.252)  # 锈褐·中
CHORD_LIT = (0.478, 0.418, 0.356)  # 锈褐·亮
CHORD_EDGE = (0.628, 0.562, 0.488)  # 受光边缘

# 腹杆：**中性深灰**，不是蓝灰。
# 背景雪山是冷白（中位 0.845），冷蓝灰的桁架在它面前会直接化掉。
# 前景实体（中位亮度）都在 0.25~0.35：榆树 0.309、烧毁房子 0.226、尸体 0.250。
WEB_DK = (0.118, 0.116, 0.112)     # 腹杆/节点板·暗
WEB_MID = (0.228, 0.224, 0.216)    # 腹杆·中
WEB_LIT = (0.362, 0.355, 0.340)    # 腹杆·亮
STEEL_LIT = (0.522, 0.512, 0.492)  # 亮钢

# 桥面（钢梁）
DECK_DK = (0.205, 0.204, 0.200)
DECK_MID = (0.348, 0.345, 0.338)
DECK_LIT = (0.518, 0.512, 0.498)

# 桥墩（混凝土，暖灰）。参考"烧毁的房子"：墙是一大片平涂，不画脏渍
CONC_DD = (0.248, 0.240, 0.226)
CONC_DK = (0.352, 0.340, 0.318)
CONC_MID = (0.478, 0.462, 0.436)
CONC_LIT = (0.640, 0.620, 0.586)

BURNT = (0.215, 0.192, 0.170)      # 烧毁的焦黑钢


# ————————————————————————————————————————————————————————————
#  基础工具
# ————————————————————————————————————————————————————————————

def _f(img):
    return np.asarray(img).astype(np.float64) / 255.0


def _gauss_rgb(arr, radius):
    return _f(Image.fromarray((np.clip(arr, 0, 1) * 255).astype(np.uint8))
              .filter(ImageFilter.GaussianBlur(radius)))


def gaussian(lum, radius):
    return _f(Image.fromarray((np.clip(lum, 0, 1) * 255).astype(np.uint8))
              .filter(ImageFilter.GaussianBlur(radius)))


def median_filter(lum, radius):
    size = radius * 2 + 1
    return _f(Image.fromarray((np.clip(lum, 0, 1) * 255).astype(np.uint8))
              .filter(ImageFilter.MedianFilter(size)))


def run_length(mask, axis):
    """每个前景像素所在连续段的长度。axis=0 横向，1 纵向。"""
    m = mask.T if axis == 0 else mask
    h, w = m.shape
    fwd = np.zeros_like(m, dtype=np.int32)
    run = np.zeros(h, dtype=np.int32)
    for x in range(w):
        run = np.where(m[:, x], run + 1, 0)
        fwd[:, x] = run
    bwd = np.zeros_like(m, dtype=np.int32)
    run = np.zeros(h, dtype=np.int32)
    for x in range(w - 1, -1, -1):
        run = np.where(m[:, x], run + 1, 0)
        bwd[:, x] = run
    out = fwd + bwd - 1
    return out.T if axis == 0 else out


def inner_contour_band(alpha, width):
    """alpha 的内缩轮廓带：墨线只画在实体上，不溢出到透明区。"""
    m = Image.fromarray((np.clip(alpha, 0, 1) * 255).astype(np.uint8))
    for _ in range(width - 1):
        m = m.filter(ImageFilter.MinFilter(3))
    return np.clip(alpha - _f(m), 0, 1)


def wobble_outline(alpha, seed=5):
    """**手绘感**的轮廓墨线带。

    手绘的线有三个特点，照片的 alpha 边缘一个都没有：

      1. 粗细不匀      —— 檐口最粗，木纹上最细
      2. 位置会抖      —— 线不是精确贴着轮廓，会往外飘一点再回来
      3. 起笔收笔有轻重

    这里准备三组候选带（收缩 1px / 原位 / 膨胀 1px，以及 1/2/3px 三种宽度），
    用一张低频噪声图在它们之间挑。挑到"膨胀"的那一档时线会往外飘，
    正好就是手绘那种飘出去再收回来的感觉。
    """
    h, w = alpha.shape
    rng = np.random.default_rng(seed)
    small = rng.normal(0, 1, (max(2, h // 26), max(2, w // 26)))
    noise = _f(Image.fromarray(((small * 46) + 128).clip(0, 255).astype(np.uint8))
               .resize((w, h), Image.BICUBIC))
    op = alpha > 0.5

    a_er = _f(Image.fromarray((np.clip(alpha, 0, 1) * 255).astype(np.uint8))
              .filter(ImageFilter.MinFilter(3)))
    a_di = _f(Image.fromarray((np.clip(alpha, 0, 1) * 255).astype(np.uint8))
              .filter(ImageFilter.MaxFilter(3)))

    bands = []
    for base in (a_er, alpha, a_di):
        for width in (2, 3, 4):
            bands.append(inner_contour_band(base, width) * op)

    n = len(bands)
    pick = np.clip(((noise + 0.5) * n).astype(int), 0, n - 1)
    out = np.zeros((h, w))
    for i, b in enumerate(bands):
        out[pick == i] = b[pick == i]
    return np.clip(out, 0, 1)


def wash_from_solid(vals, op, radius, far=1.0):
    """只在实体像素上做加权模糊，得到"附近实体色的平均值"。

    这一步替代了"先把透明区置 1.0 再模糊"的做法——后者会让每一个孔洞
    周围套上一圈白晕（桥墩上那些迷彩斑就是这么来的）。
    """
    w = op.astype(np.float64)
    num = gaussian(vals * w, radius)
    den = np.maximum(gaussian(w, radius), 1e-4)
    out = num / den
    return np.where(den < 0.03, far, out)


def classify_holes(alpha):
    """找出被实体完全包住的透明洞，并按"内切圆半径"分级。

    桁架的镂空是桥的形状本身，绝不能动。v5 的教训：开运算判"小孔"
    会把三角形的桁架格子也吃掉（row 120 从 16 段塌成 7 段）。
    这里改用内切圆半径：

        r1  半径 <= 1px（1-2px 的针孔）  -> 补掉
        r2  半径 <= 2px（3-5px 的铆钉孔）-> 补不得，但要把它的墨线环抹掉

    照片上每个铆钉位置都被抠成了一个小洞，墨线沿着洞口一圈，
    于是桁架上就排满了黑方框——那正是上一版"噪点"的来源。
    """
    h, w = alpha.shape
    trans = alpha <= 0.02
    img = Image.fromarray((np.where(trans, 0, 255)).astype(np.uint8), "L")

    seed = None
    for x in range(w):
        if trans[0, x]:
            seed = (x, 0)
            break
    if seed is None:
        for x in range(w):
            if trans[h - 1, x]:
                seed = (x, h - 1)
                break
    if seed is None:
        return np.zeros_like(trans), np.zeros_like(trans), np.zeros_like(trans)

    ImageDraw.floodfill(img, seed, 128)
    enclosed = (np.asarray(img) == 0)
    if not enclosed.any():
        return enclosed, np.zeros_like(trans), np.zeros_like(trans)

    e = Image.fromarray((enclosed * 255).astype(np.uint8), "L")
    core1 = e.filter(ImageFilter.MinFilter(3))
    core2 = core1.filter(ImageFilter.MinFilter(3))
    core1 = np.asarray(core1) > 0
    core2 = np.asarray(core2) > 0
    r1 = enclosed & (dilate(core1.astype(np.float64), 3) <= 0.5)
    r2 = enclosed & (dilate(core2.astype(np.float64), 5) <= 0.5)
    return enclosed, r1, r2


def level_shift(wash, mask, target_median, contrast=1.0):
    """把某个区的亮度整体平移到指定的中位数。

    写实渲染的曝光和手绘色板的亮度不是一回事。照片里的桁架中位亮度
    只有 0.30 出头，直接拿去分档就得到一堵黑墙。这里把每个区的中位数
    对齐到色板该有的位置，色阶才落在预期的档位上。
    """
    if not mask.any():
        return wash
    m = float(np.median(wash[mask]))
    out = wash.copy()
    out[mask] = (wash[mask] - m) * contrast + target_median
    return out


def quantize(lum, thresholds, colors):
    """亮度分档成平涂色。返回颜色图与档位图。"""
    idx = np.digitize(lum, thresholds)
    out = np.zeros(lum.shape + (3,), dtype=np.float64)
    for i, c in enumerate(colors):
        out[idx == i] = c
    return out, idx


def watercolor(out_rgb, seed=11):
    """极轻的水彩质感：低频明度漂移 + 纸纹颗粒。只加在填色上。"""
    rng = np.random.default_rng(seed)
    h, w, _ = out_rgb.shape
    small = rng.normal(0, 1, (max(2, h // 24), max(2, w // 24)))
    drift = _f(Image.fromarray(((small * 40) + 128).clip(0, 255).astype(np.uint8))
               .resize((w, h), Image.BICUBIC)) - 0.5
    out = out_rgb * (1.0 + (drift * 0.055)[..., None])
    grain = rng.normal(0, 1, (h, w))[..., None]
    return np.clip(out + grain * 0.009, 0, 1)


# ————————————————————————————————————————————————————————————
#  主流程
# ————————————————————————————————————————————————————————————

def restyle(src_path, dst_path, verbose=True):
    im = Image.open(src_path).convert("RGBA")
    arr = np.asarray(im).astype(np.float64) / 255.0
    rgb = arr[..., :3]
    alpha = arr[..., 3].copy()
    h, w = alpha.shape
    op = alpha > 0.5

    # ——— 0. 几何 ———
    # 只拆检修走道栏杆（"一个桥出现两条路"的另一半根因）。
    alpha, _ = remove_deck_railing(alpha)
    # 铆钉冲孔：1-2px 的针孔直接补掉；3-5px 的孔留着几何，但要把
    # 围着它的墨线环抹掉，否则桁架上会排满黑方框。
    _enc, hole_r1, hole_r2 = classify_holes(alpha)
    alpha[hole_r1] = 1.0
    op = alpha > 0.5
    if verbose:
        print(f"  补针孔 {int(hole_r1.sum())} px   铆钉孔去墨线环 {int(hole_r2.sum())} px")

    # ——— 0b. 抠掉烘焙进 alpha 的天空 ———
    #
    # 西端桥墩是**两片混凝土夹一个 X 形钢桁架**，格子后面本该透过去
    # 看到雪山。但写实渲染把天空烘焙进了贴图——那一带 alpha 完全不透明，
    # 镂空就这么没了，墩子成了一整块灰板子。
    #
    # 天空像素好认：又亮、又冷、又几乎没有彩度。实测掩膜正好落在
    # 交叉钢架的菱形格子里，钢构一点没伤到。
    lum_g = rgb[..., 0] * 0.299 + rgb[..., 1] * 0.587 + rgb[..., 2] * 0.114
    chroma_g = rgb.max(axis=2) - rgb.min(axis=2)
    x_frame = np.zeros((h, w), dtype=bool)
    x_frame[470:700, 140:245] = True
    sky = x_frame & (alpha > 0.5) & (lum_g > 0.72) & (chroma_g < 0.12)
    alpha[sky] = 0.0
    op = alpha > 0.5
    if verbose:
        print(f"  抠掉烘焙天空 {int(sky.sum())} px（西端墩的 X 形钢架镂空）")

    # ——— 1. 分区 ———
    # 桥面顶边随透视是斜的：y = 342 + 0.054x
    deck_top = deck_top_profile(w)
    rows = np.arange(h)[:, None]
    deck_m = (rows >= deck_top[None, :]) & (rows < deck_top[None, :] + 62)
    pier_m = rows >= deck_top[None, :] + 62
    truss_m = rows < deck_top[None, :]

    # ——— 2. 墨线 ———
    lum_raw = rgb[..., 0] * 0.299 + rgb[..., 1] * 0.587 + rgb[..., 2] * 0.114
    lum_bg = np.where(op, lum_raw, 1.0)
    med = median_filter(lum_bg, 4)
    seam0 = op & (lum_raw < med - 0.10) & (lum_raw < 0.44)
    # 走向长度：铆钉是圆点（横竖都短），缝是长线
    r_h = run_length(seam0, axis=0)
    r_v = run_length(seam0, axis=1)
    seam = seam0 & ((r_h >= 14) | (r_v >= 14))
    seam = seam & (lum_raw < 0.32)
    # 暗缝只画在钢结构上。混凝土墩子上有的是污渍和裂缝，
    # 画上去就是"迷彩斑"——房子那套画法里墙面本来就不画这种东西。
    seam = seam & (truss_m | deck_m)
    # 墩面不补暗缝。原图墩子上那些长水痕一勾就成了几道突兀的黑竖线，
    # 而 X 形钢架现在自己透空了、边缘自带轮廓墨线，不需要额外描。

    outline = wobble_outline(alpha)
    # 铆钉孔那圈墨线去掉：洞留着（它是照片里就有的），但不再勾边
    outline = np.clip(outline * (1.0 - (dilate(hole_r2.astype(np.float64), 3) > 0.5)), 0, 1)
    ink = np.clip(np.maximum(seam * 0.85, outline), 0, 1)
    ink[~op] = 0.0

    # ——— 3. 平涂用的"大结构" ———
    #
    # 只在实体上做加权模糊，孔洞自动继承附近构件的颜色；
    # 不再把透明区当白纸，否则每个孔洞周围都会套上一圈白晕。
    lum_f = rgb[..., 0] * 0.299 + rgb[..., 1] * 0.587 + rgb[..., 2] * 0.114
    warm_f = rgb[..., 0] - rgb[..., 2]
    chroma_f = rgb.max(axis=2) - rgb.min(axis=2)

    # 每个区用不同的画法：
    #
    #   桁架  成员只有 12-40px 宽，模糊太大就分不出构件了；
    #         逐构件的明暗是照片里最有用的信息，保留。
    #   桥面  一条横带，按结构分层（顶缘受光 / 梁腹 / 底缘阴影）。
    #   桥墩  一大片混凝土。手绘圆柱体就是几道**竖向**明暗带，
    #         不是照片里那些圆斑污渍——所以按列求均值，竖着分带。
    wash = np.empty((h, w))
    wash[truss_m] = wash_from_solid(lum_f, op, 6.0)[truss_m]

    # 桥墩：解析化的圆柱受光，**不看原图明暗**。
    #
    # 照片里墩面的明暗是污渍、水渍和阴影，不是结构——按它分档必然出
    # 迷彩色斑；改成逐列均值又会把两根斜撑展平成竖条，柱子像条形码。
    # 两条路都走不通，第三条：手绘的混凝土墩本来就是**几块大面**。
    #
    # 光从右上来（全关卡的影子都朝左下：房子的影子、枕木的暗边都在左），
    # 所以墩身左暗右亮；顶部靠近墩帽稍亮，底部沉进河冰转暗。
    # 墩子的位置靠 alpha 找，形状靠模型画。
    pier_cols = (pier_m & op).sum(axis=0)
    pier_h = np.maximum(h - (deck_top + 62), 1)
    pier_hit = pier_cols > (0.30 * pier_h)
    piers = []
    x = 0
    while x < w:
        if pier_hit[x]:
            x0 = x
            while x < w and pier_hit[x]:
                x += 1
            if x - x0 > 40:
                piers.append((x0, x))
        else:
            x += 1
    for (x0, x1) in piers:
        span = max(x1 - x0, 1)
        t = np.clip((np.arange(x0, x1) - x0) / span, 0, 1)
        base = 0.352 + 0.258 * t                    # 左暗 → 右亮
        rel = rows - (deck_top[None, x0:x1] + 62)
        # 顶部不加反光带：它与竖向分档交叉会在墩肩切出一个 L 形的阶梯，
        # 像贴图拼错。墩顶的受光交给上面的墩帽（deck 区）去管。
        vert = np.zeros((h, x1 - x0))
        vert = vert - np.where(rows > h - 34, 0.075, 0.0)   # 沉进河冰
        wash[:, x0:x1] = base[None, :] + vert
    wash[pier_m] = np.where(op, wash, wash)[pier_m]

    # 桥面：按结构分层
    deck_rel = rows - deck_top[None, :]
    deck_lvl = np.where(deck_rel < 2, 0.62,
                np.where(deck_rel < 14, 0.44,
                np.where(deck_rel < 38, 0.35, 0.27)))
    wash[deck_m] = deck_lvl[deck_m]
    wash = np.where(op, wash, 1.0)

    # 曝光归一：把每个区的中位亮度对齐到色板该有的位置。
    #
    # 桁架原来对齐到 0.42，成品中位 0.431、最亮的 10% 到 0.717——而背景雪山
    # 是 0.845 的冷白，桁架最亮的那一批几乎贴着背景，整座桥在雪原前"化"了。
    # 前景实体不该这么亮：榆树 0.309、烧毁房子 0.226、尸体 0.250。
    # 对齐到 0.38，成品桁架中位落在 0.29~0.30（榆树 0.309、烧毁房子 0.226），
    # 既是一道能在雪山前立住的深色剪影，又不会闷成一团黑。
    # 桥墩再压一点对比，否则竖带会多到像条形码。
    truss_op = truss_m & op
    wash = level_shift(wash, truss_op, 0.42)
    # 桥墩的亮度已经是解析模型画好的，不再做 level_shift ——
    # 一压对比，三个大面就并成一块，墩子又会变成一片平。
    pier_op = pier_m & op

    warm_t = wash_from_solid(warm_f, op, 3.0, far=0.0)
    # 暖色 = 锈蚀的钢。桁架的主梁是褐的、腹杆是灰的，一眼能分。
    # 桥墩不能这么分：混凝土上的锈斑也会变褐，判成钢就是一身麻子。
    brown_m = truss_m & op & (warm_t > 0.045)
    # 钢构件自己那一份局部明暗（柱面/梁面的受光与背光）
    steel_local = wash_from_solid(lum_f, op, 2.5)

    # ——— 4. 分档平涂（硬边，不再软过渡） ———
    web_col, _ = quantize(wash, [0.20, 0.34, 0.48, 0.62],
                           [WEB_DK, WEB_MID, WEB_LIT, STEEL_LIT])
    chord_col, _ = quantize(wash, [0.24, 0.40, 0.56, 0.70],
                             [CHORD_DK, CHORD_MID, CHORD_LIT, CHORD_EDGE])
    deck_col, _ = quantize(wash, [0.30, 0.50],
                            [DECK_DK, DECK_MID, DECK_LIT])
    conc_col, _ = quantize(wash, [0.44, 0.56],
                            [CONC_DD, CONC_MID, CONC_LIT])

    # 钢构件按自己的局部明暗上色（桁架用桁架的曝光，墩子里的钢用局部的）
    steel_local = level_shift(steel_local, brown_m, 0.40)
    out_rgb = np.where(brown_m[..., None], chord_col,
              np.where(truss_m[..., None], web_col,
              np.where(deck_m[..., None], deck_col, conc_col)))

    # ——— 5. 烧毁段：焦黑的乱钢 ———
    xs = np.arange(w)
    burn_col_m = (xs >= 800) & (xs <= 1120)
    # 只烧桁架。烧毁蒙版一旦漏到桥墩上，墩子就被盖上一块黑斑。
    burnt = burn_col_m[None, :] & truss_m & op & (lum_f < 0.26)
    burnt = np.asarray(Image.fromarray((burnt * 255).astype(np.uint8))
                       .filter(ImageFilter.GaussianBlur(1.5))) / 255.0
    burnt = np.clip(burnt * 0.85, 0, 1)
    out_rgb = out_rgb * (1 - burnt[..., None]) + np.array(BURNT) * burnt[..., None]
    # 烧毁段的墨线减淡，免得糊成一团黑
    # 烧毁段的墨线减淡，但**不能减到没有**——减太狠整段就糊成一块黑饼，
    # 看不出扭曲的钢架骨架。留一半，骨架才认得出来。
    ink = np.where(burn_col_m[None, :] & op, ink * 0.55, ink)

    # ——— 6. 雪：纸白透出来 ——
    #
    # 雪积在**朝上**的面，不会挂在斜杆的侧面上。所以除了"极亮且几乎无彩"，
    # 还得是所在构件的上沿（上方 3 行内能看到天）。
    # 少了这一条，远端桁架上会糊出一片一片的白斑，像泼了油漆。
    # 雪只留在桥面上。
    #
    # 桁架上那一点积雪在深色钢构上像一排白补丁——贴图里的雪是照片级的，
    # 一小块一整块；手绘不会在每根杆的受光尖上贴一块白。
    # 朝上的面的判断（上方 3 行内能看到天）继续保留。
    op_up = np.zeros_like(op)
    op_up[3:] = op[:-3]
    near_top = op & ~op_up
    snow_raw = deck_m & op & (wash > 0.74) & (chroma_f < 0.06) & near_top
    snow_band = (rows >= deck_top[None, :] - 7) & (rows < deck_top[None, :] + 4)
    snow_raw = snow_raw | (snow_band & op & (wash > 0.66) & (chroma_f < 0.08))
    snow = np.asarray(Image.fromarray((snow_raw * 255).astype(np.uint8))
                      .filter(ImageFilter.GaussianBlur(1.4))) / 255.0
    snow = np.clip(snow * op, 0, 1)
    out_rgb = out_rgb * (1 - snow[..., None]) + np.array(PAPER) * snow[..., None]

    # ——— 7. 墨线上色 ———
    out_rgb = out_rgb * (1 - ink[..., None]) + np.array(INK) * ink[..., None]

    # ——— 8. 水彩质感 ———
    out_rgb = watercolor(out_rgb)
    out_rgb = np.where(ink[..., None] > 0.5, np.array(INK), out_rgb)

    # ——— 9. 输出 ———
    # 透明区 RGB 写纸白，免得 Godot 的 fix_alpha_border 把暗色渗到边上
    out_rgb = np.where((alpha <= 0.02)[..., None], np.array(PAPER), out_rgb)

    out = np.dstack([np.clip(out_rgb, 0, 1), alpha[..., None]])
    Image.fromarray((out * 255).round().astype(np.uint8), "RGBA").save(dst_path)

    # ——— 10. 自检 ———
    px = out_rgb[op]
    lum = px @ np.array([0.299, 0.587, 0.114])
    chroma = px.max(axis=1) - px.min(axis=1)
    if verbose:
        print(f"  墨线占比   {ink[op].mean():.3f}")
        print(f"  留白(>0.80) {(lum > 0.80).mean():.3f}    房子 0.169")
        print(f"  彩度 p50   {np.percentile(chroma, 50):.3f}    房子 0.035")
        print(f"  亮度 p50   {np.percentile(lum, 50):.3f}    房子 0.493")
        print(f"  雪占比     {snow[op].mean():.3f}")
    return out_rgb, alpha, ink


if __name__ == "__main__":
    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    src = sys.argv[1] if len(sys.argv) > 1 else os.path.join(root, "bridge_PHOTOREAL_ORIGINAL.png")
    dst = sys.argv[2] if len(sys.argv) > 2 else os.path.join(root, "bridge_styled_v5.png")
    print(f"restyle v3: {src} -> {dst}")
    restyle(src, dst)
    print("done")
