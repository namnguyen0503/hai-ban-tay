"""Bộ xương ngón tay cho tay FP: cùng mesh da Rocketbox (tư thế Open, cùng UV/texture với các pose tĩnh) kèm xương Wrist + 5 ngón × 3 đốt
và trọng số skin gốc của Rocketbox. Xuất JSON theo ĐÚNG khung Unity của Hand_*_*.fbx (+Z ngón tay, −Y lòng bàn tay, ngón cái trái +X)
để Editor dựng SkinnedMeshRenderer, tránh phụ thuộc quy ước trục/xương của FBX.

Chạy: blender -b --factory-startup --python-exit-code 1 -P Tools/blender/make_fp_hand_rig.py
Ra: Staging/art/hands/Hand_L_Rig.json, Hand_R_Rig.json (tay phải = đối xứng qua x).
"""
import json
import os
import sys

sys.dont_write_bytecode = True
from pathlib import Path
import bpy
import bmesh
from mathutils import Vector

sys.path.insert(0, str(Path(__file__).resolve().parent))
import make_fp_hands as H

BONES = ["Wrist"] + [f"{d}{j}" for d in H.DIGITS for j in (1, 2, 3)]
PARTS = ("Proximal", "Intermediate", "Distal")
# Nửa bề rộng vùng pha trộn quanh khớp (m), theo khớp gốc / giữa / xa của ngón. Khớp gốc ngón rộng để mu khớp không gãy góc khi gập;
# khớp giữa/xa hẹp vì đốt giữa chỉ dài 1,5–2,5 cm (vùng rộng hơn nuốt mất đốt giữa).
JOINT_BLEND = (0.008, 0.0045, 0.0035)
THUMB_BLEND = (0.0, 0.006, 0.004)  # khớp cổ-bàn ngón cái giữ chia trọng số gốc của Rocketbox (gò ngón cái)


def to_unity(p, right):
    # Blender local (x, y = hướng ngón, z = lên) → Unity (x, z, y); tay phải đối xứng qua x.
    return (-p[0] if right else p[0], p[2], p[1])


def group_to_bone(name):
    for d in H.DIGITS:
        for j, part in enumerate(PARTS):
            if name == "Left" + d + part:
                return BONES.index(f"{d}{j + 1}")
    return 0


def smooth(x, a, b):
    if b <= a:
        return 1.0 if x >= a else 0.0
    t = min(1.0, max(0.0, (x - a) / (b - a)))
    return t * t * (3 - 2 * t)


def chain_coordinate(p, chain):
    """(tham số cung dọc ba xương của ngón: 0 ở khớp gốc, âm khi nằm trước khớp gốc; khoảng cách ngang tới xương)."""
    best, start = None, 0.0
    for j, (head, tail) in enumerate(chain):
        axis = tail - head
        length = axis.length
        axis = axis / length
        t = (p - head).dot(axis)
        # Đoạn đầu kéo dài về phía cổ tay, đoạn cuối kéo qua đầu ngón.
        t = min(max(t, -1.0 if j == 0 else 0.0), 1.0 if j == 2 else length)
        lateral = (p - (head + axis * t)).length
        if best is None or lateral < best[1]:
            best = (start + t, lateral)
        start += length
    return best


def chain_split(s, chain, blend, wrist_part):
    """Trọng số (cổ tay, đốt gốc, đốt giữa, đốt xa) tại tham số cung s: chuyển êm trong ±blend quanh từng khớp."""
    l1 = (chain[0][1] - chain[0][0]).length
    l2 = l1 + (chain[1][1] - chain[1][0]).length
    a = smooth(s, -blend[0], blend[0]) if wrist_part else 1.0
    b = smooth(s, l1 - blend[1], l1 + blend[1])
    c = smooth(s, l2 - blend[2], l2 + blend[2])
    return (1 - a, a - b, b - c, c)


def joint_weights(points, raw, segments):
    """
    Trọng số theo hình học xương thay cho ranh giới gắt của Rocketbox (gập > 60° thì mu khớp gãy góc): giữ cách Rocketbox chia đỉnh
    cho từng ngón (kẽ ngón, gò ngón cái), còn trong một ngón thì chia lại theo vị trí dọc xương, pha trộn hẹp quanh khớp. Mu khớp gốc
    của bốn ngón dài (cả đỉnh Rocketbox gán hẳn cho bàn tay) pha giữa bàn tay và đốt gốc trong ±JOINT_BLEND[0].
    """
    digits = len(segments)
    out = []
    for p, w in zip(points, raw):
        total = sum(w)
        w = [x / total for x in w]
        share = [sum(w[1 + d * 3 + j] for j in range(3)) for d in range(digits)]
        coords = [chain_coordinate(p, chain) for chain in segments]
        new = [0.0] * len(BONES)
        near = min(range(1, digits), key=lambda d: coords[d][1])
        s, lateral = coords[near]
        knuckle = lateral < 0.013 and s > -2 * JOINT_BLEND[0]
        if not knuckle:
            new[0] += w[0]
        for d in range(digits):
            mass = share[d] + (w[0] if knuckle and d == near else 0.0)
            if mass <= 0:
                continue
            split = chain_split(coords[d][0], segments[d], THUMB_BLEND if d == 0 else JOINT_BLEND, knuckle and d == near)
            new[0] += mass * split[0]
            for j in range(3):
                new[1 + d * 3 + j] += mass * split[1 + j]
        out.append(new)
    return out


def dump(low, segments, right):
    # Bản triangulate, shading mượt: lấy pháp tuyến/tiếp tuyến theo loop (MikkTSpace), cùng thứ Blender dùng để bake normal map.
    obj = low.copy(); obj.data = low.data.copy()
    bpy.context.collection.objects.link(obj)
    bm = bmesh.new(); bm.from_mesh(obj.data)
    bmesh.ops.triangulate(bm, faces=list(bm.faces), quad_method='BEAUTY', ngon_method='BEAUTY')
    bm.to_mesh(obj.data); bm.free()
    mesh = obj.data
    for poly in mesh.polygons: poly.use_smooth = True
    mesh.calc_tangents(uvmap="UVMap")
    uv = mesh.uv_layers["UVMap"].data
    groups = {g.index: g.name for g in obj.vertex_groups}

    count = len(mesh.vertices)
    weight = [[0.0] * len(BONES) for _ in range(count)]
    for v in mesh.vertices:
        for g in v.groups:
            weight[v.index][group_to_bone(groups[g.group])] += g.weight
    weight = joint_weights([v.co.copy() for v in mesh.vertices], weight, segments)

    def vertex_weights(v):
        acc = {b: w for b, w in enumerate(weight[v]) if w > 1e-4}
        top = sorted(acc.items(), key=lambda kv: -kv[1])[:4]
        total = sum(w for _, w in top)
        assert total > 0, v
        return [[b, round(w / total, 5)] for b, w in top]

    det = 1.0 if right else -1.0  # det của phép (x,y,z)→(±x,z,y)
    vertices, normals, tangents, uvs, weights, index = [], [], [], [], [], {}
    triangles = []
    mesh.calc_loop_triangles()
    for tri in mesh.loop_triangles:
        ids = []
        for loop_index in tri.loops:
            loop = mesh.loops[loop_index]
            p = to_unity(mesh.vertices[loop.vertex_index].co, right)
            n = to_unity(loop.normal, right)
            t = to_unity(loop.tangent, right)
            sign = loop.bitangent_sign * det
            u = uv[loop_index].uv
            key = (loop.vertex_index, round(n[0], 4), round(n[1], 4), round(n[2], 4), round(u[0], 5), round(u[1], 5),
                   round(t[0], 3), round(t[1], 3), round(t[2], 3))
            if key not in index:
                index[key] = len(vertices)
                vertices.append([round(c, 6) for c in p])
                normals.append([round(c, 5) for c in n])
                tangents.append([round(t[0], 5), round(t[1], 5), round(t[2], 5), sign])
                uvs.append([round(u[0], 6), round(u[1], 6)])
                weights.append(vertex_weights(loop.vertex_index))
            ids.append(index[key])
        triangles.append(ids)
    # Winding: mặt trước (pháp tuyến theo Unity = cross(b−a, c−a)) phải cùng chiều pháp tuyến đỉnh.
    agree = 0
    for a, b, c in triangles:
        pa, pb, pc = (Vector(vertices[i]) for i in (a, b, c))
        face = (pb - pa).cross(pc - pa)
        vn = Vector(normals[a]) + Vector(normals[b]) + Vector(normals[c])
        agree += 1 if face.dot(vn) > 0 else -1
    flip = agree < 0
    out_triangles = [[a, c, b] if flip else [a, b, c] for a, b, c in triangles]
    bones = [{"name": "Wrist", "parent": -1, "head": [0, 0, 0], "tail": [0, 0, 0.03]}]
    for d, chain in enumerate(segments):
        for j, (head, tail) in enumerate(chain):
            bones.append({"name": f"{H.DIGITS[d]}{j + 1}", "parent": 0 if j == 0 else 1 + d * 3 + j - 1,
                          "head": [round(c, 6) for c in to_unity(head, right)], "tail": [round(c, 6) for c in to_unity(tail, right)]})
    bpy.data.objects.remove(obj, do_unlink=True)
    print("RIG", "R" if right else "L", "vertices", len(vertices), "triangles", len(out_triangles), "flipWinding", flip, "agree", agree, flush=True)
    flat = lambda rows: [c for row in rows for c in row]
    bone_index = [[w[i][0] if i < len(w) else 0 for i in range(4)] for w in weights]
    bone_weight = [[w[i][1] if i < len(w) else 0.0 for i in range(4)] for w in weights]
    # Mảng phẳng để JsonUtility của Unity đọc được (không hỗ trợ mảng lồng nhau).
    return {"side": "R" if right else "L", "frame": "Unity: +Z ngón tay, −Y lòng bàn tay, ngón cái trái +X (phải −X)",
            "bones": bones, "vertices": flat(vertices), "normals": flat(normals), "tangents": flat(tangents), "uv": flat(uvs),
            "triangles": flat(out_triangles), "boneIndex": flat(bone_index), "boneWeight": flat(bone_weight)}


def main():
    assert H.SOURCE.is_file(), H.SOURCE
    H.OUT.mkdir(parents=True, exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(H.SOURCE), use_anim=False)
    import prepare_cast as cast
    import prepare_crowd as crowd
    original = next(o for o in bpy.context.scene.objects if o.type == 'ARMATURE')
    source = next(o for o in bpy.context.scene.objects if o.type == 'MESH')
    temp, a_rest, _, src_world = cast.make_rig(original)
    arm = crowd.canonical_rig(); bind = {b.name: b.matrix_local.copy() for b in arm.data.bones}
    cast.remap_mesh(source, original, arm, a_rest, bind, src_world)
    wrist = arm.matrix_world @ arm.data.bones['LeftHand'].head_local
    skinmat = bpy.data.materials.new('FP_Skin')
    low, opened, segments, uv_min, uv_max, source_tris = H.hand_base(source, arm, wrist, skinmat)
    for o in list(bpy.context.scene.objects):
        if o not in (low, arm): bpy.data.objects.remove(o, do_unlink=True)
    H.sculpt(low, segments, False)
    for right in (False, True):
        data = dump(low, segments, right)
        path = H.OUT / f"Hand_{data['side']}_Rig{os.environ.get('RIG_SUFFIX', '')}.json"
        path.write_text(json.dumps(data, separators=(",", ":")))
        print("WROTE", path, path.stat().st_size, flush=True)


if __name__ == '__main__': main()
