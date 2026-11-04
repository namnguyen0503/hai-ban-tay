"""Blender 4.2: six russet potatoes, one shared atlas; -- <out_dir>."""
import datetime
import hashlib
import json
import math
import random
import sys
from pathlib import Path

# Import the existing helpers without creating repo __pycache__ files.
sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parent))
import make_potato_sack as sack

import bpy
import numpy as np
from mathutils import Vector


SIZE = sack.SIZE
SEED = 1911
TAU = math.tau
ANGULAR, ROWS = 48, 30
SHAPES = [
    ("oval", (0.104, 0.056, 0.049)),
    ("lumpy", (0.092, 0.065, 0.054)),
    ("kidney", (0.109, 0.058, 0.050)),
    ("small round", (0.063, 0.059, 0.054)),
    ("tapered", (0.086, 0.052, 0.045)),
    ("double lobed", (0.098, 0.062, 0.051)),
]


def atlas_rect(index):
    col, row = index % 3, index // 3
    return (col*SIZE//3 + 12, (col+1)*SIZE//3 - 12,
            row*SIZE//2 + 12, (row+1)*SIZE//2 - 12)


def atlas_uv(index, u, v):
    x0, x1, y0, y1 = atlas_rect(index)
    return ((x0 + u*(x1-x0))/SIZE, (y0 + v*(y1-y0))/SIZE)


def direction(u, v):
    theta, phi = -math.pi/2 + TAU*u, math.pi*v
    return Vector((math.cos(phi), math.sin(phi)*math.cos(theta),
                   math.sin(phi)*math.sin(theta)))


def eyes(index):
    rng = random.Random(SEED + 97*index)
    positions = [(0.47, 0.24), (0.58, 0.51), (0.38, 0.76)]
    positions += [(rng.uniform(0.06, 0.94), rng.uniform(0.12, 0.88)) for _ in range(10)]
    return [(direction(u, v), rng.uniform(0.062, 0.091),
             rng.uniform(0.00028, 0.00048)) for u, v in positions]


def potato_surface(index, u, v, dimples):
    n = direction(u, v)
    x, y, z = n
    phase = index*1.71
    radial = 1 + 0.034*math.sin(9*x + phase)*math.sin(5*y - 3*z)
    radial += 0.026*math.cos(8*z + phase)*math.sin(4*y + x)
    if index == 1:
        for center, strength in [((0.55, 0.7, 0.4), 0.17),
                                 ((-0.5, -0.5, 0.65), 0.15),
                                 ((0.1, -0.9, -0.3), 0.12)]:
            d = (n - Vector(center).normalized()).length_squared
            radial += strength*math.exp(-d/0.16)
    elif index == 2:
        radial *= (0.94 + 0.09*x)*(1-0.12*math.exp(-(x/0.29)**2))
    elif index == 4:
        radial *= 0.85 + 0.16*x
    elif index == 5:
        radial *= 1 - 0.19*math.exp(-(x/0.27)**2)
    radii = Vector(SHAPES[index][1])*0.5
    p = Vector((x*radii.x, y*radii.y, z*radii.z))*radial
    if index == 2:
        p.y += 0.021*(1-x*x)
    else:
        p.y += 0.0018*math.sin(4*x + phase)*(1-x*x)
    p.z += 0.0014*math.cos(5*x + phase)*(1-x*x)
    # Broad shallow dimples in the mesh; pore/eye detail lives in the normal atlas.
    for center, width, depth in dimples:
        d = (n-center).length_squared
        p -= n*(0.75*depth*math.exp(-d/(width*1.5)**2))
    return p


def make_potato(index):
    builder = sack.MeshBuilder()
    dimples = eyes(index)
    rings = [[builder.vertex(potato_surface(index, j/ANGULAR, i/ROWS, dimples))
              for j in range(ANGULAR)] for i in range(1, ROWS)]
    for i in range(len(rings)-1):
        for j in range(ANGULAR):
            k = (j+1) % ANGULAR
            builder.face((rings[i][j], rings[i+1][j], rings[i+1][k], rings[i][k]),
                         tuple(atlas_uv(index, u, v) for u, v in
                               ((j/ANGULAR, (i+1)/ROWS), (j/ANGULAR, (i+2)/ROWS),
                                ((j+1)/ANGULAR, (i+2)/ROWS), ((j+1)/ANGULAR, (i+1)/ROWS))))
    for v, ring, reverse in ((0, rings[0], False), (1, rings[-1], True)):
        pole = builder.vertex(potato_surface(index, 0, v, dimples))
        rv = 1/ROWS if v == 0 else 1-1/ROWS
        for j in range(ANGULAR):
            k = (j+1) % ANGULAR
            indices = (pole, ring[j], ring[k])
            coords = (atlas_uv(index, (j+0.5)/ANGULAR, v),
                      atlas_uv(index, j/ANGULAR, rv), atlas_uv(index, (j+1)/ANGULAR, rv))
            if reverse:
                indices, coords = indices[::-1], coords[::-1]
            builder.face(indices, coords)
    obj = builder.object(f"Potato_{index}")
    obj.data.uv_layers.active.name = "PotatoAtlas"
    xyz = np.array([v.co[:] for v in obj.data.vertices])
    lo, hi = xyz.min(axis=0), xyz.max(axis=0)
    xyz = (xyz-(lo+hi)/2)*np.array(SHAPES[index][1])/(hi-lo)
    for vertex, co in zip(obj.data.vertices, xyz):
        vertex.co = co
    obj.location.x = index*0.15
    obj.data.update()
    sack.validate_mesh(obj)
    return obj


def noise3(x, y, z, seed, frequency):
    # Sparse hashed value noise avoids both repeating sine bands and a 3D grid allocation.
    coords = [(x+0.31*y)*frequency, (y+0.27*z)*frequency, (z+0.23*x)*frequency]
    cells = [np.floor(p).astype(np.int32).astype(np.uint32) for p in coords]
    weights = [p-np.floor(p) for p in coords]
    weights = [w*w*w*(w*(w*6-15)+10) for w in weights]
    value = np.zeros_like(x)
    for a in (0, 1):
        for b in (0, 1):
            for c in (0, 1):
                h = ((cells[0]+a)*np.uint32(73856093) ^ (cells[1]+b)*np.uint32(19349663)
                     ^ (cells[2]+c)*np.uint32(83492791) ^ np.uint32(seed))
                h = (h ^ (h >> 16))*np.uint32(2246822519)
                h = (h ^ (h >> 13))*np.uint32(3266489917)
                h ^= h >> 16
                sample = h.astype(np.float32)/np.float32(4294967295)*2-1
                value += sample*(weights[0] if a else 1-weights[0])*(weights[1] if b else 1-weights[1])*(weights[2] if c else 1-weights[2])
    return value


def bake_skin(out_dir):
    color = np.zeros((SIZE, SIZE, 3), np.float32)
    normal = np.zeros_like(color)
    for index in range(6):
        x0, x1, y0, y1 = atlas_rect(index)
        width, height = x1-x0, y1-y0
        u, v = np.meshgrid((np.arange(width, dtype=np.float32)+0.5)/width,
                           (np.arange(height, dtype=np.float32)+0.5)/height)
        theta, phi = -math.pi/2 + TAU*u, math.pi*v
        x, y, z = np.cos(phi), np.sin(phi)*np.cos(theta), np.sin(phi)*np.sin(theta)
        broad = noise3(x, y, z, SEED+index, 4.1)
        medium = noise3(x, y, z, SEED+index+100, 14)
        grain = noise3(x, y, z, SEED+index+200, 70)
        pores = noise3(x, y, z, SEED+index+300, 160)
        base = np.array([(0.30, 0.215, 0.120), (0.24, 0.165, 0.075),
                         (0.31, 0.230, 0.125), (0.27, 0.185, 0.085),
                         (0.33, 0.225, 0.120), (0.265, 0.177, 0.087)][index], np.float32)
        skin = base[None, None, :]*(1+0.18*broad[..., None]+0.10*medium[..., None])
        skin += (0.007*grain + 0.003*pores)[..., None]
        flecks = np.clip((grain-0.36)*1.5, 0, 0.30)
        skin *= 1-flecks[..., None]
        soil = np.clip((broad-0.24)*0.28, 0, 0.18)*(0.4+0.6*np.maximum(-z, 0))
        skin = skin*(1-soil[..., None]) + np.array([0.075, 0.045, 0.020])*soil[..., None]
        relief = 0.000007*grain + 0.000003*pores + 0.000018*medium
        for center, eye_width, depth in eyes(index):
            d = ((x-center.x)**2+(y-center.y)**2+(z-center.z)**2)/eye_width**2
            pupil = np.exp(-2.6*d)
            ring = np.exp(-((np.sqrt(d)-1.3)/0.40)**2)
            skin = skin*(1-0.66*pupil[..., None]) + np.array([0.043, 0.024, 0.010])*pupil[..., None]*0.66
            skin += 0.009*ring[..., None]
            relief -= depth*np.exp(-d)
            relief += depth*0.22*ring
        rng = random.Random(SEED+700+index)
        for _ in range(3):
            su, sv = rng.uniform(0.15, 0.85), rng.uniform(0.18, 0.82)
            du = (u-su+0.5) % 1-0.5
            scar = np.exp(-(du/0.017)**2-((v-sv)/0.028)**2)
            skin *= 1-0.22*scar[..., None]
            relief += 0.00004*scar
        # Ellipsoid metric keeps pore/eye depth in meters, including near the poles.
        rx, ry, rz = np.array(SHAPES[index][1])/2
        pu_y, pu_z = -TAU*ry*np.sin(phi)*np.sin(theta), TAU*rz*np.sin(phi)*np.cos(theta)
        pv_x = -math.pi*rx*np.sin(phi)
        pv_y, pv_z = math.pi*ry*np.cos(phi)*np.cos(theta), math.pi*rz*np.cos(phi)*np.sin(theta)
        e = np.maximum(pu_y*pu_y+pu_z*pu_z, 1e-10)
        f = pu_y*pv_y+pu_z*pv_z
        g = pv_x*pv_x+pv_y*pv_y+pv_z*pv_z
        dv, du = np.gradient(relief, 1/height, 1/width)
        tangent = np.stack((-du/np.sqrt(e), -(dv-du*f/e)/np.sqrt(np.maximum(g-f*f/e, 1e-9)),
                            np.ones_like(du)), axis=2)
        tangent /= np.linalg.norm(tangent, axis=2, keepdims=True)
        # Twelve-pixel dilation pads island edges for mip filtering.
        xa, xb = (index % 3)*SIZE//3, (index % 3+1)*SIZE//3
        ya, yb = (index // 3)*SIZE//2, (index // 3+1)*SIZE//2
        padding = ((y0-ya, yb-y1), (x0-xa, xb-x1), (0, 0))
        color[ya:yb, xa:xb] = np.pad(skin, padding, mode="edge")
        normal[ya:yb, xa:xb] = np.pad(tangent*0.5+0.5, padding, mode="edge")
    return (sack.save_texture("Potato_Color.png", color, out_dir),
            sack.save_texture("Potato_normal.png", normal, out_dir, True))


def object_stats(obj):
    xyz = np.array([obj.matrix_world@v.co for v in obj.data.vertices])
    lo, hi = xyz.min(axis=0), xyz.max(axis=0)
    local = np.array([v.co[:] for v in obj.data.vertices])
    return {"triangles": sack.triangles(obj), "bbox_blender_xyz_m": (hi-lo).tolist(),
            "bbox_min_m": lo.tolist(), "bbox_max_m": hi.tolist(),
            "origin_m": list(obj.matrix_world.translation),
            "local_bbox_center_m": ((local.min(axis=0)+local.max(axis=0))/2).tolist()}


def export_and_check(objects, out_dir, basename):
    """Same deterministic FBX flags as make_potato_sack, with per-object checks."""
    from io_scene_fbx import export_fbx_bin, fbx_utils, parse_fbx

    bpy.context.view_layer.update()
    expected = {obj.name: object_stats(obj) for obj in objects}
    textures = {Path(node.image.filepath).name for obj in objects for mat in obj.data.materials
                for node in mat.node_tree.nodes if node.type == "TEX_IMAGE" and node.image}
    bpy.ops.object.select_all(action="DESELECT")
    for obj in objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    path = out_dir/f"{basename}.fbx"
    header = export_fbx_bin.fbx_header_elements
    export_fbx_bin.fbx_header_elements = lambda root, data, time=None: header(root, data, datetime.datetime(1911, 1, 1))
    fbx_utils.hash = lambda key: int.from_bytes(hashlib.sha256(str(key).encode()).digest()[:8], "little") & (2**63-1)
    try:
        bpy.ops.export_scene.fbx(
            filepath=str(path), use_selection=True, object_types={"MESH"},
            path_mode="RELATIVE", embed_textures=False, apply_unit_scale=True,
            bake_space_transform=True, add_leaf_bones=False,
            axis_forward="-Z", axis_up="Y", use_triangles=True,
        )
    finally:
        export_fbx_bin.fbx_header_elements = header
        del fbx_utils.hash
    tree, _ = parse_fbx.parse(str(path))
    references = []

    def paths(element):
        if element.id == b"RelativeFilename":
            references.append(element.props[0].decode())
        for child in element.elems:
            paths(child)

    paths(tree)
    assert set(references) == textures and all(not Path(p).is_absolute() and (out_dir/p).is_file()
                                              for p in references), f"FBX texture paths: {references}"
    original = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=str(path))
    imported = list(set(bpy.data.objects)-original)
    assert len(imported) == len(objects) and all(o.type == "MESH" for o in imported), "FBX object count"
    assert {o.name.split('.')[0] for o in imported} == set(expected), "FBX object names"
    for obj in imported:
        stats, wanted = object_stats(obj), expected[obj.name.split('.')[0]]
        assert stats["triangles"] == wanted["triangles"], f"FBX triangles: {obj.name}"
        for key in ("bbox_blender_xyz_m", "bbox_min_m", "bbox_max_m", "origin_m"):
            assert np.allclose(stats[key], wanted[key], atol=1e-6), f"FBX {key}: {obj.name}"
        images = {Path(n.image.filepath).name for mat in obj.data.materials
                  for n in mat.node_tree.nodes if n.type == "TEX_IMAGE" and n.image}
        assert images == textures, f"FBX material: {obj.name}: {images}"
        sack.validate_mesh(obj)
    for obj in imported:
        bpy.data.objects.remove(obj, do_unlink=True)
    return expected


def render_preview(out_dir, scale, camera_location, target, floor_z):
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.render.threads_mode = "FIXED"
    scene.render.threads = 1
    scene.cycles.device = "CPU"
    scene.cycles.samples = 48
    scene.cycles.use_adaptive_sampling = False
    scene.cycles.use_denoising = True
    scene.cycles.seed = SEED
    scene.render.resolution_x = scene.render.resolution_y = 1024
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGB"
    scene.view_settings.view_transform = "AgX"
    scene.view_settings.look = "AgX - Medium High Contrast"
    scene.world = bpy.data.worlds.new("Preview_World")
    scene.world.use_nodes = True
    scene.world.node_tree.nodes["Background"].inputs["Color"].default_value = (0.24, 0.24, 0.24, 1)
    scene.world.node_tree.nodes["Background"].inputs["Strength"].default_value = 0.55
    bpy.ops.mesh.primitive_plane_add(size=200, location=(0, 0, floor_z))
    ground = bpy.context.object
    ground.name = "Preview_Ground"
    gray = bpy.data.materials.new("Preview_Gray")
    gray.use_nodes = True
    shader = gray.node_tree.nodes.get("Principled BSDF")
    shader.inputs["Base Color"].default_value = (0.23, 0.23, 0.23, 1)
    shader.inputs["Roughness"].default_value = 1
    ground.data.materials.append(gray)
    for name, position, energy, size in [
        ("Key", (-0.45, -0.60, 0.80), 42, 0.45),
        ("Fill", (0.55, 0.05, 0.50), 18, 0.60),
    ]:
        light = bpy.data.lights.new(name, "AREA")
        light.energy, light.shape, light.size = energy, "DISK", size
        obj = bpy.data.objects.new(name, light)
        scene.collection.objects.link(obj)
        obj.location = position
        sack.aim(obj, target)
    camera = bpy.data.cameras.new("Preview_Camera")
    camera.type, camera.ortho_scale = "ORTHO", scale
    obj = bpy.data.objects.new("Preview_Camera", camera)
    scene.collection.objects.link(obj)
    obj.location = camera_location
    sack.aim(obj, target)
    scene.camera = obj
    scene.render.filepath = str(out_dir/"preview.png")
    print("RENDER preview.png", flush=True)
    bpy.ops.render.render(write_still=True)
    # Blender writes wall-clock/render-time PNG metadata even with stamp drawing off.
    # Copy chunks verbatim, excluding metadata; compressed pixel data stays untouched.
    path = out_dir/"preview.png"
    data, chunks, offset = path.read_bytes(), [], 8
    assert data[:8] == b"\x89PNG\r\n\x1a\n", "Preview PNG signature"
    while offset < len(data):
        size = int.from_bytes(data[offset:offset+4], "big")
        kind = data[offset+4:offset+8]
        if kind not in {b"tEXt", b"zTXt", b"iTXt", b"eXIf", b"tIME"}:
            chunks.append(data[offset:offset+size+12])
        offset += size+12
    assert offset == len(data) and chunks[-1][4:8] == b"IEND", "Preview PNG chunks"
    assert any(chunk[4:8] == b"IDAT" for chunk in chunks), "Preview PNG pixel data"
    path.write_bytes(data[:8]+b"".join(chunks))


def main():
    args = sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else []
    if len(args) != 1:
        raise SystemExit("Usage: blender -b --factory-startup -P make_potatoes.py -- <out_dir>")
    out_dir = Path(args[0]).resolve()
    out_dir.mkdir(parents=True, exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.scene.unit_settings.system = "METRIC"
    bpy.context.scene.unit_settings.scale_length = 1
    objects = [make_potato(i) for i in range(6)]
    bpy.context.view_layer.update()
    for index, obj in enumerate(objects):
        stats = object_stats(obj)
        assert 1500 <= stats["triangles"] <= 3000, stats
        assert np.allclose(stats["bbox_blender_xyz_m"], SHAPES[index][1], atol=1e-6), stats
        assert np.allclose(stats["local_bbox_center_m"], 0, atol=1e-7), stats
        assert np.allclose(stats["origin_m"], [index*0.15, 0, 0], atol=1e-6), stats
    print("MESH", json.dumps({obj.name: sack.triangles(obj) for obj in objects}), flush=True)
    color, normal = bake_skin(out_dir)
    material = sack.material(color, normal)
    material.name = "Potato_Russet"
    for node in material.node_tree.nodes:
        if node.type == "NORMAL_MAP":
            node.uv_map = "PotatoAtlas"
    shader = material.node_tree.nodes.get("Principled BSDF")
    shader.inputs["Roughness"].default_value = 0.86
    for obj in objects:
        obj.data.materials.append(material)
    stats = export_and_check(objects, out_dir, "Potato")
    # The FBX stays in a 0.15 m X row; the preview stages a larger 3-by-2 view.
    for index, obj in enumerate(objects):
        obj.location = ((index % 3-1)*0.15, (index // 3-0.5)*0.17,
                        SHAPES[index][1][2]/2)
        obj.rotation_euler.z = math.radians((-8, 12, -10, 6, -13, 9)[index])
    render_preview(out_dir, 0.51, (0.12, -0.48, 0.75), (0, 0, 0.016), -0.0002)
    outputs = ["Potato.fbx", "Potato_Color.png", "Potato_normal.png", "preview.png"]
    assert all((out_dir/name).is_file() for name in outputs)
    print("POTATO_REPORT "+json.dumps({
        "outputs": [str(out_dir/name) for name in outputs], "objects": stats,
        "texture_size": [SIZE, SIZE], "seed": SEED,
        "checks": "closed meshes, positive volume, UV bounds, individual pivots, FBX round-trip",
        "caveats": "URP import not run. Use normal-map import and calculated tangents. Preview stages 3x2; FBX remains an X row. Render rounding may vary across CPU architectures.",
    }), flush=True)


if __name__ == "__main__":
    main()
