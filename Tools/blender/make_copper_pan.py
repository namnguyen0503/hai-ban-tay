"""Blender 4.2: chảo đồng lớn của bếp tàu (QA-0146) -- <out_fbx> [<preview_png>].

Chảo đồng kiểu Pháp: thân đồng dày, thành cong loe đều từ đáy lên, vành cuộn tròn, lòng tráng thiếc, hai quai sắt rèn
tán đinh vào tấm đệm. Số đo khớp PanStation (hệ chảo, gốc ở sàn, Unity Y lên):
  đáy trong phẳng y = 0,052, bán kính 0,295; vành trong 0,375, vành ngoài 0,395, đỉnh vành 0,164.
Ba object: Body (đồng ngoài + vành), Inside (lòng thiếc), Handles (quai + tấm đệm + đinh tán). Vật liệu gán ở Unity.
"""
import math
import sys
from pathlib import Path

import bmesh
import bpy
from mathutils import Vector

BOTTOM_Y, BOTTOM_R = 0.052, 0.295
RIM_IN, RIM_OUT, RIM_Y = 0.375, 0.395, 0.164
SEG = 128
WALL = 0.004


def inner_r(s):
    """Thành trong: s 0 (chân, đáy) .. 1 (vành). Chân lượn tròn rồi thành dựng loe dần (luôn ở ngoài đường thẳng nối đáy–vành)."""
    return BOTTOM_R + (RIM_IN - BOTTOM_R) * (1 - (1 - s) ** 2.4)


def wall_profile(n=28):
    pts = []
    for i in range(n + 1):
        s = i / n
        # Dồn điểm về chân thành, chỗ cong gắt.
        s = s ** 1.6
        pts.append((inner_r(s), BOTTOM_Y + (RIM_Y - 0.009 - BOTTOM_Y) * s))
    return pts


def lathe(name, profile, cap_center=None):
    """Quay một dãy (r, y) quanh trục đứng. Blender: z lên; Unity sẽ là y lên (export axis_up=Y)."""
    bm = bmesh.new()
    rings = []
    for r, y in profile:
        ring = []
        for k in range(SEG):
            a = math.tau * k / SEG
            ring.append(bm.verts.new((r * math.cos(a), r * math.sin(a), y)))
        rings.append(ring)
    for i in range(len(rings) - 1):
        for k in range(SEG):
            k2 = (k + 1) % SEG
            bm.faces.new((rings[i][k], rings[i][k2], rings[i + 1][k2], rings[i + 1][k]))
    if cap_center is not None:
        c = bm.verts.new((0, 0, cap_center))
        first = rings[0]
        for k in range(SEG):
            bm.faces.new((c, first[(k + 1) % SEG], first[k]))
    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    for p in mesh.polygons:
        p.use_smooth = True
    return obj


def flip_normals(obj):
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bmesh.ops.reverse_faces(bm, faces=bm.faces)
    bm.to_mesh(obj.data)
    bm.free()


def make_inside():
    # Đáy phẳng (từ tâm ra BOTTOM_R) rồi thành trong lên tới chân vành cuộn. Pháp tuyến hướng vào lòng (lên trên).
    prof = [(0.0001, BOTTOM_Y)]
    for i in range(1, 9):
        prof.append((BOTTOM_R * i / 8, BOTTOM_Y))
    prof += wall_profile()[1:]
    obj = lathe("Inside", prof)
    # Thứ tự đỉnh của lathe() cho pháp tuyến hướng xuống/ra ngoài; lòng chảo phải nhìn lên (Unity cull mặt sau, Cycles thì không).
    flip_normals(obj)
    return obj


def make_body():
    # Mặt ngoài: dưới đáy (gần sàn) → chân lượn → thành ngoài song song thành trong (dày WALL) → chân vành.
    prof = [(0.0001, 0.002)]
    for i in range(1, 7):
        prof.append((0.257 * i / 6, 0.002))
    # Chân lượn: cung một phần tư bán kính 0,04 từ (0,257; 0,002) lên (0,297; 0,042).
    for i in range(1, 9):
        a = math.radians(-90 + 90 * i / 8)
        prof.append((0.257 + 0.04 * math.cos(a), 0.042 + 0.04 * math.sin(a)))
    for r, y in wall_profile()[1:]:
        if y <= 0.046:
            continue
        prof.append((r + WALL, y))
    body = lathe("Body", prof)
    # Vành cuộn: ống tròn bán kính 0,009 quanh (0,386; 0,155), đỉnh 0,164, phủ r 0,377–0,395.
    bm = bmesh.new()
    ring_seg = 16
    tube = []
    for k in range(SEG):
        a = math.tau * k / SEG
        ca, sa = math.cos(a), math.sin(a)
        circle = []
        for j in range(ring_seg):
            b = math.tau * j / ring_seg
            r = 0.386 + 0.009 * math.cos(b)
            z = RIM_Y - 0.009 + 0.009 * math.sin(b)
            circle.append(bm.verts.new((r * ca, r * sa, z)))
        tube.append(circle)
    for k in range(SEG):
        k2 = (k + 1) % SEG
        for j in range(ring_seg):
            j2 = (j + 1) % ring_seg
            bm.faces.new((tube[k][j], tube[k2][j], tube[k2][j2], tube[k][j2]))
    rim = bpy.data.meshes.new("Rim")
    bm.to_mesh(rim)
    bm.free()
    rim_obj = bpy.data.objects.new("Rim", rim)
    bpy.context.collection.objects.link(rim_obj)
    for p in rim.polygons:
        p.use_smooth = True
    join([body, rim_obj])
    return body


def join(objs):
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.join()


def tube_along(name, points, radius, ring=12):
    curve = bpy.data.curves.new(name, "CURVE")
    curve.dimensions = "3D"
    spline = curve.splines.new("POLY")
    spline.points.add(len(points) - 1)
    for p, q in zip(spline.points, points):
        p.co = (q[0], q[1], q[2], 1)
    curve.bevel_depth = radius
    curve.bevel_resolution = ring // 4
    curve.use_fill_caps = True
    obj = bpy.data.objects.new(name, curve)
    bpy.context.collection.objects.link(obj)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.ops.object.convert(target="MESH")
    return bpy.context.view_layer.objects.active


def make_handles():
    parts = []
    z0 = 0.118

    def outer_at(z):
        s = (z - BOTTOM_Y) / (RIM_Y - 0.009 - BOTTOM_Y)
        return inner_r(min(max(s, 0), 1)) + WALL

    # Thành loe ra theo chiều cao: tấm đệm đặt theo bán kính ở mép trên của nó, không thì mép trên xuyên qua lòng thiếc.
    wall = outer_at(z0 + 0.02)
    for sign in (-1, 1):
        # Quai chữ U bằng thanh sắt tròn Ø24 mm, nhô 9 cm, rộng 17 cm, hơi chếch lên.
        pts = []
        for i in range(25):
            t = i / 24
            a = math.pi * t
            y = -0.085 * math.cos(a)
            out = 0.014 + 0.076 * math.sin(a) ** 0.6
            pts.append((sign * (wall + out), y, z0 + 0.016 * math.sin(a)))
        parts.append(tube_along("Loop", pts, 0.012))
        for y in (-0.085, 0.085):
            # Tấm đệm cong theo thành, hai đinh tán.
            bpy.ops.mesh.primitive_cube_add(size=1, location=(sign * (wall + 0.004), y, z0))
            plate = bpy.context.object
            plate.scale = (0.008, 0.05, 0.04)
            parts.append(plate)
            for dz in (-0.01, 0.01):
                bpy.ops.mesh.primitive_uv_sphere_add(segments=12, ring_count=6, radius=0.0075,
                                                     location=(sign * (wall + 0.008), y, z0 + dz))
                rivet = bpy.context.object
                rivet.scale = (0.6, 1, 1)
                parts.append(rivet)
    for o in parts:
        bpy.ops.object.select_all(action="DESELECT")
        o.select_set(True)
        bpy.context.view_layer.objects.active = o
        bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    join(parts)
    h = bpy.context.view_layer.objects.active
    h.name = h.data.name = "Handles"
    for p in h.data.polygons:
        p.use_smooth = True
    return h


def material(name, color, metallic, roughness):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    bsdf = m.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = (*color, 1)
    bsdf.inputs["Metallic"].default_value = metallic
    bsdf.inputs["Roughness"].default_value = roughness
    return m


def preview(path, objs):
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = 32
    scene.render.resolution_x, scene.render.resolution_y = 900, 600
    scene.render.filepath = str(path)
    world = bpy.data.worlds.new("W")
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs["Color"].default_value = (0.35, 0.33, 0.3, 1)
    world.node_tree.nodes["Background"].inputs["Strength"].default_value = 0.9
    scene.world = world
    bpy.ops.mesh.primitive_plane_add(size=6, location=(0, 0, 0))
    floor = bpy.context.object
    floor.data.materials.append(material("Floor", (0.25, 0.12, 0.08), 0, 0.8))
    bpy.ops.object.light_add(type="AREA", location=(0.6, -0.8, 1.4))
    light = bpy.context.object
    light.data.energy = 250
    light.data.size = 1.2
    bpy.ops.object.camera_add(location=(0.0, -1.05, 0.95))
    cam = bpy.context.object
    direction = Vector((0, 0, 0.06)) - cam.location
    cam.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()
    cam.data.lens = 32
    scene.camera = cam
    bpy.ops.render.render(write_still=True)


def main():
    argv = sys.argv[sys.argv.index("--") + 1:]
    out = Path(argv[0])
    out.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    inside, body, handles = make_inside(), make_body(), make_handles()
    inside.data.materials.append(material("Tin", (0.72, 0.72, 0.70), 0.9, 0.35))
    body.data.materials.append(material("Copper", (0.72, 0.36, 0.2), 1.0, 0.38))
    handles.data.materials.append(material("Iron", (0.035, 0.032, 0.03), 0.5, 0.65))
    for o in (inside, body, handles):
        o.data.name = o.name
    tris = sum(len(o.data.polygons) for o in (inside, body, handles))
    lo = Vector((min(v.co.x for o in (body, handles) for v in o.data.vertices), 0, 0))
    print(f"PAN objects={[o.name for o in (inside, body, handles)]} faces={tris} minx={lo.x:.3f}", flush=True)
    bpy.ops.object.select_all(action="DESELECT")
    for o in (inside, body, handles):
        o.select_set(True)
    bpy.context.view_layer.objects.active = body
    bpy.ops.export_scene.fbx(
        filepath=str(out), use_selection=True, object_types={"MESH"}, apply_unit_scale=True,
        bake_space_transform=True, add_leaf_bones=False, axis_forward="-Z", axis_up="Y", use_triangles=True,
        mesh_smooth_type="FACE",
    )
    if len(argv) > 1:
        preview(Path(argv[1]), (inside, body, handles))


if __name__ == "__main__":
    main()
