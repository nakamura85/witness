#!/usr/bin/env python3
"""Key light paper background of generated assets to alpha using numpy flood-fill.
Produces RGBA PNGs in previews/ with bbox crop + feathered edges.
"""
from PIL import Image
import numpy as np
import os

PREV = "tools/bridge_restyle/previews"

def neighbor_union(mask):
    out = mask.copy()
    for dy in (-1, 0, 1):
        for dx in (-1, 0, 1):
            if dy == 0 and dx == 0:
                continue
            out |= np.roll(np.roll(mask, dy, axis=0), dx, axis=1)
    return out

def key_bg(name, lum_thresh_below=22, sat_thresh=30, feather=True):
    im = Image.open(f"{PREV}/{name}.png").convert("RGB")
    a = np.asarray(im).astype(np.int16)
    h, w, _ = a.shape
    R, G, B = a[:, :, 0], a[:, :, 1], a[:, :, 2]
    lum = (R + G + B) / 3.0
    sat = R - B
    # background reference = mean of corners
    corners = np.concatenate([a[:20,:20].reshape(-1,3), a[:20,-20:].reshape(-1,3),
                              a[-20:,:20].reshape(-1,3), a[-20:,-20:].reshape(-1,3)])
    bg_lum = corners.mean()
    # candidate background pixels: bright & low-warmth
    cand = (lum > bg_lum - lum_thresh_below) & (sat < sat_thresh)
    # seed = candidate pixels on the border
    seed = np.zeros((h, w), dtype=bool)
    seed[0, :] = cand[0, :]; seed[-1, :] = cand[-1, :]
    seed[:, 0] = cand[:, 0]; seed[:, -1] = cand[:, -1]
    # flood fill
    bgmask = seed.copy()
    frontier = seed.copy()
    it = 0
    while frontier.any():
        dil = neighbor_union(frontier)
        new = dil & cand & ~bgmask
        bgmask |= new
        frontier = new
        it += 1
        if it > 3000:
            break
    alpha = np.where(bgmask, 0, 255).astype(np.uint8)
    if feather:
        af = alpha.astype(np.float32)
        for _ in range(1):
            blur = np.zeros_like(af)
            cnt = np.zeros_like(af)
            for dy in (-1, 0, 1):
                for dx in (-1, 0, 1):
                    blur += np.roll(np.roll(af, dy, 0), dx, 1)
                    cnt += 1
            af = blur / cnt
        alpha = np.clip(af, 0, 255).astype(np.uint8)
    ys, xs = np.where(alpha > 8)
    x0, x1 = max(0, xs.min()-3), min(w-1, xs.max()+3)
    y0, y1 = max(0, ys.min()-3), min(h-1, ys.max()+3)
    out = np.dstack([a.astype(np.uint8), alpha])[y0:y1+1, x0:x1+1]
    Image.fromarray(out, "RGBA").save(f"{PREV}/_keyed_{name}.png")
    print(f"{name}: bg_lum={bg_lum:.1f} iters={it} crop=({x0},{y0},{x1},{y1}) size={x1-x0+1}x{y1-y0+1} alpha>8 cover={(alpha>8).mean():.3f}")

for n in ["wall_niche", "drawer_box"]:
    key_bg(n)
