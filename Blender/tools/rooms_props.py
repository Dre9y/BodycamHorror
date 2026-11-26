"""Dressing for the light fear level's working rooms (run after propkit.py, basement_textures.py, basement_kit.py and
basement_props.py; see basement_kit for the workflow):

    bake_all(["rooms"])

Pump room:   Pump_Unit       a motor and a centrifugal pump on a skid on a concrete plinth, the suction going down into
                             the floor, the discharge up through a gate valve and back to the wall, up into the ceiling;
                             a starter box on the wall. 2.0 x 0.8 m plinth, its back 0.72 m from the wall face (origin on
                             the floor under the plinth's middle, front -Y, the wall at +Y 0.72).
Workshop:    Butcher_Table   a butcher's block on steel legs, soaked in blood, a cleaver stuck in it, a saw and knives.
                             1.7 x 0.8 m, top at 0.9 m.
             Meat_Hooks      an I-beam rail 2.4 m long under the ceiling (3.2 m) on two rods, five trolleys with chains
                             and S-hooks, bloody. Origin on the floor under the rail's middle.
             Blood_Pools     a set: Blood_PoolA, Blood_PoolB, Blood_Smear, flat on the floor, each its own origin.
             Severed_Head    the police officer's head (helmet, gas mask) cut off the SWAT model at the neck, the cut
                             capped with flesh, the spine and the windpipe in it. Origin in the middle of the cut, the
                             cut facing -Z (it stands on it).
Switch room: Transformer     an oil transformer: tank with radiator banks, conservator, porcelain bushings, cables over to
                             a control cabinet in front. 2.4 m deep (Y: the cabinet's face at -1.2), 2.4 m wide, 2.35 m
                             tall. The level's fuse box hangs on the cabinet's face.
             Cable_Tray_2m   a ladder tray with five cables, 2 m along X, 0.3 m wide (origin: middle, tray bottom).
             Cable_Drop      four cables down a wall in clamps, 2 m tall (origin: the bottom, the wall at y = 0).
             Breaker_Panel   a wall board, its door hanging open, rows of breakers, wires spilling out of the bottom.
                             0.8 x 1.1 m (origin: the bottom middle, the wall at y = 0).
Everywhere:  Beacon_Lamp     the red emergency beacon (a grey can, a red glass dome, a turning reflector): see
                             build_beacon_lamp. Origin: the middle of its base, the dome up (+Z).
"""
import bpy, bmesh, math, os, random
from mathutils import Vector, Matrix, Euler, Quaternion, noise

# blood soaking into a surface and thickening: matte with a faint sheen (not a glossy film), uneven, darker where it
# has started to clot (the cavity / blotch layer), a fine congealed texture in the normals
PLOOK["blood"] = dict(base=((0.042, 0.004, 0.003), (0.018, 0.0022, 0.0016)), rough=0.58, metal=0.0,
                      wear=((0.03, 0.004, 0.003), 0.7, 0.0, 0.2), dirt=((0.016, 0.004, 0.003), 0.82, 0.0, 0.75), dust=0.04,
                      bump=0.55, wrinkle=0.12)
PLOOK["blood_dry"] = dict(base=((0.045, 0.012, 0.008), (0.026, 0.008, 0.005)), rough=0.6, metal=0.0, chips=0.3, chip_scale=14,
                          wear=((0.07, 0.025, 0.015), 0.65, 0.0, 0.5), dirt=((0.05, 0.015, 0.01), 0.7, 0.0, 0.7), dust=0.25, bump=0.35)
PLOOK["flesh"] = dict(base=((0.2, 0.025, 0.022), (0.1, 0.008, 0.007)), rough=0.28, metal=0.0,
                      wear=((0.5, 0.16, 0.12), 0.4, 0.0, 0.6), dirt=((0.07, 0.005, 0.004), 0.2, 0.0, 0.9), dust=0.0,
                      bump=0.6, wrinkle=0.5)
PLOOK["bone"] = dict(base=((0.64, 0.57, 0.45), (0.5, 0.42, 0.32)), rough=0.5, metal=0.0,
                     wear=((0.4, 0.1, 0.08), 0.4, 0.0, 0.6), dirt=((0.25, 0.05, 0.03), 0.4, 0.0, 0.8), dust=0.1)

def rot_to(o, axis):
    """Turn an object built along +Z so that axis is its +Z."""
    o.rotation_mode = 'QUATERNION'; o.rotation_quaternion = Vector(axis).normalized().to_track_quat('Z', 'Y'); return o

def blob(name, R, loc, mat, parent, seed=0, seg=48, rings=4, dome=0.0006, lumpy=0.35, stretch=(1.0, 1.0)):
    """A flat irregular splat facing +Z (a puddle, a stain), a little domed in the middle."""
    bm = bmesh.new(); off = seed * 3.7
    rad = []
    for k in range(seg):
        a = 2 * math.pi * k / seg; d = Vector((math.cos(a), math.sin(a), 0))
        n = noise.noise(d * 1.6 + Vector((off, 0, 0))) + 0.5 * noise.noise(d * 4.2 + Vector((0, off, 2)))
        rad.append(R * max(0.35, 1 + lumpy * n))
    c = bm.verts.new((0, 0, dome)); rs = []
    for j in range(1, rings + 1):
        t = j / rings
        rs.append([bm.verts.new((math.cos(2 * math.pi * k / seg) * rad[k] * t * stretch[0],
                                 math.sin(2 * math.pi * k / seg) * rad[k] * t * stretch[1], dome * (1 - t * t))) for k in range(seg)])
    for k in range(seg): bm.faces.new((c, rs[0][k], rs[0][(k + 1) % seg]))
    for a, b in zip(rs, rs[1:]):
        for k in range(seg):
            kk = (k + 1) % seg; bm.faces.new((a[k], b[k], b[kk], a[kk]))
    o = from_bm(name, bm, mat, parent); o.location = loc
    for p in o.data.polygons: p.use_smooth = True
    return o

def drip(name, top, length, r, mat, parent, out=Vector((0, 0, 0))):
    """A run of blood down a surface from top, swelling into a drop at the end."""
    top = Vector(top); pts = [top + out * 0.002 + Vector((0, 0, -length * k / 6)) for k in range(7)]
    sweep(name, pts, r, mat, parent, seg=8)
    sphere(name + "_Drop", r * 1.7, pts[-1] + Vector((0, 0, -r)), mat, parent, 10, 7)

# ================================================================== pump room

def build_pump_unit():
    rt = root("Pump_Unit")
    con, frm, mot, pmp, grd = role("concrete"), role("frame"), role("motor"), role("pump"), role("guard")
    pip, stl, whl, blt, cab = role("pipe"), role("steel"), role("wheel"), role("bolt"), role("cable")
    btr, btg, dial, ink = role("btn_r"), role("btn_g"), role("dial"), role("ink")
    box("Pump_Plinth", (2.0, 0.8, 0.1), (0, 0, 0.05), con, rt, bv=0.012)
    for y in (-0.3, 0.3): box("Pump_Rail", (1.9, 0.1, 0.14), (0, y, 0.17), frm, rt, bv=0.004)
    for x in (-0.85, 0.0, 0.85): box("Pump_Cross", (0.08, 0.5, 0.1), (x, 0, 0.15), frm, rt, bv=0.003)
    box("Pump_Deck", (1.9, 0.7, 0.012), (0, 0, 0.246), frm, rt, bv=0.002)
    for x in (-0.9, 0.9):
        for y in (-0.3, 0.3): cyl("Pump_AnchorNut", 0.018, 0.018, 0.025, (x, y, 0.26), blt, rt, seg=6)
    zc = 0.58
    # the motor (axis +X): fan cowl, finned body, end shield, shaft
    prof = [(0, 0.0), (0.18, 0.0), (0.2, 0.012), (0.205, 0.12), (0.19, 0.13), (0.19, 0.145)]
    for i in range(13):
        z = 0.15 + i * 0.037
        prof += [(0.21, z), (0.236, z + 0.008), (0.236, z + 0.019), (0.21, z + 0.027)]
    prof += [(0.205, 0.66), (0.15, 0.68), (0.06, 0.69), (0.026, 0.7), (0.026, 0.8), (0, 0.8)]
    o = lathe("Pump_Motor", prof, mot, rt, loc=(-0.95, 0, zc), seg=40); o.rotation_euler = (0, math.radians(90), 0)
    for k in range(10):   # the fan cowl's grille
        a = 2 * math.pi * k / 10
        rod("Pump_Grille", (-0.952, math.cos(a) * 0.03, zc + math.sin(a) * 0.03), (-0.952, math.cos(a) * 0.17, zc + math.sin(a) * 0.17), 0.004, stl, rt, seg=6)
    box("Pump_MotorFeet", (0.45, 0.42, 0.1), (-0.6, 0, 0.3), mot, rt, bv=0.006)
    box("Pump_Terminal", (0.18, 0.16, 0.12), (-0.55, 0, zc + 0.28), mot, rt, bv=0.008)
    box("Pump_TerminalLid", (0.19, 0.17, 0.015), (-0.55, 0, zc + 0.345), mot, rt, bv=0.004)
    torus("Pump_Eye", 0.03, 0.007, (-0.3, 0, zc + 0.27), stl, rt, seg=16, rseg=6, axis=(0, 1, 0))
    box("Pump_Plate", (0.12, 0.003, 0.07), (-0.62, -0.24, zc + 0.02), stl, rt, bv=0.001)
    # the coupling guard
    g = box("Pump_Guard", (0.32, 0.28, 0.3), (-0.02, 0, zc), grd, rt, bv=0.012)
    for x in (-0.09, -0.03, 0.03, 0.09): box("Pump_GuardSlot", (0.022, 0.29, 0.14), (x, 0, zc + 0.02), ink, rt, bv=0.003)
    box("Pump_GuardFoot", (0.3, 0.3, zc - 0.25 - 0.15), (-0.02, 0, 0.25 + (zc - 0.25 - 0.15) / 2), grd, rt, bv=0.004)
    # the pump: bearing frame on a pedestal, the volute, suction down into the floor, discharge up
    cyl("Pump_Bearing", 0.085, 0.085, 0.24, (0.25, 0, zc), pmp, rt, seg=32, axis=(1, 0, 0), bv=0.006)
    box("Pump_Pedestal", (0.14, 0.32, zc - 0.25), (0.25, 0, 0.25 + (zc - 0.25) / 2), pmp, rt, bv=0.006)
    vol = [(0, 0), (0.27, 0), (0.3, 0.03), (0.3, 0.16), (0.27, 0.19), (0.13, 0.21), (0.1, 0.24), (0, 0.24)]
    o = lathe("Pump_Volute", vol, pmp, rt, loc=(0.37, 0, zc), seg=40); o.rotation_euler = (0, math.radians(90), 0)
    box("Pump_VoluteFoot", (0.18, 0.36, 0.06), (0.49, 0, 0.27), pmp, rt, bv=0.005)
    for k in range(10):
        a = 2 * math.pi * (k + 0.5) / 10
        cyl("Pump_VoluteBolt", 0.012, 0.012, 0.03, (0.37, math.cos(a) * 0.27, zc + math.sin(a) * 0.27), blt, rt, seg=6, axis=(1, 0, 0))
    R = 0.085
    pts = [Vector((0.6, 0, zc)), Vector((0.82, 0, zc))]
    pts += arc((0.82, 0, zc - 0.22), 0.22, 90, 0, 8, 'XZ')[1:]
    pts += [Vector((1.04, 0, 0.1)), Vector((1.04, 0, -0.05))]
    sweep("Pump_Suction", pts, R, pip, rt, seg=24)
    flange("Pump_SuctionFlange", (0.64, 0, zc), (1, 0, 0), R, pip, blt, rt)
    flange("Pump_FloorFlange", (1.04, 0, 0.1), (0, 0, 1), R, pip, blt, rt)
    r = 0.065; x = 0.49
    rod("Pump_Nozzle", (x, 0, zc + 0.2), (x, 0, zc + 0.38), r, pmp, rt, seg=24)
    flange("Pump_NozzleFlange", (x, 0, zc + 0.37), (0, 0, 1), r, pip, blt, rt, bolts=6)
    # gate valve on the riser, a pressure gauge over it
    flange("Pump_ValveFlangeA", (x, 0, zc + 0.4), (0, 0, 1), r, pip, blt, rt, bolts=6)
    cyl("Pump_ValveBody", 0.1, 0.1, 0.22, (x, 0, zc + 0.52), whl, rt, seg=24, bv=0.006)
    flange("Pump_ValveFlangeB", (x, 0, zc + 0.64), (0, 0, 1), r, pip, blt, rt, bolts=6)
    rod("Pump_ValveStem", (x, -0.08, zc + 0.52), (x, -0.32, zc + 0.52), 0.022, stl, rt, seg=10)
    cyl("Pump_ValveBonnet", 0.05, 0.04, 0.12, (x, -0.15, zc + 0.52), whl, rt, seg=16, axis=(0, -1, 0), bv=0.004)
    handwheel("Pump_ValveWheel", (x, -0.33, zc + 0.52), (0, -1, 0), 0.13, whl, rt)
    top = zc + 0.66
    pts = [Vector((x, 0, top)), Vector((x, 0, 1.45))]
    pts += [Vector((x, 0.25 - 0.25 * math.cos(a * math.pi / 16), 1.45 + 0.25 * math.sin(a * math.pi / 16))) for a in range(1, 9)]
    pts += [Vector((x, 0.42, 1.7))]
    pts += [Vector((x, 0.42 + 0.2 * math.sin(a * math.pi / 16), 1.9 - 0.2 * math.cos(a * math.pi / 16))) for a in range(1, 9)]
    pts += [Vector((x, 0.62, 3.3))]
    sweep("Pump_Riser", pts, r, pip, rt, seg=24)
    rod("Pump_GaugeStub", (x, -0.05, 1.32), (x, -0.15, 1.32), 0.011, stl, rt, seg=8)
    cyl("Pump_GaugeCase", 0.065, 0.065, 0.035, (x, -0.17, 1.32), stl, rt, seg=32, axis=(0, -1, 0), bv=0.004)
    cyl("Pump_GaugeFace", 0.056, 0.056, 0.004, (x, -0.189, 1.32), dial, rt, seg=32, axis=(0, -1, 0))
    for k in range(9):
        a = math.radians(-135 + k * 33.75)
        box("Pump_GaugeTick", (0.002, 0.002, 0.01), (x + math.sin(a) * 0.045, -0.192, 1.32 + math.cos(a) * 0.045), ink, rt, bv=0,
            rot=Quaternion((0, 1, 0), -a))
    box("Pump_GaugeNeedle", (0.003, 0.002, 0.045), (x + 0.012, -0.193, 1.335), ink, rt, bv=0, rot=Quaternion((0, 1, 0), math.radians(-40)))
    for z in (2.2, 2.9):
        box("Pump_WallBracket", (0.05, 0.12, 0.03), (x, 0.66, z), blt, rt, bv=0.003)
        torus("Pump_RiserClamp", r + 0.006, 0.008, (x, 0.62, z), blt, rt, seg=24, rseg=6)
    # the starter on the wall, its conduit down to the motor's terminal box
    box("Pump_Starter", (0.3, 0.16, 0.42), (-0.72, 0.64, 1.4), mot, rt, bv=0.01)
    box("Pump_StarterLid", (0.28, 0.012, 0.4), (-0.72, 0.555, 1.4), mot, rt, bv=0.004)
    cyl("Pump_BtnR", 0.024, 0.024, 0.03, (-0.78, 0.54, 1.47), btr, rt, seg=20, axis=(0, -1, 0), bv=0.003)
    cyl("Pump_BtnG", 0.024, 0.024, 0.03, (-0.66, 0.54, 1.47), btg, rt, seg=20, axis=(0, -1, 0), bv=0.003)
    box("Pump_StarterLabel", (0.16, 0.002, 0.05), (-0.72, 0.548, 1.32), dial, rt, bv=0)
    text_mesh("Pump_StarterText", "НАСОС 1", "arialbd.ttf", 0.028, (-0.72, 0.546, 1.32), ink, rt, rot=(math.radians(90), 0, 0))
    sweep("Pump_Conduit", [(-0.72, 0.64, 1.19), (-0.72, 0.64, 0.98), (-0.7, 0.55, 0.9), (-0.6, 0.2, zc + 0.42), (-0.55, 0.05, zc + 0.37), (-0.55, 0.0, zc + 0.35)],
          0.013, cab, rt, seg=10)
    return rt

# ================================================================== workshop

def build_butcher_table():
    rt = root("Butcher_Table")
    wd, st, bl, bd, bla, hd, fl = role("wood"), role("steel"), role("blood"), role("blood_dry"), role("blade"), role("handle"), role("flesh")
    L, D, Ht = 1.7, 0.8, 0.9
    top = box("Table_Top", (L, D, 0.12), (0, 0, Ht - 0.06), wd, rt, bv=0.01)
    subdivide(top, 8)
    dent(top, (0.1, 0.05, 0.06), 0.45, 0.012)   # worn hollow where it is cut on
    dent(top, (-0.45, -0.1, 0.06), 0.25, 0.006)
    for k in range(9):   # the block's glued staves, seen as grooves on the front and back
        x = -L / 2 + L * (k + 1) / 10
        for y in (-D / 2 - 0.001, D / 2 + 0.001): box("Table_Stave", (0.004, 0.003, 0.11), (x, y, Ht - 0.06), bd, rt, bv=0)
    for x in (-L / 2 + 0.09, L / 2 - 0.09):
        for y in (-D / 2 + 0.09, D / 2 - 0.09):
            box("Table_Leg", (0.06, 0.06, Ht - 0.12), (x, y, (Ht - 0.12) / 2), st, rt, bv=0.004)
            cyl("Table_Foot", 0.04, 0.04, 0.012, (x, y, 0.006), st, rt, seg=16)
    for y in (-D / 2 + 0.09, D / 2 - 0.09): box("Table_RailL", (L - 0.18, 0.04, 0.04), (0, y, Ht - 0.15), st, rt, bv=0.003)
    for x in (-L / 2 + 0.09, L / 2 - 0.09): box("Table_RailS", (0.04, D - 0.18, 0.04), (x, 0, Ht - 0.15), st, rt, bv=0.003)
    for k in range(6): box("Table_Slat", (L - 0.2, 0.08, 0.02), (0, -D / 2 + 0.14 + k * 0.104, 0.2), wd, rt, bv=0.003)
    for y in (-D / 2 + 0.09, D / 2 - 0.09): box("Table_ShelfRail", (L - 0.18, 0.04, 0.03), (0, y, 0.18), st, rt, bv=0.003)
    # blood: a fresh pool where the head lies, older stains, runs over the front edge
    blob("Table_BloodA", 0.3, (0.12, 0.04, Ht + 0.0012), bl, rt, seed=1, stretch=(1.35, 0.85))
    blob("Table_BloodB", 0.13, (-0.42, -0.2, Ht + 0.0012), bl, rt, seed=5)
    blob("Table_StainA", 0.45, (-0.15, 0.1, Ht + 0.0006), bd, rt, seed=2, lumpy=0.5, stretch=(1.3, 0.7))
    blob("Table_StainB", 0.2, (0.6, -0.22, Ht + 0.0006), bd, rt, seed=9, lumpy=0.5)
    rnd = random.Random(3)
    for k in range(7):
        x = rnd.uniform(-0.1, 0.45)
        drip("Table_Drip", (x, -D / 2 - 0.004, Ht - 0.002), rnd.uniform(0.03, 0.1), rnd.uniform(0.003, 0.005), bl, rt)
    for k in range(4):
        x = rnd.uniform(-0.5, 0.3)
        drip("Table_DripDry", (x, -D / 2 - 0.003, Ht - 0.01), rnd.uniform(0.04, 0.1), rnd.uniform(0.003, 0.006), bd, rt)
    for k in range(5): blob("Table_Splat", rnd.uniform(0.015, 0.04), (rnd.uniform(-0.75, 0.75), rnd.uniform(-0.35, 0.35), Ht + 0.0015), bl, rt, seed=10 + k, seg=20, rings=2)
    # a cleaver stuck in the block, a saw and two knives
    q = Quaternion((1, 0, 0), math.radians(-14)) @ Quaternion((0, 0, 1), math.radians(25))
    cl = root("Table_Cleaver"); cl.parent = rt; cl.location = (0.58, 0.18, Ht + 0.045); cl.rotation_mode = 'QUATERNION'; cl.rotation_quaternion = q
    box("Table_CleaverBlade", (0.2, 0.005, 0.11), (0, 0, 0), bla, cl, bv=0.001)
    cyl("Table_CleaverHole", 0.012, 0.012, 0.007, (-0.07, 0, 0.03), wd, cl, seg=16, axis=(0, 1, 0))
    box("Table_CleaverSpine", (0.2, 0.009, 0.012), (0, 0, 0.055), bla, cl, bv=0.002)
    box("Table_CleaverHandle", (0.13, 0.024, 0.032), (0.165, 0, 0.04), hd, cl, bv=0.006)
    for xx in (0.135, 0.195): cyl("Table_CleaverRivet", 0.005, 0.005, 0.028, (xx, 0, 0.04), bla, cl, seg=8, axis=(0, 1, 0))
    box("Table_CleaverBlood", (0.09, 0.0062, 0.05), (-0.04, 0, -0.02), bl, cl, bv=0)
    # the saw: a bow frame and its blade, lying
    sw = root("Table_Saw"); sw.parent = rt; sw.location = (-0.52, 0.24, Ht + 0.012); sw.rotation_euler = (0, 0, math.radians(12))
    sweep("Table_SawBow", [(-0.27, 0, 0.0), (-0.27, 0.09, 0.0)] + [Vector((-0.27 + 0.54 * t / 10, 0.09 + 0.02 * math.sin(math.pi * t / 10), 0.0)) for t in range(1, 10)] +
          [(0.27, 0.09, 0.0), (0.27, 0.0, 0.0)], 0.007, st, sw, seg=8)
    box("Table_SawBlade", (0.56, 0.018, 0.002), (0, -0.002, -0.004), bla, sw, bv=0)
    box("Table_SawGrip", (0.11, 0.03, 0.03), (0.31, 0.04, 0.004), hd, sw, bv=0.008)
    for k, (x, y, a) in enumerate(((-0.1, -0.28, 80), (0.3, -0.3, 100))):
        kn = root("Table_Knife"); kn.parent = rt; kn.location = (x, y, Ht + 0.006); kn.rotation_euler = (0, 0, math.radians(a))
        box("Table_KnifeBlade", (0.2, 0.026, 0.0025), (0.0, 0, 0), bla, kn, bv=0, taper=None)
        box("Table_KnifeHandle", (0.11, 0.022, 0.018), (0.155, 0, 0.006), hd, kn, bv=0.005)
    return rt

def s_hook(name, at, mat, parent, flip=False):
    """An S-hook hanging from at (its top), the hook's mouth to +X (or -X when flipped), 0.21 m long."""
    sx = -1 if flip else 1; at = Vector(at)
    pts = arc((0, 0, -0.035), 0.035, 0, 180, 8, 'XZ')
    pts += [Vector((-0.035, 0, -0.16))]
    pts += arc((0.01, 0, -0.16), 0.045, 180, 360, 10, 'XZ')[1:]
    pts += [Vector((0.055, 0, -0.115))]
    pts = [at + Vector((p.x * sx, p.y, p.z)) for p in pts]
    sweep(name, pts, 0.0075, mat, parent, seg=10)
    cyl(name + "_Tip", 0.0075, 0.0012, 0.035, at + Vector((0.055 * sx, 0, -0.098)), mat, parent, seg=10)
    return at + Vector((0.01 * sx, 0, -0.205))   # the bottom of its bend

def build_meat_hooks():
    rt = root("Meat_Hooks")
    rl, ch, hk, hb, bl, fl, bn = role("rail"), role("chain"), role("hook"), role("hook_b"), role("blood"), role("flesh"), role("bone")
    L, zr, C = 2.4, 2.55, 3.2
    box("Hooks_FlangeTop", (L, 0.1, 0.012), (0, 0, zr + 0.07), rl, rt, bv=0.002)
    box("Hooks_Web", (L, 0.008, 0.13), (0, 0, zr), rl, rt, bv=0.001)
    box("Hooks_FlangeBot", (L, 0.1, 0.012), (0, 0, zr - 0.07), rl, rt, bv=0.002)
    for x in (-L / 2 + 0.01, L / 2 - 0.01): box("Hooks_Stop", (0.02, 0.12, 0.08), (x, 0, zr - 0.11), rl, rt, bv=0.003)
    for x in (-0.9, 0.9):
        rod("Hooks_Hanger", (x, 0, zr + 0.076), (x, 0, C + 0.02), 0.013, rl, rt, seg=10)
        box("Hooks_CeilPlate", (0.14, 0.14, 0.012), (x, 0, C - 0.006), rl, rt, bv=0.002)
        for dx in (-0.045, 0.045): cyl("Hooks_PlateBolt", 0.01, 0.01, 0.02, (x + dx, 0.045, C - 0.015), rl, rt, seg=6)
    rnd = random.Random(7)
    ends = []
    for i, x in enumerate((-1.0, -0.52, -0.06, 0.4, 0.92)):
        # trolley: side plates, wheels on the bottom flange, a bolt through to the chain
        for y in (-0.062, 0.062): box("Hooks_TrolleySide", (0.1, 0.006, 0.12), (x, y, zr - 0.1), rl, rt, bv=0.002)
        for dx in (-0.03, 0.03):
            for y in (-0.03, 0.03): cyl("Hooks_Wheel", 0.022, 0.022, 0.014, (x + dx, y, zr - 0.05), rl, rt, seg=16, axis=(0, 1, 0))
        rod("Hooks_TrolleyBolt", (x, -0.07, zr - 0.15), (x, 0.07, zr - 0.15), 0.008, rl, rt, seg=8)
        hook_top = rnd.uniform(1.75, 2.05)   # the hooks' bends at head height
        z = zr - 0.17; k = 0
        while z - 0.045 > hook_top:
            o = torus("Hooks_Link", 0.02, 0.0048, (x, 0, z), ch, rt, seg=14, rseg=6, axis=(0, 1, 0) if k % 2 == 0 else (1, 0, 0))
            o.scale = (1.0, 1.0, 1.0)
            z -= 0.042; k += 1
        bloody = i in (0, 1, 3)
        bottom = s_hook("Hooks_Hook", (x, 0, z), hb if bloody else hk, rt, flip=(i % 2 == 1))
        ends.append((x, z, bottom, bloody))
        if bloody:
            for d in range(rnd.randint(1, 3)):
                sphere("Hooks_Drop", rnd.uniform(0.005, 0.008), bottom + Vector((rnd.uniform(-0.01, 0.01), 0, -0.006 - d * 0.004)), bl, rt, 8, 6).scale = (1, 1, 1.8)
            for d in range(3):
                zz = z - rnd.uniform(0.04, 0.15)
                sphere("Hooks_Smear", 0.009, (x - 0.035 * (-1 if i % 2 else 1), 0, zz), bl, rt, 8, 6).scale = (0.8, 0.8, 2.2)
    return rt

def build_blood_pools():
    rt = root("Blood_Pools"); bl, bd = role("blood"), role("blood_dry")
    # A: the big pool under the hooks, an old ring dried round it
    blob("Blood_PoolA", 0.42, (-1.2, 0, 0.002), bl, rt, seed=11, stretch=(1.2, 0.9))
    blob("Blood_PoolA_Dry", 0.62, (-1.2, 0, 0.001), bd, rt, seed=12, lumpy=0.5, stretch=(1.25, 0.95))
    rnd = random.Random(2)
    for k in range(9):
        a = rnd.uniform(0, 2 * math.pi); d = rnd.uniform(0.6, 0.95)
        blob("Blood_PoolA_Drop", rnd.uniform(0.012, 0.035), (-1.2 + math.cos(a) * d, math.sin(a) * d * 0.8, 0.0025), bl, rt, seed=20 + k, seg=16, rings=2)
    # B: a smaller one with drops trailing off
    blob("Blood_PoolB", 0.22, (0.3, 0, 0.002), bl, rt, seed=31)
    for k in range(7):
        blob("Blood_PoolB_Drop", rnd.uniform(0.01, 0.03), (0.3 + 0.25 + k * 0.09 + rnd.uniform(-0.02, 0.02), rnd.uniform(-0.04, 0.04), 0.0025), bl, rt, seed=40 + k, seg=16, rings=2)
    # a smear: something heavy dragged through it
    for k in range(10):
        t = k / 9
        blob("Blood_Smear_Part", 0.1 * (1 - 0.6 * t) + 0.03, (1.6 + t * 1.4, 0.06 * math.sin(t * 5), 0.0015 + 0.0001 * k), bd if k > 5 else bl, rt,
             seed=60 + k, lumpy=0.45, stretch=(1.6, 0.6))
    return rt

SWAT = os.path.join(PROJECT, "Assets", "Character", "SWAT", "SwatOperator.fbx")

def build_severed_head():
    rt = root("Severed_Head"); fl, bl, bn = role("flesh"), role("blood"), role("bone")
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=SWAT)
    new = [o for o in bpy.data.objects if o not in before]
    body = next(o for o in new if o.type == 'MESH')
    mw = body.matrix_world.copy()
    for m in list(body.modifiers): body.modifiers.remove(m)
    body.parent = None; body.data.transform(mw); body.matrix_world = Matrix.Identity(4)
    for o in new:
        if o != body: bpy.data.objects.remove(o, do_unlink=True)
    body.vertex_groups.clear()
    # its own textures read through its own UVs (the bake makes another layer); no spec / gloss maps on disk
    uvname = body.data.uv_layers[0].name
    for i, m in enumerate(body.data.materials):
        m.name = f"Head_Skin{i}"
        nt = m.node_tree; b = nt.nodes["Principled BSDF"]
        for inp in ("Roughness", "Specular IOR Level", "Metallic"):
            for lk in list(b.inputs[inp].links): nt.links.remove(lk)
        b.inputs["Roughness"].default_value = 0.55
        uvn = nt.nodes.new("ShaderNodeUVMap"); uvn.uv_map = uvname
        for n in nt.nodes:
            if n.type == 'TEX_IMAGE': nt.links.new(uvn.outputs["UV"], n.inputs["Vector"])
            if n.type == 'NORMAL_MAP': n.uv_map = uvname
        v = nt.nodes.new("ShaderNodeValue"); v.outputs[0].default_value = 0.0
        nt.links.new(v.outputs[0], b.inputs["Metallic"]); m["metal_src"] = v.name
    body.data.materials.append(fl); capmat = len(body.data.materials) - 1
    # off at the neck: everything but the head and neck goes, then a slanted cut, the wound capped
    bm = bmesh.new(); bm.from_mesh(body.data)
    axis = Vector((0, 0.03, 0))
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if (v.co.xy - axis.xy).length > 0.22 or v.co.z < 1.36], context='VERTS')
    p = Vector((0, 0.03, 1.495)); n = Vector((0, 0.16, 1)).normalized()
    res = bmesh.ops.bisect_plane(bm, geom=bm.verts[:] + bm.edges[:] + bm.faces[:], dist=0.0002, plane_co=p, plane_no=n, clear_inner=True)
    # whatever is left hanging loose (bits of the collar caught by the radius) goes
    bm.verts.ensure_lookup_table()
    seen = set(); islands = []
    for v in bm.verts:
        if v in seen: continue
        stack = [v]; isl = []; seen.add(v)
        while stack:
            a = stack.pop(); isl.append(a)
            for e in a.link_edges:
                b = e.other_vert(a)
                if b not in seen: seen.add(b); stack.append(b)
        islands.append(isl)
    for isl in islands:
        wide = max(v.co.x for v in isl) - min(v.co.x for v in isl)
        if len(isl) < 40 or wide > 0.3: bmesh.ops.delete(bm, geom=isl, context='VERTS')   # bits, and the collar's flap over the shoulders
    old = set(bm.faces)
    on_cut = lambda e: all(abs((v.co - p).dot(n)) < 0.002 for v in e.verts)   # only the wound: the mask's own openings stay open
    bmesh.ops.holes_fill(bm, edges=[e for e in bm.edges if e.is_boundary and on_cut(e)], sides=0)
    caps = [f for f in bm.faces if f not in old]
    bmesh.ops.triangulate(bm, faces=caps)
    for f in bm.faces:
        if f not in old: f.material_index = capmat
    bm.to_mesh(body.data); bm.free()
    # stood on the cut: the cut's normal down, the middle of the wound at the origin
    q = n.rotation_difference(Vector((0, 0, 1)))
    me = body.data
    me.transform(Matrix.Translation(-p)); me.transform(q.to_matrix().to_4x4())
    cap = [v.co.copy() for v in me.vertices if abs(v.co.z) < 0.003]
    c = sum(cap, Vector()) / max(len(cap), 1)
    me.transform(Matrix.Translation(Vector((-c.x, -c.y, 0))))
    body.name = "Head_Skin"; body.parent = rt
    for poly in me.polygons:
        if poly.material_index == capmat: poly.use_smooth = False
    # in the wound: the spine's stump and the windpipe, a rim of blood round the cut
    def under(name, R, loc, mat, seed, lumpy=0.2):
        o = blob(name, R, loc, mat, rt, seed=seed, seg=24, rings=2, dome=0.0, lumpy=lumpy); o.rotation_euler = (math.pi, 0, 0); return o
    under("Head_Spine", 0.017, (0, 0.035, -0.0015), bn, 3)
    under("Head_SpineCord", 0.006, (0, 0.035, -0.0025), bl, 4, 0.1)
    under("Head_Pipe", 0.013, (0, -0.035, -0.0015), bn, 5)
    under("Head_PipeHole", 0.008, (0, -0.035, -0.0025), bl, 6, 0.1)
    under("Head_WoundBlood", 0.05, (0, 0, -0.0008), bl, 7, 0.5)
    return rt

# ================================================================== switch room

def build_transformer():
    rt = root("Transformer")
    tk, fn, fr, pc, br = role("tank"), role("fins"), role("frame"), role("porcelain"), role("brass")
    cbn, sg, ink, cb, st, bt = role("cabinet"), role("sign"), role("ink"), role("cable"), role("steel"), role("bolt")
    for x in (-0.7, 0.7):
        box("Tr_Skid", (0.14, 2.2, 0.16), (x, 0.1, 0.08), fr, rt, bv=0.004)
        for y in (-0.9, 1.1): cyl("Tr_Wheel", 0.07, 0.07, 0.05, (x, y, 0.07), fr, rt, seg=20, axis=(1, 0, 0), bv=0.004)
    TY, TD, TW, TH, z0 = 0.25, 1.25, 1.45, 1.5, 0.16
    t = box("Tr_Tank", (TW, TD, TH), (0, TY, z0 + TH / 2), tk, rt, bv=0.025)
    subdivide(t, 4); jitter(t, 0.004, 3, 1)
    zt = z0 + TH + 0.05
    box("Tr_Cover", (TW + 0.1, TD + 0.1, 0.05), (0, TY, zt - 0.025), tk, rt, bv=0.006)
    for k in range(int((TW + 0.04) / 0.14) + 1):
        xx = -TW / 2 - 0.01 + k * 0.14
        for yy in (TY - TD / 2 - 0.02, TY + TD / 2 + 0.02): cyl("Tr_CoverBolt", 0.011, 0.011, 0.03, (xx, yy, zt + 0.004), bt, rt, seg=6)
    for sx in (-1, 1):
        for sy in (-1, 1):
            box("Tr_Lug", (0.1, 0.02, 0.1), (sx * (TW / 2 - 0.05), TY + sy * (TD / 2 + 0.06), zt + 0.03), fr, rt, bv=0.004)
    for x in (-0.4, 0.4): box("Tr_Stiffener", (0.06, 0.04, TH * 0.92), (x, TY - TD / 2 - 0.018, z0 + TH / 2), tk, rt, bv=0.004)
    # radiator banks on both sides: fins out from the tank, a header top and bottom
    for s in (-1, 1):
        x0 = s * (TW / 2 + 0.06); xm = s * (TW / 2 + 0.06 + 0.22)
        for y in [TY - 0.5 + k * 0.0714 for k in range(15)]:
            f = box("Tr_Fin", (0.42, 0.012, 1.1), (xm, y, z0 + 0.73), fn, rt, bv=0.004)
        for hz in (z0 + 0.15, z0 + 1.31):
            rod("Tr_Header", (xm, TY - 0.56, hz), (xm, TY + 0.56, hz), 0.035, fn, rt, seg=16)
            rod("Tr_Neck", (s * TW / 2, TY, hz), (x0, TY, hz), 0.045, fn, rt, seg=16)
            flange("Tr_NeckFlange", (s * (TW / 2 + 0.03), TY, hz), (s, 0, 0), 0.045, fn, bt, rt, bolts=6)
    # conservator on brackets along the back, its gauge, the pipe down to the cover with the relay on it
    cy, cz = TY + 0.42, zt + 0.42
    prof = [(0, 0), (0.1, 0.004), (0.16, 0.02), (0.18, 0.05)] + line_prof(0.18, 0.05, 0.18, 1.05, 4) + [(0.16, 1.08), (0.1, 1.096), (0, 1.1)]
    o = lathe("Tr_Conservator", prof, tk, rt, loc=(-0.55, cy, cz), seg=32); o.rotation_euler = (0, math.radians(90), 0)
    for x in (-0.35, 0.35):
        beam("Tr_ConsLeg", (x, cy, zt), (x, cy, cz - 0.12), 0.06, 0.06, fr, rt)
        torus("Tr_ConsStrap", 0.185, 0.008, (x, cy, cz), fr, rt, seg=32, rseg=6, axis=(1, 0, 0))
    cyl("Tr_OilGauge", 0.05, 0.05, 0.03, (0.565, cy, cz), st, rt, seg=24, axis=(1, 0, 0), bv=0.003)
    cyl("Tr_OilGaugeFace", 0.04, 0.04, 0.004, (0.58, cy, cz), pc, rt, seg=24, axis=(1, 0, 0))
    rod("Tr_Buchholz", (0.25, cy - 0.1, cz - 0.1), (0.25, TY + 0.05, zt + 0.01), 0.025, tk, rt, seg=12)
    box("Tr_Relay", (0.1, 0.12, 0.1), (0.25, (cy - 0.1 + TY + 0.05) / 2, (cz - 0.1 + zt) / 2), tk, rt, bv=0.006)
    sweep("Tr_Breather", [(-0.4, cy, cz - 0.17), (-0.4, cy + 0.15, cz - 0.3), (-0.4, cy + 0.2, zt - 0.3)], 0.012, st, rt, seg=8)
    cyl("Tr_BreatherCan", 0.045, 0.045, 0.18, (-0.4, cy + 0.2, zt - 0.4), pc, rt, seg=20, bv=0.004)
    # bushings: three tall high-voltage ones behind, four short low-voltage ones in front
    def bushing(name, x, y, sheds, step, rmax):
        prof = [(0, 0), (rmax * 0.9, 0), (rmax * 0.9, 0.035)]
        z = 0.05
        for i in range(sheds):
            prof += [(rmax * 0.48, z), (rmax, z + step * 0.25), (rmax, z + step * 0.4), (rmax * 0.48, z + step * 0.65)]
            z += step
        prof += [(rmax * 0.42, z), (rmax * 0.25, z + 0.015), (0, z + 0.015)]
        lathe(name, prof, pc, rt, loc=(x, y, zt), seg=24)
        cyl(name + "_Base", rmax * 1.05, rmax * 1.05, 0.03, (x, y, zt + 0.015), tk, rt, seg=24, bv=0.003)
        cyl(name + "_Stud", 0.014, 0.014, 0.07, (x, y, zt + z + 0.05), br, rt, seg=12)
        return Vector((x, y, zt + z + 0.06))
    for x in (-0.42, 0.0, 0.42): bushing("Tr_HV", x, TY + 0.05, 6, 0.06, 0.085)
    tops = [bushing("Tr_LV", x, TY - 0.42, 3, 0.05, 0.05) for x in (-0.45, -0.15, 0.15, 0.45)]
    # the low-voltage cables over the front edge and into the cabinet's roof
    for k, tp in enumerate(tops):
        sweep("Tr_Cable", [tp, tp + Vector((0, -0.08, 0.04)), Vector((tp.x * 0.8, TY - TD / 2 - 0.15, tp.z + 0.02)),
                           Vector((tp.x * 0.5, -0.78, 2.05)), Vector((tp.x * 0.35, -1.0, 1.96)), Vector((tp.x * 0.35, -1.0, 1.9))], 0.022, cb, rt, seg=10)
    # the control cabinet in front: its face at y = -1.2, the fuse box goes on the clear upper half
    CW, CD = 1.0, 0.3; cyc = -1.2 + CD / 2
    box("Tr_Cabinet", (CW, CD, 1.85), (0, cyc, 0.08 + 0.925), cbn, rt, bv=0.01)
    box("Tr_CabPlinth", (CW + 0.02, CD + 0.02, 0.08), (0, cyc, 0.04), fr, rt, bv=0.004)
    box("Tr_CabRoof", (CW + 0.06, CD + 0.06, 0.03), (0, cyc, 1.945), cbn, rt, bv=0.006)
    box("Tr_CabDoor", (CW - 0.08, 0.014, 0.95), (0, -1.206, 0.6), cbn, rt, bv=0.004)
    for z in (0.18, 1.02): cyl("Tr_CabHinge", 0.009, 0.009, 0.06, (-CW / 2 + 0.045, -1.214, z), st, rt, seg=10)
    rod("Tr_CabHandle", (0.4, -1.236, 0.52), (0.4, -1.236, 0.72), 0.009, st, rt, seg=10)
    for z in (0.52, 0.72): rod("Tr_CabHandleFoot", (0.4, -1.212, z), (0.4, -1.236, z), 0.007, st, rt, seg=8)
    for k in range(6): box("Tr_CabLouver", (0.3, 0.02, 0.012), (-0.2, -1.216, 0.2 + k * 0.03), ink, rt, bv=0, rot=Quaternion((1, 0, 0), math.radians(35)))
    box("Tr_Plate", (0.24, 0.004, 0.12), (-0.3, -1.214, 0.98), st, rt, bv=0.001)
    text_mesh("Tr_PlateText", "ТМ-630/10", "arialbd.ttf", 0.03, (-0.3, -1.2165, 1.0), ink, rt, rot=(math.radians(90), 0, 0))
    text_mesh("Tr_PlateText2", "10/0,4 кВ  1979", "arialbd.ttf", 0.018, (-0.3, -1.2165, 0.955), ink, rt, rot=(math.radians(90), 0, 0))
    def tri(name, size, at, mat):
        bm = bmesh.new(); vs = [bm.verts.new((size * math.sin(a), 0, size * math.cos(a))) for a in (0, 2 * math.pi / 3, 4 * math.pi / 3)]
        f = bm.faces.new(vs); bmesh.ops.solidify(bm, geom=[f], thickness=0.001)
        o = from_bm(name, bm, mat, rt); o.location = at; return o
    tri("Tr_SignBorder", 0.11, (0.12, -1.2135, 0.72), ink); tri("Tr_Sign", 0.092, (0.12, -1.2145, 0.72), sg)
    text_mesh("Tr_SignBolt", "!", "arialbd.ttf", 0.09, (0.12, -1.2155, 0.71), ink, rt, rot=(math.radians(90), 0, 0))
    text_mesh("Tr_Danger", "ОПАСНО  10 кВ", "arialbd.ttf", 0.03, (0.12, -1.2135, 0.56), ink, rt, rot=(math.radians(90), 0, 0))
    box("Tr_Duct", (0.4, 0.42, 0.2), (0, -0.66, 1.8), cbn, rt, bv=0.008)   # cable duct from the cabinet's back to the tank
    sweep("Tr_Earth", [(TW / 2 - 0.05, TY - TD / 2 - 0.01, z0 + 0.1), (TW / 2 + 0.05, TY - TD / 2 - 0.12, 0.08), (TW / 2 + 0.2, TY - TD / 2 - 0.25, 0.012),
                       (TW / 2 + 0.25, -1.0, 0.012)], 0.012, br, rt, seg=8)
    cyl("Tr_DrainValve", 0.03, 0.03, 0.08, (-0.5, TY - TD / 2 - 0.04, z0 + 0.12), br, rt, seg=12, axis=(0, -1, 0))
    return rt

def build_cable_tray():
    rt = root("Cable_Tray_2m"); tr, cb, cg = role("tray"), role("cable"), role("cable_g")
    L, W = 2.0, 0.3
    for s in (-1, 1):
        box("Tray_Side", (L, 0.004, 0.06), (0, s * W / 2, 0.03), tr, rt, bv=0.001)
        box("Tray_Lip", (L, 0.022, 0.004), (0, s * (W / 2 - 0.011), 0.06), tr, rt, bv=0.001)
    for k in range(8): box("Tray_Rung", (0.03, W, 0.006), (-L / 2 + 0.125 + k * 0.25, 0, 0.003), tr, rt, bv=0.001)
    rnd = random.Random(5)
    for k, (y, r) in enumerate(((-0.105, 0.018), (-0.06, 0.014), (-0.015, 0.02), (0.035, 0.012), (0.085, 0.016), (0.03, 0.01))):
        z = 0.006 + r + (0.025 if k == 5 else 0)
        ph = rnd.uniform(0, 6)
        pts = [Vector((-L / 2 + L * t / 20, y + 0.006 * math.sin(t * 0.9 + ph), z + 0.003 * max(0, math.sin(t * 1.3 + ph)))) for t in range(21)]
        sweep("Tray_Cable", pts, r, cg if k in (1, 4) else cb, rt, seg=10)
    return rt

def build_cable_drop():
    rt = root("Cable_Drop"); cb, cl = role("cable"), role("clamp")
    Hh = 2.0
    for k, x in enumerate((-0.05, -0.017, 0.017, 0.05)):
        r = 0.013 if k in (1, 2) else 0.011
        pts = [Vector((x, -0.016 - r - 0.004 * abs(math.sin(math.pi * (Hh * t / 20) / 0.4)), Hh * t / 20)) for t in range(21)]
        sweep("Drop_Cable", pts, r, cb, rt, seg=10)
    for k in range(5):
        z = 0.15 + k * 0.42
        box("Drop_Strap", (0.14, 0.006, 0.025), (0, -0.046, z), cl, rt, bv=0.001)
        for x in (-0.072, 0.072):
            box("Drop_StrapFoot", (0.012, 0.04, 0.025), (x, -0.022, z), cl, rt, bv=0.001)
            cyl("Drop_Screw", 0.007, 0.007, 0.006, (x, -0.045, z), cl, rt, seg=8, axis=(0, -1, 0))
    return rt

def build_breaker_panel():
    rt = root("Breaker_Panel")
    pt, st, ink, sg, wb = role("paint"), role("steel"), role("ink"), role("sign"), role("breaker")
    wr, wy, cb, cu, bu = role("wire_r"), role("wire_y"), role("cable"), role("copper"), role("burnt")
    W, D, H = 0.8, 0.22, 1.1
    box("Panel_Back", (W, 0.012, H), (0, -0.006, H / 2), pt, rt, bv=0.003)
    for s in (-1, 1): box("Panel_Side", (0.015, D, H), (s * (W / 2 - 0.0075), -D / 2, H / 2), pt, rt, bv=0.003)
    box("Panel_Top", (W, D, 0.015), (0, -D / 2, H - 0.0075), pt, rt, bv=0.003)
    box("Panel_Bottom", (W, D, 0.015), (0, -D / 2, 0.0075), pt, rt, bv=0.003)
    box("Panel_Plate", (W - 0.06, 0.006, H - 0.08), (0, -0.016, H / 2), st, rt, bv=0.002)
    rnd = random.Random(9)
    tips = []
    for row, z in enumerate((0.3, 0.56, 0.82)):
        box("Panel_Rail", (W - 0.1, 0.01, 0.035), (0, -0.024, z), st, rt, bv=0.001)
        for k in range(18):
            x = -W / 2 + 0.08 + k * 0.0355
            if rnd.random() < 0.12: continue   # a gap where one was pulled out
            burnt = rnd.random() < 0.08
            box("Panel_Breaker", (0.034, 0.07, 0.09), (x, -0.064, z), bu if burnt else wb, rt, bv=0.003)
            up = rnd.random() < 0.6
            box("Panel_Toggle", (0.012, 0.022, 0.018), (x, -0.105, z + (0.012 if up else -0.012)), ink, rt, bv=0.002)
            if rnd.random() < 0.5: tips.append(Vector((x, -0.07, z - 0.05)))
    # wires out of the breakers, down in a loose bunch and out of the bottom, hanging
    for k, tp in enumerate(tips):
        mat = (wr, wy, cb)[k % 3]
        hang = rnd.uniform(0.15, 0.55)
        pts = [tp, tp + Vector((0, -0.015, -0.06)), Vector((tp.x * 0.5 + rnd.uniform(-0.04, 0.04), -0.12 + rnd.uniform(-0.03, 0.03), 0.12)),
               Vector((tp.x * 0.3 + rnd.uniform(-0.05, 0.05), -0.16 + rnd.uniform(-0.03, 0.03), -0.05)),
               Vector((tp.x * 0.35 + rnd.uniform(-0.08, 0.08), -0.18 + rnd.uniform(-0.05, 0.03), -hang))]
        sweep("Panel_Wire", pts, 0.0045, mat, rt, seg=6)
        cyl("Panel_WireTip", 0.0028, 0.0028, 0.02, pts[-1] + Vector((0, 0, -0.008)), cu, rt, seg=6)
    for x in (-0.25, 0.0, 0.25):
        rod("Panel_Conduit", (x, -0.07, H), (x, -0.07, H + 0.5), 0.016, st, rt, seg=10)
        cyl("Panel_Gland", 0.024, 0.024, 0.025, (x, -0.07, H + 0.012), st, rt, seg=6)
    # the door, hanging open on its hinges at the left
    a = math.radians(105)
    dr = root("Panel_DoorPivot"); dr.parent = rt; dr.location = (-W / 2, -D - 0.008, 0); dr.rotation_euler = (0, 0, -a)
    box("Panel_Door", (W, 0.016, H - 0.01), (W / 2, 0, H / 2), pt, dr, bv=0.004)
    box("Panel_DoorRidge", (W - 0.1, 0.004, H - 0.1), (W / 2, -0.01, H / 2), pt, dr, bv=0.002)
    cyl("Panel_DoorLock", 0.014, 0.014, 0.014, (W - 0.06, -0.012, H / 2), st, dr, seg=6, axis=(0, 1, 0), bv=0.001)
    box("Panel_SignBorder", (0.2, 0.002, 0.12), (W / 2, -0.012, H - 0.22), ink, dr, bv=0)
    box("Panel_Sign", (0.19, 0.002, 0.11), (W / 2, -0.0135, H - 0.22), sg, dr, bv=0)
    text_mesh("Panel_Text", "ЩР-2  380В", "arialbd.ttf", 0.03, (W / 2, -0.0155, H - 0.22), ink, dr, rot=(math.radians(90), 0, 0))
    for z in (0.15, H - 0.15): cyl("Panel_Hinge", 0.008, 0.008, 0.05, (-W / 2, -D - 0.008, z), st, rt, seg=10)
    return rt

# ================================================================== the emergency beacon

PLOOK["lamp_grey"] = dict(base=((0.3, 0.31, 0.3), (0.24, 0.25, 0.24)), rough=0.55, metal=0.0, chips=0.15, chip_scale=10,
                          wear=((0.2, 0.19, 0.17), 0.6, 0.3, 0.5), dirt=((0.12, 0.1, 0.08), 0.85, 0.0, 0.8), dust=0.6, bottom=0.3)

def build_beacon_lamp():
    """The emergency beacon after the photo: a grey pressed-steel can with a rolled cap, a rubber gasket, a red glass
    dome. Inside it a bulb on the axis and a reflector that turns round it (Beacon_Mirror, its own mesh with its origin
    on the axis). Built dome up; the level hangs it from the ceiling, dome down. ~16 cm across."""
    rt = root("Beacon_Lamp")
    pt, rb, br, bk, ch = role("paint"), role("rubber"), role("brass"), role("bakelite"), role("chrome")
    can = lathe("Beacon_Can", [(0, 0), (0.068, 0), (0.072, 0.003), (0.074, 0.012)] + line_prof(0.074, 0.012, 0.075, 0.055, 4) +
                [(0.077, 0.058), (0.081, 0.061), (0.083, 0.066), (0.083, 0.084), (0.081, 0.089), (0.076, 0.092), (0.062, 0.094),
                 (0.058, 0.096), (0, 0.096)], pt, rt, seg=48)
    dent(can, (0.074, -0.02, 0.03), 0.03, 0.0025); dent(can, (-0.05, 0.055, 0.04), 0.025, 0.002)
    torus("Beacon_Gasket", 0.0575, 0.0035, (0, 0, 0.097), rb, rt, seg=40, rseg=6)
    for a in (35, 215):
        x, y = math.cos(math.radians(a)) * 0.07, math.sin(math.radians(a)) * 0.07
        cyl("Beacon_Screw", 0.0045, 0.0045, 0.003, (x, y, 0.0955), br, rt, seg=12)
        box("Beacon_ScrewSlot", (0.006, 0.0008, 0.001), (x, y, 0.0972), bk, rt, bv=0, rot=Quaternion((0, 0, 1), math.radians(a)))
    # the brass gland on the cap, the switch on the side
    box("Beacon_Gland", (0.022, 0.014, 0.012), (0.035, -0.066, 0.092), br, rt, bv=0.003)
    cyl("Beacon_GlandNut", 0.005, 0.005, 0.012, (0.035, -0.066, 0.1), br, rt, seg=6)
    box("Beacon_Switch", (0.016, 0.026, 0.03), (-0.081, 0.0, 0.04), bk, rt, bv=0.004)
    # the red dome (its own material in the level), the bulb in it
    prof = [(0.056 * math.cos(math.radians(a)), 0.096 + 0.056 * math.sin(math.radians(a))) for a in range(0, 91, 6)]
    o = lathe("Beacon_Glass", prof, glass_mat(), rt, seg=40); no_bake(o)
    o = lathe("Beacon_Bulb", [(0.006, 0.1), (0.007, 0.11), (0.014, 0.122), (0.015, 0.132), (0.011, 0.142), (0, 0.145)], bulb_mat(), rt, seg=20); no_bake(o)
    cyl("Beacon_Socket", 0.011, 0.011, 0.012, (0, 0, 0.1), bk, rt, seg=16)
    # the reflector: a polished dish opening sideways (+X), on a little bracket round the bulb's axis
    dish = lathe("Beacon_Mirror", [(0.0, -0.014)] + [(0.024 * t / 6, -0.014 + 0.014 * (t / 6) ** 2) for t in range(1, 7)] +
                 [(0.0245, 0.001)], ch, rt, seg=32)
    dish.location = (-0.006, 0, 0.12); dish.rotation_euler = (0, math.radians(90), 0)
    box("Beacon_Mirror_Arm", (0.016, 0.005, 0.003), (-0.014, 0, 0.098), ch, rt, bv=0.001)
    return rt

C_BEACON = CFG(paint="lamp_grey", rubber="cable", brass="brass", bakelite="cable", chrome="steel")
C_BEACON["chrome"]["metal"] = 0.95

C_PUMP = CFG(concrete="concrete", frame="grey_paint", motor="green_paint", pump="pipe_paint", guard="yellow_paint", pipe="pipe_paint",
             steel="steel", wheel="cast_iron", bolt="rust_heavy", cable="cable", btn_r="red_paint", btn_g="green_paint", dial="porcelain", ink="ink")
C_TABLE = CFG(wood="wood_dark", steel="steel", blood="blood", blood_dry="blood_dry", blade="steel", handle="wood_worn", flesh="flesh")
C_TABLE["wood"]["base"] = dict(C_TABLE["wood"]["base"], hsv=(0.5, 0.75, 0.3), tint=(0.75, 0.55, 0.5))
C_HOOKS = CFG(rail="rust_heavy", chain="rust_heavy", hook="rust_heavy", hook_b="blood_dry", blood="blood", flesh="flesh", bone="bone")
C_BLOOD = CFG(blood="blood", blood_dry="blood_dry")
C_HEAD = CFG(flesh="flesh", blood="blood", bone="bone")
C_TRANS = CFG(tank="grey_paint", fins="grey_paint", frame="rust_heavy", porcelain="porcelain", brass="brass", cabinet="green_paint",
              sign="yellow_paint", ink="ink", cable="cable", steel="steel", bolt="rust_heavy")
C_TRAY = CFG(tray="steel", cable="cable", cable_g="rubber")
C_DROP = CFG(cable="cable", clamp="steel")
C_PANEL = CFG(paint="grey_paint", steel="steel", ink="ink", sign="yellow_paint", breaker="white_paint", wire_r="red_paint",
              wire_y="yellow_paint", cable="cable", copper="brass", burnt="ink")

GROUPS["rooms"] = [
    ("Pump_Unit", build_pump_unit, C_PUMP, 2048, None, None),
    ("Butcher_Table", build_butcher_table, C_TABLE, 2048, None, None),
    ("Meat_Hooks", build_meat_hooks, C_HOOKS, 2048, None, None),
    ("Blood_Pools", build_blood_pools, C_BLOOD, 1024, "prefix:Blood_PoolA,Blood_PoolB,Blood_Smear",
     {"Blood_PoolA": (-1.2, 0, 0), "Blood_PoolB": (0.3, 0, 0), "Blood_Smear": (1.6, 0, 0)}),
    ("Severed_Head", build_severed_head, C_HEAD, 1024, None, None),
    ("Transformer", build_transformer, C_TRANS, 2048, None, None),
    ("Cable_Tray_2m", build_cable_tray, C_TRAY, 1024, None, None),
    ("Cable_Drop", build_cable_drop, C_DROP, 512, None, None),
    ("Breaker_Panel", build_breaker_panel, C_PANEL, 1024, None, None),
    ("Beacon_Lamp", build_beacon_lamp, C_BEACON, 512, {"Beacon_Mirror": ["Beacon_Mirror", "Beacon_Mirror_Arm"], "Beacon_Body": None},
     {"Beacon_Mirror": (0, 0, 0.12)}),
]
