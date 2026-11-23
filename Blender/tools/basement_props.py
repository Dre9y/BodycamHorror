"""Interior props for the basement (run after propkit.py, basement_textures.py and basement_kit.py; see basement_kit).

props_a: Barrel, Barrel_Open, Shelf_Wood, Shelf_Metal, Cabinet_Metal, Crate, Crate_Open, Pallet
props_b: Radiator_Basement, Bucket, PaintCans, Fusebox, Lamp_Caged, Door_Bunker, Window_Basement, GasCylinder
props_c: Chair_Broken, Workbench, Tyre, CementBags, Planks_Pile, Bricks, Mattress, Trash_Set

Wall pieces: Fusebox's back is at y=0 (the wall), Lamp_Caged hangs from its origin at the ceiling, Door_Bunker and
Window_Basement fit Wall_Door_2m / Wall_Window_2m (origin in the wall's middle, on the opening's sill).
Sets (PaintCans, Bricks, Trash_Set) export one mesh per piece, each with its origin at its own foot.
"""
import bpy, bmesh, math, random
from mathutils import Vector, Matrix, Euler, noise

def rot_obj(o, rx=0, ry=0, rz=0):
    o.rotation_mode = 'XYZ'; o.rotation_euler = (math.radians(rx), math.radians(ry), math.radians(rz)); return o

def pillow(o, hx, hy, hz, p=4.0, floor=0.15):
    """Round a subdivided box (centred, +-hz) into a stuffed sack / mattress."""
    for v in o.data.vertices:
        fx = min(abs(v.co.x) / hx, 1); fy = min(abs(v.co.y) / hy, 1)
        k = max((1 - fx ** p) * (1 - fy ** p), 0) ** 0.5
        v.co.z *= floor + (1 - floor) * k
        v.co.x *= 1 - 0.04 * (1 - abs(v.co.z) / hz); v.co.y *= 1 - 0.04 * (1 - abs(v.co.z) / hz)
    return o

def grid_box(name, size, loc, mat, parent, cuts=6, smooth=True):
    o = box(name, size, loc, mat, parent, bv=0); subdivide(o, cuts)
    for p in o.data.polygons: p.use_smooth = smooth
    return o

def wrap_text(o, R, z0, a0=0.0):
    """Lay a flat text mesh (made in XY) onto a cylinder of radius R about Z, centred on angle a0 (0 = -Y)."""
    me = o.data
    for v in me.vertices:
        x, y = v.co.x, v.co.y
        a = x / R + a0
        v.co = Vector((R * math.sin(a), -R * math.cos(a), z0 + y))
    o.location = (0, 0, 0); o.rotation_euler = (0, 0, 0)
    return o

def line_prof(r0, z0, r1, z1, n):
    return [(r0 + (r1 - r0) * k / n, z0 + (z1 - z0) * k / n) for k in range(1, n + 1)]

# ================================================================== props_a

def build_barrel(name="Barrel", open_top=False, role_name="drum"):
    rt = root(name); m = role(role_name)
    R, H = 0.286, 0.88
    prof = [(0, 0.012), (R - 0.03, 0.012), (R - 0.012, 0.004), (R - 0.004, 0.0), (R + 0.003, 0.006), (R + 0.004, 0.022), (R, 0.035)]
    z = 0.035
    for zc in (0.29, 0.59):
        prof += line_prof(R, z, R, zc - 0.03, 4)
        prof += [(R + 0.006, zc - 0.018), (R + 0.014, zc - 0.006), (R + 0.014, zc + 0.006), (R + 0.006, zc + 0.018), (R, zc + 0.03)]
        z = zc + 0.03
    prof += line_prof(R, z, R, H - 0.035, 4)
    prof += [(R + 0.004, H - 0.022), (R + 0.003, H - 0.006), (R - 0.004, H)]
    if open_top:
        prof += [(R - 0.009, H - 0.004), (R - 0.008, H - 0.03)] + line_prof(R - 0.008, H - 0.03, R - 0.008, 0.03, 10) + [(0, 0.03)]
    else:
        prof += [(R - 0.012, H - 0.004), (R - 0.03, H - 0.012), (R * 0.5, H - 0.012), (0, H - 0.012)]
    o = lathe(name + "_Shell", prof, m, rt, seg=56)
    rnd = random.Random(len(name) * 7 + open_top)
    for _ in range(3 if open_top else 2):
        a = rnd.uniform(0, 2 * math.pi); zz = rnd.uniform(0.15, 0.75)
        dent(o, (R * math.cos(a), R * math.sin(a), zz), rnd.uniform(0.08, 0.15), rnd.uniform(0.01, 0.025))
    if open_top:
        dent(o, (R, 0, H), 0.12, 0.03)
    jitter(o, 0.003, 4.0, 2.0 + open_top)
    if not open_top:
        cyl(name + "_Bung", 0.03, 0.03, 0.014, (0.17, 0.0, H - 0.008), m, rt, seg=6, bv=0.002)
        cyl(name + "_BungS", 0.02, 0.02, 0.012, (-0.18, 0.07, H - 0.008), m, rt, seg=16, bv=0.002)
    return rt

def build_barrel_open(): return build_barrel("Barrel_Open", True, "drum_g")

def build_shelf_wood():
    rt = root("Shelf_Wood"); w = role("wood")
    W, D, H = 1.2, 0.45, 2.0
    for x in (-W / 2 + 0.025, W / 2 - 0.025):
        for y in (-D / 2 + 0.025, D / 2 - 0.025):
            plank("Shelf_Post", (0.05, 0.05, H), (x, y, H / 2), w, rt, bv=0.004)
    levels = [0.14, 0.62, 1.1, 1.58, 1.96]
    rnd = random.Random(3)
    for li, z in enumerate(levels):
        for x in (-W / 2 + 0.06, W / 2 - 0.06):
            plank("Shelf_Rail", (0.035, D, 0.045), (x, 0, z - 0.035), w, rt, bv=0.003)
        for k in range(3):
            if li == 2 and k == 0: continue                                    # a board gone
            y = -D / 2 + 0.075 + k * 0.15
            o = plank("Shelf_Board", (W + 0.02, 0.135, 0.022), (rnd.uniform(-0.02, 0.02), y, z), w, rt, bv=0.003)
            if li == 3 and k == 1:                                             # this one sags
                for v in o.data.vertices: v.co.z -= 0.025 * math.cos(math.pi * v.co.x / W)
    # a broken board end lying across the second level, and a back X brace
    o = plank("Shelf_Broken", (0.5, 0.12, 0.022), (-0.3, -0.12, 1.12), w, rt, bv=0.003); rot_obj(o, 0, 8, 25)
    for a, b in (((-W / 2 + 0.05, D / 2 - 0.01, 0.15), (W / 2 - 0.05, D / 2 - 0.01, 1.55)), ((-W / 2 + 0.05, D / 2 - 0.01, 1.55), (W / 2 - 0.05, D / 2 - 0.01, 0.15))):
        beam("Shelf_Brace", a, b, 0.08, 0.015, w, rt, bv=0.003)
    return rt

def angle_iron(name, p1, p2, size, t, mat, parent, flip=(1, 1)):
    """An L section along p1->p2 (vertical posts here): two thin flanges along +x*flip[0] and +y*flip[1]."""
    p1 = Vector(p1); p2 = Vector(p2); c = (p1 + p2) / 2; L = (p2 - p1).length
    box(name, (size, t, L), c + Vector((flip[0] * size / 2, 0, 0)), mat, parent, bv=0.001)
    box(name, (t, size, L), c + Vector((0, flip[1] * size / 2, 0)), mat, parent, bv=0.001)

def build_shelf_metal():
    rt = root("Shelf_Metal"); m, s_, b = role("frame"), role("sheet"), role("bolt")
    W, D, H = 1.0, 0.4, 1.8
    for sx in (-1, 1):
        for sy in (-1, 1):
            angle_iron("Rack_Post", (sx * W / 2, sy * D / 2, 0), (sx * W / 2, sy * D / 2, H), 0.035, 0.003, m, rt, (-sx, -sy))
            for k in range(30):   # the slotted holes, as dark pegs (read through the bake's AO)
                z = 0.06 + k * 0.057
                box("Rack_Slot", (0.004, 0.012, 0.022), (sx * W / 2 - sx * 0.018, sy * D / 2 + 0.0, z), b, rt, bv=0)
    rnd = random.Random(9)
    for li, z in enumerate((0.1, 0.55, 1.0, 1.45, 1.78)):
        tilt = 2.5 if li == 2 else 0
        sh = box("Rack_Shelf", (W - 0.01, D - 0.01, 0.002), (0, 0, z), s_, rt, bv=0)
        subdivide(sh, 4); jitter(sh, 0.004, 3.0, li)
        for y in (-D / 2 + 0.01, D / 2 - 0.01):
            box("Rack_Lip", (W - 0.01, 0.002, 0.03), (0, y, z - 0.014), s_, rt, bv=0.0005)
        for x in (-W / 2 + 0.01, W / 2 - 0.01):
            box("Rack_Lip", (0.002, D - 0.01, 0.03), (x, 0, z - 0.014), s_, rt, bv=0.0005)
        for sx in (-1, 1):
            for sy in (-1, 1):
                cyl("Rack_Bolt", 0.006, 0.006, 0.012, (sx * (W / 2 - 0.004), sy * (D / 2 - 0.018), z - 0.012), b, rt, seg=6, axis=(1, 0, 0))
    return rt

def build_cabinet_metal():
    rt = root("Cabinet_Metal"); m, h, d = role("paint"), role("steel"), role("dark")
    W, D, H = 0.9, 0.45, 1.85; leg = 0.06
    body = grid_box("Cab_Body", (W, D, H - leg), (0, 0, leg + (H - leg) / 2), m, rt, cuts=3, smooth=False)
    dent(body, (W / 2, 0.05, 1.0), 0.18, 0.02); dent(body, (-W / 2, -0.1, 0.5), 0.12, 0.012)
    bevel(body, 0.006)
    box("Cab_Top", (W + 0.02, D + 0.02, 0.025), (0, 0, H + 0.004), m, rt, bv=0.006)
    for sx in (-1, 1):
        for sy in (-1, 1):
            box("Cab_Leg", (0.05, 0.05, leg), (sx * (W / 2 - 0.04), sy * (D / 2 - 0.04), leg / 2), m, rt, bv=0.004)
    dw = W / 2 - 0.012
    for k, sx in enumerate((-1, 1)):
        cx = sx * (dw / 2 + 0.006)
        door = grid_box("Cab_Door", (dw, 0.022, H - leg - 0.06), (cx, -D / 2 - 0.009, leg + (H - leg) / 2), m, rt, cuts=3, smooth=False)
        if k == 1: dent(door, (0.05, -0.012, -0.2), 0.14, 0.012)
        bevel(door, 0.004)
        box("Cab_Panel", (dw - 0.1, 0.006, H - leg - 0.5), (cx, -D / 2 - 0.022, leg + (H - leg) / 2 + 0.05), m, rt, bv=0.004)
        for row in (H - 0.18, 0.22):
            for i in range(6):
                box("Cab_Vent", (dw - 0.16, 0.006, 0.008), (cx, -D / 2 - 0.021, row + i * 0.018), d, rt, bv=0.001)
        for zz in (0.35, H - 0.3):
            cyl("Cab_Hinge", 0.008, 0.008, 0.07, (sx * (W / 2 - 0.004), -D / 2 - 0.02, zz), h, rt, seg=10)
    rod("Cab_Handle", (0.05, -D / 2 - 0.05, 0.92), (0.05, -D / 2 - 0.05, 1.12), 0.008, h, rt)
    for zz in (0.92, 1.12): rod("Cab_HandleFoot", (0.05, -D / 2 - 0.02, zz), (0.05, -D / 2 - 0.05, zz), 0.007, h, rt)
    cyl("Cab_Lock", 0.012, 0.012, 0.02, (0.05, -D / 2 - 0.025, 1.2), h, rt, seg=16, axis=(0, 1, 0))
    # a label holder
    box("Cab_Label", (0.12, 0.003, 0.06), (-0.22, -D / 2 - 0.024, 1.45), h, rt, bv=0.001)
    return rt

def build_crate():
    rt = root("Crate"); w, k, ink = role("wood"), role("batten"), role("ink")
    L, Wd, H = 0.8, 0.6, 0.5; bh = 0.155; t = 0.02
    rnd = random.Random(4)
    for i in range(3):
        z = 0.03 + bh / 2 + i * (bh + 0.008)
        for sy in (-1, 1):
            plank("Crate_Side", (L, t, bh), (0, sy * (Wd / 2 - t / 2), z), w, rt, bv=0.003)
        for sx in (-1, 1):
            plank("Crate_End", (t, Wd - 2 * t, bh), (sx * (L / 2 - t / 2), 0, z), w, rt, bv=0.003)
    for i in range(4):
        y = -Wd / 2 + 0.075 + i * 0.15
        plank("Crate_Top", (L + 0.01, 0.145, t), (rnd.uniform(-0.005, 0.005), y, 0.03 + 3 * bh + 0.024 + t / 2), w, rt, bv=0.003)
    for sx in (-1, 1):
        for sy in (-1, 1):
            plank("Crate_Batten", (0.06, 0.02, H), (sx * (L / 2 - 0.03), sy * (Wd / 2 + 0.01), H / 2 + 0.01), k, rt, bv=0.003)
            plank("Crate_Batten", (0.02, 0.06, H), (sx * (L / 2 + 0.01), sy * (Wd / 2 - 0.03), H / 2 + 0.01), k, rt, bv=0.003)
    for sx in (-1, 1):
        plank("Crate_Skid", (0.07, Wd, 0.03), (sx * (L / 2 - 0.08), 0, 0.015), k, rt, bv=0.003)
    o = text_mesh("Crate_Stencil", "ГРУЗ 47", "impact.ttf", 0.085, (0, -Wd / 2 - 0.0015, 0.3), ink, rt, rot=(math.radians(90), 0, 0))
    o = text_mesh("Crate_Stencil2", "НЕ КАНТОВАТЬ", "impact.ttf", 0.04, (0, -Wd / 2 - 0.0015, 0.17), ink, rt, rot=(math.radians(90), 0, 0))
    return rt

def build_crate_open():
    rt = root("Crate_Open"); w = role("wood")
    L, Wd, H = 0.7, 0.45, 0.34; t = 0.018
    for i, z in enumerate((0.05, 0.17, 0.29)):
        for sy in (-1, 1):
            if i == 2 and sy == -1: continue                       # the top front slat knocked off
            plank("CrateO_Side", (L, t, 0.09), (0, sy * (Wd / 2 - t / 2), z), w, rt, bv=0.003)
        for sx in (-1, 1):
            plank("CrateO_End", (t, Wd - 2 * t, 0.09), (sx * (L / 2 - t / 2), 0, z), w, rt, bv=0.003)
    for sx in (-1, 1):
        for sy in (-1, 1):
            plank("CrateO_Post", (0.035, 0.035, H), (sx * (L / 2 - t - 0.018), sy * (Wd / 2 - t - 0.018), H / 2), w, rt, bv=0.003)
    for i in range(3):
        plank("CrateO_Floor", (L - 0.04, 0.13, t), (0, -Wd / 2 + 0.08 + i * 0.145, 0.012), w, rt, bv=0.002)
    o = plank("CrateO_Loose", (L, t, 0.09), (0.05, -Wd / 2 - 0.12, 0.012), w, rt, bv=0.003); rot_obj(o, 88, 0, 12)
    return rt

def build_pallet():
    rt = root("Pallet"); w, b = role("wood"), role("block")
    L, Wd = 1.2, 0.8
    tops = [(-0.3275, 0.145), (-0.1638, 0.1), (0.0, 0.145), (0.1638, 0.1), (0.3275, 0.145)]
    for i, (y, wd) in enumerate(tops):
        o = plank("Pallet_Top", (L, wd, 0.022), (0, y, 0.133), w, rt, bv=0.003, warp=0.004, seed=i)
    for y in (-0.3275, 0.0, 0.3275):
        plank("Pallet_Stringer", (L, 0.145 if y else 0.145, 0.022), (0, y, 0.1), w, rt, bv=0.003)
        plank("Pallet_Bottom", (L, 0.1 if y else 0.145, 0.022), (0, y, 0.011), w, rt, bv=0.003)
    for x in (-0.5275, 0.0, 0.5275):
        for y in (-0.3275, 0.0, 0.3275):
            box("Pallet_Block", (0.145 if x == 0 else 0.145, 0.1 if y else 0.145, 0.078), (x, y, 0.061), b, rt, bv=0.004)
    return rt

# ================================================================== props_b

def build_radiator():
    rt = root("Radiator_Basement"); m = role("paint"); p = role("pipe")
    n, pitch, H = 10, 0.093, 0.58
    x0 = -(n - 1) * pitch / 2
    for i in range(n):
        x = x0 + i * pitch
        for y in (-0.035, 0.035):
            o = box("Rad_Column", (0.075, 0.05, H - 0.1), (x, y, 0.12 + (H - 0.1) / 2), m, rt, bv=0.022)
        box("Rad_Hub", (0.09, 0.12, 0.07), (x, 0, 0.12 + 0.035), m, rt, bv=0.02)
        box("Rad_Hub", (0.09, 0.12, 0.07), (x, 0, 0.12 + H - 0.035), m, rt, bv=0.02)
        box("Rad_Web", (0.012, 0.03, H - 0.16), (x, 0, 0.12 + H / 2), m, rt, bv=0.004)
    L = n * pitch
    for sx in (-1, 1):
        for zz in (0.155, 0.12 + H - 0.035):
            cyl("Rad_Plug", 0.03, 0.03, 0.03, (sx * (L / 2 + 0.01), 0, zz), m, rt, seg=8, axis=(1, 0, 0), bv=0.003)
        o = box("Rad_Foot", (0.06, 0.16, 0.12), (sx * (L / 2 - 0.12), 0, 0.06), m, rt, bv=0.015)
    sweep("Rad_Pipe", [(L / 2 + 0.02, 0, 0.12 + H - 0.035), (L / 2 + 0.12, 0, 0.12 + H - 0.035), (L / 2 + 0.16, 0.0, H + 0.13), (L / 2 + 0.16, 0, H + 0.45)], 0.016, p, rt, seg=12)
    sweep("Rad_Pipe", [(L / 2 + 0.02, 0, 0.155), (L / 2 + 0.16, 0, 0.155), (L / 2 + 0.16, 0, 0.0)], 0.016, p, rt, seg=12)
    return rt

def build_bucket():
    rt = root("Bucket"); m, h = role("zinc"), role("handle")
    r0, r1, Hb, t = 0.115, 0.15, 0.28, 0.0015
    prof = [(0, 0.004), (r0 - 0.01, 0.004), (r0 - 0.003, 0.0), (r0, 0.01)]
    for zz in (0.06, 0.1):
        r = r0 + (r1 - r0) * zz / Hb
        prof += [(r, zz - 0.006), (r + 0.004, zz), (r, zz + 0.006)]
    prof += line_prof(r0 + (r1 - r0) * 0.106 / Hb, 0.106, r1, Hb - 0.006, 6)
    prof += [(r1 + 0.005, Hb - 0.003), (r1 + 0.006, Hb + 0.002), (r1 + 0.002, Hb + 0.005), (r1 - t, Hb - 0.004)]
    prof += line_prof(r1 - t, Hb - 0.004, r0 - t, 0.012, 8) + [(0, 0.012)]
    o = lathe("Bucket_Body", prof, m, rt, seg=40)
    dent(o, (r1, 0, 0.2), 0.08, 0.012); jitter(o, 0.002, 6, 5)
    for sx in (-1, 1):
        box("Bucket_Ear", (0.006, 0.03, 0.035), (sx * (r1 + 0.003), 0, Hb - 0.03), m, rt, bv=0.002)
    pts = [(-(r1 + 0.008), 0, Hb - 0.03)] + [(-(r1 + 0.008) * math.cos(math.radians(a)), 0, Hb - 0.03 + 0.16 * math.sin(math.radians(a))) for a in range(10, 171, 10)] + [((r1 + 0.008), 0, Hb - 0.03)]
    o = sweep("Bucket_Handle", pts, 0.0028, h, rt, seg=8)
    # the handle dropped back, turned about the ears
    o.data.transform(Matrix.Translation((0, 0, Hb - 0.03)) @ Matrix.Rotation(math.radians(-70), 4, 'X') @ Matrix.Translation((0, 0, -(Hb - 0.03))))
    return rt

def build_paint_cans():
    rt = root("PaintCans"); m, lab, paint, hnd = role("tin"), role("label"), role("drip"), role("handle")
    def can(pref, x, r, h, open_=False, label_col=None, lying=False):
        prof = [(0, 0.003), (r - 0.006, 0.003), (r - 0.002, 0), (r + 0.002, 0.004), (r, 0.012)]
        prof += line_prof(r, 0.012, r, h - 0.012, 6)
        prof += [(r + 0.003, h - 0.006), (r + 0.002, h), (r - 0.004, h)]
        prof += ([(r - 0.005, h - 0.008)] + line_prof(r - 0.005, h - 0.008, r - 0.005, h * 0.55, 3) + [(0, h * 0.55)]) if open_ else [(r - 0.006, h - 0.006), (0, h - 0.006)]
        o = lathe(pref + "_Body", prof, m, rt, seg=36); jitter(o, 0.0015, 8, x * 10)
        lb = lathe(pref + "_Label", [(r + 0.0012, h * 0.2), (r + 0.0012, h * 0.8)], lab, rt, seg=36)
        if open_:
            cyl(pref + "_Paint", r - 0.005, r - 0.005, 0.003, (0, 0, h * 0.55 + 0.002), paint, rt, seg=36)
            for k in range(5):   # runs down the side from the rim
                a = k * 1.1 + 0.4; ln = 0.03 + 0.02 * (k % 3)
                box(pref + "_Run", (0.008, 0.0015, ln), (r * math.sin(a), -r * math.cos(a), h - ln / 2), paint, rt, bv=0.0007).rotation_euler = (0, 0, a)
        else:
            cyl(pref + "_Lid", r - 0.006, r - 0.006, 0.004, (0, 0, h - 0.004), m, rt, seg=36)
        if r > 0.07:
            for sx in (-1, 1): box(pref + "_Ear", (0.004, 0.016, 0.018), (sx * (r + 0.002), 0, h - 0.02), m, rt, bv=0.001)
            pts = [(-(r + 0.004) * math.cos(math.radians(a)), 0, h - 0.02 + 0.04 * math.sin(math.radians(a))) for a in range(0, 181, 15)]
            sweep(pref + "_Handle", pts, 0.0018, hnd, rt, seg=6)
        objs = [ob for ob in rt.children if ob.name.startswith(pref + "_")]
        for ob in objs:
            ob.data.transform(ob.matrix_basis); ob.matrix_basis = Matrix.Identity(4)
            if lying:
                ob.data.transform(Matrix.Translation((0, 0, r)) @ Matrix.Rotation(math.radians(90), 4, 'X') @ Matrix.Translation((0, 0, -h / 2)))
            ob.location = (x, 0, 0)
    can("CanA", 0.0, 0.09, 0.19)
    can("CanB", 0.35, 0.09, 0.19, open_=True)
    can("CanC", 0.7, 0.055, 0.11, lying=True)
    return rt

def build_fusebox():
    rt = root("Fusebox"); m, y_, ink, c, s_ = role("paint"), role("sign"), role("ink"), role("conduit"), role("steel")
    W, D, H = 0.42, 0.15, 0.56; z0 = 0.0
    box("Fuse_Body", (W, D, H), (0, -D / 2, H / 2), m, rt, bv=0.006)
    door = box("Fuse_Door", (W - 0.02, 0.016, H - 0.02), (0, -D - 0.006, H / 2), m, rt, bv=0.004)
    box("Fuse_DoorRidge", (W - 0.08, 0.004, H - 0.08), (0, -D - 0.015, H / 2), m, rt, bv=0.003)
    for zz in (0.08, H - 0.08): cyl("Fuse_Hinge", 0.007, 0.007, 0.05, (-W / 2 + 0.005, -D - 0.01, zz), s_, rt, seg=10)
    cyl("Fuse_Lock", 0.014, 0.014, 0.012, (W / 2 - 0.05, -D - 0.018, H / 2), s_, rt, seg=6, axis=(0, 1, 0), bv=0.001)
    box("Fuse_LockSlot", (0.003, 0.004, 0.016), (W / 2 - 0.05, -D - 0.025, H / 2), ink, rt, bv=0)
    # warning triangle
    def tri(name, size, yy, mat):
        bm = bmesh.new(); vs = [bm.verts.new((size * math.sin(a), 0, size * math.cos(a))) for a in (0, 2 * math.pi / 3, 4 * math.pi / 3)]
        f = bm.faces.new(vs); bmesh.ops.solidify(bm, geom=[f], thickness=0.001)
        o = from_bm(name, bm, mat, rt); o.location = (0, yy, H / 2 + 0.1); return o
    tri("Fuse_SignBorder", 0.075, -D - 0.0175, ink)
    tri("Fuse_Sign", 0.062, -D - 0.0185, y_)
    o = text_mesh("Fuse_SignBolt", "!", "arialbd.ttf", 0.06, (0, -D - 0.0195, H / 2 + 0.09), ink, rt, rot=(math.radians(90), 0, 0))
    o = text_mesh("Fuse_Text", "ЩР-1   380В", "arialbd.ttf", 0.028, (0, -D - 0.0175, 0.12), ink, rt, rot=(math.radians(90), 0, 0))
    for x in (-0.12, 0.0, 0.12):
        rod("Fuse_Conduit", (x, -0.05, H), (x, -0.05, H + 0.7), 0.012, c, rt, seg=10)
        cyl("Fuse_Gland", 0.018, 0.018, 0.025, (x, -0.05, H + 0.012), s_, rt, seg=6)
    sweep("Fuse_Cable", [(0.1, -0.06, 0.0), (0.1, -0.06, -0.12), (0.13, -0.03, -0.3), (0.12, -0.02, -0.6)], 0.008, c, rt, seg=8)
    return rt

def build_lamp_caged():
    rt = root("Lamp_Caged"); k, cab, cage, bake = role("bakelite"), role("cable"), role("cage"), None
    cyl("Lamp_Rose", 0.05, 0.045, 0.03, (0, 0, -0.015), k, rt, seg=24, bv=0.003)
    sweep("Lamp_Cable", [(0, 0, -0.03), (0.004, 0.002, -0.2), (0, 0, -0.42)], 0.0045, cab, rt, seg=8)
    lathe("Lamp_Socket", [(0, -0.42), (0.012, -0.42), (0.018, -0.44), (0.034, -0.46), (0.036, -0.52), (0.032, -0.53), (0.03, -0.55), (0, -0.55)], k, rt, seg=24)
    # glass jar and bulb (not baked: their own materials in Unity)
    o = lathe("Lamp_Jar", [(0.06, -0.55), (0.075, -0.6), (0.08, -0.68), (0.07, -0.75), (0.04, -0.775), (0, -0.78)], glass_mat(), rt, seg=32); no_bake(o)
    o = lathe("Lamp_Bulb", [(0.012, -0.55), (0.013, -0.58), (0.03, -0.61), (0.035, -0.65), (0.03, -0.69), (0.015, -0.71), (0, -0.715)], bulb_mat(), rt, seg=24); no_bake(o)
    # the cage: a collar at the socket, rings, and six bent wires over the jar
    torus("Lamp_Collar", 0.064, 0.006, (0, 0, -0.55), cage, rt, seg=32, rseg=6)
    torus("Lamp_Ring", 0.088, 0.0035, (0, 0, -0.65), cage, rt, seg=32, rseg=6)
    torus("Lamp_Ring", 0.08, 0.0035, (0, 0, -0.72), cage, rt, seg=32, rseg=6)
    for i in range(6):
        a = 2 * math.pi * i / 6; d = Vector((math.cos(a), math.sin(a), 0))
        pts = [d * 0.066 + Vector((0, 0, -0.55))]
        for t in range(1, 9):
            u = t / 8; r = 0.066 + 0.025 * math.sin(math.pi * min(u * 1.4, 1)) - 0.05 * max(0, u - 0.7) / 0.3
            pts.append(d * max(r, 0.01) + Vector((0, 0, -0.55 - 0.25 * u)))
        sweep("Lamp_Wire", pts, 0.0028, cage, rt, seg=6)
    cyl("Lamp_Cap", 0.02, 0.02, 0.012, (0, 0, -0.8), cage, rt, seg=12)
    return rt

def build_door_bunker():
    rt = root("Door_Bunker"); f, l, wh, b = role("frame"), role("leaf"), role("wheel"), role("bolt")
    OW, OH, T = 1.0, 2.1, 0.3
    # frame: a steel channel lining the opening's jambs and head
    for sx in (-1, 1):
        box("Frame_Jamb", (0.08, T + 0.02, OH), (sx * (OW / 2 - 0.04), 0, OH / 2), f, rt, bv=0.004)
        box("Frame_Face", (0.06, 0.012, OH + 0.06), (sx * (OW / 2 + 0.02), -T / 2 - 0.016, OH / 2), f, rt, bv=0.003)
    box("Frame_Head", (OW, T + 0.02, 0.08), (0, 0, OH - 0.04), f, rt, bv=0.004)
    box("Frame_HeadFace", (OW + 0.1, 0.012, 0.06), (0, -T / 2 - 0.016, OH + 0.02), f, rt, bv=0.003)
    box("Frame_Sill", (OW, T, 0.02), (0, 0, 0.01), f, rt, bv=0.003)
    # leaf
    LW, LH, LT = OW - 0.17, OH - 0.1, 0.06
    ly = -T / 2 + 0.05; lz = 0.03 + LH / 2
    leaf = grid_box("Leaf_Slab", (LW, LT, LH), (0, ly, lz), l, rt, cuts=4, smooth=False); jitter(leaf, 0.003, 2.0, 3); bevel(leaf, 0.006)
    for zz in (0.25, LH - 0.2): box("Leaf_Rib", (LW - 0.08, 0.02, 0.05), (0, ly - LT / 2 - 0.01, 0.03 + zz), l, rt, bv=0.006)
    for sx in (-1, 1): box("Leaf_Stile", (0.05, 0.02, LH - 0.1), (sx * (LW / 2 - 0.045), ly - LT / 2 - 0.01, lz), l, rt, bv=0.006)
    # rivets along the ribs and stiles
    for zz in (0.25, LH - 0.2):
        for i in range(9): sphere("Leaf_Rivet", 0.007, ((i - 4) * 0.085, ly - LT / 2 - 0.02, 0.03 + zz), b, rt, 8, 5)
    for sx in (-1, 1):
        for i in range(14): sphere("Leaf_Rivet", 0.007, (sx * (LW / 2 - 0.045), ly - LT / 2 - 0.02, 0.15 + i * 0.13), b, rt, 8, 5)
    # wheel in the middle on a boss, dogs at the edge
    cyl("Leaf_Boss", 0.06, 0.07, 0.04, (0, ly - LT / 2 - 0.02, 1.15), l, rt, seg=24, axis=(0, 1, 0), bv=0.004)
    rod("Leaf_Spindle", (0, ly - LT / 2, 1.15), (0, ly - LT / 2 - 0.12, 1.15), 0.014, b, rt)
    handwheel("Leaf_Wheel", (0, ly - LT / 2 - 0.1, 1.15), (0, 1, 0), 0.15, wh, rt, spokes=4)
    for zz in (0.5, 1.15, 1.8):
        box("Leaf_Dog", (0.1, 0.03, 0.035), (-LW / 2 + 0.04, ly - LT / 2 - 0.016, zz), l, rt, bv=0.005)
        cyl("Leaf_DogPin", 0.012, 0.012, 0.04, (-LW / 2 + 0.08, ly - LT / 2 - 0.03, zz), b, rt, seg=10, axis=(0, 1, 0))
    # hinges on the right (+x) jamb
    for zz in (0.35, 1.0, 1.7):
        cyl("Frame_Hinge", 0.022, 0.022, 0.16, (LW / 2 + 0.03, ly - LT / 2 - 0.005, zz), f, rt, seg=16, bv=0.003)
        box("Leaf_HingeStrap", (0.22, 0.012, 0.06), (LW / 2 - 0.09, ly - LT / 2 - 0.006, zz), l, rt, bv=0.003)
    box("Leaf_Plate", (0.18, 0.003, 0.1), (-0.25, ly - LT / 2 - 0.002, 1.55), b, rt, bv=0.001)
    return rt

def build_window_basement():
    """Fits Wall_Window_2m's opening (1.1 x 0.55, its sill 1.85 m up): origin on the sill, in the wall's middle."""
    rt = root("Window_Basement"); f, s_ = role("frame"), role("sill")
    OW, OH, T = 1.1, 0.55, 0.3
    fy = 0.06                                  # the frame sits towards the outside
    for sx in (-1, 1): box("Win_Jamb", (0.05, 0.08, OH), (sx * (OW / 2 - 0.025), fy, OH / 2), f, rt, bv=0.004)
    for zz in (0.025, OH - 0.025): box("Win_Rail", (OW, 0.08, 0.05), (0, fy, zz), f, rt, bv=0.004)
    cols = 6
    for i in range(1, cols):
        x = -OW / 2 + 0.05 + i * (OW - 0.1) / cols
        box("Win_Mullion", (0.025, 0.05, OH - 0.1), (x, fy, OH / 2), f, rt, bv=0.003)
    box("Win_Transom", (OW - 0.1, 0.05, 0.025), (0, fy, OH / 2), f, rt, bv=0.003)
    g = box("Win_Glass", (OW - 0.1, 0.004, OH - 0.1), (0, fy + 0.01, OH / 2), glass_mat(), rt, bv=0); no_bake(g)
    for i in range(7):
        x = -OW / 2 + 0.08 + i * (OW - 0.16) / 6
        rod("Win_Bar", (x, fy + 0.07, 0.0), (x, fy + 0.07, OH), 0.009, f, rt, seg=8)
    for zz in (0.08, OH - 0.08): box("Win_BarRail", (OW - 0.08, 0.008, 0.035), (0, fy + 0.07, zz), f, rt, bv=0.002)
    o = box("Win_Sill", (OW + 0.06, 0.24, 0.03), (0, -0.07, -0.0), s_, rt, bv=0.006); rot_obj(o, -6, 0, 0)
    return rt

def build_gas_cylinder():
    rt = root("GasCylinder"); r_, wt, br, st = role("paint"), role("text"), role("brass"), role("steel")
    R, H = 0.15, 0.93
    prof = [(0, 0.03), (R * 0.7, 0.03), (R * 0.95, 0.05), (R, 0.09)] + line_prof(R, 0.09, R, 0.72, 8)
    prof += [(R * 0.98, 0.77), (R * 0.9, 0.81), (R * 0.72, 0.85), (R * 0.45, 0.875), (0.035, 0.885), (0, 0.885)]
    o = lathe("Gas_Body", prof, r_, rt, seg=48); dent(o, (R, 0, 0.4), 0.1, 0.012); jitter(o, 0.0015, 5, 8)
    lathe("Gas_Foot", [(R * 0.94, 0.0), (R * 0.96, 0.0), (R * 0.96, 0.06), (R * 0.9, 0.06), (R * 0.9, 0.005)], r_, rt, seg=40, cyclic=True)
    lathe("Gas_Collar", [(0.04, 0.88), (0.05, 0.88), (0.05, 0.9), (0.042, 0.905)], st, rt, seg=24)
    cyl("Gas_Valve", 0.018, 0.018, 0.06, (0, 0, 0.93), br, rt, seg=12, bv=0.002)
    cyl("Gas_Outlet", 0.008, 0.008, 0.05, (0.03, 0, 0.94), br, rt, seg=10, axis=(1, 0, 0))
    cyl("Gas_Wheel", 0.025, 0.025, 0.01, (0, 0, 0.965), st, rt, seg=12, bv=0.002)
    t = text_mesh("Gas_Text", "ПРОПАН", "arialbd.ttf", 0.06, (0, 0, 0), wt, rt)
    wrap_text(t, R + 0.0015, 0.55)
    return rt

# ================================================================== props_c

def build_chair_broken():
    rt = root("Chair_Broken"); w = role("wood")
    S, Hs = 0.42, 0.45
    plank("Chair_Seat", (S, S - 0.02, 0.025), (0, 0, Hs), w, rt, bv=0.004)
    for sx in (-1, 1):
        plank("Chair_FrontLeg", (0.035, 0.035, Hs - 0.02), (sx * (S / 2 - 0.03), -S / 2 + 0.03, (Hs - 0.02) / 2), w, rt, bv=0.003)
        o = plank("Chair_BackLeg", (0.035, 0.035, 0.92), (sx * (S / 2 - 0.03), S / 2 - 0.03, 0.46), w, rt, bv=0.003)
        for v in o.data.vertices:
            if v.co.z > 0.0: v.co.y += 0.06 * (v.co.z / 0.46)
        plank("Chair_SideRail", (0.02, S - 0.08, 0.05), (sx * (S / 2 - 0.03), 0, Hs - 0.045), w, rt, bv=0.002)
        plank("Chair_Stretcher", (0.018, S - 0.08, 0.022), (sx * (S / 2 - 0.03), 0, 0.15), w, rt, bv=0.002)
    plank("Chair_FrontRail", (S - 0.08, 0.02, 0.05), (0, -S / 2 + 0.03, Hs - 0.045), w, rt, bv=0.002)
    plank("Chair_BackRail", (S - 0.08, 0.02, 0.06), (0, S / 2 - 0.02 + 0.055, 0.84), w, rt, bv=0.002)
    o = plank("Chair_Slat", (S - 0.08, 0.016, 0.06), (0, S / 2 - 0.02 + 0.03, 0.66), w, rt, bv=0.002)
    o = plank("Chair_SlatBroken", (0.2, 0.016, 0.05), (-0.08, S / 2 + 0.03, 0.56), w, rt, bv=0.002); rot_obj(o, 0, 28, 0)
    return rt

def build_workbench():
    rt = root("Workbench"); w, l, st, v = role("wood"), role("leg"), role("steel"), role("vise")
    L, Wd, H = 1.5, 0.7, 0.85
    for i in range(4):
        plank("Bench_Top", (L, Wd / 4 - 0.004, 0.05), (0, -Wd / 2 + Wd / 8 + i * Wd / 4, H - 0.025), w, rt, bv=0.004, warp=0.003, seed=i)
    for sx in (-1, 1):
        for sy in (-1, 1):
            plank("Bench_Leg", (0.08, 0.08, H - 0.05), (sx * (L / 2 - 0.08), sy * (Wd / 2 - 0.06), (H - 0.05) / 2), l, rt, bv=0.005)
        plank("Bench_SideApron", (0.03, Wd - 0.1, 0.1), (sx * (L / 2 - 0.08), 0, H - 0.1), l, rt, bv=0.003)
        plank("Bench_SideLow", (0.03, Wd - 0.1, 0.07), (sx * (L / 2 - 0.08), 0, 0.15), l, rt, bv=0.003)
    for sy in (-1, 1): plank("Bench_Apron", (L - 0.1, 0.03, 0.1), (0, sy * (Wd / 2 - 0.06), H - 0.1), l, rt, bv=0.003)
    for i in range(3): plank("Bench_Shelf", (L - 0.25, 0.17, 0.02), (0, -0.2 + i * 0.2, 0.2), w, rt, bv=0.003)
    # bench vise at the front left
    vx = -L / 2 + 0.25; vy = -Wd / 2
    box("Vise_Base", (0.16, 0.18, 0.04), (vx, vy + 0.06, H + 0.02), v, rt, bv=0.006)
    box("Vise_Body", (0.12, 0.2, 0.08), (vx, vy + 0.03, H + 0.08), v, rt, bv=0.01)
    box("Vise_JawFixed", (0.15, 0.03, 0.06), (vx, vy + 0.12, H + 0.15), v, rt, bv=0.005)
    box("Vise_JawMove", (0.15, 0.03, 0.06), (vx, vy - 0.04, H + 0.15), v, rt, bv=0.005)
    box("Vise_Slide", (0.06, 0.18, 0.05), (vx, vy - 0.02, H + 0.1), v, rt, bv=0.006)
    rod("Vise_Screw", (vx, vy - 0.05, H + 0.1), (vx, vy - 0.16, H + 0.1), 0.012, st, rt)
    rod("Vise_Bar", (vx - 0.12, vy - 0.15, H + 0.1), (vx + 0.12, vy - 0.15, H + 0.1), 0.007, st, rt)
    for sx in (-1, 1): sphere("Vise_Knob", 0.012, (vx + sx * 0.125, vy - 0.15, H + 0.1), st, rt, 10, 6)
    # a hammer and some nails on the top
    rod("Bench_HammerHandle", (0.25, 0.05, H + 0.015), (0.52, 0.12, H + 0.015), 0.013, l, rt)
    box("Bench_HammerHead", (0.035, 0.11, 0.03), (0.25, 0.05, H + 0.02), st, rt, bv=0.004).rotation_euler = (0, 0, math.radians(15))
    rnd = random.Random(2)
    for i in range(7):
        o = rod("Bench_Nail", (0, 0, 0), (0.06, 0, 0), 0.0018, st, rt, seg=6); o.location = (0.1 + rnd.uniform(-0.1, 0.1), -0.15 + rnd.uniform(-0.05, 0.05), H + 0.002); o.rotation_euler = (0, 0, rnd.uniform(0, 3.14))
    return rt

def build_tyre():
    rt = root("Tyre"); r = role("rubber")
    Ro, Ri, Wt = 0.33, 0.2, 0.2
    prof = []
    sg = lambda v, e: math.copysign(abs(v) ** e, v)
    for k in range(56):   # a squarish section (superellipse): flat tread, bulging sidewalls
        a = 2 * math.pi * k / 56
        prof.append(((Ro + Ri) / 2 + (Ro - Ri) / 2 * sg(math.cos(a), 0.45), Wt / 2 + Wt / 2 * sg(math.sin(a), 0.6)))
    o = lathe("Tyre_Body", prof, r, rt, seg=144, cyclic=True)
    for v in o.data.vertices:   # tread blocks: notches on the tread band
        rr = math.hypot(v.co.x, v.co.y)
        if rr > Ro - 0.012 and abs(v.co.z - Wt / 2) < Wt * 0.42:
            a = math.atan2(v.co.y, v.co.x)
            if math.sin(a * 32 + (v.co.z - Wt / 2) * 30) > 0.2: v.co.x *= (rr - 0.008) / rr; v.co.y *= (rr - 0.008) / rr
    jitter(o, 0.004, 3, 4)
    return rt

def build_cement_bags():
    rt = root("CementBags"); p, ink, c = role("paper"), role("ink"), role("cement")
    def bag(name, loc, rz, seed):
        o = grid_box(name, (0.6, 0.4, 0.13), (0, 0, 0), p, rt, cuts=8)
        pillow(o, 0.3, 0.2, 0.065); jitter(o, 0.008, 4, seed)
        o.data.transform(Matrix.Translation((0, 0, 0.065)))
        o.location = loc; o.rotation_euler = (0, 0, math.radians(rz)); return o
    bag("Bag_1", (-0.21, 0, 0), 90, 1); bag("Bag_2", (0.21, 0, 0), 92, 2); bag("Bag_3", (0, 0.02, 0.115), 4, 3)
    t = text_mesh("Bag_Text", "ЦЕМЕНТ", "impact.ttf", 0.075, (0, 0.0, 0.115 + 0.134), ink, rt)
    t2 = text_mesh("Bag_Text2", "М400  50 кг", "arialbd.ttf", 0.035, (0, -0.09, 0.115 + 0.131), ink, rt)
    # a torn bag's spill at the foot
    o = lathe("Bag_Spill", [(0, 0.035), (0.08, 0.03), (0.16, 0.012), (0.24, 0.0)], c, rt, seg=32)
    o.location = (0.35, -0.3, 0); o.scale = (1.3, 0.9, 1); jitter(o, 0.01, 8, 6)
    return rt

def build_planks_pile():
    rt = root("Planks_Pile"); w, d = role("wood"), role("wood2")
    rnd = random.Random(12)
    specs = [(2.0, 0.14, 0.025, (0, 0, 0.0125), 0, w), (1.8, 0.12, 0.025, (0.05, 0.16, 0.0125), 3, d),
             (1.6, 0.14, 0.025, (-0.1, -0.15, 0.0125), -2, w), (1.4, 0.1, 0.04, (0.1, 0.02, 0.045), 12, d),
             (1.2, 0.14, 0.025, (-0.2, 0.05, 0.078), -18, w), (0.7, 0.12, 0.025, (0.5, -0.1, 0.06), 40, d)]
    for i, (L, W, T, loc, rz, m) in enumerate(specs):
        o = plank("Plank", (L, W, T), loc, m, rt, bv=0.003, warp=0.006, seed=i)
        if i in (3, 5):   # a splintered end: taper one end's vertices
            for v in o.data.vertices:
                if v.co.x > L / 2 - 0.15: v.co.y *= 0.4 + 0.6 * (L / 2 - v.co.x) / 0.15; v.co.z += rnd.uniform(-0.004, 0.004)
        o.rotation_euler = (math.radians(rnd.uniform(-2, 2)), math.radians(rnd.uniform(-2, 2)), math.radians(rz))
    return rt

def build_bricks():
    rt = root("Bricks"); b = role("brick")
    def brick(name, size, loc, r, seed):
        o = grid_box(name, size, loc, b, rt, cuts=2); jitter(o, 0.003, 30, seed); bevel(o, 0.004)
        o.rotation_euler = tuple(math.radians(a) for a in r); return o
    # single and half for scattering, then a pile
    brick("Brick_Single", (0.25, 0.12, 0.065), (0, 0, 0.0325), (0, 0, 0), 1)
    brick("Brick_Half", (0.12, 0.12, 0.065), (0, 0.4, 0.0325), (0, 0, 0), 2)
    rnd = random.Random(5)
    spots = [(0.0, 0, 0.0325, 0), (0.27, 0.02, 0.0325, 4), (0.12, 0.14, 0.0325, 92), (-0.2, 0.1, 0.0325, 70),
             (0.1, 0.0, 0.1, 8), (0.05, 0.12, 0.1, -12), (0.2, 0.15, 0.06, 35), (-0.1, -0.1, 0.03, 120)]
    for i, (x, y, z, rz) in enumerate(spots):
        half = i in (2, 6)
        brick("Pile_Brick", (0.12 if half else 0.25, 0.12, 0.065), (x + 1.0, y, z), (rnd.uniform(-6, 6), rnd.uniform(-6, 6), rz), 10 + i)
    return rt

def build_mattress():
    rt = root("Mattress"); f, p = role("fabric"), role("piping")
    L, W, T = 1.9, 0.8, 0.14
    o = grid_box("Mat_Body", (L, W, T), (0, 0, 0), f, rt, cuts=12)
    pillow(o, L / 2, W / 2, T / 2, p=6, floor=0.5); jitter(o, 0.012, 2.5, 4)
    for v in o.data.vertices:   # a sag in the middle and a corner curling up
        v.co.z -= 0.02 * math.cos(math.pi * v.co.x / L) * math.cos(math.pi * v.co.y / W)
        if v.co.x > L / 2 - 0.35 and v.co.y > W / 2 - 0.3: v.co.z += 0.12 * ((v.co.x - (L / 2 - 0.35)) / 0.35) * ((v.co.y - (W / 2 - 0.3)) / 0.3)
    o.data.transform(Matrix.Translation((0, 0, T / 2)))
    # tufting buttons
    for i in range(5):
        for j in range(2):
            x = -L / 2 + 0.3 + i * (L - 0.6) / 4; y = -W / 2 + 0.25 + j * (W - 0.5)
            sphere("Mat_Tuft", 0.012, (x, y, T - 0.01 - 0.02 * math.cos(math.pi * x / L)), p, rt, 8, 5)
    return rt

def build_trash_set():
    rt = root("Trash_Set"); gg, gb, tin, pap, rag, rub, card = role("glass_g"), role("glass_b"), role("tin"), role("news"), role("rag"), role("rubble"), role("card")
    bottle = [(0, 0.0), (0.028, 0.0), (0.032, 0.008), (0.032, 0.14), (0.028, 0.17), (0.016, 0.2), (0.013, 0.225), (0.014, 0.235), (0.012, 0.24), (0.0095, 0.24), (0.0095, 0.2), (0, 0.2)]
    lathe("BottleA_Body", bottle, gg, rt, loc=(0, 0, 0), seg=24)
    o = lathe("BottleB_Body", bottle, gb, rt, seg=24)
    o.data.transform(Matrix.Translation((0, 0, 0.032)) @ Matrix.Rotation(math.radians(90), 4, 'Y') @ Matrix.Translation((0, 0, -0.12)))
    o.location = (0.3, 0, 0)
    for k, x in enumerate((0.6, 0.8)):
        o = lathe(f"Can{'AB'[k]}_Body", [(0, 0.0), (0.03, 0.0), (0.033, 0.01)] + line_prof(0.033, 0.01, 0.033, 0.105, 6) + [(0.028, 0.115), (0, 0.115)], tin, rt, seg=24)
        for v in o.data.vertices:
            v.co.z *= 0.45 if k == 0 else 0.75
            v.co.x *= 1.25 if k == 0 else 1.0
        jitter(o, 0.006, 25, 3 + k); dent(o, (0.03, 0, 0.04), 0.03, 0.01)
        if k == 1: o.data.transform(Matrix.Translation((0, 0, 0.033)) @ Matrix.Rotation(math.radians(90), 4, 'X') @ Matrix.Translation((0, 0, -0.045)))
        o.location = (x, 0, 0)
    o = sphere("Newspaper_Ball", 0.07, (0, 0, 0), pap, rt, 24, 16)
    for v in o.data.vertices: v.co.z *= 0.7
    jitter(o, 0.022, 22, 7); jitter(o, 0.008, 60, 8)
    o.data.transform(Matrix.Translation((0, 0, 0.05))); o.location = (1.1, 0, 0)
    o = box("Rag_Cloth", (0.45, 0.32, 0.002), (0, 0, 0), rag, rt, bv=0); subdivide(o, 20)
    for v in o.data.vertices: v.co.z = 0.01 + abs(noise.noise(v.co * 6.0 + Vector((3, 1, 2)))) * 0.06 + noise.noise(v.co * 18.0) * 0.008
    for pl in o.data.polygons: pl.use_smooth = True
    o.location = (1.5, 0, 0)
    o = box("Card_Sheet", (0.6, 0.42, 0.004), (0, 0, 0), card, rt, bv=0); subdivide(o, 10)
    for v in o.data.vertices: v.co.z = 0.003 + max(0, noise.noise(v.co * 3.0 + Vector((5, 2, 1)))) * 0.04
    o.location = (2.1, 0, 0)
    # debris: a low heap of grit with chunks of plaster / concrete on it
    o = lathe("Debris_Heap", [(0, 0.12), (0.1, 0.1), (0.22, 0.05), (0.38, 0.0)], rub, rt, seg=40); subdivide(o, 1); jitter(o, 0.025, 7, 9)
    rnd = random.Random(21)
    for i in range(16):
        s = rnd.uniform(0.025, 0.09); a = rnd.uniform(0, 6.28); d = rnd.uniform(0, 0.33)
        c = grid_box("Debris_Chunk", (s, s * rnd.uniform(0.6, 1.2), s * rnd.uniform(0.4, 0.8)), (math.cos(a) * d, math.sin(a) * d, 0.07 * (1 - d / 0.38) + s * 0.2), rub, rt, cuts=2)
        jitter(c, s * 0.25, 12, i); c.rotation_euler = (rnd.uniform(0, 3), rnd.uniform(0, 3), rnd.uniform(0, 3))
        for pl in c.data.polygons: pl.use_smooth = False
    for ob in rt.children:
        if ob.name.startswith("Debris_"): ob.location = Vector(ob.location) + Vector((2.9, 0, 0))
    return rt

# ================================================================== configs and groups

C_DRUM = CFG(drum="drum")
C_DRUM_G = CFG(drum_g="green_paint")
C_WOOD = CFG(wood="wood_old")
C_RACK = CFG(frame="grey_paint", sheet="grey_paint", bolt="rust_heavy")
C_CAB = CFG(paint="green_paint", steel="steel", dark="rust_heavy")
C_CRATE = CFG(wood="wood_raw", batten="wood_dark", ink="ink")
C_CRATE_O = CFG(wood="wood_worn")
C_PALLET = CFG(wood="wood_raw", block="wood_dark")
C_RAD = CFG(paint="pipe_paint", pipe="rust_heavy")
C_BUCKET = CFG(zinc="steel", handle="rust_heavy")
C_CANS = CFG(tin="grey_paint", label="yellow_paint", drip="white_paint", handle="rust_heavy")
C_FUSE = CFG(paint="grey_paint", sign="yellow_paint", ink="ink", conduit="steel", steel="steel")
C_LAMP = CFG(bakelite="cable", cable="cable", cage="rust_heavy")
C_DOOR = CFG(frame="pipe_paint", leaf="grey_paint", wheel="cast_iron", bolt="rust_heavy")
C_WIN = CFG(frame="grey_paint", sill="concrete")
C_GAS = CFG(paint="red_paint", text="white_paint", brass="brass", steel="steel")
C_CHAIR = CFG(wood="wood_worn")
C_BENCH = CFG(wood="wood_dark", leg="wood_old", steel="steel", vise="green_paint")
C_TYRE = CFG(rubber="rubber")
C_BAGS = CFG(paper="paper", ink="ink", cement="concrete")
C_PLANKS = CFG(wood="wood_old", wood2="wood_dark")
C_BRICKS = CFG(brick="brick")
C_MAT = CFG(fabric="fabric", piping="rag")
C_TRASH = CFG(glass_g="glass_green", glass_b="glass_brown", tin="steel", news="paper", rag="rag", rubble="rubble", card="paper")
C_TRASH["news"]["base"] = ((0.3, 0.29, 0.26), (0.24, 0.23, 0.2))
for _r in ("news", "rag", "card"): C_TRASH[_r]["dust"] = 0.25
C_TRASH["rubble"]["dust"] = 0.3
C_BAGS["paper"]["dust"] = 0.4

GROUPS["props_a"] = [
    ("Barrel", build_barrel, C_DRUM, 2048, None, None),
    ("Barrel_Open", build_barrel_open, C_DRUM_G, 2048, None, None),
    ("Shelf_Wood", build_shelf_wood, C_WOOD, 2048, None, None),
    ("Shelf_Metal", build_shelf_metal, C_RACK, 2048, None, None),
    ("Cabinet_Metal", build_cabinet_metal, C_CAB, 2048, None, None),
    ("Crate", build_crate, C_CRATE, 2048, None, None),
    ("Crate_Open", build_crate_open, C_CRATE_O, 1024, None, None),
    ("Pallet", build_pallet, C_PALLET, 2048, None, None),
]
GROUPS["props_b"] = [
    ("Radiator_Basement", build_radiator, C_RAD, 2048, None, None),
    ("Bucket", build_bucket, C_BUCKET, 1024, None, None),
    ("PaintCans", build_paint_cans, C_CANS, 1024, "prefix:CanA,CanB,CanC", {"CanA": (0, 0, 0), "CanB": (0.35, 0, 0), "CanC": (0.7, 0, 0)}),
    ("Fusebox", build_fusebox, C_FUSE, 1024, None, None),
    ("Lamp_Caged", build_lamp_caged, C_LAMP, 1024, None, None),
    ("Door_Bunker", build_door_bunker, C_DOOR, 2048, "prefix:Frame,Leaf", {"Frame": (0, 0, 0), "Leaf": (0.445, -0.135, 0)}),
    ("Window_Basement", build_window_basement, C_WIN, 1024, None, None),
    ("GasCylinder", build_gas_cylinder, C_GAS, 1024, None, None),
]
GROUPS["props_c"] = [
    ("Chair_Broken", build_chair_broken, C_CHAIR, 1024, None, None),
    ("Workbench", build_workbench, C_BENCH, 2048, None, None),
    ("Tyre", build_tyre, C_TYRE, 1024, None, None),
    ("CementBags", build_cement_bags, C_BAGS, 1024, None, None),
    ("Planks_Pile", build_planks_pile, C_PLANKS, 2048, None, None),
    ("Bricks", build_bricks, C_BRICKS, 1024, "prefix:Brick_Single,Brick_Half,Pile", {"Brick_Single": (0, 0, 0), "Brick_Half": (0, 0.4, 0), "Pile": (1.0, 0, 0)}),
    ("Mattress", build_mattress, C_MAT, 2048, None, None),
    ("Trash_Set", build_trash_set, C_TRASH, 2048, "prefix:BottleA,BottleB,CanA,CanB,Newspaper,Rag,Card,Debris",
     {"BottleA": (0, 0, 0), "BottleB": (0.3, 0, 0), "CanA": (0.6, 0, 0), "CanB": (0.8, 0, 0), "Newspaper": (1.1, 0, 0),
      "Rag": (1.5, 0, 0), "Card": (2.1, 0, 0), "Debris": (2.9, 0, 0)}),
]
