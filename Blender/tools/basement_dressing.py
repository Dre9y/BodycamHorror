# Builds the basement transition's dressing props and exports each as FBX into
# Assets/Models/BasementDressing. Run inside Blender (Unity's Blender bridge: run_python).
# Metres; Blender axes: X right, Y back, Z up. Wall-mounted things have the wall at Y=0 and
# face -Y (towards the viewer); floor things stand on Z=0. Materials are named only: Unity
# gives them matte URP materials by name (BasementDressingSetup.cs).
import bpy, bmesh, math, os, random
from mathutils import Vector, Matrix

OUT = "D:/BodycamHorror/Assets/Models/BasementDressing"
os.makedirs(OUT, exist_ok=True)
random.seed(7)


def clear():
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    for m in list(bpy.data.meshes):
        bpy.data.meshes.remove(m)
    for c in list(bpy.data.curves):
        bpy.data.curves.remove(c)


def mat(name):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    return m


def assign(o, name):
    o.data.materials.clear()
    o.data.materials.append(mat(name))
    return o


def box(center, size, m, rot=(0, 0, 0)):
    bpy.ops.mesh.primitive_cube_add(size=1, location=center, rotation=rot)
    o = bpy.context.active_object
    o.scale = size
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    return assign(o, m)


def cyl(a, b, r, m, verts=12):
    a, b = Vector(a), Vector(b)
    d = b - a
    bpy.ops.mesh.primitive_cylinder_add(vertices=verts, radius=r, depth=d.length, location=(a + b) / 2)
    o = bpy.context.active_object
    o.rotation_mode = 'QUATERNION'
    o.rotation_quaternion = d.normalized().to_track_quat('Z', 'Y')
    return assign(o, m)


def tube(points, r, m, res=6):
    cu = bpy.data.curves.new("tube", 'CURVE')
    cu.dimensions = '3D'
    cu.bevel_depth = r
    cu.bevel_resolution = 2
    cu.resolution_u = res
    sp = cu.splines.new('NURBS')
    sp.points.add(len(points) - 1)
    for p, co in zip(sp.points, points):
        p.co = (co[0], co[1], co[2], 1)
    sp.use_endpoint_u = True
    sp.order_u = min(4, len(points))
    o = bpy.data.objects.new("tube", cu)
    bpy.context.collection.objects.link(o)
    cu.materials.append(mat(m))
    bpy.ops.object.select_all(action='DESELECT')
    o.select_set(True)
    bpy.context.view_layer.objects.active = o
    bpy.ops.object.convert(target='MESH')
    return bpy.context.active_object


def torus(center, R, r, m, rot=(0, 0, 0)):
    bpy.ops.mesh.primitive_torus_add(major_radius=R, minor_radius=r, major_segments=32, minor_segments=8, location=center, rotation=rot)
    return assign(bpy.context.active_object, m)


def text(body, center, size, m, font="C:/Windows/Fonts/arialbd.ttf"):
    cu = bpy.data.curves.new("txt", 'FONT')
    cu.body = body
    cu.size = size
    cu.extrude = 0.0015
    cu.align_x = 'CENTER'
    cu.align_y = 'CENTER'
    if os.path.exists(font):
        cu.font = bpy.data.fonts.load(font, check_existing=True)
    o = bpy.data.objects.new("txt", cu)
    bpy.context.collection.objects.link(o)
    o.location = center
    o.rotation_euler = (math.radians(90), 0, 0)   # stands up, reads from -Y
    cu.materials.append(mat(m))
    bpy.ops.object.select_all(action='DESELECT')
    o.select_set(True)
    bpy.context.view_layer.objects.active = o
    bpy.ops.object.convert(target='MESH')
    return bpy.context.active_object


def export(name):
    objs = [o for o in bpy.context.scene.objects if o.type == 'MESH']
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.join()
    o = bpy.context.active_object
    o.name = name
    o.data.name = name
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    bpy.ops.object.shade_flat()
    bpy.ops.export_scene.fbx(filepath=f"{OUT}/{name}.fbx", use_selection=True, object_types={'MESH'},
                             apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
                             bake_space_transform=True, mesh_smooth_type='FACE', add_leaf_bones=False)
    n = len(o.data.vertices)
    clear()
    return f"{name}: {n} verts"


log = []

# ---------------------------------------------------------------- 1. cables under the ceiling
clear()
# a bundle of four along X, clipped to the ceiling (Z=0) every metre, sagging between the clips
for i, (dy, dz, r) in enumerate([(0, -.03, .011), (.028, -.032, .008), (-.026, -.035, .009), (.012, -.055, .013)]):
    pts = []
    for k in range(9):
        x = -1 + k * .25
        sag = .035 * math.sin(math.pi * ((x + 1) % 1.0))
        pts.append((x, dy, dz - sag))
    tube(pts, r, "Cable" if i != 3 else "CableBlack")
for x in (-1, 0, 1):
    box((x, 0, -.02), (.03, .09, .04), "Steel")
log.append(export("Cable_Run_2m"))

# a torn length hanging from the ceiling: a loop and a cut end with bare wires
tube([(-.7, 0, 0), (-.45, 0, -.45), (-.1, .02, -.8), (.25, 0, -.5), (.5, 0, -.05), (.55, 0, 0)], .012, "CableBlack")
tube([(.55, 0, 0), (.6, .03, -.4), (.52, .06, -.85), (.58, .04, -1.15)], .011, "Cable")
for a in range(3):
    ang = a * 2.1
    tube([(.58, .04, -1.15), (.58 + .03 * math.cos(ang), .04 + .03 * math.sin(ang), -1.22)], .003, "Copper")
box((-.7, 0, -.02), (.03, .06, .04), "Steel")
box((.55, 0, -.02), (.03, .06, .04), "Steel")
log.append(export("Cable_Torn"))

# ---------------------------------------------------------------- 2. ventilation duct
def duct(length, grille):
    w, h, t = .5, .34, .012
    top = -.18   # hangs 18 cm under the ceiling (Z=0)
    zc = top - h / 2
    for s in (-1, 1):
        box((0, s * (w / 2 - t / 2), zc), (length, t, h), "Duct")
        box((0, 0, zc + s * (h / 2 - t / 2)), (length, w, t), "Duct")
    for x in [-length / 2 + .03, 0, length / 2 - .03]:
        for s in (-1, 1):
            box((x, s * (w / 2 + .006), zc), (.05, .012, h + .03), "DuctSeam")
            box((x, 0, zc + s * (h / 2 + .006)), (.05, w + .03, .012), "DuctSeam")
    for x in (-length / 2 + .35, length / 2 - .35):   # hanger straps to the ceiling
        for s in (-1, 1):
            box((x, s * (w / 2 + .02), top / 2 - .02), (.03, .006, -top + .1), "Steel")
        box((x, 0, top + .004), (.03, w + .05, .006), "Steel")
    if grille:
        x = length / 2 + .01
        box((x, 0, zc), (.02, w + .04, h + .04), "DuctSeam")
        for k in range(7):
            z = zc - h / 2 + .03 + k * (h - .06) / 6
            box((x + .01, 0, z), (.012, w - .02, .03), "Steel", rot=(math.radians(35), 0, 0))
        # one louvre bent out of line, the frame dented
        bpy.context.active_object.rotation_euler = (math.radians(70), math.radians(8), 0)


duct(2, False)
log.append(export("Duct_2m"))
duct(2, True)
log.append(export("Duct_2m_Grille"))

# ---------------------------------------------------------------- 3. residents' storage lockers
# Three slatted cages 1 m wide, 2.1 m high, 0.9 m deep: front on Y=0 (faces -Y), back against a wall at Y=0.9.
H, D, bay = 2.1, .9, 1.0
for b in range(3):
    x0 = -1.5 + b * bay
    # door frame and door
    box((x0 + .03, 0, H / 2), (.06, .05, H), "WoodDark")
    door_w = .7
    dx0 = x0 + .15
    for z in (.12, H / 2, H - .12):
        box((dx0 + door_w / 2, -.025, z), (door_w, .03, .1), "Wood")
    box((dx0 + door_w / 2, -.025, H / 2), (math.hypot(H - .3, door_w) - .1, .03, .09), "Wood", rot=(0, -math.atan2(H - .3, door_w), 0))
    n = 9
    for k in range(n):
        x = x0 + .07 + k * (bay - .1) / n
        if dx0 - .02 < x < dx0 + door_w + .02:
            box((x + .04, -.01, H / 2), (.075, .02, H - .05), "Wood")   # door boards
        else:
            box((x + .04, 0, H / 2), (.075, .02, H - .05), "Wood")
    # hasp and padlock (one bay's lock is torn off, its door ajar)
    hx = dx0 + door_w - .03
    if b != 1:
        box((hx, -.05, 1.05), (.12, .01, .035), "Steel")
        box((hx + .07, -.065, 1.0), (.045, .018, .05), "Padlock")
        torus((hx + .07, -.065, 1.035), .016, .004, "Steel", rot=(math.radians(90), 0, 0))
    else:
        box((hx + .02, -.05, 1.05), (.05, .01, .035), "Steel")
    # bay number, painted on the frame
    box((x0 + .55, -.042, 1.9), (.12, .004, .09), "Enamel")
box((1.5 - .03, 0, H / 2), (.06, .05, H), "WoodDark")
box((0, 0, H - .03), (3.0, .05, .06), "WoodDark")
for s in (-1.5, 1.5):   # side partitions back to the wall
    for k in range(7):
        box((s, .08 + k * .12, H / 2), (.02, .075, H - .05), "Wood")
log.append(export("Storage_Lockers"))

# ---------------------------------------------------------------- 4. enamel signs
def sign(name, lines, size, w, h):
    box((0, -.004, 0), (w, .008, h), "EnamelBlue")
    for s in (-1, 1):
        box((0, -.0085, s * (h / 2 - .012)), (w - .02, .002, .008), "EnamelWhite")
        box((s * (w / 2 - .012), -.0085, 0), (.008, .002, h - .02), "EnamelWhite")
    for x in (-w / 2 + .025, w / 2 - .025):
        cyl((x, -.006, h / 2 - .025), (x, -.012, h / 2 - .025), .006, "Steel", 8)
    step = h / (len(lines) + 1)
    for i, (t, sz) in enumerate(lines):
        text(t, (0, -.0095, h / 2 - step * (i + 1)), sz, "EnamelWhite")
    # chips in the enamel: black iron showing through
    for k in range(6):
        box((random.uniform(-w / 2 + .03, w / 2 - .03), -.0088, random.uniform(-h / 2 + .03, h / 2 - .03)),
            (random.uniform(.008, .025), .002, random.uniform(.006, .018)), "Iron")
    return export(name)


log.append(sign("Sign_Heating", [("ВВОД ТЕПЛОСЕТИ", .045), ("ПРИ АВАРИИ ЗАКРЫТЬ", .03), ("ЗАДВИЖКУ", .03)], 0, .5, .26))
log.append(sign("Sign_Basement", [("ПОДВАЛ", .07)], 0, .42, .16))

# ---------------------------------------------------------------- 5. stair handrail
# A pipe rail 6 m along X, 7 cm off the wall (Y=0), on brackets every 1.5 m. Tilted to the stair in Unity.
cyl((-3, -.07, 0), (3, -.07, 0), .022, "Rail", 12)
for x in (-2.7, -1.2, .3, 1.8, 2.8):
    cyl((x, -.07, 0), (x, -.07, -.06), .008, "Rail", 8)
    cyl((x, -.07, -.06), (x, 0, -.06), .008, "Rail", 8)
    cyl((x, 0, -.06), (x, .005, -.06), .03, "Rail", 10)   # wall plate
log.append(export("Stair_Handrail_6m"))

# ---------------------------------------------------------------- 6. the residents' things
# sled: bent steel runners, wooden slats
for s in (-1, 1):
    y = s * .17
    tube([(-.42, y, .03), (.2, y, .03), (.38, y, .05), (.46, y, .16), (.40, y, .24), (.32, y, .2)], .009, "SledRunner")
    for x in (-.3, .2):
        cyl((x, y, .03), (x, y, .2), .008, "SledRunner", 8)
for k in range(6):
    box((-.38 + k * .12, 0, .21), (.08, .38, .015), "SledWood")
tube([(.35, -.17, .22), (.48, 0, .26), (.35, .17, .22)], .007, "SledRunner")
log.append(export("Sled"))

# bicycle (an old Soviet one), standing upright, along X, wheels on the floor
R = .33
for x in (-.52, .52):
    torus((x, 0, R), R, .02, "Tyre", rot=(math.radians(90), 0, 0))
    torus((x, 0, R), R - .03, .008, "BikeChrome", rot=(math.radians(90), 0, 0))
    for k in range(12):
        a = k * math.pi / 6
        cyl((x, 0, R), (x + (R - .03) * math.cos(a), 0, R + (R - .03) * math.sin(a)), .002, "BikeChrome", 4)
    cyl((x, -.04, R), (x, .04, R), .025, "BikeChrome", 10)
crank, seat, head = Vector((-.05, 0, .32)), Vector((-.15, 0, .78)), Vector((.4, 0, .82))
for a, b in [(crank, seat), (crank, head + Vector((0, 0, -.1))), (seat + Vector((0, 0, -.04)), head),
             (crank, Vector((-.52, 0, R))), (seat + Vector((0, 0, -.04)), Vector((-.52, 0, R))),
             (head, Vector((.52, 0, R)))]:
    cyl(a, b, .016, "BikeFrame", 10)
cyl(head, head + Vector((-.03, 0, .12)), .014, "BikeChrome", 8)
cyl(head + Vector((-.03, -.25, .12)), head + Vector((-.03, .25, .12)), .011, "BikeChrome", 8)
box(seat + Vector((0, 0, .05)), (.24, .12, .05), "CableBlack")
cyl(seat, seat + Vector((0, 0, .05)), .012, "BikeChrome", 8)
torus(crank, .09, .008, "BikeChrome", rot=(math.radians(90), 0, 0))
log.append(export("Bicycle"))

# jars of preserves for a shelf: a row along X, standing on Z=0
contents = ["Pickles", "Tomatoes", "Pickles", "Jam", "Tomatoes", "Pickles"]
for i, c in enumerate(contents):
    x = -.3 + i * .12 + random.uniform(-.01, .01)
    r, h = random.choice([(.045, .15), (.05, .18), (.04, .12)])
    cyl((x, 0, .004), (x, 0, h - .015), r - .003, c, 14)
    cyl((x, 0, h - .015), (x, 0, h), r - .006, "Lid", 14)
    if i == 2:
        cyl((x, 0, .0), (x, 0, h * .4), r, "Dust", 14)   # one has gone off: a grey crust
log.append(export("Jars_Preserves"))

print("\n".join(log))
