#!/usr/bin/env python3
"""Objective checks for the restyled bridge PNG (no reliance on vision model)."""
import sys
import numpy as np
from PIL import Image

INK = np.array([0.14, 0.10, 0.08])  # InkStyle.InkColor


def report(path):
    im = Image.open(path).convert("RGBA")
    a = np.asarray(im).astype(np.float64) / 255.0
    rgb = a[..., :3]
    alpha = a[..., 3]
    h, w = a.shape[:2]
    opaque = alpha > 0.5
    n_op = int(opaque.sum())
    print(f"\n=== {path}  ({w}x{h}) ===")
    print(f"opaque px: {n_op} ({100*n_op/(h*w):.1f}%)  transparent: {h*w-n_op}")

    # tone count (quantize to 5 bits/channel to count distinct flat tones)
    q = (rgb[opaque] * 31).round().astype(int)
    keys = q[:, 0] * 1024 + q[:, 1] * 32 + q[:, 2]
    print(f"distinct flat tones (5-bit): {len(np.unique(keys))}")

    # warm-brown ink pixels: close to INK and dark
    d = np.sqrt(((rgb - INK) ** 2).sum(axis=-1))
    inkpx = int(((d < 0.06) & opaque).sum())
    print(f"warm-brown ink pixels (near InkColor, opaque): {inkpx} ({100*inkpx/max(n_op,1):.2f}% of opaque)")

    # edge smoothness: mean |grad| of alpha boundary (lower => smoother AA edge)
    gx = np.abs(alpha[1:, :] - alpha[:-1, :]).sum() + np.abs(alpha[:, 1:] - alpha[:, :-1]).sum()
    print(f"alpha edge roughness (sum |grad|): {gx:.0f}  (lower = smoother)")

    return a


if __name__ == "__main__":
    report("bridge_PHOTOREAL_ORIGINAL.png")
    for p in sys.argv[1:]:
        report(p)
