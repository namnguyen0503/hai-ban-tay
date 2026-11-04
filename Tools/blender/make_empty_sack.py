"""Blender 4.2: an untied, collapsed jute sack and loose rope; -- <out_dir>."""
import json
import math
import random
import sys
from pathlib import Path

sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parent))
import make_potato_sack as sack
from make_potatoes import export_and_check, render_preview

import bpy
import numpy as np
from mathutils import Vector


SEED = 1911
TAU = math.tau
ANGULAR, ROWS = 80, 64
THICKNESS = 0.0009


def surface(t, theta):
    c, s = math.cos(theta), math.sin(theta)
    width = 0.132 + 0.022*math.sin(math.pi*t)**0.8
    width -= 0.030*math.exp(-((t-0.77)/0.13)**2)
    width += 0.008*math.sin(10*t + 1.1)
    half_height = float(np.interp(t, [0, 0.16, 0.40, 0.63, 0.76, 0.89, 1],
                                   [0.0025, 0.004, 0.006, 0.005, 0.011, 0.027, 0.033]))
    x = -0.215 + 0.410*t
    y = width*math.copysign(abs(c)**0.86, c)
    y += 0.009*math.sin(7*t + 0.7)*math.sin(math.pi*t)
    z = 0.002 + (1+math.copysign(abs(s)**0.55, s))*half_height
    top = max(s, 0)**1.4
    # Broad diagonal pleats, with adjacent troughs rather than an inflated pillow.
    for center, slope, amplitude in [(0.26, 0.095, 0.018), (0.49, -0.110, 0.026),
                                     (0.66, 0.093, 0.020)]:
        crease = center + slope*c + 0.012*math.sin(5*theta+0.4)
        fold = math.exp(-((t-crease)/0.025)**2)
        z += top*amplitude*fold
        z -= top*0.003*math.exp(-((t-crease-0.045)/0.018)**2)
        x -= top*0.006*fold
        y += top*0.004*c*fold
    z += top*0.003*math.sin(18*t + 7*c)*math.sin(math.pi*t)
    mouth = sack.smooth(0.77, 1, t)
    x += mouth*(0.009*math.sin(5*theta+0.4) + 0.005*math.cos(3*theta))
    y += mouth*0.006*c*math.sin(9*theta+0.7)
    z += mouth*(0.004*math.sin(5*theta+0.6) + 0.003*math.sin(9*theta))*((1+s)/2)
    # Fold one corner of the sewn end back onto the body.
    corner = math.exp(-((t-0.075)/0.085)**2)*math.exp(-(sack.angle_delta(theta, 2.55)/0.52)**2)
    x += 0.028*corner
    z += 0.030*corner
    return Vector((x, y, z))


def inward(t, theta):
    h = 0.0001
    along = surface(min(1, t+h), theta)-surface(max(0, t-h), theta)
    around = surface(t, theta+h)-surface(t, theta-h)
    normal = around.cross(along).normalized()
    p = surface(t, theta)-THICKNESS*normal
    if t == 0:
        p.x += THICKNESS
    return p


def mouth_hem(builder):
    # A welded closed tube, with only a UV cut; no coincident end caps.
    points = [surface(1, -math.pi/2+TAU*j/ANGULAR) for j in range(ANGULAR)]
    start, distance = len(builder.vertices), [0.0]
    for i, p in enumerate(points):
        tangent = (points[(i+1) % ANGULAR]-points[(i-1) % ANGULAR]).normalized()
        normal = Vector((1, 0, 0))
        normal = (normal-tangent*normal.dot(tangent)).normalized()
        binormal = tangent.cross(normal)
        for j in range(6):
            phi = TAU*j/6
            builder.vertex(p+0.00125*(normal*math.cos(phi)+binormal*math.sin(phi)))
        distance.append(distance[-1]+(points[(i+1) % ANGULAR]-p).length)

    def uv(i, j):
        return (0.305+0.66*distance[i]/0.65, 0.065+0.015*j/6)

    for i in range(ANGULAR):
        k = (i+1) % ANGULAR
        for j in range(6):
            q = (j+1) % 6
            builder.face((start+i*6+j, start+i*6+q, start+k*6+q, start+k*6+j),
                         (uv(i, j), uv(i, j+1), uv(i+1, j+1), uv(i+1, j)))


def make_bag():
    builder = sack.MeshBuilder()
    outer, inner, distances, lengths = [], [], [], []
    for i in range(ROWS+1):
        t = i/ROWS
        ring = [surface(t, -math.pi/2+TAU*j/ANGULAR) for j in range(ANGULAR)]
        distance = [0.0]
        for j in range(ANGULAR):
            distance.append(distance[-1]+(ring[(j+1) % ANGULAR]-ring[j]).length)
        distances.append(distance)
        lengths.append(distance[-1])
        outer.append([builder.vertex(p) for p in ring])
        inner.append([builder.vertex(inward(t, -math.pi/2+TAU*j/ANGULAR)) for j in range(ANGULAR)])
    vertical = [0.0]
    for i in range(1, ROWS+1):
        vertical.append(vertical[-1]+sum((Vector(builder.vertices[outer[i][j]])-
                                          Vector(builder.vertices[outer[i-1][j]])).length
                                         for j in range(ANGULAR))/ANGULAR)
    vertical = [0.32+v/sack.ATLAS_METERS for v in vertical]

    def uv(i, j, inside=False):
        if inside and i/ROWS >= 0.75:
            return (0.27+0.45*distances[i][j]/lengths[i], 0.215+0.077*(i/ROWS-0.75)/0.25)
        return (0.5+(distances[i][j]-lengths[i]/2)/sack.ATLAS_METERS, vertical[i])

    for rings, inside in ((outer, False), (inner, True)):
        for i in range(ROWS):
            for j in range(ANGULAR):
                k = (j+1) % ANGULAR
                indices = (rings[i][j], rings[i][k], rings[i+1][k], rings[i+1][j])
                coords = (uv(i, j, inside), uv(i, j+1, inside), uv(i+1, j+1, inside), uv(i+1, j, inside))
                if inside:
                    indices, coords = indices[::-1], coords[::-1]
                builder.face(indices, coords)
        center = builder.vertex(sum((Vector(builder.vertices[v]) for v in rings[0]), Vector())/ANGULAR)
        for j in range(ANGULAR):
            k = (j+1) % ANGULAR
            indices = (center, rings[0][k], rings[0][j])
            coords = ((0.12, 0.114), (0.12+0.094*math.cos(TAU*(j+1)/ANGULAR), 0.114+0.005*math.sin(TAU*(j+1)/ANGULAR)),
                      (0.12+0.094*math.cos(TAU*j/ANGULAR), 0.114+0.005*math.sin(TAU*j/ANGULAR)))
            if inside:
                indices, coords = indices[::-1], coords[::-1]
            builder.face(indices, coords)
    # Join outer and inner cloth around an actual empty opening, never cap the mouth.
    for j in range(ANGULAR):
        k = (j+1) % ANGULAR
        builder.face((outer[-1][j], outer[-1][k], inner[-1][k], inner[-1][j]),
                     (uv(ROWS, j), uv(ROWS, j+1), uv(ROWS, j+1, True), uv(ROWS, j, True)))
    mouth_hem(builder)
    side_angle = sack.SIDE_ANGLE-math.pi
    welt = [surface(0.015+0.95*i/80, side_angle)+Vector((0, -0.001, 0.0005)) for i in range(81)]
    sack.tube(builder, welt, 0.0010, sides=6)
    for i in range(39):
        t = 0.025+0.94*i/38
        stitch = [surface(t+(j/4-0.5)*0.006, side_angle+(j/4-0.5)*0.035)
                  +Vector((0, -0.0013, 0.0008+0.0006*math.sin(math.pi*j/4))) for j in range(5)]
        sack.tube(builder, stitch, 0.00038, sides=4)
    end = [surface(0.007, TAU*j/80) for j in range(41)]
    sack.tube(builder, end, 0.00095, sides=6)
    for i in range(27):
        theta = 0.18+(math.pi-0.36)*i/26
        stitch = [surface(0.008+0.022*j/4, theta)+Vector((0, 0, 0.001+0.0007*math.sin(math.pi*j/4)))
                  for j in range(5)]
        sack.tube(builder, stitch, 0.00038, sides=4)
    rng = random.Random(SEED)
    for i in range(18):
        theta = TAU*i/18 + rng.uniform(-0.05, 0.05)
        p = surface(1, theta)
        fibre = Vector((rng.uniform(0.003, 0.007), 0.001*math.cos(theta), 0.0008*math.sin(theta)))
        sack.tube(builder, [p, p+fibre*0.5, p+fibre], 0.00018, sides=4)
    return builder.object("PotatoSackEmpty_Bag"), vertical, lengths


def make_rope():
    builder = sack.MeshBuilder()
    points = sack.path_samples([
        (-0.125, -0.174, 0.0045), (-0.070, -0.199, 0.0045),
        (0.010, -0.204, 0.0045), (0.075, -0.188, 0.0045),
        (0.117, -0.160, 0.0045), (0.099, -0.145, 0.0045),
        (0.055, -0.158, 0.0045), (0.028, -0.182, 0.0160),
        (0.078, -0.210, 0.0045), (0.153, -0.208, 0.0045),
        (0.184, -0.186, 0.0045), (0.157, -0.165, 0.0045),
    ], 14)
    sack.two_ply(builder, points)
    rng = random.Random(SEED+1)
    for end, sign in ((points[0], -1), (points[-1], 1)):
        for i in range(5):
            a = TAU*i/5
            offset = Vector((0, 0.0015*math.cos(a), 0.0015*math.sin(a)))
            p = end+offset
            tail = Vector((sign*rng.uniform(0.004, 0.010), rng.uniform(-0.003, 0.003), -0.001))
            sack.tube(builder, [p, p+tail*0.5+offset*0.3, p+tail], 0.00022, sides=4, rope=True)
    return builder.object("PotatoSackEmpty_Rope")


def fit_and_center(objects):
    xyz = np.array([v.co[:] for obj in objects for v in obj.data.vertices])
    lo, hi = xyz.min(axis=0), xyz.max(axis=0)
    scale = np.array([0.45/(hi[0]-lo[0]), 0.35/(hi[1]-lo[1]), 1])
    for obj in objects:
        for vertex in obj.data.vertices:
            vertex.co = (np.array(vertex.co)-(lo+hi)/2)*scale
        obj.data.update()
    bpy.context.view_layer.update()
    bounds = np.array([v.co[:] for obj in objects for v in obj.data.vertices])
    dimensions = bounds.max(axis=0)-bounds.min(axis=0)
    assert np.allclose(dimensions[:2], [0.45, 0.35], atol=1e-6) and dimensions[2] <= 0.10, dimensions
    assert np.allclose(bounds.min(axis=0)+bounds.max(axis=0), 0, atol=1e-7), "Assembly pivot"
    return dimensions.tolist()


def bake_burlap(out_dir, vertical, lengths):
    # Reuse the original direct bake and all its weave, dirt, crumple and rope colors.
    save = sack.save_texture
    sack.save_texture = lambda name, *args, **kwargs: save(name.replace("PotatoSack", "PotatoSackEmpty"), *args, **kwargs)
    # The original bake samples its ROWS-sized seam table.
    t = np.linspace(0, 1, sack.ROWS+1)
    try:
        return sack.bake_atlas(out_dir, np.interp(t, np.linspace(0, 1, ROWS+1), vertical),
                               np.interp(t, np.linspace(0, 1, ROWS+1), lengths))
    finally:
        sack.save_texture = save


def main():
    args = sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else []
    if len(args) != 1:
        raise SystemExit("Usage: blender -b --factory-startup -P make_empty_sack.py -- <out_dir>")
    for kind in ("Color", "NormalGL", "Roughness"):
        path = sack.SOURCE/f"hessian_380_{kind}_2K.jpg"
        assert path.is_file(), f"Missing source: {path}"
    out_dir = Path(args[0]).resolve()
    out_dir.mkdir(parents=True, exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.scene.unit_settings.system = "METRIC"
    bpy.context.scene.unit_settings.scale_length = 1
    bag, vertical, lengths = make_bag()
    rope = make_rope()
    objects = [bag, rope]
    dimensions = fit_and_center(objects)
    for obj in objects:
        sack.validate_mesh(obj)
        assert obj.location.length < 1e-8, "Assembly origin"
    counts = {obj.name: sack.triangles(obj) for obj in objects}
    assert 15000 <= sum(counts.values()) <= 40000, counts
    print("MESH", json.dumps(counts), flush=True)
    color, normal = bake_burlap(out_dir, vertical, lengths)
    material = sack.material(color, normal)
    material.name = "PotatoSackEmpty_Jute"
    for obj in objects:
        obj.data.materials.append(material)
    stats = export_and_check(objects, out_dir, "PotatoSackEmpty")
    render_preview(out_dir, 0.60, (0.72, -0.92, 0.78), (0, 0, -0.008), -dimensions[2]/2-0.0002)
    outputs = ["PotatoSackEmpty.fbx", "PotatoSackEmpty_Color.png", "PotatoSackEmpty_normal.png", "preview.png"]
    assert all((out_dir/name).is_file() for name in outputs)
    print("EMPTY_SACK_REPORT "+json.dumps({
        "outputs": [str(out_dir/name) for name in outputs], "objects": stats,
        "texture_size": [sack.SIZE, sack.SIZE], "seed": SEED,
        "bbox_blender_xyz_m": dimensions,
        "bbox_unity_xyz_m": [dimensions[0], dimensions[2], dimensions[1]], "pivot": [0, 0, 0],
        "checks": "closed thick cloth/rope, positive volume, UV bounds, assembly pivot, FBX round-trip",
        "caveats": "Static sculpted folds. Roughness 0.89; normal-map import and calculated tangents. URP import not run. Render rounding may vary across CPU architectures.",
    }), flush=True)


if __name__ == "__main__":
    main()
