"""Deterministic 1911 Amiral Latouche-Tréville, metres, bow +X / port +Y.

blender -b --factory-startup -P Tools/blender/make_ship.py -- --pass 3
Pass 0 is the draft; passes 1, 2, 3 refine deck hardware, rigging and finish.
python3 Tools/blender/make_ship.py --compare <output-dir>
blender -b --factory-startup -P Tools/blender/make_ship.py -- --verify-fbx
Only this script and Staging/art_hq_fbx/Ship are written. References read-only.
"""
import argparse
from collections import deque
import json
import math
import subprocess
import sys
import struct
import zlib
from pathlib import Path

sys.dont_write_bytecode = True


# Dimensions and historical layout: the annotated photograph's supplied ruler.
ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "Staging/art_hq_fbx/Ship"
REFERENCE = ROOT / "Staging/reference/ship/ANNOTATED_side_photo.png"
LENGTH, BEAM, DRAFT = 124.10, 15.20, 7.0
MAIN_Z, RAISED_HEIGHT = 5.60, 2.50
T1_Z, BOAT_Z = 8.45, 11.35
FORE_END, SUPER_START, SUPER_END, POOP_START = .26, .46, .82, .91
FOREMAST_F, MAINMAST_F, FUNNEL_F = .32, .85, .65
FOREMAST_TOP, MAINMAST_TOP, FUNNEL_TOP = 35.60, 29.60, 21.60
FUNNEL_RADIUS, FUNNEL_RAKE = 1.90, math.radians(3.0)
UV_METRES = 2.0
SIDE_WIDTH, SIDE_HEIGHT, SIDE_SCALE = 2000, 820, 136.0
PREVIEW_WIDTH, PREVIEW_HEIGHT = 1600, 1000
FONT = "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf"
HOST_PYTHON = "/usr/bin/python3"
TAU = math.tau
HULL_WIDTHS = [(0,.035),(.025,.85),(.08,3.40),(.16,6.10),(.25,7.25),
               (.37,7.60),(.60,7.60),(.76,7.25),(.85,6.40),(.92,4.90),
               (.955,4.35),(.975,3.36),(.992,1.92),(.997,1.20),(.999,.69),(1,.035)]
GROUPS = ["Hull", "Forecastle", "ForeWellDeck", "Superstructure", "Bridge",
          "BoatDeck", "Lifeboats", "Funnel", "Foremast", "Mainmast", "Derricks",
          "Rigging", "Railings", "DeckFittings", "Poop", "Rudder", "Propeller",
          "NameText"]
GROUPS += ["GalleyChimney", "HoldCompanion"]
COLORS = {
    "Hull": (.025,.030,.035), "BootTop": (.19,.035,.027),
    "ShipWhite": (.82,.82,.76), "DeckPlanks": (.43,.285,.145),
    "Rail": (.20,.095,.036), "Iron": (.085,.095,.105),
    "Mast": (.57,.39,.18), "Funnel": (.023,.025,.026),
    "FunnelBand": (.90,.90,.85), "FunnelStar": (.022,.055,.20),
    "Canvas": (.265,.30,.23), "Glass": (.018,.040,.050),
    "VentInside": (.31,.045,.030), "NameText": (.95,.94,.87),
}
HATCHES = [(.290,(4.20,4.80,.82)), (.400,(7.20,5.20,.88))]
BOAT_FRACTIONS = [.54,.61,.70,.77]
# Read from Game/.../SceneBuilders/PlayerBuilder.cs; requested nav XY margin stays 0.30.
PLAYER_HEIGHT, PLAYER_STEP = 1.75, .30
INTERIOR_VOLUMES = {
    "galley_block": {"min": [-7.45,-5.18,5.45], "max": [4.92,4.90,8.30],
                     "allowed": "Only the front/starboard shell <= 0.04 m and roof."},
    "hold": {"min": [5.90,-3.20,2.60], "max": [15.40,3.20,5.40],
             "allowed": "The deck underside may serve as the hold ceiling."},
}
GALLEY_TRANSFORM = {"local_axes": ["lx", "ly", "lz"],
    "ship_axes": ["x", "y", "z"], "matrix": [[-1,0,0],[0,0,-1],[0,1,0]],
    "translation": [-.20,-.85,5.60],
    "formula": "x = -lx - 0.20; y = -lz - 0.85; z = ly + 5.60"}
HOLD_APERTURE = ([6.10,-.65,2.59], [8.70,.65,5.61])
BOAT_STAIR_Y = 5.60
BOAT_STAIR_CUTS = [([-1.10,s*BOAT_STAIR_Y-.60,10.95], [2.20,s*BOAT_STAIR_Y+.60,11.36])
                   for s in (-1,1)]
OPENINGS = [
    {"id":"galley_door", "kind":"door", "center":[4.964,-3.45,6.60],
     "size":[.04,.90,2.0], "normal":[1,0,0]},
    {"id":"corridor_starboard_door", "kind":"door", "center":[-6.20,-5.23,6.60],
     "size":[1.0,.04,2.0], "normal":[0,-1,0]},
    {"id":"galley_porthole", "kind":"porthole", "center":[4.964,-1.45,7.15],
     "radius":.25, "outer_radius":.30, "normal":[1,0,0],
     "glass_center":[4.934,-1.45,7.15], "glass_thickness":.008},
    {"id":"hold_companion_door", "kind":"door", "center":[6.0,0,6.60],
     "size":[.04,1.16,2.0], "normal":[-1,0,0]},
    {"id":"hold_deck_opening", "kind":"deck_opening", "center":[7.40,0,5.60],
     "size":[2.60,1.30], "normal":[0,0,1]},
]


def x_at(f):
    return LENGTH * (.5-f)


def fraction(x):
    return .5-x/LENGTH


def smooth(a, b, v):
    t = max(0., min(1., (v-a)/(b-a)))
    return t*t*(3-2*t)


def half_width(f):
    """Monotone Hermite station fairing; preserve the measured 15.2 m beam."""
    f = max(0., min(1., f))
    slopes = [(HULL_WIDTHS[i+1][1]-HULL_WIDTHS[i][1]) /
              (HULL_WIDTHS[i+1][0]-HULL_WIDTHS[i][0])
              for i in range(len(HULL_WIDTHS)-1)]
    for i, (a, b) in enumerate(zip(HULL_WIDTHS, HULL_WIDTHS[1:])):
        if f <= b[0]:
            t, h = (f-a[0])/(b[0]-a[0]), b[0]-a[0]
            m0 = slopes[i] if i == 0 else (0. if slopes[i-1]*slopes[i] <= 0
                 else 2*slopes[i-1]*slopes[i]/(slopes[i-1]+slopes[i]))
            m1 = slopes[i] if i == len(slopes)-1 else (0. if slopes[i]*slopes[i+1] <= 0
                 else 2*slopes[i]*slopes[i+1]/(slopes[i]+slopes[i+1]))
            return ((2*t**3-3*t*t+1)*a[1]+(t**3-2*t*t+t)*h*m0
                    +(-2*t**3+3*t*t)*b[1]+(t**3-t*t)*h*m1)
    return HULL_WIDTHS[-1][1]


def main_z(f):
    return MAIN_Z + .80*max(0., (FORE_END-f)/FORE_END)**2 + \
        .30*max(0., (f-POOP_START)/(1-POOP_START))**2


def raised_z(f):
    return main_z(f)+RAISED_HEIGHT


def compare(directory):
    """Host Python/Pillow: align the supplied bow/stern ruler, no photo tracing."""
    from PIL import Image, ImageDraw, ImageFont
    d = Path(directory)
    projection = json.loads((d/"side_projection.json").read_text())
    left, right = projection["bow_px"], projection["stern_px"]
    span = right-left
    photo = Image.open(REFERENCE).convert("RGB")
    scale = span/(1351-35)  # Supplied red ruler endpoints in the annotated photo.
    photo = photo.resize((round(photo.width*scale),round(photo.height*scale)),
                         Image.Resampling.LANCZOS)
    side = Image.open(d/"check_side.png").convert("RGB")
    font = ImageFont.truetype(FONT, 23)
    panel_y = photo.height+90
    canvas = Image.new("RGB", (SIDE_WIDTH, panel_y+side.height+75), "white")
    canvas.paste(photo,(round(left-35*scale),45))
    canvas.paste(side,(0,panel_y))
    draw = ImageDraw.Draw(canvas)
    draw.text((25,8),"1911 PHOTO — annotated layout authority",font=font,fill="#222222")
    draw.text((25,panel_y-35),"MODEL — same hull length; bow +X / port view",font=font,fill="#222222")
    for water_y in (45+436*scale, panel_y+projection["waterline_px"]):
        draw.line((left,water_y,right,water_y),fill="#00a7df",width=2)
        for i in range(11):
            px = left+span*i/10
            draw.line((px,water_y,px,water_y+155),fill="#ee2020",width=2)
            draw.text((px-15,water_y+158),f"{i/10:.1f}",font=font,fill="#ee2020")
    canvas.save(d/"compare_side.png")
    print("COMPARE", d/"compare_side.png", flush=True)


if "--compare" in sys.argv:
    compare(sys.argv[sys.argv.index("--compare")+1])
    raise SystemExit(0)

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector
from bpy_extras.object_utils import world_to_camera_view

MATERIALS, BUILDERS, OBJECTS = {}, {}, {}
FORE_OBSTACLES, CONNECTIONS = [], []
OBSTACLES = []
EDGES = {}
CONTACT_GEOMETRY = {}
LOD = 0
CHIMNEY_CLEARANCE = True
HULL_PRIMARY_COUNT = 0
HULL_PRIMARY_VERTICES = None
PROP_BLADE_START = 0
PASS = 3


class Mesh:
    """One buffer per required export object; primitives never make loose objects."""
    def __init__(self, name):
        self.name, self.vertices, self.faces, self.mats, self.smooth = name, [], [], [], []

    def vertex(self, p):
        self.vertices.append(tuple(p))
        return len(self.vertices)-1

    def face(self, indices, mat, smooth_face=False):
        clean = []
        for i in indices:
            if not clean or i != clean[-1]:
                clean.append(i)
        if len(clean)>1 and clean[0] == clean[-1]:
            clean.pop()
        if len(set(clean))<3:
            return
        self.faces.append(tuple(clean))
        self.mats.append(list(COLORS).index(mat))
        self.smooth.append(smooth_face)

    def object(self):
        data = bpy.data.meshes.new(self.name)
        data.from_pydata(self.vertices, [], self.faces)
        data.update()
        for mat in MATERIALS.values():
            data.materials.append(mat)
        for p, mat, sm in zip(data.polygons, self.mats, self.smooth):
            p.material_index, p.use_smooth = mat, sm
        bm = bmesh.new()
        bm.from_mesh(data)
        bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
        # Open decorative surfaces need an explicit facing for Unity's backface culling.
        for face in bm.faces:
            mat=list(COLORS)[face.material_index]
            center=face.calc_center_median()
            if mat == 'Canvas' and face.normal.z < 0:
                face.normal_flip()
            if self.name == 'Funnel' and mat == 'FunnelStar' and face.normal.y*center.y < 0:
                face.normal_flip()
            if self.name == 'Superstructure' and mat == 'ShipWhite' and abs(face.normal.x)>.75:
                if abs(center.x-4.964)<.0001 and face.normal.x<0:
                    face.normal_flip()
                elif abs(center.x-4.924)<.0001 and face.normal.x>0:
                    face.normal_flip()
            if self.name == 'Railings' and mat in ('Hull','ShipWhite') and face.calc_area()>.35 and abs(face.normal.y)>.75:
                target=center.y if mat == 'Hull' else -center.y
                if face.normal.y*target < 0:
                    face.normal_flip()
        bmesh.ops.triangulate(bm, faces=list(bm.faces))
        bm.to_mesh(data)
        bm.free()
        data.update()
        uv = data.uv_layers.new(name="UVMap")
        # Real-world box projection. Horizontal planks always have U along X.
        co = np.array([v.co[:] for v in data.vertices],dtype=np.float64)
        loop_verts = np.empty(len(data.loops),dtype=np.int32)
        data.loops.foreach_get("vertex_index",loop_verts)
        normals = np.array([p.normal[:] for p in data.polygons])
        dominant = np.repeat(np.argmax(np.abs(normals),axis=1),3)
        xyz = co[loop_verts]
        u = np.where(dominant == 0, xyz[:,1], xyz[:,0])/UV_METRES
        v = np.where(dominant == 2, xyz[:,1], xyz[:,2])/UV_METRES
        uv.data.foreach_set("uv", np.stack((u,v),axis=1).astype(np.float32).ravel())
        obj = bpy.data.objects.new(self.name,data)
        bpy.context.collection.objects.link(obj)
        obj.parent = bpy.data.objects["AmiralLatoucheTreville"]
        OBJECTS[self.name] = obj
        return obj


def box(m, center, size, mat="Iron", bevel=0., rotation=None):
    """Chamfered box, explicit triangles at corners; no bevel modifiers to bake."""
    if LOD:
        if max(size) < .15:
            return
        bevel = 0.
    c, h = Vector(center), [s/2 for s in size]
    r = rotation or Matrix.Identity(3)
    if bevel <= 0:
        verts = [m.vertex(c+r@Vector((sx*h[0],sy*h[1],sz*h[2])))
                 for sx in (-1,1) for sy in (-1,1) for sz in (-1,1)]
        for f in [(0,1,3,2),(4,6,7,5),(0,4,5,1),(2,3,7,6),(0,2,6,4),(1,5,7,3)]:
            m.face([verts[i] for i in f],mat)
        return
    b = min(bevel, min(h)*.8)
    ids = {}
    corners = [(sx,sy,sz) for sx in (-1,1) for sy in (-1,1) for sz in (-1,1)]
    for signs in corners:
        for axis in range(3):
            p = [signs[i]*(h[i] if i == axis else h[i]-b) for i in range(3)]
            ids[signs,axis] = m.vertex(c+r@Vector(p))
    for axis in range(3):
        others = [i for i in range(3) if i != axis]
        for sign in (-1,1):
            face = []
            for a,d in [(-1,-1),(-1,1),(1,1),(1,-1)]:
                s = [0,0,0]
                s[axis],s[others[0]],s[others[1]] = sign,a,d
                face.append(ids[tuple(s),axis])
            m.face(face,mat)
    for direction in range(3):
        axes = [i for i in range(3) if i != direction]
        for a,d in [(-1,-1),(-1,1),(1,-1),(1,1)]:
            s0,s1 = [0,0,0],[0,0,0]
            s0[axes[0]]=s1[axes[0]]=a
            s0[axes[1]]=s1[axes[1]]=d
            s0[direction],s1[direction] = -1,1
            p,q = tuple(s0),tuple(s1)
            m.face((ids[p,axes[0]],ids[q,axes[0]],ids[q,axes[1]],ids[p,axes[1]]),mat)
    for s in corners:
        m.face([ids[s,i] for i in range(3)],mat)


def tube(m, points, radius, mat="Iron", sides=12, caps=True,reduce_path=True):
    if LOD and m.name == "Rigging" and max(radius if isinstance(radius,(tuple,list,np.ndarray)) else [radius]) < .075:
        return
    if LOD:
        sides = max(6, round(sides * (.85 if LOD == 1 else .40)))
        if len(points) > 6 and reduce_path:
            indices = sorted(set(range(0,len(points),2 if LOD == 1 else 4)) | {len(points)-1})
            points = [points[i] for i in indices]
            if isinstance(radius,(tuple,list,np.ndarray)):
                radius = [radius[i] for i in indices]
    points = [Vector(p) for p in points]
    radii = radius if isinstance(radius,(tuple,list,np.ndarray)) else [radius]*len(points)
    rings, normal = [], None
    for i,p in enumerate(points):
        tangent = (points[min(i+1,len(points)-1)]-points[max(0,i-1)]).normalized()
        if normal is None:
            normal = tangent.cross(Vector((0,0,1)))
            if normal.length < .05:
                normal = tangent.cross(Vector((0,1,0)))
        normal = (normal-tangent*normal.dot(tangent)).normalized()
        binormal = tangent.cross(normal).normalized()
        rings.append([m.vertex(p+float(radii[i])*(normal*math.cos(TAU*j/sides)+
                              binormal*math.sin(TAU*j/sides))) for j in range(sides)])
    for i in range(len(rings)-1):
        for j in range(sides):
            k = (j+1)%sides
            m.face((rings[i][j],rings[i][k],rings[i+1][k],rings[i+1][j]),mat,True)
    if caps:
        for i in (0,len(rings)-1):
            center = m.vertex(points[i])
            for j in range(sides):
                m.face((center,rings[i][j],rings[i][(j+1)%sides]),mat)


def rod(m,a,b,r,mat="Iron",sides=12):
    tube(m,[a,b],r,mat,sides)


def torus(m, center, radius, thickness, mat="Iron", normal=(0,0,1), steps=24, sides=8):
    protected=m.name=='Superstructure' and abs(center[0]-4.974)<.001 and abs(center[1]+1.45)<.001
    if LOD == 2 and radius < .45 and not protected:
        return
    if LOD and not protected:
        steps = max(8,round(steps * (.375 if LOD == 1 else .10)))
    n = Vector(normal).normalized()
    u = n.cross(Vector((0,0,1)))
    if u.length < .1:
        u = n.cross(Vector((0,1,0)))
    u.normalize()
    v = n.cross(u)
    c = Vector(center)
    points = [c+radius*(u*math.cos(TAU*j/steps)+v*math.sin(TAU*j/steps))
              for j in range(steps+1)]
    # Sample closed circles once; striding the path again can leave two segments.
    tube(m,points,thickness,mat,sides,caps=False,reduce_path=False)


def beam(m,a,b,width,height,mat="Iron",bevel=.015):
    a,b = Vector(a),Vector(b)
    r = Vector((1,0,0)).rotation_difference((b-a).normalized()).to_matrix()
    box(m,(a+b)/2,((b-a).length,width,height),mat,bevel,r)


def bounds_vertices(vertices):
    arr = np.asarray(vertices)
    lo,hi = arr.min(axis=0),arr.max(axis=0)
    return {"center": ((lo+hi)/2).round(5).tolist(),
            "size": (hi-lo).round(5).tolist()}


def obstacle(m,start,identifier,kind,zone=None,yaw=0.,nav_blocking=True,vertices=None):
    if LOD or (vertices is None and len(m.vertices) == start):
        return
    vertices = np.asarray(m.vertices[start:] if vertices is None else vertices)
    angle = math.radians(yaw)
    rotation = np.array([[math.cos(angle),-math.sin(angle),0],
                         [math.sin(angle), math.cos(angle),0],[0,0,1]])
    data = bounds_vertices(vertices @ rotation)
    data['center'] = (np.asarray(data['center']) @ rotation.T).round(5).tolist()
    x,y,z = data['center']
    f = fraction(x)
    zone = zone or ('forecastle' if f < FORE_END else 'poop' if f >= POOP_START
                   else 'aft_well' if f >= SUPER_END else 'fore_well' if f < SUPER_START
                   else 'boat_deck' if z > 10.90 else 'superstructure_t2' if z > 8.0
                   else 'superstructure_t1_promenade')
    item = {'id':identifier or f'{zone}_{kind}_{x:.3f}_{y:.3f}', 'kind':kind,
            'shape':'box', **data, 'yaw_deg':yaw, 'zone':zone,
            'object':m.name, 'nav_blocking':nav_blocking}
    OBSTACLES.append(item)
    if (identifier and (identifier.startswith('aft_cowl_') or identifier.startswith('poop_stairs_'))) or (kind=='hatch_coaming' and zone=='aft_well'):
        end=len(m.vertices)
        CONTACT_GEOMETRY[item['id']]=(m.vertices[start:end],
            [tuple(i-start for i in face) for face in m.faces if min(face)>=start and max(face)<end])
    if zone == 'fore_well':
        FORE_OBSTACLES.append(item)


def boom_colliders(m,start,sides,a,b,identifier,zone):
    if LOD:
        return
    # These are interpolated sections of the actual conical mesh, not a tall
    # envelope projecting its overhead tip onto the entire deck.
    first=np.asarray(m.vertices[start:start+sides])
    last=np.asarray(m.vertices[start+sides:start+2*sides])
    count=math.ceil((Vector(b)-Vector(a)).length/.75)
    yaw=math.degrees(math.atan2(b[1]-a[1],b[0]-a[0]))
    for i in range(count):
        sections=np.concatenate([first+(last-first)*t for t in (i/count,(i+1)/count)])
        obstacle(m,start,f'{identifier}_{i:02d}','derrick_boom',zone,yaw,vertices=sections)


def materials():
    for name,color in COLORS.items():
        mat = bpy.data.materials.new(name)
        mat.diffuse_color = (*color,1)
        mat.use_nodes = True
        bsdf = mat.node_tree.nodes.get("Principled BSDF")
        bsdf.inputs["Base Color"].default_value = (*color,1)
        bsdf.inputs["Roughness"].default_value = .32 if name == "Glass" else .62
        bsdf.inputs["Metallic"].default_value = .45 if name == "Iron" else 0.
        if name in ("DeckPlanks","Rail"):
            # Preview grain only. FBX exposes the plain base color for Unity PBR replacement.
            nodes,links = mat.node_tree.nodes,mat.node_tree.links
            tex = nodes.new("ShaderNodeTexCoord")
            mapping = nodes.new("ShaderNodeVectorMath")
            mapping.operation = "MULTIPLY"
            mapping.inputs[1].default_value = (.8,85,4)
            noise = nodes.new("ShaderNodeTexNoise")
            noise.inputs["Scale"].default_value = 1.0
            noise.inputs["Detail"].default_value = 2.0
            ramp = nodes.new("ShaderNodeValToRGB")
            ramp.color_ramp.elements[0].position = .18
            ramp.color_ramp.elements[0].color = (*(c*.66 for c in color),1)
            ramp.color_ramp.elements[1].position = .8
            ramp.color_ramp.elements[1].color = (*(min(1,c*1.16) for c in color),1)
            links.new(tex.outputs["UV"],mapping.inputs[0])
            links.new(mapping.outputs[0],noise.inputs["Vector"])
            links.new(noise.outputs["Fac"],ramp.inputs[0])
            links.new(ramp.outputs[0],bsdf.inputs["Base Color"])
        MATERIALS[name] = mat


def hull():
    global HULL_PRIMARY_COUNT, HULL_PRIMARY_VERTICES
    m = BUILDERS["Hull"]
    count = (208 if PASS >= 3 else 156) if LOD == 0 else (144 if LOD == 1 else 72)
    fs = sorted(set(np.linspace(0,1,count+1).tolist()+[f for f,w in HULL_WIDTHS]))
    sections = []
    for f in fs:
        # Steel closure lies underneath the timber wearing surface.
        top = main_z(f)-.045
        # Vertical topsides, a rounded bilge, and a tucked-under counter stern.
        zs = [-7,-6.85,-6.5,-6,-5.4,-4.7,-4,-3,-2,0,.20,1.5,3,4.3,top]
        widths = [.018,.24,.44,.63,.76,.85,.915,.956,.985,.995,1,1,1,1,1]
        ring = []
        for side,indices in ((1,range(len(zs))),(-1,range(len(zs)-1,-1,-1))):
            for i in indices:
                z = zs[i]
                shift = 6.2*smooth(.80,1,f)*((top-z)/(top+DRAFT))**1.1
                shift -= 4.0*(1-smooth(0,.13,f))*max(0,-z/DRAFT)**1.7
                ring.append(m.vertex((x_at(f)+shift,side*half_width(f)*widths[i],z)))
        sections.append(ring)
    n = len(sections[0])
    for i in range(len(sections)-1):
        for j in range(n):
            k = (j+1)%n
            ids = (sections[i][j],sections[i+1][j],sections[i+1][k],sections[i][k])
            z = sum(m.vertices[v][2] for v in ids)/4
            m.face(ids,"BootTop" if z < .20 else "Hull",True)
    for ring in (sections[0],sections[-1]):
        # Close each corresponding port/starboard level. A centre fan would cut
        # across the counter's curved X/Z profile and produce folded end faces.
        levels=n//2
        for j in range(levels-1):
            ids=(ring[j],ring[j+1],ring[n-2-j],ring[n-1-j])
            z=sum(m.vertices[v][2] for v in ids)/4
            m.face(ids,"BootTop" if z < .2 else "Hull",True)
    HULL_PRIMARY_COUNT = len(m.vertices)
    HULL_PRIMARY_VERTICES = np.asarray(m.vertices).copy()
    # Subtle plating seams; no texture baking or commercial model geometry.
    for side in (() if LOD else (-1,1)):
        for z in (1.45,3.0,4.5):
            points = [(x_at(f),side*(half_width(f)+.014),z)
                      for f in np.linspace(.035,.965,130)]
            tube(m,points,.011,"Hull",6)


def deck(m, fa, fb, height, inset=.18, planks=True, width_limit=None, body=False):
    """Fair deck outline plus fore-aft physical planks clipped to that outline."""
    zfun = height if callable(height) else lambda f: height
    wf = lambda f: max(.018,min(half_width(f)-inset,width_limit or 100))
    fs = np.linspace(fa,fb,51)
    rings = []
    for f in fs:
        top = zfun(f)-(.036 if planks and LOD != 2 else 0.)
        bottom = main_z(f)-.08 if body else top-.10
        rings.append([m.vertex((x_at(f),y,z)) for y,z in
                      [(-wf(f),bottom),(wf(f),bottom),(wf(f),top),(-wf(f),top)]])
    for i in range(len(rings)-1):
        for j in range(4):
            k=(j+1)%4
            m.face((rings[i][j],rings[i+1][j],rings[i+1][k],rings[i][k]),
                   "DeckPlanks" if j == 2 else ("Hull" if body else "ShipWhite"))
    for ring in (rings[0],rings[-1]):
        m.face(ring,"ShipWhite" if body else "DeckPlanks")
    if not planks or LOD == 2:
        return
    maxw = max(wf(f) for f in fs)
    plank_width = .205
    rows = int(2*maxw/plank_width)
    for row in range(rows):
        y = (row-(rows-1)/2)*plank_width
        samples = [f for f in fs if wf(f) >= abs(y)+plank_width/2]
        if len(samples) < 2:
            continue
        a,b = samples[0],samples[-1]
        # Find the actual clipped end rather than leave stair-stepped plank tips.
        for edge in ("a","b"):
            lo,hi = (max(fa,a-(fb-fa)/50),a) if edge == "a" else (b,min(fb,b+(fb-fa)/50))
            for _ in range(15):
                mid=(lo+hi)/2
                inside=wf(mid)>=abs(y)+plank_width/2
                if inside == (edge == "a"):
                    hi=mid
                else:
                    lo=mid
            if edge == "a":
                a=hi
            else:
                b=lo
        xa,xb = x_at(b),x_at(a)
        p = xa
        first = 1.8+(row%3)*.75
        while p < xb-.04:
            q = min(xb,p+(first if p == xa else 3.8))
            za,zb = zfun(fraction(p)),zfun(fraction(q))
            r = Vector((1,0,0)).rotation_difference(Vector((q-p,0,zb-za)).normalized()).to_matrix()
            box(m,((p+q)/2,y,(za+zb)/2-.014),
                (math.hypot(q-p,zb-za)-.003,plank_width-.004,.028),
                "DeckPlanks",.002 if PASS >= 1 else 0,r)
            p=q


def porthole(m, x, y, z, side, radius=.16, white=False):
    start=len(m.vertices)
    rod(m,(x,y-side*.013,z),(x,y+side*.015,z),radius,"Glass",24)
    if LOD == 2:
        return
    torus(m,(x,y+side*.022,z),radius+.021,.025,
          "ShipWhite" if white else "Iron",(0,1,0),24,8)
    if PASS >= 3 and not LOD:
        for angle in (0,math.pi):
            rod(m,(x+(radius+.01)*math.cos(angle),y,z+(radius+.01)*math.sin(angle)),
                (x+(radius+.01)*math.cos(angle),y+side*.053,z+(radius+.01)*math.sin(angle)),
                .022,"Iron",8)
    if m.name == 'Poop':
        obstacle(m,start,f'poop_porthole_{x:.3f}_{side}','window_frame','poop')


def window(m, center, size, front=False):
    start = len(m.vertices)
    x,y,z = center
    sx,sz = size
    if front:
        box(m,(x,y,z),(.035,sx,sz),"Glass",.008)
        for yy in (y-sx/2-.035,y+sx/2+.035):
            box(m,(x+.023,yy,z),(.06,.065,sz+.13),"ShipWhite",.009)
        for zz in (z-sz/2-.035,z+sz/2+.035):
            box(m,(x+.025,y,zz),(.06,sx+.13,.065),"ShipWhite",.009)
    else:
        box(m,(x,y,z),(sx,.035,sz),"Glass",.008)
        for xx in (x-sx/2-.035,x+sx/2+.035):
            box(m,(xx,y,z),(.065,.055,sz+.13),"ShipWhite",.009)
        for zz in (z-sz/2-.035,z+sz/2+.035):
            box(m,(x,y,zz),(sx+.13,.055,.065),"ShipWhite",.009)
    obstacle(m,start,None,'window_frame')


def clip_halfspace(points, axis, value, less):
    """Convex polygon clipping; boundary shared exactly by both halfspaces."""
    result = []
    for a,b in zip(points,points[1:]+points[:1]):
        da,db = a[axis]-value,b[axis]-value
        ia,ib = (da <= 0,db <= 0) if less else (da >= 0,db >= 0)
        if ia:
            result.append(a)
        if ia != ib:
            t = da/(da-db)
            p = tuple(a[j]+t*(b[j]-a[j]) for j in range(3))
            result.append(p)
    clean = []
    for p in result:
        if not clean or sum((a-b)**2 for a,b in zip(p,clean[-1])) > 1e-18:
            clean.append(p)
    if len(clean)>1 and sum((a-b)**2 for a,b in zip(clean[0],clean[-1])) < 1e-18:
        clean.pop()
    return clean


def polygon_area_3d(points):
    if len(points) < 3:
        return 0.
    origin = np.asarray(points[0])
    return float(np.linalg.norm(sum((np.cross(np.asarray(a)-origin,np.asarray(b)-origin)
                    for a,b in zip(points[1:-1],points[2:])),np.zeros(3)))/2)


def box_difference(points, lo, hi):
    """Return disjoint outside faces; never a face bridging through the box."""
    inside, outside = points, []
    for axis in range(3):
        for value,keep_less in ((lo[axis],False),(hi[axis],True)):
            part = clip_halfspace(inside,axis,value,not keep_less)
            if polygon_area_3d(part)>1e-10:
                outside.append(part)
            inside = clip_halfspace(inside,axis,value,keep_less)
            if len(inside)<3:
                return outside
    return outside


def clear_mesh_volumes(m):
    # 0.1 mm outside the reserved box prevents float32 round-off entering it.
    cutters = [([q-.0001 for q in v['min']],[q+.0001 for q in v['max']])
               for v in INTERIOR_VOLUMES.values()]
    cutters.append(HOLD_APERTURE)
    if m.name in ('BoatDeck','Bridge'):
        cutters += BOAT_STAIR_CUTS
    if CHIMNEY_CLEARANCE and m.name == 'Bridge':
        cutters.append(([2.12,-3.88,11.05],[2.68,-3.32,11.90]))
    for lo,hi in cutters:
        co = np.asarray(m.vertices)
        changed = False
        faces,mats,smooth_faces = [],[],[]
        for indices,mat,sm in zip(m.faces,m.mats,m.smooth):
            points = co[list(indices)]
            if np.any(points.max(axis=0)<=np.asarray(lo)+1e-7) or np.any(points.min(axis=0)>=np.asarray(hi)-1e-7):
                faces.append(indices); mats.append(mat); smooth_faces.append(sm)
                continue
            changed = True
            for part in box_difference([tuple(p) for p in points],lo,hi):
                faces.append(tuple(m.vertex(p) for p in part))
                mats.append(mat); smooth_faces.append(sm)
        if changed:
            m.faces,m.mats,m.smooth = faces,mats,smooth_faces
    # Remove orphan vertices too: the exported vertex audit includes every vertex.
    used = sorted({i for face in m.faces for i in face})
    remap = {old:new for new,old in enumerate(used)}
    m.vertices = [m.vertices[i] for i in used]
    m.faces = [tuple(remap[i] for i in face) for face in m.faces]


def shell_wall(m, axis, outer, inward, u_range, z_range, doors=(), circle=None, zone=None):
    """Four-centimetre wall, partitioned around real apertures without booleans."""
    us = sorted(set(u_range) | {u for door in doors for u in door[:2]})
    zs = sorted(set(z_range) | {z for door in doors for z in door[2:]})
    if circle:
        u,z,r = circle
        us = sorted(set(us) | {u-r,u+r})
        zs = sorted(set(zs) | {z-r,z+r})
    def point(depth,u,z):
        return (depth,u,z) if axis == 0 else (u,depth,z)
    for a,b in zip(us,us[1:]):
        for c,e in zip(zs,zs[1:]):
            u,z = (a+b)/2,(c+e)/2
            if any(q[0]<u<q[1] and q[2]<z<q[3] for q in doors):
                continue
            if circle and abs(u-circle[0])<circle[2] and abs(z-circle[1])<circle[2]:
                continue
            start = len(m.vertices)
            size = [.04,b-a,e-c] if axis == 0 else [b-a,.04,e-c]
            box(m,point(outer+inward*.02,u,z),size,'ShipWhite')
            obstacle(m,start,f'{m.name}_wall_{axis}_{outer:.3f}_{a:.3f}_{c:.3f}',
                     'deckhouse_wall',zone)
    if circle:
        u,z,r = circle
        steps = 48 if LOD == 0 else 32 if LOD == 1 else 24
        rings = []
        for depth in (outer,outer+inward*.04):
            inner,outer_ring = [],[]
            for j in range(steps):
                a = TAU*j/steps
                cu,cz = math.cos(a),math.sin(a)
                square_r = r/max(abs(cu),abs(cz))
                inner.append(m.vertex(point(depth,u+r*cu,z+r*cz)))
                outer_ring.append(inner[-1] if abs(square_r-r)<1e-10 else
                                  m.vertex(point(depth,u+square_r*cu,z+square_r*cz)))
            rings.append((inner,outer_ring))
        for j in range(steps):
            k = (j+1)%steps
            for inner,outer_ring in rings:
                m.face((inner[j],inner[k],outer_ring[k],outer_ring[j]),'ShipWhite')
            m.face((rings[0][0][j],rings[1][0][j],rings[1][0][k],rings[0][0][k]),'ShipWhite')
    for a,b,c,e in doors:
        # Frame is outside the wall; nothing shrinks the specified aperture.
        for u in (a-.04,b+.04):
            start = len(m.vertices)
            box(m,point(outer-inward*.025,u,(c+e)/2),
                (.05,.08,e-c+.08) if axis == 0 else (.08,.05,e-c+.08),'Iron')
            obstacle(m,start,f'{m.name}_door_jamb_{axis}_{outer:.3f}_{u:.3f}','door_frame',zone)
        start = len(m.vertices)
        box(m,point(outer-inward*.025,(a+b)/2,e+.04),
            (.05,b-a+.16,.08) if axis == 0 else (b-a+.16,.05,.08),'Iron')
        obstacle(m,start,f'{m.name}_door_header_{axis}_{outer:.3f}_{a:.3f}','door_frame',zone)


def houses():
    m = BUILDERS["Superstructure"]
    deck(m,SUPER_START,SUPER_END,MAIN_Z,width_limit=7.15)
    shell_wall(m,0,x_at(SUPER_START),-1,(-5.23,5.23),(MAIN_Z,8.39),
               [(-3.90,-3.00,MAIN_Z,7.60)],(-1.45,7.15,.25),'superstructure_t1_promenade')
    shell_wall(m,0,x_at(.815),1,(-5.23,5.23),(MAIN_Z,8.39),zone='superstructure_t1_promenade')
    shell_wall(m,1,-5.23,1,(x_at(.815),x_at(SUPER_START)),(MAIN_Z,8.39),
               [(-6.70,-5.70,MAIN_Z,7.60)],zone='superstructure_t1_promenade')
    shell_wall(m,1,5.23,-1,(x_at(.815),x_at(SUPER_START)),(MAIN_Z,8.39),zone='superstructure_t1_promenade')
    start = len(m.vertices)
    torus(m,(4.974,-1.45,7.15),.275,.025,'Iron',(1,0,0),48,12)
    rod(m,(4.930,-1.45,7.15),(4.938,-1.45,7.15),.25,'Glass',48)
    obstacle(m,start,'galley_porthole_glazing','glazing','superstructure_t1_promenade')
    for fa,fb,z,roof,w in [(.46,.815,MAIN_Z,T1_Z,10.46),(.485,.792,T1_Z,BOAT_Z,9.2)]:
        xa,xb=x_at(fa),x_at(fb)
        if z == T1_Z:
            start = len(m.vertices)
            box(m,((xa+xb)/2,0,(z+roof)/2),(xa-xb,w,roof-z-.12),"ShipWhite",.075)
            obstacle(m,start,'tier2_deckhouse','deckhouse','superstructure_t2')
        for side in (-1,1):
            for f in np.linspace(fa+.014,fb-.014,19):
                if z == MAIN_Z and side == -1 and x_at(f)-.46 < -5.70 and x_at(f)+.46 > -6.70:
                    continue
                window(m,(x_at(f),side*(w/2+.028),z+1.56),(.77,1.03))
            for f in (fa+.012,(fa+fb)/2,fb-.012):
                x=x_at(f)
                start = len(m.vertices)
                box(m,(x,side*(w/2+.07),z+1.05),(1.02,.055,2.10),"ShipWhite",.035)
                window(m,(x,side*(w/2+.11),z+1.60),(.61,.58))
                rod(m,(x-.35,side*(w/2+.14),z+.94),(x-.35,side*(w/2+.25),z+.94),.033)
                obstacle(m,start,f'old_door_fittings_{z:.2f}_{f:.5f}_{side}','door_fittings')
        for y in np.linspace(-w/2+.6,w/2-.6,8):
            if z == MAIN_Z and y+.45 >= -4.0 and y-.45 <= 2.3:
                continue
            window(m,(xa+.015,y,z+1.65),(.76,.94),True)
    deck(m,SUPER_START,SUPER_END,T1_Z,width_limit=7.15)
    deck(BUILDERS["BoatDeck"],.48,.80,BOAT_Z,width_limit=7.0)
    # Two unmistakable full-height white stanchion rows at each promenade level.
    for fa,fb,z,top in [(.46,.82,MAIN_Z,T1_Z),(.48,.80,T1_Z,BOAT_Z)]:
        for side in (-1,1):
            for f in np.linspace(fa,fb,23):
                y=side*min(half_width(f)-.40,6.92)
                start = len(m.vertices)
                rod(m,(x_at(f),y,z),(x_at(f),y,top-.05),.054,"ShipWhite",12)
                if PASS >= 2:
                    box(m,(x_at(f),y,top-.18),(.28,.12,.23),"ShipWhite",.018)
                obstacle(m,start,f'promenade_column_{z:.2f}_{f:.5f}_{side}','stanchion',
                         'superstructure_t1_promenade' if z == MAIN_Z else 'superstructure_t2')
            points=[(x_at(f),side*min(half_width(f)-.40,6.92),top-.10)
                    for f in np.linspace(fa,fb,90)]
            tube(m,points,.090,"ShipWhite",12)
    b=BUILDERS["Bridge"]
    start = len(b.vertices)
    box(b,(x_at(.482),0,12.52),(4.95,8.60,2.34),"ShipWhite",.09)
    obstacle(b,start,'bridge_house','deckhouse','boat_deck')
    deck(b,.461,.504,BOAT_Z,width_limit=7.36)
    box(b,(x_at(.48),0,13.87),(6.0,10.3,.21),"ShipWhite",.065)
    for y in np.linspace(-3.72,3.72,8):
        window(b,(x_at(.482)+2.49,y,12.95),(.83,1.13),True)
    for side in (-1,1):
        for x in np.linspace(x_at(.482)-1.74,x_at(.482)+1.73,4):
            window(b,(x,side*4.325,12.96),(.72,1.12))
        for f in (.464,.50):
            start = len(b.vertices)
            rod(b,(x_at(f),side*6.8,BOAT_Z),(x_at(f),side*6.8,13.62),.055,"ShipWhite",12)
            obstacle(b,start,f'bridge_wing_column_{side}_{f:.3f}','stanchion','boat_deck')
        box(b,(x_at(.482),side*6.2,13.77),(5.65,2.5,.13),"ShipWhite",.025)
    # Wheel, binnacle and telegraph on the open bridge wings.
    fittings=BUILDERS["DeckFittings"]
    for side in (-1,1):
        start = len(fittings.vertices)
        rod(fittings,(x_at(.475),side*5.3,BOAT_Z),(x_at(.475),side*5.3,BOAT_Z+.93),.15,"Iron",20)
        torus(fittings,(x_at(.475),side*5.3,BOAT_Z+1.02),.29,.035,"Rail",(1,0,0),28,8)
        obstacle(fittings,start,f'bridge_wheel_{side}','bridge_controls','boat_deck')
        start = len(fittings.vertices)
        box(fittings,(x_at(.495),side*5.4,BOAT_Z+.47),(.42,.43,.94),"Rail",.025)
        obstacle(fittings,start,f'bridge_binnacle_{side}','bridge_controls','boat_deck')


def hatch(m,f,size,identifier=None,z=MAIN_Z):
    start=len(m.vertices)
    x=x_at(f)
    length,width,height=size
    # Individual wooden coaming beams, metal dogs and cambered canvas cover.
    box(m,(x,0,z+.19),(length,width,.38),"Iron",.035)
    for side in (-1,1):
        box(m,(x,side*(width/2-.12),z+height/2),(length,.24,height),"Rail",.035)
        box(m,(x+side*(length/2-.12),0,z+height/2),(.24,width,height),"Rail",.035)
    rows=[]
    longitudinal,cross=(4,4) if LOD == 2 else (20,12)
    for i in range(longitudinal+1):
        xx=x-length/2+.10+(length-.20)*i/longitudinal
        rows.append([m.vertex((xx,yy,z+height+.11*(1-(2*yy/(width-.15))**2)+
                               .018*math.cos(i*1.7*20/longitudinal)))
                     for yy in np.linspace(-width/2+.06,width/2-.06,cross+1)])
    for a,b in zip(rows,rows[1:]):
        for j in range(cross):
            m.face((a[j],b[j],b[j+1],a[j+1]),"Canvas",True)
    for side in (() if LOD == 2 else (-1,1)):
        for xx in np.linspace(x-length/2+.2,x+length/2-.2,max(8,int(length*2))):
            box(m,(xx,side*(width/2+.025),z+height*.55),(.055,.07,height*.85),"Iron",.009)
            if PASS >= 1:
                tube(m,[(xx,side*(width/2+.06),z+.18),(xx,side*(width/2+.05),z+height+.03),
                        (xx,side*(width/2-.22),z+height+.09)],.018,"Rail",6)
                box(m,(xx,side*(width/2+.065),z+.26),(.15,.08,.13),"Iron",.018)
    obstacle(m,start,identifier,"hatch_coaming")


def gear(m,center,r,normal=(0,1,0)):
    c=Vector(center)
    n=Vector(normal)
    u=Vector((1,0,0))
    v=n.cross(u)
    rod(m,c-n*.06,c+n*.06,r,"Iron",32)
    for i in (() if LOD else range(24)):
        angle=TAU*i/24
        direction=u*math.cos(angle)+v*math.sin(angle)
        tangent=-u*math.sin(angle)+v*math.cos(angle)
        rot=Matrix((direction,n,tangent)).transposed()
        box(m,c+direction*(r+.03),(.11,.14,.095),"Iron",.013,rot)


def winch(m,x,y,z,identifier=None,large=False):
    start=len(m.vertices)
    scale=1.15 if large else 1.0
    length=2.0*scale
    box(m,(x,y,z+.13),(length,1.25*scale,.26),"Iron",.05)
    for dx in (-.65,.65):
        box(m,(x+dx*scale,y,z+.55),(.20,.90,.87),"Iron",.04)
    rod(m,(x-length/2,y,z+.86),(x+length/2,y,z+.86),.12,"Iron",24)
    for dx in (-.47,.47):
        a,b=x+(dx-.30)*scale,x+(dx+.30)*scale
        rod(m,(a,y,z+.87),(b,y,z+.87),.30*scale,"Iron",32)
        for xx in (a,b):
            rod(m,(xx-.045,y,z+.87),(xx+.045,y,z+.87),.43*scale,"Iron",32)
        if PASS >= 1 and LOD == 0:
            for xx in np.linspace(a+.035,b-.035,15):
                torus(m,(xx,y,z+.87),.311*scale,.015,"Rail",(1,0,0),20,6)
    box(m,(x,y-.43*scale,z+.55),(.82,.37,.57),"Iron",.055)
    rod(m,(x-.37,y-.44*scale,z+.57),(x+.37,y-.44*scale,z+.57),.19,"Iron",24)
    if PASS >= 1 and LOD != 2:
        gear(m,(x+.50*scale,y-.48*scale,z+.69),.31)
        torus(m,(x-.62*scale,y-.70*scale,z+.73),.23,.028,"Iron",(0,1,0),24,8)
        for i in range(5):
            a=TAU*i/5
            rod(m,(x-.62*scale,y-.7*scale,z+.73),
                (x-.62*scale+.22*math.cos(a),y-.7*scale,z+.73+.22*math.sin(a)),.018)
        tube(m,[(x+.2,y-.6,z+.35),(x+.2,y-.65,z+.75),(x+.52,y-.65,z+.75)],.038,"Iron",12)
        for dx in (-.82,.82):
            for dy in (-.47,.47):
                rod(m,(x+dx*scale,y+dy*scale,z+.25),(x+dx*scale,y+dy*scale,z+.30),.055,"Iron",6)
    obstacle(m,start,identifier,"steam_winch")


def bitts(m,x,y,z,identifier=None):
    start=len(m.vertices)
    box(m,(x,y,z+.095),(1.26,.58,.19),"Iron",.035)
    for dx in (-.36,.36):
        rod(m,(x+dx,y,z+.12),(x+dx,y,z+.71),.155,"Iron",20)
        rod(m,(x+dx,y,z+.65),(x+dx,y,z+.77),.22,"Iron",24)
    obstacle(m,start,identifier,"bitts")


def vent(m,x,y,z,height=2.4,radius=.30,identifier=None,facing=(1,0,0)):
    start=len(m.vertices)
    direction=Vector(facing).normalized()
    c=Vector((x,y,z))
    turn=.60
    rod(m,c,c+Vector((0,0,height-turn)),radius,"ShipWhite",24)
    torus(m,c+Vector((0,0,.10)),radius+.07,.038,"Iron",steps=24)
    path=[c+direction*(turn*(1-math.cos(t)))+
          Vector((0,0,height-turn+turn*math.sin(t))) for t in np.linspace(0,math.pi/2,13)]
    radii=[radius*(1+.70*smooth(0,1,i/12)) for i in range(13)]
    tube(m,path,radii,"ShipWhite",28,caps=False)
    tube(m,path[-6:],[r-.04 for r in radii[-6:]],"VentInside",28,caps=False)
    torus(m,path[-1],radii[-1]-.012,.035,"ShipWhite",direction,32,8)
    # Red bowl behind the open lip; no fake closed white disks at the mouth.
    rod(m,path[-6]-direction*.025,path[-6],radii[-6]-.04,"VentInside",28)
    if PASS >= 2 and not LOD:
        for angle in np.linspace(0,TAU,8,endpoint=False):
            bx,by=x+(radius+.07)*math.cos(angle),y+(radius+.07)*math.sin(angle)
            rod(m,(bx,by,z+.035),(bx,by,z+.09),.030,"Iron",6)
    obstacle(m,start,identifier,"cowl_ventilator")


def stairs(m,bottom,top,width,identifier,fore=False,from_zone=None,to_zone=None):
    start=len(m.vertices)
    bottom,top=Vector(bottom),Vector(top)
    delta=top-bottom
    count=math.ceil(delta.z/.21)
    run=abs(delta.x)
    sign=1 if delta.x > 0 else -1
    for i in range(count):
        x=bottom.x+delta.x*(i+.5)/count
        z=bottom.z+delta.z*(i+1)/count
        box(m,(x,bottom.y,z-.055),(run/count+.024,width,.11),"DeckPlanks",.012)
        if PASS >= 1:
            box(m,(x-sign*run/(2*count),bottom.y,z-.105),(.043,width,.22),"ShipWhite",.012)
    for side in (-1,1):
        y=bottom.y+side*(width/2+.055)
        beam(m,(bottom.x,y,bottom.z+.05),(top.x,y,top.z-.04),.10,.24,"ShipWhite",.025)
        for t in np.linspace(0,1,5):
            p=bottom+delta*t
            rod(m,(p.x,y,p.z),(p.x,y,p.z+1.05),.035,"ShipWhite",10)
        beam(m,(bottom.x,y,bottom.z+1.06),(top.x,y,top.z+1.06),.075,.075,"Rail",.012)
    if not LOD:
        CONNECTIONS.append({"id":identifier,"from":from_zone or ("fore_well" if fore else "aft_well"),
                            "to":to_zone or ("forecastle" if delta.x > 0 else "superstructure_t2" if fore else "poop"),
                            "bottom":list(bottom),"top":list(top),"width":width,
                            "steps":count,"rise":delta.z,"run":run})
    obstacle(m,start,identifier,"stairs",from_zone or ('fore_well' if fore else 'aft_well'),nav_blocking=False)


def new_access():
    m = BUILDERS['GalleyChimney']
    start = len(m.vertices)
    rod(m,(2.40,-3.60,T1_Z),(2.40,-3.60,11.60),.22,'Funnel',40)
    # Open weather cap above the pipe; existing palette only.
    tube(m,[(2.40,-3.60,11.65),(2.40,-3.60,11.87)],[.34,.035],'Iron',40)
    for side in (-1,1):
        rod(m,(2.40+side*.18,-3.60,11.52),(2.40+side*.18,-3.60,11.68),.025,'Iron',8)
    obstacle(m,start,'galley_chimney','chimney','superstructure_t2')
    m = BUILDERS['HoldCompanion']
    # Door 1.16 x 2.0 m: jambs sit flush with the side walls' inner faces (y +-0.66), so a carrier never
    # catches a jamb corner and a 1.75 m capsule plus skin clears the header.
    shell_wall(m,0,6.00,1,(-.70,.70),(MAIN_Z,7.70),
               [(-.58,.58,MAIN_Z,7.60)],zone='fore_well')
    shell_wall(m,0,8.40,-1,(-.70,.70),(MAIN_Z,7.70),zone='fore_well')
    for side in (-1,1):
        shell_wall(m,1,side*.70,-side,(6.00,8.40),(MAIN_Z,7.70),zone='fore_well')
    start = len(m.vertices)
    box(m,(7.20,0,7.75),(2.40,1.40,.10),'ShipWhite')
    obstacle(m,start,'hold_companion_roof','companion_roof','fore_well',nav_blocking=False)
    for side in (-1,1):
        stairs(BUILDERS['DeckFittings'],(-1.0,side*BOAT_STAIR_Y,T1_Z),(2.20,side*BOAT_STAIR_Y,BOAT_Z),
               1.0,f'boat_deck_stairs_{side}',from_zone='superstructure_t2',to_zone='boat_deck')
        if not LOD:
            CONNECTIONS[-1]['landing']=[2.52,side*BOAT_STAIR_Y,BOAT_Z]
    # Unity supplies these steps. Keep the complete stair envelope empty in FBX.
    if not LOD:
        CONNECTIONS.append({'id':'hold_stairs','from':'fore_well','to':'hold',
            'bottom':[9.60,0,2.80],'top':[6.10,0,MAIN_Z], 'width':1.10,'steps':14,
            'rise':2.80,'run':3.50,'step_rise':.20,'step_run':.25,'descent_direction':[1,0,0],
            'approach':[5.78,0,MAIN_Z],'geometry_owner':'Unity',
            'run_note':'Legacy run is total horizontal run; step_run is 0.25 m.'})
        for side in (-1,1):
            for other,x,y in [('fore_well',4.964,6.05),('aft_well',-39.712,5.72)]:
                CONNECTIONS.append({'id':f'promenade_level_{other}_{side}',
                    'from':other,'to':'superstructure_t1_promenade',
                    'bottom':[x+(.04 if other=='fore_well' else -.04),side*y,MAIN_Z],
                    'top':[x+(-.04 if other=='fore_well' else .04),side*y,MAIN_Z],
                    'width':1.0,'steps':0,'rise':0.,'run':.08,'kind':'level_passage'})


def fore_fittings():
    m=BUILDERS["DeckFittings"]
    for i,(f,size) in enumerate(HATCHES):
        hatch(m,f,size,f"fore_hatch_{i+1}")
        for side in (-1,1):
            winch(m,x_at(f+.005),side*(size[1]/2+1.40),MAIN_Z,
                  f"fore_winch_{i+1}_{'port' if side>0 else 'starboard'}",i == 1)
    for side in (-1,1):
        for f in (.300,.447):
            bitts(m,x_at(f),side*(half_width(f)-.68),MAIN_Z,
                  f"fore_bitts_{f:.3f}_{side}")
        vent(m,x_at(.437),side*4.10,MAIN_Z,2.40,.28,f"fore_cowl_aft_{side}")
        vent(m,x_at(.340),side*5.90,MAIN_Z,1.65,.23,f"fore_cowl_mast_{side}",(-1,0,0))
        top_x=x_at(FORE_END)
        stairs(m,(top_x-3.65,side*6.0,MAIN_Z),
               (top_x,side*6.0,raised_z(FORE_END)),1.12,
               f"forecastle_stairs_{side}",True)
        stairs(m,(x_at(SUPER_START)+4.05,side*5.65,MAIN_Z),
               (x_at(SUPER_START),side*5.65,T1_Z),1.12,
               f"promenade_stairs_{side}",True)


def bow_fittings():
    m=BUILDERS["DeckFittings"]
    start = len(m.vertices)
    x,z=x_at(.108),raised_z(.108)
    box(m,(x,0,z+.16),(2.35,4.0,.32),"Iron",.045)
    for y in (-1.45,1.45):
        box(m,(x,y,z+.68),(1.10,.29,1.26),"Iron",.04)
    rod(m,(x,-2.20,z+1.02),(x,2.20,z+1.02),.17,"Iron",28)
    for y in (-1.18,1.18):
        rod(m,(x,y-.33,z+1.02),(x,y+.33,z+1.02),.48,"Iron",32)
        for yy in (y-.33,y+.33):
            rod(m,(x,yy-.055,z+1.02),(x,yy+.055,z+1.02),.63,"Iron",32)
    obstacle(m,start,'anchor_windlass','anchor_windlass','forecastle')
    for side in (-1,1):
        for f in (.06,.18,.24):
            bitts(m,x_at(f),side*max(.6,half_width(f)-1.0),raised_z(f))
        vent(m,x_at(.207),side*3.80,raised_z(.207),1.60,.23,facing=(1,0,0))
        f=.052
        ax,ay,az=x_at(f),side*(half_width(f)+.13),6.45
        torus(m,(ax,ay,az+.22),.39,.105,"Iron",(0,1,0),32,10)
        rod(m,(ax,ay,az-.48),(ax,ay,az+.49),.15,"Iron",16)
        torus(m,(ax,ay+side*.09,az+.51),.21,.050,"Iron",(0,1,0),24,8)
        # Stockless anchor: crown and two broad flukes, no transverse stock.
        anchor_y=ay+side*.28
        beam(m,(ax,anchor_y,az+.27),(ax,anchor_y,az-1.17),.21,.25,"Iron",.02)
        for dx in (-1,1):
            beam(m,(ax,anchor_y,az-1.15),(ax+dx*.70,anchor_y,az-.83),.25,.29,"Iron",.025)
            rot=Matrix.Rotation(dx*math.radians(27),3,'Y')
            box(m,(ax+dx*.70,anchor_y,az-.71),(.40,.19,.64),"Iron",.025,rot)
        if PASS >= 1 and LOD != 2:
            # Small paired chain links from hawse to wildcat, laid on forecastle.
            a=Vector((x,side*1.18,z+.44))
            b=Vector((ax,side*(half_width(f)-.35),raised_z(f)+.10))
            for i in range(30):
                p=a.lerp(b,i/29)
                torus(m,p,.082,.023,"Iron",(0,0,1) if i%2 else (0,1,0),12,6)


def aft_fittings():
    m=BUILDERS["DeckFittings"]
    hatch(m,.883,(4.3,4.65,.76))
    for side in (-1,1):
        winch(m,x_at(.862),side*3.67,MAIN_Z)
        vent(m,x_at(.906)+.52,side*2.72,MAIN_Z,1.9,.26,f'aft_cowl_{side}')
        bitts(m,x_at(.906),side*4.50,MAIN_Z)
        stairs(m,(x_at(.91)+3.75,side*3.75,MAIN_Z),
               (x_at(.91),side*3.75,raised_z(.91)),1.10,f"poop_stairs_{side}")
    poop=BUILDERS["Poop"]
    f=.946
    start = len(poop.vertices)
    box(poop,(x_at(f),0,raised_z(f)+1.10),(5.0,5.6,2.20),"ShipWhite",.08)
    obstacle(poop,start,'poop_deckhouse','deckhouse','poop')
    start = len(poop.vertices)
    box(poop,(x_at(f),0,raised_z(f)+2.28),(5.23,5.85,.20),"ShipWhite",.045)
    obstacle(poop,start,'poop_deckhouse_roof','deckhouse_roof','poop')
    for side in (-1,1):
        for dx in (-1.35,1.35):
            porthole(poop,x_at(f)+dx,side*2.83,raised_z(f)+1.45,side,.21,True)
        for ff in (.931,.977):
            bitts(m,x_at(ff),side*max(.5,half_width(ff)-.90),raised_z(ff))
    start = len(poop.vertices)
    rod(poop,(x_at(.991),0,raised_z(.991)),(x_at(.995),0,13.2),.055,"Mast",12)
    obstacle(poop,start,'ensign_staff','mast','poop')
    tube(BUILDERS["Rigging"],[(x_at(.995),0,13.1),(x_at(.981),0,raised_z(.981))],.011,"Iron",6)
    # French merchant ensign using existing livery materials, separate vertices.
    for j,mat in enumerate(("FunnelStar","ShipWhite","VentInside")):
        layers=[]
        for dy in (-.006,.006):
            verts=[]
            for i in range(5):
                xx=x_at(.995)-.53*(j+i/4)
                verts.append([poop.vertex((xx,.12*math.sin((j+i/4)*2.2)+dy,z)) for z in (11.75,12.75)])
            layers.append(verts)
            for a,b in zip(verts,verts[1:]):
                poop.face((a[0],b[0],b[1],a[1]),mat)
        for edge in (0,1):
            for i in range(4):
                poop.face((layers[0][i][edge],layers[0][i+1][edge],
                           layers[1][i+1][edge],layers[1][i][edge]),mat)
        for end in (0,4):
            poop.face((layers[0][end][0],layers[0][end][1],layers[1][end][1],layers[1][end][0]),mat)


def mast(name,f,top,cross_z):
    m=BUILDERS[name]
    x=x_at(f)
    zone='fore_well' if name=='Foremast' else 'aft_well'
    start=len(m.vertices)
    rod(m,(x,0,MAIN_Z),(x,0,MAIN_Z+.18),.78,"Iron",32)
    obstacle(m,start,name.lower()+'_base','mast_base',zone)
    start=len(m.vertices)
    rod(m,(x,0,MAIN_Z+.18),(x,0,MAIN_Z+.87),.52,"ShipWhite",24)
    obstacle(m,start,name.lower()+'_collar','mast_collar',zone)
    start=len(m.vertices)
    tube(m,[(x,0,MAIN_Z+.6),(x,0,cross_z+.7)],[.39,.24],"Mast",32)
    obstacle(m,start,name.lower()+'_shaft','mast',zone)
    start=len(m.vertices)
    tube(m,[(x+.13,0,cross_z-.75),(x+.13,0,top)],[.205,.045],"Mast",24)
    obstacle(m,start,name.lower()+'_upper_shaft','mast',zone)
    for z in np.arange(MAIN_Z+1,cross_z+.5,2.5):
        rod(m,(x,0,z-.035),(x,0,z+.035),.41-(z-MAIN_Z)*.006,"Iron",24)
    beam(m,(x-1.1,0,cross_z),(x+1.1,0,cross_z),.22,.18,"Mast",.025)
    beam(m,(x,-2.2,cross_z),(x,2.2,cross_z),.20,.19,"Mast",.025)
    for side in (-1,1):
        beam(m,(x,side*1.95,cross_z),(x,side*.2,cross_z-1.2),.10,.11,"Iron",.015)
    if PASS >= 1:
        for a in np.linspace(0,TAU,10,endpoint=False):
            rod(m,(x+.63*math.cos(a),.63*math.sin(a),MAIN_Z+.17),
                (x+.63*math.cos(a),.63*math.sin(a),MAIN_Z+.24),.065,"Iron",6)
    # Ropes have 2.5 cm diameter and six sides, including real ratlines in pass 2.
    rig=BUILDERS["Rigging"]
    for side in (-1,1):
        shrouds=[]
        for dx in (-2.1,-.7,.7,2.1):
            a=Vector((x+dx,side*min(half_width(f)-.4,6.75),MAIN_Z+1.05))
            b=Vector((x+.02,side*.43,cross_z+.1))
            tube(rig,[a,a.lerp(b,.5)+Vector((0,side*.06,0)),b],.0125,"Iron",6)
            shrouds.append((a,b))
            start = len(rig.vertices)
            box(rig,(a.x,a.y,MAIN_Z+.56),(.17,.11,1.04),"Iron",.016)
            torus(rig,(a.x,a.y,MAIN_Z+1.02),.10,.023,"Iron",(0,1,0),12,6)
            obstacle(rig,start,f'{name}_shroud_anchor_{side}_{dx:.1f}','rigging_anchor',
                     'fore_well' if name=='Foremast' else 'aft_well')
        if PASS >= 2:
            for z in np.arange(MAIN_Z+2,cross_z-1.2,.50):
                ends=[a.lerp(b,(z-a.z)/(b.z-a.z)) for a,b in (shrouds[0],shrouds[-1])]
                rod(rig,*ends,.010,"Iron",6)
        rod(rig,(x,side*1.85,cross_z),(x+.13,side*.1,top-3),.0125,"Iron",6)
    if name == "Foremast":
        rod(rig,(x+.13,0,top-.8),(x_at(.015),0,raised_z(.015)+.95),.014,"Iron",6)
        rod(rig,(x,0,cross_z+.5),(x_at(.47),0,BOAT_Z+1),.0125,"Iron",6)
    else:
        rod(rig,(x+.13,0,top-.75),(x_at(.982),0,raised_z(.982)+.8),.014,"Iron",6)
        rod(rig,(x,0,cross_z+.3),(x_at(.70),0,BOAT_Z+.35),.0125,"Iron",6)


def derricks():
    m,rig=BUILDERS["Derricks"],BUILDERS["Rigging"]
    fx=x_at(FOREMAST_F)
    for side in (-1,1):
        a=Vector((fx,side*1.25,MAIN_Z+2.05))
        b=Vector((fx+13.6,side*3.25,raised_z(.21)+.83))
        heel_start=len(m.vertices)
        rod(m,a-Vector((0,0,.35)),a+Vector((0,0,.25)),.28,"Iron",20)
        obstacle(m,heel_start,f'fore_derrick_heel_{side}','derrick_heel')
        bracket_start=len(m.vertices)
        beam(m,(fx,side*.40,a.z),(fx,side*1.25,a.z),.18,.18,'Iron',.015)
        obstacle(m,bracket_start,f'fore_derrick_bracket_{side}','derrick_bracket','fore_well')
        boom_start=len(m.vertices)
        tube(m,[a,b],[.22,.115],"Mast",24)
        boom_colliders(m,boom_start,24,a,b,f'fore_derrick_boom_{side}','fore_well')
        torus(m,b,.16,.036,"Iron",(0,1,0),20,8)
        rod(rig,(fx,side*.20,25.0),b,.0125,"Iron",6)
        rod(rig,b,(fx+1,side*2.4,MAIN_Z+.55),.0125,"Iron",6)
        # Crutch is above forecastle, keeping the laid boom clear of its bulkhead.
        crutch_x=fx+11.4
        start = len(m.vertices)
        for dy in (-.30,.30):
            rod(m,(crutch_x,side*3.0+dy,raised_z(fraction(crutch_x))),
                (crutch_x,side*3.0+dy,b.z-.06),.075,"Iron",12)
        beam(m,(crutch_x,side*3-.44,b.z-.04),(crutch_x,side*3+.44,b.z-.04),.15,.14,"Rail",.02)
        obstacle(m,start,f'fore_derrick_crutch_{side}','derrick_crutch','forecastle')
    mx=x_at(MAINMAST_F)
    # Four actual 50-degree booms: two each side, two forward and two aft.
    for side in (-1,1):
        for direction in (-1,1):
            a=Vector((mx,side*.60,MAIN_Z+1.55))
            horizontal=11.4*math.cos(math.radians(50))
            b=a+Vector((direction*horizontal*.94,side*horizontal*.342,
                        11.4*math.sin(math.radians(50))))
            boom_start=len(m.vertices)
            tube(m,[a,b],[.20,.105],"Mast",24)
            boom_colliders(m,boom_start,24,a,b,f'aft_derrick_boom_{side}_{direction}','aft_well')
            start = len(m.vertices)
            rod(m,a-Vector((0,0,.28)),a+Vector((0,0,.30)),.255,"Iron",20)
            obstacle(m,start,f'aft_derrick_heel_{side}_{direction}','derrick_heel','aft_well')
            torus(m,b,.135,.035,"Iron",(0,1,0),20,8)
            rod(rig,(mx,side*.24,23.3),b,.0125,"Iron",6)
            runner=b-Vector((0,0,3.1))
            rod(rig,b,runner,.0125,"Iron",6)
            torus(rig,runner,.10,.027,"Iron",(1,0,0),16,6)


def funnel():
    m=BUILDERS["Funnel"]
    middle=(BOAT_Z+FUNNEL_TOP)/2
    axis=lambda z: Vector((x_at(FUNNEL_F)-math.tan(FUNNEL_RAKE)*(z-middle),0,z))
    for z0,z1,mat in [(BOAT_Z,16.2,"Funnel"),(16.2,19.5,"FunnelBand"),(19.5,FUNNEL_TOP,"Funnel")]:
        tube(m,[axis(z0),axis(z1)],FUNNEL_RADIUS,mat,96,caps=False)
    tube(m,[axis(FUNNEL_TOP-1.15),axis(FUNNEL_TOP)],FUNNEL_RADIUS-.095,"Funnel",96,caps=False)
    torus(m,axis(FUNNEL_TOP-.04),FUNNEL_RADIUS-.035,.055,"Funnel",steps=96,sides=10)
    rod(m,axis(BOAT_Z-.12),axis(BOAT_Z+.17),2.12,"Funnel",64)
    obstacle(m,0,'main_funnel_shell','funnel','boat_deck')
    for side in (-1,1):
        for offset in (-.79,.79):
            center=Vector((axis(17.85).x+offset,0,17.85))
            points=[]
            for j in range(10):
                a=math.pi/2+TAU*j/10
                r=.67 if j%2 == 0 else .285
                xx,zz=center.x+r*math.cos(a),center.z+r*math.sin(a)
                dx=xx-axis(zz).x
                yy=side*(math.sqrt(FUNNEL_RADIUS**2-dx**2)+.022)
                points.append(m.vertex((xx,yy,zz)))
            dx=center.x-axis(center.z).x
            cc=m.vertex((center.x,side*(math.sqrt(FUNNEL_RADIUS**2-dx**2)+.025),center.z))
            for j in range(10):
                m.face((cc,points[j],points[(j+1)%10]),"FunnelStar")
    # Aft steam pipe, whistle and modest funnel stays.
    steam=[axis(z)+Vector((-2.03,0,0)) for z in (BOAT_Z+.1,20.2,21.2)]
    tube(m,steam,.09,"ShipWhite",16)
    tube(m,[steam[-1],steam[-1]+Vector((-.18,0,.23))],.10,"Iron",16)
    rod(m,axis(20.2)+Vector((-1.98,.2,0)),axis(20.2)+Vector((-1.98,.2,.48)),.105,"Iron",16)
    for z in (13.3,16.0,19.8):
        rod(m,axis(z)+Vector((-1.80,0,0)),axis(z)+Vector((-2.08,0,0)),.043,"Iron",12)
    for side in (-1,1):
        for dx in (-3.0,3.0):
            rod(BUILDERS["Rigging"],axis(20.45)+Vector((dx*.45,side*.95,0)),
                (x_at(.65)+dx,side*3.0,BOAT_Z),.011,"Iron",6)


def boats():
    m=BUILDERS["Lifeboats"]
    fittings=BUILDERS["DeckFittings"]
    for side in (-1,1):
        for index,f in enumerate(BOAT_FRACTIONS):
            start = len(m.vertices)
            x,y,z=x_at(f),side*5.25,BOAT_Z+.68
            rings=[]
            inner=[]
            longitudinal=22 if LOD == 2 else 44
            cross=8 if LOD == 2 else 16
            for i in range(longitudinal+1):
                t=-1+2*i/longitudinal
                width=1.03*max(.025,1-abs(t)**2)**.62
                keel=z+.13*abs(t)**2
                ring=[]
                for j in range(cross+1):
                    a=-math.pi/2+math.pi*j/cross
                    ring.append(m.vertex((x+t*3.36,y+width*math.sin(a),keel+1.12*(1-math.cos(a)))))
                rings.append(ring)
                inner.append([m.vertex((x+t*3.32,y+(width-.045)*math.sin(-math.pi/2+math.pi*j/cross),
                                        keel+.09+1.01*(1-math.cos(-math.pi/2+math.pi*j/cross))))
                              for j in range(cross+1)])
            shells=[(rings,"ShipWhite")]
            if LOD != 2 or index == 2:
                shells.append((inner,"DeckPlanks"))
            for shell,mat in shells:
                for a,b in zip(shell,shell[1:]):
                    for j in range(cross):
                        m.face((a[j],b[j],b[j+1],a[j+1]),mat,True)
            for edge in (0,cross):
                tube(m,[m.vertices[r[edge]] for r in rings],.049,"Rail",10)
                for i in range(longitudinal):
                    m.face((rings[i][edge],rings[i+1][edge],inner[i+1][edge],inner[i][edge]),"Rail")
            for end in (0,longitudinal):
                for j in range(cross):
                    m.face((rings[end][j],rings[end][j+1],inner[end][j+1],inner[end][j]),"ShipWhite")
            for dx in (() if LOD == 2 and index != 2 else np.linspace(-2.65,2.65,7)):
                w=1.03*(1-abs(dx/3.36)**2)**.62
                box(m,(x+dx,y,z+.73),(.27,w*1.85,.08),"DeckPlanks",.015)
                if PASS >= 2:
                    beam(m,(x+dx,y-.75*w,z+.35),(x+dx,y-.80*w,z+.83),.08,.08,"Rail",.01)
            if index != 2:
                rows=[]
                cover_long=16 if LOD == 2 else 32
                cover_cross=4 if LOD == 2 else 8
                for i in range(cover_long+1):
                    t=-1+2*i/cover_long
                    w=.99*max(.015,1-abs(t)**2)**.62
                    rows.append([m.vertex((x+t*3.30,y+q*w,z+1.13+.16*(1-q*q)*w/.99))
                                 for q in np.linspace(-1,1,cover_cross+1)])
                for a,b in zip(rows,rows[1:]):
                    for j in range(cover_cross):
                        m.face((a[j],b[j],b[j+1],a[j+1]),"Canvas",True)
                if PASS >= 1 and LOD != 2:
                    for dx in np.linspace(-2.9,2.9,12):
                        w=1.03*(1-abs(dx/3.36)**2)**.62
                        for ss in (-1,1):
                            rod(m,(x+dx,y+ss*w,z+1.1),(x+dx,y+ss*(w*.75),z+.57),.014,"Rail",6)
            else:
                for s in (-1,1):
                    beam(m,(x-2.4,y+s*.28,z+.81),(x+2.2,y+s*.34,z+.85),.065,.075,"Rail",.01)
            obstacle(m,start,f'lifeboat_{side}_{index+1}','lifeboat','boat_deck')
            for dx in (-2.17,2.17):
                start = len(fittings.vertices)
                box(fittings,(x+dx,y,BOAT_Z+.29),(.35,1.5,.58),"Iron",.035)
                obstacle(fittings,start,f'boat_cradle_{side}_{index+1}_{dx:.2f}','boat_cradle','boat_deck')
                # Swan-neck davit bends inward above the gunwale.
                yy=side*6.63
                path=[(x+dx,yy,BOAT_Z),(x+dx,yy,BOAT_Z+2.70),
                      (x+dx,side*6.56,BOAT_Z+3.23),(x+dx,side*6.22,BOAT_Z+3.58),
                      (x+dx,side*5.50,BOAT_Z+3.62)]
                start = len(fittings.vertices)
                tube(fittings,path,.075,"Iron",16)
                for rr in (-.08,.08):
                    rod(BUILDERS["Rigging"],(x+dx+rr,side*5.5,BOAT_Z+3.60),
                        (x+dx+rr,y,z+.96),.0125,"Iron",6)
                torus(fittings,path[-1],.13,.032,"Iron",(1,0,0),20,8)
                obstacle(fittings,start,f'boat_davit_{side}_{index+1}_{dx:.2f}','davit','boat_deck')
    for side in (-1,1):
        for f,h in ((.535,3.3),(.590,3.5),(.725,3.4),(.790,2.6)):
            vent(fittings,x_at(f),side*3.43,BOAT_Z,h,.37)
    for f in (.578,.751):
        start = len(fittings.vertices)
        box(fittings,(x_at(f),0,BOAT_Z+.34),(2.25,2.6,.68),"ShipWhite",.06)
        for y in (-.65,.65):
            box(fittings,(x_at(f),y,BOAT_Z+.71),(1.92,1.05,.055),"Glass",.012)
        obstacle(fittings,start,f'skylight_{f:.3f}','skylight','boat_deck')


def railing(fa,fb,z,bulwark=False,width_limit=100,zone=None):
    m=BUILDERS["Railings"]
    zfun=z if callable(z) else lambda f:z
    zone = zone or ('forecastle' if fa == 0 else 'fore_well' if fa == FORE_END
                    else 'poop' if fa == POOP_START else 'aft_well' if fa == SUPER_END
                    else 'boat_deck' if z == BOAT_Z else 'superstructure_t2' if z == T1_Z
                    else 'superstructure_t1_promenade')
    records = EDGES.setdefault(zone,[])
    def build_path(path,open_edge=False,source=None):
        if len(path)<2:
            return
        if not LOD:
            records.append({'points':[[p[0],p[1]] for p in path], 'height':1.08,
                            **({'open':True,'connection':source} if open_edge else {})})
        if open_edge:
            return
        if LOD or bulwark:
            for a,b in zip(path,path[1:]):
                # Closed thin panels preserve the rail silhouette in both LODs.
                aa,bb = Vector(a),Vector(b)
                direction = (bb-aa).normalized()
                inward = Vector((-direction.y,direction.x,0))*.05
                rings=[]
                for offset in (-inward,inward):
                    rings.append([m.vertex(p) for p in (aa+offset,bb+offset,
                                  bb+offset+Vector((0,0,1.06)),aa+offset+Vector((0,0,1.06)))])
                for ring in rings:
                    m.face(ring,'Hull' if bulwark else 'ShipWhite')
                for j in range(4):
                    k=(j+1)%4
                    m.face((rings[0][j],rings[1][j],rings[1][k],rings[0][k]),'ShipWhite')
            tube(m,[(x,y,h+1.08) for x,y,h in path],.044,'Rail',10)
        else:
            for level in (.40,.76,1.08):
                tube(m,[(x,y,h+level) for x,y,h in path],.044 if level == 1.08 else .024,
                     'Rail' if level == 1.08 else 'ShipWhite',10 if level == 1.08 else 8)
        if not LOD:
            for i in sorted(set(range(0,len(path),3)) | {len(path)-1}):
                x,y,h=path[i]
                rod(m,(x,y,h),(x,y,h+1.08),.038,'ShipWhite',10)
        elif zone == 'forecastle' and abs(path[0][0]-LENGTH/2)<1e-6:
            x,y,h=path[0]
            rod(m,(x,y,h),(x,y,h+1.08),.038,'ShipWhite',10)
    for side in (-1,1):
        stations=list(np.linspace(fa,fb,max(10,math.ceil((fb-fa)*LENGTH/(.55 if LOD == 0 else 1.65)))))
        dock = zone == 'fore_well' and side == -1
        if dock:
            stations += [fraction(12.464),fraction(14.464)]
        stations=sorted(set(stations))
        sections=[]
        active=[]
        state=None
        for f0,f1 in zip(stations,stations[1:]):
            opened=dock and 12.464 < x_at((f0+f1)/2) < 14.464
            pair=[(x_at(f),side*min(max(.035,half_width(f)-.22),width_limit),zfun(f)) for f in (f0,f1)]
            if state is not None and opened != state:
                sections.append((active,state)); active=[]
            if not active:
                active.append(pair[0])
            active.append(pair[1]); state=opened
        sections.append((active,state))
        for path,opened in sections:
            build_path(path,opened,'Marseille gangway' if opened else None)
    if bulwark:
        return
    for f in (fa,fb):
        width=min(max(.035,half_width(f)-.22),width_limit)
        if width <= .5:
            continue
        if zone == 'boat_deck' and abs(f-.48)<1e-6:
            # Bridge-wing flooring continues forward of this old cross-deck
            # rail. Place the rail at its actual perimeter so stairs reach aft.
            for side in (-1,1):
                build_path([(x_at(f),side*6.9,BOAT_Z),
                            (x_at(.461),side*6.9,BOAT_Z),
                            (x_at(.461),side*4.55,BOAT_Z)])
                if not LOD:
                    records.append({'points':[[2.20,side*BOAT_STAIR_Y-.60],
                                              [2.20,side*BOAT_STAIR_Y+.60]],
                                    'height':1.08,'open':True,
                                    'connection':f'boat_deck_stairs_{side}'})
            continue
        # The T1 facade is already a wall. A rail across it would close the new door.
        if zone == 'superstructure_t1_promenade' and abs(f-SUPER_START)<1e-6:
            if not LOD:
                for side in (-1,1):
                    records.append({'points':[[x_at(f),side*5.23],[x_at(f),side*width]],
                                    'height':1.08,'open':True,'connection':f'promenade_level_fore_well_{side}'})
            continue
        gaps=[]
        for connection in CONNECTIONS:
            for point in (connection['bottom'],connection['top']):
                if abs(point[0]-x_at(f))<.051 and abs(point[2]-zfun(f))<.03:
                    half=connection['width']/2+.10
                    gaps.append((max(-width,point[1]-half),min(width,point[1]+half),connection['id']))
        cuts=sorted(set([-width,width]+[v for a,b,_ in gaps for v in (a,b)]))
        for a,b in zip(cuts,cuts[1:]):
            ids=[key for lo,hi,key in gaps if lo < (a+b)/2 < hi]
            build_path([(x_at(f),a,zfun(f)),(x_at(f),b,zfun(f))],bool(ids),ids[0] if ids else None)


def underwater():
    global PROP_BLADE_START
    r=BUILDERS["Rudder"]
    outline=[(-60.1,-1.75),(-61.78,-2.15),(-61.90,-5.5),(-61.2,-6.78),(-60.1,-6.7)]
    rings=[[r.vertex((x,y,z)) for x,z in outline] for y in (-.14,.14)]
    for ring in rings:
        center=r.vertex(np.mean([r.vertices[i] for i in ring],axis=0))
        for i in range(len(ring)):
            r.face((center,ring[i],ring[(i+1)%len(ring)]),"Iron")
    for i in range(len(outline)):
        j=(i+1)%len(outline)
        r.face((rings[0][i],rings[1][i],rings[1][j],rings[0][j]),"Iron")
    rod(r,(-60.1,0,-6.70),(-60.1,0,1.65),.14,"Iron",24)
    for z in (-2.25,-3.8,-5.5):
        rod(r,(-60.1,0,z-.13),(-60.1,0,z+.13),.25,"Iron",24)
    p=BUILDERS["Propeller"]
    center=Vector((-59.10,0,-4.47))
    rod(p,(-55.6,0,-4.47),(-59.5,0,-4.47),.29,"Iron",32)
    tube(p,[center+Vector((.45,0,0)),center,center+Vector((-.58,0,0))],
         [.33,.48,.12],"Iron",40)
    PROP_BLADE_START=len(p.vertices)
    longitudinal,cross=(8,6) if LOD == 2 else (20,12)
    for blade in range(4):
        angle=blade*TAU/4
        shells=[]
        for thick in (-.065,.065):
            rows=[]
            for i in range(longitudinal+1):
                t=i/longitudinal
                radial=.42+1.76*t
                sweep=.43*t*t
                width=.22+.62*math.sin(math.pi*(t*.94))
                row=[]
                for j in range(cross+1):
                    q=-1+2*j/cross
                    tangent=sweep+width*q*.5
                    yy=radial*math.cos(angle)-tangent*math.sin(angle)
                    zz=radial*math.sin(angle)+tangent*math.cos(angle)
                    row.append(p.vertex(center+Vector((.36*q+.09*t+thick,yy,zz))))
                rows.append(row)
            shells.append(rows)
            for a,b in zip(rows,rows[1:]):
                for j in range(cross):
                    p.face((a[j],b[j],b[j+1],a[j+1]),"Iron",True)
        for edge in (0,cross):
            for i in range(longitudinal):
                p.face((shells[0][i][edge],shells[0][i+1][edge],
                        shells[1][i+1][edge],shells[1][i][edge]),"Iron",True)
        for end in (0,longitudinal):
            for j in range(cross):
                p.face((shells[0][end][j],shells[0][end][j+1],
                        shells[1][end][j+1],shells[1][end][j]),"Iron",True)


def name_text():
    m=BUILDERS["NameText"]
    font=bpy.data.fonts.load(FONT)
    for side in (-1,1):
        curve=bpy.data.curves.new("temporary_name",'FONT')
        curve.body="AMIRAL LATOUCHE-TRÉVILLE"
        curve.font=font
        curve.size=.57
        curve.extrude=.006
        curve.resolution_u=1 if LOD == 2 else 3
        curve.align_x='CENTER'
        obj=bpy.data.objects.new("temporary_name",curve)
        bpy.context.collection.objects.link(obj)
        bpy.context.view_layer.update()
        data=bpy.data.meshes.new_from_object(obj.evaluated_get(bpy.context.evaluated_depsgraph_get()))
        start=len(m.vertices)
        for v in data.vertices:
            xx=x_at(.128)-side*v.co.x
            yy=side*(half_width(fraction(xx))+.042+v.co.z)
            m.vertex((xx,yy,6.86+v.co.y))
        for poly in data.polygons:
            m.face([start+i for i in poly.vertices],"NameText")
        bpy.data.meshes.remove(data)
        bpy.data.objects.remove(obj,do_unlink=True)
        bpy.data.curves.remove(curve)


def rectangle(center,size,yaw=0.,grow=0.):
    angle=math.radians(yaw)
    c,s=math.cos(angle),math.sin(angle)
    return [(center[0]+c*x-s*y,center[1]+s*x+c*y)
            for x,y in [(-size[0]/2-grow,-size[1]/2-grow),
                        (size[0]/2+grow,-size[1]/2-grow),
                        (size[0]/2+grow,size[1]/2+grow),
                        (-size[0]/2-grow,size[1]/2+grow)]]


def union_intervals(intervals,x):
    result=[]
    for lo,hi in sorted(intervals,key=lambda q:edge_y(q[0],x)):
        if result and edge_y(lo,x)<=edge_y(result[-1][1],x)+1e-9:
            if edge_y(hi,x)>edge_y(result[-1][1],x):
                result[-1]=(result[-1][0],hi)
        else:
            result.append((lo,hi))
    return result


def edge_y(edge,x):
    a,b=edge
    return a[1]+(b[1]-a[1])*(x-a[0])/(b[0]-a[0])


def polygon_intervals(poly,x):
    crossing=[(a,b) for a,b in zip(poly,poly[1:]+poly[:1])
              if min(a[0],b[0])<x<max(a[0],b[0])]
    crossing.sort(key=lambda e:edge_y(e,x))
    assert len(crossing)%2 == 0,'invalid polygon parity'
    return list(zip(crossing[::2],crossing[1::2]))


def decompose_free(polygons,blockers):
    """Exact vertical decomposition, including every segment intersection.

    Cells are trapezoids bounded by input line segments. No raster/grid samples
    decide connectivity; only positive-length shared intervals connect cells.
    """
    all_polys=polygons+blockers
    edges=[(a,b) for p in all_polys for a,b in zip(p,p[1:]+p[:1])
           if abs(a[0]-b[0])+abs(a[1]-b[1])>1e-10]
    events={float(p[0]) for poly in all_polys for p in poly}
    for i,(a,b) in enumerate(edges):
        dx,dy=b[0]-a[0],b[1]-a[1]
        for c,d in edges[i+1:]:
            if max(a[0],b[0])<min(c[0],d[0]) or max(c[0],d[0])<min(a[0],b[0]):
                continue
            if max(a[1],b[1])<min(c[1],d[1]) or max(c[1],d[1])<min(a[1],b[1]):
                continue
            ex,ey=d[0]-c[0],d[1]-c[1]
            den=dx*ey-dy*ex
            if abs(den)<1e-12:
                continue
            cx,cy=c[0]-a[0],c[1]-a[1]
            t=(cx*ey-cy*ex)/den
            u=(cx*dy-cy*dx)/den
            if -1e-9<=t<=1+1e-9 and -1e-9<=u<=1+1e-9:
                events.add(a[0]+t*dx)
    xs=[]
    for x in sorted(events):
        if not xs or x-xs[-1]>1e-8:
            xs.append(x)
    cells=[]
    parent=[]
    previous=[]
    def find(i):
        while parent[i]!=i:
            parent[i]=parent[parent[i]]
            i=parent[i]
        return i
    for xa,xb in zip(xs,xs[1:]):
        x=(xa+xb)/2
        base=union_intervals([q for p in polygons for q in polygon_intervals(p,x)],x)
        blocked=union_intervals([q for p in blockers for q in polygon_intervals(p,x)],x)
        free=[]
        for low,high in base:
            cursor=low
            for bl,bh in blocked:
                if edge_y(bh,x)<=edge_y(cursor,x)+1e-9:
                    continue
                if edge_y(bl,x)>=edge_y(high,x)-1e-9:
                    break
                if edge_y(bl,x)>edge_y(cursor,x)+1e-9:
                    free.append((cursor,bl))
                cursor=bh
                if edge_y(cursor,x)>=edge_y(high,x)-1e-9:
                    break
            if edge_y(cursor,x)<edge_y(high,x)-1e-9:
                free.append((cursor,high))
        current=[]
        for lo,hi in free:
            left=[edge_y(lo,xa),edge_y(hi,xa)]
            right=[edge_y(lo,xb),edge_y(hi,xb)]
            area=(xb-xa)*(left[1]-left[0]+right[1]-right[0])/2
            if area<=1e-10:
                continue
            index=len(cells)
            cells.append({'x':[xa,xb],'left':left,'right':right,'area':area})
            parent.append(index)
            for old in previous:
                last=cells[old]
                if abs(last['x'][1]-xa)<1e-8 and min(last['right'][1],left[1])-max(last['right'][0],left[0])>1e-8:
                    parent[find(index)]=find(old)
            current.append(index)
        previous=current
    roots=sorted({find(i) for i in range(len(cells))})
    groups={root:[] for root in roots}
    for i,cell in enumerate(cells):
        cell['component']=roots.index(find(i))
        groups[find(i)].append(cell)
    parts=[]
    for root in roots:
        largest=max(groups[root],key=lambda c:c['area'])
        parts.append({'area_m2':round(sum(c['area'] for c in groups[root]),6),
                      'witness':[(largest['x'][0]+largest['x'][1])/2,
                                 sum(largest['left']+largest['right'])/4]})
    return cells,parts


def nav_self_check():
    square=rectangle((0,0),(10,10))
    assert len(decompose_free([square],[rectangle((0,0),(2,2))])[1])==1
    assert len(decompose_free([square],[rectangle((0,0),(1,12))])[1])==2
    assert len(decompose_free([square],[rectangle((0,0),(20,1),45)])[1])==2
    adjoining=[rectangle((-2.5,0),(5,10)),rectangle((2.5,0),(5,10))]
    assert len(decompose_free(adjoining,[])[1])==1
    face=[(-2,-2,0),(2,-2,0),(2,2,0),(-2,2,0)]
    parts=box_difference(face,[-1,-1,-1],[1,1,1])
    assert abs(sum(polygon_area_3d(p) for p in parts)-12)<1e-8
    print('NAV SELF CHECK: hole, separating bar, rotated bar, shared boundary; clipping area PASS',flush=True)


def nav_check(zones):
    nav_self_check()
    cells_by_zone={}
    components={}
    polygon_checks=[]
    for name,zone in zones.items():
        blockers=[rectangle(o['center'],o['size'],o['yaw_deg'],.30)
                  for o in zone['obstacles'] if o['nav_blocking']]
        for edge in zone['edges']:
            if edge.get('open'):
                continue
            for a,b in zip(edge['points'],edge['points'][1:]):
                dx,dy=b[0]-a[0],b[1]-a[1]
                length=math.hypot(dx,dy)
                if length>1e-8:
                    blockers.append(rectangle(((a[0]+b[0])/2,(a[1]+b[1])/2),
                        (length,.10),math.degrees(math.atan2(dy,dx)),.30))
        blockers += [rectangle(((min(p[0] for p in hole)+max(p[0] for p in hole))/2,
                                 (min(p[1] for p in hole)+max(p[1] for p in hole))/2),
                                (max(p[0] for p in hole)-min(p[0] for p in hole),
                                 max(p[1] for p in hole)-min(p[1] for p in hole)),grow=.30)
                     for hole in zone.get('deck_holes',[])]
        cells,parts=decompose_free(zone['walkable_polygons'],blockers)
        cells_by_zone[name]=cells
        components[name]=parts
        for i,poly in enumerate(zone['walkable_polygons']):
            _,pieces=decompose_free([poly],blockers)
            polygon_checks.append({'zone':name,'polygon':i,'components':len(pieces),
                                   'connected':len(pieces)==1,'pieces':pieces})
    def attachment(name,point):
        matches=set()
        x,y=point[:2]
        for cell in cells_by_zone[name]:
            xa,xb=cell['x']
            if xa-1e-6<=x<=xb+1e-6:
                t=max(0.,min(1.,(x-xa)/(xb-xa)))
                lo=cell['left'][0]*(1-t)+cell['right'][0]*t
                hi=cell['left'][1]*(1-t)+cell['right'][1]*t
                if lo-1e-6<=y<=hi+1e-6:
                    matches.add(f"{name}#{cell['component']}")
        return matches
    graph={f'{name}#{i}':set() for name,parts in components.items() for i in range(len(parts))}
    region_graph={name:set() for name in zones}
    bad_connections=[]
    for connection in CONNECTIONS:
        a,b=connection['from'],connection['to']
        region_graph[a].add(b); region_graph[b].add(a)
        pa=connection.get('approach',connection['top'] if b=='hold' else connection['bottom'])
        pb=connection['bottom'] if b=='hold' else connection.get('landing',connection['top'])
        aa,bb=attachment(a,pa),attachment(b,pb)
        if not aa or not bb:
            bad_connections.append({'id':connection['id'],'from_components':sorted(aa),
                                    'to_components':sorted(bb),'from_point':pa,'to_point':pb})
        for left in aa:
            for right in bb:
                graph[left].add(right); graph[right].add(left)
    def reachable(graph,start):
        seen=set(start)
        queue=deque(start)
        while queue:
            for node in graph[queue.popleft()]-seen:
                seen.add(node); queue.append(node)
        return seen
    regions=reachable(region_graph,['fore_well'])
    start=max(range(len(components['fore_well'])),key=lambda i:components['fore_well'][i]['area_m2'])
    reached=reachable(graph,[f'fore_well#{start}'])
    unreachable_parts=[{'id':f'{name}#{i}',**part} for name,parts in components.items()
                       for i,part in enumerate(parts) if f'{name}#{i}' not in reached]
    return {'method':'segment arrangement / vertical trapezoid decomposition; no grid approximation',
            'clearance_m':.30,'obstacle_offset':'0.30 m on each side in box-local XY; conservative square offset',
            'edge_clearance':'Closed rail/wall edges use 0.10 m wide segment boxes, also expanded by 0.30 m; open edges produce no barrier.',
            'boundary_note':'Walkable outlines inset 0.33 m from hull shell; deck holes offset by the same 0.30 m agent margin. Stair links use clear deck landings.',
            'overhead_rule':f'Only obstacle boxes intersecting [deck-0.05, deck+{PLAYER_HEIGHT:.2f}] block walking.',
            'step_rule':f'Boxes whose top is <= {PLAYER_STEP:.2f} m above deck are step-over colliders (PlayerBuilder.cs).',
            'stair_rule':'Stair envelopes are traversable links, never solid nav blockers.',
            'regions_reachable':sorted(regions),'unreachable_regions':sorted(set(zones)-regions),
            'polygon_checks':polygon_checks,'region_components':components,
            'unreachable_fragments':unreachable_parts,'unattached_connections':bad_connections,
            'passed':len(regions)==len(zones) and not unreachable_parts and not bad_connections
                     and all(c['connected'] for c in polygon_checks)}


def write_zones(d):
    def zone(fa,fb,z,width,polygons=None):
        xa,xb=x_at(fb),x_at(fa)
        yy=width/2
        return {'x_range':[round(xa,5),round(xb,5)],'y_range':[-yy,yy],
                'deck_surface_z':z,'inner_clear_width_mid':width,
                'walkable_polygons':polygons or [rectangle(((xa+xb)/2,0),(xb-xa,width))],
                'navigation_inset_from_shell':.33,'obstacles':[],'edges':[]}
    def outline(fa,fb,limit=100):
        return [(x_at(f),-min(limit,max(.018,half_width(f)-.33))) for f in np.linspace(fa,fb,41)]+ \
               [(x_at(f),min(limit,max(.018,half_width(f)-.33))) for f in np.linspace(fb,fa,41)]
    def promenade_polys(fa,fb,inner,limit):
        fs=np.linspace(fa,fb,41)
        return [[(x_at(f),side*min(half_width(f)-.33,limit-.08)) for f in fs]+
                [(x_at(f),side*inner) for f in reversed(fs)] for side in (-1,1)]
    fore=zone(.26,.46,MAIN_Z,round(2*(half_width(.36)-.30),5),[outline(.26,.46)])
    fore['stair_connections']=[c for c in CONNECTIONS if c['from']=='fore_well']
    fore['clearance_note']='Subtract oriented obstacle boxes and deck_holes. Hatches and mast retained; companion doorway remains open.'
    fore['stairs_collision_note']='Stair boxes are envelopes: use traversable bottom/top/width ramps and handrails, never a solid envelope collider.'
    t1=promenade_polys(.46,.82,5.23,7.05)
    t2=promenade_polys(.46,.82,4.73,6.9)
    # Cross-deck landings on the exposed T1 roof join the two T2 promenades.
    t2 += [rectangle(((4.964+1.862)/2,0),(4.964-1.862,13.64)),
           rectangle(((-39.712-36.368)/2,0),(-36.368+39.712,12.80))]
    zones={'forecastle':zone(0,.26,raised_z(.13),2*(half_width(.13)-.264),[outline(0,.26)]),
           'fore_well':fore,
           'superstructure_t1_promenade':zone(.46,.82,MAIN_Z,14.012,t1),
           'superstructure_t2':zone(.46,.82,T1_Z,13.712,t2),
           'boat_deck':zone(.48,.80,BOAT_Z,13.712,[outline(.48,.80,6.20)]+
                    [rectangle(((.40+x_at(.461))/2,s*(4.64+6.82)/2),
                               (x_at(.461)-.40,6.82-4.64)) for s in (-1,1)]),
           'aft_well':zone(.82,.91,MAIN_Z,2*(half_width(.865)-.30),[outline(.82,.91)]),
           'poop':zone(.91,1,raised_z(.955),2*(half_width(.955)-.264),[outline(.91,1)]),
           'hold':{'x_range':[5.90,15.40],'y_range':[-3.20,3.20],'deck_surface_z':2.80,
                   'inner_clear_width_mid':6.40,'navigation_inset_from_shell':0.,
                   'walkable_polygons':[rectangle((10.65,0),(9.50,6.40))],
                   'obstacles':[],'edges':[],'geometry_owner':'Unity'}}
    for name,fa,fb in (('forecastle',0,.26),('poop',.91,1)):
        samples=[{'x':x_at(f),'z':raised_z(f)} for f in np.linspace(fa,fb,21)]
        zones[name]['deck_surface_z_range']=[min(q['z'] for q in samples),max(q['z'] for q in samples)]
        zones[name]['surface_rule']={'kind':'longitudinal_sheer','samples':samples}
    for name,zone_data in zones.items():
        zone_data['y_range']=[min(p[1] for poly in zone_data['walkable_polygons'] for p in poly),
                              max(p[1] for poly in zone_data['walkable_polygons'] for p in poly)]
        zone_data['edges']=EDGES.get(name,[])
    zones['superstructure_t2']['extent_note']='T2 house 0.485–0.792; exposed T1 roof includes both cross-deck landings.'
    zones['boat_deck']['x_range'][1]=x_at(.461)
    zones['boat_deck']['extent_note']='Main paths stop at |y|=6.20 behind boats; two forward bridge-wing landings cover x=0.40..4.8399, |y|=4.64..6.82. Narrow dead strips behind the wing columns are not walkable.'
    zones['boat_deck']['excluded_clearance_pockets']=[
        {'witness':[-.9070825,s*6.66],
         'reason':'Behind wing column, between expanded stair aperture and outer deck edge; no 0.30 m clearance route to a landing.'}
        for s in (-1,1)]
    zones['fore_well']['deck_holes']=[rectangle((7.40,0),(2.60,1.30))]
    zones['boat_deck']['deck_holes']=[rectangle((.55,s*BOAT_STAIR_Y),(3.30,1.20)) for s in (-1,1)]
    def deck_z(name,x):
        return raised_z(fraction(x)) if name in ('forecastle','poop') else zones[name]['deck_surface_z']
    for source in OBSTACLES:
        absolute=list(source['center'])
        footprint=np.asarray(rectangle(absolute,source['size'],source['yaw_deg'],.30))
        for name,zone_data in zones.items():
            if name=='hold':
                continue
            surface=deck_z(name,absolute[0])
            in_height=absolute[2]-source['size'][2]/2<surface+PLAYER_HEIGHT and absolute[2]+source['size'][2]/2>surface-.05
            poly=np.asarray([p for q in zone_data['walkable_polygons'] for p in q])
            intersects=not (np.any(footprint.max(axis=0)<poly.min(axis=0)) or
                            np.any(footprint.min(axis=0)>poly.max(axis=0)))
            if name!=source['zone'] and not (in_height and intersects):
                continue
            record={k:v for k,v in source.items() if k!='zone'}
            record['center_absolute']=absolute
            record['center']=[absolute[0],absolute[1],round(absolute[2]-surface,5)]
            record['deck_reference_z']=surface
            record['step_over']=absolute[2]+source['size'][2]/2<=surface+PLAYER_STEP
            record['nav_blocking']=source['nav_blocking'] and in_height and not record['step_over']
            if name!=source['zone']:
                record['shared_from']=source['zone']
            zone_data['obstacles'].append(record)
    for name,opening_id,points in [
        ('fore_well','galley_door',[[4.964,-3.90],[4.964,-3.00]]),
        ('superstructure_t1_promenade','corridor_starboard_door',[[-6.70,-5.23],[-5.70,-5.23]]),
        ('fore_well','hold_companion_door',[[6.0,-.58],[6.0,.58]])]:
        zones[name]['edges'].append({'points':points,'height':2.0,'open':True,'opening':opening_id})
    nav=nav_check(zones)
    data={'schema_version':2,'units':'metres','coordinates':{'bow':'+X','port':'+Y','up':'+Z',
          'origin':'waterline amidships','fbx_axis_forward':'-Z','fbx_axis_up':'Y',
          'note':'JSON uses Blender coordinates; obstacle center.z is relative to its deck_reference_z. center_absolute retains model-space bounds.'},
          'length':LENGTH,'beam':BEAM,'zones':zones,'connections':CONNECTIONS,
          'openings':OPENINGS,'interior_volumes':INTERIOR_VOLUMES,
          'galley_coordinate_transform':GALLEY_TRANSFORM,'nav_check':nav,
          'layout_adjustments':[
              {'id':'fore_derrick_heels','before_z':6.95,'after_z':7.65,'reason':'1.75 m player headroom; same X/Y, boom tips and ship bounds.'},
              {'id':'aft_cowls','before':[x_at(.906),[-2.55,2.55],MAIN_Z],
               'after':[x_at(.906)+.52,[-2.72,2.72],MAIN_Z],
               'reason':'Clear rear center passage after 0.30 m obstacle expansion; preserve access to poop stairs.'}],
          'collision_rules':{'obstacle_center_z':'height above deck_reference_z',
              'yaw_deg':'about Blender +Z; positive rotates +X toward +Y',
              'edges':'closed polylines produce rail/wall colliders; open=true produces no barrier',
              'shared_obstacles':'Deduplicate globally by id; shared_from marks boxes exposed at adjacent deck boundaries.',
              'deck_holes':'Subtract these inner loops from walkable_polygons when building floor colliders; nav offsets them by 0.30 m.',
              'stairs':'replace stair envelope boxes with traversable connections',
              'integration':'ShipDeck.cs currently uses hard-coded fore_well colliders; loading v2 is a separate Unity change.'}}
    data['interior_volumes']['galley_block']['coordinate_transform']=GALLEY_TRANSFORM
    (d/'deck_zones.json').write_text(json.dumps(data,ensure_ascii=False,indent=2)+'\n')
    print('NAV CHECK',json.dumps({k:v for k,v in nav.items() if k not in ('polygon_checks','region_components')}),flush=True)
    return data


def object_bounds(obj):
    return np.array([obj.matrix_world@Vector(v) for v in obj.bound_box])


def empty_volume_check(meshes):
    result={}
    for identifier,volume in INTERIOR_VOLUMES.items():
        lo,hi=np.asarray(volume['min']),np.asarray(volume['max'])
        vertices_by_object={}
        faces_by_object={}
        for name,obj in meshes.items():
            co=np.asarray([obj.matrix_world@v.co for v in obj.data.vertices])
            inside=np.all((co>lo)&(co<hi),axis=1)
            count=int(inside.sum())
            if count:
                vertices_by_object[name]=count
            if np.any(co.max(axis=0)<=lo) or np.any(co.min(axis=0)>=hi):
                continue
            crossing=0
            for poly in obj.data.polygons:
                points=co[list(poly.vertices)]
                if np.any(points.max(axis=0)<=lo+1e-6) or np.any(points.min(axis=0)>=hi-1e-6):
                    continue
                clipped=[tuple(p) for p in points]
                for axis in range(3):
                    clipped=clip_halfspace(clipped,axis,lo[axis]+1e-6,False)
                    clipped=clip_halfspace(clipped,axis,hi[axis]-1e-6,True)
                    if len(clipped)<3:
                        break
                if polygon_area_3d(clipped)>1e-9:
                    crossing+=1
            if crossing:
                faces_by_object[name]=crossing
        result[identifier]={'vertices_inside':sum(vertices_by_object.values()),
            'faces_intersecting':sum(faces_by_object.values()),
            'vertices_by_object':vertices_by_object,'faces_by_object':faces_by_object,
            'allowed_vertices_excluded':0,
            'note':'Roof underside >=8.314 and deck underside >=5.464 lie outside reserved boxes; no exemptions needed.',
            'passed':not vertices_by_object and not faces_by_object}
    print('EMPTY VOLUMES',json.dumps(result),flush=True)
    return result


def opening_check(meshes):
    from mathutils.bvhtree import BVHTree
    def tree(name,glass=False):
        obj=meshes[name]
        verts=[obj.matrix_world@v.co for v in obj.data.vertices]
        polys=[tuple(p.vertices) for p in obj.data.polygons
               if (obj.data.materials[p.material_index].name=='Glass')==glass]
        return BVHTree.FromPolygons(verts,polys,all_triangles=True)
    solid=tree('Superstructure')
    companion=tree('HoldCompanion')
    checks=[]
    for opening in OPENINGS:
        kind=opening['kind']
        center=Vector(opening['center'])
        normal=Vector(opening['normal'])
        if kind=='deck_opening':
            blocked=[]
            trees=[tree('ForeWellDeck'),tree('Hull')]
            for x in (6.15,7.4,8.65):
                for y in (-.50,0,.50):
                    for bvh in trees:
                        hit=bvh.ray_cast(Vector((x,y,6.1)),Vector((0,0,-1)),3.5)[0]
                        if hit is not None:
                            blocked.append(tuple(hit))
        else:
            target=companion if opening['id']=='hold_companion_door' else solid
            samples=[center]
            if kind=='porthole':
                samples += [center+Vector((0,.22*math.cos(a),.22*math.sin(a)))
                            for a in np.linspace(0,TAU,12,endpoint=False)]
            else:
                horizontal=Vector((0,1,0)) if normal.x else Vector((1,0,0))
                width=opening['size'][1] if normal.x else opening['size'][0]
                samples += [center+horizontal*(s*(width/2-.06))+Vector((0,0,t))
                            for s in (-1,1) for t in (-.80,.80)]
            blocked=[tuple(hit) for p in samples
                     if (hit:=target.ray_cast(p+normal*.50,-normal,1.0)[0]) is not None]
        checks.append({'id':opening['id'],'blocked_rays':len(blocked),'passed':not blocked})
    hit=tree('Superstructure',True).ray_cast(Vector((5.20,-1.45,7.15)),Vector((-1,0,0)),.5)[0]
    checks.append({'id':'galley_glass_recess','measured_front_x':float(hit.x) if hit else None,
                   'passed':hit is not None and abs(hit.x-4.938)<.001})
    print('OPENINGS',json.dumps(checks),flush=True)
    return checks


def verify(d):
    """Checks run on final mesh vertices, never merely echo layout parameters."""
    def span(name):
        co=object_bounds(OBJECTS[name])
        return co.min(axis=0),co.max(axis=0)
    f_lo,f_hi=span("Funnel")
    fm_lo,fm_hi=span("Foremast")
    mm_lo,mm_hi=span("Mainmast")
    ss_lo,ss_hi=span("Superstructure")
    fc_lo,fc_hi=span("Forecastle")
    pp_lo,pp_hi=span("Poop")
    # Centres use only the actual long vertical mast shaft vertices above 10 m.
    def shaft_center(name):
        co=np.array(BUILDERS[name].vertices)
        co=co[(co[:,2]>10)&(co[:,2]<12)]
        return float((co[:,0].min()+co[:,0].max())/2)
    funnel_co=np.array(BUILDERS["Funnel"].vertices)
    # Surface axis is estimated from the black/white shell, excluding pipe, stars, lip.
    shell=funnel_co[:96*6]
    axis_center=(shell[:,0].min()+shell[:,0].max())/2
    def surface_sample(name,f,y=0,start_z=None):
        obj=OBJECTS[name]
        x=x_at(f)
        hits=[]
        for z in ([start_z-1] if start_z is not None else (10,8,6)):
            ok,loc,norm,index=obj.ray_cast(Vector((x,y,z+1)),Vector((0,0,-1)))
            if ok and norm.z>.5:
                hits.append(float(loc.z))
        assert hits, f"no actual deck ray hit {name}"
        return max(hits)
    rows=[("funnel centre",FUNNEL_F,fraction(axis_center),.02,"fraction"),
          ("foremast",FOREMAST_F,fraction(shaft_center("Foremast")),.02,"fraction"),
          ("mainmast",MAINMAST_F,fraction(shaft_center("Mainmast")),.02,"fraction"),
          ("superstructure start",SUPER_START,fraction(ss_hi[0]),.02,"fraction"),
          ("superstructure end",SUPER_END,fraction(ss_lo[0]),.02,"fraction"),
          ("forecastle end",FORE_END,fraction(fc_lo[0]),.02,"fraction"),
          ("poop start",POOP_START,fraction(pp_hi[0]),.02,"fraction"),
          ("funnel top",FUNNEL_TOP,float(f_hi[2]),FUNNEL_TOP*.15,"m"),
          ("foremast top",FOREMAST_TOP,float(fm_hi[2]),FOREMAST_TOP*.15,"m"),
          ("mainmast top",MAINMAST_TOP,float(mm_hi[2]),MAINMAST_TOP*.15,"m"),
          ("main deck amidships",MAIN_Z,surface_sample("Superstructure",.5,5.945,MAIN_Z+.12),MAIN_Z*.15,"m"),
          ("forecastle deck at 0.13",raised_z(.13),surface_sample("Forecastle",.13),raised_z(.13)*.15,"m")]
    print("\nTARGET vs MEASURED — refinement",PASS,flush=True)
    results=[]
    for label,target,measured,tolerance,unit in rows:
        passed=abs(target-measured)<=tolerance
        print(f"{label:27s} TARGET {target:8.4f} MEASURED {measured:8.4f} {unit:8s} tol {tolerance:.4f} {'PASS' if passed else 'FAIL'}",flush=True)
        results.append(dict(metric=label,target=target,measured=measured,unit=unit,tolerance=tolerance,passed=bool(passed)))
    assert all(r["passed"] for r in results),"side-profile tolerance failed"
    counts={n:len(o.data.polygons) for n,o in OBJECTS.items()}
    bounds=np.concatenate([object_bounds(o) for o in OBJECTS.values()])
    lo,hi=bounds.min(axis=0),bounds.max(axis=0)
    shell=HULL_PRIMARY_VERTICES
    hull_lo,hull_hi=shell.min(axis=0),shell.max(axis=0)
    assert abs((hull_hi-hull_lo)[0]-LENGTH)<.04
    assert abs((hull_hi-hull_lo)[1]-BEAM)<.05
    assert all(o.parent.name == "AmiralLatoucheTreville" for o in OBJECTS.values())
    assert set(OBJECTS) == set(GROUPS)
    for o in OBJECTS.values():
        assert len(o.data.uv_layers)==1
        assert all(len(p.vertices)==3 for p in o.data.polygons)
        assert all(p.area>1e-10 for p in o.data.polygons),f"degenerate triangle {o.name}"
        assert all(np.isfinite(v) for vert in o.data.vertices for v in vert.co)
        assert all(o.data.materials[p.material_index].name in COLORS for p in o.data.polygons)
    assert len(FORE_OBSTACLES)>=21, f"fore obstacles {len(FORE_OBSTACLES)}"
    assert len([c for c in CONNECTIONS if c.get('steps',0)>0])==9
    # Land at the deck edge; an inset landing would bury final steps in its wall.
    for connection in CONNECTIONS:
        if connection['id']=='hold_stairs' or connection.get('steps',0)==0:
            continue
        if connection['to']=='boat_deck':
            assert connection['width']>=.9 and abs(connection['rise'])<=connection['run']
            assert abs(connection['bottom'][2]-T1_Z)<.001 and abs(connection['top'][2]-BOAT_Z)<.001
            continue
        f=FORE_END if connection['to']=='forecastle' else SUPER_START if connection['to']=='superstructure_t2' else POOP_START
        assert abs(connection['top'][0]-x_at(f))<.001,'stairs enter deck wall'
    for name,obj in OBJECTS.items():
        for poly in obj.data.polygons:
            mat=obj.data.materials[poly.material_index].name
            if mat == 'Canvas':
                assert poly.normal.z > .5,(name,'cover faces inward')
            if name == 'Funnel' and mat == 'FunnelStar':
                assert poly.normal.y*poly.center.y > 0,'star faces inside funnel'
    from mathutils.bvhtree import BVHTree
    hull_bvh=BVHTree.FromObject(OBJECTS['Hull'],bpy.context.evaluated_depsgraph_get())
    blade_clearances=[]
    for point in BUILDERS['Propeller'].vertices[PROP_BLADE_START:]:
        point=Vector(point)
        nearest,normal,index,distance=hull_bvh.find_nearest(point)
        assert normal.dot(point-nearest)>-.002,'propeller blade inside hull'
        for direction in (Vector((1,.123,.321)),Vector((-1,.217,.313)),Vector((0,1,.113))):
            direction.normalize()
            origin=point.copy()
            hits=0
            while True:
                crossing,_,_,_=hull_bvh.ray_cast(origin,direction)
                if crossing is None:
                    break
                hits+=1
                assert hits<100,'clearance ray failed to terminate'
                origin=crossing+direction*.0001
            assert hits%2 == 0,'propeller blade inside hull (ray parity)'
        blade_clearances.append(distance)
    # Measure all four boom elevations independently from their endpoint vectors.
    boom_angles=[]
    co=np.array(BUILDERS["Derricks"].vertices)
    # Tagged construction endpoints are recorded separately for reproducible auditing.
    for side in (-1,1):
        for direction in (-1,1):
            a=np.array((x_at(MAINMAST_F),side*.6,MAIN_Z+1.55))
            horizontal=11.4*math.cos(math.radians(50))
            b=a+np.array((direction*horizontal*.94,side*horizontal*.342,11.4*math.sin(math.radians(50))))
            # Find the mesh tip's cap centre (rather than compare only the intended endpoint).
            distance=np.linalg.norm(co-b,axis=1)
            tip=co[np.argmin(distance)]
            angle=math.degrees(math.atan2(tip[2]-a[2],np.linalg.norm((tip-a)[:2])))
            assert abs(angle-50)<.2
            boom_angles.append(angle)
    volumes=empty_volume_check(OBJECTS)
    openings=opening_check(OBJECTS)
    contact_checks=[]
    for identifier,(vertices,faces) in CONTACT_GEOMETRY.items():
        if not identifier.startswith('aft_cowl_'):
            continue
        tree=BVHTree.FromPolygons([Vector(p) for p in vertices],faces)
        for other,(ov,of) in CONTACT_GEOMETRY.items():
            if other.startswith('aft_cowl_'):
                continue
            overlap=tree.overlap(BVHTree.FromPolygons([Vector(p) for p in ov],of))
            contact_checks.append({'object':identifier,'other':other,'overlap_pairs':len(overlap),
                                   'passed':not overlap})
    stair_clearance=[]
    for stair in [o for o in OBSTACLES if o['id'].startswith('boat_deck_stairs')]:
        c,h=np.asarray(stair['center']),np.asarray(stair['size'])/2
        conflicts=[]
        for other in OBSTACLES:
            if other['zone'] not in ('boat_deck','superstructure_t2') or other['kind'] not in ('lifeboat','boat_cradle','davit','stanchion'):
                continue
            oc,oh=np.asarray(other['center']),np.asarray(other['size'])/2
            if np.all(np.minimum(c+h,oc+oh)-np.maximum(c-h,oc-oh)>1e-5):
                conflicts.append(other['id'])
        stair_clearance.append({'id':stair['id'],'conflicts':conflicts,'passed':not conflicts})
    report={"asset_version":4,"refinement_pass":PASS,"target_vs_measured":results,"triangles_per_object":counts,
            "triangles_total":sum(counts.values()),"bounds":{"min":lo.tolist(),"max":hi.tolist(),"size":(hi-lo).tolist()},
            "hull_bounds":{"min":hull_lo.tolist(),"max":hull_hi.tolist()},
            "aft_derrick_angles_degrees":boom_angles,"materials":list(COLORS),
            "propeller_blade_min_hull_clearance_m":min(blade_clearances),
            "uv_channels":1,"uv_metres_per_unit":UV_METRES,"fore_well_obstacle_count":len(FORE_OBSTACLES),
            "interior_volume_check":volumes,"opening_check":openings,
            "boat_stair_clearance_check":stair_clearance,
            "aft_cowl_geometry_clearance_check":contact_checks,
            "new_object_bounds":{name:{'min':object_bounds(OBJECTS[name]).min(axis=0).tolist(),
                                       'max':object_bounds(OBJECTS[name]).max(axis=0).tolist()}
                                 for name in ('GalleyChimney','HoldCompanion')},
            "chimney_bridge_decision":"Fixed chimney coordinates retained; clearance cut in bridge deck. Cap remains inside existing bridge volume.",
            "build_pass3":"not complete yet",
            "fbx_verification":"not run in this build yet",
            "caveats":["Approximate hull lines and deck furniture; no shipyard lines plan supplied.",
                       "Existing decorative windows remain surface geometry; all five v4 access apertures are genuine mesh holes.",
                       "Preview wood grain is procedural Blender shading; replace material slots with project CC0 PBR in Unity.",
                       "Fore derrick heels raised 0.70 m and aft cowls moved 0.52 m forward / 0.17 m outward to clear the requested walking routes."]}
    (d/"validation.json").write_text(json.dumps(report,indent=2)+"\n")
    assert all(v['passed'] for v in volumes.values()),'reserved interior volume occupied'
    assert all(o['passed'] for o in openings),'blocked aperture'
    assert all(c['passed'] for c in stair_clearance),'boat stair conflicts with lifeboat/cradle/davit/column'
    assert all(c['passed'] for c in contact_checks),'shifted aft cowl intersects hatch or stairs'
    print("TRIANGLES",json.dumps(counts),"TOTAL",sum(counts.values()),flush=True)
    print("BOUNDING BOX",lo.tolist(),hi.tolist(),"SIZE",(hi-lo).tolist(),flush=True)
    return report


def render_setup():
    scene=bpy.context.scene
    scene.render.engine='CYCLES'
    scene.cycles.device='CPU'
    scene.cycles.samples=32 if PASS >= 3 else 12
    scene.cycles.seed=1911
    scene.cycles.use_denoising=True
    scene.cycles.max_bounces=5
    scene.render.threads_mode='FIXED'
    scene.render.threads=12
    scene.render.image_settings.file_format='PNG'
    scene.render.image_settings.color_mode='RGBA'
    scene.render.film_transparent=False
    if scene.world is None:
        scene.world=bpy.data.worlds.new('PreviewWorld')
    scene.world.use_nodes=True
    scene.world.node_tree.nodes['Background'].inputs['Color'].default_value=(.36,.39,.42,1)
    scene.world.node_tree.nodes['Background'].inputs['Strength'].default_value=.55
    scene.view_settings.view_transform='AgX'
    light=bpy.data.lights.new('__Sun','SUN')
    light.energy=2.4
    light.angle=math.radians(9)
    sun=bpy.data.objects.new('__Sun',light)
    bpy.context.collection.objects.link(sun)
    sun.rotation_euler=(math.radians(28),math.radians(-22),math.radians(-32))
    area=bpy.data.lights.new('__Fill','AREA')
    area.energy=4200
    area.shape='DISK'
    area.size=70
    fill=bpy.data.objects.new('__Fill',area)
    bpy.context.collection.objects.link(fill)
    fill.location=(25,30,45)
    fill.rotation_euler=(Vector((0,0,8))-fill.location).to_track_quat('-Z','Y').to_euler()
    camdata=bpy.data.cameras.new('__Camera')
    cam=bpy.data.objects.new('__Camera',camdata)
    bpy.context.collection.objects.link(cam)
    scene.camera=cam
    return cam


def render_one(d,name,camera,location,target,orthographic=False,lens=48):
    scene=bpy.context.scene
    camera.location=location
    camera.rotation_euler=(Vector(target)-camera.location).to_track_quat('-Z','Y').to_euler()
    camera.data.type='ORTHO' if orthographic else 'PERSP'
    camera.data.lens=lens
    camera.data.clip_end=1000
    camera.data.clip_start=.05
    camera.data.ortho_scale=SIDE_SCALE
    scene.render.resolution_x=SIDE_WIDTH if orthographic else PREVIEW_WIDTH
    scene.render.resolution_y=SIDE_HEIGHT if orthographic else PREVIEW_HEIGHT
    scene.render.resolution_percentage=100
    scene.render.filepath=str(d/name)
    side = orthographic and name == 'check_side.png'
    if side:
        silhouette=bpy.data.materials.new('__Silhouette')
        silhouette.use_nodes=True
        nodes=silhouette.node_tree.nodes
        nodes.clear()
        emission=nodes.new('ShaderNodeEmission')
        emission.inputs['Color'].default_value=(0,0,0,1)
        output=nodes.new('ShaderNodeOutputMaterial')
        silhouette.node_tree.links.new(emission.outputs[0],output.inputs['Surface'])
        scene.view_layers[0].material_override=silhouette
        scene.world.node_tree.nodes['Background'].inputs['Color'].default_value=(1,1,1,1)
        scene.world.node_tree.nodes['Background'].inputs['Strength'].default_value=1
        scene.view_settings.view_transform='Standard'
        scene.cycles.samples=24 if PASS >= 3 else 4
        scene.cycles.use_denoising=False
        bpy.context.view_layer.update()
        bow=world_to_camera_view(scene,camera,Vector((LENGTH/2,0,0)))
        stern=world_to_camera_view(scene,camera,Vector((-LENGTH/2,0,0)))
        assert bow.x < stern.x,"port camera must put +X bow on image LEFT"
        projection={"bow_px":bow.x*SIDE_WIDTH,"stern_px":stern.x*SIDE_WIDTH,
                    "waterline_px":(1-bow.y)*SIDE_HEIGHT}
        (d/"side_projection.json").write_text(json.dumps(projection,indent=2)+"\n")
    print("RENDER",name,flush=True)
    bpy.ops.render.render(write_still=True)
    if side:
        scene.view_layers[0].material_override=None
        bpy.data.materials.remove(silhouette)
        scene.world.node_tree.nodes['Background'].inputs['Color'].default_value=(.36,.39,.42,1)
        scene.world.node_tree.nodes['Background'].inputs['Strength'].default_value=.55
        scene.view_settings.view_transform='AgX'
        scene.cycles.samples=32 if PASS >= 3 else 12
        scene.cycles.use_denoising=True


def export(d,filename='AmiralLatoucheTreville.fbx',save_blend=True):
    bpy.ops.object.select_all(action='DESELECT')
    root=bpy.data.objects['AmiralLatoucheTreville']
    root.select_set(True)
    for o in OBJECTS.values():
        o.select_set(True)
    bpy.context.view_layer.objects.active=OBJECTS['Hull']
    bpy.ops.export_scene.fbx(filepath=str(d/filename),
        use_selection=True,object_types={'MESH','EMPTY'},path_mode='RELATIVE',
        embed_textures=False,apply_unit_scale=True,bake_space_transform=True,
        add_leaf_bones=False,axis_forward='-Z',axis_up='Y',use_mesh_modifiers=True)
    if not save_blend:
        return
    for obj in list(bpy.data.objects):
        if obj.name.startswith('__'):
            bpy.data.objects.remove(obj,do_unlink=True)
    for collection in list(bpy.data.collections):
        if not collection.objects:
            bpy.data.collections.remove(collection)
    bpy.context.scene.camera=None
    bpy.context.preferences.filepaths.save_version=0
    bpy.ops.wm.save_as_mainfile(filepath=str(d/'AmiralLatoucheTreville.blend'),compress=True)


def make_lods(d,report):
    global LOD, HULL_PRIMARY_VERTICES, PROP_BLADE_START
    originals=dict(OBJECTS)
    builders=dict(BUILDERS)
    primary=HULL_PRIMARY_VERTICES
    blade_start=PROP_BLADE_START
    lods={'LOD0':{'file':'AmiralLatoucheTreville.fbx','triangles_total':report['triangles_total'],
                   'triangles_per_object':report['triangles_per_object'],'ratio':1.0}}
    for level in (1,2):
        for name,obj in originals.items():
            obj.name='__Original_'+name
        build_ship(level)
        bpy.context.view_layer.update()
        counts={name:len(obj.data.polygons) for name,obj in OBJECTS.items()}
        ratio=sum(counts.values())/report['triangles_total']
        volumes=empty_volume_check(OBJECTS)
        holes=opening_check(OBJECTS)
        bounds=np.concatenate([object_bounds(o) for o in OBJECTS.values()])
        lods[f'LOD{level}']={'file':f'AmiralLatoucheTreville_LOD{level}.fbx',
            'triangles_total':sum(counts.values()),'triangles_per_object':counts,'ratio':ratio,
            'bounds':{'min':bounds.min(axis=0).tolist(),'max':bounds.max(axis=0).tolist()},
            'interior_volume_check':volumes,'opening_check':holes,
            'methods':['omit fine rigging and sub-0.15m furniture','panel railings',
                       'fewer cylinder sides and curved-path sections',
                       'simplified plank bevels' if level==1 else 'continuous deck surfaces without plank meshes',
                       *(['coarse hatch covers and propeller blades; omit small hardware and covered boat interiors'] if level==2 else [])]}
        print(f'LOD{level} TRIANGLES {sum(counts.values())} RATIO {ratio:.6f}',flush=True)
        (d/'lod_build.json').write_text(json.dumps(lods,indent=2)+'\n')
        assert all(v['passed'] for v in volumes.values()),f'LOD{level} reserved volume occupied'
        assert all(h['passed'] for h in holes),f'LOD{level} closed aperture'
        lo,hi=(.30,.40) if level==1 else (.08,.12)
        assert lo<=ratio<=hi,f'LOD{level} triangle ratio {ratio:.6f} outside [{lo},{hi}]'
        bpy.data.objects['AmiralLatoucheTreville']['lod']=level
        export(d,lods[f'LOD{level}']['file'],save_blend=False)
        for obj in list(OBJECTS.values()):
            mesh=obj.data
            bpy.data.objects.remove(obj,do_unlink=True)
            bpy.data.meshes.remove(mesh)
        for name,obj in originals.items():
            obj.name=name
    OBJECTS.clear(); OBJECTS.update(originals)
    BUILDERS.clear(); BUILDERS.update(builders)
    HULL_PRIMARY_VERTICES=primary
    PROP_BLADE_START=blade_start
    LOD=0
    bpy.data.objects['AmiralLatoucheTreville']['lod']=0
    report['lods']=lods
    return lods


def preview_top_overlay(d,camera):
    """Project the exported JSON onto the render; PNG encoding uses stdlib only."""
    data=json.loads((d/'deck_zones.json').read_text())
    scene=bpy.context.scene
    source=bpy.data.images.load(str(d/'preview_top.png'),check_existing=False)
    width,height=source.size
    values=np.empty(width*height*4,dtype=np.float32)
    source.pixels.foreach_get(values)
    linear=values.reshape(height,width,4)[::-1,:,:3]
    pixels=np.clip(np.where(linear<=.0031308,linear*12.92,1.055*linear**(1/2.4)-.055),0,1).copy()
    def project(points,z):
        projected=[world_to_camera_view(scene,camera,Vector((p[0],p[1],z))) for p in points]
        return [(p.x*width,(1-p.y)*height) for p in projected]
    def line(points,color,thickness=2):
        for a,b in zip(points,points[1:]):
            count=max(1,math.ceil(max(abs(a[0]-b[0]),abs(a[1]-b[1]))))
            xs=np.rint(np.linspace(a[0],b[0],count+1)).astype(int)
            ys=np.rint(np.linspace(a[1],b[1],count+1)).astype(int)
            for dx in range(-thickness//2,thickness//2+1):
                for dy in range(-thickness//2,thickness//2+1):
                    xx,yy=xs+dx,ys+dy
                    ok=(xx>=0)&(xx<width)&(yy>=0)&(yy<height)
                    pixels[yy[ok],xx[ok]]=color
    def polygon(points,color,alpha):
        if not points:
            return
        for y in range(max(0,int(min(p[1] for p in points))),min(height,int(max(p[1] for p in points))+1)):
            scan=y+.5
            crossing=sorted(a[0]+(b[0]-a[0])*(scan-a[1])/(b[1]-a[1])
                for a,b in zip(points,points[1:]+points[:1]) if min(a[1],b[1])<=scan<max(a[1],b[1]))
            for lo,hi in zip(crossing[::2],crossing[1::2]):
                left,right=max(0,int(math.ceil(lo))),min(width,int(math.ceil(hi)))
                pixels[y,left:right]=pixels[y,left:right]*(1-alpha)+np.asarray(color)*alpha
        line(points+points[:1],color)
    for name,zone in data['zones'].items():
        if name=='hold':
            continue
        for poly in zone['walkable_polygons']:
            polygon(project(poly,zone['deck_surface_z']),(.18,.90,.42),.14)
    for name,zone in data['zones'].items():
        if name=='hold':
            continue
        for box_data in zone['obstacles']:
            color=(.95,.24,.20) if box_data['nav_blocking'] else (.80,.45,.62)
            polygon(project(rectangle(box_data['center'],box_data['size'],box_data['yaw_deg']),
                            zone['deck_surface_z']),color,.20)
        for edge in zone['edges']:
            if edge.get('open'):
                line(project(edge['points'],zone['deck_surface_z']),(.18,.78,1.),3)
    for connection in data['connections']:
        if connection['steps']:
            a,b=connection['bottom'],connection['top']
            poly=rectangle(((a[0]+b[0])/2,a[1]),(abs(b[0]-a[0]),connection['width']))
            polygon(project(poly,max(a[2],b[2])),(1.,.76,.08),.30)
            line(project([a,b],max(a[2],b[2])),(1.,.76,.08),3)
    rgb=np.rint(pixels*255).astype(np.uint8)
    raw=b''.join(b'\0'+row.tobytes() for row in rgb)
    def chunk(kind,body):
        return struct.pack('>I',len(body))+kind+body+struct.pack('>I',zlib.crc32(kind+body)&0xffffffff)
    png=b'\x89PNG\r\n\x1a\n'+chunk(b'IHDR',struct.pack('>IIBBBBB',width,height,8,2,0,0,0))
    png+=chunk(b'IDAT',zlib.compress(raw,6))+chunk(b'IEND',b'')
    (d/'preview_top.png').write_bytes(png)
    bpy.data.images.remove(source)
    print('TOP OVERLAY from deck_zones.json: green walkable; red blockers; pink overhead/stair envelopes; yellow stairs; blue open edges',flush=True)


def write_report(d,validation,zones):
    verification=validation.get('fbx_verification','not run')
    lines=['# make_ship.py v4 — Amiral Latouche-Tréville','',
        '## Thay đổi','',
        '- Dựng vỏ T1 mỏng 0,04 m, ba lỗ xuyên vách; bỏ cửa sổ giả trùng khu bếp/cửa hông.',
        '- Bỏ cả mặt sàn gỗ và mặt thép trong thể tích bếp; khoét sàn xuống hầm.',
        '- Thêm GalleyChimney, HoldCompanion; hai cầu thang thật lên boong xuồng với tay vịn.',
        '- Giữ 18 tên mesh cũ, thêm hai mesh yêu cầu; giữ 14 vật liệu, mét, gốc/trục và kích thước thân tàu.',
        '- JSON v2: collider mọi boong, lan can/mép hở, 9 cầu thang + 4 lối nối đồng mức, thể tích nội thất và biến đổi tọa độ.',
        '- Đưa lan can ngang cánh buồng lái ra mép thật x=4,8399 / y=±6,90; landing cầu xuồng thông với boong chính phía sau.',
        '- Tách collider thân buồng boong đuôi và mái trên cao; bổ sung ô tròn nhô trên vách vào vật cản.',
        '- LOD1/2 dựng lại cùng layout, tên mesh và vật liệu; giữ các lỗ mở.',
        '- LOD2 dùng sàn liên tục ở cao độ danh định, thay cả lớp ván đã bỏ.',
        '- Để thông đường sau khi nở collider: nâng gốc hai cần trước 0,70 m; dịch hai ống thông gió sau +0,52 m theo X và 0,17 m ra ngoài.',
        '', '## Tọa độ thực tế (Blender, mét)','']
    for opening in zones['openings']:
        dimensions=f"radius={opening['radius']}, outer_radius={opening['outer_radius']}" if 'radius' in opening else f"size={opening['size']}"
        lines.append(f"- {opening['id']}: center={opening['center']}; {dimensions}; normal={opening['normal']}.")
    for name in ('GalleyChimney','HoldCompanion'):
        co=object_bounds(OBJECTS[name]) if name in OBJECTS else None
        bounds=validation['new_object_bounds'][name] if co is None else {'min':co.min(axis=0).tolist(),'max':co.max(axis=0).tolist()}
        lines.append(f'- {name}: mesh bounds min={bounds["min"]}, max={bounds["max"]}.')
    for c in zones['connections']:
        if c['id'].startswith('boat_deck_stairs') or c['id']=='hold_stairs':
            lines.append(f"- {c['id']}: bottom={c['bottom']}, top={c['top']}, width={c['width']}, steps={c['steps']}, rise(total)={c['rise']}, run(total)={c['run']}.")
            if 'landing' in c:
                lines.append(f"  Landing trên boong: {c['landing']}; mép mở lối thang rộng 1,20 m.")
            if 'approach' in c:
                lines.append(f"  Điểm tiếp cận cửa: {c['approach']}; hướng xuống +X, step_rise=0,20 m, step_run=0,25 m; Unity dựng bậc.")
    for adjustment in zones['layout_adjustments']:
        lines.append(f"- Điều chỉnh {adjustment['id']}: {json.dumps(adjustment,ensure_ascii=False)}")
    lines += ['',f"Khối bếp: `{GALLEY_TRANSFORM['formula']}`.",
              'Ống bếp: tâm (2.40, -3.60), thân r=0.22, z=8.45…11.60; chụp cao đến z=11.87.',
              'Mái che: chân x=6.00…8.40, y=-0.70…0.70, đỉnh z=7.70. Khung cửa nhô 0.05 m phía ngoài.',
              'Cầu xuồng: rise/run = 2.90/3.20 m, dốc 42.18°, rộng 1.00 m; không chạm xuồng/giá/cột.',
              'Mép hở cầu tàu Marseille: x=12.464…14.464, mạn phải; lấy từ tọa độ cảnh hiện có.',
              'Dải chết sau cột cánh buồng lái (witness x=-0,907083, y=±6,66) được ghi excluded_clearance_pockets và loại khỏi đa giác đi được.',
              '', '## Tam giác','', '| LOD | Tam giác | Tỷ lệ LOD0 |','|---|---:|---:|']
    for key,lod in validation.get('lods',{}).items():
        lines.append(f"| {key} | {lod['triangles_total']:,} | {lod['ratio']:.2%} |")
    lines += ['', '## Kiểm chứng đã chạy','',f"- Pass 3 / render: {validation['build_pass3'].rstrip('.')}."]
    for key,lod in validation.get('lods',{}).items():
        for name,v in lod.get('interior_volume_check',validation['interior_volume_check']).items():
            lines.append(f"- {key} / {name}: {v['vertices_inside']} đỉnh trong khối; {v['faces_intersecting']} mặt giao khối; ngoại lệ dùng: {v['allowed_vertices_excluded']}.")
    nav=zones['nav_check']
    lines += [f"- Nav: {'PASS' if nav['passed'] else 'FAIL'}; vùng không tới được: {nav['unreachable_regions']}; mảnh không tới được: {nav['unreachable_fragments']}; liên kết không gắn được: {nav['unattached_connections']}.",
              '- Nav dùng phân hoạch cạnh/đa giác với offset vật cản, lan can/thành mạn và lỗ sàn 0,30 m; không dùng raster. Kiểm từng đa giác và từng mảnh qua landing của liên kết.',
              '- Nav dùng chiều cao nhân vật 1,75 m, stepOffset 0,30 m đọc từ PlayerBuilder.cs; lỗ sàn cũng có khoảng lùi 0,30 m.',
              f"- Hai cầu xuồng: {json.dumps(validation['boat_stair_clearance_check'])}.",
              f"- Ống thông gió sau so với hầm hàng/cầu thang: {json.dumps(validation['aft_cowl_geometry_clearance_check'])}.",
              f"- FBX roundtrip: {verification.rstrip('.')}.",
              '', '## Giới hạn / chưa chạy','',
              '- Unity không chạy và Game không sửa, đúng phạm vi. ShipDeck.cs hiện dùng collider ghi cứng; JSON v2 chưa được tích hợp vào runtime.',
              '- Sàn/phòng bếp và bậc thang hầm do Unity dựng; FBX chỉ cung cấp vỏ, lỗ mở và dữ liệu.',
              '- Phần chụp ống bếp z=11.60…11.87 nằm trong thể tích buồng lái hiện có; xem quyết định giao cắt trong validation.',
              '- Kiểm nav là hình học collider đã xuất; không thay thế kiểm điều khiển nhân vật trong Unity.',
              '- Pillow có sẵn chỉ dùng bước compare cũ; không cài addon/package. Vẽ overlay mới dùng stdlib + numpy.',
              '', 'Preview top: xanh lá = đa giác; đỏ = vật cản; hồng = vật trên cao / envelope cầu thang; vàng = cầu thang; xanh dương = mép hở.','']
    text_report='\n'.join(lines)
    (d/'REPORT_v4.md').write_text(text_report)
    print(text_report,flush=True)


def verify_fbx():
    expected=json.loads((OUT/'validation.json').read_text())
    lod_results={}
    # Existing exporter bakes vertices to cm/Y-up. Blender restores model-space
    # metres/Z-up with this shared root basis, rather than identity node matrices.
    fbx_basis=np.array([[.01,0,0,0],[0,0,-.01,0],[0,.01,0,0],[0,0,0,1]])
    for key,lod in expected['lods'].items():
        bpy.ops.wm.read_factory_settings(use_empty=True)
        bpy.ops.import_scene.fbx(filepath=str(OUT/lod['file']))
        meshes={o.name:o for o in bpy.data.objects if o.type=='MESH'}
        assert set(meshes)==set(GROUPS),(key,'object names')
        root=bpy.data.objects['AmiralLatoucheTreville']
        assert np.allclose(np.asarray(root.matrix_world),fbx_basis,atol=1e-7),(key,'root unit/axis basis')
        assert not any(o.type in {'CAMERA','LIGHT','ARMATURE'} for o in bpy.data.objects)
        assert all(len(o.data.uv_layers)==1 for o in meshes.values())
        assert set(bpy.data.materials.keys())==set(COLORS),(key,'material set',list(bpy.data.materials.keys()))
        for name,o in meshes.items():
            tris=sum(len(p.vertices)-2 for p in o.data.polygons)
            assert tris==lod['triangles_per_object'][name],(key,name,tris)
            assert o.parent and o.parent.name=='AmiralLatoucheTreville'
            assert np.allclose(np.asarray(o.matrix_world),fbx_basis,atol=1e-7),(key,name,'changed origin/axes')
            assert np.allclose(np.asarray(o.matrix_local),np.eye(4),atol=1e-5),(key,name,'mesh local transform')
            assert [m.name for m in o.data.materials]==list(COLORS),(key,name,'material slots')
            assert all(o.data.materials[p.material_index].name in COLORS for p in o.data.polygons)
            assert all(p.area*.0001>1e-10 for p in o.data.polygons),(key,name,'degenerate world-space faces')
        co=np.concatenate([object_bounds(o) for o in meshes.values()])
        bounds=expected['bounds'] if key=='LOD0' else lod['bounds']
        assert np.allclose(co.min(axis=0),bounds['min'],atol=.003),(key,'minimum bounds')
        assert np.allclose(co.max(axis=0),bounds['max'],atol=.003),(key,'maximum bounds')
        volumes=empty_volume_check(meshes)
        holes=opening_check(meshes)
        assert all(v['passed'] for v in volumes.values()),(key,'interior volume')
        assert all(h['passed'] for h in holes),(key,'aperture')
        lod_results[key]={'fbx_roundtrip':'PASS','mesh_objects':len(meshes),
            'triangles':lod['triangles_total'],'interior_volume_check':volumes,'opening_check':holes,
            'material_slots':list(COLORS),'origin_axes':'shared cm/Y-up basis and identity local transforms; world model coordinates verified',
            'fbx_to_blender_basis':fbx_basis.tolist()}
    zones=json.loads((OUT/'deck_zones.json').read_text())
    CONNECTIONS.clear(); CONNECTIONS.extend(zones['connections'])
    recomputed=nav_check(zones['zones'])
    assert recomputed==zones['nav_check'],'nav result does not match reloaded JSON'
    assert recomputed['passed'],'reloaded navigation is disconnected'
    result={"fbx_roundtrip":"PASS","lods":lod_results,
            "hierarchy":"flat under AmiralLatoucheTreville","bounds_tolerance_metres":.003,
            "nav_json_recheck":"PASS: stored result equals analytic recomputation",
            "nav_reachability":"PASS" if recomputed['passed'] else "FAIL",
            "cameras":0,"lights":0,"armatures":0}
    (OUT/'fbx_verification.json').write_text(json.dumps(result,indent=2)+"\n")
    expected['fbx_verification']='PASS: real imports of LOD0/LOD1/LOD2; volumes, apertures, names, materials, shared cm/Y-up basis, hierarchy and world bounds. Reloaded nav also passed.'
    expected['fbx_roundtrip']=lod_results
    (OUT/'validation.json').write_text(json.dumps(expected,indent=2)+'\n')
    write_report(OUT,expected,zones)
    print(json.dumps(result,indent=2),flush=True)


def build_ship(level):
    global LOD
    LOD=level
    BUILDERS.clear(); OBJECTS.clear()
    BUILDERS.update({name:Mesh(name) for name in GROUPS})
    hull()
    deck(BUILDERS['Forecastle'],0,FORE_END,raised_z,inset=0,body=True)
    if not LOD:
        cap=np.asarray(BUILDERS['Forecastle'].vertices[200:204])
        obstacle(BUILDERS['Forecastle'],0,'forecastle_riser','deck_bulkhead','fore_well',
                 vertices=np.concatenate([cap,cap+np.array((.04,0,0))]))
    deck(BUILDERS['ForeWellDeck'],FORE_END,SUPER_START,MAIN_Z)
    deck(BUILDERS['Hull'],.82,.91,MAIN_Z)
    deck(BUILDERS['Poop'],POOP_START,1,raised_z,inset=0,body=True)
    if not LOD:
        cap=np.asarray(BUILDERS['Poop'].vertices[:4])
        obstacle(BUILDERS['Poop'],0,'poop_riser','deck_bulkhead','aft_well',
                 vertices=np.concatenate([cap,cap+np.array((-.04,0,0))]))
    houses()
    fore_fittings()
    bow_fittings()
    aft_fittings()
    new_access()
    mast('Foremast',FOREMAST_F,FOREMAST_TOP,25.0)
    mast('Mainmast',MAINMAST_F,MAINMAST_TOP,20.0)
    derricks()
    funnel()
    boats()
    railing(0,FORE_END,raised_z)
    railing(FORE_END,SUPER_START,MAIN_Z,True)
    railing(.46,.82,MAIN_Z,width_limit=7.05)
    railing(.46,.82,T1_Z,width_limit=6.9)
    railing(.48,.80,BOAT_Z,width_limit=6.9)
    railing(.82,.91,MAIN_Z,True)
    railing(.91,1,raised_z)
    underwater()
    for side in (-1,1):
        for f in np.linspace(.055,.92,54):
            porthole(BUILDERS['Hull'],x_at(f),side*(half_width(f)+.024),4.34,side)
        for f in np.linspace(.085,.238,11):
            porthole(BUILDERS['Forecastle'],x_at(f),side*(half_width(f)-.12),7.04,side,.15)
    name_text()
    for name,m in BUILDERS.items():
        print('MESH',name,'vertices',len(m.vertices),flush=True)
        clear_mesh_volumes(m)
        m.object()

def main():
    global PASS
    args=sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else []
    parser=argparse.ArgumentParser()
    parser.add_argument('--pass',dest='refinement',type=int,choices=range(4),default=3)
    parser.add_argument('--verify-fbx',action='store_true')
    options=parser.parse_args(args)
    if options.verify_fbx:
        verify_fbx()
        return
    PASS=options.refinement
    d=OUT if PASS==3 else OUT/'_scratch'/f'pass_{PASS}'
    d.mkdir(parents=True,exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene=bpy.context.scene
    scene.unit_settings.system='METRIC'
    scene.unit_settings.scale_length=1.0
    root=bpy.data.objects.new('AmiralLatoucheTreville',None)
    bpy.context.collection.objects.link(root)
    root['ship']='AMIRAL LATOUCHE-TRÉVILLE'
    root['year']=1911
    root['refinement_pass']=PASS
    root['length_m']=LENGTH
    root['beam_m']=BEAM
    root['origin']='waterline amidships; bow +X, port +Y, up +Z'
    materials()
    print('BUILD refinement',PASS,flush=True)
    build_ship(0)
    bpy.context.view_layer.update()
    report=verify(d)
    zones=write_zones(d)
    report['nav_check']=zones['nav_check']
    (d/'validation.json').write_text(json.dumps(report,indent=2)+'\n')
    if not zones['nav_check']['passed']:
        write_report(d,report,zones)
        raise AssertionError('navigation check failed; see validation.json and REPORT_v4.md')
    if PASS == 3:
        make_lods(d,report)
        (d/'validation.json').write_text(json.dumps(report,indent=2)+'\n')
    write_report(d,report,zones)
    camera=render_setup()
    render_one(d,'check_side.png',camera,(0,240,14.3),(0,0,14.3),True)
    render_one(d,'preview_34.png',camera,(110,107,65),(0,0,9),lens=48)
    render_one(d,'preview_stern.png',camera,(-102,89,45),(-6,0,9),lens=43)
    render_one(d,'preview_deck.png',camera,(27.0,4.90,MAIN_Z+1.6),(-6,-.5,10.5),lens=20)
    render_one(d,'preview_front.png',camera,(15,-3,9.5),(4.3,-.8,8.5),lens=28)
    render_one(d,'preview_top.png',camera,(0,0,110),(0,0,0),True)
    preview_top_overlay(d,camera)
    if PASS == 3:
        export(d)
    subprocess.run([HOST_PYTHON,str(Path(__file__).resolve()),'--compare',str(d)],check=True)
    report['build_pass3']=('PASS' if zones['nav_check']['passed'] else 'PARTIAL: navigation FAIL')+': full mesh validation, LOD generation, six renders and existing photo comparison completed.'
    report['rendered_previews']=['check_side.png','preview_34.png','preview_stern.png','preview_deck.png','preview_front.png','preview_top.png','compare_side.png']
    report['collision_obstacle_counts']={name:len(zone['obstacles']) for name,zone in zones['zones'].items()}
    (d/'validation.json').write_text(json.dumps(report,indent=2)+'\n')
    write_report(d,report,zones)
    assert zones['nav_check']['passed'],'navigation fragments require a deck-layout decision; artifacts exported with nav FAIL'
    print('SHIP COMPLETE',d,flush=True)


if __name__=='__main__':
    main()
