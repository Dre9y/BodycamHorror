"""Export the final cutscene to Unity as one FBX with a single baked take (frames 0-510).
    exec(open(r"D:/BodycamHorror/Blender/tools/elevator_finale_export.py", encoding="utf-8").read())

Works on the open Elevator_Finale.blend: saves it, rearranges for export (one root, gun meshes under
the arms armature, generated textures copied into Assets), exports, then reverts the file.
"""
import bpy, os, shutil
from mathutils import Matrix

OUT_DIR = "D:/BodycamHorror/Assets/Cutscenes/ElevatorFinale/"
OUT = OUT_DIR + "ElevatorFinale.fbx"
TEX_OUT = OUT_DIR + "Textures/"
O = bpy.data.objects
sc = bpy.context.scene

bpy.ops.wm.save_mainfile()
os.makedirs(TEX_OUT, exist_ok=True)

# generated textures live in Blender/tex: copy into the project so the FBX references resolve
for img in bpy.data.images:
    p = bpy.path.abspath(img.filepath)
    if img.source == "FILE" and "/Blender/tex/" in p.replace("\\", "/"):
        dst = TEX_OUT + os.path.basename(p)
        shutil.copyfile(p, dst)
        img.filepath = dst

# bake what constraints / NLA produce (IK arms, gun clips, camera Child Of) into plain keys:
# the FBX exporter only reads the raw pose
def bake(ob, types):
    for o in sc.objects:
        o.hide_select = False
        o.select_set(False)
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    win = bpy.context.window_manager.windows[0]
    with bpy.context.temp_override(window=win, screen=win.screen, view_layer=bpy.context.view_layer,
                                   active_object=ob, object=ob, selected_objects=[ob],
                                   selected_editable_objects=[ob]):
        if types == {"POSE"}:
            bpy.ops.object.mode_set(mode="POSE")
        bpy.ops.nla.bake(frame_start=0, frame_end=510, step=1, only_selected=False,
                         visual_keying=True, clear_constraints=True, clear_parents=False,
                         use_current_action=False, bake_types=types)
        if types == {"POSE"}:
            bpy.ops.object.mode_set(mode="OBJECT")
    ad = ob.animation_data
    ad.action_blend_type = "REPLACE"
    for t in ad.nla_tracks:
        t.mute = True


for name, types in (("Armature", {"POSE"}), ("Infected_Rig", {"POSE"}), ("A_Camera", {"OBJECT"})):
    bake(O[name], types)

# gun meshes ride the arms armature (as in Assets/Pistol/source~/export_pistol.py)
arm = O["Armature"]
sc.frame_set(0)
for name in ("xd_grip_safe", "xd_mag", "xd_slide"):
    ob = O[name]
    if ob.parent is None:
        m = arm.matrix_world.inverted() @ ob.matrix_world
        ob.data.transform(m)
        ob.parent = arm
        ob.matrix_parent_inverse = Matrix.Identity(4)
        ob.matrix_basis = Matrix.Identity(4)

skip = {"IK_Left", "IK_Right", "Pole_Left", "Pole_Right"}
export = [o for o in sc.objects
          if o.type in {"MESH", "ARMATURE", "EMPTY", "CAMERA"} and o.name not in skip
          and not o.hide_render and o.visible_get()]

root = bpy.data.objects.new("ElevatorFinale", None)
sc.collection.objects.link(root)
for o in export:
    if o.parent is None:
        mw = o.matrix_world.copy()
        o.parent = root
        o.matrix_world = mw
export.append(root)

for o in sc.objects:
    o.hide_select = False          # the arms meshes come unselectable from rig.blend
    o.select_set(o in export)
bpy.context.view_layer.objects.active = root
sc.frame_start, sc.frame_end = 0, 510

win = bpy.context.window_manager.windows[0]
with bpy.context.temp_override(window=win, screen=win.screen, view_layer=bpy.context.view_layer,
                               active_object=root, object=root, selected_objects=export,
                               selected_editable_objects=export):
  bpy.ops.export_scene.fbx(
    filepath=OUT,
    check_existing=False,
    use_selection=True,
    object_types={"ARMATURE", "MESH", "EMPTY", "CAMERA"},
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
    bake_anim_use_all_actions=False,
    bake_anim_force_startend_keying=True,
    bake_anim_step=1.0,
    bake_anim_simplify_factor=0.0,
    path_mode="RELATIVE",
    embed_textures=False,
)
print("exported", OUT, len(export), "objects")
bpy.ops.wm.revert_mainfile()
