"""v3.87 自检：南满铁路里程标贴图资产 + 关卡代码替换。

不依赖 Godot 运行，纯离线核对：
  (1) 贴图 railway_kilometer_post.png：尺寸 96x184、RGBA、四角透明（无残留白块）、
      存在不透明石面与深色墨线像素（说明有真实笔触，不是空壳）。
  (2) N918Level.cs：旧程序化白块（DrawRect 里程碑循环）已删除；
      改用 DrawTextureRect 采样 railway_kilometer_post.png；存在 _kilometerTex 字段与兜底。

返回 0 = 全部 PASS；非 0 = 有 FAIL（同时打印明细）。
"""

from pathlib import Path

import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
TEX = ROOT / "assets/art/props/railway_kilometer_post.png"
CS = ROOT / "scripts/N918Level.cs"


def check(label, cond, detail=""):
    mark = "PASS" if cond else "FAIL"
    print(f"[{mark}] {label}" + (f" -- {detail}" if detail else ""))
    return cond


ok = True

# ---------------------------------------------------------------- (1) 贴图
if not TEX.exists():
    ok &= check("贴图存在", False, str(TEX))
else:
    im = Image.open(TEX).convert("RGBA")
    a = np.asarray(im, dtype=np.float32) / 255.0
    alpha = a[..., 3]
    rgb = a[..., :3]
    ink = np.mean(rgb, axis=2)
    ok &= check("尺寸 96x184", im.size == (96, 184), str(im.size))
    ok &= check("模式 RGBA", im.mode == "RGBA")

    corner = alpha[[0, 0, -1, -1], [0, -1, 0, -1]]
    ok &= check("四角透明（无残留白块）", float(corner.max()) < 0.05,
                f"max corner alpha={corner.max():.3f}")

    opaq = float((alpha > 0.5).mean())
    ok &= check("存在不透明石面", 0.20 < opaq < 0.95, f"opaque ratio={opaq:.3f}")

    # 墨线：不透明区域里明显偏暗的像素（细断续墨线为 1px 描边，占比本就低）
    dark = (alpha > 0.5) & (ink < 0.40)
    ok &= check("存在深色墨线笔触", float(dark.mean()) > 0.008,
                f"ink ratio={dark.mean():.3f}")

    # 暖米灰而非冷白：不透明石面平均蓝通道低于红通道
    stone = rgb[alpha > 0.5]
    if len(stone):
        rmean, bmean = stone[..., 0].mean(), stone[..., 2].mean()
        ok &= check("暖米灰（R>B，非冷白）", rmean > bmean + 0.03,
                    f"R={rmean:.3f} B={bmean:.3f}")

# ---------------------------------------------------------------- (2) 代码
src = CS.read_text(encoding="utf-8")

# 旧里程碑循环特征：DrawRect(..., stone) 紧接 DrawRect(..., stoneShade) 的 24x46 白块
old_block = ("DrawRect(new Rect2(x, GroundY + 12f, 24f, 46f), stone)" in src) and (
    "for (int k = 0; k < 3; k++)" in src
    and "DrawLine(new Vector2(x + 6f, GroundY + 22f + k * 8f)" in src
)
ok &= check("旧程序化白块循环已删除", not old_block)

ok &= check("改用 DrawTextureRect 采样新贴图",
            'DrawTextureRect(_kilometerTex, new Rect2(x, GroundY + 12f, 24f, 46f), false)'
            in src)
ok &= check("引用 railway_kilometer_post.png",
            "res://assets/art/props/railway_kilometer_post.png" in src)
ok &= check("_kilometerTex 字段已声明", "private static Texture2D _kilometerTex;" in src)
ok &= check("贴图缺失有兜底（不整列消失）",
            "贴图缺失兜底" in src and "DrawRect(new Rect2(x, GroundY + 12f, 24f, 46f), stone)"
            in src and "else" in src)

print("\nRESULT:", "ALL PASS" if ok else "HAS FAIL")
raise SystemExit(0 if ok else 1)
