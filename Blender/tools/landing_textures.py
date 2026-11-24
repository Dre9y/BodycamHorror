"""2D textures for the PvE landing (Assets/Editor/KeyPropsBuilder.cs), run with the system Python (Pillow + numpy):

    python Blender/tools/landing_textures.py

Writes
  Assets/Generated/Textures/Graffiti/Graffiti_Num<N>.png   a flat number sprayed on its door (1..4), for decals
  Assets/Generated/Textures/Props/KeyTag_<N>.png            a key's plastic tag with the flat number in marker (1..4)
  Assets/Generated/Textures/Props/Door_NoNumber_BaseColor.png
                                                            the flat door's paint with its printed "37" painted out
"""
import os
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

from decor_textures import F, fbm, arr, to_img, spray, over, wall_break, text_mask, save_graffiti, PROP_TEX, PROJECT

# ------------------------------------------------------------------ door numbers, sprayed

NUM_STYLE = {
    # number: paint colour, tilt, font, a slash under it
    1: ((0.9, 0.9, 0.86), -6, "impact.ttf", True),
    2: ((0.9, 0.9, 0.86), 4, "impact.ttf", False),
    3: ((0.62, 0.05, 0.03), -3, "segoeprb.ttf", True),
    4: ((0.9, 0.9, 0.86), 7, "impact.ttf", False),
}

def g_number(n):
    """A big hasty digit on a flat door: one pass of the can, a run or two, a slash under some."""
    col, rot, font, slash = NUM_STYLE[n]
    S = (768, 768); seed = 700 + n
    m = text_mask(S, str(n), F(font, 560), (S[0] // 2, S[1] // 2 - 30), rot=rot)
    if slash:
        d = ImageDraw.Draw(m)
        d.line((170, 660, 600, 625), fill=255, width=26)
    m = m.filter(ImageFilter.MaxFilter(5))
    L = spray(S, m, col, seed, soft=2.6, over=0.28, drips=9, drip_len=(30, 150), drip_w=(5, 9))
    L[..., :3] *= (0.82 + 0.25 * fbm(S[1], S[0], 8, seed))[..., None]
    wall_break(L, seed + 1, 0.22)
    save_graffiti(f"Num{n}", L)

# ------------------------------------------------------------------ key tags

TAG_COLOUR = {1: (0.78, 0.22, 0.12), 2: (0.16, 0.38, 0.62), 3: (0.86, 0.62, 0.12), 4: (0.22, 0.5, 0.26)}

def key_tag(n):
    """A worn plastic key tag: faded colour, scuffs, a hole at the top for the ring, the number in black marker."""
    W, H = 256, 384; seed = 900 + n
    base = np.array(TAG_COLOUR[n], float)
    noise = fbm(H, W, 6, seed)
    img = base[None, None, :] * (0.78 + 0.3 * noise[..., None])
    # scuffs: pale scratches
    sc = Image.new("L", (W, H), 0); d = ImageDraw.Draw(sc); rng = np.random.default_rng(seed)
    for _ in range(22):
        x, y = rng.uniform(0, W), rng.uniform(0, H); L = rng.uniform(8, 40); a = rng.uniform(0, np.pi)
        d.line((x, y, x + L * np.cos(a), y + L * np.sin(a)), fill=int(rng.uniform(60, 160)), width=1)
    img = img + arr(sc)[..., None] * 0.35
    # grime towards the edges
    yy, xx = np.mgrid[0:H, 0:W]
    edge = np.minimum(np.minimum(xx, W - 1 - xx), np.minimum(yy, H - 1 - yy)) / 40.0
    img *= (0.7 + 0.3 * np.clip(edge, 0, 1))[..., None]
    out = to_img(img)
    d = ImageDraw.Draw(out)
    # the ring hole (dark), and the number in marker, slightly crooked
    d.ellipse((W / 2 - 22, 26, W / 2 + 22, 70), fill=(18, 16, 14))
    mk = text_mask((W, H), str(n), F("segoeprb.ttf", 230), (W // 2 + 4, H // 2 + 40), rot=-5)
    mk = mk.filter(ImageFilter.GaussianBlur(1.2))
    ink = np.array([0.06, 0.05, 0.05])
    a = np.clip(arr(mk) * 1.3, 0, 1)[..., None] * (0.85 + 0.15 * fbm(H, W, 10, seed + 3))[..., None]
    o = arr(out) * (1 - a) + ink[None, None, :] * a
    sm = text_mask((W, H), "кв.", F("segoeprb.ttf", 54), (W // 2 - 30, 112), rot=-4).filter(ImageFilter.GaussianBlur(0.8))
    a2 = np.clip(arr(sm) * 1.2, 0, 1)[..., None]
    o = o * (1 - a2) + ink[None, None, :] * a2
    to_img(o).save(os.path.join(PROP_TEX, f"KeyTag_{n}.png"))

# ------------------------------------------------------------------ the flat door without its number

def door_without_number():
    """Door_BaseColor carries a printed "37" on the leaf; patch it over with the paint just above it, feathered."""
    src = Image.open(os.path.join(PROP_TEX, "Door_BaseColor.png")).convert("RGB")
    a = arr(src)
    x0, x1, y0, y1, dy = 186, 274, 1882, 1944, -62   # the digits (UV is upside down there), and where clean paint is
    patch = a[y0 + dy:y1 + dy, x0:x1].copy()
    h, w = patch.shape[:2]
    yy, xx = np.mgrid[0:h, 0:w]
    feather = np.clip(np.minimum(np.minimum(xx, w - 1 - xx), np.minimum(yy, h - 1 - yy)) / 10.0, 0, 1)[..., None]
    a[y0:y1, x0:x1] = a[y0:y1, x0:x1] * (1 - feather) + patch * feather
    to_img(a).save(os.path.join(PROP_TEX, "Door_NoNumber_BaseColor.png"))

# ------------------------------------------------------------------ the blood arrow on the landing floor

def g_blood_arrow():
    """An arrow smeared on the floor with bloody fingers, pointing up the picture (to the iron door): a pool where the
    hand started, a shaft dragged out of it thinning as the blood ran out, the head in two quick strokes; finger
    streaks along every stroke, drops flicked round it."""
    W, H = 1024, 2048; rng = np.random.default_rng(1313)
    yy, xx = np.mgrid[0:H, 0:W].astype(np.float64)
    cover = np.zeros((H, W))   # how much blood (0..1+: thick)

    def stroke(a, b, w0, w1, load0, load1, seed):
        r = np.random.default_rng(seed)
        a = np.array(a, float); b = np.array(b, float); v = b - a; L = np.linalg.norm(v); u = v / L; n = np.array([-u[1], u[0]])
        px, py = xx - a[0], yy - a[1]
        t = (px * u[0] + py * u[1]) / L
        d = px * n[0] + py * n[1]
        d = d + np.sin(t * r.uniform(5, 9) + r.uniform(0, 6)) * r.uniform(6, 14)   # the hand wavers
        w = w0 + (w1 - w0) * np.clip(t, 0, 1)
        inside = np.clip((w - np.abs(d)) / 6.0, 0, 1) * np.clip(t * L / 18.0 + 1, 0, 1) * np.clip((1 - t) * L / 30.0 + 1, 0, 1)
        # four fingers: streaks along the stroke, the gaps between them thin
        dn = d / w + 0.25 * np.sin(t * r.uniform(3, 6) + r.uniform(0, 6))   # fingers drift apart and together
        fingers = 0.5 + 0.5 * np.cos(dn * np.pi * 3.6 + r.uniform(0, 6))
        fibres = sum(np.sin(d * f + r.uniform(0, 6)) for f in r.uniform(0.15, 0.6, 5)) / 5
        clot = fbm(H, W, 22, seed + 50)
        load = load0 + (load1 - load0) * np.clip(t, 0, 1)
        # the blood gives out in skips near the end of a stroke
        skip = np.clip(load * 1.4 + 0.25 * fbm(H, W, 40, seed)[..., None].squeeze() - 0.15, 0, 1)
        amount = inside * load * (0.7 + 0.22 * fingers + 0.12 * fibres + 0.35 * (clot - 0.5)) * skip
        np.maximum(cover, amount, out=cover)

    tip = (W * 0.5, 180)
    stroke((W * 0.52, 1880), (W * 0.49, 380), 84, 52, 1.3, 0.6, 1)      # the shaft, out of the pool
    stroke(tip, (W * 0.17, 760), 58, 40, 1.15, 0.45, 2)                   # head, left
    stroke(tip, (W * 0.84, 730), 60, 38, 1.1, 0.4, 3)                     # head, right
    stroke((W * 0.49, 560), tip, 50, 40, 1.0, 0.85, 4)                    # the shaft run into the tip

    # the pool where the hand was pressed down first, and drops flicked about
    blob = Image.new("L", (W, H), 0); dd = ImageDraw.Draw(blob)
    cx, cy = W * 0.52, 1880
    dd.ellipse((cx - 95, cy - 70, cx + 105, cy + 95), fill=255)
    pool = arr(blob.filter(ImageFilter.GaussianBlur(40)))
    pool = np.clip((pool + (fbm(H, W, 30, 1316) - 0.5) * 0.7 - 0.35) * 3.0, 0, 1.4)   # a ragged spread, not a disc
    # drops shaken off the hand, along the strokes and near the pool
    drops = Image.new("L", (W, H), 0); dr = ImageDraw.Draw(drops)
    for _ in range(40):
        k = rng.random()
        if k < 0.4: x, y = cx + rng.normal(0, 120), cy + rng.normal(-40, 90)
        else: x, y = W * 0.5 + rng.normal(0, 110), rng.uniform(150, 1800)
        r = rng.uniform(2.5, 10) * (1.5 if k < 0.4 else 1)
        dr.ellipse((x - r, y - r * rng.uniform(0.7, 1.2), x + r, y + r), fill=255)
    pool = np.maximum(pool, arr(drops.filter(ImageFilter.GaussianBlur(1.2))) * 1.1)
    cover = np.maximum(cover, pool)

    # colour: thick blood near black-red, thin smears dried to brown-red; ragged dried edges
    edge = fbm(H, W, 60, 1314)
    a = np.clip(cover * 1.25 - 0.08 + (edge - 0.5) * 0.25, 0, 1)
    thick = np.clip(cover - 0.55, 0, 1) / 0.45
    thin_col = np.array([0.36, 0.06, 0.035]); thick_col = np.array([0.17, 0.012, 0.01])
    rgb = thin_col[None, None, :] * (1 - thick[..., None]) + thick_col[None, None, :] * thick[..., None]
    rgb *= (0.85 + 0.3 * fbm(H, W, 14, 1315))[..., None]
    save_graffiti("BloodArrow", np.dstack((rgb, a * 0.95)))

if __name__ == "__main__":
    import sys
    if "arrow" in sys.argv:
        g_blood_arrow(); print("blood arrow"); sys.exit()
    for n in (1, 2, 3, 4):
        g_number(n); key_tag(n); print("number", n)
    door_without_number(); print("door without number")
    g_blood_arrow(); print("blood arrow")
