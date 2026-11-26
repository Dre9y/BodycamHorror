"""Low-poly 'red crew' girl (v2), rebuilt from the front/back concept sheet.
Proportions measured from the concept silhouette (959 px tall -> 1.60 m).
Run inside Blender. Z up, character faces -Y, feet at origin."""
import bpy, bmesh, math
from mathutils import Vector, Matrix

COL_NAME = "RedCrewGirl_Parts"

# ---------------------------------------------------------------- cleanup
for o in list(bpy.data.objects):
    if o.name.startswith("RCG_") or o.name == "RedCrewGirl":
        bpy.data.objects.remove(o, do_unlink=True)
for me in list(bpy.data.meshes):
    if me.users == 0:
        bpy.data.meshes.remove(me)
if COL_NAME in bpy.data.collections:
    bpy.data.collections.remove(bpy.data.collections[COL_NAME])
col = bpy.data.collections.new(COL_NAME)
bpy.context.scene.collection.children.link(col)

# ---------------------------------------------------------------- materials
def srgb(c):
    return tuple(((x + 0.055) / 1.055) ** 2.4 if x > 0.04045 else x / 12.92 for x in c)

def mat(name, rgb, rough=0.8, emit=0.0):
    m = bpy.data.materials.get("RCG_" + name) or bpy.data.materials.new("RCG_" + name)
    m.use_nodes = True
    b = m.node_tree.nodes.get("Principled BSDF")
    lin = srgb(rgb) + (1.0,)
    b.inputs["Base Color"].default_value = lin
    b.inputs["Roughness"].default_value = rough
    if emit:   # a touch of self-light keeps face/eyes readable in shadow (anime look)
        b.inputs["Emission Color"].default_value = lin
        b.inputs["Emission Strength"].default_value = emit
    m.diffuse_color = lin
    return m

M = {
    "hair":    mat("Hair",      (0.86, 0.11, 0.31)),
    "hairDk":  mat("HairDark",  (0.62, 0.05, 0.20)),
    "red":     mat("Red",       (0.82, 0.10, 0.25)),
    "redDk":   mat("RedDark",   (0.58, 0.05, 0.16)),
    "shirt":   mat("Shirt",     (0.87, 0.90, 0.95)),
    "shirtDk": mat("ShirtShade",(0.70, 0.74, 0.82)),
    "black":   mat("Black",     (0.07, 0.07, 0.08)),
    "yellow":  mat("Yellow",    (1.00, 0.80, 0.06)),
    "yellowDk":mat("YellowDark",(0.86, 0.60, 0.02)),
    "skin":    mat("Skin",      (1.00, 0.89, 0.86), emit=0.18),
    "white":   mat("White",     (0.98, 0.98, 0.98), emit=0.3),
    "iris":    mat("Iris",      (0.90, 0.12, 0.25), emit=0.25),
    "irisDk":  mat("IrisDark",  (0.45, 0.02, 0.10)),
    "lash":    mat("Lash",      (0.16, 0.05, 0.07)),
    "blush":   mat("Blush",     (0.98, 0.62, 0.66)),
    "mouth":   mat("Mouth",     (0.55, 0.12, 0.16)),
    "sole":    mat("SoleStripe",(0.78, 0.78, 0.80)),
}

parts = []

def finish(o, m):
    for c in list(o.users_collection):
        c.objects.unlink(o)
    col.objects.link(o)
    o.data.materials.clear()
    o.data.materials.append(m)
    o.name = "RCG_" + o.name
    parts.append(o)
    return o

def new_obj(name, bm, m):
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me); bm.free()
    o = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(o)
    return finish(o, m)

def cone(r1, r2, depth, verts, loc, m, rot=(0, 0, 0), scale=(1, 1, 1), name="Cone"):
    bpy.ops.mesh.primitive_cone_add(vertices=verts, radius1=r1, radius2=r2, depth=depth,
                                    location=loc, rotation=rot)
    o = bpy.context.active_object
    o.scale = scale; o.name = name
    return finish(o, m)

def box(size, loc, m, rot=(0, 0, 0), name="Box", bevel=0.0, seg=1):
    bpy.ops.mesh.primitive_cube_add(size=1, location=loc, rotation=rot)
    o = bpy.context.active_object
    o.scale = size; o.name = name
    finish(o, m)
    if bevel:
        bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
        b = o.modifiers.new("Bevel", "BEVEL"); b.width = bevel; b.segments = seg
    return o

def sphere(r, loc, m, seg=8, rings=6, scale=(1, 1, 1), name="Sphere"):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=seg, ring_count=rings, radius=r, location=loc)
    o = bpy.context.active_object
    o.scale = scale; o.name = name
    return finish(o, m)

def limb(p1, p2, r1, r2, verts, m, name="Limb", flat=1.0, roll=0.0):
    """Cone from p1 (radius r1) to p2 (radius r2); `flat` squashes local Y."""
    p1, p2 = Vector(p1), Vector(p2)
    d = p2 - p1
    bpy.ops.mesh.primitive_cone_add(vertices=verts, radius1=r1, radius2=r2, depth=d.length,
                                    location=(p1 + p2) / 2)
    o = bpy.context.active_object
    o.rotation_mode = "QUATERNION"
    o.rotation_quaternion = Vector((0, 0, 1)).rotation_difference(d.normalized()) @ \
        Matrix.Rotation(roll, 4, "Z").to_quaternion()
    o.scale = (1, flat, 1); o.name = name
    return finish(o, m)

def oriented_box(size, origin, direction, m, name="OBox", roll=0.0, bevel=0.0):
    d = Vector(direction).normalized()
    q = Vector((0, 0, 1)).rotation_difference(d) @ Matrix.Rotation(roll, 4, "Z").to_quaternion()
    bpy.ops.mesh.primitive_cube_add(size=1)
    o = bpy.context.active_object
    o.rotation_mode = "QUATERNION"; o.rotation_quaternion = q
    o.scale = size
    o.location = Vector(origin) + d * (size[2] / 2)
    o.name = name
    finish(o, m)
    if bevel:
        bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
        b = o.modifiers.new("Bevel", "BEVEL"); b.width = bevel; b.segments = 1
    return o

def edit_bm(o, fn):
    bm = bmesh.new(); bm.from_mesh(o.data)
    fn(bm)
    bm.to_mesh(o.data); bm.free(); o.data.update()

def apply_all(o):
    bpy.ops.object.select_all(action="DESELECT")
    o.select_set(True); bpy.context.view_layer.objects.active = o
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

def solid(o, t, offset=-1.0):
    s = o.modifiers.new("Solid", "SOLIDIFY"); s.thickness = t; s.offset = offset
    return o

def ell(cx, cz, rx, rz, n=12, a0=0.0):
    return [(cx + rx * math.cos(a0 + 2 * math.pi * i / n), cz + rz * math.sin(a0 + 2 * math.pi * i / n)) for i in range(n)]

# ================================================================= KEY HEIGHTS (m)
Z_SHOE_TOP, Z_CUFF_TOP, Z_CROTCH = 0.175, 0.25, 0.86
Z_WAIST_LO, Z_WAIST_HI = 0.978, 1.02
Z_SHOULDER = 1.205
HEAD_C = Vector((0, 0, 1.37))
HEAD_R = Vector((0.1, 0.097, 0.13))           # chin at 1.24, skull top 1.50

# ================================================================= SHOES / SOCKS
for side in (-1, 1):
    s = "L" if side > 0 else "R"
    sx = side * 0.095
    box((0.13, 0.235, 0.058), (sx, -0.028, 0.029), M["black"], name="Sole_" + s, bevel=0.012, seg=2)
    box((0.134, 0.239, 0.009), (sx, -0.028, 0.021), M["sole"], name="SoleStripe_" + s, bevel=0.004)
    up = box((0.12, 0.215, 0.125), (sx, -0.03, 0.115), M["black"], name="ShoeUpper_" + s, bevel=0.028, seg=2)
    def toe(bm):
        for v in bm.verts:
            if v.co.z > 0 and v.co.y < 0:
                v.co.z -= 0.045 * (-v.co.y / 0.1)       # top slopes down toward the toe
    edit_bm(up, toe)
    # shoe opening collar + white sock
    cone(0.047, 0.047, 0.012, 10, (sx, 0.0, Z_SHOE_TOP - 0.004), M["black"], name="ShoeCollar_" + s)
    cone(0.041, 0.04, 0.05, 10, (sx, 0.0, Z_SHOE_TOP + 0.02), M["white"], name="Sock_" + s)

# ================================================================= PANTS
for side in (-1, 1):
    s = "L" if side > 0 else "R"
    limb((side * 0.05, 0, Z_CROTCH + 0.03), (side * 0.09, 0, Z_CUFF_TOP), 0.054, 0.076, 10,
         M["red"], "PantsLeg_" + s, flat=0.9)
    cone(0.084, 0.083, 0.062, 10, (side * 0.09, 0, Z_CUFF_TOP - 0.028), M["red"], name="Cuff_" + s, scale=(1, 0.92, 1))
    cone(0.0845, 0.0845, 0.006, 10, (side * 0.09, 0, Z_CUFF_TOP + 0.002), M["redDk"], name="CuffFold_" + s, scale=(1, 0.92, 1))
    # front crease + side seam (thin darker slivers)
    top, bot = Vector((side * 0.05, -0.05, Z_CROTCH + 0.02)), Vector((side * 0.09, -0.069, Z_CUFF_TOP + 0.005))
cone(0.104, 0.074, Z_WAIST_LO - Z_CROTCH + 0.01, 10, (0, 0, (Z_WAIST_LO + Z_CROTCH) / 2), M["red"], name="Hips", scale=(1, 0.76, 1))
cone(0.076, 0.075, Z_WAIST_HI - Z_WAIST_LO, 10, (0, 0, (Z_WAIST_LO + Z_WAIST_HI) / 2), M["red"], name="Waistband", scale=(1, 0.78, 1))
cone(0.0765, 0.0765, 0.004, 10, (0, 0, Z_WAIST_LO), M["redDk"], name="WaistSeam", scale=(1, 0.78, 1))
for a in (-60, -25, 25, 60, 150, 210):
    r = math.radians(a - 90)
    x, y = 0.0765 * math.cos(r), 0.0765 * 0.78 * math.sin(r)
    box((0.01, 0.006, 0.036), (x, y, (Z_WAIST_LO + Z_WAIST_HI) / 2), M["redDk"],
        rot=(0, 0, r + math.pi / 2), name="BeltLoop")

# ================================================================= TORSO
def torso_r(z):  # x radius of shirt at height z
    t = (z - Z_WAIST_LO) / (Z_SHOULDER - Z_WAIST_LO)
    return 0.066 + 0.02 * t
TORSO_FLAT = 0.72
def torso_front_y(z, x, lift=0.002):
    rx = torso_r(z); ry = rx * TORSO_FLAT
    return -(ry * math.sqrt(max(0.0, 1 - (x / rx) ** 2)) + lift)

cone(torso_r(Z_WAIST_LO), torso_r(Z_SHOULDER), Z_SHOULDER - Z_WAIST_LO + 0.01, 10,
     (0, 0, (Z_WAIST_LO + Z_SHOULDER) / 2 + 0.005), M["shirt"], name="Torso", scale=(1, TORSO_FLAT, 1))
cone(torso_r(Z_SHOULDER) + 0.002, 0.04, 0.03, 10, (0, 0, Z_SHOULDER + 0.012), M["shirt"], name="ShoulderCap", scale=(1, 0.74, 1))
# tucked-in blouse fold above the waistband
cone(0.074, 0.068, 0.02, 10, (0, 0, Z_WAIST_HI + 0.008), M["shirtDk"], name="Tuck", scale=(1, 0.76, 1))
cone(0.025, 0.024, 0.06, 8, (0, 0, Z_SHOULDER + 0.03), M["skin"], name="Neck")
# collar band + pointed collar flaps
cone(0.033, 0.03, 0.022, 10, (0, 0.002, Z_SHOULDER + 0.022), M["shirt"], name="CollarBand", scale=(1, 0.95, 1))
for side in (-1, 1):
    bm = bmesh.new()
    vs = [bm.verts.new(p) for p in ((side * 0.004, -0.04, Z_SHOULDER + 0.03),
                                    (side * 0.028, -0.032, Z_SHOULDER + 0.03),
                                    (side * 0.034, -0.056, Z_SHOULDER - 0.012),
                                    (side * 0.01, -0.061, Z_SHOULDER - 0.016))]
    f = bm.faces.new(vs if side > 0 else vs[::-1])
    o = new_obj("CollarFlap", bm, M["shirt"]); solid(o, 0.005, 1.0)
# tie
knot_z = Z_SHOULDER + 0.002
box((0.02, 0.012, 0.018), (0, torso_front_y(knot_z, 0) - 0.006, knot_z), M["black"], name="TieKnot", bevel=0.003)
bm = bmesh.new()
tie_pts = [(-0.008, knot_z - 0.008), (0.008, knot_z - 0.008), (0.016, 1.06), (0.0, 1.04), (-0.016, 1.06)]
vs = [bm.verts.new((x, torso_front_y(z, x, 0.004), z)) for x, z in tie_pts]
bm.faces.new(vs[::-1])
solid(new_obj("Tie", bm, M["black"]), 0.004, 1.0)
# name tag (character's left chest) and shirt placket buttons
tz = 1.15
box((0.03, 0.006, 0.0095), (0.045, torso_front_y(tz, 0.045, 0.002), tz), M["yellow"],
    rot=(0, 0, math.radians(-12)), name="NameTag")
# suspenders: front strap, over shoulder, back strap
def strap(pts):
    for a, b in zip(pts, pts[1:]):
        limb(a, b, 0.0095, 0.0095, 4, M["black"], "Suspender", flat=0.3, roll=math.radians(45))
for side in (-1, 1):
    xf_b, xf_t = side * 0.044, side * 0.05
    front = [Vector((xf_b, torso_front_y(Z_WAIST_HI, xf_b, 0.003), Z_WAIST_HI - 0.01)),
             Vector((xf_t, torso_front_y(Z_SHOULDER - 0.01, xf_t, 0.003), Z_SHOULDER - 0.01))]
    over = [front[-1], Vector((side * 0.052, -0.028, Z_SHOULDER + 0.024)),
            Vector((side * 0.052, 0.028, Z_SHOULDER + 0.024)),
            Vector((side * 0.05, -torso_front_y(Z_SHOULDER - 0.01, xf_t, 0.003), Z_SHOULDER - 0.01))]
    back = [over[-1], Vector((side * 0.04, -torso_front_y(Z_WAIST_HI, xf_b, 0.003), Z_WAIST_HI - 0.01))]
    strap(front); strap(over); strap(back)
    # little clips where straps meet the waistband
    yc = torso_front_y(Z_WAIST_HI, xf_b, 0.005)
    box((0.014, 0.005, 0.01), (xf_b, yc, Z_WAIST_HI - 0.004), M["sole"], name="StrapClip")
    box((0.014, 0.005, 0.01), (side * 0.04, -yc, Z_WAIST_HI - 0.004), M["sole"], name="StrapClip")

# ================================================================= ARMS
for side in (-1, 1):
    s = "L" if side > 0 else "R"
    sh = Vector((side * 0.098, 0, Z_SHOULDER - 0.012))
    wr = Vector((side * 0.207, -0.008, 0.896))
    d = (wr - sh).normalized()
    L = (wr - sh).length
    sl_end = sh + d * (L * 0.42)
    limb(sh - d * 0.015, sl_end, 0.044, 0.047, 9, M["shirt"], "Sleeve_" + s)
    limb(sl_end - d * 0.004, sl_end + d * 0.02, 0.051, 0.05, 9, M["shirt"], "SleeveCuff_" + s)
    cone_ring = limb(sl_end + d * 0.0, sl_end + d * 0.004, 0.0515, 0.0515, 9, M["shirtDk"], "SleeveFold_" + s)
    # pink smudge patch on the sleeve (concept detail)
    pp = sh + d * 0.05 + Vector((side * 0.035, -0.03, 0.0))
    box((0.016, 0.004, 0.012), pp, M["blush"], rot=(0, 0, math.radians(side * 40)), name="SleevePatch")
    elbow = sh + d * (L * 0.68)
    limb(sl_end, elbow, 0.0235, 0.02, 7, M["skin"], "UpperArm_" + s)
    sphere(0.0205, elbow, M["skin"], seg=7, rings=4, name="Elbow_" + s)
    limb(elbow, wr, 0.021, 0.0175, 7, M["skin"], "Forearm_" + s)
    # glove: flared cuff, palm, fanned fingers, thumb
    limb(wr - d * 0.012, wr + d * 0.04, 0.024, 0.037, 9, M["yellow"], "GloveCuff_" + s)
    limb(wr + d * 0.038, wr + d * 0.042, 0.0375, 0.0375, 9, M["yellowDk"], "GloveCuffRim_" + s)
    perp = d.cross(Vector((0, 1, 0))).normalized()
    if perp.x * side < 0:
        perp = -perp
    palm_o = wr + d * 0.03
    oriented_box((0.056, 0.026, 0.06), palm_o, d, M["yellow"], "Palm_" + s, bevel=0.006)
    base = palm_o + d * 0.056
    for i, off in enumerate((-0.02, -0.0067, 0.0067, 0.02)):
        fd = (d + perp * (0.12 + off * 11.0)).normalized()
        ln = (0.056, 0.064, 0.06, 0.047)[i]
        k = base + perp * off
        mid = k + fd * ln * 0.5
        limb(k, mid, 0.0082, 0.0075, 5, M["yellow"], "Finger_" + s, flat=0.85)
        fd2 = (fd + Vector((0, -0.12, 0))).normalized()
        limb(mid, mid + fd2 * ln * 0.5, 0.0075, 0.0058, 5, M["yellow"], "FingerTip_" + s, flat=0.85)
    td = (d * 0.75 - perp * 0.55 + Vector((0, -0.3, 0))).normalized()
    t0 = palm_o + d * 0.012 - perp * 0.02
    limb(t0, t0 + td * 0.022, 0.0085, 0.0075, 5, M["yellow"], "Thumb_" + s)
    td2 = (td + d * 0.6).normalized()
    limb(t0 + td * 0.022, t0 + td * 0.022 + td2 * 0.02, 0.0075, 0.0055, 5, M["yellow"], "ThumbTip_" + s)

# ================================================================= HEAD
# Angular, faceted anime head: 10 segments puts a flat plane at the front of the face,
# rings at z = .38/.71/.92 give wide cheek planes, then straight jaw lines to a chin point.
head = sphere(1.0, HEAD_C, M["skin"], seg=10, rings=8, scale=tuple(HEAD_R), name="Head")
def head_shape(bm):
    a = math.radians(18)    # half a segment: a flat facet faces forward instead of a ridge
    for v in bm.verts:
        x, y, z = v.co
        x, y = x * math.cos(a) - y * math.sin(a), x * math.sin(a) + y * math.cos(a)
        if y < 0 and -0.39 < z < 0.75:                  # flat face plane
            y *= 0.9
        if z < 0:
            t = -z
            k = 1 - 0.07 * (t / 0.4) if t < 0.4 else 0.93 - 0.83 * ((t - 0.4) / 0.6)
            x *= k
            if t > 0.39 and y <= 0:
                # keep the lower face nearly vertical so its planes catch light like the concept
                ring = math.sqrt(max(1e-6, 1 - t * t))
                keys = ((0.38, 0.84), (0.71, 0.75), (0.93, 0.57), (1.0, 0.4))
                for (t0, d0), (t1, d1) in zip(keys, keys[1:]):
                    if t <= t1:
                        D = d0 + (d1 - d0) * (t - t0) / (t1 - t0); break
                y = -D * (abs(y) / ring) if t < 0.999 else -D
            elif y > 0:
                y *= max(k, 0.35)
        if y > 0 and z > -0.3:                          # rounder back of skull
            y *= 1.06
        v.co = (x, y, z)
edit_bm(head, head_shape)
apply_all(head)

HAIR_LIFT = 0.015
def surf(x, z, lift):
    """Point on the face surface seen from the front, lifted along the normal."""
    ok, loc, n, _ = head.ray_cast(Vector((x, -1.0, z)), Vector((0, 1, 0)))
    if not ok:
        return None
    n = (n * 0.7 + Vector((0, -0.3, 0))).normalized()
    return loc + n * lift

def surf_smooth(x, z, lift):
    """Point on a smooth ellipsoid around the skull: used for hair so strands stay
    clean planes instead of copying every facet of the angular face."""
    rx, ry, rz = 0.1, 0.092, 0.13
    dz = z - HEAD_C.z
    s = 1 - (x / rx) ** 2 - (dz / rz) ** 2
    y = -ry * math.sqrt(max(s, 0.0)) - (0.0 if s > 0 else 0.02 * min(1.0, -s * 4))
    n = Vector((x / rx ** 2, y / ry ** 2, dz / rz ** 2)).normalized()
    return Vector((x, y, z)) + n * lift

def decal(pts, m, lift, name, cuts=1, thick=0.0, smooth=False):
    bm = bmesh.new()
    vs = [bm.verts.new((x, 0, z)) for x, z in pts]
    f = bm.faces.new(vs)
    f.normal_update()
    if f.normal.y > 0:
        f.normal_flip()
    bmesh.ops.triangulate(bm, faces=[f])
    # face decals: dense enough to follow the facet ridges instead of sinking through them
    bmesh.ops.subdivide_edges(bm, edges=bm.edges[:], cuts=cuts if smooth else max(cuts, 2), use_grid_fill=True)
    for v in bm.verts:
        L = lift(v.co.z) if callable(lift) else lift
        p = surf_smooth(v.co.x, v.co.z, L) if smooth else surf(v.co.x, v.co.z, L)
        v.co = p if p else Vector((v.co.x, -0.06, v.co.z))
    o = new_obj(name, bm, m)
    if thick:
        solid(o, thick, -1.0)
    return o

# ---------------------------------------------------------------- face
def lid_v(u):  # upper lid height relative to eye centre
    pts = [(-0.024, 0.003), (-0.012, 0.0075), (0.0, 0.009), (0.012, 0.0102), (0.024, 0.0108), (0.031, 0.007)]
    for (u0, v0), (u1, v1) in zip(pts, pts[1:]):
        if u <= u1:
            t = max(0.0, min(1.0, (u - u0) / (u1 - u0)))
            return v0 + (v1 - v0) * t
    return pts[-1][1]

def clip_lid(pts, margin=0.0006):
    return [(u, min(v, lid_v(u) - margin)) for u, v in pts]

for side in (-1, 1):
    s = "L" if side > 0 else "R"
    ecx, ecz = side * 0.043, 1.338; ES = 1.25
    W = lambda P: [(ecx + side * u * ES, ecz + v * ES) for u, v in P]
    white = [(-0.022, 0.0035), (-0.012, 0.0078), (0.0, 0.0092), (0.012, 0.0104), (0.024, 0.011), (0.028, 0.006),
             (0.026, -0.002), (0.02, -0.0095), (0.01, -0.0148), (-0.002, -0.0158), (-0.012, -0.0125), (-0.019, -0.005)]
    decal(W(white), M["white"], 0.0012, "EyeWhite_" + s, cuts=1)
    decal(W(clip_lid(ell(0.002, -0.0035, 0.0125, 0.0125, 14))), M["iris"], 0.0021, "Iris_" + s)
    decal(W(clip_lid(ell(0.002, 0.003, 0.0108, 0.0062, 12))), M["irisDk"], 0.0026, "IrisTop_" + s)
    decal(W(clip_lid(ell(0.002, -0.0035, 0.0052, 0.0078, 10))), M["irisDk"], 0.0031, "Pupil_" + s)
    decal(W(ell(-0.0035, 0.0012, 0.0034, 0.0034, 8)), M["white"], 0.0040, "Shine_" + s)
    decal(W(ell(0.0075, -0.0105, 0.0019, 0.0019, 6)), M["white"], 0.0040, "Shine2_" + s)
    lash = [(-0.024, 0.0025), (-0.012, 0.007), (0.0, 0.0085), (0.012, 0.0097), (0.024, 0.0103), (0.03, 0.0068),
            (0.0365, 0.0015), (0.032, 0.0125), (0.024, 0.0158), (0.012, 0.0158), (0.0, 0.0148), (-0.012, 0.0122),
            (-0.022, 0.0068)]
    decal(W(lash), M["lash"], 0.0046, "UpperLash_" + s, cuts=0)
    low = [(0.011, -0.0152), (0.02, -0.0108), (0.0262, -0.0035), (0.0278, -0.0035), (0.0225, -0.0118), (0.012, -0.0168)]
    decal(W(low), M["lash"], 0.0030, "LowerLash_" + s, cuts=0)
    crease = [(-0.006, 0.0205), (0.012, 0.0215), (0.026, 0.0185), (0.026, 0.0172), (0.012, 0.0199), (-0.006, 0.0191)]
    decal(W(crease), M["lash"], 0.0015, "Crease_" + s, cuts=0)
    decal(W(ell(0.019, -0.0285, 0.013, 0.0055, 10)), M["blush"], 0.0016, "Blush_" + s)
    for k in range(3):   # blush hatch strokes
        u = 0.012 + k * 0.0055
        decal(W([(u, -0.0258), (u + 0.0022, -0.0258), (u + 0.0006, -0.031), (u - 0.0012, -0.031)]),
              M["mouth"], 0.0021, "BlushLine", cuts=0)
decal([(-0.0075, 1.2835), (0.0, 1.2815), (0.0075, 1.2835), (0.0072, 1.2818), (0.0, 1.2797), (-0.0072, 1.2818)],
      M["mouth"], 0.0012, "Mouth", cuts=0)

# ---------------------------------------------------------------- hair
# skull-hugging hairline shell (covers the scalp above the bangs)
shell = sphere(1.0, HEAD_C, M["hair"], seg=14, rings=10, scale=(0.105, 0.103, 0.136), name="Hairline")
edit_bm(shell, lambda bm: bmesh.ops.delete(bm, geom=[f for f in bm.faces
        if (f.calc_center_median().z < 0.3 and f.calc_center_median().y < 0.25) or f.calc_center_median().z < -0.55], context="FACES"))
# outer volume / back of the head
cap = sphere(1.0, HEAD_C + Vector((0, 0.012, 0.014)), M["hair"], seg=14, rings=10,
             scale=(0.122, 0.12, 0.146), name="HairCap")
edit_bm(cap, lambda bm: bmesh.ops.delete(bm, geom=[f for f in bm.faces
        if (f.calc_center_median().y < -0.2 and (
            (f.calc_center_median().z < 0.45 and abs(f.calc_center_median().x) < 0.8)
            or f.calc_center_median().z < -0.25)) or f.calc_center_median().z < -0.8], context="FACES"))
solid(cap, 0.01)

def strand(root_x, root_z, tip_x, tip_z, w, lift=None, name="Bang", bend=0.0, m=None):
    if lift is None:   # roots tuck under the hair cap, tips settle close to the skin
        lift = lambda z: 0.012 + 0.02 * max(0.0, min(1.0, (z - tip_z) / (root_z - tip_z)))
    """Flat tapered hair spike hugging the forehead, given thickness."""
    pts = []
    for t, k in ((0.0, 1.0), (0.45, 0.78), (0.8, 0.38)):
        cx = root_x + (tip_x - root_x) * t + bend * math.sin(t * math.pi)
        cz = root_z + (tip_z - root_z) * t
        pts.append((cx - w * k / 2, cz)); pts.insert(0, (cx + w * k / 2, cz))
    left = pts[len(pts) // 2:]; right = pts[:len(pts) // 2]
    poly = left + [(tip_x, tip_z)] + right
    return decal(poly, m or M["hair"], lift, name, cuts=1, thick=0.011, smooth=True)

# (root x, root z, tip x, tip z, width, bend)
bangs = [(-0.083, 1.478, -0.094, 1.318, 0.042, -0.006),
         (-0.06, 1.478, -0.068, 1.37, 0.044, 0.0),
         (-0.032, 1.478, -0.036, 1.383, 0.04, 0.004),
         (-0.008, 1.478, -0.004, 1.33, 0.036, 0.0),
         (0.016, 1.478, 0.02, 1.378, 0.036, -0.003),
         (0.04, 1.478, 0.05, 1.385, 0.042, 0.0),
         (0.064, 1.478, 0.076, 1.364, 0.044, 0.004),
         (0.085, 1.478, 0.096, 1.318, 0.042, 0.006)]
for rx, rz, tx, tz, w, b in bangs:
    strand(rx, rz, tx, tz, w, bend=b)

# side locks framing the cheeks (in front of the ears)
for side in (-1, 1):
    bm = bmesh.new()
    P = [(0.1, -0.012, 1.44), (0.11, 0.015, 1.44), (0.116, 0.008, 1.33), (0.108, -0.012, 1.262), (0.102, -0.02, 1.32)]
    vs = [bm.verts.new((side * x, y, z)) for x, y, z in P]
    bm.faces.new(vs if side < 0 else vs[::-1])
    solid(new_obj("SideLock", bm, M["hair"]), 0.012, 0.0)
    limb((side * 0.108, 0.012, 1.43), (side * 0.112, 0.01, 1.29), 0.028, 0.004, 5, M["hair"], "SideLockBack", flat=0.55)
# nape spikes
for x in (-0.07, -0.035, 0.0, 0.035, 0.07):
    limb((x, 0.085, 1.33), (x * 1.15, 0.088 + abs(x) * 0.1, 1.255), 0.03, 0.003, 4, M["hair"], "Nape", flat=0.45)

# drill twin-tails: stacked flared tiers whose ledges tilt in turn, reading as a spiral
def drill(side, cx=0.172, cy=0.016, z_top=1.508):
    c = Vector((side * cx, cy, z_top))
    sphere(0.044, c + Vector((0, 0, -0.026)), M["hair"], seg=8, rings=5, scale=(1, 0.95, 0.8), name="DrillDome")
    radii = (0.061, 0.058, 0.052, 0.045)
    heights = (0.082, 0.078, 0.07, 0.062)
    for i, (r, h) in enumerate(zip(radii, heights)):
        a = math.radians(side * (35 + i * 105))
        ecc = Vector((math.cos(a), math.sin(a), 0)) * 0.016
        top, bot = c.copy(), c + ecc + Vector((0, 0, -h))
        limb(top, bot, r * 0.42, r, 8, M["hair"], "DrillTier%d" % i, roll=a)
        # darker underside ring sells the overhang of each ledge
        limb(bot + (bot - top).normalized() * 0.0005, bot + (bot - top).normalized() * 0.006,
             r * 0.98, r * 0.6, 8, M["hairDk"], "DrillLedge%d" % i, roll=a)
        c = Vector((bot.x - side * 0.007, cy, bot.z + h * 0.24))
    limb(c, c + Vector((-side * 0.012, 0, -0.05)), 0.034, 0.002, 7, M["hair"], "DrillTip")
    limb((side * 0.085, 0.01, 1.485), (side * 0.15, cy, 1.5), 0.032, 0.036, 6, M["hair"], "TailBridge")
for side in (-1, 1):
    drill(side)

# ================================================================= HAT (pillbox)
HAT_C = Vector((0.004, 0.012, 1.553))
hat_rot = (math.radians(-5), math.radians(4), 0)
cone(0.076, 0.072, 0.066, 12, HAT_C, M["red"], rot=hat_rot, name="Hat")
cone(0.0775, 0.0775, 0.014, 12, HAT_C + Vector((0, 0.003, -0.024)), M["black"], rot=hat_rot, name="HatBand")
cone(0.072, 0.066, 0.006, 12, HAT_C + Vector((0, -0.003, 0.034)), M["redDk"], rot=hat_rot, name="HatTopRim")
sphere(0.013, (-0.054, -0.046, 1.534), M["yellow"], seg=8, rings=5, name="HatButton")

# ================================================================= JOIN
for o in parts:
    bpy.ops.object.select_all(action="DESELECT")
    o.select_set(True); bpy.context.view_layer.objects.active = o
    for m_ in list(o.modifiers):
        bpy.ops.object.modifier_apply(modifier=m_.name)
bpy.ops.object.select_all(action="DESELECT")
for o in parts:
    o.select_set(True)
bpy.context.view_layer.objects.active = head
bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
bpy.ops.object.join()
ch = bpy.context.active_object
ch.name = "RedCrewGirl"; ch.data.name = "RedCrewGirl_Mesh"
bpy.context.scene.cursor.location = (0, 0, 0)
bpy.ops.object.origin_set(type="ORIGIN_CURSOR")
skin_idx = [i for i, m_ in enumerate(ch.data.materials) if m_ and m_.name == "RCG_Skin"]
for p in ch.data.polygons:
    p.use_smooth = False                            # faceted look everywhere, face included
cube = bpy.data.objects.get("Cube")
if cube:
    cube.hide_set(True); cube.hide_render = True
print("tris:", sum(len(p.vertices) - 2 for p in ch.data.polygons), "dims:", tuple(round(v, 3) for v in ch.dimensions))
