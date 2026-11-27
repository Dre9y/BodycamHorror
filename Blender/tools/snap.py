"""snap(target, rx, rz, dist, path): a quick EEVEE still of the open scene from an orbit camera, for checking props
without the viewport (exec'd over the bridge)."""
import bpy, math
from mathutils import Vector, Euler

def snap(target, rx, rz, dist, path, res=(900, 520), lens=40):
    sc = bpy.context.scene
    cam = bpy.data.objects.get("_SnapCam")
    if not cam:
        cam = bpy.data.objects.new("_SnapCam", bpy.data.cameras.new("_SnapCam")); sc.collection.objects.link(cam)
    sun = bpy.data.objects.get("_SnapSun")
    if not sun:
        sun = bpy.data.objects.new("_SnapSun", bpy.data.lights.new("_SnapSun", 'SUN')); sc.collection.objects.link(sun)
        sun.data.energy = 3.0; sun.rotation_euler = (math.radians(50), 0, math.radians(30))
    if not sc.world: sc.world = bpy.data.worlds.new("World")
    sc.world.use_nodes = True
    bg = sc.world.node_tree.nodes.get("Background")
    if bg: bg.inputs[0].default_value = (0.25, 0.25, 0.27, 1); bg.inputs[1].default_value = 0.8
    cam.data.lens = lens
    rot = Euler((math.radians(rx), 0, math.radians(rz)), 'XYZ')
    cam.rotation_euler = rot
    cam.location = Vector(target) + rot.to_matrix() @ Vector((0, 0, dist))
    sc.camera = cam
    prev = sc.render.engine
    sc.render.engine = 'BLENDER_EEVEE'
    sc.render.resolution_x, sc.render.resolution_y = res; sc.render.resolution_percentage = 100
    sc.render.filepath = path; sc.render.image_settings.file_format = 'PNG'
    bpy.ops.render.render(write_still=True)
    sc.render.engine = prev
    for o in (cam, sun): o.hide_render = False
    return path

def unsnap():
    for n in ("_SnapCam", "_SnapSun"):
        o = bpy.data.objects.get(n)
        if o: bpy.data.objects.remove(o, do_unlink=True)
