"""Deterministic 1911 environment kit. Authoring inputs are Unity world metres.

Run the three make_*.py wrappers in Blender. --map and --clean-png use host
Python. Existing game assets are read, never edited.
"""
import argparse
import datetime
import hashlib
import json
import math
from pathlib import Path
import random
import shutil
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Staging/art_env'
SURFACES = ROOT / 'Game/Assets/_Project/Art/Surfaces'
TILES = {'QuayStone': 3, 'NhaRongWall': 4, 'NhaRongRoof': 2,
         'Cobble': 2, 'Shutter': 1, 'Awning': 1.5, 'Basilica': 4,
         'Wood': 1, 'Iron': 1, 'Hill': 12, 'Shore': 6, 'Burlap': .8,
         'Marble': 1.5, 'Door': 1, 'Wall': 4, 'Brass': .6, 'DeckPlanks': 2}
FLAT = {'Foliage': (.17, .28, .075), 'FoliageLight': (.27, .36, .11),
        'WindowDark': (.055, .077, .075), 'CanvasRed': (.30, .065, .035),
        'CanvasIvory': (.73, .68, .48), 'WaterRiver': (.12, .21, .15),
        'WaterHarbor': (.065, .17, .21), 'LampGlow': (1, .69, .30),
        'StatueGold': (.67, .44, .09), 'CafeGlass': (.15,.10,.055),
        'HorseCoat': (.23,.12,.055), 'Collision': (.85, .12, .12)}


def write_json(path, data):
    path.write_text(json.dumps(data, ensure_ascii=False, indent=2) + '\n')


def draw_map(directory):
    """Overlay actual Blender orthographic render using its projected pixels."""
    from PIL import Image, ImageDraw, ImageFont
    d = Path(directory)
    layout = json.loads((d / 'layout.json').read_text())
    proj = json.loads((d / 'map_projection.json').read_text())
    base = Image.open(d / 'preview_top_raw.png').convert('RGBA')
    w, h = base.size
    footer = max(110 + len(proj['anchors'])*22, 440)
    canvas_height=max(h+76,footer+len(layout['crowd']['paths'])*22+85)
    img = Image.new('RGBA', (w + 530, canvas_height), '#172126')
    img.paste(base, (0, 76))
    overlay = Image.new('RGBA', img.size)
    pen = ImageDraw.Draw(overlay)
    fontpath = '/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf'
    font = ImageFont.truetype(fontpath, 14)
    small = ImageFont.truetype(fontpath, 12)
    title = ImageFont.truetype(fontpath, 22)
    pen.text((20, 14), layout['scene'] + ' | Unity world metres, Y up', font=title, fill='white')
    pen.text((20, 46), 'Cyan: crowd paths | red: physical blockers | gold: anchors | green: walk surfaces', font=font, fill='#c9d2d6')
    def pts(seq):
        return [(int(p[0]), int(p[1]) + 76) for p in seq]
    for p in proj['walks']:
        q = pts(p)
        pen.polygon(q, fill=(70, 230, 120, 22))
        pen.line(q + [q[0]], fill='#65eb99', width=2)
    for p in proj['blockers']:
        q = pts(p)
        pen.polygon(q, fill=(255, 70, 70, 58))
        pen.line(q + [q[0]], fill=(255, 100, 80, 190), width=1)
    for p in proj['paths']:
        q = pts(p['points'])
        if p['loop']:
            q.append(q[0])
        pen.line(q, fill='#36ddec', width=3)
        for a in q:
            pen.ellipse((a[0]-3, a[1]-3, a[0]+3, a[1]+3), fill='#36ddec')
    for i, (name, point) in enumerate(proj['anchors'].items(), 1):
        x, y = pts([point])[0]
        if 0 <= x < w and 76 <= y < h + 76:
            pen.ellipse((x-6, y-6, x+6, y+6), fill='#ffc75c', outline='#181b1b')
            pen.text((x+8, y-11), str(i), fill='#ffda83', stroke_fill='#171b1d', stroke_width=2, font=font)
        uy = 104 + (i-1)*22
        pen.text((w+16, uy), f'{i:02d} {name}', font=small, fill='#ffda83')
    for i, p in enumerate(layout['crowd']['paths']):
        pen.text((w+16, footer+i*22), f"path: {p['name']} ({p.get('count', 0)} NPC)", font=small, fill='#73e7ee')
    pen.text((w+16, canvas_height-34), '+X right; +Z toward river / top of image', font=small, fill='white')
    img = Image.alpha_composite(img, overlay).convert('RGB')
    img.save(d / 'preview_top.png')


if __name__ == '__main__' and '--map' in sys.argv:
    draw_map(sys.argv[sys.argv.index('--map')+1])
    sys.exit(0)
if __name__ == '__main__' and '--clean-png' in sys.argv:
    from PIL import Image
    path = Path(sys.argv[sys.argv.index('--clean-png')+1])
    with Image.open(path) as source:
        pixels = source.convert('RGB')
        # Keep rendered pixels; remove Blender's wall-clock/render-time chunks.
        Image.frombytes('RGB', pixels.size, pixels.tobytes()).save(path)
    sys.exit(0)

import bpy
from mathutils import Vector, Matrix
from bpy_extras.object_utils import world_to_camera_view


def bpos(p):
    return Vector((p[0], p[2], p[1]))


def upos(p):
    return (p[0], p[2], p[1])


def rotate(p, yaw):
    a = math.radians(yaw)
    return (p[0]*math.cos(a)+p[2]*math.sin(a), p[1],
            -p[0]*math.sin(a)+p[2]*math.cos(a))


class Mesh:
    """Small mesh builder; positive-volume primitives, metric face UVs."""
    def __init__(self, name):
        self.name = name
        self.v, self.f, self.m, self.slots = [], [], [], []
        self.data = None

    def add(self, vertices, faces, material, orient=True):
        offset = len(self.v)
        self.v.extend(vertices)
        if material not in self.slots:
            self.slots.append(material)
        mi = self.slots.index(material)
        # Each primitive is convex. Orient in U first, then compensate reflection.
        middle=sum((Vector(p) for p in vertices),Vector())/len(vertices)
        for face in faces:
            a,b,c=[Vector(vertices[i]) for i in face[:3]]
            normal=(b-a).cross(c-a)
            face_middle=sum((Vector(vertices[i]) for i in face),Vector())/len(face)
            if orient and normal.dot(face_middle-middle)<-1e-9:
                face=tuple(reversed(face))
            self.f.append(tuple(offset+i for i in reversed(face)))
        self.m.extend([mi]*len(faces))

    def box(self, center, size, mat, yaw=0):
        pts = [(-1,-1,-1), (1,-1,-1), (1,1,-1), (-1,1,-1),
               (-1,-1,1), (1,-1,1), (1,1,1), (-1,1,1)]
        verts = []
        for p in pts:
            q = rotate(tuple(p[i]*size[i]/2 for i in range(3)), yaw)
            verts.append(tuple(q[i]+center[i] for i in range(3)))
        self.add(verts, [(0,3,2,1), (4,5,6,7), (0,1,5,4),
                         (3,7,6,2), (0,4,7,3), (1,2,6,5)], mat)

    def beam(self, a, b, radius, mat, sides=8, r2=None):
        av, bv = Vector(a), Vector(b)
        direction = (bv-av).normalized()
        u = direction.cross(Vector((0,1,0)))
        if u.length < .01:
            u = direction.cross(Vector((1,0,0)))
        u.normalize()
        v = direction.cross(u).normalized()
        vertices = []
        for p, r in ((av,radius),(bv,radius if r2 is None else r2)):
            for i in range(sides):
                t = i*math.tau/sides
                vertices.append(tuple(p+r*(math.cos(t)*u+math.sin(t)*v)))
        faces = [tuple(reversed(range(sides))), tuple(range(sides,2*sides))]
        faces += [(i,(i+1)%sides,(i+1)%sides+sides,i+sides) for i in range(sides)]
        self.add(vertices, faces, mat)

    def tube(self, points, radius, mat, sides=8):
        for a, b in zip(points, points[1:]):
            self.beam(a,b,radius,mat,sides)

    def chain_link(self, center, horizontal=False):
        # Elliptical torus: alternate XY/XZ planes to interlock actual links.
        u=Vector((1,0,0)); v=Vector((0,0,1) if horizontal else (0,1,0))
        normal=u.cross(v); vertices=[]
        for i in range(12):
            theta=math.tau*i/12
            radial=(u*math.cos(theta)+v*math.sin(theta)).normalized()
            middle=Vector(center)+u*(.085*math.cos(theta))+v*(.040*math.sin(theta))
            for j in range(6):
                phi=math.tau*j/6
                vertices.append(tuple(middle+.012*(radial*math.cos(phi)+normal*math.sin(phi))))
        faces=[(i*6+j,((i+1)%12)*6+j,((i+1)%12)*6+(j+1)%6,i*6+(j+1)%6)
               for i in range(12) for j in range(6)]
        self.add(vertices,faces,'Iron',orient=False)

    def ellipsoid(self, center, size, mat, seg=12, rings=6):
        # Pole fans avoid zero-area polygons from repeated sphere poles.
        vertices = [(center[0], center[1]-size[1]/2, center[2])]
        for j in range(1,rings):
            lat = -math.pi/2 + math.pi*j/rings
            for i in range(seg):
                a = math.tau*i/seg
                vertices.append((center[0]+size[0]/2*math.cos(lat)*math.cos(a),
                                 center[1]+size[1]/2*math.sin(lat),
                                 center[2]+size[2]/2*math.cos(lat)*math.sin(a)))
        top = len(vertices)
        vertices.append((center[0],center[1]+size[1]/2,center[2]))
        faces = [(0,1+(i+1)%seg,1+i) for i in range(seg)]
        for j in range(rings-2):
            for i in range(seg):
                a = 1+j*seg+i
                b = 1+j*seg+(i+1)%seg
                faces.append((a,b,b+seg,a+seg))
        faces += [(top,1+(rings-2)*seg+i,1+(rings-2)*seg+(i+1)%seg) for i in range(seg)]
        self.add(vertices, faces, mat)

    def hip_roof(self, center, width, depth, eave, rise, mat='NhaRongRoof'):
        x,y,z = center
        v = [(x-width/2,y+eave,z-depth/2), (x+width/2,y+eave,z-depth/2),
             (x+width/2,y+eave,z+depth/2), (x-width/2,y+eave,z+depth/2),
             (x-width/2+depth*.42,y+eave+rise,z),
             (x+width/2-depth*.42,y+eave+rise,z)]
        self.add(v, [(0,4,5,1),(1,5,2),(2,5,4,3),(3,4,0),(0,1,2,3)], mat)

    def arch(self, x, base, z, width, spring, thickness=.32, mat='Basilica', depth=.35, n=16):
        r = width/2
        for i in range(n):
            a,b = math.pi*i/n, math.pi*(i+1)/n
            vertices = []
            for zz in (z-depth/2,z+depth/2):
                vertices += [(x+r*math.cos(a),base+spring+r*math.sin(a),zz),
                             (x+r*math.cos(b),base+spring+r*math.sin(b),zz),
                             (x+(r+thickness)*math.cos(b),base+spring+(r+thickness)*math.sin(b),zz),
                             (x+(r+thickness)*math.cos(a),base+spring+(r+thickness)*math.sin(a),zz)]
            self.add(vertices, [(0,3,2,1),(4,5,6,7),(0,1,5,4),(3,7,6,2),(0,4,7,3),(1,2,6,5)],mat)

    def object(self, kit, name=None, pos=(0,0,0), yaw=0, collision=False):
        if self.data is None:
            mesh = bpy.data.meshes.new(self.name)
            mesh.from_pydata([bpos(v) for v in self.v], [], self.f)
            mesh.update()
            for slot in self.slots:
                mesh.materials.append(kit.material(slot))
            uv = mesh.uv_layers.new(name='UV_Metres')
            for p, mi in zip(mesh.polygons, self.m):
                p.material_index = mi
                vs = [mesh.vertices[i].co for i in p.vertices]
                tangent = (vs[1]-vs[0]).normalized()
                bitangent = p.normal.cross(tangent).normalized()
                p.use_smooth = self.slots[mi] in ('Foliage','FoliageLight')
                for li, co in zip(p.loop_indices, vs):
                    uv.data[li].uv = ((co-vs[0]).dot(tangent),(co-vs[0]).dot(bitangent))
            self.data = mesh
        obj = bpy.data.objects.new(name or self.name,self.data)
        kit.collection.objects.link(obj)
        obj.parent = kit.root
        obj.location = bpos(pos)
        obj.rotation_euler[2] = math.radians(-yaw)
        obj.hide_render = collision
        obj.display_type = 'WIRE' if collision else 'TEXTURED'
        obj['unity_world_metres'] = True
        kit.objects.append(obj)
        return obj


class Kit:
    def __init__(self, name, scene, refinement):
        bpy.ops.wm.read_factory_settings(use_empty=True)
        self.name, self.scene, self.refinement = name, scene, refinement
        self.rng = random.Random(1911)
        self.collection = bpy.context.scene.collection
        self.root = bpy.data.objects.new(name,None)
        self.collection.objects.link(self.root)
        self.root['authoring_frame'] = 'Blender=(Unity.x,Unity.z,Unity.y); Unity imported root yaw=180'
        self.objects, self.cache, self.mats = [], {}, {}
        self.layout = {'schema_version':1, 'scene':scene, 'units':'metres', 'frame':'unity_world',
                       'fbx':name+'.fbx', 'fbx_transform':{'position':[0,0,0], 'rotation_euler':[0,180,0], 'scale':1},
                       'player_spawn':{'pos':[0,0,0], 'yaw':180}, 'anchors':{}, 'anchor_roles':{},
                       'walk_bounds':[], 'crowd':{'paths':[], 'spots':[], 'capacity':0},
                       'blockers':[], 'collision_surfaces':[], 'notes':'',
                       'beat_routes':[], 'measurements':[]}

    def material(self, name):
        if name in self.mats:
            return self.mats[name]
        m = bpy.data.materials.new(name)
        m.use_nodes = True
        bs = m.node_tree.nodes.get('Principled BSDF')
        m.diffuse_color = (*FLAT.get(name,(.65,.56,.40)),1)
        bs.inputs['Base Color'].default_value = m.diffuse_color
        bs.inputs['Roughness'].default_value = .78
        if name in ('Iron','Brass','StatueGold'):
            bs.inputs['Metallic'].default_value = .65
            bs.inputs['Roughness'].default_value = .48
        if name in ('WaterRiver','WaterHarbor'):
            bs.inputs['Roughness'].default_value = .25
            bs.inputs['Metallic'].default_value = .12
            noise = m.node_tree.nodes.new('ShaderNodeTexNoise')
            noise.inputs['Scale'].default_value = 1.7
            bump = m.node_tree.nodes.new('ShaderNodeBump')
            bump.inputs['Strength'].default_value = .17
            bump.inputs['Distance'].default_value = .12
            m.node_tree.links.new(noise.outputs['Fac'],bump.inputs['Height'])
            m.node_tree.links.new(bump.outputs['Normal'],bs.inputs['Normal'])
        if name == 'LampGlow':
            bs.inputs['Emission Color'].default_value = (1,.55,.20,1)
            bs.inputs['Emission Strength'].default_value = 4 if self.name=='SaigonStreet' else .15
        if name == 'CafeGlass':
            bs.inputs['Emission Color'].default_value = (.72,.30,.07,1)
            bs.inputs['Emission Strength'].default_value = .3 if self.name=='SaigonStreet' else .04
        if name in TILES:
            source = SURFACES / name
            colors = sorted(source.glob('*_Color_2K.jpg'))
            normals = sorted(source.glob('*_NormalGL_2K.jpg'))
            assert colors and normals, f'Missing PBR maps: {source}'
            dest = OUT / '_textures' / name
            dest.mkdir(parents=True, exist_ok=True)
            for p in colors+normals:
                target = dest / p.name
                if not target.exists():
                    shutil.copyfile(p,target)
            coords = m.node_tree.nodes.new('ShaderNodeTexCoord')
            mapping = m.node_tree.nodes.new('ShaderNodeVectorMath')
            mapping.operation = 'SCALE'
            mapping.inputs[3].default_value = 1/TILES[name]
            m.node_tree.links.new(coords.outputs['UV'],mapping.inputs[0])
            tex = m.node_tree.nodes.new('ShaderNodeTexImage')
            tex.image = bpy.data.images.load(str(dest/colors[0].name),check_existing=True)
            m.node_tree.links.new(mapping.outputs['Vector'],tex.inputs['Vector'])
            m.node_tree.links.new(tex.outputs['Color'],bs.inputs['Base Color'])
            nt = m.node_tree.nodes.new('ShaderNodeTexImage')
            nt.image = bpy.data.images.load(str(dest/normals[0].name),check_existing=True)
            nt.image.colorspace_settings.name = 'Non-Color'
            normal = m.node_tree.nodes.new('ShaderNodeNormalMap')
            normal.inputs['Strength'].default_value = .65
            m.node_tree.links.new(mapping.outputs['Vector'],nt.inputs['Vector'])
            m.node_tree.links.new(nt.outputs['Color'],normal.inputs['Color'])
            m.node_tree.links.new(normal.outputs['Normal'],bs.inputs['Normal'])
        self.mats[name] = m
        return m

    def box(self, name, center, size, mat, yaw=0, collider=False, floor=False):
        mesh = Mesh(name)
        mesh.box((0,0,0),size,mat)
        obj = mesh.object(self,pos=center,yaw=yaw)
        if collider:
            self.collider(name,center,size,yaw,[obj.name],floor)
        return obj

    def collider(self, name, center, size, yaw=0, visuals=None, floor=False):
        mesh = Mesh('COL_'+name)
        mesh.box((0,0,0),size,'Collision')
        obj = mesh.object(self,pos=center,yaw=yaw,collision=True)
        data = {'name':name, 'mesh_name':obj.name, 'center':list(center), 'size':list(size),
                'yaw':yaw, 'visuals':visuals or []}
        self.layout['collision_surfaces' if floor else 'blockers'].append(data)
        return obj

    def anchor(self, name, pos, role='walk'):
        self.layout['anchors'][name] = list(pos)
        self.layout['anchor_roles'][name] = role

    def walk(self, name, mn, mx):
        self.layout['walk_bounds'].append({'name':name,'min':list(mn),'max':list(mx)})

    def path(self, name, points, loop, count):
        self.layout['crowd']['paths'].append({'name':name,'points':[list(p) for p in points],
                                             'loop':loop,'count':count})

    def spot(self, name, pos, count):
        self.layout['crowd']['spots'].append({'name':name,'pos':list(pos),'count':count})

    def queue(self, name, points, count):
        self.layout['crowd']['spots'].append({'name':name,'points':[list(p) for p in points],'count':count})

    def measure(self, name, size, basis):
        self.layout['measurements'].append({'name':name,'size_m':list(size),'basis':basis})

    def instance(self, kind, name, pos, yaw=0, collider=True):
        if kind not in self.cache:
            self.cache[kind] = prop_mesh(kind)
        obj = self.cache[kind].object(self,name,pos,yaw)
        sizes = {'bollard':(.62,1,.62), 'crate':(1.1,.95,.85), 'barrel':(.8,1.1,.8),
                 'sack':(.62,.92,.45), 'chair':(.55,.95,.55), 'table':(.9,.78,.9),
                 'rickshaw':(1.45,2.2,2.9), 'cart':(2.1,1.8,3.5),
                 'carriage':(2.3,2.7,5.2), 'horse':(.75,2.6,2.9),
                 'bench':(1.9,1,.7), 'lamp':(.4,5.1,.4)}
        if collider and kind in sizes:
            size = sizes[kind]
            self.collider(name,(pos[0],pos[1]+size[1]/2,pos[2]),size,yaw,[obj.name])
        return obj

    def fence(self, name, a, b, height=1.6, iron=False):
        av,bv = Vector(a),Vector(b)
        length = (bv-av).length
        mat = 'Iron' if iron else 'Wood'
        mesh = Mesh(name)
        n = max(1,math.ceil(length/2.6))
        for i in range(n+1):
            p = av.lerp(bv,i/n)
            mesh.box(tuple(p+Vector((0,height/2,0))),(.16,height,.16),mat)
        for y in (.3,height-.12):
            mesh.beam(tuple(av+Vector((0,y,0))),tuple(bv+Vector((0,y,0))),.055,mat)
        if iron:
            for i in range(math.ceil(length/.32)+1):
                p = av.lerp(bv,i/max(1,math.ceil(length/.32)))
                mesh.beam(tuple(p+Vector((0,.18,0))),tuple(p+Vector((0,height,0))),.022,mat,6)
        else:
            for i in range(n):
                p,q = av.lerp(bv,i/n),av.lerp(bv,(i+1)/n)
                mesh.beam(tuple(p+Vector((0,.32,0))),tuple(q+Vector((0,height-.16,0))),.06,mat,6)
        obj = mesh.object(self)
        center = tuple((av+bv)/2+Vector((0,height/2,0)))
        yaw = -math.degrees(math.atan2(b[2]-a[2],b[0]-a[0]))
        self.collider(name,center,(length,height,.19),yaw,[obj.name])

    def tree(self, name, pos, size=1, tropical=True, collider=True):
        mesh = Mesh(name)
        mesh.beam((0,0,0),(.3,5.2,0),.30,'Wood',9,r2=.12)
        rng = self.rng
        for i in range(6 if self.refinement==0 else 11):
            a = math.tau*i/6
            end = (math.cos(a)*2.9,5.2+(i%3)*.7,math.sin(a)*2.5)
            mesh.beam((.12,2.8,0),end,.12,'Wood',7,r2=.035)
            mesh.ellipsoid((end[0],end[1]+.55,end[2]),(4.2,2.5,3.7),
                           'FoliageLight' if i%3==0 else 'Foliage',18 if collider and self.refinement else 12,8 if collider and self.refinement else 6)
            if collider and self.refinement:
                for j in range(9):
                    pt=(end[0]+rng.uniform(-1.8,1.8),end[1]+rng.uniform(.0,1.5),end[2]+rng.uniform(-1.4,1.4))
                    mesh.ellipsoid(pt,(.85,.48,.75),'FoliageLight' if j%3==0 else 'Foliage',8,4)
        obj = mesh.object(self,pos=pos)
        obj.scale = (size,size,size)
        if collider:
            self.collider(name,(pos[0],pos[1]+2.2*size,pos[2]),(.65*size,4.4*size,.65*size),visuals=[obj.name])
        return obj


def prop_mesh(kind):
    m = Mesh('Linked_'+kind)
    if kind == 'horse':
        m.ellipsoid((0,1.45,0),(.62,.90,1.85),'HorseCoat',20,10)
        m.beam((0,1.65,-.70),(0,2.25,-1.03),.24,'HorseCoat',12,r2=.18)
        m.ellipsoid((0,2.20,-1.24),(.40,.44,.67),'HorseCoat',16,8)
        for xx in (-.22,.22):
            for zz in (-.60,.62):
                m.beam((xx,1.3,zz),(xx,.13,zz-.07),.095,'HorseCoat',10,r2=.06)
                m.box((xx,.08,zz-.11),(.15,.16,.24),'WindowDark')
        for xx in (-.13,.13):
            m.beam((xx,2.32,-1.1),(xx,2.62,-1.10),.075,'HorseCoat',8,r2=.018)
            m.ellipsoid((xx*1.6,2.28,-1.38),(.045,.055,.045),'WindowDark',8,4)
        m.tube([(0,1.6,.82),(0,1.32,1.18),(0,.9,1.22)],.07,'WindowDark')
        m.tube([(0,1.82,-.67),(0,2.28,-1.02)],.075,'WindowDark')
        for xx in (-.32,.32):
            m.tube([(xx,1.5,.65),(xx,1.70,-.68),(xx*.5,2.18,-1.25)],.023,'Door')
    elif kind == 'crate':
        for i in range(6):
            m.box((0,.09+i*.145,0),(1.1,.13,.85),'Wood')
        for x in (-.47,.47):
            for z in (-.44,.44):
                m.box((x,.475,z),(.095,.95,.055),'Wood')
        for z in (-.452,.452):
            m.beam((-.5,.13,z),(.5,.84,z),.055,'Wood',4)
        for x in (-.47,.47):
            for y in (.08,.84):
                for z in (-.47,.47):
                    m.beam((x,y,z),(x,y,z+.018),.022,'Iron',8)
    elif kind == 'barrel':
        seg=20
        v=[]
        for y,r in ((0,.32),(.12,.365),(.55,.405),(.98,.365),(1.1,.32)):
            v += [(r*math.cos(i*math.tau/seg),y,r*math.sin(i*math.tau/seg)) for i in range(seg)]
        f=[tuple(reversed(range(seg))),tuple(range(seg*4,seg*5))]
        f += [(j*seg+i,j*seg+(i+1)%seg,(j+1)*seg+(i+1)%seg,(j+1)*seg+i)
              for j in range(4) for i in range(seg)]
        m.add(v,f,'Wood')
        for y,r in ((.13,.367),(.32,.397),(.81,.397),(.98,.367)):
            for i in range(seg):
                a,b=i*math.tau/seg,(i+1)*math.tau/seg
                m.beam((r*math.cos(a),y,r*math.sin(a)),(r*math.cos(b),y,r*math.sin(b)),.028,'Iron',6)
        m.box((0,1.108,0),(.53,.018,.52),'Wood')
    elif kind == 'sack':
        m.ellipsoid((0,.40,0),(.66,.79,.47),'Burlap',16,8)
        m.beam((0,.69,0),(.035,.88,0),.095,'Burlap',10,r2=.058)
        m.beam((-.065,.78,0),(.078,.78,0),.012,'Wood',6)
    elif kind == 'bollard':
        m.box((0,.06,0),(.66,.12,.66),'Iron')
        m.beam((0,.10,0),(0,.9,0),.16,'Iron',12,r2=.19)
        m.beam((-.32,.78,0),(.32,.78,0),.1,'Iron',12)
        for x in (-.22,.22):
            for z in (-.22,.22):
                m.beam((x,.12,z),(x,.16,z),.035,'Iron',8)
    elif kind == 'chair':
        for x in (-.22,.22):
            for z in (-.20,.20):
                m.beam((x,0,z),(x*.85,.45,z*.9),.025,'Wood',8)
        m.box((0,.45,0),(.5,.05,.46),'Wood')
        for x in (-.22,.22):
            m.beam((x,.44,-.18),(x,.94,-.25),.025,'Wood',8)
        for y in (.65,.86,.94):
            m.beam((-.22,y,-.24),(.22,y,-.24),.022,'Wood',8)
        for x in (-.13,0,.13):
            m.beam((x,.55,-.22),(x,.92,-.25),.016,'Wood',6)
    elif kind == 'table':
        m.beam((0,.73,0),(0,.78,0),.45,'Marble',24)
        m.beam((0,.08,0),(0,.73,0),.05,'Iron',10)
        for i in range(3):
            a=i*math.tau/3
            m.beam((0,.12,0),(.33*math.cos(a),.035,.33*math.sin(a)),.036,'Iron',8)
    elif kind == 'bench':
        for i in range(5):
            m.box((0,.48,-.25+i*.125),(1.9,.055,.11),'Wood')
        for i in range(4):
            m.box((0,.67+i*.11,-.32),(1.9,.09,.045),'Wood')
        for x in (-.72,.72):
            m.box((x,.25,0),(.08,.5,.5),'Iron')
            m.box((x,.72,-.35),(.075,.58,.08),'Iron')
    elif kind == 'lamp':
        m.beam((0,.12,0),(0,4.5,0),.075,'Iron',12,r2=.038)
        m.beam((0,0,0),(0,.4,0),.18,'Iron',12,r2=.12)
        m.box((0,4.72,0),(.34,.42,.34),'LampGlow')
        for x in (-.19,.19):
            for z in (-.19,.19):
                m.beam((x,4.48,z),(x,4.96,z),.02,'Iron',6)
        m.hip_roof((0,0,0),.57,.57,4.97,.22,'Iron')
        m.beam((0,5.18,0),(0,5.3,0),.04,'Iron',8,r2=.008)
    elif kind in ('cart','rickshaw','carriage'):
        length=1.9 if kind=='rickshaw' else 2.5
        width=1.1 if kind=='rickshaw' else 1.85
        floor=.88
        m.box((0,floor,0),(width,.12,length),'Wood')
        for x in (-width/2,width/2):
            m.box((x,1.13,0),(.085,.42,length),'Wood')
        m.box((0,1.13,length/2),(width,.42,.09),'Wood')
        for x in (-width/2-.08,width/2+.08):
            for z in ((0,) if kind!='carriage' else (-.85,.85)):
                # Spoked wheel lies in YZ, axle along X.
                for i in range(24):
                    a,b=i*math.tau/24,(i+1)*math.tau/24
                    m.beam((x,.61+.59*math.cos(a),z+.59*math.sin(a)),
                           (x,.61+.59*math.cos(b),z+.59*math.sin(b)),.035,'Iron',6)
                for i in range(12):
                    a=i*math.tau/12
                    m.beam((x,.61,z),(x,.61+.54*math.cos(a),z+.54*math.sin(a)),.027,'Wood',6)
                m.beam((x-.1,.61,z),(x+.1,.61,z),.12,'Iron',10)
        for x in (-width*.36,width*.36):
            m.beam((x,.93,-.5),(x,.60,-2.55),.046,'Wood',8)
        if kind == 'rickshaw':
            m.box((0,1.24,.38),(1,.12,.9),'CanvasRed')
            m.box((0,1.57,.8),(1,.60,.12),'CanvasRed')
            for x in (-.57,.57):
                m.beam((x,1.15,.9),(x,2.05,.9),.025,'Iron',8)
            m.hip_roof((0,0,.4),1.34,1.4,2.01,.28,'CanvasIvory')
        if kind == 'carriage':
            m.box((0,1.63,.12),(1.80,1.45,2.15),'Door')
            for x in (-.91,.91):
                m.box((x,1.85,-.05),(.04,.62,1.32),'WindowDark')
                m.beam((x,1.9,-.05),(x,2.25,-.05),.035,'Wood',6)
            m.hip_roof((0,0,.15),2.07,2.55,2.37,.29,'CanvasIvory')
            m.box((0,1.18,-1.33),(1.35,.16,.68),'Wood')
        if kind == 'cart':
            for i in range(8):
                x=(i%3-1)*.53
                z=(i//3-1)*.52
                m.ellipsoid((x,1.22,z),(.6,.62,.46),'Burlap',12,6)
    else:
        raise ValueError(kind)
    return m


def window(m, x, y, z, width=1.35, height=2, arch=False, sign=1):
    m.box((x,y,z),(width,height,.12),'WindowDark')
    for side in (-1,1):
        sx=x+side*(width/2+.20)
        m.box((sx,y,z+.10*sign),(.38,height,.09),'Shutter')
        for i in range(12):
            m.box((sx,y-height/2+.10+i*(height-.15)/12,z+.16*sign),(.32,.055,.055),'Shutter')
    for xx in (-width/2-.025,width/2+.025):
        m.box((x+xx,y,z+.075*sign),(.10,height+.12,.15),'Basilica')
    for yy in (-height/2,height/2):
        m.box((x,y+yy,z+.09*sign),(width+.22,.09,.19),'Basilica')
    m.box((x,y,z+.08*sign),(.042,height,.035),'Wood')
    m.box((x,y-.20,z+.08*sign),(width,.042,.035),'Wood')
    if arch:
        m.arch(x,y-height/2,z+.13*sign,width,height,.13,'Basilica',.14,12)


def facade(k, name, center, width, depth, floors, french=True, front_sign=1):
    x,y,z=center
    h=floors*3.5+.45
    m=Mesh(name)
    m.box((0,h/2,0),(width,h,depth),'Wall' if french else 'NhaRongWall')
    front=front_sign*(depth/2+.07)
    for f in range(floors+1):
        m.box((0,f*3.5+.2,front), (width+.16,.19,.24),'Basilica')
    bays=max(2,round(width/2.8))
    for i in range(bays):
        xx=-width/2+(i+.5)*width/bays
        for f in range(1,floors):
            window(m,xx,3.5*f+1.60,front+.07*front_sign,1.15,1.90,False,front_sign)
        is_cafe=name in ('Shop_-1_4','PortFacade_07')
        m.box((xx,1.45,front+.05*front_sign),(1.75,2.65,.08),'CafeGlass' if is_cafe else 'Door')
        m.arch(xx,.15,front+.18*front_sign,1.75,1.85,.16,'Basilica',.2,12)
        m.box((xx,1.34,front+.11*front_sign),(.055,2.5,.04),'Iron')
        if is_cafe:
            m.box((xx,1.0,front+.13*front_sign),(1.70,.055,.06),'Wood')
            m.box((xx,.4,front+.13*front_sign),(1.70,.7,.07),'Wood')
    # Balcony rails have real bars and top rails, readable at eye height.
    if floors>=3 or not french:
        by=3.55
        bz=front+front_sign*.76
        m.box((0,by,bz),(width-.4,.14,1.6),'Basilica')
        for xx in (-width/2+.22,width/2-.22):
            m.beam((xx,by,bz-front_sign*.74),(xx,by+1.04,bz-front_sign*.74),.026,'Iron')
        m.beam((-width/2+.2,by+1.04,bz+front_sign*.74),(width/2-.2,by+1.04,bz+front_sign*.74),.03,'Iron')
        n=math.ceil(width/.23)
        for i in range(n):
            xx=-width/2+.28+i*(width-.56)/max(1,n-1)
            m.beam((xx,by+.08,bz+front_sign*.74),(xx,by+1.03,bz+front_sign*.74),.015,'Iron',6)
    m.box((0,h+.05,0),(width+.6,.28,depth+.6),'Basilica')
    m.hip_roof((0,0,0),width+.7,depth+.6,h+.2,1.1 if french else 1.5)
    if french:
        for xx in (-width*.3,width*.3):
            m.box((xx,h+.85,-depth*.2),(.65,1.3,.55),'QuayStone')
    obj=m.object(k,pos=center)
    k.collider(name,(x,y+h/2,z),(width,h,depth),visuals=[obj.name])
    return obj


def canopy(k, name, center, width, depth, height, poles=True):
    x,y,z=center
    m=Mesh(name)
    for i in range(round(width/.42)):
        ww=width/round(width/.42)
        xx=-width/2+(i+.5)*ww
        m.box((xx,height,0),(ww,.065,depth),'CanvasRed' if i%2==0 else 'CanvasIvory')
        m.box((xx,height-.2,depth/2),(ww,.35,.055),'CanvasRed' if i%2==0 else 'CanvasIvory')
    if poles:
        for xx in (-width/2+.1,width/2-.1):
            for zz in (-depth/2+.1,depth/2-.1):
                m.beam((xx,0,zz),(xx,height,zz),.038,'Iron',8)
    obj=m.object(k,pos=center)
    if poles:
        for i,xx in enumerate((-width/2+.1,width/2-.1)):
            for j,zz in enumerate((-depth/2+.1,depth/2-.1)):
                k.collider(f'{name}_post_{i}_{j}',(x+xx,y+height/2,z+zz),(.10,height,.10),visuals=[obj.name])


def warehouse(k,name,center,width,depth):
    x,y,z=center
    m=Mesh(name)
    m.box((0,2.4,0),(width,4.8,depth),'Wall')
    m.hip_roof((0,0,0),width+1,depth+1,4.9,1.7,'Iron')
    n=max(2,round(width/5))
    for i in range(n):
        xx=-width/2+(i+.5)*width/n
        m.box((xx,1.55,depth/2+.05),(2.8,3.1,.1),'Door')
        m.beam((xx-1.35,.08,depth/2+.12),(xx+1.35,3.02,depth/2+.12),.06,'Iron',6)
    for xx in (-width/2+.2,width/2-.2):
        m.box((xx,2.4,depth/2+.10),(.3,4.8,.3),'Basilica')
    obj=m.object(k,pos=center)
    k.collider(name,(x,y+2.4,z),(width,4.8,depth),visuals=[obj.name])


def cargo(k,prefix,pos,rows=3,cols=5):
    x,y,z=pos
    for j in range(rows):
        for i in range(cols):
            kind='barrel' if (i+j)%4==0 else 'crate'
            xx=x+i*1.2
            zz=z+j*1.02
            k.instance(kind,f'{prefix}_{j}_{i}',(xx,y,zz),yaw=(i%3-1)*3)
            if kind=='crate' and j==rows-1 and i%2==0:
                k.instance('crate',f'{prefix}_{j}_{i}_upper',(xx,y+.95,zz),yaw=4,collider=False)
                k.collider(f'{prefix}_{j}_{i}_upper',(xx,y+1.425,zz),(1.1,.95,.85),4,[f'{prefix}_{j}_{i}_upper'])


def boat(k,name,pos,length=8,sail=False,covered=True):
    m=Mesh(name)
    xs=[-length/2,-length*.36,0,length*.36,length/2]
    widths=[.02,.9,1.15,.82,.02]
    verts=[]
    for x,w in zip(xs,widths):
        verts += [(x,.15,-w),(x,-.45,-w*.44),(x,-.45,w*.44),(x,.15,w)]
    faces=[]
    for i in range(4):
        for j in range(3):
            faces.append((i*4+j,(i+1)*4+j,(i+1)*4+j+1,i*4+j+1))
    faces += [(0,1,2,3),(16,19,18,17)]
    m.add(verts,faces,'Wood')
    for i in range(1,4):
        m.box((xs[i],.06,0),(.13,.10,widths[i]*1.8),'Wood')
    if covered:
        for j in range(13):
            x=-length*.25+j*length*.042
            pts=[(x,.10+1.0*math.sin(i*math.pi/12),.92*math.cos(i*math.pi/12)) for i in range(13)]
            m.tube(pts,.033,'Wood',6)
        for j in range(12):
            x0=-length*.25+j*length*.042
            x1=x0+length*.042
            for i in range(12):
                a,b=i*math.pi/12,(i+1)*math.pi/12
                m.add([(x0,.14+math.sin(a),.92*math.cos(a)),(x1,.14+math.sin(a),.92*math.cos(a)),
                       (x1,.14+math.sin(b),.92*math.cos(b)),(x0,.14+math.sin(b),.92*math.cos(b))],[(0,1,2,3)],'Burlap')
    if sail:
        m.beam((0,0,0),(0,7,0),.06,'Wood',8,r2=.025)
        m.beam((-length*.35,1.3,0),(length*.30,6.5,0),.04,'Wood',8)
        m.add([(-length*.35,1.3,.04),(length*.30,6.5,.04),(0,6.7,.04)],[(0,1,2)],'CanvasIvory')
    m.beam((length*.32,.15,0),(length*.8,.26,1.5),.035,'Wood',6)
    return m.object(k,pos=pos,yaw=(k.rng.random()-.5)*16)


def distant_ship(k,name,pos,length=58):
    m=Mesh(name)
    m.box((0,1.7,0),(length,3.4,9),'Iron')
    m.box((0,3.65,0),(length*.50,1.6,7),'Wall')
    m.beam((-length*.18,3.7,0),(-length*.18,10,0),.55,'Iron',12)
    for x in (-length*.32,length*.30):
        m.beam((x,3.6,0),(x,19,0),.13,'Wood',8,r2=.045)
        m.beam((x-6,15,0),(x+6,15,0),.06,'Wood',8)
    return m.object(k,pos=pos,yaw=-4)


def mooring(k,name,quay_pos,ship_pos):
    m=Mesh(name)
    pts=[]
    for i in range(13):
        t=i/12
        p=Vector(quay_pos).lerp(Vector(ship_pos),t)
        p.y-=.25*math.sin(t*math.pi)
        pts.append(tuple(p))
    m.tube(pts,.038,'Burlap',8)
    m.object(k)
    c=Mesh(name+'_coil')
    for j in range(3):
        pts=[(quay_pos[0]+(.28+j*.08)*math.cos(i*math.tau/36),quay_pos[1]-.62+j*.015,
              quay_pos[2]-.4+(.28+j*.08)*math.sin(i*math.tau/36)) for i in range(37)]
        c.tube(pts,.021,'Burlap',6)
    c.object(k)


def quay_edge(k,minimum,maximum,y,z,gap):
    for i,x in enumerate(range(math.ceil(minimum/6)*6,math.floor(maximum/6)*6+1,6)):
        if abs(x-gap[0])<gap[1]/2+.5:
            continue
        k.instance('bollard',f'Bollard_{i:02d}',(x,y,z-.42))
    # Actual metal picket rail under draped chain. Collider tracks visible rail.
    for side,a,b in (('west',minimum,gap[0]-gap[1]/2),('east',gap[0]+gap[1]/2,maximum)):
        k.fence('QuayRail_'+side,(a,y,z-.15),(b,y,z-.15),1.05,True)
        m=Mesh('QuayChain_'+side)
        n=math.ceil((b-a)/3)
        for j in range(n):
            xx=a+(b-a)*j/n
            xx2=a+(b-a)*(j+1)/n
            if k.refinement<2:
                pts=[(xx+(xx2-xx)*i/12,y+.76-.20*math.sin(i*math.pi/12),z-.15) for i in range(13)]
                m.tube(pts,.025,'Iron',6)
            else:
                links=math.ceil((xx2-xx)/.135)
                for i in range(links):
                    t=(i+.5)/links
                    m.chain_link((xx+(xx2-xx)*t,y+.76-.20*math.sin(t*math.pi),z-.15),i%2==0)
        m.object(k)


def nharong_build(k):
    y=-4.1
    k.layout['ship']={'position':[-14.964,-5.6,0],'yaw':0,'length':124.1,'beam':15.2,
                      'gangway_top':[-1.5,0,-7.38], 'gangway_foot':[-1.5,y,-13.5]}
    k.layout['player_spawn']={'pos':[3,y,-18], 'yaw':0}
    k.box('QuayFoundation',(-15,y-.75,-28),(150,1.5,40),'QuayStone',collider=True,floor=True)
    k.walk('quay',(-90,y,-48),(60,y,-8))
    k.box('QuayCap',(-15,y-.04,-8.25),(150,.11,.50),'Basilica')
    quay_edge(k,-90,60,y,-8,(-1.5,2.4))
    k.fence('EndWest',(-90,y,-8),(-90,y,-48),1.8)
    k.fence('EndEast',(60,y,-8),(60,y,-48),1.8)
    k.fence('BackWest',(-90,y,-48),(-11,y,-48),1.8)
    k.fence('BackEast',(23,y,-48),(60,y,-48),1.8)
    warehouse(k,'WarehouseWest',(-62,y,-43),34,10)
    warehouse(k,'WarehouseEast',(43,y,-43),28,10)
    cargo(k,'CargoWest',(-55,y,-29),3,6)
    cargo(k,'CargoEast',(32,y,-28),3,6)
    k.instance('cart','PotatoCart',(6,y,-20),90)
    k.instance('rickshaw','RickshawWaiting',(23,y,-35),20)
    k.instance('carriage','HorseCarriage',(-73,y,-31),90)
    k.instance('horse','Horse',(-76,y,-31),90)
    for i,x in enumerate((-78,-34,-23,29,52)):
        k.tree(f'Tamarind_{i}',(x,y,-36),.95)
    for i,x in enumerate((-80,-40,18,51)):
        k.instance('lamp',f'QuayLamp_{i}',(x,y,-18.5))
    # Booth clears both the 1.4 m gangway and its two-metre landing approach.
    k.box('TicketBooth',(2.7,y+1.35,-14.9),(2.2,2.7,1.8),'Wood',collider=True)
    k.box('TicketWindow',(2.7,y+1.45,-13.975),(1.25,.70,.05),'WindowDark')
    bm=Mesh('TicketRoof'); bm.hip_roof((0,0,0),2.7,2.3,2.7,.65)
    bm.object(k,pos=(2.7,y,-14.9))
    # Nine real arcade openings, two storeys, attic vents, hipped tiled roof.
    m=Mesh('NhaRongCustoms')
    width,depth=34,15
    m.box((0,4.48,-1.6),(width,8.96,depth-3.2),'NhaRongWall')
    for f in (.15,4.25,8.6):
        m.box((0,f,5.75),(width+.3,.21,3.9),'Basilica')
    for i in range(10):
        xx=-17+i*34/9
        m.box((xx,1.50,7.25),(.6,3,.7),'Basilica')
        m.box((xx,6.40,7.25),(.38,4.25,.38),'Basilica')
        m.box((xx,3.0,7.25),(.8,.20,.9),'Basilica')
    for i in range(9):
        xx=-17+(i+.5)*34/9
        m.arch(xx,.05,7.25,3.16,2.58,.34,'Basilica',.65,20)
        # Spandrel starts above arch crown, keeping opening genuinely hollow.
        m.box((xx,4.05,7.25),(3.18,.32,.65),'NhaRongWall')
        window(m,xx,6.22,4.39,1.52,2.46)
        m.box((xx,9.1,5.8),(.85,.60,.10),'WindowDark')
        window(m,xx,1.6,4.39,1.30,2.5,True)
    m.box((0,9.05,0),(35,.25,16),'Basilica')
    m.hip_roof((0,0,0),35.8,17.5,9.24,2.8)
    m.beam((-11,12.10,0),(11,12.10,0),.12,'NhaRongRoof',12)
    # Roof end curls and sculpted mirrored dragons facing the central moon.
    for sign in (-1,1):
        for zz in (-8.5,8.5):
            m.tube([(sign*17.5,9.3,zz),(sign*18,9.5,zz),(sign*18.15,9.85,zz)],.075,'NhaRongRoof',8)
        pts=[(sign*x,12.12+h,0) for x,h in [(7.0,0),(6.7,.28),(6.8,.68),(6.4,1.0),
                                                         (5.9,.7),(5.4,.22),(4.8,.11),(4.1,.45),(3.5,.98),(2.9,1.16)]]
        m.tube(pts,.16,'Basilica',10)
        m.ellipsoid((sign*2.72,13.32,0),(.66,.42,.44),'Basilica',14,8)
        m.beam((sign*2.72,13.36,0),(sign*2.25,13.40,0),.14,'Basilica',10,r2=.09)
        for zz in (-.18,.18):
            m.beam((sign*2.95,13.48,zz),(sign*3.12,13.9,zz),.05,'Basilica',8,r2=.012)
            m.ellipsoid((sign*2.60,13.45,zz),(.075,.08,.065),'WindowDark',8,4)
            for xx in (4.0,5.3):
                m.tube([(sign*xx,12.46,zz),(sign*(xx-.3),12.15,zz*2),(sign*(xx-.65),12.12,zz*2.3)],.055,'Basilica',8)
        for j in range(14):
            xx=3.35+j*.24
            hh=.9 if xx<4 else .28
            m.beam((sign*xx,12.20+hh,0),(sign*(xx+.09),12.52+hh,0),.053,'Basilica',6,r2=.006)
    m.ellipsoid((0,13.05,0),(.82,.82,.22),'Basilica',20,10)
    if k.refinement==2:
        m.beam((0,12.10,0),(0,12.69,0),.095,'Basilica',10,r2=.15)
        m.box((0,12.18,0),(.48,.15,.40),'Basilica')
    obj=m.object(k,pos=(6,y,-56))
    k.collider('CustomsCore',(6,y+4.5,-57.6),(34,9,11.8),visuals=[obj.name])
    for i in range(10):
        k.collider(f'ArcadePier_{i}',(-11+i*34/9,y+1.5,-48.75),(.6,3,.7),visuals=[obj.name])
    k.box('VerandahFloor',(6,y+.025,-49.75),(34,.25,3.5),'Basilica',collider=True,floor=True)
    k.walk('verandah',(-11,y+.15,-51.5),(23,y+.15,-48))
    # Water steps are behind a physical gate; do not expose a fall at a playable boundary.
    for i in range(7):
        k.box(f'WaterStep_{i}',(-63,y-.18-i*.22,-7.45+i*.45),(2,.22,.6),'QuayStone')
    k.fence('WaterStepsGate',(-64.25,y,-8.7),(-61.75,y,-8.7),1.2,True)
    k.box('River',(0,-5.62,116),(630,.12,248),'WaterRiver')
    k.box('FarBank',(0,-5.7,251),(680,1.5,34),'Shore')
    for i in range(26):
        k.tree(f'FarBankTree_{i}',(-305+i*24,-4.95,251+(i%3)*4),.75,collider=False)
    for i in range(8):
        x=-220+i*61
        far=Mesh(f'StiltHouse_{i}')
        for xx in (-3,3):
            for zz in (-2,2):
                far.beam((xx,0,zz),(xx,3.4,zz),.12,'Wood',6)
        far.box((0,3.4,0),(6.8,.15,4.8),'Wood')
        far.box((0,4.5,0),(6,2.2,4),'Wood')
        far.hip_roof((0,0,0),7.4,5.2,5.6,1.3,'Burlap')
        far.object(k,pos=(x,-5.6,233))
    for i,pos in enumerate(((-66,-5.35,22),(28,-5.35,32),(-114,-5.35,12),(83,-5.35,48))):
        boat(k,f'Sampan_{i}',pos,8+i,covered=True)
    distant_ship(k,'DistantSteamer_1',(-185,-5.6,100),63)
    distant_ship(k,'DistantSteamer_2',(139,-5.6,145),52)
    for i,x in enumerate((-54,-30,18,42)):
        mooring(k,f'Mooring_{i}',(x,y+.75,-8.4),(x+4,-.75,-7.30))
        k.anchor(f'mooring_bollard_{i}',(x,y,-8.4),'object')
    anchors={'gangway_foot':(-1.5,y,-13.5),'ticket_booth':(2.7,y,-14.9),
             'ticket_checker':(-3.2,y,-15.1),'potato_cart':(6,y,-20),'potato_pickup':(3.7,y,-20),
             'mai_standing':(-5,y,-18.2),'porters_loading':(-8,y,-20.5),
             'farewell_group':(15,y,-24),'vendor':(-25,y,-31),'customs_approach':(6,y,-42)}
    for name,p in anchors.items():
        k.anchor(name,p,'object' if name in ('ticket_booth','potato_cart') else 'walk')
    k.anchor('gangway_top',(-1.5,0,-7.38),'ship')
    k.anchor('legacy_deck_spawn',(1.8,0,-2.6),'ship')
    k.anchor('chef_galley_door',(-10,0,-3.45),'ship')
    k.anchor('food_hold_landing',(-7.56,-3,0),'ship')
    k.anchor('railing_departure',(2.4,1,-6.8),'ship')
    k.anchor('hands_opening',(3,y,-18),'walk')
    k.path('porters_loop',[(-8,y,-20.5),(-12,y,-17),(-4.8,y,-16.8),(-1.5,y,-16.8),
                           (-1.5,y,-13.5),(-1.5,0,-7.38),(-1.5,0,-6.4)],False,12)
    k.layout['crowd']['paths'][-1]['motion']='shuttle; queue on gangway, reverse at deck landing'
    k.path('quay_workers_loop',[(-34,y,-21),(-34,y,-32),(-15,y,-32),(-15,y,-21)],True,8)
    k.path('passersby',[(-26,y,-34),(-10,y,-34),(8,y,-34),(18,y,-34)],False,6)
    k.queue('queue_gangway',[(0,y,-27),(0,y,-22),(0,y,-18),(-1.5,y,-16)],9)
    k.spot('farewell_group_1',(15,y,-24),5)
    k.spot('vendor_group',(-25,y,-31),4)
    k.spot('mai_and_helpers',(-5,y,-18.2),3)
    k.spot('warehouse_workers',(-40,y,-32),3)
    k.layout['beat_routes']=[{'name':'load_potatoes','anchors':['potato_pickup','mai_standing','ticket_checker','gangway_foot']},
                             {'name':'customs_view','anchors':['gangway_foot','customs_approach']}]
    k.measure('quay',(150,1.5,40),'gameplay extent; covers 124.1 m ship plus both ends')
    k.measure('customs_core',(34,8.96,15),'photo proportions; estimated, not measured survey')
    k.measure('customs_roof',(35.8,4.7,17.5),'9.24 m eaves, 12.04 m ridge, dragons to 13.9 m above quay')
    k.measure('arcade_bay',(34/9,4.25,3.5),'nine visible arches; estimated from old front views')
    k.layout['notes']='1911 daytime reconstruction. No reference photographs embedded. New quay spawn follows approved M10d plan; old builder deck spawn retained as anchor. Ship and retracting gangway remain scene-owned and are NOT included in environment FBX.'


def saigon_build(k):
    k.layout['player_spawn']={'pos':[-27,.12,-7.5],'yaw':90}
    k.box('ContinuousStreetBase',(0,-.4,0),(84,.8,20),'Shore',collider=True,floor=True)
    k.box('RoadCobble',(0,-.029,0),(84,.07,12),'Cobble')
    k.walk('street',(-42,0,-6),(42,0,6))
    for sign in (-1,1):
        k.box('Sidewalk_'+str(sign),(0,.015,sign*8),(84,.21,4),'QuayStone',collider=True,floor=True)
        k.walk('sidewalk_'+str(sign),(-42,.12,sign*8-2),(42,.12,sign*8+2))
        for i in range(7):
            x=-36+i*12
            facade(k,f'Shop_{sign}_{i}',(x,.12,sign*15),12,10,2+(i%3==1),False,-sign)
        for i,x in enumerate((-33,-18,0,24,36)):
            k.tree(f'Tamarind_{sign}_{i}',(x,.12,sign*6.45),.72)
        for i,x in enumerate((-36,-13,8,31)):
            k.instance('lamp',f'ElectricLamp_{sign}_{i}',(x,.12,sign*6.2))
    canopy(k,'FrenchCafeAwning',(12,.12,-8.75),11,2.4,3.05)
    for i,x in enumerate((9,12,15)):
        k.instance('table',f'CafeTable_{i}',(x,.12,-8.25))
        k.instance('chair',f'CafeChair_{i}_west',(x-.8,.12,-8.25),90)
        k.instance('chair',f'CafeChair_{i}_east',(x+.8,.12,-8.25),-90)
    k.instance('rickshaw','RickshawSouth',(-35,.12,-8.15),90)
    k.instance('carriage','CarriageNorth',(28,0,3.8),90)
    if k.refinement==2:
        k.instance('horse','CarriageHorse',(25,0,3.8),90)
    # End boundaries are visible construction barriers and parked work carts.
    for sign in (-1,1):
        k.fence(f'EndConstruction_{sign}',(sign*42,0,-10),(sign*42,0,10),2.2)
        k.instance('cart',f'EndCart_{sign}',(sign*39,0,0),90)
        k.box(f'ConstructionTimber_{sign}',(sign*40,1.05,6.8),(2.4,2.1,3.8),'Wood',collider=True)
    k.anchor('le_walk_start',(-27,.12,-7.5))
    k.anchor('conversation_ba',(-5,.12,-7.4))
    k.anchor('conversation_le',(-3.7,.12,-7.4))
    k.anchor('hands_pose',(-5,.12,-7.4))
    k.anchor('cafe_approach',(12,.12,-6.5))
    k.anchor('cafe_door',(12,.12,-9.7))
    k.anchor('electric_light_view',(6.8,.12,-7.0))
    k.anchor('electric_light_bulb',(8,4.84,-6.2),'light')
    k.anchor('cafe_seat',(9.8,.12,-8.25),'seat')
    k.anchor('water_machine',(20,.12,-8.8),'object')
    k.box('WaterStand',(20,.65,-8.8),(.65,1.05,.5),'Iron',collider=True)
    wm=Mesh('WaterSpout'); wm.tube([(20,1.2,-8.8),(20,1.4,-8.5),(20,1.15,-8.3)],.045,'Brass')
    wm.object(k)
    k.anchor('screen_facade',(-12,3.5,-9.7),'object')
    k.anchor('few_days_later',(-5,.12,-7.4))
    k.path('evening_south',[(-27,.12,-7.4),(-20,.12,-7.4),(-13,.12,-7.4),(-5,.12,-7.4),(3,.12,-7.4)],False,5)
    k.path('evening_north',[(-25,.12,8.1),(-8,.12,8.1),(10,.12,8.1),(22,.12,8.1)],False,5)
    k.spot('cafe_patrons',(12,.12,-8.25),3)
    k.layout['beat_routes']=[{'name':'P1_P2_P3','anchors':['le_walk_start','conversation_ba','hands_pose','electric_light_view','cafe_approach']}]
    k.measure('street_segment',(84,.8,20),'fictional Catinat/Charner-inspired 1911 block')
    k.measure('road',(84,0,12),'12 m carriageway; 4 m pavements with 0.12 m curbs')
    k.measure('shop_module',(12,7.45,10),'2 or 3 floors; 3.5 m floor pitch; no precise street photo supplied')
    k.measure('cafe_canopy',(11,3.05,2.4),'gameplay composition for electric-light observation')
    k.layout['notes']='All street placement and storefront identities are conjectural. Electric lamps follow S16 and the official district history recording replacement by electric street lights in 1909. No electric tram or overhead tram wires added to Saigon. Prologue P1-P4 anchors included; cut cards do not require meshes.'


def crane(k,name,pos):
    m=Mesh(name)
    m.box((0,.25,0),(3.2,.5,3.2),'QuayStone')
    for x in (-.9,.9):
        for z in (-.9,.9):
            m.beam((x,.5,z),(x*.25,8,z*.25),.095,'Iron',8)
    for i in range(5):
        yy=.7+i*1.45
        m.beam((-.8,yy,-.8),(.8,yy+1.45,-.8),.044,'Iron',6)
        m.beam((.8,yy,.8),(-.8,yy+1.45,.8),.044,'Iron',6)
    m.beam((0,7.7,0),(0,11,0),.12,'Iron',10)
    m.beam((0,8.3,0),(0,10.3,7.5),.15,'Iron',10)
    m.beam((0,11,0),(0,10.3,7.5),.05,'Iron',6)
    m.beam((0,10.3,7.5),(0,2.1,7.5),.021,'Iron',6)
    m.tube([(0,2.1,7.5),(0,1.9,7.5),(.16,1.8,7.5),(.23,1.95,7.5)],.045,'Iron',8)
    obj=m.object(k,pos=pos,yaw=-90)
    k.collider(name,(pos[0],pos[1]+4,pos[2]),(3.2,8,3.2),visuals=[obj.name])


def ship_beat_anchors(k):
    """External ship task references; use the existing ship's documented frame."""
    if 'ship' not in k.layout:
        return
    zones=json.loads((ROOT/'Game/Assets/_Project/Art/Models/Ship/deck_zones.json').read_text())
    transform=zones['galley_coordinate_transform']
    origin=k.layout['ship']['position']
    def ship_point(p):
        return tuple(origin[i]+p[i] for i in range(3))
    def galley_point(p):
        b=[sum(row[i]*p[i] for i in range(3))+offset
           for row,offset in zip(transform['matrix'],transform['translation'])]
        return ship_point(upos(b))
    stations={'galley_coal_bin':(.3,.6,2.5),'galley_stove_mouth':(-3,.65,1.85),
              'galley_prep_drop':(.3,1.2,0),'galley_scrub_pot':(3.4,.4,-1.8),
              'galley_scrub_stool':(3.4,0,-.9),'galley_evening_seat':(-3.85,0,1.35),
              'galley_evening_lamp':(-4.55,.76,.75),'galley_evening_book':(-4.35,.78,.5),
              'galley_wash_basin':(6.62,.82,-1.6)}
    for i in range(3):
        stations[f'galley_storm_pot_{i}']=(-4+i,.9,-2.65)
    for name,p in stations.items():
        k.anchor(name,galley_point(p),'ship_task')
    for i,p in enumerate(((13.9,2.8,1.7),(14,2.8,2.35),(13.3,2.8,2.1))):
        k.anchor(f'hold_potato_sack_{i}',ship_point(p),'ship_task')
    connection=next(c for c in zones['connections'] if c['id']=='hold_stairs')
    for end in ('top','bottom'):
        k.anchor('hold_stair_'+end,ship_point(upos(connection[end])),'ship')
    k.anchor('food_hold_landing',ship_point(upos(connection['bottom'])),'ship')
    if k.name=='Marseille':
        k.anchor('last_shift_sweep',galley_point((-1.5,0,-1.45)),'ship_task')
        k.anchor('last_shift_wash',galley_point((6.62,.82,-1.6)),'ship_task')
    k.layout['ship_anchor_sources']={'frame':'deck_zones.galley_coordinate_transform + ShipConfig position',
        'stations':'GalleySceneBuilder.cs and ShipInterior.cs; ship-owned task objects, not part of environment FBX',
        'stairs':'deck_zones.connections.hold_stairs; bottom is y=-2.8 world for both port scenes',
        'storm_pots':'intended local counter offsets from BuildPots; runtime task integration remains ship-owned'}


def marseille_build(k):
    y=-2.5
    k.layout['ship']={'position':[-13.464,-5.6,4.17],'yaw':0,'length':124.1,'beam':15.2,
                      'gangway_top':[0,0,-3.21], 'gangway_foot':[0,y,-9.2]}
    k.layout['player_spawn']={'pos':[3,0,-2],'yaw':180}
    k.box('ContinuousQuay',(-14,y-1.8,-24.35),(148,3.6,31.3),'QuayStone',collider=True,floor=True)
    k.walk('quay',(-88,y,-40),(60,y,-8.7))
    quay_edge(k,-88,60,y,-8.7,(0,2.4))
    for sign in (-1,1):
        x=-88 if sign<0 else 60
        k.fence(f'QuayEnd_{sign}',(x,y,-8.7),(x,y,-40),1.8,True)
    k.box('StreetInset',(-14,y-.033,-18.5),(148,.08,4),'Cobble')
    for i,z in enumerate((-17.8,-19.2)):
        k.box(f'TramRail_{i}',(-14,y+.017,z),(148,.034,.07),'Iron')
    k.box('CafePavement',(-14,y+.005,-25.5),(148,.19,10),'Cobble',collider=True,floor=True)
    k.walk('cafe_sidewalk',(-88,y+.1,-30.5),(60,y+.1,-20.5))
    for i,x in enumerate((-79,-67,-55,-43,-31,-19,-7,5,17,29,41,53)):
        # Central sightline above cafe roof preserves silhouette of basilica.
        if i==7:
            h=1  # low cafe pavilion in the builder's central silhouette gap
        else:
            h=4+(i%3==0)
        facade(k,f'PortFacade_{i:02d}',(x,y+.1,-35),12,9,h,True)
    k.fence('RearBoundary',(-88,y,-40),(60,y,-40),1.8,True)
    warehouse(k,'WarehouseWest',(-67,y,-13.45),26,7.4)
    cargo(k,'WineCargo',(-41,y,-13.8),2,6)
    cargo(k,'CargoEast',(31,y,-12.8),2,5)
    crane(k,'QuayCraneWest',(-51,y,-13))
    crane(k,'QuayCraneEast',(49,y,-13))
    for i,x in enumerate((-48,-22,13,40)):
        k.instance('lamp',f'PortLamp_{i}',(x,y,-15.2))
    k.instance('bench','PortBench',(6,y,-14.5),180)
    k.instance('cart','PortCart',(-26,y,-13.5),90)
    for i,x in enumerate((-84,-60,-30,24,54)):
        mooring(k,f'Mooring_{i}',(x,y+.75,-9.12),(x+3,-.70,-3.25))
        k.anchor(f'mooring_bollard_{i}',(x,y,-9.12),'object')
    # E5 positions preserve current builder values exactly, with 0.1 m pavement lift.
    canopy(k,'CafeAwning',(3,y+.1,-25.5),9,3,3.05)
    for i,x in enumerate((.5,3.1,5.7)):
        k.instance('table',f'CafeTable_{i}',(x,y+.1,-24))
        k.instance('chair',f'CafeChair_{i}',(x,y+.1,-23.2),180)
        k.instance('chair',f'CafeChair_{i}_side',(x+1,y+.1,-24),-90)
    k.anchor('gangway_foot',(0,y,-9.2))
    k.anchor('gangway_top',(0,0,-3.21),'ship')
    k.anchor('dawn_deck_spawn',(3,0,-2),'ship')
    k.anchor('hands_pay_pose',(3,0,-2),'ship')
    k.anchor('salary_queue',(-2.5,0,-1.1),'ship')
    k.anchor('salary_table',(-4.2,0,-1.1),'ship')
    k.anchor('last_shift_sweep',(-12.5,0,3.3),'ship')
    k.anchor('last_shift_wash',(-19.5,0,2.9),'ship')
    k.anchor('goodbye_chef',(-8.5,0,.72),'ship')
    k.anchor('goodbye_jacques',(-2.4,0,.5),'ship')
    k.anchor('goodbye_mai',(-3.3,0,-1.8),'ship')
    k.anchor('goodbye_french_kitchenhand',(-5.2,0,1.8),'ship')
    k.anchor('goodbye_two_soldiers',(2.4,0,1.5),'ship')
    k.anchor('quay_step',(0,y,-10.4))
    k.anchor('mai_poverty_conversation',(-3.8,y,-12.7))
    k.anchor('poor_porters',(-8,y,-13.4))
    k.anchor('street_edge',(0,y,-15))
    k.anchor('tram_watch',(0,y,-15.6))
    k.anchor('tram_from',(-36,y,-18.5),'tram')
    k.anchor('tram_to',(36,y,-18.5),'tram')
    k.anchor('cafe_approach',(.5,y+.1,-21.8))
    k.anchor('cafe_seat',(.5,y+.1,-23.15),'seat')
    k.anchor('cafe_table',(.5,y+.88,-24),'object')
    k.anchor('waiter_start',(-1.4,y+.1,-24.6))
    k.anchor('waiter_table',(.25,y+.87,-23.88),'object')
    k.anchor('notebook_link_pages',(.5,y+1.12,-23.15),'object')
    # Low-poly background hill and Romano-Byzantine tower; placement is scenographic.
    hill=Mesh('GardeHill')
    hill.ellipsoid((0,0,0),(320,88,135),'Hill',28,12)
    hill.object(k,pos=(-2,0,-155))
    basilica=Mesh('NotreDameDeLaGarde')
    basilica.box((-6,3.4,0),(20,6.8,9),'Basilica')
    basilica.hip_roof((-6,0,0),21,10,6.8,2.6)
    basilica.box((7,9,0),(6.3,18,6.3),'Basilica')
    for i in range(10):
        basilica.box((7,1.4+i*1.5,0),(6.4,.32,6.4),'QuayStone')
    for xx in (5.35,8.65):
        window(basilica,xx,14.9,3.25,1.3,2.8,True)
    basilica.beam((7,18,0),(7,21.2,0),2.1,'Basilica',16,r2=1.4)
    basilica.ellipsoid((7,23,0),(1.2,3.2,1.05),'StatueGold',10,6)
    basilica.ellipsoid((7,25.1,0),(.6,.7,.6),'StatueGold',10,6)
    basilica.beam((7,23.7,0),(8.5,24.3,0),.14,'StatueGold',8)
    basilica.object(k,pos=(-2,43,-150))
    k.anchor('basilica_silhouette',(-2,43,-150),'background')
    for i in range(30):
        x=-125+i*9
        z=-120-(i%4)*11
        u=((x+2)/160)**2+((z+155)/67.5)**2
        if u>=.96:
            continue
        yy=44*math.sqrt(1-u)-.8
        far=Mesh(f'HillsideHouse_{i}')
        far.box((0,3,0),(8,6,7),'Wall')
        far.hip_roof((0,0,0),8.4,7.4,6,1.5)
        far.object(k,pos=(x,yy,z))
    k.box('HarborWater',(0,-5.62,106),(680,.12,227),'WaterHarbor')
    distant_ship(k,'DistantSteamer_1',(-168,-5.6,93),67)
    distant_ship(k,'DistantSteamer_2',(142,-5.6,68),52)
    for i,p in enumerate(((-101,-5.3,12),(88,-5.3,23),(-151,-5.3,38))):
        boat(k,f'HarborSailboat_{i}',p,10+i,True,False)
    k.path('porters_loop',[(-17,y,-11.1),(-17,y,-14),(-5,y,-14),(-5,y,-11.1)],True,6)
    k.path('arrivals',[(-2,y,-11),(1.4,y,-12.5),(1.4,y,-15)],False,5)
    k.path('promenade',[(-12,y+.1,-21.4),(0,y+.1,-21.4),(13,y+.1,-21.4),(23,y+.1,-21.4)],False,5)
    k.spot('cafe_customers',(5.7,y+.1,-24),4)
    k.spot('poor_workers',(-8,y,-13.4),3)
    k.spot('mai_conversation',(-3.8,y,-12.7),2)
    k.layout['beat_routes']=[{'name':'E3_E4_E5','anchors':['gangway_foot','quay_step','street_edge','tram_watch','cafe_approach']},
                             {'name':'E3b_Mai','anchors':['quay_step','mai_poverty_conversation','street_edge']}]
    k.measure('quay',(148,3.6,31.3),'ship extent plus both ends; solid foundation below waterline')
    k.measure('port_facades',(12,14.45,9),'4-5 floors, 3.5 m pitch, old port photograph proportions')
    k.measure('cafe_pavilion',(12,3.95,9),'single low shop in current builder sightline gap; scenographic, not a known historical address')
    k.measure('tram_track',(148,.034,1.4),'rail centres Z=-17.8/-19.2 from current builder')
    k.measure('cafe_canopy',(9,3.05,3),'E5 current XY/Z composition; pavement lifted 0.1 m')
    k.measure('basilica',(26,25.45,10),'simple background silhouette; not a surveyed replica')
    k.layout['notes']='Dawn 6 July 1911. Joliette cargo and Vieux-Port architectural vocabulary combined for gameplay; actual berth is not asserted. Eleven facades have 4-5 floors; a low cafe pavilion occupies the current builder central sightline gap. Street barrier is absent from art; dynamic tram safety/beat gates remain runtime responsibilities. Ship, gangway, animated tram and NPCs are scene-owned, not baked into FBX. E5 pavement lift of 0.1 m is declared in anchors.'


def local_box_point(point, blocker):
    p = tuple(point[i]-blocker['center'][i] for i in range(3))
    return rotate(p,-blocker['yaw'])


def segment_intersects_blocker(a,b,blocker,clearance=.3):
    p,q=local_box_point(a,blocker),local_box_point(b,blocker)
    if blocker['center'][1]+blocker['size'][1]/2 <= min(a[1],b[1])+.30:
        return False
    if blocker['center'][1]-blocker['size'][1]/2 >= max(a[1],b[1])+1.75:
        return False
    lo,hi=0.,1.
    for ax in (0,2):
        half=blocker['size'][ax]/2+clearance
        delta=q[ax]-p[ax]
        if abs(delta)<1e-10:
            if abs(p[ax])>half:
                return False
        else:
            x1,x2=(-half-p[ax])/delta,(half-p[ax])/delta
            lo=max(lo,min(x1,x2)); hi=min(hi,max(x1,x2))
            if lo>hi:
                return False
    return True


def verify_geometry(k):
    bpy.context.view_layer.update()
    stats={'scene':k.scene,'objects':len(k.objects),'visual_objects':0,'collider_objects':0,
           'triangles':0,'visual_triangles':0,'collider_triangles':0,
           'materials':sorted(k.mats),'material_count':len(k.mats),
           'metres_per_uv':1,'surface_tiles_m':{n:TILES[n] for n in k.mats if n in TILES},
           'linked_meshes':{},'objects_manifest':[], 'checks':{}}
    fingerprint=hashlib.sha256()
    for obj in k.objects:
        obj.data.calc_loop_triangles()
        tris=len(obj.data.loop_triangles)
        col=obj.name.startswith('COL_')
        stats['collider_objects' if col else 'visual_objects']+=1
        stats['collider_triangles' if col else 'visual_triangles']+=tris
        stats['triangles']+=tris
        assert all(p.area>1e-9 for p in obj.data.polygons),f'degenerate face {obj.name}'
        assert len(obj.data.uv_layers)==1,obj.name
        assert all(math.isfinite(c) for v in obj.data.vertices for c in v.co),obj.name
        if col:
            assert len(obj.data.vertices)==8 and len(obj.data.polygons)==6,obj.name
            # Signed volume checks closed box winding, beyond merely normal direction.
            signed=0
            for t in obj.data.loop_triangles:
                a,b,c=[obj.data.vertices[i].co for i in t.vertices]
                signed+=a.dot(b.cross(c))/6
            assert signed>0,f'inverted collision mesh {obj.name}'
        coords=[upos(obj.matrix_world@v.co) for v in obj.data.vertices]
        mn=[min(p[i] for p in coords) for i in range(3)]
        mx=[max(p[i] for p in coords) for i in range(3)]
        stats['objects_manifest'].append({'name':obj.name,'triangles':tris,'min':mn,'max':mx,
                                           'material_slots':[m.name for m in obj.data.materials],
                                           'mesh':obj.data.name})
        fingerprint.update(obj.name.encode())
        for co in coords:
            fingerprint.update((','.join(f'{c:.6f}' for c in co)+';').encode())
        for p in obj.data.polygons:
            fingerprint.update((str(tuple(p.vertices))+str(p.material_index)).encode())
        for uv in obj.data.uv_layers[0].data:
            fingerprint.update((','.join(f'{c:.6f}' for c in uv.uv)+';').encode())
    for kind,mesh in k.cache.items():
        stats['linked_meshes'][kind]=sum(o.data==mesh.data for o in k.objects)
    failures=[]
    def check_seq(name,points):
        for a,b in zip(points,points[1:]):
            for blocker in k.layout['blockers']:
                if segment_intersects_blocker(a,b,blocker):
                    failures.append({'route':name,'blocker':blocker['name'],'segment':[a,b]})
    for path in k.layout['crowd']['paths']:
        points=path['points']+([path['points'][0]] if path['loop'] else [])
        check_seq(path['name'],points)
    for spot in k.layout['crowd']['spots']:
        if 'points' in spot:
            check_seq(spot['name'],spot['points'])
        else:
            # Seated groups occupy furniture on purpose; mobile groups require clearance.
            if 'cafe' not in spot['name']:
                check_seq(spot['name'],[spot['pos'],spot['pos']])
    for route in k.layout['beat_routes']:
        check_seq(route['name'],[k.layout['anchors'][n] for n in route['anchors']])
    k.layout['crowd']['capacity']=sum(p['count'] for p in k.layout['crowd']['paths'])+sum(s['count'] for s in k.layout['crowd']['spots'])
    stats['checks']={'finite_vertices':'PASS','no_zero_area_faces':'PASS','convex_box_colliders':'PASS',
                     'collision_outward_winding':'PASS','metric_uv':'PASS',
                     'route_clearance_m':.30,'route_obstruction_failures':failures,
                     'routes':'PASS' if not failures else 'FAIL', 'crowd_capacity':k.layout['crowd']['capacity']}
    stats['geometry_sha256']=fingerprint.hexdigest()
    return stats


def export(k,d):
    # Same ship flags. Stable metadata/IDs; do not edit the installed addon.
    from io_scene_fbx import export_fbx_bin, fbx_utils
    original=export_fbx_bin.fbx_header_elements
    original_uuid=fbx_utils._key_to_uuid
    def fixed_header(root,data,time=None):
        return original(root,data,datetime.datetime(1911,6,5,12,0,0))
    def stable_uuid(uuids,key):
        assert isinstance(key,(str,int)),('Unexpected FBX ID key',type(key))
        if isinstance(key,int) and 0 <= key < 2**63:
            uid=key
        else:
            uid=int.from_bytes(hashlib.sha256(str(key).encode()).digest()[:8],'big') & (2**63-1)
        assert uid not in uuids,('Stable FBX ID collision',key)
        return fbx_utils.UUID(uid)
    export_fbx_bin.fbx_header_elements=fixed_header
    fbx_utils._key_to_uuid=stable_uuid
    fbx_utils._keys_to_uuids.clear(); fbx_utils._uuids_to_keys.clear()
    try:
        bpy.ops.object.select_all(action='DESELECT')
        k.root.select_set(True)
        for o in k.objects:
            o.select_set(True)
        bpy.context.view_layer.objects.active=k.objects[0]
        bpy.ops.export_scene.fbx(filepath=str(d/(k.name+'.fbx')),
            use_selection=True,object_types={'MESH','EMPTY'},path_mode='RELATIVE',
            embed_textures=False,apply_unit_scale=True,bake_space_transform=True,
            add_leaf_bones=False,axis_forward='-Z',axis_up='Y',use_mesh_modifiers=True)
    finally:
        export_fbx_bin.fbx_header_elements=original
        fbx_utils._key_to_uuid=original_uuid
        fbx_utils._keys_to_uuids.clear(); fbx_utils._uuids_to_keys.clear()


def import_reference_ship(k):
    if 'ship' not in k.layout:
        return
    before=set(bpy.data.objects)
    path=ROOT/'Game/Assets/_Project/Art/Models/Ship/AmiralLatoucheTreville_LOD1.fbx'
    assert path.is_file(),f'Read-only preview ship missing: {path}'
    bpy.ops.import_scene.fbx(filepath=str(path),use_custom_normals=True)
    added=set(bpy.data.objects)-before
    ordered=sorted(added,key=lambda o:o.name)
    roots=[o for o in ordered if o.parent not in added]
    offset=bpos(k.layout['ship']['position'])
    for o in roots:
        o.location+=offset
    for o in ordered:
        o.name='__REF_SHIP_'+o.name
        if o.type=='MESH':
            for i,mat in enumerate(o.data.materials):
                if mat.name.split('.')[0] in TILES:
                    o.data.materials[i]=k.material(mat.name.split('.')[0])
    m=Mesh('__REF_Gangway')
    a=Vector(k.layout['ship']['gangway_top']); b=Vector(k.layout['ship']['gangway_foot'])
    for i in range(24):
        p=a.lerp(b,(i+.5)/24)
        m.box(tuple(p-Vector((0,.035,0))),(1.4,.07,abs(a.z-b.z)/24+.005),'DeckPlanks')
    for side in (-.76,.76):
        off=Vector((side,0,0))
        m.beam(tuple(a+off+Vector((0,.95,0))),tuple(b+off+Vector((0,.95,0))),.035,'Wood',8)
        for i in range(5):
            p=a.lerp(b,i/4)+off
            m.beam(tuple(p),tuple(p+Vector((0,.95,0))),.028,'Wood',8)
    count=len(k.objects)
    m.object(k)
    del k.objects[count:]


def setup_render(k):
    s=bpy.context.scene
    s.render.engine='CYCLES'
    s.cycles.device='CPU'
    s.cycles.samples=12 if k.refinement<2 else 24
    s.cycles.seed=1911
    s.cycles.use_animated_seed=False
    s.cycles.use_adaptive_sampling=False
    s.cycles.use_denoising=True
    s.cycles.max_bounces=4
    s.render.threads_mode='FIXED'; s.render.threads=1
    s.render.resolution_x=960 if k.refinement<2 else 1280
    s.render.resolution_y=540 if k.refinement<2 else 720
    s.render.resolution_percentage=100
    s.render.image_settings.file_format='PNG'
    s.view_settings.view_transform='AgX'
    s.world=bpy.data.worlds.new('__PreviewWorld'); s.world.use_nodes=True
    bg=s.world.node_tree.nodes['Background']
    bg.inputs['Color'].default_value=(.34,.43,.50,1) if k.name!='SaigonStreet' else (.055,.09,.18,1)
    bg.inputs['Strength'].default_value=.8 if k.name!='SaigonStreet' else .60
    s.view_settings.exposure=.6 if k.name!='SaigonStreet' else 1.1
    if k.name!='SaigonStreet':
        sky=s.world.node_tree.nodes.new('ShaderNodeTexSky')
        sky.sky_type='NISHITA'
        sky.sun_elevation=math.radians(55 if k.name=='NhaRong' else 4)
        sky.sun_rotation=math.radians(130)
        sky.sun_disc=False
        s.world.node_tree.links.new(sky.outputs['Color'],bg.inputs['Color'])
        bg.inputs['Strength'].default_value=.42 if k.name=='NhaRong' else .32
    sun=bpy.data.lights.new('__Sun','SUN'); sun.energy=2.5 if k.name=='NhaRong' else (1.1 if k.name=='Marseille' else .13)
    sun.color=(1,.86,.65) if k.name!='SaigonStreet' else (.48,.62,1)
    sun.angle=math.radians(5)
    obj=bpy.data.objects.new('__Sun',sun); k.collection.objects.link(obj)
    obj.rotation_euler=(math.radians(32 if k.name=='NhaRong' else 76),math.radians(-20),math.radians(-35))
    if k.name=='SaigonStreet':
        for x,z in ((8,-6.2),(-13,-6.2),(31,-6.2),(-13,6.2),(8,6.2)):
            light=bpy.data.lights.new('__Electric','POINT'); light.energy=230; light.color=(1,.61,.30); light.shadow_soft_size=.8
            o=bpy.data.objects.new('__Electric',light); k.collection.objects.link(o); o.location=bpos((x,4.8,z))
        area=bpy.data.lights.new('__CafeLight','AREA'); area.energy=330; area.shape='RECTANGLE'; area.size=10; area.size_y=2
        area.color=(1,.63,.31)
        o=bpy.data.objects.new('__CafeLight',area); k.collection.objects.link(o); o.location=bpos((12,2.8,-8.6))
    camera=bpy.data.cameras.new('__Camera'); camera.lens=28
    obj=bpy.data.objects.new('__Camera',camera); k.collection.objects.link(obj); s.camera=obj
    return obj


def render_one(k,d,camera,name,eye,target,ortho=None,lens=28):
    camera.location=bpos(eye)
    camera.rotation_euler=(bpos(target)-camera.location).to_track_quat('-Z','Y').to_euler()
    camera.data.type='ORTHO' if ortho else 'PERSP'
    camera.data.lens=lens
    if ortho:
        camera.data.ortho_scale=ortho
    bpy.context.scene.render.filepath=str(d/name)
    print('RENDER',k.name,name,flush=True)
    bpy.ops.render.render(write_still=True)
    subprocess.run(['/usr/bin/python3',str(Path(__file__).resolve()),'--clean-png',str(d/name)],check=True)


def previews(k,d):
    import_reference_ship(k)
    camera=setup_render(k)
    if k.name=='NhaRong':
        overview=((93,68,88),(-15,-3,-31),None,32)
        spawn=((3,-2.45,-18),(6,1.4,-53),None,23)
        beat=((-7,-2.45,-18),(-1.5,-.3,-9),None,26)
        top=((-15,150,-29),(-15,-4.1,-29),178,28)
    elif k.name=='SaigonStreet':
        overview=((60,52,42),(0,2,0),None,28)
        spawn=((-27,1.77,-7.5),(10,2,-8.1),None,29)
        beat=((4,1.77,-5.8),(12,2.2,-9.4),None,25)
        top=((0,100,0),(0,0,0),100,28)
    else:
        overview=((105,76,91),(-14,10,-53),None,27)
        spawn=((3,1.65,-2),(-2,11,-47),None,22)
        beat=((.5,-.75,-21.8),(3,-1.05,-25.5),None,23)
        top=((-14,150,-21),(-14,-2.5,-21),177,28)
    for filename,args in [('preview_overview.png',overview),('preview_spawn.png',spawn),('preview_beat.png',beat),('preview_top_raw.png',top)]:
        render_one(k,d,camera,filename,args[0],args[1],args[2],args[3])
    s=bpy.context.scene
    width=s.render.resolution_x; height=s.render.resolution_y
    def project(p):
        co=world_to_camera_view(s,camera,bpos(p))
        return [co.x*width,(1-co.y)*height]
    walks=[]
    for region in k.layout['walk_bounds']:
        a,b=region['min'],region['max']
        walks.append([project((x,a[1]+.02,z)) for x,z in ((a[0],a[2]),(b[0],a[2]),(b[0],b[2]),(a[0],b[2]))])
    blocks=[]
    for ob in k.layout['blockers']:
        q=[]
        for x,z in ((-.5,-.5),(.5,-.5),(.5,.5),(-.5,.5)):
            v=rotate((x*ob['size'][0],ob['size'][1]/2,z*ob['size'][2]),ob['yaw'])
            p=[v[i]+ob['center'][i] for i in range(3)]
            q.append(project(p))
        blocks.append(q)
    write_json(d/'map_projection.json',{'walks':walks,'blockers':blocks,
        'paths':[{'points':[project(p) for p in path['points']],'loop':path['loop']} for path in k.layout['crowd']['paths']],
        'anchors':{name:project(p) for name,p in k.layout['anchors'].items()}})
    subprocess.run(['/usr/bin/python3',str(Path(__file__).resolve()),'--map',str(d)],check=True)


def verify_fbx(name):
    d=OUT/name
    expected=json.loads((d/'stats.json').read_text())
    bpy.ops.wm.open_mainfile(filepath=str(d/(name+'.blend')))
    authored={o.name:o for o in bpy.data.objects if o.type=='MESH'}
    # Blender FBX globally reorders slots and remaps each face. Verify the
    # semantic assignment per face, not an authoring-only slot index order.
    face_materials={n:[o.material_slots[p.material_index].material.name for p in o.data.polygons]
                    for n,o in authored.items()}
    face_vertices={n:[sorted(tuple(o.matrix_world@o.data.vertices[i].co)
                            for i in p.vertices) for p in o.data.polygons]
                   for n,o in authored.items()}
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(d/(name+'.fbx')))
    meshes={o.name:o for o in bpy.data.objects if o.type=='MESH'}
    assert set(meshes)=={r['name'] for r in expected['objects_manifest']},'object names after real FBX import'
    for record in expected['objects_manifest']:
        o=meshes[record['name']]
        o.data.calc_loop_triangles()
        assert len(o.data.loop_triangles)==record['triangles'],o.name
        coords=[upos(o.matrix_world@v.co) for v in o.data.vertices]
        for i in range(3):
            assert abs(min(p[i] for p in coords)-record['min'][i])<.003,(o.name,'min',i)
            assert abs(max(p[i] for p in coords)-record['max'][i])<.003,(o.name,'max',i)
        imported_slots=[slot.material.name for slot in o.material_slots]
        assert sorted(imported_slots)==sorted(record['material_slots']), (o.name,imported_slots)
        actual_materials=[o.material_slots[p.material_index].material.name for p in o.data.polygons]
        assert actual_materials==face_materials[o.name],(o.name,'face material remap')
        actual_vertices=[sorted(tuple(o.matrix_world@o.data.vertices[i].co)
                                for i in p.vertices) for p in o.data.polygons]
        assert len(actual_vertices)==len(face_vertices[o.name]),(o.name,'face count')
        for a,b in zip(actual_vertices,face_vertices[o.name]):
            assert len(a)==len(b) and all(min((Vector(p)-Vector(q)).length for q in b)<.003 for p in a), (o.name,'face topology/world positions')
        assert len(o.data.uv_layers)==1,o.name
    result={'status':'PASS','mesh_objects':len(meshes),'triangles':expected['triangles'],
             'units_axes_bounds_tolerance_m':.003,'object_names':'PASS','materials':'PASS','uv':'PASS',
             'every_face_material_assignment':'PASS','every_face_world_vertices':'PASS',
             'colliders':sum(n.startswith('COL_') for n in meshes)}
    write_json(d/'fbx_roundtrip.json',result)
    print('FBX ROUNDTRIP',json.dumps(result),flush=True)


def main(name):
    parser=argparse.ArgumentParser()
    parser.add_argument('--pass',dest='refinement',type=int,choices=(0,1,2),default=2)
    parser.add_argument('--geometry-only',action='store_true')
    parser.add_argument('--verify-fbx',action='store_true')
    args=parser.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
    if args.verify_fbx:
        verify_fbx(name); return
    d=OUT/name if args.refinement==2 else OUT/name/'_iterations'/f'pass_{args.refinement}'
    d.mkdir(parents=True,exist_ok=True)
    k=Kit(name,{'NhaRong':'01_NhaRong','SaigonStreet':'00_Prologue','Marseille':'03_Marseille'}[name],args.refinement)
    bpy.context.scene.unit_settings.system='METRIC'; bpy.context.scene.unit_settings.scale_length=1
    {'NhaRong':nharong_build,'SaigonStreet':saigon_build,'Marseille':marseille_build}[name](k)
    ship_beat_anchors(k)
    stats=verify_geometry(k)
    write_json(d/'layout.json',k.layout); write_json(d/'stats.json',stats)
    print('STATS',name,{key:stats[key] for key in ('objects','triangles','material_count','geometry_sha256')},flush=True)
    if stats['checks']['route_obstruction_failures']:
        print(json.dumps(stats['checks']['route_obstruction_failures'],indent=2),flush=True)
        raise AssertionError('route blocked; stop and inspect stats.json before continuing')
    if args.refinement==2:
        export(k,d)
        # Save only authored model; no ship, camera, preview lights in deliverable.
        bpy.context.preferences.filepaths.save_version=0
        bpy.ops.wm.save_as_mainfile(filepath=str(d/(name+'.blend')),compress=True)
    if not args.geometry_only:
        previews(k,d)
    print('ENVIRONMENT COMPLETE',name,d,flush=True)
