"""Khrushchevka living-room props for BodycamHorror (the 'gigakhrushch' flats of the Level scene): the furniture and
the clutter of a Soviet panel flat, gone to rot. Run inside Blender over the bridge, after propkit.py and
apartment_props.py (it reuses their helpers):

    exec(open(r"E:\\Unity\\Projects\\BodycamHorror\\Blender\\tools\\propkit.py").read())
    exec(open(r"E:\\Unity\\Projects\\BodycamHorror\\Blender\\tools\\apartment_props.py").read())
    exec(open(r"E:\\Unity\\Projects\\BodycamHorror\\Blender\\tools\\khrushchev_props.py").read())
    build_khrush(); bake_khrush()     # bakes in the background (Blender/bake_log.txt)
    finish_khrush()                   # join, baked material, FBX export, save Props_Khrushchev.blend

Props (one FBX each, metres, Z up, fronts towards -Y = +Z in Unity):
  Wardrobe        a two-door lacquered wardrobe with a mezzanine          1.0 x 0.56 x 1.95, origin floor middle
  Sideboard       a 'servant': cupboard and drawers under a glazed top with plates, cups and a crystal vase;
                  its panes are their own mesh (Sideboard_Glass) for a see-through material   1.2 x 0.42 x 1.8
  Bed             a single wooden bed, a sagging mattress, a wool blanket hanging off, a pillow   0.95 x 2.05
  CRT_TV          a wood-cased tube television with rabbit ears                0.62 x 0.46 x 0.48, origin under it
  TV_Stand        a low veneer stand on splayed legs, magazines in its niche   0.8 x 0.42 x 0.5
  DiningTable     a kitchen table under a checked oilcloth                      1.0 x 0.65 x 0.75
  CoffeeTable     a low coffee table, papers on it                              0.9 x 0.5 x 0.5
  Radiator_Cast   a cast-iron radiator under thick cream paint, pipes to the wall; origin on the floor at the wall
  WallCarpet      the carpet on the wall (2.0 x 1.4), origin at its middle on the wall plane
  Rug_Floor       a worn rug on the floor, a corner curled up (2.2 x 1.5)
  Curtains        a rod with two heavy floral curtains, floor to 2.45 m; origin on the floor at the wall, 2.3 m wide
  Chandelier      three frosted globes on brass arms; origin at the ceiling (it hangs down 0.6 m); the globes are
                  their own mesh (Chandelier_Glass) so they can glow
  FlatDecor       a set, one mesh per piece each with its own origin: WallClock, Portrait, Calendar (on the wall plane),
                  Radio, Ashtray, PottedPlant, Kettle (on their feet)
  FlatFurniture   a set: Nightstand, Stool, FloorLamp (each on its feet)
"""
import bpy, bmesh, math, os, random
import numpy as np
from mathutils import Vector

KH_SINGLE = ["Wardrobe", "Sideboard", "Bed", "CRT_TV", "TV_Stand", "DiningTable", "CoffeeTable", "Radiator_Cast",
             "WallCarpet", "Rug_Floor", "Curtains", "Chandelier"]
KH_SETS = {"FlatDecor": ["WallClock", "Portrait", "Calendar", "Radio", "Ashtray", "PottedPlant", "Kettle"],
           "FlatFurniture": ["Nightstand", "Stool", "FloorLamp"]}
KH_PROPS = KH_SINGLE + list(KH_SETS)
KH_RES = {"Wardrobe": 2048, "Sideboard": 2048, "Bed": 2048, "CRT_TV": 1024, "TV_Stand": 1024, "DiningTable": 1024,
          "CoffeeTable": 1024, "Radiator_Cast": 1024, "WallCarpet": 2048, "Rug_Floor": 2048, "Curtains": 2048,
          "Chandelier": 1024, "FlatDecor": 2048, "FlatFurniture": 2048}
SET_GAP = 0.8   # pieces of a set stand this far apart along X inside the set

# ------------------------------------------------------------------ print textures (numpy)

def _bl(img, mask, col):
    """_blend that also takes boolean masks."""
    _blend(img, np.asarray(mask, dtype=float), col)

def _grid(h, w):
    yy, xx = np.mgrid[0:h, 0:w].astype(float)
    return yy, xx

def tex_carpet(name, pal, W=1600, H=1120, seed=1):
    """A Soviet wall/floor carpet: a lattice field of small rosettes, a stepped central medallion with pendants,
    quarter medallions in the corners, a wide border with a chain of diamonds; faded, worn, dusty."""
    yy, xx = _grid(H, W)
    u = (xx + 0.5) / W * 2 - 1; v = (yy + 0.5) / H * 2 - 1
    au, av = np.abs(u), np.abs(v)
    ex = (1 - au) * W / 2; ey = (1 - av) * H / 2; e = np.minimum(ex, ey)
    img = np.zeros((H, W, 3)); img[:] = pal["field"]
    k = 64.0
    a = (xx + yy) / k; b = (xx - yy) / k
    da = a % 1.0; db = b % 1.0
    line = np.minimum(np.minimum(da, 1 - da), np.minimum(db, 1 - db))
    _bl(img, (line < 0.045) * 0.85, pal["line"])
    dm = np.abs(da - 0.5) + np.abs(db - 0.5)
    _bl(img, (dm < 0.24), pal["motif1"])
    _bl(img, (dm < 0.15), pal["motif2"])
    _bl(img, (dm < 0.06), pal["light"])
    # the medallion: a stepped diamond with an eight-point star in it
    mu, mv = u / 0.40, v / 0.56
    m = np.abs(mu) + np.abs(mv)
    th = np.arctan2(mv, mu); r = np.sqrt(mu ** 2 + mv ** 2)
    step = np.maximum(np.abs(mu), np.abs(mv)) * 0.35 + m * 0.65
    _bl(img, step < 1.0, pal["med_bg"])
    _bl(img, np.abs(step - 1.0) < 0.025, pal["light"])
    _bl(img, step < 0.86, pal["med2"])
    _bl(img, np.abs(step - 0.86) < 0.02, pal["line"])
    star = r * (1 + 0.22 * np.cos(8 * th))
    _bl(img, star < 0.62, pal["field"])
    _bl(img, np.abs(star - 0.62) < 0.025, pal["light"])
    _bl(img, star < 0.36, pal["motif1"])
    _bl(img, (np.abs(mu) + np.abs(mv) < 0.16), pal["light"])
    for sgn in (-1, 1):   # pendants above and below
        pm = np.abs(u / 0.08) + np.abs((v - sgn * 0.66) / 0.09)
        _bl(img, pm < 1.0, pal["med_bg"]); _bl(img, pm < 0.55, pal["med2"]); _bl(img, pm < 0.2, pal["light"])
    # quarter medallions in the corners (inside the border)
    cm = (1 - au) / 0.30 + (1 - av) / 0.42
    _bl(img, cm < 1.0, pal["med_bg"])
    _bl(img, np.abs(cm - 1.0) < 0.03, pal["light"])
    _bl(img, cm < 0.75, pal["med2"])
    # the border
    B = H * 0.11
    band = e < B
    img[band] = img[band] * 0.0 + np.array(pal["border"])
    along = np.where(ey < ex, xx, yy)
    dia = np.abs((along % 56) - 28) / 28 + np.abs((e - B / 2) / (B * 0.42))
    _bl(img, band & (dia < 0.95), pal["motif2"])
    _bl(img, band & (dia < 0.6), pal["field"])
    _bl(img, band & (dia < 0.25), pal["light"])
    for d0, wdt in ((B, 5), (B + 12, 3), (5, 4), (B - 6, 2)):
        _bl(img, np.abs(e - d0) < wdt, pal["light"] if wdt < 4 else pal["line"])
    # pile, fading, wear
    rng = np.random.default_rng(seed)
    img *= (0.88 + 0.12 * rng.random((H, W)))[..., None]
    img *= (0.72 + 0.28 * _fbm(H, W, 6, seed + 3))[..., None]
    fade = np.clip((_fbm(H, W, 3, seed + 9) - 0.55) * 3.0, 0, 1)[..., None]
    img = img * (1 - fade * 0.35) + np.array(pal.get("faded", (0.45, 0.38, 0.3))) * fade * 0.35
    worn = np.clip((_fbm(H, W, 18, seed + 5) - 0.68) * 6.0, 0, 1)[..., None]
    img = img * (1 - worn * 0.5) + np.array((0.3, 0.26, 0.2)) * worn * 0.5
    return _save(name, img)

PAL_WALL = dict(field=(0.40, 0.055, 0.045), line=(0.10, 0.035, 0.035), motif1=(0.07, 0.08, 0.15), motif2=(0.6, 0.48, 0.3),
                light=(0.76, 0.68, 0.52), med_bg=(0.06, 0.05, 0.075), med2=(0.5, 0.33, 0.14), border=(0.09, 0.05, 0.06),
                faded=(0.5, 0.36, 0.3))
PAL_RUG = dict(field=(0.3, 0.09, 0.06), line=(0.12, 0.06, 0.04), motif1=(0.16, 0.15, 0.12), motif2=(0.52, 0.42, 0.26),
               light=(0.66, 0.6, 0.46), med_bg=(0.13, 0.08, 0.06), med2=(0.42, 0.22, 0.1), border=(0.16, 0.1, 0.07),
               faded=(0.38, 0.32, 0.26))

def tex_oilcloth():
    s = 1024; yy, xx = _grid(s, s)
    img = np.zeros((s, s, 3)); img[:] = (0.86, 0.83, 0.74)
    r = ((yy // 32) % 2 == 0).astype(float); c = ((xx // 32) % 2 == 0).astype(float)
    red = np.array((0.62, 0.1, 0.08))
    _bl(img, r * 0.5, red); _bl(img, c * 0.5, red); _bl(img, r * c, red * 0.85)
    rng = np.random.default_rng(4)
    for _ in range(14):   # stains: tea rings, grease
        cx, cy, R = rng.uniform(0, s), rng.uniform(0, s), rng.uniform(20, 90)
        d = np.sqrt((xx - cx) ** 2 + (yy - cy) ** 2)
        _bl(img, (np.abs(d - R) < 3) * 0.45 + (d < R) * 0.12, (0.32, 0.2, 0.08))
    img *= (0.78 + 0.22 * _fbm(s, s, 8, 2))[..., None]
    return _save("Khrush_Oilcloth", img)

def tex_curtain():
    s = 1024; img = np.zeros((s, s, 3)); img[:] = (0.62, 0.53, 0.4)
    yy, xx = _grid(s, s)
    img *= (0.94 + 0.06 * np.sin(xx * 0.8))[..., None]   # the weave's ribs
    rng = np.random.default_rng(8)
    for gy in range(5):
        for gx in range(4):
            cx = gx * 256 + (128 if gy % 2 else 0) + rng.uniform(-20, 20); cy = gy * 205 + rng.uniform(-20, 20)
            for ox in (0, -s, s):
                for oy in (0, -s, s):
                    X, Y = cx + ox, cy + oy
                    if -120 < X < s + 120 and -120 < Y < s + 120:
                        for i in range(5):
                            a = i * 1.257 + gx; _leaf(img, X + math.cos(a) * 26, Y + math.sin(a) * 26, 40, a, (0.3, 0.32, 0.16))
                        _rose(img, X, Y, 30, rng, dark=(0.36, 0.08, 0.05), light=(0.66, 0.3, 0.2))
                        _rose(img, X + 46, Y + 30, 13, rng, dark=(0.42, 0.3, 0.12), light=(0.7, 0.55, 0.3))
    img *= (0.7 + 0.3 * _fbm(s, s, 4, 6))[..., None]
    return _save("Khrush_Curtain", img)

def tex_blanket():
    s = 1024
    def stripes(n):
        a = np.zeros((n, 3)); a[:] = (0.16, 0.2, 0.12)
        for p0, wdt, col in ((0, 30, (0.08, 0.09, 0.06)), (70, 10, (0.5, 0.42, 0.24)), (110, 46, (0.22, 0.12, 0.07)),
                             (200, 6, (0.5, 0.42, 0.24)), (226, 18, (0.08, 0.09, 0.06))):
            for k in range(0, n, 256): a[k + p0:k + p0 + wdt] = col
        return a
    a = stripes(s)
    tw = ((np.arange(s)[:, None] + np.arange(s)[None, :]) % 4 < 2)[..., None]
    img = np.where(tw, a[:, None, :], a[None, :, :]) * 0.6 + (a[:, None, :] + a[None, :, :]) * 0.2
    img *= (0.8 + 0.2 * _fbm(s, s, 10, 7))[..., None]
    return _save("Khrush_Blanket", img)

def tex_portrait():
    """A faded studio portrait of nobody in particular: a man's bust, dark suit, sepia, foxed."""
    h, w = 640, 512; yy, xx = _grid(h, w)
    img = np.zeros((h, w, 3))
    vig = np.clip(1.2 - np.sqrt(((xx - w / 2) / (w * 0.6)) ** 2 + ((yy - h * 0.45) / (h * 0.6)) ** 2), 0, 1)
    img[:] = (0.16, 0.12, 0.08); img += (vig ** 2)[..., None] * np.array((0.32, 0.26, 0.18))
    head = ((xx - w / 2) / 82) ** 2 + ((yy - 250) / 104) ** 2
    shade = np.clip(0.75 - (xx - w / 2 + 30) / 220, 0.25, 1.0)
    _bl(img, (head < 1.0) * shade, (0.62, 0.5, 0.36))
    _bl(img, ((xx - w / 2) / 86) ** 2 + ((yy - 172) / 52) ** 2 < 1.0, (0.12, 0.09, 0.06))   # hair
    sh = ((xx - w / 2) / 230) ** 2 + ((yy - 640) / 260) ** 2 < 1.0
    _bl(img, sh, (0.07, 0.06, 0.05))
    shirt = (np.abs(xx - w / 2) < (yy - 380) * 0.35) & (yy > 380) & (yy < 560)
    _bl(img, shirt, (0.58, 0.52, 0.42))
    _bl(img, (np.abs(xx - w / 2) < 12) & (yy > 400) & (yy < 560), (0.1, 0.07, 0.06))   # tie
    neck = (np.abs(xx - w / 2) < 46) & (yy > 320) & (yy < 400)
    _bl(img, neck * 0.9, (0.5, 0.4, 0.29))
    img *= (0.7 + 0.3 * _fbm(h, w, 5, 21))[..., None]
    fox = np.clip((_fbm(h, w, 22, 23) - 0.7) * 5, 0, 1)
    _bl(img, fox * 0.5, (0.42, 0.3, 0.16))
    gray = img.mean(axis=2, keepdims=True)
    img = gray * np.array((1.1, 0.95, 0.75))
    return _save("Khrush_Portrait", img)

def tex_calendar():
    h, w = 768, 512; yy, xx = _grid(h, w)
    img = np.zeros((h, w, 3)); img[:] = (0.84, 0.82, 0.75)
    ph = (yy > 30) & (yy < 330) & (xx > 30) & (xx < w - 30)
    land = np.zeros((h, w, 3)); land[:] = (0.4, 0.52, 0.62)
    hills = yy > 190 + 40 * np.sin(xx / 70) + 30 * _fbm(h, w, 6, 2)
    land[hills] = (0.3, 0.38, 0.22)
    land *= (0.75 + 0.25 * _fbm(h, w, 12, 3))[..., None]
    img[ph] = land[ph]
    _bl(img, (yy > 345) & (yy < 395) & (xx > 30) & (xx < w - 30), (0.62, 0.1, 0.08))
    _bl(img, (yy > 360) & (yy < 380) & (xx > 160) & (xx < 350), (0.86, 0.8, 0.7))
    for r in range(6):
        for c in range(7):
            x0 = 40 + c * 63; y0 = 420 + r * 54
            col = (0.6, 0.1, 0.08) if c >= 5 else (0.15, 0.14, 0.13)
            digits = 1 if (r * 7 + c) < 9 else 2
            for d in range(digits):
                _bl(img, (xx > x0 + d * 14) & (xx < x0 + d * 14 + 10) & (yy > y0) & (yy < y0 + 22), col)
    img *= (0.8 + 0.2 * _fbm(h, w, 6, 9))[..., None]
    _bl(img, np.clip((_fbm(h, w, 3, 4) - 0.5) * 2, 0, 1) * 0.35, (0.6, 0.48, 0.28))   # yellowed
    return _save("Khrush_Calendar", img)

def tex_clock():
    s = 512; yy, xx = _grid(s, s)
    d = np.sqrt((xx - s / 2) ** 2 + (yy - s / 2) ** 2); th = np.arctan2(yy - s / 2, xx - s / 2)
    img = np.zeros((s, s, 3)); img[:] = (0.05, 0.05, 0.05)
    _bl(img, d < 240, (0.82, 0.78, 0.66))
    _bl(img, np.abs(d - 232) < 4, (0.2, 0.18, 0.15))
    for i in range(60):
        a = i / 60 * math.tau; big = i % 5 == 0
        ang = np.abs(((th - a + math.pi) % math.tau) - math.pi)
        _bl(img, (ang * d < (6 if big else 1.6)) & (d > (175 if big else 205)) & (d < 222), (0.08, 0.07, 0.06))
    img *= (0.8 + 0.2 * _fbm(s, s, 5, 4))[..., None]
    return _save("Khrush_Clock", img)

def tex_dial():
    h, w = 128, 512; yy, xx = _grid(h, w)
    img = np.zeros((h, w, 3)); img[:] = (0.62, 0.52, 0.32)
    for i in range(46):
        x = 30 + i * 10
        _bl(img, (np.abs(xx - x) < 1.2) & (yy > (40 if i % 5 else 30)) & (yy < 70), (0.12, 0.09, 0.05))
    for r, y in ((0, 85), (1, 100)):
        for i in range(9): _bl(img, (xx > 40 + i * 52) & (xx < 60 + i * 52) & (np.abs(yy - y) < 4), (0.2, 0.14, 0.08))
    _bl(img, (np.abs(xx - 300) < 2) & (yy > 18) & (yy < 112), (0.7, 0.08, 0.04))
    img *= (0.75 + 0.25 * _fbm(h, w, 4, 5))[..., None]
    return _save("Khrush_Dial", img)

# ------------------------------------------------------------------ geometry helpers

def sheet(name, fn, nu, nv, mat, parent, thick=0.0, uvscale=(1.0, 1.0), smooth=True):
    """A cloth/paper sheet: fn(u, v) -> (x, y, z) for u, v in 0..1; its 'Print' UV is (u, v) * uvscale.
    The front is the side the cross product du x dv points to."""
    bm = bmesh.new()
    grid = [[bm.verts.new(fn(i / nu, j / nv)) for i in range(nu + 1)] for j in range(nv + 1)]
    uvl = bm.loops.layers.uv.new("Print")
    for j in range(nv):
        for i in range(nu):
            f = bm.faces.new((grid[j][i], grid[j][i + 1], grid[j + 1][i + 1], grid[j + 1][i]))
            for lp, (a, b) in zip(f.loops, ((i, j), (i + 1, j), (i + 1, j + 1), (i, j + 1))):
                lp[uvl].uv = (a / nu * uvscale[0], b / nv * uvscale[1])
    o = from_bm(name, bm, mat, parent)
    for p in o.data.polygons: p.use_smooth = smooth
    if thick:
        m = o.modifiers.new("Solid", "SOLIDIFY"); m.thickness = thick; m.offset = 0
    return o

def drape(name, W, L, top, hx, hy0, hy1, mat, parent, loc=(0, 0, 0), nu=44, nv=56, seed=1, tile=0.6,
          fold=0.022, flare=0.12, thick=0.006, rumple=0.008, floor=0.004):
    """Cloth lying on a W x L top at height top, hanging hx over both X sides, hy0 over the -Y end and hy1 over +Y;
    folds grow as it drops, and what reaches the floor spreads out on it."""
    rng = random.Random(seed); ph = [rng.uniform(0, 6.28) for _ in range(8)]
    S0, S1 = -(W / 2 + hx), W / 2 + hx; T0, T1 = -(L / 2 + hy0), L / 2 + hy1
    def fn(u, v):
        s = S0 + (S1 - S0) * u; t = T0 + (T1 - T0) * v
        dx = max(0.0, abs(s) - W / 2); dy = max(0.0, abs(t) - L / 2)
        x = math.copysign(min(abs(s), W / 2), s); y = math.copysign(min(abs(t), L / 2), t)
        d = max(dx, dy); z = top - d
        w = fold * min(1.0, d / 0.12)
        if dx > 0: x += math.copysign(0.006 + dx * flare + w * math.sin(t * 23 + ph[0]) + 0.4 * w * math.sin(t * 51 + ph[1]), s)
        if dy > 0: y += math.copysign(0.006 + dy * flare + w * math.sin(s * 23 + ph[2]) + 0.4 * w * math.sin(s * 47 + ph[3]), t)
        if d == 0: z += rumple * math.sin(s * 9 + ph[4]) * math.sin(t * 7 + ph[5]) + rumple * 0.5 * math.sin(s * 23 + t * 17 + ph[6])
        if z < floor:   # on the floor: lies out flat
            out = floor - z; z = floor
            if dx >= dy: x += math.copysign(out * 0.9, s)
            else: y += math.copysign(out * 0.9, t)
        return (x + loc[0], y + loc[1], z + loc[2])
    return sheet(name, fn, nu, nv, mat, parent, thick, ((S1 - S0) / tile, (T1 - T0) / tile))

def panel_front(prefix, w, h, loc, mat, parent, depth=0.018, inset=0.05, bv=0.003):
    """A cabinet door: a board with a thin raised moulding frame on its face (the face at loc's -Y side)."""
    x, y, z = loc
    box(prefix, (w, depth, h), (x, y, z), mat, parent, bv=bv)
    fy = y - depth / 2 - 0.003
    for dz in (-1, 1):
        box(prefix + "_MoulH", (w - 2 * inset, 0.006, 0.012), (x, fy, z + dz * (h / 2 - inset)), mat, parent, bv=0.002)
    for dx in (-1, 1):
        box(prefix + "_MoulV", (0.012, 0.006, h - 2 * inset), (x + dx * (w / 2 - inset), fy, z), mat, parent, bv=0.002)

def splay_leg(name, top, length, out, r0, r1, mat, parent):
    """A tapered round leg from top, going down length and out by out (a Vector in XY)."""
    a = Vector(top); b = a + Vector((out[0], out[1], -length))
    v = b - a
    return cyl(name, r1, r0, v.length, (a + b) / 2, mat, parent, seg=12, axis=-v)

# ------------------------------------------------------------------ the props

def build_wardrobe(x0):
    rt = root("Wardrobe", (x0, 0, 0))
    ven, inner, brass, dark = role("veneer"), role("carcass"), role("brass"), role("dark")
    W, D, H = 1.0, 0.56, 1.95
    box("W_Plinth", (W - 0.04, D - 0.06, 0.08), (0, 0.01, 0.04), dark, rt, bv=0.004)
    box("W_SideL", (0.022, D, H - 0.1), (-W / 2 + 0.011, 0, 0.08 + (H - 0.1) / 2), ven, rt, bv=0.003)
    box("W_SideR", (0.022, D, H - 0.1), (W / 2 - 0.011, 0, 0.08 + (H - 0.1) / 2), ven, rt, bv=0.003)
    box("W_Back", (W - 0.04, 0.006, H - 0.12), (0, D / 2 - 0.003, 0.08 + (H - 0.1) / 2), inner, rt, bv=0)
    box("W_Bottom", (W - 0.044, D - 0.02, 0.02), (0, 0, 0.09), ven, rt, bv=0.002)
    box("W_Top", (W + 0.03, D + 0.03, 0.035), (0, -0.005, H - 0.0175), ven, rt, bv=0.006)
    box("W_Mid", (W - 0.044, D - 0.02, 0.02), (0, 0, 1.6), ven, rt, bv=0.002)
    dw = W / 2 - 0.024
    for sx in (-1, 1):
        cx = sx * (dw / 2 + 0.003)
        panel_front(f"W_Door{sx}", dw, 1.46, (cx, -D / 2 - 0.009, 0.11 + 0.73), ven, rt)
        panel_front(f"W_DoorTop{sx}", dw, 0.3, (cx, -D / 2 - 0.009, 1.62 + 0.15), ven, rt, inset=0.04)
        hx = sx * 0.035
        rod(f"W_Handle{sx}", (hx, -D / 2 - 0.04, 0.88), (hx, -D / 2 - 0.04, 1.06), 0.006, brass, rt)
        for z in (0.88, 1.06): rod(f"W_HandleFoot{sx}", (hx, -D / 2 - 0.018, z), (hx, -D / 2 - 0.04, z), 0.005, brass, rt)
        rod(f"W_KnobTop{sx}", (hx, -D / 2 - 0.018, 1.7), (hx, -D / 2 - 0.035, 1.7), 0.01, brass, rt)
    cyl("W_Keyhole", 0.009, 0.009, 0.004, (0.03, -D / 2 - 0.02, 0.97), brass, rt, seg=12, axis=(0, 1, 0))
    return rt

def build_sideboard(x0):
    rt = root("Sideboard", (x0, 0, 0))
    ven, inner, brass, dark, glass = role("veneer"), role("carcass"), role("brass"), role("dark"), role("glass")
    china, crystal = role("china"), role("crystal")
    W, D = 1.2, 0.42
    box("S_Plinth", (W - 0.04, D - 0.05, 0.07), (0, 0.01, 0.035), dark, rt, bv=0.004)
    box("S_Low", (W, D, 0.78), (0, 0, 0.07 + 0.39), inner, rt, bv=0.003)
    box("S_Counter", (W + 0.03, D + 0.025, 0.028), (0, -0.005, 0.864), ven, rt, bv=0.006)
    dw = W / 2 - 0.01
    for sx in (-1, 1):
        cx = sx * (dw / 2 + 0.004)
        panel_front(f"S_Door{sx}", dw, 0.54, (cx, -D / 2 - 0.009, 0.08 + 0.27), ven, rt)
        panel_front(f"S_Drawer{sx}", dw, 0.19, (cx, -D / 2 - 0.009, 0.64 + 0.1), ven, rt, inset=0.035)
        rod(f"S_DrawerPull{sx}", (cx - 0.07, -D / 2 - 0.03, 0.745), (cx + 0.07, -D / 2 - 0.03, 0.745), 0.0055, brass, rt)
        for px in (cx - 0.07, cx + 0.07): rod("S_PullFoot", (px, -D / 2 - 0.018, 0.745), (px, -D / 2 - 0.03, 0.745), 0.005, brass, rt)
        cyl(f"S_DoorKnob{sx}", 0.012, 0.009, 0.022, (sx * 0.04, -D / 2 - 0.028, 0.4), brass, rt, seg=14, axis=(0, -1, 0))
    # the glazed top: sides, back, roof, a shelf
    UD = 0.32; uy = D / 2 - UD / 2; z0, z1 = 0.878, 1.8
    for sx in (-1, 1): box(f"S_USide{sx}", (0.022, UD, z1 - z0), (sx * (W / 2 - 0.011), uy, (z0 + z1) / 2), ven, rt, bv=0.003)
    box("S_UBack", (W - 0.04, 0.008, z1 - z0), (0, D / 2 - 0.004, (z0 + z1) / 2), inner, rt, bv=0)
    box("S_URoof", (W + 0.02, UD + 0.03, 0.03), (0, uy - 0.01, z1 + 0.015), ven, rt, bv=0.006)
    box("S_UShelf", (W - 0.044, UD - 0.03, 0.018), (0, uy + 0.01, 1.33), ven, rt, bv=0.002)
    fy = D / 2 - UD - 0.004
    for z, hgt in ((z0 + 0.012, 0.024), (z1 - 0.012, 0.024)):
        box("S_Rail", (W - 0.044, 0.03, hgt), (0, fy + 0.012, z), ven, rt, bv=0.002)
    for i, sx in enumerate((-1, 1)):
        gx = sx * (W / 4 - 0.012) + (0.008 if i else -0.008)
        g = box(f"Sideboard_Glass{i}", (W / 2 - 0.03, 0.004, z1 - z0 - 0.05), (gx, fy + 0.004 + i * 0.008, (z0 + z1) / 2), glass, rt, bv=0)
        box(f"S_GlassEdge{i}", (0.012, 0.012, z1 - z0 - 0.05), (gx + sx * (W / 4 - 0.02), fy + 0.004 + i * 0.008, (z0 + z1) / 2), ven, rt, bv=0.001)
    # plates standing in a row on both shelves, cups, a crystal vase
    rng = random.Random(7)
    for z in (z0 + 0.0, 1.339):
        for k in range(4):
            px = -0.42 + k * 0.1; r = rng.uniform(0.1, 0.12)
            p = revolve("S_Plate", [(0.0, 0.0), (r * 0.55, 0.0), (r * 0.6, 0.006), (r, 0.016), (r * 0.98, 0.019), (r * 0.58, 0.01), (0, 0.009)], 32, china, rt)
            p.location = (px, D / 2 - 0.035, z + r + 0.002); p.rotation_euler = (math.radians(78), 0, rng.uniform(-0.05, 0.05))
        for k in range(3):
            cx = 0.12 + k * 0.11
            revolve("S_Cup", [(0, 0.001), (0.022, 0.001), (0.03, 0.01), (0.037, 0.055), (0.035, 0.057), (0.031, 0.052), (0.024, 0.012), (0, 0.011)],
                    24, china, rt, loc=(cx, uy + 0.03 + rng.uniform(-0.02, 0.02), z + 0.002))
    revolve("S_Vase", [(0, 0.0), (0.04, 0.0), (0.045, 0.01), (0.03, 0.05), (0.028, 0.15), (0.05, 0.22), (0.047, 0.225), (0.024, 0.16),
                       (0.022, 0.055), (0, 0.05)], 16, crystal, rt, loc=(0.42, uy + 0.02, 1.339))
    revolve("S_Bowl", [(0, 0.0), (0.05, 0.0), (0.09, 0.05), (0.085, 0.052), (0.045, 0.008), (0, 0.006)], 16, crystal, rt, loc=(-0.3, uy + 0.02, z0 + 0.012))
    return rt

def build_bed(x0):
    rt = root("Bed", (x0, 0, 0))
    wood, sheet_m, blanket, ticking = role("bedwood"), role("linen"), role("blanket"), role("ticking")
    W, L = 0.9, 1.95
    for sx in (-1, 1):
        box(f"B_Rail{sx}", (0.035, L, 0.16), (sx * (W / 2 + 0.018), 0, 0.3), wood, rt, bv=0.004)
    for sy, h in ((1, 0.95), (-1, 0.62)):
        y = sy * (L / 2 + 0.03)
        for sx in (-1, 1): box("B_Post", (0.05, 0.05, h), (sx * (W / 2 + 0.03), y, h / 2), wood, rt, bv=0.006)
        box("B_Board", (W + 0.01, 0.025, h - 0.32), (0, y, 0.24 + (h - 0.32) / 2), wood, rt, bv=0.004)
        box("B_TopRail", (W + 0.12, 0.055, 0.04), (0, y, h + 0.01), wood, rt, bv=0.008)
        for k in range(3): box("B_Spindle", (0.02, 0.03, h - 0.42), (-0.25 + k * 0.25, y - sy * 0.016, 0.3 + (h - 0.42) / 2), wood, rt, bv=0.003)
    mt = pillow("B_Mattress", (W - 0.02, L - 0.03, 0.15), (0, 0, 0.455), ticking, rt, puff=0.02, sag=0.04)
    print_uv_cube(mt, 0.6)
    drape("B_Sheet", W - 0.02, L - 0.04, 0.535, 0.06, 0.03, 0.02, sheet_m, rt, seed=3, tile=0.9, fold=0.01, rumple=0.01)
    drape("B_Blanket", W - 0.01, 1.2, 0.555, 0.34, 0.38, 0.0, blanket, rt, loc=(0.04, -0.33, 0), seed=5, tile=0.7, fold=0.03, rumple=0.022)
    pw = pillow("B_Pillow", (0.6, 0.4, 0.14), (0, L / 2 - 0.27, 0.6), sheet_m, rt, puff=0.04, sag=0.03, rot=(math.radians(-12), 0, math.radians(4)))
    print_uv_cube(pw, 0.9)
    return rt

def build_tv(x0):
    rt = root("CRT_TV", (x0, 0, 0))
    ven, bezel, screen, metal, dark = role("veneer"), role("bezel"), role("screen"), role("chrome"), role("dark")
    W, D, H = 0.62, 0.30, 0.48
    box("T_Case", (W, D, H), (0, 0, H / 2), ven, rt, bv=0.012)
    box("T_Rear", (0.44, 0.2, 0.36), (0, D / 2 + 0.09, 0.22), dark, rt, bv=0.02, taper=None)
    box("T_RearCap", (0.3, 0.06, 0.24), (0, D / 2 + 0.21, 0.22), dark, rt, bv=0.015)
    box("T_Bezel", (W - 0.03, 0.02, H - 0.03), (0, -D / 2 - 0.006, H / 2), bezel, rt, bv=0.006)
    sw, sh, scx = 0.43, 0.34, -0.075
    def scr(u, v):
        x = scx + (u - 0.5) * sw; z = H / 2 + (v - 0.5) * sh
        bulge = 0.018 * (1 - (2 * u - 1) ** 2) * (1 - (2 * v - 1) ** 2)
        return (x, -D / 2 - 0.017 - bulge, z)
    sheet("T_Screen", scr, 20, 16, screen, rt)
    for i, z in enumerate((0.36, 0.28, 0.2)):
        cyl(f"T_Knob{i}", 0.02, 0.017, 0.025, (0.235, -D / 2 - 0.028, z), dark, rt, seg=18, axis=(0, -1, 0))
        box(f"T_KnobMark{i}", (0.004, 0.004, 0.016), (0.235, -D / 2 - 0.041, z + 0.008), metal, rt, bv=0)
    for k in range(8): box("T_Grille", (0.08, 0.006, 0.006), (0.235, -D / 2 - 0.017, 0.07 + k * 0.012), dark, rt, bv=0)
    box("T_Badge", (0.06, 0.004, 0.014), (scx, -D / 2 - 0.017, 0.035), metal, rt, bv=0)
    cyl("T_AntBase", 0.05, 0.045, 0.025, (0.1, 0.03, H + 0.0125), dark, rt, seg=20)
    for sx, ln in ((-1, 0.55), (1, 0.48)):
        a = Vector((0.1, 0.03, H + 0.025)); b = a + Vector((sx * ln * 0.55, 0.06 * sx, ln * 0.83))
        rod(f"T_Ant{sx}", a, b, 0.0025, metal, rt, seg=8)
        sphere(f"T_AntTip{sx}", 0.005, b, metal, rt, u=8, v=6)
    for sx in (-1, 1):
        for sy in (-1, 1): cyl("T_Foot", 0.015, 0.015, 0.012, (sx * 0.25, sy * 0.1, -0.0), dark, rt, seg=10)
    return rt

def build_tvstand(x0):
    rt = root("TV_Stand", (x0, 0, 0))
    ven, dark, paper, brass = role("veneer"), role("carcass"), role("paper"), role("brass")
    W, D, H0, H1 = 0.8, 0.42, 0.17, 0.52
    box("TS_Body", (W, D, H1 - H0), (0, 0, (H0 + H1) / 2), ven, rt, bv=0.006)
    box("TS_Niche", (W / 2 - 0.03, D - 0.03, H1 - H0 - 0.05), (W / 4, -0.02, (H0 + H1) / 2), dark, rt, bv=0)
    panel_front("TS_Door", W / 2 - 0.02, H1 - H0 - 0.03, (-W / 4, -D / 2 - 0.009, (H0 + H1) / 2), ven, rt, inset=0.035)
    cyl("TS_Knob", 0.011, 0.008, 0.02, (-0.06, -D / 2 - 0.028, (H0 + H1) / 2), brass, rt, seg=12, axis=(0, -1, 0))
    for sx in (-1, 1):
        for sy in (-1, 1):
            splay_leg("TS_Leg", (sx * (W / 2 - 0.06), sy * (D / 2 - 0.06), H0), H0, (sx * 0.04, sy * 0.03), 0.018, 0.011, ven, rt)
    rng = random.Random(2)
    for k in range(5):
        z = H0 + 0.025 + 0.004 + k * 0.007
        box("TS_Mag", (0.3, 0.22, 0.006), (W / 4 + rng.uniform(-0.02, 0.02), -0.02 + rng.uniform(-0.02, 0.02), z), paper, rt, bv=0).rotation_euler = (0, 0, rng.uniform(-0.12, 0.12))
    return rt

def build_dining(x0):
    rt = root("DiningTable", (x0, 0, 0))
    wood, cloth = role("tablewood"), role("oilcloth")
    W, L, H = 1.0, 0.65, 0.74
    box("DT_Top", (W, L, 0.025), (0, 0, H - 0.0125), wood, rt, bv=0.004)
    for sx in (-1, 1):
        box("DT_ApronX", (0.02, L - 0.1, 0.09), (sx * (W / 2 - 0.05), 0, H - 0.07), wood, rt, bv=0.002)
        box("DT_ApronY", (W - 0.1, 0.02, 0.09), (0, sx * (L / 2 - 0.05), H - 0.07), wood, rt, bv=0.002)
        for sy in (-1, 1):
            box("DT_Leg", (0.045, 0.045, H - 0.025), (sx * (W / 2 - 0.06), sy * (L / 2 - 0.06), (H - 0.025) / 2), wood, rt, bv=0.004, taper=0.7)
    drape("DT_Cloth", W, L, H + 0.002, 0.13, 0.11, 0.11, cloth, rt, seed=9, tile=0.5, fold=0.012, flare=0.05, thick=0.002, rumple=0.002)
    return rt

def build_coffee(x0):
    rt = root("CoffeeTable", (x0, 0, 0))
    ven, paper, news = role("veneer"), role("paper"), role("newsprint")
    W, D, H = 0.9, 0.5, 0.5
    box("CT_Top", (W, D, 0.022), (0, 0, H - 0.011), ven, rt, bv=0.006)
    box("CT_Shelf", (W - 0.14, D - 0.1, 0.016), (0, 0, 0.16), ven, rt, bv=0.003)
    for sx in (-1, 1):
        for sy in (-1, 1):
            splay_leg("CT_Leg", (sx * (W / 2 - 0.08), sy * (D / 2 - 0.07), H - 0.022), H - 0.022, (sx * 0.05, sy * 0.03), 0.02, 0.012, ven, rt)
    rng = random.Random(12)
    for k in range(4):
        s = box("CT_Sheet", (0.21, 0.297, 0.0012), (rng.uniform(-0.25, 0.15), rng.uniform(-0.08, 0.06), H + 0.001 + k * 0.0013), paper, rt, bv=0)
        s.rotation_euler = (0, 0, rng.uniform(-0.6, 0.6))
    n = box("CT_Paper", (0.4, 0.29, 0.004), (0.2, 0.05, H + 0.003), news, rt, bv=0); n.rotation_euler = (0, 0, 0.25)
    box("CT_Mags", (0.24, 0.3, 0.02), (0.1, 0.0, 0.18), news, rt, bv=0.002)
    return rt

def build_radiator(x0):
    rt = root("Radiator_Cast", (x0, 0, 0))
    paint, pipe = role("radpaint"), role("radpipe")
    N, sw, z0, h = 9, 0.094, 0.12, 0.58
    yc = -0.115
    x_first = -(N - 1) * sw / 2
    for i in range(N):
        x = x_first + i * sw
        for dy in (-0.034, 0.034):
            box("R_Col", (0.074, 0.04, h - 0.08), (x, yc + dy, z0 + h / 2), paint, rt, bv=0.016)
        box("R_Web", (0.04, 0.03, h - 0.12), (x, yc, z0 + h / 2), paint, rt, bv=0.006)
        for z in (z0 + 0.045, z0 + h - 0.045):
            cyl("R_Hub", 0.046, 0.046, sw - 0.004, (x, yc, z), paint, rt, seg=20, axis=(1, 0, 0), bv=0.006)
    xe = x_first - sw / 2 - 0.02; xr = -x_first + sw / 2 + 0.02
    for z in (z0 + 0.045, z0 + h - 0.045):
        cyl("R_Plug", 0.03, 0.03, 0.025, (xr, yc, z), paint, rt, seg=16, axis=(1, 0, 0), bv=0.003)
        rod("R_PipeIn", (xe, yc, z), (xe - 0.12, yc, z), 0.016, pipe, rt)
    pts = [(xe - 0.12, yc, z0 + h - 0.045), (xe - 0.2, yc, z0 + h - 0.045), (xe - 0.2, -0.035, z0 + h - 0.045), (xe - 0.2, -0.035, 2.7)]
    tube("R_Riser", pts, 0.016, pipe, rt)
    pts = [(xe - 0.12, yc, z0 + 0.045), (xe - 0.26, yc, z0 + 0.045), (xe - 0.26, -0.035, z0 + 0.045), (xe - 0.26, -0.035, 0.0)]
    tube("R_Return", pts, 0.016, pipe, rt)
    cyl("R_Valve", 0.026, 0.026, 0.05, (xe - 0.07, yc, z0 + h - 0.045), pipe, rt, seg=14, axis=(1, 0, 0))
    cyl("R_ValveWheel", 0.03, 0.03, 0.01, (xe - 0.07, yc, z0 + h + 0.0), pipe, rt, seg=14)
    for sx in (-1, 1):
        box("R_Bracket", (0.02, 0.12, 0.012), (sx * 0.25, -0.06, z0 + h - 0.09), pipe, rt, bv=0.002)
    return rt

def build_wallcarpet(x0):
    rt = root("WallCarpet", (x0, 0, 0))
    wool, fringe = role("carpet"), role("fringe")
    W, H = 2.0, 1.4
    rng = random.Random(6); nails = 5
    def fn(u, v):
        x = (u - 0.5) * W; z = (v - 0.5) * H
        top_sag = 0.012 * abs(math.sin(u * math.pi * (nails - 1))) * max(0.0, v - 0.75) / 0.25   # between the nails
        wave = 0.008 * math.sin(u * 9 + 1.3) * math.sin(v * 5) + 0.012 * (1 - v) ** 2 * math.sin(u * 14)
        return (x, -0.012 - wave - 0.004 * (1 - v), z - top_sag)
    sheet("WC_Carpet", fn, 60, 42, wool, rt, thick=0.008)
    for sx in (-1, 1):   # the fringe on the short sides
        for k in range(36):
            z = -H / 2 + 0.02 + (H - 0.04) * k / 35; x = sx * (W / 2)
            rod("WC_Fringe", (x, -0.013, z), (x + sx * rng.uniform(0.04, 0.06), -0.015 - rng.uniform(0, 0.006), z - rng.uniform(0.0, 0.025)), 0.0025, fringe, rt, seg=5)
    for k in range(nails): cyl("WC_Nail", 0.006, 0.006, 0.01, ((k / (nails - 1) - 0.5) * (W - 0.06), -0.022, H / 2 - 0.025), role("nail"), rt, seg=8, axis=(0, 1, 0))
    return rt

def build_rug(x0):
    rt = root("Rug_Floor", (x0, 0, 0))
    wool, fringe = role("carpet"), role("fringe")
    W, L = 2.2, 1.5
    def fn(u, v):
        x = (u - 0.5) * W; y = (v - 0.5) * L
        z = 0.006 + 0.004 * math.sin(u * 11) * math.sin(v * 7)
        c = max(0.0, (u - 0.8) / 0.2) * max(0.0, (v - 0.75) / 0.25)   # a corner turned up
        z += 0.12 * c ** 1.6
        x -= 0.06 * c ** 2; y -= 0.05 * c ** 2
        b = math.exp(-(((u - 0.3) / 0.12) ** 2 + ((v - 0.4) / 0.1) ** 2)) * 0.02   # a ruck
        return (x, y, z + b)
    sheet("RF_Rug", fn, 66, 46, wool, rt, thick=0.01)
    rng = random.Random(9)
    for sx in (-1, 1):
        for k in range(40):
            y = -L / 2 + 0.02 + (L - 0.04) * k / 39; x = sx * W / 2
            if sx > 0 and y > L * 0.25: continue
            rod("RF_Fringe", (x, y, 0.004), (x + sx * rng.uniform(0.05, 0.07), y + rng.uniform(-0.01, 0.01), 0.002), 0.0028, fringe, rt, seg=5)
    return rt

def build_curtains(x0):
    rt = root("Curtains", (x0, 0, 0))
    cloth, rodm = role("curtain"), role("brass")
    W, top = 2.3, 2.45
    rod("C_Rod", (-W / 2, -0.09, top + 0.03), (W / 2, -0.09, top + 0.03), 0.011, rodm, rt)
    for sx in (-1, 1):
        sphere("C_Finial", 0.022, (sx * (W / 2 + 0.015), -0.09, top + 0.03), rodm, rt)
        rod("C_Bracket", (sx * (W / 2 - 0.07), 0.0, top + 0.03), (sx * (W / 2 - 0.07), -0.09, top + 0.03), 0.006, rodm, rt)
        cyl("C_Plate", 0.025, 0.025, 0.008, (sx * (W / 2 - 0.07), -0.004, top + 0.03), rodm, rt, seg=12, axis=(0, 1, 0))
        pw = 0.62; inner = sx * 0.42; outer = sx * (W / 2 - 0.02)
        ph = random.Random(sx + 4).uniform(0, 3)
        def fn(u, v, sx=sx, inner=inner, outer=outer, ph=ph):
            if sx > 0: u = 1 - u   # keep the front facing the room
            z = 0.02 + v * (top - 0.02)
            gather = 1.0 - 0.35 * (1 - v) ** 2   # drawn in towards the bottom, like it is tied back loosely
            x = outer + (inner - outer) * u * (0.75 + 0.25 * gather)
            pleat = 0.035 * math.sin(u * 2 * math.pi * 7 + ph) * (0.6 + 0.4 * v)
            return (x, -0.09 - 0.035 + pleat + 0.03 * (1 - v) * math.sin(u * 3 + ph), z)
        sheet(f"C_Drape{sx}", fn, 70, 50, cloth, rt, thick=0.004, uvscale=(pw * 2.2 / 0.6, top / 0.6))
        for k in range(8):
            x = outer + (inner - outer) * k / 7
            cyl("C_Ring", 0.016, 0.016, 0.005, (x, -0.09, top + 0.03), rodm, rt, seg=12, axis=(1, 0, 0))
    return rt

def build_chandelier(x0):
    rt = root("Chandelier", (x0, 0, 0))
    brass, glass = role("brass"), role("globe")
    cyl("CH_Cup", 0.065, 0.04, 0.035, (0, 0, -0.0175), brass, rt, seg=24)
    rod("CH_Stem", (0, 0, -0.03), (0, 0, -0.36), 0.008, brass, rt)
    revolve("CH_Body", [(0, 0.0), (0.03, 0.0), (0.045, -0.03), (0.04, -0.06), (0.02, -0.09), (0.012, -0.12), (0, -0.125)], 24, brass, rt, loc=(0, 0, -0.34))
    for i in range(3):
        a = i / 3 * math.tau + 0.3
        c, s = math.cos(a), math.sin(a)
        pts = [(c * 0.03, s * 0.03, -0.39), (c * 0.12, s * 0.12, -0.44), (c * 0.2, s * 0.2, -0.42), (c * 0.235, s * 0.235, -0.36)]
        tube(f"CH_Arm{i}", [tuple(p) for p in smooth_path(pts, 16)], 0.0065, brass, rt)
        bx, by = c * 0.235, s * 0.235
        revolve(f"CH_Holder{i}", [(0, 0.0), (0.03, 0.0), (0.04, 0.025), (0.036, 0.03), (0, 0.03)], 20, brass, rt, loc=(bx, by, -0.365))
        revolve(f"Chandelier_Glass{i}", [(0.0, 0.0), (0.03, 0.0), (0.055, 0.03), (0.075, 0.08), (0.078, 0.12), (0.065, 0.16),
                                          (0.045, 0.185), (0.044, 0.19), (0, 0.19)], 28, glass, rt, loc=(bx, by, -0.335))
    return rt

# ---- FlatDecor pieces (local origin given by the set spot)

def build_flatdecor(x0):
    rt = root("FlatDecor", (x0, 0, 0))
    spots = {n: i * SET_GAP for i, n in enumerate(KH_SETS["FlatDecor"])}
    wood, face, blackm, frame, pic, paperc, nailm = role("veneer"), role("clockface"), role("dark"), role("frame"), role("portrait"), role("calendar"), role("nail")
    # WallClock: wall plane at y = 0, centre at its origin
    x = spots["WallClock"]
    cyl("WallClock_Rim", 0.16, 0.16, 0.045, (x, -0.0225, 0), wood, rt, seg=48, axis=(0, 1, 0), bv=0.008)
    f = cyl("WallClock_Face", 0.14, 0.14, 0.004, (x, -0.046, 0), face, rt, seg=48, axis=(0, 1, 0))
    me = f.data; uv = me.uv_layers.new(name="Print")
    for lp in me.loops:
        co = me.vertices[lp.vertex_index].co; uv.data[lp.index].uv = (0.5 + co.x / 0.30, 0.5 + co.y / 0.30)
    box("WallClock_HourHand", (0.008, 0.002, 0.07), (x + 0.015, -0.05, 0.02), blackm, rt, bv=0).rotation_euler = (0, math.radians(-40), 0)
    box("WallClock_MinHand", (0.005, 0.002, 0.11), (x - 0.03, -0.052, 0.035), blackm, rt, bv=0).rotation_euler = (0, math.radians(35), 0)
    cyl("WallClock_Pin", 0.008, 0.008, 0.008, (x, -0.054, 0), blackm, rt, seg=10, axis=(0, 1, 0))
    # Portrait: frame and print, centre at origin, wall at y = 0, tilted forward a little off its nail
    x = spots["Portrait"]; pw, ph = 0.42, 0.54
    pv = root("Portrait_Pivot"); pv.parent = rt; pv.location = (x, -0.02, 0); pv.rotation_euler = (math.radians(-4), 0, 0)
    for sz in (-1, 1):
        box("Portrait_FrameH", (pw, 0.03, 0.045), (0, 0, sz * (ph / 2 - 0.0225)), frame, pv, bv=0.006)
        box("Portrait_FrameV", (0.045, 0.03, ph - 0.09), (sz * (pw / 2 - 0.0225), 0, 0), frame, pv, bv=0.006)
    p = box("Portrait_Picture", (pw - 0.08, 0.004, ph - 0.08), (0, 0.004, 0), pic, pv, bv=0)
    print_uv_planar(p, 0, 2, ((-(pw - 0.08) / 2, (pw - 0.08) / 2), (-(ph - 0.08) / 2, (ph - 0.08) / 2)))
    box("Portrait_Back", (pw - 0.02, 0.004, ph - 0.02), (0, 0.013, 0), wood, pv, bv=0)
    # Calendar: hangs from a nail, the bottom curling off the wall
    x = spots["Calendar"]; cw, chh = 0.3, 0.45
    def cal(u, v):
        return (x + (u - 0.5) * cw, -0.004 - 0.03 * (1 - v) ** 3 - 0.004 * math.sin(u * 3), (v - 0.5) * chh)
    sheet("Calendar_Sheet", cal, 10, 16, paperc, rt, thick=0.0015)
    cyl("Calendar_Nail", 0.004, 0.004, 0.015, (x, -0.008, chh / 2 + 0.01), nailm, rt, seg=8, axis=(0, 1, 0))
    # Radio: a tube radio, on its feet
    x = spots["Radio"]; rw, rd, rh = 0.46, 0.22, 0.29
    box("Radio_Case", (rw, rd, rh), (x, 0, 0.012 + rh / 2), wood, rt, bv=0.02)
    g = box("Radio_Grille", (rw - 0.06, 0.006, 0.13), (x - 0.0, -rd / 2 - 0.001, 0.012 + rh - 0.1), role("grille"), rt, bv=0.002)
    print_uv_cube(g, 0.08)
    d = box("Radio_Dial", (rw - 0.08, 0.006, 0.05), (x, -rd / 2 - 0.002, 0.012 + 0.11), role("dial"), rt, bv=0.001)
    print_uv_planar(d, 0, 2, ((-(rw - 0.08) / 2, (rw - 0.08) / 2), (-0.025, 0.025)))
    for k, kx in enumerate((-0.16, -0.08, 0.08, 0.16)):
        cyl("Radio_Knob", 0.018, 0.015, 0.022, (x + kx, -rd / 2 - 0.012, 0.012 + 0.045), blackm, rt, seg=16, axis=(0, -1, 0))
    for sx in (-1, 1):
        for sy in (-1, 1): box("Radio_Foot", (0.04, 0.03, 0.012), (x + sx * 0.18, sy * 0.07, 0.006), blackm, rt, bv=0.003)
    # Ashtray: heavy amber glass, butts and ash
    x = spots["Ashtray"]
    revolve("Ashtray_Bowl", [(0, 0.0), (0.06, 0.0), (0.065, 0.004), (0.068, 0.03), (0.06, 0.032), (0.05, 0.012), (0, 0.012)], 24, role("amber"), rt, loc=(x, 0, 0))
    rng = random.Random(1)
    for k in range(7):
        a = rng.uniform(0, math.tau); r = rng.uniform(0.0, 0.035)
        c = Vector((x + math.cos(a) * r, math.sin(a) * r, 0.016 + k * 0.002)); dd = Vector((math.cos(a + 1.7), math.sin(a + 1.7), rng.uniform(0.0, 0.5))).normalized()
        rod("Ashtray_Butt", c, c + dd * rng.uniform(0.025, 0.04), 0.0042, role("butt"), rt, seg=8)
    cyl("Ashtray_Ash", 0.045, 0.04, 0.004, (x, 0, 0.014), role("ash"), rt, seg=16)
    # PottedPlant: a clay pot, a leggy geranium gone half dry
    x = spots["PottedPlant"]
    revolve("PottedPlant_Pot", [(0.0, 0.002), (0.055, 0.002), (0.058, 0.0), (0.075, 0.13), (0.082, 0.134), (0.083, 0.15), (0.074, 0.15),
                                (0.07, 0.136), (0.0, 0.13)], 28, role("pot"), rt, loc=(x, 0, 0))
    cyl("PottedPlant_Soil", 0.07, 0.07, 0.004, (x, 0, 0.134), role("soil"), rt, seg=24)
    cyl("PottedPlant_Saucer", 0.09, 0.08, 0.015, (x, 0, 0.0075), role("pot"), rt, seg=28)
    bm = bmesh.new(); rng = random.Random(5); stems = []
    for k in range(7):
        a = rng.uniform(0, math.tau); lean = rng.uniform(0.15, 0.6); ln = rng.uniform(0.18, 0.38)
        base = Vector((x + math.cos(a) * 0.02, math.sin(a) * 0.02, 0.135))
        tip = base + Vector((math.cos(a) * lean * ln, math.sin(a) * lean * ln, ln))
        mid = (base + tip) / 2 + Vector((0, 0, 0.03))
        path = smooth_path([tuple(base), tuple(mid), tuple(tip)], 10); stems.append(path)
        for i in (4, 6, 8, 9):
            fwd = Vector((math.cos(a + rng.uniform(-1.5, 1.5)), math.sin(a + rng.uniform(-1.5, 1.5)), rng.uniform(-0.2, 0.5))).normalized()
            leaf_mesh(bm, path[i], fwd, Vector((0, 0, 1)), rng.uniform(0.04, 0.065))
    lv = from_bm("PottedPlant_Leaves", bm, role("leaf"), rt)
    sm = lv.modifiers.new("Solid", "SOLIDIFY"); sm.thickness = 0.001
    for k, path in enumerate(stems): tube(f"PottedPlant_Stem{k}", [tuple(p) for p in path], 0.0028, role("stem"), rt)
    # Kettle: a red enamel teapot-kettle
    x = spots["Kettle"]; en = role("enamel")
    revolve("Kettle_Body", [(0, 0.0), (0.075, 0.0), (0.09, 0.02), (0.095, 0.08), (0.085, 0.13), (0.06, 0.16), (0.045, 0.165), (0, 0.165)], 32, en, rt, loc=(x, 0, 0))
    revolve("Kettle_Lid", [(0, 0.0), (0.05, 0.0), (0.045, 0.012), (0.012, 0.02), (0.014, 0.035), (0, 0.04)], 24, en, rt, loc=(x, 0, 0.163))
    tube("Kettle_Spout", [tuple(p) for p in smooth_path([(x - 0.07, 0, 0.05), (x - 0.12, 0, 0.09), (x - 0.15, 0, 0.15)], 10)], 0.012, en, rt)
    tube("Kettle_Handle", [tuple(p) for p in smooth_path([(x - 0.05, 0, 0.17), (x - 0.03, 0, 0.25), (x + 0.03, 0, 0.25), (x + 0.05, 0, 0.17)], 14)], 0.006, role("chrome"), rt)
    return rt

def build_flatfurniture(x0):
    rt = root("FlatFurniture", (x0, 0, 0))
    spots = {n: i * SET_GAP * 1.4 for i, n in enumerate(KH_SETS["FlatFurniture"])}
    ven, brass, paint, shade, metal = role("veneer"), role("brass"), role("stoolpaint"), role("shade"), role("chrome")
    # Nightstand
    x = spots["Nightstand"]; W, D, H = 0.45, 0.4, 0.6
    box("Nightstand_Body", (W, D, H - 0.06), (x, 0, 0.06 + (H - 0.06) / 2), ven, rt, bv=0.005)
    box("Nightstand_Plinth", (W - 0.04, D - 0.04, 0.06), (x, 0.01, 0.03), role("dark"), rt, bv=0.003)
    panel_front("Nightstand_Drawer", W - 0.03, 0.12, (x, -D / 2 - 0.009, H - 0.08), ven, rt, inset=0.025)
    panel_front("Nightstand_Door", W - 0.03, 0.36, (x, -D / 2 - 0.009, 0.07 + 0.19), ven, rt, inset=0.035)
    cyl("Nightstand_Knob", 0.012, 0.009, 0.02, (x, -D / 2 - 0.028, H - 0.08), brass, rt, seg=12, axis=(0, -1, 0))
    cyl("Nightstand_Knob2", 0.012, 0.009, 0.02, (x + 0.15, -D / 2 - 0.028, 0.3), brass, rt, seg=12, axis=(0, -1, 0))
    # Stool
    x = spots["Stool"]; S, H = 0.32, 0.45
    box("Stool_Seat", (S, S, 0.03), (x, 0, H - 0.015), paint, rt, bv=0.006)
    for sx in (-1, 1):
        for sy in (-1, 1):
            box("Stool_Leg", (0.035, 0.035, H - 0.03), (x + sx * 0.12, sy * 0.12, (H - 0.03) / 2), paint, rt, bv=0.004, taper=0.85)
        box("Stool_RailX", (0.24, 0.02, 0.04), (x, sx * 0.12, 0.15), paint, rt, bv=0.003)
        box("Stool_RailY", (0.02, 0.24, 0.04), (x + sx * 0.12, 0, 0.32), paint, rt, bv=0.003)
    box("Stool_Slot", (0.1, 0.022, 0.032), (x, 0, H - 0.015), role("dark"), rt, bv=0.003)
    # FloorLamp
    x = spots["FloorLamp"]
    cyl("FloorLamp_Base", 0.15, 0.16, 0.025, (x, 0, 0.0125), metal, rt, seg=32, bv=0.004)
    cyl("FloorLamp_Boss", 0.03, 0.04, 0.05, (x, 0, 0.05), metal, rt, seg=20)
    rod("FloorLamp_Pole", (x, 0, 0.05), (x, 0, 1.42), 0.011, brass, rt)
    rod("FloorLamp_Spider", (x - 0.1, 0, 1.4), (x + 0.1, 0, 1.4), 0.003, metal, rt, seg=6)
    o = revolve("FloorLamp_Shade", [(0.21, 1.26), (0.215, 1.255), (0.15, 1.52), (0.145, 1.525), (0.14, 1.52), (0.205, 1.26)], 36, shade, rt, loc=(x, 0, 0))
    o.rotation_euler = (math.radians(3), math.radians(-2), 0)
    rng = random.Random(3)
    for k in range(48):   # a tasselled fringe round the rim
        a = k / 48 * math.tau
        p = Vector((x + math.cos(a) * 0.212, math.sin(a) * 0.212, 1.262))
        rod("FloorLamp_Tassel", p, p + Vector((0, 0, -rng.uniform(0.04, 0.055))), 0.0022, shade, rt, seg=4)
    return rt

# ------------------------------------------------------------------ materials

def _wood(dark, light, rough=0.3, **kw):
    d = dict(base=(dark, light), rough=rough, metal=0.0, wood=True, scratch=0.6, chips=0.15, chip_scale=14,
             wear=((0.32, 0.2, 0.11), 0.75, 0.0, 1.0), dirt=((0.035, 0.025, 0.015), 0.85, 0.0, 0.85), dust=1.0,
             bottom=0.5, edge_radius=0.003, ao_dist=0.12)
    d.update(kw); return d

def _plain(col, rough, metal=0.0, wear_col=(0.4, 0.36, 0.3), amount=0.8, dust=0.6, **kw):
    d = dict(base=(col, tuple(c * 0.85 for c in col)), rough=rough, metal=metal,
             wear=(wear_col, 0.85, 0.0, amount), dirt=((0.05, 0.035, 0.022), 0.9, 0.0, 0.85), dust=dust, edge_radius=0.003, ao_dist=0.06)
    d.update(kw); return d

def _print(rough=0.95, dust=0.5, wrinkle=0.0, **kw):
    d = dict(base=((1.0, 1.0, 1.0), (0.9, 0.88, 0.85)), rough=rough, metal=0.0, image=None,
             wear=((0.5, 0.42, 0.33), 0.95, 0.0, 0.5), dirt=((0.08, 0.05, 0.035), 0.95, 0.0, 0.8), dust=dust,
             wrinkle=wrinkle, bump=0.35, edge_radius=0.01, ao_dist=0.1)
    d.update(kw); return d

CFG_COMMON = {
    "veneer": _wood((0.15, 0.07, 0.032), (0.085, 0.038, 0.017), 0.28),
    "carcass": _plain((0.2, 0.15, 0.1), 0.85, dust=0.9),
    "bedwood": _wood((0.2, 0.11, 0.05), (0.12, 0.065, 0.03), 0.4),
    "tablewood": _wood((0.32, 0.22, 0.12), (0.22, 0.15, 0.08), 0.6),
    "dark": _plain((0.04, 0.03, 0.025), 0.6, dust=0.8),
    "brass": dict(base=((0.55, 0.4, 0.18), (0.42, 0.3, 0.13)), rough=0.35, metal=1.0, chips=0.3, chip_scale=20,
                  wear=((0.2, 0.15, 0.08), 0.8, 0.5, 1.0), dirt=((0.1, 0.08, 0.04), 0.9, 0.3, 1.0), dust=0.6, edge_radius=0.002, ao_dist=0.03),
    "chrome": dict(base=((0.6, 0.6, 0.6), (0.5, 0.5, 0.5)), rough=0.25, metal=1.0, chips=0.4, chip_scale=24,
                   wear=((0.2, 0.1, 0.05), 0.85, 0.3, 1.0), dirt=((0.12, 0.07, 0.03), 0.9, 0.2, 1.0), dust=0.5, edge_radius=0.002, ao_dist=0.03),
    "glass": dict(base=((0.3, 0.33, 0.32), (0.25, 0.28, 0.27)), rough=0.1, metal=0.0,
                  wear=((0.4, 0.4, 0.38), 0.6, 0.0, 0.2), dirt=((0.2, 0.18, 0.14), 0.7, 0.0, 0.6), dust=0.9, ao_dist=0.05),
    "china": _plain((0.82, 0.8, 0.74), 0.2, wear_col=(0.3, 0.28, 0.25), amount=0.9, chips=0.4, chip_scale=18),
    "crystal": _plain((0.55, 0.58, 0.6), 0.1, amount=0.3, dust=0.8),
    "paper": _print(0.9, image=None, wrinkle=0.15),
    "newsprint": _plain((0.62, 0.6, 0.54), 0.9, dust=0.6, wrinkle=0.3),
    "linen": _plain((0.62, 0.6, 0.55), 0.95, wear_col=(0.45, 0.4, 0.3), dust=0.4, wrinkle=0.45, bump=0.45, bottom=0.3),
    "ticking": _plain((0.5, 0.48, 0.42), 0.95, wear_col=(0.35, 0.26, 0.16), dust=0.5, wrinkle=0.3, bottom=0.4,
                      chips=0.4, chip_scale=4),
    "blanket": _print(0.98, 0.4, 0.4),
    "oilcloth": _print(0.35, 0.6, 0.1),
    "curtain": _print(0.95, 0.5, 0.2, bottom=0.5),
    "carpet": _print(1.0, 0.7, 0.1, bump=0.5),
    "fringe": _plain((0.55, 0.48, 0.36), 1.0, dust=0.5),
    "nail": _plain((0.3, 0.3, 0.3), 0.5, metal=1.0),
    "bezel": _plain((0.06, 0.055, 0.05), 0.45, dust=0.9),
    "screen": dict(base=((0.03, 0.04, 0.035), (0.025, 0.03, 0.028)), rough=0.08, metal=0.0,
                   wear=((0.2, 0.2, 0.2), 0.3, 0.0, 0.1), dirt=((0.14, 0.13, 0.11), 0.6, 0.0, 0.7), dust=1.0, ao_dist=0.05),
    "radpaint": dict(base=((0.66, 0.62, 0.52), (0.58, 0.54, 0.44)), rough=0.45, metal=0.0, chips=0.55, chip_scale=8,
                     wear=((0.24, 0.11, 0.05), 0.85, 0.3, 1.0), dirt=((0.16, 0.09, 0.04), 0.9, 0.1, 1.0), dust=1.0,
                     bottom=0.6, wet=0.2, edge_radius=0.004, ao_dist=0.05),
    "radpipe": dict(base=((0.5, 0.47, 0.4), (0.42, 0.4, 0.34)), rough=0.5, metal=0.2, chips=0.7, chip_scale=10,
                    wear=((0.25, 0.11, 0.04), 0.85, 0.3, 1.0), dirt=((0.15, 0.08, 0.03), 0.9, 0.1, 1.0), dust=0.8, edge_radius=0.002, ao_dist=0.04),
    "globe": _plain((0.78, 0.74, 0.62), 0.35, amount=0.3, dust=1.0),
    "clockface": _print(0.4, 0.6),
    "frame": _wood((0.2, 0.12, 0.05), (0.1, 0.06, 0.025), 0.35),
    "portrait": _print(0.5, 0.6),
    "calendar": _print(0.85, 0.5, 0.25),
    "grille": _plain((0.42, 0.34, 0.22), 0.95, dust=0.8, wrinkle=0.2),
    "dial": _print(0.25, 0.5),
    "amber": _plain((0.35, 0.18, 0.05), 0.12, amount=0.4, dust=0.6),
    "butt": _plain((0.7, 0.62, 0.45), 0.9, wear_col=(0.6, 0.35, 0.12), amount=1.0, dust=0.3),
    "ash": _plain((0.25, 0.24, 0.22), 1.0, dust=0.2),
    "pot": _plain((0.48, 0.21, 0.1), 0.88, wear_col=(0.62, 0.55, 0.45), dust=0.5),
    "soil": _plain((0.05, 0.035, 0.025), 1.0, dust=0.3),
    "leaf": dict(base=((0.12, 0.2, 0.06), (0.24, 0.22, 0.08)), rough=0.55, metal=0.0,
                 wear=((0.36, 0.26, 0.09), 0.8, 0.0, 1.0), dirt=((0.1, 0.07, 0.03), 0.85, 0.0, 0.6), dust=0.6, edge_radius=0.003, ao_dist=0.03),
    "stem": _plain((0.2, 0.22, 0.1), 0.7, dust=0.3),
    "enamel": _plain((0.48, 0.06, 0.04), 0.25, wear_col=(0.06, 0.05, 0.05), amount=1.0, chips=0.6, chip_scale=12, dust=0.6),
    "stoolpaint": _plain((0.42, 0.34, 0.22), 0.55, wear_col=(0.3, 0.2, 0.1), amount=1.0, chips=0.6, chip_scale=10, dust=0.8, scratch=0.5),
    "shade": _plain((0.6, 0.45, 0.28), 0.95, dust=1.0, wrinkle=0.15),
}

BUILDERS = {"Wardrobe": build_wardrobe, "Sideboard": build_sideboard, "Bed": build_bed, "CRT_TV": build_tv, "TV_Stand": build_tvstand,
            "DiningTable": build_dining, "CoffeeTable": build_coffee, "Radiator_Cast": build_radiator, "WallCarpet": build_wallcarpet,
            "Rug_Floor": build_rug, "Curtains": build_curtains, "Chandelier": build_chandelier, "FlatDecor": build_flatdecor,
            "FlatFurniture": build_flatfurniture}

def _textures():
    """Print images per role; the two carpets get their own role configs (different images)."""
    imgs = {"blanket": tex_blanket(), "oilcloth": tex_oilcloth(), "curtain": tex_curtain(), "portrait": tex_portrait(),
            "calendar": tex_calendar(), "clockface": tex_clock(), "dial": tex_dial(), "paper": tex_paper()}
    return imgs, tex_carpet("Khrush_WallCarpet", PAL_WALL, seed=2), tex_carpet("Khrush_Rug", PAL_RUG, W=1600, H=1090, seed=7)

# ------------------------------------------------------------------ run

def build_khrush(props=None):
    clear_scene(); ROLES.clear()
    props = props or KH_PROPS
    imgs, wall_img, rug_img = _textures()
    x = 0.0
    for p in props:
        BUILDERS[p](x); x += 3.0 if p not in KH_SETS else 7.0
    for p in props:
        cfg = {k: dict(v) for k, v in CFG_COMMON.items()}
        for k, im in imgs.items(): cfg[k]["image"] = im
        cfg["carpet"]["image"] = wall_img if p == "WallCarpet" else rug_img
        for o in meshes_of(p):
            if "Print" not in o.data.uv_layers: print_uv_const(o)
        assign_materials(p, cfg)
    apply_modifiers()
    for p in props:
        for o in meshes_of(p):
            if "Print" in o.data.uv_layers: o.data.uv_layers.active = o.data.uv_layers["Print"]
    return "built: " + ", ".join(f"{p}={len(meshes_of(p))}" for p in props)

def bake_khrush(props=None):
    bake_in_background([(p, KH_RES[p]) for p in (props or KH_PROPS)], BAKE_LOG)

def _export_kh(p):
    rt = bpy.data.objects[p]; loc = rt.location.copy(); rt.location = (0, 0, 0)
    objs = [rt] + list(rt.children_recursive)
    with bpy.context.temp_override(**ctx(), selected_objects=objs, active_object=rt):
        bpy.ops.object.select_all(action='DESELECT')
        for o in objs: o.select_set(True)
        bpy.ops.export_scene.fbx(filepath=os.path.join(FBX_OUT, p + ".fbx"), use_selection=True,
                                 apply_scale_options='FBX_SCALE_UNITS', bake_space_transform=True,
                                 object_types={'EMPTY', 'MESH'}, use_mesh_modifiers=True, mesh_smooth_type='FACE',
                                 path_mode='STRIP', embed_textures=False, add_leaf_bones=False)
    rt.location = loc

def finish_khrush(props=None):
    out = []; os.makedirs(FBX_OUT, exist_ok=True)
    for p in (props or KH_PROPS):
        groups = origins = None
        if p in KH_SETS:
            names = KH_SETS[p]; gap = SET_GAP * (1.4 if p == "FlatFurniture" else 1.0)
            groups = {n: [o.name for o in meshes_of(p) if o.name.startswith(n + "_")] for n in names}
            origins = {n: (i * gap, 0, 0) for i, n in enumerate(names)}
        elif p in ("Sideboard", "Chandelier"):
            groups = {p + "_Glass": [o.name for o in meshes_of(p) if o.name.startswith(p + "_Glass")], p + "_Mesh": None}
        made = finalize(p, groups, origins); _export_kh(p); out.append(f"{p}: {made}")
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(PROJECT, "Blender", "Props_Khrushchev.blend"))
    return "; ".join(out)
