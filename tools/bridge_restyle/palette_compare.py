#!/usr/bin/env python3
"""Compare tonal/saturation distribution of bridge variants vs canonical game art
(house_intact_near.png, corpse_ija_prone.png) so the bridge can be tuned to land
in the SAME distribution instead of by eye."""
import sys
import numpy as np
from PIL import Image


def stats(path):
    im = Image.open(path).convert("RGBA")
    a = np.asarray(im).astype(np.float64) / 255.0
    rgb = a[..., :3]
    alpha = a[..., 3]
    op = alpha > 0.5
    px = rgb[op]
    n = len(px)
    # HSV
    mx = px.max(axis=1); mn = px.min(axis=1); d = mx - mn + 1e-9
    s = np.where(mx > 1e-6, d / (mx + 1e-9), 0.0)
    v = mx
    # dominant quantized colors
    q = (px * 31).round().astype(int)
    keys = q[:, 0] * 1024 + q[:, 1] * 32 + q[:, 2]
    uniq, counts = np.unique(keys, return_counts=True)
    order = np.argsort(-counts)[:8]
    pal = []
    for idx in order:
        k = uniq[idx]
        r = int(k // 1024); g = int((k % 1024) // 32); b = int(k % 32)
        pal.append(f"({r*8:3d},{g*8:3d},{b*8:3d}):{100*counts[idx]/n:4.1f}%")
    return dict(
        n=n,
        mean=np.round(px.mean(axis=0), 3),
        med_s=round(float(np.median(s)), 3),
        med_v=round(float(np.median(v)), 3),
        pal=pal,
    )


def show(label, s):
    print(f"\n[{label}]  n={s['n']}")
    print(f"  mean RGB     : {s['mean']}")
    print(f"  median sat   : {s['med_s']}   median value: {s['med_v']}")
    print(f"  top tones    : " + "  ".join(s['pal']))


if __name__ == "__main__":
    targets = {
        "HOUSE (approved)": "../../assets/art/props/house_intact_near.png",
        "CORPSE (approved)": "../../assets/art/sprites/corpse_ija_prone.png",
    }
    for lbl, p in targets.items():
        try:
            show(lbl, stats(p))
        except Exception as e:
            print(lbl, "ERR", e)
    for p in sys.argv[1:]:
        try:
            show("BRIDGE:" + p, stats(p))
        except Exception as e:
            print(p, "ERR", e)
