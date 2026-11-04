"""Cây me, sao, dầu nhiều cụm; LOD0/1 và ảnh billboard RGBA. Không ghi Staging.

blender -b --factory-startup \
    -P Tools/blender/make_world10_trees.py
"""
from pathlib import Path
import math
import random
import sys
from types import SimpleNamespace
sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parent))
from make_env_common import Mesh, export, bpos
import bpy
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Game/Assets/_Project/Art/Generated/World10/Trees'
OUT.mkdir(parents=True, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
materials = {}
def material(name):
    if name not in materials:
        m = bpy.data.materials.new(name)
        m.diffuse_color = (*{'Bark': (.22, .14, .065), 'Leaves': (.12, .25, .055)}[name], 1)
        m.use_nodes = True
        m.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = m.diffuse_color
        m.node_tree.nodes['Principled BSDF'].inputs['Roughness'].default_value = .9
        materials[name] = m
    return materials[name]
root = bpy.data.objects.new('World10Trees', None)
bpy.context.scene.collection.objects.link(root)
kit = SimpleNamespace(name='World10Trees', root=root, collection=bpy.context.scene.collection, objects=[], material=material)

for kind, tall, spread, seed in [('Me', 8.7, 4.0, 1911), ('Sao', 11.0, 3.2, 1912), ('Dau', 10.0, 3.8, 1913)]:
    for lod in (0, 1):
        rng = random.Random(seed)
        m = Mesh(f'{kind}_LOD{lod}')
        m.beam((0, 0, 0), (.16, tall * .69, -.12), .30, 'Bark', 10 if lod == 0 else 6, r2=.07)
        # Tầng cành lệch nhau, tán đứng ở đỉnh và cụm nhỏ ở mép; tránh vòng tán phẳng như ô.
        for i in range(10 if lod == 0 else 7):
            a = i * 2.39996 + rng.uniform(-.25, .25)
            radius = spread * rng.uniform(.42, .90)
            y = tall * (.52 + .033 * i) + rng.uniform(-.25, .25)
            end = (math.cos(a) * radius, y, math.sin(a) * radius * .83)
            m.beam((.12, tall * .38 + i * .11, 0), end, .095, 'Bark', 7 if lod == 0 else 5, r2=.025)
            count = 5 if lod == 0 else 1
            for j in range(count):
                centre = (end[0] + rng.uniform(-.65, .65), end[1] + rng.uniform(.05, .9), end[2] + rng.uniform(-.6, .6))
                start = len(m.v)
                size = (rng.uniform(1.7, 2.4), rng.uniform(1.6, 2.3), rng.uniform(1.6, 2.2)) if lod == 0 else (3.0, 2.8, 2.8)
                m.ellipsoid(centre, size, 'Leaves', 10 if lod == 0 else 8, 6 if lod == 0 else 4)
                # Mép tán không tròn hoàn hảo; nhiễu cố định, không đổi theo lần export.
                for v in range(start, len(m.v)):
                    p = m.v[v]
                    m.v[v] = tuple(centre[d] + (p[d] - centre[d]) * rng.uniform(.86, 1.15) for d in range(3))
        m.ellipsoid((.12, tall - 1, 0), (2.6, 2.8, 2.5), 'Leaves', 12 if lod == 0 else 8, 6 if lod == 0 else 4)
        m.object(kit)
export(kit, OUT)

scene = bpy.context.scene
scene.render.engine = 'BLENDER_EEVEE_NEXT'
scene.render.resolution_x = 512
scene.render.resolution_y = 512
scene.render.resolution_percentage = 100
scene.render.film_transparent = True
scene.render.image_settings.file_format = 'PNG'
scene.render.image_settings.color_mode = 'RGBA'
scene.world = bpy.data.worlds.new('BillboardWorld')
scene.world.use_nodes = True
scene.world.node_tree.nodes['Background'].inputs[0].default_value = (.55, .65, .75, 1)
scene.world.node_tree.nodes['Background'].inputs[1].default_value = .8
sun_data = bpy.data.lights.new('BillboardSun', 'SUN')
sun_data.energy = 2
sun = bpy.data.objects.new('BillboardSun', sun_data)
scene.collection.objects.link(sun)
sun.rotation_euler = (.4, -.5, -.4)
cam_data = bpy.data.cameras.new('BillboardCamera')
cam = bpy.data.objects.new('BillboardCamera', cam_data)
scene.collection.objects.link(cam)
scene.camera = cam
cam_data.type = 'ORTHO'
# Khung thống nhất 16 × 16 m, gốc tại chân cây: khớp shader billboard ở Unity.
cam_data.ortho_scale = 16
cam.location = (0, -30, 8)
cam.rotation_euler = (Vector((0, 0, 8)) - cam.location).to_track_quat('-Z', 'Y').to_euler()
scene.view_settings.view_transform = 'Standard'
for kind in ('Me', 'Sao', 'Dau'):
    for obj in kit.objects:
        obj.hide_render = obj.name != kind + '_LOD0'
    scene.render.filepath = str(OUT / (kind + '_Billboard.png'))
    bpy.ops.render.render(write_still=True)
print('World10Trees: 3 loài × LOD0/1 + billboard, hoàn tất; Staging không đổi.', flush=True)
