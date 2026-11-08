import bpy
from mathutils import Matrix

OUT = "E:/Unity/Projects/BodycamHorror/Assets/Pistol/PistolArms.fbx"
MESHES = ["DMC5_Soldier_Gear", "DMC5_Soldier_Gloves", "DMC5_Soldier_Shirt", "DMC5_Soldier_Skin", "xd_grip_safe", "xd_mag", "xd_slide"]

print("=====BEGIN=====")
arm = bpy.data.objects["Armature"]
scene = bpy.context.scene
ad = arm.animation_data

def use(action):
    ad.action = action
    if action.slots:
        ad.action_slot = action.slots[0]

def world(name):
    return arm.matrix_world @ arm.pose.bones[name].matrix

# --- timing of the reload, relative to the gun
print("--- reload timing (frame: mag-from-rest, slide-from-rest)")
use(bpy.data.actions["Idle"])
scene.frame_set(0)
inv = world("Base").inverted()
mag_rest = inv @ world("Mag").translation
up_rest = inv @ world("Up").translation
print("slide rest in Base space", tuple(round(v, 4) for v in up_rest))
use(bpy.data.actions["Reload"])
slide_back = None
for f in range(0, 46):
    scene.frame_set(f)
    i = world("Base").inverted()
    m = (i @ world("Mag").translation - mag_rest).length
    u = i @ world("Up").translation - up_rest
    if f == 0:
        slide_back = u.copy()
    print("  f%02d mag %.3f slide %.3f" % (f, m, u.length))
print("slide travel vector in Base space", tuple(round(v, 4) for v in slide_back), "len", round(slide_back.length, 4))

use(bpy.data.actions["Shoot"])
print("--- shoot: slide and trigger finger")
for f in range(0, 11):
    scene.frame_set(f)
    i = world("Base").inverted()
    u = i @ world("Up").translation - up_rest
    print("  f%02d slide %.3f base %s" % (f, u.length, tuple(round(v, 3) for v in world("Base").translation)))

# --- clean the actions so the exporter takes all of them
def bags(action):
    for layer in action.layers:
        for strip in layer.strips:
            for bag in strip.channelbags:
                yield bag

for action in bpy.data.actions:
    for bag in bags(action):
        dead = []
        for fc in bag.fcurves:
            try:
                arm.path_resolve(fc.data_path, False)
            except ValueError:
                dead.append(fc)
        for fc in dead:
            print("removing dead curve", action.name, fc.data_path)
            bag.fcurves.remove(fc)

idle = bpy.data.actions["Idle"]
for bag in bags(idle):
    for fc in bag.fcurves:
        fc.keyframe_points.insert(1.0, fc.evaluate(0.0), options={"FAST"})
        fc.update()
print("idle range", tuple(idle.frame_range))

# --- bake the gun meshes into armature space and export
for name in MESHES:
    ob = bpy.data.objects[name]
    if ob.parent is None:
        m = ob.matrix_world.copy()
        ob.data.transform(m)
        ob.matrix_world = Matrix.Identity(4)
        ob.parent = arm
        ob.matrix_parent_inverse = Matrix.Identity(4)

for ob in bpy.context.view_layer.objects:
    ob.select_set(False)
arm.select_set(True)
for name in MESHES:
    bpy.data.objects[name].hide_select = False
    bpy.data.objects[name].select_set(True)
print("selected for export", [o.name for o in bpy.context.selected_objects])
bpy.context.view_layer.objects.active = arm

use(bpy.data.actions["Idle"])
scene.frame_set(0)
scene.frame_start = 0
scene.frame_end = 50

bpy.ops.export_scene.fbx(
    filepath=OUT,
    check_existing=False,
    use_selection=True,
    object_types={"ARMATURE", "MESH"},
    apply_unit_scale=True,
    apply_scale_options="FBX_SCALE_ALL",
    global_scale=1.0,
    axis_forward="-Z",
    axis_up="Y",
    use_mesh_modifiers=False,
    mesh_smooth_type="FACE",
    use_tspace=True,
    add_leaf_bones=False,
    primary_bone_axis="Y",
    secondary_bone_axis="X",
    armature_nodetype="NULL",
    use_armature_deform_only=False,
    bake_anim=True,
    bake_anim_use_all_bones=True,
    bake_anim_use_nla_strips=False,
    bake_anim_use_all_actions=True,
    bake_anim_force_startend_keying=True,
    bake_anim_step=1.0,
    bake_anim_simplify_factor=0.0,
    path_mode="AUTO",
    embed_textures=False,
)
print("exported", OUT)
print("=====END=====")
