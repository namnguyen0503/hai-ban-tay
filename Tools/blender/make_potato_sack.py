"""Blender 4.2: make a 1911 jute potato sack; -- <out_dir>."""
import datetime
import hashlib
import json
import math
import random
import sys
from pathlib import Path

import bmesh
import bpy
import numpy as np
from mathutils import Vector


ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / "Staging/art_hq/Surfaces/Burlap"
SIZE = 2048
SEED = 1911
TAU = math.tau
ANGULAR = 160
ROWS = 96
TILE_METERS = 0.75
ATLAS_METERS = 1.40
SIDE_ANGLE = 0.12
PROFILE = [
    (0.00, 0.168, 0.119), (0.04, 0.199, 0.145),
    (0.14, 0.215, 0.155), (0.30, 0.207, 0.153),
    (0.47, 0.188, 0.143), (0.61, 0.174, 0.129),
    (0.70, 0.146, 0.108), (0.77, 0.088, 0.070),
    (0.82, 0.037, 0.030), (0.85, 0.036, 0.029),
    (0.90, 0.057, 0.041), (0.95, 0.081, 0.057),
    (1.00, 0.095, 0.066),
]
RNG = random.Random(SEED)
BULGES = [
    (RNG.uniform(0, TAU), RNG.uniform(0.10, 0.65),
     RNG.uniform(0.015, 0.027), RNG.uniform(0.21, 0.36),
    RNG.uniform(0.042, 0.073)) for _ in range(27)
]
BULGES += [(4.62,0.49,0.015,0.22,0.049), (5.11,0.40,0.018,0.24,0.047),
           (4.10,0.36,0.015,0.22,0.046), (5.45,0.23,0.014,0.25,0.046),
           (4.60,0.14,0.012,0.23,0.042)]
GATHERS = [(i * TAU / 15 + RNG.uniform(-0.10, 0.10),
            RNG.uniform(0.015, 0.028)) for i in range(15)]


def smooth(a, b, x):
    t = max(0.0, min(1.0, (x - a) / (b - a)))
    return t * t * (3 - 2 * t)


def angle_delta(a, b):
    return (a - b + math.pi) % TAU - math.pi


def profile(t, column):
    for i in range(len(PROFILE) - 1):
        if t <= PROFILE[i + 1][0]:
            break
    a, b = PROFILE[i], PROFILE[i + 1]
    p, q = PROFILE[max(0, i - 1)], PROFILE[min(len(PROFILE) - 1, i + 2)]
    dt = b[0] - a[0]
    s = max(0, min(1, (t - a[0]) / dt))
    ma = (b[column] - p[column]) / (b[0] - p[0])
    mb = (q[column] - a[column]) / (q[0] - a[0])
    return ((2*s**3 - 3*s*s + 1) * a[column]
            + (s**3 - 2*s*s + s) * dt * ma
            + (-2*s**3 + 3*s*s) * b[column]
            + (s**3 - s*s) * dt * mb)


def surface(t, theta):
    rx, ry = profile(t, 1), profile(t, 2)
    power = 2 / (2.0 + 0.48 * (1 - smooth(0.15, 0.62, t)))
    c, s = math.cos(theta), math.sin(theta)
    x = rx * math.copysign(abs(c)**power, c)
    y = ry * math.copysign(abs(s)**power, s)
    displacement = 0.0
    for a, h, amplitude, aw, hw in BULGES:
        d = (angle_delta(theta, a) / aw)**2 + ((t-h) / hw)**2
        displacement += amplitude * (math.exp(-0.5*d) - 0.22*math.exp(-0.18*d))
    displacement += 0.0035 * math.sin(3*theta + 8*t) * math.sin(math.pi*t)**2
    gather = math.exp(-((t - 0.745) / 0.205)**2) * (1-0.65*smooth(0.79,0.835,t))
    for a, amplitude in GATHERS:
        bend = a + 0.14 * math.sin(7*t + a)
        displacement -= amplitude * gather * math.exp(
            -(angle_delta(theta, bend) / 0.088)**2)
    # Loaded cloth sags between potatoes; shallow diagonal creases.
    front = math.exp(-(angle_delta(theta, 4.55)/1.15)**4)
    crease = 0.255 + 0.055*math.sin(1.7*theta+0.6)
    displacement -= 0.007*front*math.exp(-((t-crease)/0.016)**2)
    displacement += 0.003*front*math.exp(-((t-crease-0.025)/0.022)**2)
    diagonal = 0.51 + 0.10*math.sin(theta+0.9)
    pinch = math.exp(-(angle_delta(theta,4.35)/0.75)**4)
    displacement -= 0.011*pinch*math.exp(-((t-diagonal)/0.012)**2)
    displacement += 0.004*pinch*math.exp(-((t-diagonal-0.023)/0.025)**2)
    displacement -= 0.003 * math.exp(-(angle_delta(theta, SIDE_ANGLE) / 0.023)**2)
    displacement += 0.005 * math.sin(11*theta + 2*t) * math.exp(-((t-0.075)/0.065)**2)
    # Compressed neck and a loose, uneven folded mouth.
    displacement += 0.0036 * math.cos(15*theta + 0.5) * math.exp(-((t-0.837)/0.052)**2)
    fringe = smooth(0.855, 1.0, t)
    displacement += fringe * (0.008*math.sin(11*theta + 0.3)*(1+0.35*math.sin(3*theta))
                              + 0.007*math.sin(7*theta - 0.9) + 0.004*math.sin(3*theta))
    x += displacement * c + 0.033 * t**1.5
    y += displacement * s + 0.006 * math.sin(3*t)
    z = 0.008 + 0.576*t
    z += fringe**2 * (0.004*math.sin(11*theta + 0.8) + 0.009*math.sin(5*theta)
                      - 0.006*math.cos(2*theta+0.4))
    z += 0.003 * math.sin(4*theta + 5*t) * math.sin(math.pi*t)**2
    return Vector((x, y, z))


class MeshBuilder:
    def __init__(self):
        self.vertices, self.faces, self.uvs = [], [], []

    def vertex(self, co):
        self.vertices.append(tuple(co))
        return len(self.vertices) - 1

    def face(self, indices, uvs):
        self.faces.append(indices)
        self.uvs.append(uvs)

    def object(self, name):
        mesh = bpy.data.meshes.new(name)
        mesh.from_pydata(self.vertices, [], self.faces)
        mesh.update()
        uv = mesh.uv_layers.new(name="SackAtlas")
        for polygon, coords in zip(mesh.polygons, self.uvs):
            polygon.use_smooth = True
            for loop, xy in zip(polygon.loop_indices, coords):
                uv.data[loop].uv = xy
        obj = bpy.data.objects.new(name, mesh)
        bpy.context.collection.objects.link(obj)
        return obj


def path_samples(keys, steps):
    keys = [Vector(p) for p in keys]
    result = []
    for segment in range(len(keys)-1):
        p0, p1 = keys[max(0, segment-1)], keys[segment]
        p2, p3 = keys[segment+1], keys[min(len(keys)-1, segment+2)]
        for j in range(steps):
            t = j / steps
            result.append(0.5 * ((2*p1) + (-p0+p2)*t
                          + (2*p0-5*p1+4*p2-p3)*t*t
                          + (-p0+3*p1-3*p2+p3)*t*t*t))
    return result + [keys[-1]]


def frames(points):
    tangents = [(points[min(i+1, len(points)-1)]
                 - points[max(0, i-1)]).normalized() for i in range(len(points))]
    normal = tangents[0].cross(Vector((0, 0, 1)))
    if normal.length < 0.1:
        normal = tangents[0].cross(Vector((0, 1, 0)))
    normal.normalize()
    result, distance = [], 0.0
    for i, tangent in enumerate(tangents):
        if i:
            distance += (points[i] - points[i-1]).length
        normal = (normal - tangent * normal.dot(tangent)).normalized()
        result.append((normal.copy(), tangent.cross(normal).normalized(), distance))
    return result


def tube(builder, points, radius, sides=6, rope=False):
    points = [Vector(p) for p in points]
    basis = frames(points)
    start = len(builder.vertices)
    for p, (n, b, distance) in zip(points, basis):
        for j in range(sides):
            phi = TAU*j/sides
            builder.vertex(p + radius * (n*math.cos(phi) + b*math.sin(phi)))

    def uv(i, j):
        if rope:
            return (0.30 + 0.66*basis[i][2]/0.65, 0.035 + 0.12*j/sides)
        return (0.305 + 0.66*basis[i][2]/0.65, 0.065 + 0.015*j/sides)

    for i in range(len(points)-1):
        for j in range(sides):
            k = (j+1) % sides
            builder.face((start+i*sides+j, start+i*sides+k,
                          start+(i+1)*sides+k, start+(i+1)*sides+j),
                         (uv(i,j), uv(i,j+1), uv(i+1,j+1), uv(i+1,j)))
    for i, reverse in ((0, True), (len(points)-1, False)):
        center = builder.vertex(points[i])
        for j in range(sides):
            indices = (center, start+i*sides+j, start+i*sides+(j+1)%sides)
            coords = (uv(i, 0.5*sides), uv(i,j), uv(i,j+1))
            if reverse:
                indices, coords = indices[::-1], coords[::-1]
            builder.face(indices, coords)


def two_ply(builder, points):
    basis = frames(points)
    for strand in range(2):
        helix = []
        for p, (n, b, distance) in zip(points, basis):
            phi = TAU*distance/0.019 + strand*math.pi
            helix.append(p + 0.0018 * (n*math.cos(phi) + b*math.sin(phi)))
        tube(builder, helix, 0.00225, sides=8, rope=True)


def make_bag():
    builder = MeshBuilder()
    rings, uv_rings, lengths = [], [], []
    for i in range(ROWS+1):
        ring = [surface(i/ROWS, math.pi/2 + TAU*j/ANGULAR) for j in range(ANGULAR)]
        distances = [0.0]
        for j in range(ANGULAR):
            distances.append(distances[-1] + (ring[(j+1)%ANGULAR]-ring[j]).length)
        rings.append([builder.vertex(p) for p in ring])
        lengths.append(distances[-1])
        uv_rings.append(distances)
    # Arc-length UVs: straight warp/weft, one cut at the back.
    vertical = [0.0]
    for i in range(1, ROWS+1):
        vertical.append(vertical[-1] + sum(
            (Vector(builder.vertices[rings[i][j]]) - Vector(builder.vertices[rings[i-1][j]])).length
            for j in range(ANGULAR)) / ANGULAR)
    vertical = [0.32 + v/ATLAS_METERS for v in vertical]

    def uv(i, j):
        return (0.5 + (uv_rings[i][j]-lengths[i]/2)/ATLAS_METERS, vertical[i])

    for i in range(ROWS):
        for j in range(ANGULAR):
            k = (j+1) % ANGULAR
            builder.face((rings[i][j], rings[i][k], rings[i+1][k], rings[i+1][j]),
                         (uv(i,j), uv(i,j+1), uv(i+1,j+1), uv(i+1,j)))
    # Flat underside, curved transitions to the weighted body.
    center = builder.vertex((0, 0, 0))
    inner = None
    for step in range(1, 7):
        fraction = step/6
        if step == 6:
            ring = rings[0]
        else:
            ring = [builder.vertex((builder.vertices[v][0]*fraction,
                                    builder.vertices[v][1]*fraction,
                                    0.008*smooth(0.60, 1.0, fraction))) for v in rings[0]]

        def bottom_uv(j, f):
            theta = math.pi/2 + TAU*j/ANGULAR
            return (0.12 + 0.099*f*math.cos(theta), 0.114 + 0.087*f*math.sin(theta))

        for j in range(ANGULAR):
            k = (j+1) % ANGULAR
            if inner is None:
                builder.face((center, ring[k], ring[j]),
                             ((0.12,0.114), bottom_uv(j+1,fraction), bottom_uv(j,fraction)))
            else:
                builder.face((inner[j], inner[k], ring[k], ring[j])[::-1],
                             (bottom_uv(j,(step-1)/6), bottom_uv(j+1,(step-1)/6),
                              bottom_uv(j+1,fraction), bottom_uv(j,fraction))[::-1])
        inner = ring
    # The mouth has thickness and a recessed cloth interior, no open mesh edges.
    inside = []
    for i in range(13):
        t = 1.0 - 0.16*i/12
        ring = []
        for j in range(ANGULAR):
            theta = math.pi/2 + TAU*j/ANGULAR
            p = surface(t, theta)
            p -= Vector((math.cos(theta), math.sin(theta), 0))*0.0018
            ring.append(builder.vertex(p))
        inside.append(ring)

    def interior_uv(i, j):
        return (0.27 + 0.45*j/ANGULAR, 0.292 - 0.077*i/12)

    for j in range(ANGULAR):
        k = (j+1) % ANGULAR
        builder.face((rings[-1][j], rings[-1][k], inside[0][k], inside[0][j]),
                     (uv(ROWS,j), uv(ROWS,j+1), interior_uv(0,j+1), interior_uv(0,j)))
    for i in range(12):
        for j in range(ANGULAR):
            k = (j+1) % ANGULAR
            builder.face((inside[i][j], inside[i][k], inside[i+1][k], inside[i+1][j]),
                         (interior_uv(i,j), interior_uv(i,j+1),
                          interior_uv(i+1,j+1), interior_uv(i+1,j)))
    center = builder.vertex(sum((Vector(builder.vertices[v]) for v in inside[-1]), Vector())/ANGULAR)
    for j in range(ANGULAR):
        k = (j+1) % ANGULAR
        builder.face((center, inside[-1][j], inside[-1][k]),
                     ((0.5,0.214), interior_uv(12,j), interior_uv(12,j+1)))
    for i in range(20):
        theta = TAU*i/20 + RNG.uniform(-0.08,0.08)
        p = surface(1,theta)
        direction = Vector((0.25*math.cos(theta),0.25*math.sin(theta),1))
        direction *= RNG.uniform(0.003,0.008)
        tube(builder,[p,p+direction*0.35,p+direction*0.72,
                      p+direction+Vector((0.001*math.sin(i),0,-0.001))],
             0.00022,sides=4)

    # A folded side welt, visible hand stitches, and a sewn bottom edge.
    welt = [surface(0.025 + 0.78*i/94, SIDE_ANGLE)
            + Vector((math.cos(SIDE_ANGLE),math.sin(SIDE_ANGLE),0))*0.0013 for i in range(95)]
    tube(builder, welt, 0.0012)
    for i in range(45):
        t = 0.035 + 0.75*i/44
        stitch = []
        for j in range(7):
            a = SIDE_ANGLE + (j/6-0.5)*0.028
            p = surface(t + (j/6-0.5)*0.004, a)
            p += Vector((math.cos(a), math.sin(a), 0)) * (0.0015 + 0.0012*math.sin(math.pi*j/6))
            stitch.append(p)
        tube(builder, stitch, 0.00048, sides=4)
    bottom = [surface(0.035, math.pi + math.pi*i/100) for i in range(101)]
    tube(builder, bottom, 0.0010)
    for i in range(31):
        theta = math.pi + 0.09 + (math.pi-0.18)*i/30
        stitch = []
        for j in range(5):
            p = surface(0.031 + 0.011*j/4, theta)
            p += Vector((math.cos(theta), math.sin(theta),0)) * (0.001+0.001*math.sin(math.pi*j/4))
            stitch.append(p)
        tube(builder, stitch, 0.00045, sides=4)
    return builder.object("PotatoSack_Bag"), vertical, lengths


def make_rope():
    builder = MeshBuilder()
    center = Vector((0.033*0.835**1.5, 0.006*math.sin(3*0.835), 0.008+0.576*0.835))
    wrap = []
    for i in range(241):
        f = i/240
        theta = -math.pi/2 + TAU*1.65*f
        wrap.append(center + Vector((0.042*math.cos(theta), 0.035*math.sin(theta), 0.009*(f-0.5))))
    two_ply(builder, wrap)
    # Crossed overhand knot, both ends hanging under gravity.
    def relative(keys):
        return [center + Vector(p) for p in keys]

    knot_a = relative([
        (-0.020,-0.032,0.002), (-0.009,-0.047,0.010), (0.009,-0.052,0.001),
        (0.019,-0.044,-0.008), (0.006,-0.040,-0.012), (-0.011,-0.047,-0.001),
        (-0.003,-0.056,0.010), (0.016,-0.049,0.007), (0.021,-0.041,0.000),
    ])
    knot_b = relative([
        (0.020,-0.031,0.002), (0.014,-0.040,-0.010), (-0.001,-0.056,-0.014),
        (-0.015,-0.053,-0.007), (-0.019,-0.042,0.003), (-0.006,-0.041,0.010),
        (0.008,-0.047,0.012), (0.013,-0.057,0.001), (-0.006,-0.057,-0.014),
    ])
    two_ply(builder, path_samples(knot_a, 8))
    two_ply(builder, path_samples(knot_b, 8))
    tails = [
        relative([(0.016,-0.049,-0.005), (0.028,-0.060,-0.023),
                  (0.040,-0.067,-0.051), (0.036,-0.068,-0.075)]),
        relative([(-0.010,-0.055,-0.011), (-0.027,-0.061,-0.029),
                  (-0.029,-0.064,-0.055), (-0.040,-0.060,-0.062)]),
    ]
    for tail in tails:
        points = path_samples(tail, 13)
        two_ply(builder, points)
        for j in range(6):
            a = TAU*j/6
            offset = Vector((math.cos(a),math.sin(a),0))*0.0017
            end = points[-1] + offset
            length = RNG.uniform(0.006, 0.012)
            fibre = [end, end+offset*0.8+Vector((0,0,-length*0.5)),
                     end+offset*1.6+Vector((0.001*math.sin(a),0,-length))]
            tube(builder, path_samples(fibre, 3), 0.00025, sides=4, rope=True)
    return builder.object("PotatoSack_Rope")


def source_pixels(filename, noncolor=False):
    image = bpy.data.images.load(str(SOURCE/filename), check_existing=True)
    if noncolor:
        image.colorspace_settings.name = "Non-Color"
    pixels = np.empty(SIZE*SIZE*4, np.float32)
    image.pixels.foreach_get(pixels)
    return pixels.reshape(SIZE,SIZE,4)[...,:3].copy()


def sample_tile(pixels, u, v):
    x, y = np.mod(u,1)*SIZE, np.mod(v,1)*SIZE
    ix, iy = np.floor(x).astype(np.int32), np.floor(y).astype(np.int32)
    fx, fy = (x-ix)[...,None], (y-iy)[...,None]
    return ((pixels[iy%SIZE,ix%SIZE]*(1-fx) + pixels[iy%SIZE,(ix+1)%SIZE]*fx)*(1-fy)
            + (pixels[(iy+1)%SIZE,ix%SIZE]*(1-fx) + pixels[(iy+1)%SIZE,(ix+1)%SIZE]*fx)*fy)


def save_texture(name, pixels, out_dir, noncolor=False):
    image = bpy.data.images.new(name, SIZE, SIZE, alpha=False)
    image.colorspace_settings.name = "Non-Color" if noncolor else "sRGB"
    rgba = np.ones((SIZE,SIZE,4), np.float32)
    rgba[...,:3] = np.clip(pixels,0,1)
    image.pixels.foreach_set(rgba.ravel())
    image.filepath_raw = str(out_dir/name)
    image.file_format = "PNG"
    image.save()
    image.filepath = str(out_dir/name)
    return image


def bake_atlas(out_dir, vertical, lengths):
    # Direct UV-space bake: source weave + wear, no extra maps or bake scene.
    color_src = source_pixels("hessian_380_Color_2K.jpg")
    normal_src = source_pixels("hessian_380_NormalGL_2K.jpg", True)
    rough_src = source_pixels("hessian_380_Roughness_2K.jpg", True)
    u, v = np.meshgrid((np.arange(SIZE,dtype=np.float32)+0.5)/SIZE,
                       (np.arange(SIZE,dtype=np.float32)+0.5)/SIZE)
    tu, tv = u*ATLAS_METERS/TILE_METERS, v*ATLAS_METERS/TILE_METERS
    color = sample_tile(color_src,tu,tv)
    normal = sample_tile(normal_src,tu,tv)*2-1
    rough = sample_tile(rough_src,tu,tv)[...,0]
    mottling = (np.sin(26*u+4*np.sin(14*v))*np.sin(32*v+2*np.sin(19*u))
                + 0.45*np.sin(75*u+8*v)*np.sin(57*v-3*u)) / 1.45
    lower = np.exp(-np.maximum(v-0.32,0)/0.075)
    dirt = lower*(0.15+0.09*(mottling+1)*0.5)
    for cx, cy, sx, sy, strength in [
        (0.44,0.44,0.065,0.055,0.16), (0.67,0.39,0.09,0.035,0.18),
        (0.31,0.55,0.047,0.061,0.11), (0.53,0.63,0.11,0.045,0.075),
        (0.72,0.58,0.05,0.08,0.12),
    ]:
        dirt += strength*np.exp(-((u-cx)/sx)**2-((v-cy)/sy)**2)*(0.72+0.28*mottling)
    color *= (0.94-dirt)[...,None]
    color += (0.008*mottling + 0.012*(rough-0.5))[...,None]
    dust = 0.075*np.exp(-((v-0.43)/0.13)**2)*(mottling*0.5+0.5)
    color = color*(1-dust[...,None]) + np.array([0.43,0.38,0.29])*dust[...,None]
    # Fine crumples baked into tangent normals; large folds stay in geometry.
    height = 0.00009*np.sin(145*u + 10*np.sin(28*v))*np.sin(185*v+4*np.sin(43*u))
    height *= (0.5+0.5*rough)
    dv, du = np.gradient(height, ATLAS_METERS/SIZE)
    normal[...,0] -= du
    normal[...,1] -= dv
    f = ((SIDE_ANGLE-math.pi/2)%TAU)/TAU
    widths = np.interp(v[:,0], vertical, lengths)/ATLAS_METERS
    seam_u = 0.5 + (f-0.5)*widths[:,None]
    seam = np.exp(-((u-seam_u)/0.0011)**2)*(v>=0.32)*(v<=vertical[int(ROWS*0.80)])
    color *= (1-0.14*seam)[...,None]
    # Interior gets a separate unlit, darker cloth strip.
    inside = (v>0.205)&(v<0.301)&(u>0.255)&(u<0.735)
    color[inside] = sample_tile(color_src,tu,tv)[inside]*0.76
    bottom = (u<0.24)&(v<0.21)
    color[bottom] *= 0.76
    rope = (u>0.285)&(v<0.18)
    fibres = np.sin(1150*v + 1.2*np.sin(76*u)) + 0.35*np.sin(2470*v+13*u)
    rope_color = np.array([0.405,0.305,0.195],np.float32)[None,None,:]
    rope_color = rope_color + (0.021*fibres+0.013*mottling)[...,None]
    color[rope] = rope_color[rope]
    normal[rope,0] = (0.055*np.sin(91*u+8*v))[rope]
    normal[rope,1] = (0.24*np.cos(1150*v+1.2*np.sin(76*u)))[rope]
    normal[rope,2] = 1.0
    normal /= np.maximum(np.linalg.norm(normal,axis=2,keepdims=True),1e-6)
    return (save_texture("PotatoSack_Color.png",color,out_dir),
            save_texture("PotatoSack_normal.png",normal*0.5+0.5,out_dir,True))


def material(color, normal):
    mat = bpy.data.materials.new("PotatoSack_Jute")
    mat.use_nodes = True
    nodes, links = mat.node_tree.nodes, mat.node_tree.links
    shader = nodes.get("Principled BSDF")
    shader.inputs["Roughness"].default_value = 0.89
    shader.inputs["Specular IOR Level"].default_value = 0.25
    base = nodes.new("ShaderNodeTexImage")
    base.image, base.label = color, "Baked burlap, dirt and rope"
    base.location = (-500,150)
    links.new(base.outputs["Color"],shader.inputs["Base Color"])
    tex = nodes.new("ShaderNodeTexImage")
    tex.image, tex.label = normal, "OpenGL tangent normal"
    tex.location = (-500,-150)
    node = nodes.new("ShaderNodeNormalMap")
    node.uv_map = "SackAtlas"
    node.location = (-200,-100)
    links.new(tex.outputs["Color"],node.inputs["Color"])
    links.new(node.outputs["Normal"],shader.inputs["Normal"])
    return mat


def validate_mesh(obj):
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    assert all(e.is_manifold for e in bm.edges), f"Open/non-manifold edge: {obj.name}"
    assert all(f.calc_area()>1e-12 for f in bm.faces), f"Degenerate face: {obj.name}"
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    assert bm.calc_volume(signed=True)>0, f"Inverted mesh: {obj.name}"
    bm.to_mesh(obj.data)
    bm.free()
    assert all(0<=c<=1 for loop in obj.data.uv_layers.active.data for c in loop.uv), "Atlas overflow"


def fit_and_center(objects):
    coords = np.array([v.co[:] for obj in objects for v in obj.data.vertices])
    lo, hi = coords.min(axis=0), coords.max(axis=0)
    scale = np.array([0.45,0.35,0.60])/(hi-lo)
    center = (lo+hi)/2
    for obj in objects:
        for v in obj.data.vertices:
            v.co = (np.array(v.co)-center)*scale
        obj.data.update()
    bpy.context.view_layer.update()
    bounds = np.array([obj.matrix_world@v.co for obj in objects for v in obj.data.vertices])
    dimensions = bounds.max(axis=0)-bounds.min(axis=0)
    assert np.allclose(dimensions,[0.45,0.35,0.60],atol=1e-6)
    assert np.allclose(bounds.max(axis=0)+bounds.min(axis=0),0,atol=1e-6)
    return dimensions.tolist()


def triangles(obj):
    return sum(len(p.vertices)-2 for p in obj.data.polygons)


def export_and_check(objects, out_dir):
    from io_scene_fbx import export_fbx_bin, fbx_utils, parse_fbx

    bpy.ops.object.select_all(action="DESELECT")
    for obj in objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    # Pin Blender 4.2's timestamp and salted FBX IDs without editing Blender.
    header = export_fbx_bin.fbx_header_elements
    export_fbx_bin.fbx_header_elements = lambda root, data, time=None: header(
        root,data,datetime.datetime(1911,1,1))
    fbx_utils.hash = lambda key: int.from_bytes(
        hashlib.sha256(str(key).encode()).digest()[:8],"little") & (2**63-1)
    try:
        bpy.ops.export_scene.fbx(
            filepath=str(out_dir/"PotatoSack.fbx"), use_selection=True,
            object_types={"MESH"}, path_mode="RELATIVE", embed_textures=False,
            apply_unit_scale=True, bake_space_transform=True, add_leaf_bones=False,
            axis_forward="-Z", axis_up="Y", use_triangles=True,
        )
    finally:
        export_fbx_bin.fbx_header_elements = header
        del fbx_utils.hash
    tree, _ = parse_fbx.parse(str(out_dir/"PotatoSack.fbx"))
    references = []

    def paths(element):
        if element.id==b"RelativeFilename":
            references.append(element.props[0].decode())
        for child in element.elems:
            paths(child)

    paths(tree)
    assert references and all(not Path(p).is_absolute() and (out_dir/p).is_file()
                              for p in references), f"FBX relative paths: {references}"
    original = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=str(out_dir/"PotatoSack.fbx"))
    imported = list(set(bpy.data.objects)-original)
    assert len(imported)==2 and all(o.type=="MESH" for o in imported), "FBX object count"
    xyz = np.array([o.matrix_world@v.co for o in imported for v in o.data.vertices])
    assert np.allclose(xyz.max(axis=0)-xyz.min(axis=0),[0.45,0.35,0.60],atol=1e-5), "FBX scale"
    assert sum(map(triangles,imported))==sum(map(triangles,objects)), "FBX triangles"
    names = set()
    for obj in imported:
        assert obj.location.length<1e-5, "FBX pivot"
        for slot in obj.material_slots:
            names.update(Path(n.image.filepath).name for n in slot.material.node_tree.nodes
                         if n.type=="TEX_IMAGE" and n.image)
    assert names=={"PotatoSack_Color.png","PotatoSack_normal.png"}, f"FBX textures: {names}"
    for obj in imported:
        bpy.data.objects.remove(obj,do_unlink=True)


def aim(obj, point):
    obj.rotation_euler = (Vector(point)-obj.location).to_track_quat("-Z","Y").to_euler()


def previews(out_dir):
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = 48
    scene.cycles.use_denoising = True
    scene.cycles.seed = SEED
    scene.render.resolution_x = scene.render.resolution_y = 1024
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGB"
    scene.render.film_transparent = False
    scene.view_settings.view_transform = "AgX"
    scene.view_settings.look = "AgX - Medium High Contrast"
    scene.world = bpy.data.worlds.new("Preview_World")
    scene.world.use_nodes = True
    scene.world.node_tree.nodes["Background"].inputs["Color"].default_value = (0.24,0.24,0.24,1)
    scene.world.node_tree.nodes["Background"].inputs["Strength"].default_value = 0.55
    bpy.ops.mesh.primitive_plane_add(size=200, location=(0,0,-0.3001))
    ground = bpy.context.object
    ground.name = "Preview_Ground"
    mat = bpy.data.materials.new("Preview_Gray")
    mat.diffuse_color = (0.23,0.23,0.23,1)
    mat.use_nodes = True
    mat.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.23,0.23,0.23,1)
    mat.node_tree.nodes["Principled BSDF"].inputs["Roughness"].default_value = 1
    ground.data.materials.append(mat)
    for name, location, energy, size in [
        ("Key",(-0.75,-1.10,1.15),78,0.65),
        ("Fill",(0.85,-0.30,0.65),30,0.85),
    ]:
        light = bpy.data.lights.new(name,"AREA")
        light.energy, light.shape, light.size = energy, "DISK", size
        obj = bpy.data.objects.new(name,light)
        scene.collection.objects.link(obj)
        obj.location = location
        aim(obj,(0,0,0))
    camera = bpy.data.cameras.new("Preview_Camera")
    camera.type, camera.ortho_scale = "ORTHO", 0.79
    obj = bpy.data.objects.new("Preview_Camera",camera)
    scene.collection.objects.link(obj)
    scene.camera = obj
    for filename, location in [
        ("preview_front.png",(0,-1.65,0.22)),
        ("preview_34.png",(1.05,-1.45,0.32)),
    ]:
        obj.location = location
        aim(obj,(0,0,0.005))
        scene.render.filepath = str(out_dir/filename)
        print(f"RENDER {filename}",flush=True)
        bpy.ops.render.render(write_still=True)


def main():
    args = sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else []
    if len(args)!=1:
        raise SystemExit("Usage: blender -b --factory-startup -P make_potato_sack.py -- <out_dir>")
    for filename in ("hessian_380_Color_2K.jpg", "hessian_380_NormalGL_2K.jpg",
                     "hessian_380_Roughness_2K.jpg"):
        assert (SOURCE/filename).is_file(), f"Missing source: {SOURCE/filename}"
    out_dir = Path(args[0]).resolve()
    out_dir.mkdir(parents=True,exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.scene.unit_settings.system = "METRIC"
    bpy.context.scene.unit_settings.scale_length = 1.0
    bag, vertical, lengths = make_bag()
    rope = make_rope()
    objects = [bag,rope]
    for obj in objects:
        validate_mesh(obj)
    dimensions = fit_and_center(objects)
    counts = {"bag":triangles(bag),"rope":triangles(rope)}
    assert 20000<=sum(counts.values())<=60000, f"Triangle budget: {counts}"
    print("MESH",json.dumps(counts),flush=True)
    color, normal = bake_atlas(out_dir,vertical,lengths)
    mat = material(color,normal)
    for obj in objects:
        obj.data.materials.append(mat)
    export_and_check(objects,out_dir)
    previews(out_dir)
    outputs = ["PotatoSack.fbx","PotatoSack_Color.png","PotatoSack_normal.png",
               "preview_front.png","preview_34.png"]
    assert all((out_dir/name).is_file() for name in outputs)
    print("POTATO_SACK_REPORT "+json.dumps({
        "script":str(Path(__file__).resolve()),
        "outputs":[str(out_dir/name) for name in outputs],
        "triangles":counts, "texture_size":[SIZE,SIZE],
        "bbox_blender_xyz_m":dimensions, "bbox_unity_xyz_m":[0.45,0.60,0.35],
        "pivot":[0,0,0], "checks":"closed meshes, positive volume, atlas, FBX round-trip",
        "caveats":"Roughness 0.89; use calculated tangents on import. URP import not tested. Cycles preview metadata and last-bit CPU rounding may vary.",
    }),flush=True)


if __name__ == "__main__":
    main()
