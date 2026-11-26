"""The light fear level's emergency power: the hand crank in the switch room and the fuse it needs.
Run inside Blender over the bridge, after the basement kit:

    exec(open(r"E:\\Unity\\Projects\\BodycamHorror\\Blender\\tools\\propkit.py").read())
    exec(open(r"E:\\Unity\\Projects\\BodycamHorror\\Blender\\tools\\basement_textures.py").read())
    exec(open(r"E:\\Unity\\Projects\\BodycamHorror\\Blender\\tools\\basement_kit.py").read())
    exec(open(r"E:\\Unity\\Projects\\BodycamHorror\\Blender\\tools\\emergency_power.py").read())
    bake_all(["emergency"])       # build, bake, FBX -> Assets/Generated/Imported/Basement (see Blender/bake_log.txt)

EmergencyCrank: a cast gearbox of a manual drive on a wall plate, the output shaft running up to the ceiling in a tube.
    Its back is on the wall (y = 0), the origin on the crank's axis; the shaft end is at y = -SHAFT_Y.
EmergencyCrank_Handle: the part that turns, its own prop so Unity can spin it alone. Origin on the axis at the shaft
    end; the arm starts pointing along +X (+X in Blender = -X in Unity), the grip sticks out towards -Y.
Fuse: a PN-2 style knife fuse, its length along X, the blades in the XZ plane, origin in its middle.
FuseHolder: the fuse's base on the fuse box door, back on the door (y = 0); a fuse sits in it upright with its axis
    at (0, FUSE_AXIS_Y, 0) and its blades between the jaws (blade plane YZ).
"""
import bpy, bmesh, math
from mathutils import Vector, Matrix, noise

SHAFT_Y = 0.215          # the shaft's end face, out from the wall
FUSE_AXIS_Y = -0.046     # a fuse in the holder: its axis out from the door
FUSE_BLADE_Z = 0.0615    # its blades' middles above / below its centre

# ------------------------------------------------------------------ helpers

def tapered_bar(name, length, w0, w1, t, mat, parent, loc=(0, 0, 0), bv=0.004):
    """A flat bar along +X from x=0, w0 wide at the root narrowing to w1, t thick (Y)."""
    o = box(name, (length, t, 1.0), (0, 0, 0), mat, parent, bv=0)
    for v in o.data.vertices:
        v.co.x += length / 2
        k = v.co.x / length
        v.co.z *= w0 + (w1 - w0) * k
    o.location = loc
    if bv: bevel(o, bv, 3)
    return o

def ribbon_arc(name, r, a0, a1, width, y, mat, parent, steps=28, thick=0.0006):
    """A thin painted strip on a face at depth y: an arc of radius r (x right, z up; angles clockwise from 12 o'clock)."""
    bm = bmesh.new(); rows = []
    for i in range(steps + 1):
        a = math.radians(a0 + (a1 - a0) * i / steps)
        d = Vector((math.sin(a), 0, math.cos(a)))
        rows.append([bm.verts.new(d * (r - width / 2) + Vector((0, y, 0))), bm.verts.new(d * (r + width / 2) + Vector((0, y, 0)))])
    for a, b in zip(rows, rows[1:]): bm.faces.new((a[0], a[1], b[1], b[0]))
    bmesh.ops.solidify(bm, geom=bm.faces[:], thickness=thick)
    return from_bm(name, bm, mat, parent)

def arrow_head(name, r, a, size, y, mat, parent, thick=0.0006):
    """A flat triangle at angle a (deg, clockwise from 12) on radius r, pointing clockwise."""
    t = math.radians(a)
    p = Vector((math.sin(t), 0, math.cos(t))) * r
    fwd = Vector((math.cos(t), 0, -math.sin(t)))   # clockwise tangent
    out = Vector((math.sin(t), 0, math.cos(t)))
    bm = bmesh.new()
    vs = [bm.verts.new(p + fwd * size + Vector((0, y, 0))), bm.verts.new(p + out * size * 0.6 + Vector((0, y, 0))),
          bm.verts.new(p - out * size * 0.6 + Vector((0, y, 0)))]
    f = bm.faces.new(vs); bmesh.ops.solidify(bm, geom=[f], thickness=thick)
    return from_bm(name, bm, mat, parent)

def hex_bolt(name, loc, axis, r, mat, parent, length=0.012, washer=True):
    """A hex head (and a washer under it) on a face, pointing along axis."""
    a = Vector(axis).normalized(); p = Vector(loc)
    if washer: cyl(name + "_Washer", r * 1.45, r * 1.45, 0.002, p + a * 0.001, mat, parent, seg=16, axis=a)
    cyl(name, r, r, length * 0.6, p + a * (0.002 + length * 0.3), mat, parent, seg=6, axis=a, bv=0.0008)
    cyl(name + "_Stub", r * 0.55, r * 0.55, length * 0.5, p + a * (0.002 + length * 0.7), mat, parent, seg=10, axis=a)

def rivet(name, loc, r, mat, parent):
    o = sphere(name, r, loc, mat, parent, 10, 6)
    for v in o.data.vertices: v.co.y *= 0.5
    return o

# ------------------------------------------------------------------ the crank's body

def build_emergency_crank():
    rt = root("EmergencyCrank")
    plate, cast, bolt, tube, sign, ink, brass, cable = (role("plate"), role("cast"), role("bolt"), role("tube"),
                                                        role("sign"), role("ink"), role("brass"), role("cable"))
    # wall plate: a heavy steel sheet on four anchor bolts, a little bowed
    o = box("Crank_Plate", (0.40, 0.012, 0.60), (0, -0.006, 0.07), plate, rt, bv=0.004); subdivide(o, 4); jitter(o, 0.0015, 6, 2)
    for x in (-0.165, 0.165):
        for z in (-0.19, 0.345):
            hex_bolt("Crank_Anchor", (x, -0.012, z), (0, -1, 0), 0.011, bolt, rt, length=0.02)
    # mounting foot of the gearbox, cast in one with it
    o = box("Crank_Foot", (0.30, 0.03, 0.30), (0, -0.027, 0.0), cast, rt, bv=0.012)
    for x in (-0.12, 0.12):
        for z in (-0.12, 0.12):
            hex_bolt("Crank_FootBolt", (x, -0.042, z), (0, -1, 0), 0.009, bolt, rt)
    # the gear case: a fat cast drum along the axis
    o = lathe("Crank_Case", [(0.0, 0.0), (0.125, 0.0), (0.135, 0.01), (0.138, 0.03), (0.136, 0.085), (0.128, 0.1),
                             (0.13, 0.105), (0.0, 0.105)], cast, rt, seg=48)
    o.rotation_euler = (math.radians(90), 0, 0); o.location = (0, -0.04, 0)
    jitter(o, 0.0012, 18, 3)
    # cast ribs from the drum to the foot
    for ang in (45, 135, 225, 315):
        a = math.radians(ang); d = Vector((math.cos(a), 0, math.sin(a)))
        rib = box("Crank_Rib", (0.05, 0.08, 0.012), (0, 0, 0), cast, rt, bv=0.004)
        rib.location = d * 0.155 + Vector((0, -0.08, 0)); rib.rotation_euler = (0, -a, 0)
    # front cover on a bolted flange
    o = lathe("Crank_CoverFlange", [(0.0, 0.0), (0.14, 0.0), (0.143, 0.004), (0.143, 0.014), (0.14, 0.018), (0.0, 0.018)], cast, rt, seg=48)
    o.rotation_euler = (math.radians(90), 0, 0); o.location = (0, -0.143, 0)
    o = lathe("Crank_Cover", [(0.0, 0.0), (0.118, 0.0), (0.112, 0.012), (0.1, 0.018), (0.0, 0.02)], cast, rt, seg=48)
    o.rotation_euler = (math.radians(90), 0, 0); o.location = (0, -0.161, 0)
    for k in range(8):
        a = math.radians(22.5 + 45 * k)
        hex_bolt("Crank_CoverBolt", (math.cos(a) * 0.129, -0.161, math.sin(a) * 0.129), (0, -1, 0), 0.008, bolt, rt, washer=False)
    # the bearing boss and the shaft's end (a square for the crank's socket)
    o = lathe("Crank_Boss", [(0.0, 0.0), (0.05, 0.0), (0.046, 0.022), (0.036, 0.032), (0.0, 0.034)], cast, rt, seg=32)
    o.rotation_euler = (math.radians(90), 0, 0); o.location = (0, -0.178, 0)
    cyl("Crank_Shaft", 0.016, 0.016, 0.01, (0, -SHAFT_Y + 0.005, 0), bolt, rt, seg=16, axis=(0, 1, 0))
    # grease nipple on top, drain plug under it
    cyl("Crank_GreaseBase", 0.009, 0.009, 0.012, (0, -0.095, 0.14), bolt, rt, seg=6)
    cyl("Crank_GreaseNipple", 0.004, 0.004, 0.012, (0, -0.095, 0.152), brass, rt, seg=10)
    sphere("Crank_GreaseBall", 0.0055, (0, -0.095, 0.159), brass, rt, 10, 6)
    cyl("Crank_Drain", 0.011, 0.011, 0.012, (0, -0.095, -0.141), bolt, rt, seg=6)
    # the clockwise arrow and "ВКЛ" painted on the cover (round the boss)
    ribbon_arc("Crank_Arrow", 0.078, 40, 300, 0.009, -0.1808, ink, rt)
    arrow_head("Crank_ArrowHead", 0.078, 300, 0.017, -0.1808, ink, rt)
    text_mesh("Crank_TextOn", "ВКЛ", "arialbd.ttf", 0.022, (-0.07, -0.1803, 0.07), ink, rt, rot=(math.radians(90), 0, 0), extrude=0.0003)
    # the output: a side boss at +X, a short tube, an elbow and the tube up the wall to the ceiling (2.1 m above the axis)
    o = lathe("Crank_SideBoss", [(0.0, 0.0), (0.042, 0.0), (0.04, 0.04), (0.034, 0.05), (0.0, 0.05)], cast, rt, seg=24)
    o.rotation_euler = (0, math.radians(90), 0); o.location = (0.12, -0.09, 0.03)
    cyl("Crank_SideFlange", 0.05, 0.05, 0.012, (0.172, -0.09, 0.03), cast, rt, seg=24, axis=(1, 0, 0), bv=0.002)
    pts = [(0.17, -0.09, 0.03), (0.22, -0.09, 0.03)] + \
          [(0.22 + 0.06 * math.sin(math.radians(a)), -0.09, 0.09 - 0.06 * math.cos(math.radians(a))) for a in range(10, 90, 10)] + \
          [(0.28, -0.09, 0.09)] + [(0.28, -0.09, 0.15 + 0.1 * i) for i in range(21)]
    o = sweep("Crank_Tube", pts, 0.024, tube, rt, seg=18); jitter(o, 0.0008, 10, 4)
    for z in (0.62, 1.4, 2.06):
        cyl("Crank_TubeCoupling", 0.03, 0.03, 0.04, (0.28, -0.09, z), tube, rt, seg=18, bv=0.003)
    for z in (0.45, 1.2, 1.9):   # wall clamps: a saddle round the tube, two lugs screwed to the wall
        o = lathe("Crank_Clamp", [(0.026, -0.012), (0.031, -0.012), (0.031, 0.012), (0.026, 0.012)], bolt, rt, seg=18, cyclic=True)
        o.location = (0.28, -0.09, z)
        box("Crank_ClampLeg", (0.008, 0.07, 0.024), (0.25, -0.05, z), bolt, rt, bv=0.002)
        box("Crank_ClampLeg", (0.008, 0.07, 0.024), (0.31, -0.05, z), bolt, rt, bv=0.002)
        box("Crank_ClampFoot", (0.1, 0.006, 0.024), (0.28, -0.006, z), bolt, rt, bv=0.002)
    # armoured cable out of the bottom of the case, down into the wall
    cyl("Crank_Gland", 0.016, 0.014, 0.03, (0.07, -0.1, -0.13), brass, rt, seg=6, bv=0.002)
    sweep("Crank_Cable", [(0.07, -0.1, -0.145), (0.072, -0.1, -0.22), (0.08, -0.08, -0.3), (0.1, -0.04, -0.36),
                          (0.12, -0.012, -0.42), (0.125, 0.01, -0.45)], 0.011, cable, rt, seg=12)
    # a riveted sign above it: АВАРИЙНОЕ ПИТАНИЕ, and the rule under it
    o = box("Crank_Sign", (0.32, 0.004, 0.11), (0, -0.014, 0.265), sign, rt, bv=0.0015); subdivide(o, 3); jitter(o, 0.0008, 12, 5)
    for x in (-0.145, 0.145):
        for z in (0.222, 0.308):
            rivet("Crank_SignRivet", (x, -0.016, z), 0.006, bolt, rt)
    text_mesh("Crank_SignText", "АВАРИЙНОЕ", "arialbd.ttf", 0.036, (0, -0.0165, 0.282), ink, rt, rot=(math.radians(90), 0, 0), extrude=0.0003)
    text_mesh("Crank_SignText2", "ПИТАНИЕ", "arialbd.ttf", 0.036, (0, -0.0165, 0.243), ink, rt, rot=(math.radians(90), 0, 0), extrude=0.0003)
    text_mesh("Crank_PlateText", "ВРАЩАТЬ ДО УПОРА", "arialbd.ttf", 0.015, (0, -0.0125, -0.207), ink, rt, rot=(math.radians(90), 0, 0), extrude=0.0003)
    return rt

def build_emergency_crank_handle():
    """Cast crank arm on a hub, a worn wooden grip turning on a steel pin. Origin on the axis at the shaft end."""
    rt = root("EmergencyCrank_Handle", (0, -1.0, 0)); cast, wood, steel = role("cast"), role("wood"), role("steel")
    R = 0.2
    o = lathe("Handle_Hub", [(0.0, 0.0), (0.034, 0.0), (0.038, 0.006), (0.038, 0.044), (0.034, 0.05), (0.0, 0.05)], cast, rt, seg=32)
    o.rotation_euler = (math.radians(90), 0, 0)   # along -Y, out from the shaft end
    jitter(o, 0.0008, 30, 11)
    # the shaft's end nut holding it on, a split pin through it
    cyl("Handle_Nut", 0.02, 0.02, 0.014, (0, -0.057, 0), steel, rt, seg=6, axis=(0, 1, 0), bv=0.0015)
    cyl("Handle_ShaftEnd", 0.011, 0.011, 0.006, (0, -0.066, 0), steel, rt, seg=12, axis=(0, 1, 0), bv=0.001)
    rod("Handle_SplitPin", (-0.016, -0.061, 0.003), (0.016, -0.061, 0.003), 0.0016, steel, rt, seg=6)
    # the cross pin through the hub
    rod("Handle_Pin", (-0.05, -0.022, 0), (0.05, -0.022, 0), 0.0045, steel, rt, seg=10)
    for s in (-1, 1): sphere("Handle_PinHead", 0.0065, (s * 0.044, -0.022, 0), steel, rt, 10, 6)
    # the arm: cast, tapering, set out a little from the hub
    o = tapered_bar("Handle_Arm", R, 0.05, 0.032, 0.022, cast, rt, loc=(0, -0.038, 0), bv=0.005)
    subdivide(o, 3); jitter(o, 0.0008, 25, 6)
    cyl("Handle_ArmBoss", 0.022, 0.022, 0.026, (R, -0.038, 0), cast, rt, seg=24, axis=(0, 1, 0), bv=0.003)
    # the grip: a turned wooden barrel on a pin, a washer and the peened end
    rod("Handle_GripPin", (R, -0.05, 0), (R, -0.18, 0), 0.007, steel, rt, seg=12)
    o = lathe("Handle_Grip", [(0.0, 0.0), (0.014, 0.0), (0.018, 0.012), (0.02, 0.045), (0.019, 0.09), (0.016, 0.105),
                              (0.0, 0.106)], wood, rt, seg=24)
    o.rotation_euler = (math.radians(90), 0, 0); o.location = (R, -0.058, 0)
    jitter(o, 0.0007, 40, 7)
    cyl("Handle_GripWasher", 0.013, 0.013, 0.003, (R, -0.1655, 0), steel, rt, seg=16, axis=(0, 1, 0))
    sphere("Handle_GripCap", 0.009, (R, -0.169, 0), steel, rt, 12, 8)
    return rt

# ------------------------------------------------------------------ the fuse and its holder

def build_fuse():
    rt = root("Fuse", (0, -2.0, 0)); por, cap, blade, ink, red, screw = (role("porcelain"), role("cap"), role("blade"),
                                                                     role("ink"), role("red"), role("screw"))
    L, W = 0.076, 0.042
    o = box("Fuse_Body", (L, W, W), (0, 0, 0), por, rt, bv=0.005); subdivide(o, 2); jitter(o, 0.0005, 30, 8)
    for s in (-1, 1):
        x = s * (L / 2 + 0.003)
        box("Fuse_Cap", (0.006, W + 0.006, W + 0.006), (x, 0, 0), cap, rt, bv=0.0015)
        for dy in (-0.013, 0.013):
            for dz in (-0.013, 0.013):
                cyl("Fuse_CapScrew", 0.0032, 0.0032, 0.002, (x + s * 0.004, dy, dz), screw, rt, seg=10, axis=(1, 0, 0))
        # the knife: a thick blade with a stiffening rib and the hole's rim
        box("Fuse_Blade", (0.036, 0.003, 0.022), (x + s * 0.019, 0, 0), blade, rt, bv=0.0008)
        box("Fuse_BladeRoot", (0.008, 0.008, 0.026), (x + s * 0.005, 0, 0), blade, rt, bv=0.001)
        cyl("Fuse_BladeHole", 0.0045, 0.0045, 0.0034, (x + s * 0.026, 0, 0), screw, rt, seg=12, axis=(0, 1, 0))
    # the stamp on top and the blown-indicator on the side
    text_mesh("Fuse_Stamp", "ПН2-100", "arialbd.ttf", 0.0095, (0, 0.006, W / 2 + 0.0002), ink, rt, extrude=0.0002)
    text_mesh("Fuse_Stamp2", "100А  380В", "arial.ttf", 0.0075, (0, -0.007, W / 2 + 0.0002), ink, rt, extrude=0.0002)
    cyl("Fuse_Indicator", 0.0045, 0.0045, 0.002, (0.02, -W / 2 - 0.0008, 0), red, rt, seg=14, axis=(0, 1, 0))
    return rt

def build_fuse_holder():
    rt = root("FuseHolder", (0, -3.0, 0)); base, brass, screw, wire, ink = (role("textolite"), role("brass"), role("screw"),
                                                                      role("wire"), role("ink"))
    T = 0.014
    o = box("Holder_Base", (0.08, T, 0.25), (0, -T / 2, 0), base, rt, bv=0.003); subdivide(o, 2); jitter(o, 0.0004, 20, 9)
    for z in (-0.112, 0.112):
        cyl("Holder_Screw", 0.006, 0.006, 0.003, (0, -T - 0.0015, z), screw, rt, seg=16, axis=(0, 1, 0))
        box("Holder_ScrewSlot", (0.009, 0.001, 0.0015), (0, -T - 0.0031, z), ink, rt, bv=0)
    for s in (-1, 1):
        z = s * FUSE_BLADE_Z
        # contact: a brass block on the base with two spring jaws either side of the blade (the blade is in the YZ plane)
        box("Holder_Block", (0.034, 0.012, 0.03), (0, -T - 0.006, z), brass, rt, bv=0.0015)
        for x in (-0.0034, 0.0034):
            box("Holder_Jaw", (0.0028, 0.034, 0.028), (x * 1.55, -T - 0.012 - 0.017, z), brass, rt, bv=0.0007)
        rod("Holder_JawRivet", (-0.008, -T - 0.034, z), (0.008, -T - 0.034, z), 0.0018, screw, rt, seg=8)
        # the terminal screw and its wire, which loops off into the box through the door
        cyl("Holder_Terminal", 0.005, 0.005, 0.004, (0.012, -T - 0.014, z + s * 0.012), screw, rt, seg=6, axis=(0, 1, 0))
        sweep("Holder_Wire", [(0.012, -T - 0.016, z + s * 0.012), (0.026, -T - 0.016, z + s * 0.016), (0.036, -T - 0.008, z + s * 0.03),
                              (0.038, -0.004, z + s * 0.045), (0.038, 0.006, z + s * 0.05)], 0.0028, wire, rt, seg=8)
    text_mesh("Holder_Label", "FU1", "arialbd.ttf", 0.011, (-0.024, -T - 0.0002, 0.0), ink, rt, rot=(math.radians(90), math.radians(-90), 0), extrude=0.0002)
    return rt

# ------------------------------------------------------------------ looks, bake groups

PLOOK["textolite"] = dict(base=((0.11, 0.06, 0.03), (0.08, 0.045, 0.025)), rough=0.35, metal=0.0,
                          wear=((0.2, 0.13, 0.07), 0.7, 0.0, 0.6), dirt=((0.05, 0.035, 0.02), 0.85, 0.0, 1.0), dust=0.7, scratch=0.3)
PLOOK["brass_old"] = dict(base=((0.42, 0.3, 0.12), (0.3, 0.22, 0.09)), rough=0.5, metal=1.0, wear=((0.14, 0.2, 0.13), 0.85, 0.1, 1.0),
                          dirt=((0.07, 0.055, 0.03), 0.9, 0.0, 1.0), dust=0.6)
PLOOK["porcelain_old"] = dict(base=((0.46, 0.43, 0.36), (0.36, 0.33, 0.27)), rough=0.4, metal=0.0, wear=((0.22, 0.17, 0.11), 0.85, 0.0, 0.9),
                              dirt=((0.1, 0.07, 0.04), 0.9, 0.0, 1.0), dust=1.0, chips=0.45, chip_scale=14, scratch=0.5, bottom=0.6)
PLOOK["sign_red"] = dict(base=((0.2, 0.03, 0.018), (0.14, 0.025, 0.015)), rough=0.6, metal=0.0, chips=0.7, chip_scale=10,
                         wear=((0.14, 0.07, 0.035), 0.85, 0.3, 1.0), dirt=((0.08, 0.05, 0.03), 0.9, 0.0, 1.0), dust=1.0,
                         scratch=0.6)
PLOOK["stencil"] = dict(base=((0.52, 0.5, 0.44), (0.42, 0.4, 0.35)), rough=0.7, metal=0.0, wear=((0.16, 0.08, 0.04), 0.85, 0.0, 1.0),
                        dirt=((0.1, 0.07, 0.04), 0.9, 0.0, 1.0), dust=1.0, chips=0.65, chip_scale=18)

C_CRANK = CFG(plate="grey_paint", cast="green_paint", bolt="rust_heavy", tube="pipe_paint", sign="sign_red",
              ink="stencil", brass="brass_old", cable="cable")
C_HANDLE = CFG(cast="cast_iron", wood="wood_worn", steel="steel")
C_HANDLE["wood"]["dirt"] = 1.0
C_FUSE_PN = CFG(porcelain="porcelain_old", cap="steel", blade="brass_old", ink="ink", red="red_paint", screw="rust_heavy")
C_FUSE_PN["ink"]["base"] = ((0.06, 0.07, 0.12), (0.05, 0.06, 0.1))   # the blue stamp
C_HOLDER = CFG(textolite="textolite", brass="brass_old", screw="rust_heavy", wire="cable", ink="stencil")

GROUPS["emergency"] = [
    ("EmergencyCrank", build_emergency_crank, C_CRANK, 2048, None, None),
    ("EmergencyCrank_Handle", build_emergency_crank_handle, C_HANDLE, 1024, None, None),
    ("Fuse", build_fuse, C_FUSE_PN, 1024, None, None),
    ("FuseHolder", build_fuse_holder, C_HOLDER, 512, None, None),
]
