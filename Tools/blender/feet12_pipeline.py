"""Sửa riêng chân từ bản chuẩn bất biến; không ghi Staging hoặc đổi phần thân trên."""
import hashlib
import json
import shutil
from pathlib import Path

import bpy
import bmesh
from mathutils import Vector
import prepare_cast as cast
import prepare_crowd as crowd

ROOT = Path(__file__).resolve().parents[2]
OUTPUT = ROOT / "Tools/blender/generated/Characters"
SCRATCH = ROOT / "Logs/feet12_blender"


def upper_faces(meshes):
    """Đối chứng thân trên để sửa cổ chân không chạm mũ, đai eo, tạp dề hoặc mặt."""
    result = []
    for obj in meshes:
        for face in obj.data.polygons:
            points = [obj.matrix_world @ obj.data.vertices[i].co for i in face.vertices]
            if min(p.z for p in points) <= .50:
                continue
            material = obj.data.materials[face.material_index].name.replace("_ForearmSkin", "_Body")
            result.append((material, tuple(sorted(tuple(round(c, 5) for c in p) for p in points))))
    return sorted(result)


def check_leg(obj):
    data = bmesh.new(); data.from_mesh(obj.data)
    seen = set(); components = 0
    for vertex in data.verts:
        if vertex in seen:
            continue
        components += 1; todo = [vertex]; seen.add(vertex)
        while todo:
            current = todo.pop()
            for edge in current.link_edges:
                other = edge.other_vert(current)
                if other not in seen:
                    seen.add(other); todo.append(other)
    assert components == 1, (obj.name, "Chân phải liền khối", components)
    assert all(v.link_faces for v in data.verts), obj.name
    assert all(p.calc_area() > 1e-10 for p in data.faces), obj.name
    boundary = [e for e in data.edges if e.is_boundary]
    assert boundary and all(abs(v.co.z-crowd.BARE_LEG_TOP)<.0001 for e in boundary for v in e.verts), (obj.name, "Lỗ dưới cổ chân")
    data.free()
    for vertex in obj.data.vertices:
        assert abs(sum(g.weight for g in vertex.groups) - 1) < 1e-5
        assert len([g for g in vertex.groups if g.weight > 1e-6]) <= 3
    assert crowd.triangles(obj) < 3000, (obj.name, "Ngân sách chân")
    return {"connected_components": components, "triangles": crowd.triangles(obj), "ankle_holes": 0}


def prepare():
    OUTPUT.mkdir(parents=True, exist_ok=True); SCRATCH.mkdir(parents=True, exist_ok=True)
    # Lần sinh dở không được dùng báo cáo PASS của lần trước để finalize.
    (SCRATCH / "verification.json").write_text(json.dumps({"passed": False, "roles": {}}))
    (SCRATCH / "lods").mkdir(exist_ok=True)
    bpy.context.preferences.filepaths.save_version = 0
    stats = json.loads((cast.CHARACTERS / "cast_stats.json").read_text())
    crowd.CHARACTERS = OUTPUT; crowd.SCRATCH = SCRATCH
    checks = {}
    for role in stats:
        bpy.ops.wm.read_factory_settings(use_empty=True)
        source = cast.CHARACTERS / role
        out = OUTPUT / role; out.mkdir(exist_ok=True)
        bpy.ops.import_scene.fbx(filepath=str(source / (role + ".fbx")), use_anim=False)
        rig = next(o for o in bpy.data.objects if o.type == "ARMATURE")
        rig.data.pose_position = "REST"
        meshes = [o for o in bpy.data.objects if o.type == "MESH"]
        body = max(meshes, key=lambda o: len(o.data.vertices))
        cast.select([body]); bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
        original_upper = upper_faces(meshes)
        body.data.update()
        original_triangles = crowd.triangles(body)
        if role in crowd.PORTERS:
            # Chỉ bỏ da chân ghép và cuff cũ; quần/thân/tay/mặt giữ nguyên.
            crowd.delete_faces(body, lambda f: body.data.materials[f.material_index].name.endswith("_Skin") or
                               (body.data.materials[f.material_index].name.endswith("_Trousers") and
                                max(v.co.z for v in f.verts) < .43))
            # Mép quần và cuff cùng theo bắp chân; không lẫn Foot tại đầu gối dưới.
            for vertex in body.data.vertices:
                if vertex.co.z >= .49: continue
                label="Left" if vertex.co.x>0 else "Right"
                blend=min(1,max(0,(.49-vertex.co.z)/.06))
                old={body.vertex_groups[g.group].name:g.weight*(1-blend) for g in vertex.groups}
                old[label+"LowerLeg"]=old.get(label+"LowerLeg",0)+blend
                for group in list(vertex.groups): body.vertex_groups[group.group].remove([vertex.index])
                for name,weight in old.items():
                    if weight>1e-6: body.vertex_groups[name].add([vertex.index],weight,"REPLACE")
            skin, uv = crowd.hand_skin(body, role)
            pants = next(m for m in body.data.materials if m.name.endswith("_Trousers"))
            legs = crowd.barelegs(role, rig, skin, pants, uv, body)
            checks[role] = {"legs": [check_leg(o) for o in legs if "BareLegFoot" in o.name]}
            assert len(checks[role]["legs"]) == 2
            body = crowd.join_meshes([body] + legs, role + "_LOD0_Mesh")
            meshes = [body]
        elif role in {"VendorVN_F", "PassengerFR_F"}:
            # Giày chef nguồn còn đỉnh nhọn trên cổ; cắt phẳng, cho gấu quần chồng 15 mm.
            data = bmesh.new(); data.from_mesh(body.data)
            shoe_faces = [f for f in data.faces if body.data.materials[f.material_index].name.endswith("_Leather")]
            geom = set(shoe_faces)
            for f in shoe_faces:
                geom.update(f.edges); geom.update(f.verts)
            bmesh.ops.bisect_plane(data, geom=list(geom), dist=1e-6, plane_co=(0,0,.145), plane_no=(0,0,1))
            remove = [f for f in data.faces if body.data.materials[f.material_index].name.endswith("_Leather") and
                      sum(v.co.z for v in f.verts)/len(f.verts) > .145]
            bmesh.ops.delete(data, geom=remove, context="FACES")
            loose = [v for v in data.verts if not v.link_faces]
            if loose: bmesh.ops.delete(data, geom=loose, context="VERTS")
            data.to_mesh(body.data); data.free(); body.data.update()
            lower = body.vertex_groups
            shoe_rim=[v for v in body.data.vertices if abs(v.co.z-.145)<.0001 and
                      any(body.data.materials[p.material_index].name.endswith("_Leather") and v.index in p.vertices
                          for p in body.data.polygons)]
            assert shoe_rim, (role,"Thiếu mép giày để nối weights gấu quần")
            rim_weights={v.index:{lower[g.group].name:g.weight for g in v.groups} for v in shoe_rim}
            for polygon in body.data.polygons:
                if not body.data.materials[polygon.material_index].name.endswith("_Trousers"): continue
                for index in polygon.vertices:
                    v = body.data.vertices[index]
                    if abs(v.co.z - .155) < .0001: v.co.z = .115
            for v in body.data.vertices:
                if not .11 < v.co.z < .22: continue
                if not any(body.data.materials[p.material_index].name.endswith("_Trousers") and v.index in p.vertices
                           for p in body.data.polygons): continue
                label = "Left" if v.co.x > 0 else "Right"
                t = min(1,max(0,(.20-v.co.z)/.085)); t=t*t*(3-2*t)
                rim=min((p for p in shoe_rim if p.co.x*v.co.x>0),
                        key=lambda p:(p.co.x-v.co.x)**2+(p.co.y-v.co.y)**2)
                weights={name:w*t for name,w in rim_weights[rim.index].items()}
                weights[label+"LowerLeg"]=weights.get(label+"LowerLeg",0)+(1-t)
                for g in list(v.groups): lower[g.group].remove([v.index])
                for name,weight in weights.items():
                    group=lower.get(name) or lower.new(name=name)
                    group.add([v.index],weight,"REPLACE")
            checks[role] = {"repair": "Cắt cổ giày, gấu chồng 30 mm, blend theo weights mép giày"}
        else:
            leather = [i for i,m in enumerate(body.data.materials) if "leather" in m.name.lower()]
            if not leather:
                print("FEET12_SHOD_INTACT", role, flush=True)
                continue
            cloth = next(i for i,m in enumerate(body.data.materials) if m.name.endswith("_Body"))
            uv=body.data.uv_layers.active.data
            patches={}
            for label,sign in [("Left",1),("Right",-1)]:
                candidates=[p for p in body.data.polygons if p.material_index==cloth and
                            .30<p.center.z<.45 and p.center.x*sign>0 and
                            all((uv[i].uv.x<.29 or uv[i].uv.x>.71) and uv[i].uv.y>.565 for i in p.loop_indices)]
                assert candidates,(role,label,"Thiếu UV vải quần bên cạnh cổ chân")
                p=max(candidates,key=lambda p:p.area); corners=[uv[i].uv.copy() for i in p.loop_indices[:3]]
                patches[label]=(sum(corners,Vector((0,0)))/3,corners[1]-corners[0],corners[2]-corners[0],p.center.x)
            data=bmesh.new(); data.from_mesh(body.data)
            for height in [.145,.30]:
                ankle_faces=[f for f in data.faces if f.material_index in [cloth]+leather and
                             min(v.co.z for v in f.verts)<=height<=max(v.co.z for v in f.verts)]
                geom=set(ankle_faces)
                for face in ankle_faces: geom.update(face.edges); geom.update(face.verts)
                bmesh.ops.bisect_plane(data,geom=list(geom),
                                      dist=1e-6,plane_co=(0,0,height),plane_no=(0,0,1))
            reassigned=0
            for face in data.faces:
                height=sum(v.co.z for v in face.verts)/len(face.verts)
                if face.material_index==cloth and height<.145:
                    # Cả mặt Body lọt dưới ranh cũng là giày; tránh các chóp vải trắng còn sót.
                    face.material_index=leather[0]
                elif face.material_index in leather and .145 < height < .35:
                    face.material_index=cloth; reassigned+=1
            data.to_mesh(body.data); data.free(); body.data.update()
            uv=body.data.uv_layers.active.data
            for face in body.data.polygons:
                if face.material_index!=cloth or not .145<face.center.z<.30: continue
                if all((uv[i].uv.x<.29 or uv[i].uv.x>.71) and uv[i].uv.y>.565 for i in face.loop_indices):
                    continue  # Giữ UV vải nguồn đã đúng, chỉ chuyển phần còn trỏ vào atlas giày.
                center,u,v,cx=patches["Left" if face.center.x>0 else "Right"]
                for loop in face.loop_indices:
                    point=body.data.vertices[body.data.loops[loop].vertex_index].co
                    uv[loop].uv=center+u*((point.x-cx)/.15)*.15+v*((point.z-.145)/.155-.5)*.15
            checks[role] = {"repair": "Ranh quần–giày 14,5 cm; UV cổ chân nằm trong vùng vải quần", "faces": reassigned}
        actual_upper = upper_faces(meshes)
        if actual_upper != original_upper:
            (SCRATCH / "upper_difference.json").write_text(json.dumps({"role": role,
                "removed": [f for f in original_upper if f not in actual_upper],
                "added": [f for f in actual_upper if f not in original_upper]}, indent=2))
        assert actual_upper == original_upper, (role, "Không được thay đổi thân trên")
        for image in bpy.data.images:
            if image.source != "FILE":
                continue
            name = Path(image.filepath.replace("\\", "/")).name
            assert (source / name).is_file(), (role, name)
            shutil.copy2(source / name, out / name)
            image.filepath = str(out / name)
        count = crowd.triangles(body)
        assert count < original_triangles if role in crowd.PORTERS else count <= original_triangles + 256, (role, count, original_triangles)
        checks[role].update({"before_triangles": original_triangles, "after_triangles": count})
        rig.data.pose_position = "REST"
        cast.export_fbx(out / (role + ".fbx"), [rig] + meshes)
        stats[role]["triangles"] = sum(crowd.triangles(o) for o in meshes)
        stats[role]["textures"] = sorted(p.name for p in out.glob("*.png"))
        stats[role]["texture_count"] = len(stats[role]["textures"])
        # Không mang số đo Unity của bản cũ sang bản FBX vừa sinh.
        stats[role].pop("unity_lod_triangles", None)
        stats[role].pop("texture_sizes", None)
        stats[role]["notes"] = "Chân/cổ chân feet12; không đổi thân trên hoặc skeleton."
        crowd.lod_role(role, stats[role])
        checks[role]["lod_triangles"] = stats[role]["lod_triangles"]
    (OUTPUT / "cast_stats.json").write_text(json.dumps(stats, indent=2, ensure_ascii=False))
    (SCRATCH / "verification.json").write_text(json.dumps({"passed": True, "roles": checks}, indent=2, ensure_ascii=False))
    print("FEET12_PREPARED", checks, flush=True)
