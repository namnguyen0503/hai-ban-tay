"""Retarget/author 15 clips on the prepared Rocketbox T-pose skeleton, 30 fps.

blender -b --factory-startup -P Tools/blender/export_anims.py -- --pass 1
"""
import argparse
import json
import math
import shutil
import sys
from pathlib import Path

sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parent))
import bpy
from mathutils import Matrix, Quaternion, Vector
import prepare_cast as cast

SCRATCH, ANIMS, CHARACTERS, SOURCES = cast.SCRATCH, cast.ANIMS, cast.CHARACTERS, cast.SOURCES
ROCKET = SOURCES / "MicrosoftRocketbox_Animations/Assets/Animations"
KAY = SOURCES / "KayKit_CharacterAnimations_1.1/Animations/fbx/Rig_Medium"
SPECS = {
    "Idle": ("static", "m_idle_neutral_01", None),
    "IdleLook": ("static", "m_idle_look_around_01", None),
    "Walk": ("xy", "m_walk_neutral", None),
    "WorkTable": ("static", "m_work_table", None),
    "WorkMid": ("static", "m_work_mid", None),
    "Talk": ("static", "m_gestic_talk_neutral_01", (121, 361)),
    "SitIdle": ("static", "m_sit_chair_idle_neutral_01", (301, 481)),
    "SitDown": ("xyz", "m_sit_down_chair_01", None),
    "StandUp": ("xyz", "m_sit_stand_up_chair_01", None),
    "Documents": ("static", "m_documents_idle", (241, 421)),
    "Presentation": ("static", "m_gestic_presentation_right_01", None),
}
CLIPS = ["Idle", "IdleLook", "Walk", "WorkTable", "WorkMid", "Stir", "HoldCarry", "CarryWalk",
         "PickUp", "Point", "Talk", "SitIdle", "SitRead", "SitDown", "StandUp"]
ONESHOT = {"PickUp", "Point", "SitDown", "StandUp"}
KAY_MAP = {"Hips": "hips", "Spine": "spine", "Chest": "chest", "Head": "head"}
for label, suffix in [("Left", "l"), ("Right", "r")]:
    for new, old in [("UpperArm", "upperarm"), ("LowerArm", "lowerarm"), ("Hand", "hand"),
                     ("UpperLeg", "upperleg"), ("LowerLeg", "lowerleg"), ("Foot", "foot"), ("Toes", "toes")]:
        KAY_MAP[label + new] = old + "." + suffix


def retarget(rig, skeleton, path, kay_action=None, frame_range=None):
    """Sample world rotations, then rebake against corrected parents and T-pose."""
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=str(path), use_anim=True)
    imported = set(bpy.data.objects) - before
    source = next(o for o in imported if o.type == "ARMATURE")
    if kay_action:
        action = bpy.data.actions.get("Rig_Medium|" + kay_action)
    else:
        action = source.animation_data.action
    assert action, (path, kay_action)
    inventory=json.loads((SOURCES/"_evidence/fbx-inventory.json").read_text())
    entry=next(item for item in inventory if item["file"]==str(path.relative_to(SOURCES)))
    original=next(a for a in entry["actions"] if a["name"]==("Rig_Medium|"+kay_action if kay_action else "Bip01|Take 001|BaseLayer"))
    assert list(action.frame_range)==original["frames"], (path,action.name,list(action.frame_range),original["frames"])
    source.animation_data_create().action = action
    start, end = frame_range or tuple(round(v) for v in action.frame_range)
    rest = {b.name: b.matrix_local.copy() for b in rig.data.bones}
    a_rest = {n: Matrix(m) for n, m in skeleton["a_rest"].items()}
    character_bind = {n: Matrix(m) for n, m in skeleton["rocketbox_rest"].items()}
    mapping = KAY_MAP if kay_action else {n: old for old, n in cast.MAP.items()}
    source_rest = {n: source.matrix_world @ source.data.bones[n].matrix_local for n in mapping.values()}
    hip_rest = source_rest[mapping["Hips"]].translation
    scale = 1
    if kay_action:
        scale = ((rest["LeftUpperLeg"].translation-rest["LeftLowerLeg"].translation).length +
                 (rest["LeftLowerLeg"].translation-rest["LeftFoot"].translation).length) / (
                 (source_rest["upperleg.l"].translation-source_rest["lowerleg.l"].translation).length +
                 (source_rest["lowerleg.l"].translation-source_rest["foot.l"].translation).length)
    samples = []
    for frame in range(start, end + 1):
        bpy.context.scene.frame_set(frame)
        bpy.context.view_layer.update()
        desired = {}
        for bone in rig.data.bones:
            name = bone.name
            if name in mapping:
                old = mapping[name]
                pose = source.matrix_world @ source.pose.bones[old].matrix
                # Rocketbox motion FBXs store clip-specific bind poses. Physical
                # joint axes come from the CHARACTER bind, not each motion bind.
                reference = source_rest[old] if kay_action else character_bind[old]
                delta = pose.to_quaternion() @ reference.to_quaternion().conjugated()
                orientation = delta @ (rest[name] if kay_action else a_rest[name]).to_quaternion()
            elif kay_action and name in ["UpperChest", "Neck"]:
                orientation = desired[bone.parent.name].to_quaternion() @ (
                    rest[bone.parent.name].to_quaternion().conjugated() @ rest[name].to_quaternion())
            else:
                parent = bone.parent
                orientation = desired[parent.name].to_quaternion() @ (
                    rest[parent.name].to_quaternion().conjugated() @ rest[name].to_quaternion())
            mat = orientation.to_matrix().to_4x4()
            if bone.parent:
                mat.translation = desired[bone.parent.name] @ (rest[bone.parent.name].inverted() @ rest[name].translation)
            else:
                src_head = (source.matrix_world @ source.pose.bones[mapping["Hips"]].matrix).translation
                mat.translation = rest[name].translation + (src_head - hip_rest) * scale
                # In-place means no planar root movement; vertical seating/bend stays.
                mat.translation.x = rest[name].translation.x
                mat.translation.y = rest[name].translation.y
            desired[name] = mat
        local = {}
        for bone in rig.data.bones:
            name = bone.name
            if bone.parent:
                parent = bone.parent.name
                basis = (rest[parent].inverted() @ rest[name]).inverted() @ desired[parent].inverted() @ desired[name]
            else:
                basis = rest[name].inverted() @ desired[name]
            local[name] = [basis.translation.copy(), basis.to_quaternion().normalized()]
        samples.append(local)
    source_description = {"pack": "KayKit_CharacterAnimations_1.1" if kay_action else "MicrosoftRocketbox_Animations",
                          "file": str(path.relative_to(SOURCES)), "action": action.name,
                          "source_frames": [start, end], "license": "CC0-1.0" if kay_action else "MIT"}
    for obj in imported:
        bpy.data.objects.remove(obj, do_unlink=True)
    for a in list(bpy.data.actions):
        if a.users == 0:
            bpy.data.actions.remove(a)
    return samples, source_description


def copy_pose(pose):
    return {n: [loc.copy(), rot.copy()] for n, (loc, rot) in pose.items()}


def cyclic(samples, width=12):
    """Blend seam on both ends; final sample duplicates first at loop boundary."""
    width = min(width, (len(samples) - 1) // 4)
    if width < 1:
        return samples
    seam = {}
    for n in samples[0]:
        seam[n] = [samples[0][n][0].lerp(samples[-1][n][0], .5), samples[0][n][1].slerp(samples[-1][n][1], .5)]
    for i in range(width):
        t = i / width
        weight = 1 - t*t*(3-2*t)
        for index in [i, len(samples)-1-i]:
            for n in seam:
                samples[index][n][0] = samples[index][n][0].lerp(seam[n][0], weight)
                samples[index][n][1] = samples[index][n][1].slerp(seam[n][1], weight)
    samples[-1] = copy_pose(samples[0])
    return samples


def world_pose(rig, pose):
    result = {}
    for b in rig.data.bones:
        local = Matrix.LocRotScale(pose[b.name][0], pose[b.name][1], Vector((1,1,1)))
        if b.parent:
            result[b.name] = result[b.parent.name] @ b.parent.matrix_local.inverted() @ b.matrix_local @ local
        else:
            result[b.name] = b.matrix_local @ local
    return result


def set_world_rotation(rig, pose, name, rotation):
    world = world_pose(rig, pose)
    bone = rig.data.bones[name]
    desired = rotation.to_matrix().to_4x4()
    desired.translation = world[name].translation
    if bone.parent:
        local_rest = bone.parent.matrix_local.inverted() @ bone.matrix_local
        basis = local_rest.inverted() @ world[bone.parent.name].inverted() @ desired
    else:
        basis = bone.matrix_local.inverted() @ desired
    pose[name][1] = basis.to_quaternion().normalized()


def two_bone_ik(rig, pose, upper, lower, hand, goal, pole):
    """Analytic two-bone reach for hands and feet; export no controls."""
    world = world_pose(rig, pose)
    shoulder = world[upper].translation
    l1 = (rig.data.bones[lower].head_local-rig.data.bones[upper].head_local).length
    l2 = (rig.data.bones[hand].head_local-rig.data.bones[lower].head_local).length
    vector = Vector(goal) - shoulder
    distance = min(vector.length, l1 + l2 - .002)
    direction = vector.normalized()
    cos_angle = min(1, max(-1, (l1*l1 + distance*distance - l2*l2)/(2*l1*distance)))
    side = Vector(pole)-shoulder
    side = (side-direction*side.dot(direction)).normalized()
    elbow = shoulder + direction*(l1*cos_angle) + side*(l1*math.sqrt(1-cos_angle*cos_angle))
    for name, target in [(upper,elbow),(lower,Vector(goal))]:
        world = world_pose(rig, pose)
        end = lower if name == upper else hand
        current = world[end].translation-world[name].translation
        delta = current.rotation_difference(target-world[name].translation)
        set_world_rotation(rig,pose,name,delta @ world[name].to_quaternion())
    return (world_pose(rig,pose)[hand].translation-Vector(goal)).length


def arm_ik(rig, pose, label, goal, pole, palm_direction=None):
    hand=label+"Hand"
    error=two_bone_ik(rig,pose,label+"UpperArm",label+"LowerArm",hand,goal,pole)
    if palm_direction:
        world = world_pose(rig,pose)
        current = world[hand].to_3x3() @ Vector((0,1,0))
        delta = current.rotation_difference(Vector(palm_direction))
        set_world_rotation(rig,pose,hand,delta @ world[hand].to_quaternion())
    return error


def hold_fingers(pose, label, point=False):
    for finger in ["Thumb","Index","Middle","Ring","Little"]:
        for part in ["Proximal","Intermediate","Distal"]:
            name=label+finger+part
            angle=0 if point and finger=="Index" else .50 if finger=="Thumb" else .85
            pose[name][1] = Quaternion((1,0,0), angle)


def bake(rig, name, samples):
    rig.animation_data_clear()
    action=bpy.data.actions.new(name)
    rig.animation_data_create().action=action
    rig.data.pose_position="POSE"
    previous={}
    # FBX armature-only files use the current pose as their default transform.
    # Frame zero supplies the shared bind pose; only frames 1..N are exported.
    rest={name:[Vector(),Quaternion()] for name in cast.PARENTS}
    for i,pose in enumerate([rest]+samples):
        for bone in rig.pose.bones:
            bone.rotation_mode="QUATERNION"
            loc,rot=pose[bone.name]
            if bone.name in previous and previous[bone.name].dot(rot)<0: rot=-rot
            bone.location=loc
            bone.rotation_quaternion=rot
            bone.scale=(1,1,1)
            bone.keyframe_insert("location",frame=i,group=bone.name)
            bone.keyframe_insert("rotation_quaternion",frame=i,group=bone.name)
            previous[bone.name]=rot.copy()
    for fc in action.fcurves:
        for key in fc.keyframe_points: key.interpolation="LINEAR"
    return action


def preview(rig, meshes, clips, pass_number):
    before=set(bpy.data.objects)
    for i,name in enumerate(CLIPS):
        action=bpy.data.actions[name]
        rig.animation_data.action=action
        bpy.context.scene.frame_set(1+(len(clips[name])-1)//2)
        bpy.context.view_layer.update()
        offset=Vector(((i%5-2)*1.55,0,(2-i//5)*2.20))
        for obj in meshes:
            evaluated=obj.evaluated_get(bpy.context.evaluated_depsgraph_get())
            data=bpy.data.meshes.new_from_object(evaluated)
            copy=bpy.data.objects.new(name+"_"+obj.name,data)
            bpy.context.collection.objects.link(copy)
            copy.matrix_world=obj.matrix_world.copy()
            copy.location+=offset
        cast.caption(name,offset.x,offset.z-.20,.12)
    rig.hide_render=True
    for obj in meshes: obj.hide_render=True
    cast.caption("30 fps  |  Mid-frame poses on Cook  |  15 shared Humanoid clips",0,6.55,.115)
    path=SCRATCH/f"anim_preview_pass{pass_number}.png"
    cast.studio(path,(0,0,3.15),8.15,7.2,(1900,1700),samples=24)
    shutil.copyfile(path,ANIMS/"anim_preview.png")
    for obj in set(bpy.data.objects)-before:
        bpy.data.objects.remove(obj,do_unlink=True)
    rig.hide_render=False
    for obj in meshes: obj.hide_render=False


def main(pass_number):
    bpy.ops.wm.open_mainfile(filepath=str(SCRATCH/"Cook.blend"))
    rig=next(o for o in bpy.data.objects if o.type=="ARMATURE")
    meshes=[o for o in bpy.data.objects if o.type=="MESH"]
    skeleton=json.loads((CHARACTERS/"skeleton.json").read_text())
    assert len(rig.data.bones)==52
    rig.data.pose_position="POSE"
    clips,provenance={},{}
    for name,(folder,stem,trim) in SPECS.items():
        path=ROCKET/("all_animations_max_motextr_"+folder)/(stem+".max.fbx")
        clips[name],src=retarget(rig,skeleton,path,frame_range=trim)
        provenance[name]={"sources":[src],"edits":["World-space rotation retarget; thighs to Hips, shoulders to UpperChest; facial/helper motion omitted; planar hips translation neutralized."]}
        print("RETARGET",name,len(clips[name]),flush=True)
    clips["HoldCarry"],src=retarget(rig,skeleton,KAY/"Rig_Medium_Tools.fbx",kay_action="Holding_A")
    provenance["HoldCarry"]={"sources":[src],"edits":["KayKit T-pose rotation retarget; adult wrist reach and finger grip authored at waist."]}
    for i,pose in enumerate(clips["HoldCarry"]):
        for label,sign in [("Left",1),("Right",-1)]:
            goal=(sign*.155,-.29,1.065+.002*math.sin(i/max(1,len(clips["HoldCarry"])-1)*math.tau))
            arm_ik(rig,pose,label,goal,(sign*.43,.01,1.15),(sign*-.25,-1,0))
            hold_fingers(pose,label)
    clips["PickUp"],src=retarget(rig,skeleton,KAY/"Rig_Medium_General.fbx",kay_action="PickUp")
    pickup_errors=[]
    for i,pose in enumerate(clips["PickUp"]):
        phase=i/(len(clips["PickUp"])-1)
        t=min(1,phase/.60) if phase<.60 else max(0,(1-phase)/.40)
        depth=t*t*(3-2*t)
        pose["Hips"][0]=rig.data.bones["Hips"].matrix_local.to_quaternion().conjugated() @ Vector((0,0,-.525*depth))
        pose["Hips"][1]=Quaternion((1,0,0),0)
        for name,angle in [("Spine",.95),("Chest",.40),("UpperChest",.20)]:
            pose[name][1]=Quaternion((1,0,0),angle*depth)
        for label,sign in [("Left",1),("Right",-1)]:
            foot=rig.data.bones[label+"Foot"].head_local.copy()
            two_bone_ik(rig,pose,label+"UpperLeg",label+"LowerLeg",label+"Foot",foot,(sign*.13,-.50,.35))
            for part in ["Foot","Toes"]:
                set_world_rotation(rig,pose,label+part,rig.data.bones[label+part].matrix_local.to_quaternion())
            goal=(sign*(.155-.045*depth),-.29-.17*depth,1.065-.95*depth)
            error=arm_ik(rig,pose,label,goal,(sign*.43,-.17,.45+.60*(1-depth)),(0,-.5,-.7))
            if depth>.99: pickup_errors.append(error)
            hold_fingers(pose,label)
    provenance["PickUp"]={"sources":[src],"edits":["KayKit PickUp retarget, then adult floor reach authored: hips lower 0.525 m, spine bends, feet pinned with analytic IK; both wrists lower to 0.115 m and lift to waist. Planar drift removed."]}
    clips["Stir"]=[copy_pose(clips["WorkTable"][len(clips["WorkTable"])//2]) for i in range(61)]
    errors=[]
    for i,pose in enumerate(clips["Stir"]):
        theta=i/60*math.tau
        pose["Hips"][0]+=rig.data.bones["Hips"].matrix_local.to_quaternion().conjugated() @ Vector((0,0,.002*math.sin(theta)))
        goal=(-.075+.044*math.cos(theta),-.285+.044*math.sin(theta),1.075)
        errors.append(arm_ik(rig,pose,"Right",goal,(-.42,-.01,1.17),(0,-.3,-1)))
        arm_ik(rig,pose,"Left",(.17,-.30,1.04),(.42,-.01,1.17),(0,-1,-.2))
        hold_fingers(pose,"Right")
    provenance["Stir"]={"sources":provenance["WorkTable"]["sources"],"edits":["Mid work-table pose with periodic breathing; analytic right-arm IK follows an 88 mm diameter horizontal circle at wrist z=1.075 m, left wrist steady at counter; finger grip authored."]}
    upper=[n for n in cast.PARENTS if not any(n.startswith(s+part) for s in ["Left","Right"] for part in ["UpperLeg","LowerLeg","Foot","Toes"]) and n!="Hips"]
    clips["CarryWalk"]=[]
    for i,walk in enumerate(clips["Walk"]):
        pose=copy_pose(walk)
        hold=clips["HoldCarry"][round(i/(len(clips["Walk"])-1)*(len(clips["HoldCarry"])-1))]
        for n in upper: pose[n]=[hold[n][0].copy(),hold[n][1].copy()]
        clips["CarryWalk"].append(pose)
    provenance["CarryWalk"]={"sources":provenance["Walk"]["sources"]+provenance["HoldCarry"]["sources"],"edits":["Walk pelvis/legs/toes plus HoldCarry spine, arms, head and finger rotations."]}
    clips["SitRead"]=[]
    for i,seated in enumerate(clips["SitIdle"]):
        pose=copy_pose(seated)
        document=clips["Documents"][i%len(clips["Documents"])]
        for n in upper:
            if n not in ["Spine","Chest","UpperChest"]: pose[n]=[document[n][0].copy(),document[n][1].copy()]
        for label,sign in [("Left",1),("Right",-1)]:
            world=world_pose(rig,pose)
            chest=world["Chest"].translation
            arm_ik(rig,pose,label,(sign*.15,chest.y-.28,chest.z+.03),(sign*.41,chest.y-.06,chest.z-.10),(sign*-.4,-.65,.2))
            hold_fingers(pose,label)
        clips["SitRead"].append(pose)
    provenance["SitRead"]={"sources":provenance["SitIdle"]["sources"]+provenance["Documents"]["sources"],"edits":["Seated hips/legs/spine, document-holding arms/head, adult IK wrists at chest, finger grip authored; book/paper is a game prop."]}
    presentation=clips["Presentation"]
    reach=[-(world_pose(rig,p)["RightHand"].translation.y-world_pose(rig,p)["RightUpperArm"].translation.y) for p in presentation]
    peak=max(range(len(reach)),key=reach.__getitem__)
    start=max(0,min(len(presentation)-121,peak-60))
    clips["Point"]=[]
    for i,source_pose in enumerate(presentation[start:start+121]):
        weight=min(1,i/30,(120-i)/35)
        weight=max(0,weight)
        weight=weight*weight*(3-2*weight)
        neutral=copy_pose(clips["Idle"][0])
        posed=copy_pose(source_pose)
        arm_ik(rig,posed,"Right",(-.40,-.37,1.34),(-.48,-.06,1.12),(-.4,-1,.08))
        hold_fingers(posed,"Right",point=True)
        for name in neutral:
            neutral[name][0]=neutral[name][0].lerp(posed[name][0],weight)
            neutral[name][1]=neutral[name][1].slerp(posed[name][1],weight)
        clips["Point"].append(neutral)
    provenance["Point"]={"sources":provenance["Presentation"]["sources"]+provenance["Idle"]["sources"],"edits":[f"121-frame presentation window {start+1}..{start+121}; right arm reach authored at chest, index extended and other fingers curled; ease from/to Idle."]}
    for name in CLIPS:
        if name not in ONESHOT:
            cyclic(clips[name],width=4 if name in ["Walk","CarryWalk"] else 12)
            provenance[name]["edits"].append("Seam blended; last frame duplicates first for loop closure.")
        action=bake(rig,name,clips[name])
        action.use_fake_user=True
        scene=bpy.context.scene
        scene.render.fps=30
        scene.frame_start=1
        scene.frame_end=len(clips[name])
        scene.frame_set(0)
        cast.export_fbx(ANIMS/(name+".fbx"),[rig],animation=True)
        provenance[name].update({"frames":len(clips[name]),"fps":30,"loop":name not in ONESHOT})
        print("CLIP_EXPORTED",name,len(clips[name]),flush=True)
    for name in ["Documents","Presentation"]:
        del provenance[name]
    (ANIMS/"clip_manifest.json").write_text(json.dumps(provenance,indent=2))
    checks={"stir_max_wrist_error_m":max(errors),"pickup_contact_max_wrist_error_m":max(pickup_errors)}
    assert checks["stir_max_wrist_error_m"]<.002,checks
    assert checks["pickup_contact_max_wrist_error_m"]<.02,checks
    (SCRATCH/"authoring_checks.json").write_text(json.dumps(checks,indent=2))
    shutil.copyfile(SOURCES/"MicrosoftRocketbox_Animations/LICENSE.md",ANIMS/"LICENSE_Rocketbox_MIT.txt")
    licenses=list((SOURCES/"KayKit_CharacterAnimations_1.1").glob("*icense*"))
    assert licenses, "Missing staged KayKit license"
    shutil.copyfile(licenses[0],ANIMS/"LICENSE_KayKit_CC0.txt")
    rig.animation_data.action=bpy.data.actions["Idle"]
    bpy.context.scene.frame_set(1)
    bpy.ops.wm.save_as_mainfile(filepath=str(SCRATCH/"cast_animations.blend"),check_existing=False)
    preview(rig,meshes,clips,pass_number)
    print("ANIMS_COMPLETE",json.dumps({n:len(clips[n]) for n in CLIPS}),flush=True)


if __name__=="__main__":
    parser=argparse.ArgumentParser()
    parser.add_argument("--pass",dest="pass_number",type=int,default=1)
    args=parser.parse_args(sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else [])
    main(args.pass_number)
