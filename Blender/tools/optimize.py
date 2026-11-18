"""Mesh optimisation pass for BodycamHorror props, run inside Blender over the bridge after propkit.py:

    exec(open(r"D:\\BodycamHorror\\Blender\\tools\\propkit.py").read())
    exec(open(r"D:\\BodycamHorror\\Blender\\tools\\optimize.py").read())
    print(decimate_all())

Each FBX in Assets/Generated/Imported listed in DECIMATE is imported, its meshes thinned with a Decimate
(collapse) modifier to the given share of their triangles, and exported back over itself with the same settings
the prop scripts use. The UVs survive, so the baked textures still fit; the .blend sources keep the full detail,
so run this again after re-exporting a prop from its .blend.
"""
import bpy, os

DECIMATE = {
    # prop: {mesh name (or "*" for every mesh): share of triangles kept}
    "Sofa": {"*": 0.45},             # 18.6k: the cushions' subdivision and the throw's fringe
    "WallShelf": {"*": 0.6},         # 10.3k: mostly ivy leaves and vines
    "ElectricPanel": {"*": 0.5},     # 16.0k: ~30 wires as round tubes
    "DeskLamp": {"DeskLamp_Mesh": 0.45},   # 15.7k: the arm's springs; the bulb is left alone
}

def _tris(objs):
    return sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o in objs if o.type == 'MESH')

def decimate_fbx(prop, ratios):
    path = os.path.join(FBX_OUT if 'FBX_OUT' in globals() else os.path.join(PROJECT, "Assets", "Generated", "Imported"), prop + ".fbx")
    clear_scene()
    bpy.ops.import_scene.fbx(filepath=path, bake_space_transform=True, use_custom_normals=True)
    objs = list(bpy.context.scene.objects)
    meshes = [o for o in objs if o.type == 'MESH']
    before = _tris(meshes)
    for o in meshes:
        r = ratios.get(o.name, ratios.get("*"))
        if not r or r >= 1: continue
        m = o.modifiers.new("Decimate", 'DECIMATE'); m.decimate_type = 'COLLAPSE'; m.ratio = r
    apply_modifiers(meshes)
    after = _tris(meshes)
    with bpy.context.temp_override(**ctx(), selected_objects=objs, active_object=objs[0]):
        bpy.ops.object.select_all(action='DESELECT')
        for o in objs: o.select_set(True)
        bpy.ops.export_scene.fbx(filepath=path, use_selection=True, apply_scale_options='FBX_SCALE_UNITS',
                                 bake_space_transform=True, object_types={'EMPTY', 'MESH'}, use_mesh_modifiers=True,
                                 mesh_smooth_type='FACE', path_mode='STRIP', embed_textures=False, add_leaf_bones=False)
    return f"{prop}: {before} -> {after} tris"

def decimate_all():
    return "; ".join(decimate_fbx(p, r) for p, r in DECIMATE.items())
