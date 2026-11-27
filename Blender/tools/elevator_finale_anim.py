"""Final cutscene animation (30 fps). Run after elevator_finale.py, in Elevator_Finale.blend:
    exec(open(r"D:/BodycamHorror/Blender/tools/elevator_finale_anim.py", encoding="utf-8").read())

Beats (frames):
  0   doors open, landing empty, bulb buzzing
  15  doors start to close
  36  the infected lunges in from the left; 44 its right arm is through the gap
  47  doors clamp on the arm; 48-124 the arm thrashes about inside the cab
  70  player draws the pistol (Take), backs off
  112 left hand comes through, both hands grab the leaf edges; 128-178 it prises the doors apart
  178-196 the head pushes into the gap
  196 headshot; 200-232 it falls back out of the doorway
  214 doors close again (shut at 238)
  262 player lowers the gun; 300 cab jolts and goes up; 420 end
"""
import bpy, math, random
from mathutils import Vector, Matrix, Euler

sc = bpy.context.scene
END = 510
sc.frame_start, sc.frame_end = 0, END
sc.render.fps = 30
O = bpy.data.objects
rnd = random.Random(7)

DOOR_SPEED = 0.84 / 40.0    # gap closes 0.84 m in 40 frames


def wipe(ob):
    if ob.animation_data:
        ob.animation_data.action = None
        for t in list(ob.animation_data.nla_tracks):
            ob.animation_data.nla_tracks.remove(t)
    for a in list(bpy.data.actions):
        if a.name.startswith(ob.name + "_Cut") and a.users == 0:
            bpy.data.actions.remove(a)


def new_action(ob, name=None):
    ob.animation_data_create()
    act = bpy.data.actions.new(name or ob.name + "_Cut")
    ob.animation_data.action = act
    return act


def k(target, path, f, value=None, interp=None, index=-1):
    if value is not None:
        if index >= 0:
            getattr(target, path)[index] = value
        else:
            setattr(target, path, value)
    target.keyframe_insert(path, frame=f, index=index)


def fcurves(ob):
    ad = ob.animation_data
    act = ad.action
    for layer in act.layers:
        for strip in layer.strips:
            bag = strip.channelbag(ad.action_slot)
            if bag:
                yield from bag.fcurves


def set_interp(ob, kind, data_path_prefix=""):
    for fc in fcurves(ob):
        if fc.data_path.startswith(data_path_prefix):
            for kp in fc.keyframe_points:
                kp.interpolation = kind


def ease(t):
    return t * t * (3 - 2 * t)


# ------------------------------------------------------------------ doors
def gap_keys():
    """(frame, gap, interpolation) — the gap between the two leaves."""
    g = []
    g += [(0, 0.84, "CONSTANT"), (15, 0.84, "LINEAR")]
    hit = 47
    g += [(hit, 0.84 - (hit - 15) * DOOR_SPEED, "BEZIER")]           # clamps on the arm (~0.17)
    # the motor keeps shoving: small bites on the arm while it thrashes
    f = hit
    while f < 124:
        f += rnd.randint(5, 9)
        g.append((f, rnd.uniform(0.12, 0.19), "BEZIER"))
    # prised apart in jerks, one slip back
    g += [(128, 0.15, "BEZIER"), (138, 0.23, "BEZIER"), (143, 0.20, "BEZIER"), (152, 0.31, "BEZIER"),
          (160, 0.38, "BEZIER"), (166, 0.36, "BEZIER"), (176, 0.50, "BEZIER"), (196, 0.52, "BEZIER"),
          (214, 0.52, "LINEAR")]
    g += [(214 + round(0.50 / DOOR_SPEED), 0.005, "BEZIER"), (END, 0.005, "CONSTANT")]   # a thin seam stays
    return g


def animate_doors():
    keys = gap_keys()
    for side, s in (("L", -1), ("R", 1)):
        for prefix, lag in (("CabDoor_", 0), ("ShaftDoor_", 1)):
            ob = O[prefix + side]
            wipe(ob); new_action(ob)
            for f, g, it in keys:
                ob.location.x = s * g / 2
                ob.keyframe_insert("location", index=0, frame=f + lag)
            for fc in fcurves(ob):
                for kp, (_, _, it) in zip(fc.keyframe_points, keys):
                    kp.interpolation = it
    # tiny shudder of the leaves when they slam shut
    shut = keys[-2][0]
    for side in ("L", "R"):
        ob = O["CabDoor_" + side]
        for df, dy in ((0, 0.0), (2, 0.006), (5, -0.003), (8, 0.0)):
            ob.location.y = -0.645 + dy
            ob.keyframe_insert("location", index=1, frame=shut + df)
    return keys


def gap_at(keys, f):
    for (f0, g0, it), (f1, g1, _) in zip(keys, keys[1:]):
        if f0 <= f <= f1:
            if it == "CONSTANT":
                return g0
            t = (f - f0) / max(1, f1 - f0)
            return g0 + (g1 - g0) * (t if it == "LINEAR" else ease(t))
    return keys[-1][1]


# ------------------------------------------------------------------ infected
def setup_infected():
    rig = O["Infected_Rig"]
    root = O.get("Infected_Root")
    if not root:
        root = bpy.data.objects.new("Infected_Root", None)
        root.empty_display_type = "ARROWS"; root.empty_display_size = 0.3
        bpy.data.collections["Monster"].objects.link(root)
    rig.parent = root
    rig.matrix_parent_inverse = Matrix.Identity(4)
    rig.location = (0, 0, 1.008)
    rig.rotation_euler = (0, 0, math.pi)
    for side in ("Left", "Right"):
        for n in ("IK_" + side, "Pole_" + side):
            e = O.get(n)
            if not e:
                e = bpy.data.objects.new(n, None)
                e.empty_display_type = "SPHERE"; e.empty_display_size = 0.04
                bpy.data.collections["Monster"].objects.link(e)
        fore = rig.pose.bones["mixamorig:%sForeArm" % side]
        for c in list(fore.constraints):
            fore.constraints.remove(c)
        ik = fore.constraints.new("IK")
        ik.name = "IK"; ik.target = O["IK_" + side]; ik.pole_target = O["Pole_" + side]
        ik.chain_count = 2; ik.pole_angle = math.radians(-90 if side == "Right" else -90)
    for pb in rig.pose.bones:
        pb.rotation_mode = "XYZ"
    return rig, root


def pose_key(rig, f, bone, x=0.0, y=0.0, z=0.0):
    pb = rig.pose.bones["mixamorig:" + bone]
    pb.rotation_euler = (x, y, z)
    pb.keyframe_insert("rotation_euler", frame=f)


def animate_infected(keys):
    rig, root = setup_infected()
    for ob in (rig, root, O["IK_Left"], O["IK_Right"], O["Pole_Left"], O["Pole_Right"]):
        wipe(ob); new_action(ob)

    # --- root: feet position (world) and yaw. 0 yaw = facing +Y (into the lift)
    def R(f, x, y, yaw=0.0, tilt=0.0, z=0.0):
        root.location = (x, y, z); root.rotation_euler = (tilt, 0, yaw)
        root.keyframe_insert("location", frame=f); root.keyframe_insert("rotation_euler", frame=f)

    R(0, -1.6, -1.6, -0.9)
    R(33, -1.6, -1.6, -0.9)
    R(38, -0.75, -1.15, -0.5)
    R(43, -0.15, -1.02, -0.1)
    for f in range(50, 124, 9):                    # body jerking behind the leaf while the arm thrashes
        R(f, -0.15 + rnd.uniform(-0.04, 0.04), -1.02 + rnd.uniform(-0.03, 0.02), rnd.uniform(-0.12, 0.08))
    R(128, -0.08, -1.0, 0.0)
    R(160, -0.02, -0.98, 0.0)
    R(182, 0.0, -0.94, 0.04)
    R(196, 0.0, -0.94, 0.04)
    # headshot: knocked back, topples backwards out of the doorway
    R(198, 0.0, -0.98, 0.06, -0.05)
    R(203, 0.02, -1.06, 0.1, 0.3)
    R(210, 0.05, -1.12, 0.12, 0.95)
    R(217, 0.06, -1.15, 0.12, 1.47, 0.05)
    R(221, 0.06, -1.15, 0.12, 1.39, 0.05)
    R(227, 0.06, -1.15, 0.12, 1.48, 0.05)
    for fc in fcurves(root):
        for kp in fc.keyframe_points:
            if 198 <= kp.co.x <= 217:
                kp.interpolation = "QUAD" if fc.data_path == "rotation_euler" else "BEZIER"
                kp.easing = "EASE_IN"

    # --- spine / head: x = bend forward, y = turn, z = lean sideways
    def body(f, spine=0.0, chest=0.0, neck=0.0, head=0.0, turn=0.0, lean=0.0, head_turn=0.0, head_roll=0.0):
        pose_key(rig, f, "Spine", spine, turn * 0.3, lean * 0.4)
        pose_key(rig, f, "Spine1", spine, turn * 0.3, lean * 0.3)
        pose_key(rig, f, "Spine2", chest, turn * 0.4, lean * 0.3)
        pose_key(rig, f, "Neck", neck, head_turn * 0.4, head_roll * 0.4)
        pose_key(rig, f, "Head", head, head_turn * 0.6, head_roll * 0.6)

    body(0, 0.1, 0.05, 0.05, 0.05)
    body(33, 0.1, 0.05, 0.05, 0.05)
    body(40, 0.2, 0.15, -0.1, -0.15, 0.2)
    for f in range(46, 124, 6):
        body(f, rnd.uniform(0.12, 0.22), rnd.uniform(0.1, 0.2), rnd.uniform(-0.25, 0.1), rnd.uniform(-0.3, 0.1),
             rnd.uniform(0.1, 0.35), rnd.uniform(-0.25, 0.25), rnd.uniform(-0.4, 0.4), rnd.uniform(-0.3, 0.3))
    body(128, 0.18, 0.15, -0.1, -0.1)
    body(145, 0.15, 0.12, -0.15, -0.15, 0, 0.12)
    body(160, 0.17, 0.14, -0.2, -0.2, 0, -0.1)
    body(176, 0.2, 0.17, -0.25, -0.3, 0, 0.0, 0.1, 0.25)    # face into the gap, head cocked, looking up at the player
    body(186, 0.22, 0.18, -0.28, -0.32, 0, 0.0, -0.05, -0.2)
    body(192, 0.23, 0.19, -0.3, -0.3, 0, 0.0, 0.05, 0.3)
    body(196, 0.23, 0.19, -0.3, -0.32, 0, 0.0, 0.0, 0.25)
    body(199, 0.05, -0.1, 0.5, 0.8, 0, 0.0, 0.2, 0.1)           # head snaps back
    body(205, -0.1, -0.2, 0.4, 0.5, 0, 0.2, 0.3, 0.2)
    body(217, -0.2, -0.3, 0.2, 0.3, 0, 0.1, 0.6, 0.3)
    body(227, -0.15, -0.25, 0.1, 0.2, 0, 0.1, 0.7, 0.3)
    # legs: braced while prising, buckle when shot
    def legs(f, hipL=0.0, kneeL=0.0, hipR=0.0, kneeR=0.0):
        pose_key(rig, f, "LeftUpLeg", -hipL); pose_key(rig, f, "LeftLeg", kneeL)
        pose_key(rig, f, "RightUpLeg", -hipR); pose_key(rig, f, "RightLeg", kneeR)
    legs(0); legs(36); legs(43, 0.3, 0.4, -0.2, 0.2); legs(128, 0.25, 0.35, -0.15, 0.2)
    legs(176, 0.35, 0.45, 0.0, 0.3); legs(198, 0.35, 0.45, 0.0, 0.3)
    legs(205, 0.5, 0.9, 0.3, 0.6); legs(217, -0.3, 0.3, 0.6, 0.2); legs(227, -0.4, 0.2, 0.7, 0.3)

    # fingers: claw (curl the index chain)
    def claw(f, side, curl):
        for i in (1, 2, 3):
            pose_key(rig, f, "%sHandIndex%d" % (side, i), curl * (0.9 if i == 1 else 0.7))
    for side in ("Left", "Right"):
        claw(0, side, 0.0)
    for f in range(46, 124, 4):
        claw(f, "Right", rnd.uniform(-0.15, 0.25))
    for f in (128, 196):
        claw(f, "Right", 0.35); claw(f, "Left", 0.35)
    claw(112, "Left", 0.0)
    claw(204, "Right", 0.0); claw(204, "Left", 0.0)

    # --- arms (IK targets in world space)
    ikR, ikL, poleR, poleL = O["IK_Right"], O["IK_Left"], O["Pole_Right"], O["Pole_Left"]
    ikc = {s: rig.pose.bones["mixamorig:%sForeArm" % s].constraints["IK"] for s in ("Left", "Right")}

    def T(e, f, p):
        e.location = p; e.keyframe_insert("location", frame=f)

    def infl(side, f, v):
        ikc[side].influence = v
        ikc[side].keyframe_insert("influence", frame=f)

    infl("Right", 0, 0.0); infl("Right", 36, 0.0); infl("Right", 39, 1.0)
    infl("Left", 0, 0.0); infl("Left", 104, 0.0); infl("Left", 108, 1.0)
    infl("Right", 199, 1.0); infl("Right", 204, 0.0)
    infl("Left", 198, 1.0); infl("Left", 202, 0.0)

    # right arm: shoots through the gap and thrashes about inside the cab
    T(ikR, 36, (-0.45, -0.95, 1.45))
    T(ikR, 41, (-0.03, -0.66, 1.40))
    T(ikR, 45, (0.03, -0.45, 1.34))
    f = 45
    while f < 120:
        f += rnd.randint(4, 7)
        T(ikR, f, (rnd.uniform(-0.12, 0.15), rnd.uniform(-0.5, -0.36), rnd.uniform(1.1, 1.55)))
    # both hands on the leaf edges, follow the edges as the doors are forced
    for f in (124, 128, 138, 143, 152, 160, 166, 176, 186, 196):
        g = gap_at(keys, f)
        T(ikR, f, (g / 2 + 0.035, -0.60, 1.22 + (0.03 if f % 2 else 0)))
    T(ikL, 104, (-0.25, -0.85, 1.45))
    T(ikL, 110, (-0.06, -0.66, 1.52))
    for f in (116, 124, 128, 138, 143, 152, 160, 166, 176, 186, 196):
        g = gap_at(keys, f)
        T(ikL, f, (-g / 2 - 0.035, -0.60, 1.50 - (0.02 if f % 2 else 0)))
    # elbows pushed out and down
    for f, xr, xl in ((0, 0.45, -0.45), (124, 0.35, -0.4), (196, 0.45, -0.5)):
        T(poleR, f, (xr, -0.9, 0.95)); T(poleL, f, (xl, -0.9, 1.05))
    # after the shot the arms fling (IK off; keyed FK)
    pose_key(rig, 0, "RightArm"); pose_key(rig, 198, "RightArm")
    pose_key(rig, 0, "LeftArm"); pose_key(rig, 198, "LeftArm")
    pose_key(rig, 208, "RightArm", -0.8, 0.0, -0.9); pose_key(rig, 208, "LeftArm", -0.8, 0.0, 0.9)
    pose_key(rig, 222, "RightArm", -1.2, 0.3, -1.3); pose_key(rig, 222, "LeftArm", -1.0, -0.2, 1.4)
    return rig


# ------------------------------------------------------------------ player
def chest_offset_action(arm):
    """Additive aim: NLA COMBINE track rotating the Chest (arms + gun) on top of the clips."""
    act = bpy.data.actions.get("Player_Aim") or bpy.data.actions.new("Player_Aim")
    for layer in list(act.layers):
        act.layers.remove(layer)
    return act


def animate_player(cab):
    arm = O["Armature"]
    arm.parent = cab
    wipe(arm)
    ad = arm.animation_data_create()
    ad.action = None
    A = bpy.data.actions

    def strip(track, name, start, act_start=None, act_end=None, hold="HOLD_FORWARD", blend="REPLACE"):
        act = A[name]
        s = track.strips.new(name, int(start), act)
        if act_start is not None:
            s.action_frame_start = act_start
        if act_end is not None:
            s.action_frame_end = act_end
        s.frame_end = start + (s.action_frame_end - s.action_frame_start)
        s.extrapolation = hold
        s.blend_type = blend
        if act.slots:
            s.action_slot = act.slots[0]
        return s

    clips = ad.nla_tracks.new(); clips.name = "Clips"
    strip(clips, "Hide", 0, 10, 11, "HOLD")            # gun out of sight
    strip(clips, "Take", 66)                           # draw (12 frames)
    s = strip(clips, "Idle", 79, 0, 1); s.frame_end = 194
    strip(clips, "Shoot", 196)                         # 10 frames
    s = strip(clips, "Idle", 207, 0, 1); s.frame_end = 258
    strip(clips, "Hide", 262)
    # body / camera keys on top
    act = new_action(arm, "Player_Cut")
    ad.action_blend_type = "COMBINE"
    pb_t = arm.pose.bones["CameraTarget"]
    pb_c = arm.pose.bones["Chest"]     # stays in the clips' rotation mode (quaternion), keys combine on top

    def body(f, y=0.3, x=0.0, z=0.0):
        arm.location = (x, y, z); arm.keyframe_insert("location", frame=f)

    def look(f, x=0.0, z=0.0, y=-2.0):
        """CameraTarget in armature space, relative to its rest (0, -2, 1.62)."""
        pb_t.location = (0, 0, 0)
        bpy.context.view_layer.update()
        rest = pb_t.matrix.copy()
        want = Matrix.Translation((x, y, 1.62 + z))
        pb_t.matrix = want @ rest.to_3x3().to_4x4()
        bpy.context.view_layer.update()
        pb_t.keyframe_insert("location", frame=f)

    def aim(f, up=0.0, left=0.0):
        pb_c.rotation_quaternion = Euler((up, left, 0)).to_quaternion()
        pb_c.keyframe_insert("rotation_quaternion", frame=f)

    body(0); body(40)
    body(46, 0.42); body(52, 0.40)                 # flinches back against the wall
    body(120, 0.40); body(176, 0.34)
    body(196, 0.34); body(197, 0.36); body(204, 0.34)
    body(300, 0.34); body(303, 0.34, 0, -0.03); body(308, 0.34)
    look(0, 0.0, -0.1); look(14, 0.15, -0.2)      # glance at the panel… doors start closing
    look(30, 0.0, -0.05)
    look(42, -0.1, 0.0); look(47, 0.05, -0.15)
    for f in range(56, 124, 10):                   # follows the thrashing hand
        look(f, rnd.uniform(-0.25, 0.25), rnd.uniform(-0.45, -0.1))
    look(130, 0.0, 0.0); look(176, 0.0, 0.08); look(196, 0.0, 0.1)
    look(198, 0.05, 0.25); look(206, 0.0, 0.05)    # recoil
    look(230, 0.0, -0.25); look(258, 0.0, -0.1)
    look(300, 0.0, -0.1); look(304, 0.03, -0.18); look(330, 0.0, 0.0)
    look(380, -0.15, 0.1); look(420, -0.1, 0.05); look(470, 0.05, -0.05); look(510, 0.0, 0.0)
    aim(0); aim(79); aim(110, 0.0, 0.1); aim(150, 0.0, -0.05); aim(178, 0.0, 0.0); aim(196, 0.01, 0.0)
    aim(206, 0.0, 0.0); aim(240, -0.15, 0.0)
    # breathing / hand-held noise on the camera target
    for fc in fcurves(arm):
        if fc.data_path.endswith('"CameraTarget"].location') and fc.array_index in (0, 2):
            n = fc.modifiers.new("NOISE"); n.scale = 25; n.strength = 0.03; n.phase = rnd.uniform(0, 100)
    return arm


# ------------------------------------------------------------------ lift, lights, gunshot
def animate_lift_and_lights(keys):
    cab = O["Cab"]
    wipe(cab); new_action(cab)
    shut = keys[-2][0]
    # jolt, pick up speed, then climb evenly (0.05 m a frame) past the floors above
    for f, z in ((0, 0.0), (300, 0.0), (302, -0.035), (306, 0.005), (312, 0.0), (360, 1.2), (END, 1.2 + 0.05 * (END - 360))):
        cab.location.z = z; cab.keyframe_insert("location", index=2, frame=f)
    for fc in fcurves(cab):
        if fc.data_path == "location" and fc.array_index == 2:
            for kp in fc.keyframe_points:
                if kp.co.x >= 312:
                    kp.interpolation = "QUAD" if kp.co.x < 360 else "LINEAR"; kp.easing = "EASE_IN"
    # slam shake when the doors shut
    for f, x in ((shut, 0.0), (shut + 2, 0.008), (shut + 5, -0.004), (shut + 9, 0.0)):
        cab.location.x = x; cab.keyframe_insert("location", index=0, frame=f)
    for fc in fcurves(cab):
        if fc.data_path == "location" and fc.array_index in (0, 1):
            n = fc.modifiers.new("NOISE"); n.scale = 3; n.strength = 0.004
            n.use_restricted_range = True; n.frame_start = 304; n.frame_end = END; n.blend_in = 10

    cl = O["Cab_Light"].data; wipe(cl); new_action(cl)
    for f, e in ((0, 45), (236, 45), (240, 10), (242, 45), (300, 45), (302, 4), (305, 45), (308, 12), (311, 45), (END, 45)):
        cl.energy = e; cl.keyframe_insert("energy", frame=f)
    bl = O["Landing_Light"].data; wipe(bl); new_action(bl)
    f = 0
    while f < END:
        bl.energy = 40 if rnd.random() > 0.15 else rnd.uniform(3, 15)
        bl.keyframe_insert("energy", frame=f)
        f += rnd.randint(3, 25)
    for fc in fcurves(bl):
        for kp in fc.keyframe_points:
            kp.interpolation = "CONSTANT"


def muzzle_flash(shot=196):
    arm = O["Armature"]
    col = bpy.data.collections["PlayerArms"]
    lo = O.get("Muzzle_Light")
    if not lo:
        ld = bpy.data.lights.new("Muzzle_Light", "POINT"); ld.color = (1.0, 0.75, 0.4); ld.shadow_soft_size = 0.02
        lo = bpy.data.objects.new("Muzzle_Light", ld); col.objects.link(lo)
    ld = lo.data; wipe(ld); new_action(ld)
    for f, e in ((0, 0), (shot, 0), (shot + 1, 900), (shot + 2, 250), (shot + 3, 0)):
        ld.energy = e; ld.keyframe_insert("energy", frame=f)
    for fc in fcurves(ld):
        for kp in fc.keyframe_points:
            kp.interpolation = "CONSTANT"
    # flash card
    fl = O.get("Muzzle_Flash")
    if not fl:
        me = bpy.data.meshes.new("Muzzle_Flash")
        me.from_pydata([(-0.06, 0, -0.06), (0.06, 0, -0.06), (0.06, 0, 0.06), (-0.06, 0, 0.06)], [], [(0, 1, 2, 3)])
        me.uv_layers.new().data.foreach_set("uv", [0, 0, 1, 0, 1, 1, 0, 1])
        fl = bpy.data.objects.new("Muzzle_Flash", me); col.objects.link(fl)
        m = bpy.data.materials.new("MuzzleFlash"); m.use_nodes = True
        nt = m.node_tree; nt.nodes.clear()
        out = nt.nodes.new("ShaderNodeOutputMaterial")
        em = nt.nodes.new("ShaderNodeEmission"); em.inputs[1].default_value = 12
        tr = nt.nodes.new("ShaderNodeBsdfTransparent")
        mix = nt.nodes.new("ShaderNodeMixShader")
        tex = nt.nodes.new("ShaderNodeTexImage")
        tex.image = bpy.data.images.load("D:/BodycamHorror/Assets/Textures/FX/MuzzleFlash.png", check_existing=True)
        nt.links.new(tex.outputs[0], em.inputs[0]); nt.links.new(tex.outputs[1], mix.inputs[0])
        nt.links.new(tr.outputs[0], mix.inputs[1]); nt.links.new(em.outputs[0], mix.inputs[2])
        nt.links.new(mix.outputs[0], out.inputs[0])
        m.blend_method = "BLEND" if hasattr(m, "blend_method") else None
        me.materials.append(m)
    # put the card on the muzzle: the slide vertex furthest along the barrel, facing the camera
    sc.frame_set(shot)
    dg = bpy.context.evaluated_depsgraph_get()
    sl = O["xd_slide"].evaluated_get(dg)
    me_s = sl.to_mesh()
    base_m = arm.matrix_world @ arm.pose.bones["Base"].matrix
    cam = O["A_Camera"].evaluated_get(dg)
    fwd = -(cam.matrix_world.to_3x3().col[2])
    pts = [sl.matrix_world @ v.co for v in me_s.vertices]
    tip = max(pts, key=lambda p: p.dot(fwd))
    sl.to_mesh_clear()
    tip = tip + fwd * 0.03
    rot = cam.matrix_world.to_3x3() @ Euler((math.pi / 2, 0, 0)).to_matrix()
    for o in (fl, lo):
        o.parent = arm; o.parent_type = "BONE"; o.parent_bone = "Base"
        o.matrix_parent_inverse = Matrix.Identity(4)
        bpy.context.view_layer.update()
        o.matrix_world = Matrix.Translation(tip) @ rot.to_4x4()
        bpy.context.view_layer.update()
    fl.rotation_euler.rotate_axis("Y", rnd.uniform(0, 6.28))
    wipe(fl); new_action(fl)
    for f, s in ((0, 0.0), (shot, 0.0), (shot + 1, 2.2), (shot + 2, 1.4), (shot + 3, 0.0)):
        fl.scale = (s, s, s); fl.keyframe_insert("scale", frame=f)
    for fc in fcurves(fl):
        for kp in fc.keyframe_points:
            kp.interpolation = "CONSTANT"


def blood_splat(shot=196):
    """Spray on the landing wall behind the head, appears with the shot."""
    col = bpy.data.collections["Landing"]
    sp = O.get("Blood_Splat")
    if not sp:
        me = bpy.data.meshes.new("Blood_Splat")
        me.from_pydata([(-0.5, 0, -0.5), (0.5, 0, -0.5), (0.5, 0, 0.5), (-0.5, 0, 0.5)], [], [(0, 1, 2, 3)])
        me.uv_layers.new().data.foreach_set("uv", [0, 0, 1, 0, 1, 1, 0, 1])
        sp = bpy.data.objects.new("Blood_Splat", me); col.objects.link(sp)
        m = bpy.data.materials.new("BloodSplat"); m.use_nodes = True
        nt = m.node_tree; bs = nt.nodes["Principled BSDF"]
        tex = nt.nodes.new("ShaderNodeTexImage")
        tex.image = bpy.data.images.load("D:/BodycamHorror/Assets/Textures/ChainRoom/NowYouSeeMe_Blood.png", check_existing=True)
        nt.links.new(tex.outputs[0], bs.inputs["Base Color"]); nt.links.new(tex.outputs[1], bs.inputs["Alpha"])
        bs.inputs["Roughness"].default_value = 0.2
        me.materials.append(m)
    sp.location = (0.1, -3.33, 1.5); sp.rotation_euler = (0, 0.4, 0)
    wipe(sp); new_action(sp)
    for f, s in ((0, 0.0), (shot, 0.0), (shot + 1, 0.8)):
        sp.scale = (s, s, s); sp.keyframe_insert("scale", frame=f)
    for fc in fcurves(sp):
        for kp in fc.keyframe_points:
            kp.interpolation = "CONSTANT"


def run():
    keys = animate_doors()
    animate_infected(keys)
    animate_player(O["Cab"])
    animate_lift_and_lights(keys)
    muzzle_flash()
    blood_splat()
    sc.camera = O["A_Camera"]
    sc.frame_set(0)
    print("cutscene animated, doors shut at", keys[-2][0])


run()
