"""
The Sniffer's hostile run for the Manthing: Sniffer.fbx has its own (non-humanoid) skeleton and a bent, half-quadruped
bind pose, which a humanoid avatar in Unity cannot take as its T-pose. This builds a clean T-pose rig with Unity's
humanoid bone names, bakes ANIM_Sniffer_HostileRun onto it frame by frame (each bone takes the world-space rotation its
Sniffer bone makes from its bind pose, the bind directions aligned to the T-pose ones) and exports only the new rig.

Run: blender -b --python sniffer_run_rig.py -- <Sniffer.fbx> <out.fbx> [action name part]
"""
import math
import sys
import bpy
from mathutils import Vector, Matrix

args = sys.argv[sys.argv.index("--") + 1:]
SRC, OUT = args[0], args[1]
TAKE = args[2] if len(args) > 2 else "ANIM_Sniffer_HostileRun"

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=SRC, automatic_bone_orientation=False)
sc = bpy.context.scene
src = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
act = next(a for a in bpy.data.actions if a.name.endswith(TAKE))
src.animation_data_create()
for t in list(src.animation_data.nla_tracks):
    src.animation_data.nla_tracks.remove(t)
src.animation_data.action = act
F0, F1 = int(act.frame_range[0]), int(act.frame_range[1])

# face it down -Y (Blender's usual front, which Unity takes as +Z): it faces the way its hips' right x up points
bpy.context.view_layer.update()
W0 = src.matrix_world
right = (W0 @ src.data.bones['Hip_R'].head_local) - (W0 @ src.data.bones['Hip_L'].head_local)
right.z = 0.0
fwd = Vector((0, 0, 1)).cross(right.normalized())
turn = math.atan2(fwd.x, -fwd.y)   # angle from -Y round to fwd
# applied as a matrix, not to the object: the action keys the object's own transform and would put it back
RZ = Matrix.Rotation(-turn, 4, 'Z')
SW = RZ @ W0.copy()

def rest_head(n): return SW @ src.data.bones[n].head_local

MAP = {
    'Hips': ('HIPS', 'Spine1_M'),
    'Spine': ('Spine1_M', 'Spine2_M'),
    'Chest': ('Spine2_M', 'Chest_M'),
    'UpperChest': ('Chest_M', 'Neck_M'),
    'Neck': ('Neck_M', 'Head_M'),
    'Head': ('Head_M', 'HeadEnd_M'),
}
for side, s in (('Left', 'L'), ('Right', 'R')):
    MAP.update({
        side + 'Shoulder': ('Scapula_%s' % s, 'Shoulder_%s' % s),
        side + 'UpperArm': ('Shoulder_%s' % s, 'Elbow_%s' % s),
        side + 'LowerArm': ('Elbow_%s' % s, 'Wrist_%s' % s),
        side + 'Hand': ('Wrist_%s' % s, 'MiddleFinger1_%s' % s),
        side + 'UpperLeg': ('Hip_%s' % s, 'Knee_%s' % s),
        side + 'LowerLeg': ('Knee_%s' % s, 'Ankle1_%s' % s),
        side + 'Foot': ('Ankle1_%s' % s, 'Ankle4_%s' % s),
        side + 'Toes': ('Ankle4_%s' % s, 'Ankle5End_%s' % s),
    })
PARENT = {'Hips': None, 'Spine': 'Hips', 'Chest': 'Spine', 'UpperChest': 'Chest', 'Neck': 'UpperChest', 'Head': 'Neck'}
for side in ('Left', 'Right'):
    PARENT.update({side + 'Shoulder': 'UpperChest', side + 'UpperArm': side + 'Shoulder', side + 'LowerArm': side + 'UpperArm',
                   side + 'Hand': side + 'LowerArm', side + 'UpperLeg': 'Hips', side + 'LowerLeg': side + 'UpperLeg',
                   side + 'Foot': side + 'LowerLeg', side + 'Toes': side + 'Foot'})
ORDER = ['Hips', 'Spine', 'Chest', 'UpperChest', 'Neck', 'Head']
for side in ('Left', 'Right'):
    ORDER += [side + n for n in ('Shoulder', 'UpperArm', 'LowerArm', 'Hand', 'UpperLeg', 'LowerLeg', 'Foot', 'Toes')]

def seg(name):
    a, b = MAP[name]
    return max((rest_head(b) - rest_head(a)).length, 0.5)

# ---- the T-pose, from the segment lengths alone: faces -Y, its left at +X, up +Z
heads, tails = {}, {}
leg = seg('LeftUpperLeg') + seg('LeftLowerLeg')
hip_w = (rest_head('Hip_L') - rest_head('Hip_R')).length / 2
sh_w = (rest_head('Scapula_L') - rest_head('Scapula_R')).length / 2
foot_drop = 0.35 * seg('LeftFoot')
z0 = leg + foot_drop
p = Vector((0.0, 0.0, z0))
for n in ['Hips', 'Spine', 'Chest', 'UpperChest', 'Neck', 'Head']:
    heads[n] = p.copy()
    p = p + Vector((0, 0, seg(n)))
    tails[n] = p.copy()
chest_top = heads['Neck'].z
for side, sx in (('Left', 1.0), ('Right', -1.0)):
    q = Vector((sx * sh_w, 0.0, chest_top - 0.15 * seg('UpperChest')))
    for n in ('Shoulder', 'UpperArm', 'LowerArm', 'Hand'):
        heads[side + n] = q.copy()
        q = q + Vector((sx * seg(side + n), 0, 0))
        tails[side + n] = q.copy()
    q = Vector((sx * hip_w, 0.0, z0))
    for n in ('UpperLeg', 'LowerLeg'):
        heads[side + n] = q.copy()
        q = q - Vector((0, 0, seg(side + n)))
        tails[side + n] = q.copy()
    heads[side + 'Foot'] = q.copy()
    q = q + Vector((0, -0.94, -0.35)).normalized() * seg(side + 'Foot')
    tails[side + 'Foot'] = q.copy()
    heads[side + 'Toes'] = q.copy()
    q = q + Vector((0, -1, 0)) * seg(side + 'Toes')
    tails[side + 'Toes'] = q.copy()

arm_data = bpy.data.armatures.new('SnifferRig')
rig = bpy.data.objects.new('SnifferRig', arm_data)
sc.collection.objects.link(rig)
for o in bpy.context.view_layer.objects: o.select_set(False)
rig.select_set(True)
bpy.context.view_layer.objects.active = rig
bpy.ops.object.mode_set(mode='EDIT')
eb = {}
for n in ORDER:
    b = arm_data.edit_bones.new(n)
    b.head, b.tail = heads[n], tails[n]
    b.roll = 0.0
    eb[n] = b
for n in ORDER:
    if PARENT[n]:
        eb[n].parent = eb[PARENT[n]]
        eb[n].use_connect = False
bpy.ops.object.mode_set(mode='POSE')

out_act = bpy.data.actions.new('RampageRun')
rig.animation_data_create().action = out_act
for pb in rig.pose.bones:
    pb.rotation_mode = 'QUATERNION'

R3 = lambda m: m.to_3x3().normalized()
rest = {n: rig.data.bones[n].matrix_local.copy() for n in ORDER}
srcRestRot = {n: R3(SW) @ R3(src.data.bones[MAP[n][0]].matrix_local) for n in ORDER}
align = {}
for n in ORDER:
    a, b = MAP[n]
    want = (rest_head(b) - rest_head(a)).normalized()
    have = R3(rest[n]).col[1].normalized()
    align[n] = have.rotation_difference(want).to_matrix()

# the hips' height: the source's, scaled to the new rig (the same lengths, so the same)
for f in range(F0, F1 + 1):
    sc.frame_set(f)
    SWf = RZ @ src.matrix_world   # the object's own keyed transform this frame, turned round
    target = {}
    for n in ORDER:
        a, b = MAP[n]
        sp = src.pose.bones[a]
        delta = (R3(SWf) @ R3(sp.matrix)) @ srcRestRot[n].inverted()
        # the in-between bones (NeckPart, Spine1Part, ElbowPart...) bend the segment too: aim it exactly at where the
        # next joint is now, keeping the twist the joint bone gives it
        rest_dir = (rest_head(b) - rest_head(a)).normalized()
        now_dir = ((SWf @ src.pose.bones[b].head) - (SWf @ sp.head)).normalized()
        fix = (delta @ rest_dir).rotation_difference(now_dir).to_matrix()
        target[n] = fix @ delta @ align[n] @ R3(rest[n])
    for n in ORDER:
        pb = rig.pose.bones[n]
        restR = R3(rest[n])
        par = PARENT[n]
        if par:
            local = (target[par] @ R3(rest[par]).inverted() @ restR).inverted() @ target[n]
        else:
            local = restR.inverted() @ target[n]
            pb.location = restR.inverted() @ ((SWf @ src.pose.bones['HIPS'].head) - rest[n].to_translation())
            pb.keyframe_insert('location', frame=f)
        pb.rotation_quaternion = local.to_quaternion()
        pb.keyframe_insert('rotation_quaternion', frame=f)

sc.frame_start, sc.frame_end = F0, F1
sc.render.fps = 24
bpy.ops.object.mode_set(mode='OBJECT')

err = 0.0
for f in (F0, (F0 + F1) // 2, F1):
    sc.frame_set(f)
    for n in ('LeftHand', 'RightFoot', 'Head', 'LeftLowerLeg', 'RightLowerArm'):
        got = rig.matrix_world @ rig.pose.bones[n].head
        want = (RZ @ src.matrix_world) @ src.pose.bones[MAP[n][0]].head
        err = max(err, (got - want).length)
print("FRAMES", F0, F1, "MAX JOINT ERROR", round(err, 3), "of leg length", round(leg, 1))
# what the retarget reads: each bone's direction against its Sniffer segment's
worst = (0.0, '')
for f in range(F0, F1 + 1, 3):
    sc.frame_set(f)
    for n in ORDER:
        a, b = MAP[n]
        want = ((RZ @ src.matrix_world) @ src.pose.bones[b].head) - ((RZ @ src.matrix_world) @ src.pose.bones[a].head)
        pb = rig.pose.bones[n]
        got = (rig.matrix_world @ pb.tail) - (rig.matrix_world @ pb.head)
        ang = math.degrees(got.angle(want)) if want.length > 1e-4 and got.length > 1e-4 else 0.0
        if ang > worst[0]: worst = (ang, '%s @%d' % (n, f))
print("WORST DIRECTION ERROR %.2f deg (%s)" % worst)

for o in bpy.context.view_layer.objects: o.select_set(False)
rig.select_set(True)
bpy.context.view_layer.objects.active = rig
bpy.ops.export_scene.fbx(filepath=OUT, use_selection=True, object_types={'ARMATURE'}, add_leaf_bones=False,
                         bake_anim=True, bake_anim_use_all_bones=True, bake_anim_use_nla_strips=False,
                         bake_anim_use_all_actions=False, bake_anim_force_startend_keying=True,
                         bake_anim_simplify_factor=0.0, apply_scale_options='FBX_SCALE_ALL')
print("EXPORTED", OUT)
