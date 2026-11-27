"""Surface textures for the Level scene's flat (the part before the padlock): tileable wallpapers and parquet, and the
overlays laid on top of them - blood (a pool, a drag, drops, a wall splatter, a handprint), torn wallpaper, ceiling
water stains, a broken ceiling. Plain numpy, run inside Blender over the bridge (it only needs bpy to write PNGs):

    exec(open(r"E:\\Unity\\Projects\\BodycamHorror\\Blender\\tools\\level_polish_textures.py").read())
    make_all()

Writes to Assets/Generated/Textures/Level: <Name>_BaseColor.png (RGBA, alpha = coverage for the overlays),
<Name>_Normal.png (OpenGL, +Y up), <Name>_MetallicSmoothness.png (R metal, A smoothness).
Tileable maps repeat at the sizes in TILE_M (metres), which LevelPolish.cs uses for the UV scale.
"""
import bpy, math, os
import numpy as np

PROJECT = r"E:\Unity\Projects\BodycamHorror"
LVL_TEX = os.path.join(PROJECT, "Assets", "Generated", "Textures", "Level")
TILE_M = {"Wallpaper_Floral": 1.06, "Wallpaper_Stripe": 1.06, "Parquet": 1.2}

# ------------------------------------------------------------------ helpers

def tnoise(h, w, cy, cx, seed):
    """Value noise that wraps at the image edges (cy x cx lattice cells)."""
    rng = np.random.default_rng(seed); g = rng.random((cy, cx))
    y = np.arange(h) * cy / h; x = np.arange(w) * cx / w
    yi = y.astype(int); xi = x.astype(int); fy = y - yi; fx = x - xi
    fy = fy * fy * (3 - 2 * fy); fx = fx * fx * (3 - 2 * fx)
    y0, y1 = yi % cy, (yi + 1) % cy; x0, x1 = xi % cx, (xi + 1) % cx
    a = g[y0][:, x0]; b = g[y0][:, x1]; c = g[y1][:, x0]; d = g[y1][:, x1]
    top = a + (b - a) * fx[None, :]; bot = c + (d - c) * fx[None, :]
    return top + (bot - top) * fy[:, None]

def tfbm(h, w, cells, seed, octaves=5, aspect=1.0):
    out = np.zeros((h, w)); amp = 1.0; tot = 0.0
    for o in range(octaves):
        cy = max(1, int(cells * 2 ** o)); cx = max(1, int(round(cy * aspect * w / h)))
        out += tnoise(h, w, cy, cx, seed + o * 31) * amp; tot += amp; amp *= 0.5
    return out / tot

def sstep(a, b, x):
    t = np.clip((x - a) / (b - a), 0, 1); return t * t * (3 - 2 * t)

def blend(img, mask, col):
    m = np.clip(mask, 0, 1)[..., None]; img[:] = img * (1 - m) + np.asarray(col, float)[None, None, :] * m

def wrapd(coord, c, size):
    return (coord - c + size / 2) % size - size / 2

def normal_from_height(hgt, strength, wrap=True):
    if wrap:
        dx = (np.roll(hgt, -1, 1) - np.roll(hgt, 1, 1)) * 0.5
        dy = (np.roll(hgt, 1, 0) - np.roll(hgt, -1, 0)) * 0.5   # row-1 is up (+v)
    else:
        gy, gx = np.gradient(hgt); dx = gx; dy = -gy
    n = np.dstack([-dx * strength, -dy * strength, np.ones_like(hgt)])
    n /= np.linalg.norm(n, axis=2, keepdims=True)
    return n * 0.5 + 0.5

def save(name, rgb, alpha=None, nonc=False):
    h, w = rgb.shape[:2]
    px = np.ones((h, w, 4), np.float32); px[..., :3] = np.clip(rgb, 0, 1)
    if alpha is not None: px[..., 3] = np.clip(alpha, 0, 1)
    px = px[::-1]
    if name in bpy.data.images: bpy.data.images.remove(bpy.data.images[name])
    im = bpy.data.images.new(name, w, h, alpha=True, float_buffer=False)
    if nonc: im.colorspace_settings.name = 'Non-Color'
    im.pixels.foreach_set(px.ravel())
    os.makedirs(LVL_TEX, exist_ok=True)
    im.filepath_raw = os.path.join(LVL_TEX, name + ".png"); im.file_format = 'PNG'
    im.save(); bpy.data.images.remove(im)

def save_set(name, base, alpha, height, strength, smooth, metal=None, wrap=True):
    save(name + "_BaseColor", base, alpha)
    save(name + "_Normal", normal_from_height(height, strength, wrap), nonc=True)
    ms = np.zeros(base.shape); ms[..., :] = 0 if metal is None else metal[..., None]
    save(name + "_MetallicSmoothness", ms, smooth, nonc=True)

def grid(h, w):
    yy, xx = np.mgrid[0:h, 0:w].astype(float); return yy, xx

# ------------------------------------------------------------------ wallpapers

def _flower(img, hgt, yy, xx, cx, cy, R, rng, cols, W, H, petals=5):
    dx = wrapd(xx, cx, W); dy = wrapd(yy, cy, H)
    r = np.sqrt(dx * dx + dy * dy) + 1e-6; th = np.arctan2(dy, dx) + rng.uniform(0, 6.28)
    shape = R * (0.55 + 0.45 * np.abs(np.cos(th * petals / 2)))
    m = sstep(1.0, 0.85, r / shape)
    blend(img, m * 0.9, cols[0]); blend(img, sstep(0.55, 0.4, r / shape) * 0.8, cols[1])
    blend(img, sstep(0.2, 0.1, r / R), cols[2]); hgt += m * 0.4

def _leaf(img, hgt, yy, xx, cx, cy, L, ang, col, W, H):
    dx = wrapd(xx, cx, W); dy = wrapd(yy, cy, H)
    u = dx * math.cos(ang) + dy * math.sin(ang); v = -dx * math.sin(ang) + dy * math.cos(ang)
    t = np.clip(u / L, 0, 1); half = 0.32 * L * np.sin(np.pi * t)
    m = (u > 0) * (u < L) * sstep(1.0, 0.8, np.abs(v) / (half + 1e-3))
    blend(img, m * 0.85, col); hgt += m * 0.3
    vein = (u > 0) * (u < L) * sstep(1.6, 0.4, np.abs(v)); blend(img, vein * 0.35, np.asarray(col) * 0.6)

def _age_wall(img, hgt, H, W, seed, yellow=0.25, stains=0.35):
    """Yellowing, foxing, water runs and a strip seam every half tile."""
    yy, xx = grid(H, W)
    f = tfbm(H, W, 3, seed, 5); f2 = tfbm(H, W, 10, seed + 5, 4)
    img *= (0.86 + 0.24 * f)[..., None]
    blend(img, sstep(0.45, 0.75, f) * yellow, (0.55, 0.45, 0.28))
    fox = sstep(0.68, 0.74, tfbm(H, W, 24, seed + 9, 3)) * sstep(0.4, 0.6, f)
    blend(img, fox * 0.45, (0.42, 0.3, 0.16))
    runs = tfbm(H, W, 2, seed + 13, 3, aspect=8.0)   # tall streaks
    blend(img, sstep(0.55, 0.8, runs) * stains * (0.5 + 0.5 * f2), (0.36, 0.3, 0.2))
    for sx in (0, W // 2):   # the joint between two strips, slightly overlapped and lifted
        d = np.abs(wrapd(xx, sx, W))
        blend(img, sstep(3.5, 0.5, d) * 0.5, (0.25, 0.2, 0.14))
        hgt += sstep(6, 1, d) * 0.6 * (0.5 + 0.5 * tnoise(H, W, 30, 1, seed + sx))
    hgt += f2 * 0.5 + tfbm(H, W, 64, seed + 21, 3) * 0.6   # paper texture

def wallpaper_floral(S=1024, seed=3):
    """A cream Soviet paper wallpaper: a half-drop of small brown-rose bouquets between faint olive stripes."""
    H = W = S; yy, xx = grid(H, W); rng = np.random.default_rng(seed)
    img = np.zeros((H, W, 3)); img[:] = (0.74, 0.67, 0.52); hgt = np.zeros((H, W))
    for k in range(8):   # faint vertical bands, 4 per strip
        cx = (k + 0.5) * W / 8; d = np.abs(wrapd(xx, cx, W))
        blend(img, sstep(5, 2, d) * 0.35, (0.55, 0.52, 0.38)); blend(img, sstep(1.5, 0.3, np.abs(d - 9)) * 0.25, (0.6, 0.45, 0.35))
    pink, dark, eye, leaf = (0.62, 0.36, 0.3), (0.42, 0.2, 0.17), (0.72, 0.6, 0.35), (0.38, 0.42, 0.26)
    for col in range(4):
        for row in range(4):
            cx = (col + 0.5) * W / 4; cy = (row + 0.5 + (0.5 if col % 2 else 0)) * H / 4
            for j in range(3):
                a = rng.uniform(0, 6.28)
                _leaf(img, hgt, yy, xx, cx + math.cos(a) * 10, cy + math.sin(a) * 10, rng.uniform(34, 48), a, leaf, W, H)
            _flower(img, hgt, yy, xx, cx, cy, rng.uniform(20, 26), rng, (pink, dark, eye), W, H)
            for j in range(2):
                a = rng.uniform(0, 6.28); r = rng.uniform(30, 40)
                _flower(img, hgt, yy, xx, cx + math.cos(a) * r, cy + math.sin(a) * r, rng.uniform(9, 12), rng, ((0.66, 0.5, 0.38), dark, eye), W, H, 4)
            for j in range(6):   # sprig dots
                a = rng.uniform(0, 6.28); r = rng.uniform(55, 75)
                d = np.hypot(wrapd(xx, cx + math.cos(a) * r, W), wrapd(yy, cy + math.sin(a) * r, H))
                blend(img, sstep(4, 2.5, d) * 0.8, leaf)
    _age_wall(img, hgt, H, W, seed + 40)
    smooth = 0.18 + 0.08 * tfbm(H, W, 8, seed + 3, 3)
    save_set("Wallpaper_Floral", img, None, hgt, 2.0, smooth)

def wallpaper_stripe(S=1024, seed=11):
    """An older green wallpaper: broad sage and moss stripes with a damask diamond in the broad one."""
    H = W = S; yy, xx = grid(H, W)
    img = np.zeros((H, W, 3)); img[:] = (0.42, 0.47, 0.36); hgt = np.zeros((H, W))
    u = (xx % (W / 4)) / (W / 4)   # 4 stripes per tile (2 per strip)
    blend(img, sstep(0.02, 0.05, u) * sstep(0.62, 0.58, u), (0.56, 0.6, 0.47))
    blend(img, sstep(0.01, 0.0, np.abs(u - 0.75) - 0.03) * 0.8, (0.6, 0.52, 0.32))
    cxs = (np.floor(xx / (W / 4)) + 0.31) * (W / 4); cy = H / 8
    dx = np.abs(xx - cxs) / (W / 4 * 0.24); dy = np.abs(wrapd(yy % (H / 4), cy, H / 4)) / (H / 8 * 0.9)
    dm = dx + dy
    blend(img, sstep(1.0, 0.94, dm) * sstep(0.62, 0.7, dm) * 0.8, (0.36, 0.42, 0.3))
    blend(img, sstep(0.35, 0.28, dm) * 0.8, (0.66, 0.6, 0.4))
    hgt += sstep(1.0, 0.94, dm) * 0.3
    _age_wall(img, hgt, H, W, seed + 40, yellow=0.35, stains=0.45)
    smooth = 0.22 + 0.1 * tfbm(H, W, 8, seed + 3, 3)
    save_set("Wallpaper_Stripe", img, None, hgt, 2.0, smooth)

# ------------------------------------------------------------------ parquet

def parquet(S=2048, seed=5):
    """Basket-weave oak parquet ('palubа'): 0.3 m squares of four planks, direction alternating, 4 x 4 per tile;
    dull varnish worn through on the walking lines, dirt in the joints, a few lifted planks."""
    H = W = S; yy, xx = grid(H, W); rng = np.random.default_rng(seed)
    sq = S / 4; pl = sq / 4
    si = np.floor(xx / sq); sj = np.floor(yy / sq); horiz = ((si + sj) % 2) == 0
    lx = xx - si * sq; ly = yy - sj * sq
    across = np.where(horiz, ly, lx); along = np.where(horiz, lx, ly)
    k = np.floor(across / pl); off = across - k * pl
    pid = (si * 7 + sj * 31 + k * 3).astype(int)
    tone = rng.random(200)[pid % 200]; tone2 = rng.random(200)[(pid * 13 + 5) % 200]
    # grain runs along each plank
    g1 = tfbm(H, W, 6, seed, 4, aspect=1.0); g2 = tfbm(H, W, 6, seed + 1, 4, aspect=1.0)
    gH = tfbm(H, W, 3, seed + 2, 5, aspect=12.0); gV = np.ascontiguousarray(tfbm(W, H, 3, seed + 3, 5, aspect=12.0).T)
    grain = np.where(horiz, gH, gV)
    rings = 0.5 + 0.5 * np.sin(grain * 40 + tone * 6)
    light = np.array((0.42, 0.27, 0.14)); darkc = np.array((0.2, 0.11, 0.05))
    t = np.clip(0.35 + 0.45 * tone[..., None] + 0.15 * (rings[..., None] - 0.5) + 0.1 * (g1[..., None] - 0.5), 0, 1)
    img = darkc * (1 - t) + light * t
    img *= (0.9 + 0.2 * tone2)[..., None]
    joint = np.minimum(off, pl - off); ends = np.minimum(along, sq - along)
    gap = np.minimum(joint, ends)
    jm = sstep(2.2, 0.6, gap)
    blend(img, jm * 0.92, (0.04, 0.025, 0.015))
    # wear: lighter, matte paths and a dirty perimeter (no varnish where people walked)
    wearn = tfbm(H, W, 2, seed + 7, 4)
    worn = sstep(0.5, 0.75, wearn)
    blend(img, worn * 0.35, (0.5, 0.4, 0.28))
    dirt = sstep(0.55, 0.85, tfbm(H, W, 5, seed + 8, 5))
    blend(img, dirt * 0.5, (0.08, 0.06, 0.04))
    scratches = sstep(0.92, 0.97, np.abs(np.sin(tfbm(H, W, 20, seed + 9, 3) * 90)))
    blend(img, scratches * 0.25, (0.55, 0.45, 0.32))
    hgt = -jm * 1.0 + 0.15 * rings + 0.25 * g2
    lift = (rng.random(200)[pid % 200] > 0.93)   # some planks stand proud
    hgt += lift * sstep(0, pl * 0.5, np.minimum(off, pl - off)) * 0.5
    smooth = 0.55 - worn * 0.35 - dirt * 0.25 - jm * 0.3 + 0.05 * tone2
    save_set("Parquet", img, None, hgt, 3.0, smooth)

# ------------------------------------------------------------------ overlays (not tiled)

def _blob(H, W, seed, cells=4, rough=0.55, cx=0.5, cy=0.5, rx=0.38, ry=0.38):
    yy, xx = grid(H, W)
    u = (xx / W - cx) / rx; v = (yy / H - cy) / ry
    r = np.sqrt(u * u + v * v)
    n = tfbm(H, W, cells, seed, 5)
    return r - (n - 0.5) * 2 * rough   # < 1 inside

def _blood_cols(thick, dry):
    """Dark arterial red where it pools, a thin brighter rim, browning where it dried."""
    wet = np.array((0.16, 0.005, 0.008)); deep = np.array((0.05, 0.0, 0.002)); rim = np.array((0.3, 0.02, 0.02))
    brown = np.array((0.12, 0.035, 0.02))
    t = np.clip(thick, 0, 1)[..., None]
    c = rim * (1 - t) + wet * t
    c = c * (1 - sstep(0.4, 1.0, t)) + deep * sstep(0.4, 1.0, t)
    d = np.clip(dry, 0, 1)[..., None]
    return c * (1 - d) + brown * d

def blood_pool(S=1024, seed=21):
    H = W = S; yy, xx = grid(H, W)
    b = _blob(H, W, seed, 3, 0.45, rx=0.33, ry=0.3)
    cov = sstep(1.0, 0.97, b)
    rng = np.random.default_rng(seed)
    for i in range(26):   # satellites and spatter round it
        a = rng.uniform(0, 6.28); r = rng.uniform(0.32, 0.47); R = rng.uniform(0.004, 0.022) * S
        cx, cy = 0.5 + math.cos(a) * r, 0.5 + math.sin(a) * r * 0.9
        d = np.hypot(xx - cx * W, yy - cy * H)
        cov = np.maximum(cov, sstep(R, R * 0.8, d))
    thick = sstep(1.0, 0.55, b) * cov
    dry = sstep(0.85, 1.0, b) * 0.6 + (1 - sstep(0.0, 0.2, thick)) * 0.3
    base = _blood_cols(thick, dry)
    hgt = thick * 4 + tfbm(H, W, 20, seed + 3, 3) * 0.3 * thick
    smooth = 0.25 + 0.68 * sstep(0.05, 0.5, thick)
    save_set("Blood_Pool", base, cov, hgt, 2.5, smooth, wrap=False)

def blood_drag(Wd=2048, Hd=512, seed=31):
    """A smear dragged along +U: dense at the start (U=0), thinning and breaking into streaks towards U=1."""
    H, W = Hd, Wd; yy, xx = grid(H, W)
    u = xx / W; v = yy / H
    wob = (tfbm(H, W, 1, seed, 3, aspect=1) - 0.5) * 0.25
    width = 0.36 * (1 - 0.55 * u) + 0.05
    d = np.abs(v - 0.5 - wob) / width
    streak = tfbm(H, W, 18, seed + 1, 4, aspect=0.05)   # lines along U
    edge = 1 - (tfbm(H, W, 6, seed + 2, 5) - 0.5) * 0.6
    cov = sstep(edge, edge - 0.15, d)
    cov *= np.clip(0.3 + streak * 1.4 - u * 0.9, 0, 1) ** 0.8
    cov *= sstep(1.0, 0.85, u) * sstep(0.1, 0.2, u)
    blob = _blob(H, W, seed + 4, 3, 0.35, cx=0.17, cy=0.5, rx=0.08, ry=0.36)
    cov = np.maximum(cov, sstep(1.0, 0.9, blob))
    thick = cov * (0.25 + 0.6 * (1 - u)) * (0.6 + 0.4 * streak)
    base = _blood_cols(thick * 1.2, 0.35 + 0.4 * u)
    hgt = thick * 2.5
    smooth = 0.2 + 0.6 * sstep(0.15, 0.6, thick)
    save_set("Blood_Drag", base, sstep(0.05, 0.3, cov), hgt, 2.0, smooth, wrap=False)

def blood_drops(S=1024, seed=41):
    """Round drops fallen from a height: a disc, a crown of tiny satellites, here and there a tail."""
    H = W = S; yy, xx = grid(H, W); rng = np.random.default_rng(seed)
    cov = np.zeros((H, W)); thick = np.zeros((H, W))
    for i in range(14):
        cx, cy = rng.uniform(0.1, 0.9) * W, rng.uniform(0.1, 0.9) * H; R = rng.uniform(0.012, 0.04) * S
        n = tnoise(H, W, 30, 30, seed + i)
        d = np.hypot(xx - cx, yy - cy) / R + (n - 0.5) * 0.5
        cov = np.maximum(cov, sstep(1.0, 0.9, d)); thick = np.maximum(thick, sstep(1.0, 0.3, d))
        for j in range(rng.integers(4, 10)):
            a = rng.uniform(0, 6.28); rr = R * rng.uniform(1.3, 2.4); r2 = R * rng.uniform(0.05, 0.18)
            dd = np.hypot(xx - cx - math.cos(a) * rr, yy - cy - math.sin(a) * rr)
            cov = np.maximum(cov, sstep(r2, r2 * 0.6, dd))
    base = _blood_cols(thick, 0.3)
    save_set("Blood_Drops", base, cov, thick * 3, 2.0, 0.3 + 0.6 * thick, wrap=False)

def blood_splatter(S=1024, seed=51):
    """Wall splatter (V up): a burst with radiating elongated drops, and runs dripping down from it."""
    H = W = S; yy, xx = grid(H, W); rng = np.random.default_rng(seed)
    cx, cy = 0.5 * W, 0.38 * H
    b = _blob(H, W, seed, 5, 0.5, cx=0.5, cy=0.38, rx=0.13, ry=0.11)
    cov = sstep(1.0, 0.9, b); thick = sstep(1.0, 0.4, b)
    for i in range(70):
        a = rng.uniform(0, 6.28); r = rng.uniform(0.1, 0.42) * S; L = rng.uniform(0.01, 0.05) * S; R = rng.uniform(0.002, 0.01) * S
        px, py = cx + math.cos(a) * r, cy + math.sin(a) * r
        u = (xx - px) * math.cos(a) + (yy - py) * math.sin(a); v = -(xx - px) * math.sin(a) + (yy - py) * math.cos(a)
        e = np.sqrt((u / (L + R)) ** 2 + (v / R) ** 2)
        cov = np.maximum(cov, sstep(1.0, 0.8, e)); thick = np.maximum(thick, sstep(1.0, 0.2, e) * 0.6)
    for i in range(9):   # runs (image rows grow downwards)
        x0 = cx + rng.uniform(-0.11, 0.11) * W; y0 = cy + rng.uniform(0.0, 0.06) * H; L = rng.uniform(0.15, 0.55) * H
        w0 = rng.uniform(0.004, 0.009) * W
        wig = (tnoise(H, W, 12, 1, seed + 70 + i) - 0.5) * 6
        t = np.clip((yy - y0) / L, 0, 1)
        d = np.abs(xx - x0 - wig) / (w0 * (1 - 0.5 * t) + 1)
        m = (yy > y0) * (yy < y0 + L) * sstep(1.0, 0.6, d)
        bulb = np.hypot(xx - x0, (yy - y0 - L) * 0.8) < w0 * 1.6
        cov = np.maximum(cov, np.maximum(m, bulb)); thick = np.maximum(thick, (m * 0.5 + bulb * 0.9))
    base = _blood_cols(thick, 0.45)
    save_set("Blood_Splatter", base, cov, thick * 2.5, 2.0, 0.25 + 0.5 * thick, wrap=False)

def blood_hand(S=512, seed=61):
    """A right hand pressed flat on a wall and dragged down (V up)."""
    H = W = S; yy, xx = grid(H, W)
    def cap(ax, ay, bx, by, r):
        px, py = xx - ax, yy - ay; vx, vy = bx - ax, by - ay
        t = np.clip((px * vx + py * vy) / (vx * vx + vy * vy), 0, 1)
        return np.hypot(px - vx * t, py - vy * t) / r
    s = S / 512
    shape = np.minimum.reduce([
        np.hypot((xx - 256 * s) / (78 * s), (yy - 250 * s) / (90 * s)),
        cap(205 * s, 180 * s, 190 * s, 70 * s, 20 * s), cap(245 * s, 168 * s, 245 * s, 45 * s, 21 * s),
        cap(285 * s, 172 * s, 298 * s, 58 * s, 20 * s), cap(318 * s, 195 * s, 345 * s, 105 * s, 17 * s),
        cap(185 * s, 270 * s, 120 * s, 205 * s, 23 * s)])
    n = tfbm(H, W, 10, seed, 4)
    print_ = sstep(1.0, 0.85, shape + (n - 0.5) * 0.5) * np.clip(0.4 + n * 1.2, 0, 1)
    # drag downwards: streaks below the palm
    streak = tfbm(H, W, 22, seed + 1, 3, aspect=0.06)
    below = (yy > 250 * s) * np.clip(1 - (yy - 250 * s) / (220 * s), 0, 1)
    drag = sstep(1.0, 0.8, np.abs(xx - 256 * s) / (80 * s)) * below * np.clip(streak * 2.2 - 0.3, 0, 1)
    cov = np.clip(np.maximum(print_, drag), 0, 1)
    base = _blood_cols(cov * 0.6, 0.55)
    save_set("Blood_Hand", base, cov, cov * 1.5, 1.5, 0.2 + 0.4 * cov, wrap=False)

def wall_torn(S=1024, seed=71):
    """Wallpaper torn away: grey-pink plaster in the middle, the paper's white backing round it, dark glue stains."""
    H = W = S
    b = _blob(H, W, seed, 4, 0.6, rx=0.36, ry=0.42)
    cov = sstep(1.08, 1.0, b)
    plaster = np.zeros((H, W, 3)); plaster[:] = (0.55, 0.52, 0.48)
    f = tfbm(H, W, 8, seed + 1, 5)
    plaster *= (0.8 + 0.35 * f)[..., None]
    blend(plaster, sstep(0.55, 0.75, tfbm(H, W, 4, seed + 2, 5)) * 0.5, (0.42, 0.36, 0.28))
    backing = sstep(1.08, 1.0, b) * sstep(0.9, 0.98, b)
    blend(plaster, backing, (0.8, 0.77, 0.68))
    blend(plaster, sstep(0.88, 0.92, b) * sstep(1.0, 0.92, b) * 0.6, (0.25, 0.2, 0.13))
    hgt = backing * 1.2 + f * 0.8 - sstep(0.9, 0.5, b) * 0.4
    save_set("Wall_Torn", plaster, cov, hgt, 3.0, 0.08 + 0.05 * f, wrap=False)

def ceiling_stain(S=1024, seed=81):
    """A water stain: tide-mark rings of rust-brown, mould specks in the middle."""
    H = W = S
    b = _blob(H, W, seed, 3, 0.5, rx=0.4, ry=0.4)
    cov = sstep(1.0, 0.6, b) * 0.85
    base = np.zeros((H, W, 3)); base[:] = (0.5, 0.4, 0.25)
    rings = sum(sstep(0.03, 0.0, np.abs(b - r)) for r in (0.95, 0.78, 0.6))
    blend(base, np.clip(rings, 0, 1) * 0.8, (0.3, 0.2, 0.1))
    mould = sstep(0.7, 0.78, tfbm(H, W, 30, seed + 2, 3)) * sstep(0.7, 0.2, b)
    blend(base, mould, (0.1, 0.1, 0.08))
    cov = np.clip(cov * 0.55 + rings * 0.4 + mould, 0, 1) * sstep(1.0, 0.92, b)
    save_set("Ceiling_Stain", base, cov, rings + mould, 1.0, 0.1 + 0.0 * b, wrap=False)

def ceiling_hole(S=1024, seed=91):
    """Plaster fallen out of the ceiling: the slab's dark concrete in a broken-edged hole, cracks running off it."""
    H = W = S; yy, xx = grid(H, W); rng = np.random.default_rng(seed)
    b = _blob(H, W, seed, 4, 0.45, rx=0.28, ry=0.24)
    hole = sstep(1.0, 0.94, b)
    base = np.zeros((H, W, 3)); base[:] = (0.62, 0.6, 0.56)
    blend(base, hole, (0.12, 0.115, 0.11))
    blend(base, hole * sstep(0.6, 0.8, tfbm(H, W, 12, seed + 1, 4)) * 0.6, (0.2, 0.19, 0.17))
    lip = sstep(1.12, 1.0, b) * (1 - hole)
    blend(base, lip * 0.6, (0.35, 0.3, 0.24))
    cracks = np.zeros((H, W))
    for i in range(8):   # random walks out from the rim
        a = rng.uniform(0, 6.28); x = 0.5 * W + math.cos(a) * 0.25 * W; y = 0.5 * H + math.sin(a) * 0.22 * H
        for st in range(60):
            a += rng.normal(0, 0.35); x += math.cos(a) * 4; y += math.sin(a) * 4
            if not (2 < x < W - 3 and 2 < y < H - 3): break
            cracks[int(y) - 1:int(y) + 2, int(x) - 1:int(x) + 2] = 1
    blend(base, cracks * 0.9, (0.18, 0.16, 0.13))
    cov = np.clip(np.maximum(hole, np.maximum(lip * 0.9, cracks * 0.9)), 0, 1)
    save_set("Ceiling_Hole", base, cov, -hole * 3 + lip - cracks, 2.0, 0.05 + 0 * b, wrap=False)

def mould(S=1024, seed=101):
    """Black mould creeping up from a floor corner (V up, the bottom edge at the floor)."""
    H = W = S; yy, xx = grid(H, W)
    up = yy / H   # image row 0 is the top: 1 at the bottom edge
    f = tfbm(H, W, 6, seed, 6)
    reach = sstep(0.0, 0.9, up) * sstep(0.0, 0.25, xx / W) * sstep(1.0, 0.75, xx / W)
    cov = sstep(0.75, 0.55, (1 - up) + (f - 0.5) * 0.9) * reach
    specks = sstep(0.72, 0.8, tfbm(H, W, 40, seed + 1, 3)) * sstep(0.95, 0.4, (1 - up))
    cov = np.clip(cov + specks * 0.8, 0, 1)
    base = np.zeros((H, W, 3)); base[:] = (0.07, 0.08, 0.06)
    blend(base, sstep(0.4, 0.0, cov) * 0.6, (0.28, 0.27, 0.2))
    save_set("Wall_Mould", base, cov * 0.9, cov, 1.0, 0.1 + 0 * f, wrap=False)

def make_all():
    import time; t = time.time(); out = []
    for fn in (wallpaper_floral, wallpaper_stripe, parquet, blood_pool, blood_drag, blood_drops, blood_splatter, blood_hand,
               wall_torn, ceiling_stain, ceiling_hole, mould):
        t0 = time.time(); fn(); out.append(f"{fn.__name__} {time.time() - t0:.1f}s")
    return ", ".join(out) + f" | total {time.time() - t:.1f}s"
