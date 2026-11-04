"""Static first-person Rocketbox hands. Read-only source; output Staging/art/hands.
Run: blender -b --factory-startup
  --python-exit-code 1 -P Tools/blender/make_fp_hands.py
Canonical Unity frame: wrist origin, +Z fingers, -Y palm, left thumb +X.
"""
import json
import math
import sys

sys.dont_write_bytecode = True
from pathlib import Path
import bpy
import bmesh
import numpy as np
from mathutils import Matrix, Vector

sys.path.insert(0, str(Path(__file__).resolve().parent))
ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / "Staging/characters/MicrosoftRocketbox_Characters/Assets/Avatars/Adults/Male_Adult_02/Export/Male_Adult_02.fbx"
TEXTURES = ROOT / "Staging/characters/MicrosoftRocketbox_Characters/Source/Avatars/Adults/Male_Adult_02/Textures"
DIGITS = ("Thumb", "Index", "Middle", "Ring", "Little")
OUT = ROOT / "Staging/art/hands"
POSES = {"Lowered": (12, 18, 12), "Open": (0, 5, 3),
         "Grip": (58, 75, 55), "Carry": (42, 65, 42), "Shovel": (55, 78, 50)}


def blender_point(p):
    # Khung FBX: Blender +Y thành Unity +Z; lòng tay Blender -Z thành Unity -Y.
    return (p.x, p.z, p.y)


def object_mesh(name, vertices, faces, uvs, material):
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(vertices, [], faces)
    mesh.materials.append(material)
    layer = mesh.uv_layers.new(name="UVMap")
    for poly, coords in zip(mesh.polygons, uvs):
        poly.use_smooth = True
        for li, uv in zip(poly.loop_indices, coords):
            layer.data[li].uv = uv
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    # Đổi hệ trục/đối xứng: tính lại winding hướng ra ngoài.
    bm = bmesh.new(); bm.from_mesh(mesh)
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    bm.to_mesh(mesh); bm.free()
    return obj


def deformations(armature, angles, opened):
    deltas = {}
    for digit, spread in zip(DIGITS, (-22, -7, 0, 5, 12)):
        previous = None
        cumulative = 0
        for j, part in enumerate(("Proximal", "Intermediate", "Distal")):
            name = "Left" + digit + part
            bone = armature.data.bones[name]
            rest = armature.matrix_world @ bone.matrix_local
            head = rest.translation
            if previous is not None:
                head = previous @ (armature.matrix_world @ bone.head_local)
            # Mỗi ngón cong khác nhau; ngón út khép hơn, ngón trỏ thả lỏng hơn.
            factor = {"Thumb": .65, "Index": .91, "Middle": 1, "Ring": 1.07, "Little": 1.14}[digit]
            cumulative += angles[j] * factor
            direction = Vector((1, 0, 0))
            if digit == "Thumb":
                direction = Vector((0.78, -0.62, -0.06))
            elif opened:
                direction = Matrix.Rotation(math.radians(spread), 3, 'Z') @ direction
            direction = Matrix.Rotation(math.radians(cumulative), 3, 'Y') @ direction
            old = (armature.matrix_world @ bone.tail_local) - rest.translation
            rotation = old.normalized().rotation_difference(direction.normalized()).to_matrix().to_4x4()
            delta = Matrix.Translation(head) @ rotation @ Matrix.Translation(-rest.translation)
            deltas[name] = delta
            previous = delta
    return deltas


def select(objects):
    bpy.ops.object.select_all(action='DESELECT')
    for obj in objects: obj.select_set(True)
    bpy.context.view_layer.objects.active = objects[-1]


def apply(obj, modifier):
    select([obj])
    bpy.ops.object.modifier_apply(modifier=modifier.name)


def tri_count(obj):
    obj.data.calc_loop_triangles()
    return len(obj.data.loop_triangles)


def local(point, wrist):
    return Vector((-(point.y-wrist.y), point.x-wrist.x, point.z-wrist.z))


def world(point, wrist):
    return Vector((point.y+wrist.x, -point.x+wrist.y, point.z+wrist.z))


def hand_base(source, arm, wrist, material):
    opened = deformations(arm, POSES['Open'], True)
    selected = [p for p in source.data.polygons if all(
        source.data.vertices[i].co.x >= wrist.x-.024 for i in p.vertices)]
    indices = sorted({i for p in selected for i in p.vertices})
    remap = {v: i for i,v in enumerate(indices)}
    groups = {g.index:g.name for g in source.vertex_groups}
    weights, points = [], []
    for i in indices:
        v = source.data.vertices[i]
        weight = {groups[g.group]:g.weight for g in v.groups}
        total = sum(weight.values())
        assert total > 0
        weight = {n:w/total for n,w in weight.items()}
        point = sum((opened.get(n, Matrix.Identity(4)) @ v.co*w for n,w in weight.items()), Vector())
        p=local(point,wrist)
        # Mép áo gốc loe thành vành ở cổ tay: thu vào dưới cuff mới, giữ nguyên pivot.
        if p.y<.009:
            radius=math.sqrt((p.x/.029)**2+(p.z/.021)**2)
            if radius>1:
                p.x/=radius; p.z/=radius
        points.append(p)
        weights.append(weight)
    uv = source.data.uv_layers.active.data
    coords = [[tuple(uv[i].uv) for i in p.loop_indices] for p in selected]
    atlas = np.array([c for poly in coords for c in poly])
    uv_min, uv_max = atlas.min(axis=0)-.003, atlas.max(axis=0)+.003
    coords = [[tuple((np.array(c)-uv_min)/(uv_max-uv_min)) for c in poly] for poly in coords]
    hand = object_mesh('Skin', points, [[remap[i] for i in p.vertices] for p in selected], coords, material)
    for i,weight in enumerate(weights):
        for name,value in weight.items():
            group = hand.vertex_groups.get(name) or hand.vertex_groups.new(name=name)
            group.add([i],value,'REPLACE')
    # Ghép tam giác cùng mặt rồi làm tròn silhouette; giữ UV và trọng số gốc.
    bm=bmesh.new(); bm.from_mesh(hand.data)
    bmesh.ops.join_triangles(bm, faces=list(bm.faces), angle_face_threshold=.8,
                            angle_shape_threshold=1.2, cmp_uvs=True)
    bm.to_mesh(hand.data); bm.free()
    sub=hand.modifiers.new('Khối ngón và khớp', 'SUBSURF'); sub.levels=1
    apply(hand,sub)
    if tri_count(hand)>2460:
        dec=hand.modifiers.new('Ngân sách da', 'DECIMATE'); dec.ratio=2450/tri_count(hand)
        apply(hand,dec)
    segments=[]
    for digit in DIGITS:
        chain=[]
        for part in ('Proximal','Intermediate','Distal'):
            name='Left'+digit+part
            bone=arm.data.bones[name]
            delta=opened[name]
            chain.append((local(delta @ (arm.matrix_world @ bone.head_local),wrist),
                          local(delta @ (arm.matrix_world @ bone.tail_local),wrist)))
        segments.append(chain)
    return hand, opened, segments, uv_min, uv_max, len(selected)


def sculpt(obj, segments, fine):
    obj.data.update()
    for v in obj.data.vertices:
        p=v.co.copy(); n=v.normal.copy()
        nearest=min(((max(0,min(1,(p-a).dot(b-a)/(b-a).length_squared)),a,b,j,d)
                     for d,chain in enumerate(segments) for j,(a,b) in enumerate(chain)),
                    key=lambda item:(p-(item[1]+(item[2]-item[1])*item[0])).length_squared)
        t,a,b,j,d=nearest
        axis=(b-a).normalized(); radial=p-(a+(b-a)*t)
        distance=radial.length
        height=0
        if distance<.016:
            # Khối khớp thật được giữ trên mesh runtime.
            joint=math.exp(-((p-a).dot(axis)/.0038)**2)
            height += .00085*joint*max(0,n.z)
            if fine:
                crease=math.exp(-((p-a).dot(axis)/.00085)**2)
                paired=math.exp(-(((p-a).dot(axis)-.002)/.00065)**2)
                height -= (.00048*crease+.00022*paired)*(.35+.65*abs(n.z))
                # Móng nổi, chân móng và mép tự do; màu lấy từ Rocketbox.
                if j==2 and n.z>.25:
                    nail=math.exp(-((t-.62)/.32)**8-(radial.x/.0045)**6)
                    height += .00075*nail
                    height -= .0003*math.exp(-((t-.28)/.055)**2)*nail
        if .018<p.y<.085 and n.z>.3:
            # Gân mu tay: gờ nhỏ chạy dọc dưới da.
            for chain in segments[1:]:
                cx=chain[0][0].x*.65
                height += .00045*math.exp(-((p.x-cx)/.0028)**2)*math.sin(math.pi*(p.y-.018)/.067)
        if fine:
            if n.z<-.25 and -.002<p.y<.09:
                for offset,slope in ((.043,.27),(.061,-.18),(.014,.55)):
                    height -= .00036*math.exp(-((p.y-offset-slope*p.x)/.0008)**2)
            # Nếp da vi mô được bake từ relief dưới một milimét.
            height += .000065*math.sin(p.x*7400+math.sin(p.y*3900))*math.sin(p.y*6100+p.z*1300)
        v.co += n*height
    obj.data.update()


def pixels(image):
    w,h=image.size
    result=np.empty(w*h*4,np.float32); image.pixels.foreach_get(result)
    return result.reshape(h,w,4)


def save_image(name, data, noncolor=False):
    h,w,_=data.shape
    image=bpy.data.images.new(name,width=w,height=h,alpha=True)
    image.colorspace_settings.name='Non-Color' if noncolor else 'sRGB'
    image.pixels.foreach_set(np.asarray(data,dtype=np.float32).ravel())
    image.filepath_raw=str(OUT/(name+'.png')); image.file_format='PNG'; image.save()
    return image


def crop_texture(kind, uv_min, uv_max, size=2048):
    image=bpy.data.images.load(str(TEXTURES/f'm003_body_{kind}.tga'),check_existing=False)
    if kind!='color': image.colorspace_settings.name='Non-Color'
    src=pixels(image); h,w,_=src.shape
    y,x=np.mgrid[0:size,0:size]/(size-1)
    x=(uv_min[0]+x*(uv_max[0]-uv_min[0]))*(w-1)
    y=(uv_min[1]+y*(uv_max[1]-uv_min[1]))*(h-1)
    ix,iy=x.astype(int),y.astype(int); fx=(x-ix)[...,None]; fy=(y-iy)[...,None]
    return (src[iy,ix]*(1-fx)*(1-fy)+src[iy,np.minimum(ix+1,w-1)]*fx*(1-fy)+
            src[np.minimum(iy+1,h-1),ix]*(1-fx)*fy+src[np.minimum(iy+1,h-1),np.minimum(ix+1,w-1)]*fx*fy).astype(np.float32)


def skin_regions(low, segments, size=2048):
    # Raster vị trí/normals theo UV để bụi nằm ở đầu ngón và lòng tay, không ở cổ tay.
    regions=np.zeros((size,size),np.float32)
    low.data.calc_loop_triangles(); uv=low.data.uv_layers.active.data
    tips=[np.array(chain[-1][1]) for chain in segments]
    for tri in low.data.loop_triangles:
        coords=np.array([uv[i].uv for i in tri.loops])*(size-1)
        x0,y0=np.maximum(0,np.floor(coords.min(axis=0)).astype(int))
        x1,y1=np.minimum(size-1,np.ceil(coords.max(axis=0)).astype(int))
        a,b,c=coords; area=np.cross(b-a,c-a)
        if abs(area)<.01: continue
        y,x=np.mgrid[y0:y1+1,x0:x1+1]
        q=np.stack((x,y),axis=-1)
        u=np.cross(b-q,c-q)/area; v=np.cross(c-q,a-q)/area; w=1-u-v
        inside=(u>=0)&(v>=0)&(w>=0)
        weights=np.stack((u,v,w),axis=-1)
        positions=weights @ np.array([low.data.vertices[i].co for i in tri.vertices])
        normals=weights @ np.array([low.data.vertices[i].normal for i in tri.vertices])
        fingertip=np.maximum.reduce([np.exp(-np.sum((positions-tip)**2,axis=2)/.013**2) for tip in tips])
        palm=np.maximum(0,-normals[:,:,2])*.35*np.exp(-((positions[:,:,1]-.045)/.05)**2)
        patch=regions[y0:y1+1,x0:x1+1]
        patch[inside]=np.clip(fingertip+palm,0,1)[inside]
    return regions


def bake_skin(low, high, uv_min, uv_max, segments):
    scene=bpy.context.scene; scene.render.engine='CYCLES'; scene.cycles.samples=1
    scene.render.threads_mode='FIXED'; scene.render.threads=2
    image=bpy.data.images.new('SculptBake',width=2048,height=2048)
    image.colorspace_settings.name='Non-Color'
    mat=low.data.materials[0]; mat.use_nodes=True
    target=mat.node_tree.nodes.new('ShaderNodeTexImage'); target.image=image
    mat.node_tree.nodes.active=target
    select([high,low])
    scene.render.bake.use_selected_to_active=True; scene.render.bake.cage_extrusion=.006
    scene.render.bake.margin=16
    bpy.ops.object.bake(type='NORMAL',normal_space='TANGENT')
    detail=pixels(image)[:,:,:3]*2-1
    original=crop_texture('normal',uv_min,uv_max)[:,:,:3]*2-1
    # Ghép normal tiếp tuyến của sculpt và atlas gốc bằng RNM.
    t=detail+np.array([0,0,1]); u=original*np.array([-1,-1,1])
    normal=t*np.sum(t*u,axis=2)[...,None]/np.maximum(t[:,:,2:3],.01)-u
    normal /= np.maximum(np.linalg.norm(normal,axis=2)[...,None],1e-6)
    out=np.ones((2048,2048,4),np.float32); out[:,:,:3]=normal*.5+.5
    save_image('FP_Skin_Normal',out,True)
    albedo=crop_texture('color',uv_min,uv_max)
    # Da rám nắng, giữ nếp nhăn/móng của nguồn; bụi tro lốm đốm nhẹ, không phủ đen cả tay.
    y,x=np.mgrid[0:2048,0:2048]/2048
    rng=np.random.default_rng(1911)
    grain=rng.random((2048,2048))-.5
    soot=skin_regions(low,segments)*(.055+.12*np.maximum(0,np.sin(x*math.tau*23)*np.sin(y*math.tau*19)-.25))
    # Tăng tương phản nếp da cục bộ để đèn fill không làm lòng tay thành mảng trắng.
    luminance=albedo[:,:,:3].mean(axis=2)
    local_mean=sum(np.roll(luminance,(dy,dx),(0,1)) for dy in (-24,0,24) for dx in (-24,0,24))/9
    albedo[:,:,:3]=np.clip(albedo[:,:,:3]+.45*(luminance-local_mean)[...,None],0,1)
    albedo[:,:,:3]*=np.array([.82,.72,.61])[None,None,:]*(1-soot+.018*grain)[...,None]
    save_image('FP_Skin_BaseColor',albedo)
    spec=crop_texture('specular',uv_min,uv_max)
    spec[:,:,:3]=np.clip(spec[:,:,:3]*.28+.10,.10,.27)
    spec[:,:,3]=.55+.15*np.mean(spec[:,:,:3],axis=2)
    save_image('FP_Skin_SpecGloss',spec,True)
    return image


def fabric_textures():
    size=1024; y,x=np.mgrid[0:size,0:size]/size
    # Vải cotton thô: sợi đan chéo, độ xoắn và vết làm việc.
    warp=np.cos(x*math.tau*180); weft=np.cos(y*math.tau*170)
    height=.65*warp+.5*weft+.22*warp*weft
    stain=.075*np.sin(x*math.tau*3)**4*np.sin(y*math.tau*2)**6
    rgb=np.ones((size,size,4),np.float32)
    rgb[:,:,:3]=(1+.055*height-stain)[...,None]*np.array([.49,.43,.34])
    save_image('FP_Sleeve_BaseColor',rgb)
    dy,dx=np.gradient(height)
    normal=np.dstack((-dx*.32,-dy*.32,np.ones_like(x)))
    normal/=np.linalg.norm(normal,axis=2)[...,None]
    rgb[:,:,:3]=normal*.5+.5
    save_image('FP_Sleeve_Normal',rgb,True)


def sleeve(material):
    vertices,faces,uvs=[],[],[]
    # Mép trong nối cổ tay; cuff xắn hai lớp có khối lùi lên cẳng tay.
    rings=[(.001,.029,.020),(-.012,.030,.022),(-.021,.035,.026),
           (-.025,.040,.031),(-.032,.042,.033),(-.041,.040,.031),
           (-.046,.042,.033),(-.054,.043,.033),(-.061,.040,.030),
           (-.082,.041,.031),(-.125,.045,.034),(-.18,.049,.037)]
    sides=24
    for z,rx,ry in rings:
        for j in range(sides):
            a=math.tau*j/sides
            fold=1+.055*math.cos(a*5+z*48)+.026*math.cos(a*9-z*62)
            vertices.append(blender_point(Vector((rx*math.cos(a)*fold,ry*math.sin(a)*fold,z))))
    for i in range(len(rings)-1):
        for j in range(sides):
            k=(j+1)%sides
            faces.append((i*sides+j,i*sides+k,(i+1)*sides+k,(i+1)*sides+j))
            a,b=-rings[i][0]*4,-rings[i+1][0]*4
            uvs.append(((j/sides,a),((j+1)/sides,a),((j+1)/sides,b),(j/sides,b)))
    return object_mesh('Sleeve',vertices,faces,uvs,material)


def posed_mesh(base, arm, wrist, opened, pose, side):
    obj=base.copy(); obj.data=base.data.copy(); obj.name='Skin'
    bpy.context.collection.objects.link(obj)
    deltas=deformations(arm,POSES[pose],pose=='Open')
    relative={n:d @ opened[n].inverted() for n,d in deltas.items()}
    groups={g.index:g.name for g in obj.vertex_groups}
    for v in obj.data.vertices:
        point=world(v.co,wrist)
        weight=[(groups[g.group],g.weight) for g in v.groups]
        total=sum(w for _,w in weight); assert total>0
        point=sum((relative.get(n,Matrix.Identity(4)) @ point*w/total for n,w in weight),Vector())
        p=local(point,wrist)
        if side=='R': p.x=-p.x
        v.co=p
    if side=='R':
        bm=bmesh.new(); bm.from_mesh(obj.data)
        bmesh.ops.reverse_faces(bm,faces=list(bm.faces)); bm.to_mesh(obj.data); bm.free()
    obj.vertex_groups.clear()
    return obj


def preview(obj, cuff):
    # Ảnh kỹ thuật mu tay và lòng tay, render trực tiếp từ mesh.
    mat=obj.data.materials[0]; mat.use_nodes=True
    nodes=mat.node_tree.nodes; links=mat.node_tree.links; bsdf=nodes.get('Principled BSDF')
    bsdf.inputs['Roughness'].default_value=.72
    tex=nodes.new('ShaderNodeTexImage'); tex.image=bpy.data.images.load(str(OUT/'FP_Skin_BaseColor.png'))
    links.new(tex.outputs['Color'],bsdf.inputs['Base Color'])
    tex=nodes.new('ShaderNodeTexImage'); tex.image=bpy.data.images.load(str(OUT/'FP_Skin_Normal.png'))
    tex.image.colorspace_settings.name='Non-Color'
    norm=nodes.new('ShaderNodeNormalMap'); links.new(tex.outputs['Color'],norm.inputs['Color']); links.new(norm.outputs['Normal'],bsdf.inputs['Normal'])
    mat=cuff.data.materials[0]; mat.use_nodes=True
    bsdf=mat.node_tree.nodes.get('Principled BSDF'); bsdf.inputs['Roughness'].default_value=.9
    tex=mat.node_tree.nodes.new('ShaderNodeTexImage'); tex.image=bpy.data.images.load(str(OUT/'FP_Sleeve_BaseColor.png'))
    mat.node_tree.links.new(tex.outputs['Color'],bsdf.inputs['Base Color'])
    tex=mat.node_tree.nodes.new('ShaderNodeTexImage'); tex.image=bpy.data.images.load(str(OUT/'FP_Sleeve_Normal.png'))
    tex.image.colorspace_settings.name='Non-Color'
    norm=mat.node_tree.nodes.new('ShaderNodeNormalMap')
    mat.node_tree.links.new(tex.outputs['Color'],norm.inputs['Color']); mat.node_tree.links.new(norm.outputs['Normal'],bsdf.inputs['Normal'])
    scene=bpy.context.scene; scene.render.engine='BLENDER_EEVEE_NEXT'
    scene.render.resolution_x=1000; scene.render.resolution_y=1000; scene.render.resolution_percentage=100
    scene.world=bpy.data.worlds.new('Studio'); scene.world.color=(.18,.18,.18)
    camera=bpy.data.objects.new('QA Camera',bpy.data.cameras.new('QA Camera')); bpy.context.collection.objects.link(camera)
    camera.data.type='ORTHO'; camera.data.ortho_scale=.34; scene.camera=camera
    for name,pos,power,size in [('Key',(.12,.12,.30),1.3,.2),('Fill',(-.15,.08,-.3),.8,.18)]:
        light=bpy.data.objects.new(name,bpy.data.lights.new(name,'AREA')); bpy.context.collection.objects.link(light)
        light.location=pos; light.rotation_euler=(Vector((0,.03,0))-light.location).to_track_quat('-Z','Y').to_euler()
        light.data.energy=power; light.data.shape='DISK'; light.data.size=size
    for label,z in [('dorsal',.6),('palm',-.6)]:
        camera.location=(0,.03,z); camera.rotation_euler=(Vector((0,.03,0))-camera.location).to_track_quat('-Z','Y').to_euler()
        scene.render.filepath=str(ROOT/'Logs'/f'hands3_{label}_preview.png'); bpy.ops.render.render(write_still=True)
    for o in list(bpy.context.scene.objects):
        if o.type in ('CAMERA','LIGHT'): bpy.data.objects.remove(o,do_unlink=True)


def main():
    assert SOURCE.is_file(),SOURCE
    OUT.mkdir(parents=True,exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(SOURCE),use_anim=False)
    import prepare_cast as cast
    import prepare_crowd as crowd
    original=next(o for o in bpy.context.scene.objects if o.type=='ARMATURE')
    source=next(o for o in bpy.context.scene.objects if o.type=='MESH')
    temp,a_rest,_,src_world=cast.make_rig(original)
    arm=crowd.canonical_rig(); bind={b.name:b.matrix_local.copy() for b in arm.data.bones}
    cast.remap_mesh(source,original,arm,a_rest,bind,src_world)
    wrist=arm.matrix_world @ arm.data.bones['LeftHand'].head_local
    skinmat=bpy.data.materials.new('FP_Skin'); clothmat=bpy.data.materials.new('FP_Sleeve')
    low,opened,segments,uv_min,uv_max,source_tris=hand_base(source,arm,wrist,skinmat)
    for o in list(bpy.context.scene.objects):
        if o not in (low,arm): bpy.data.objects.remove(o,do_unlink=True)
    sculpt(low,segments,False)
    high=low.copy(); high.data=low.data.copy(); high.name='Sculpt_7200'; bpy.context.collection.objects.link(high)
    sub=high.modifiers.new('Sculpt chi tiết', 'SUBSURF'); sub.levels=1; apply(high,sub)
    if tri_count(high)>8000:
        dec=high.modifiers.new('Sculpt 7.2k', 'DECIMATE'); dec.ratio=7200/tri_count(high); apply(high,dec)
    sculpt(high,segments,True)
    assert 4000<=tri_count(high)<=8000,tri_count(high)
    print('SCULPT',tri_count(low),tri_count(high),flush=True)
    bake_skin(low,high,uv_min,uv_max,segments); fabric_textures()
    high_tris=tri_count(high)
    bpy.data.objects.remove(high,do_unlink=True)
    report={'source':str(SOURCE.relative_to(ROOT)),'license':'MIT / Microsoft Rocketbox',
            'source_hand_triangles':source_tris,'sculpt_triangles':high_tris,
            'pivot':'wrist','frame':'Unity +Z fingers, -Y palm, left thumb +X',
            'textures':'Da 2048² (atlas cắt + sculpt bake), vải 1024²','models':{}}
    cuff=sleeve(clothmat); low.hide_render=False
    arm.hide_render=True
    preview(low,cuff)
    bpy.data.objects.remove(cuff,do_unlink=True)
    for pose in POSES:
        for side in ('L','R'):
            objects=[posed_mesh(low,arm,wrist,opened,pose,side),sleeve(clothmat)]
            select(objects)
            counts={o.name.split('.')[0]:tri_count(o) for o in objects}
            assert 200<=sum(counts.values())<=3000,counts
            name=f'Hand_{side}_{pose}'
            bpy.ops.export_scene.fbx(filepath=str(OUT/(name+'.fbx')),use_selection=True,
                object_types={'MESH'},axis_forward='-Z',axis_up='Y',bake_space_transform=True,
                apply_unit_scale=True,apply_scale_options='FBX_SCALE_UNITS',bake_anim=False,
                add_leaf_bones=False,path_mode='RELATIVE',embed_textures=False)
            report['models'][name]=counts
            for o in objects: bpy.data.objects.remove(o,do_unlink=True)
    (OUT/'manifest.json').write_text(json.dumps(report,indent=2,ensure_ascii=False)+'\n')
    print('FP_HANDS_REPORT',json.dumps(report,ensure_ascii=False),flush=True)


if __name__=='__main__': main()
