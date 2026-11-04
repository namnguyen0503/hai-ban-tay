"""Sixteen additional in-place clips using the existing retarget/export code."""
import argparse
import json
import math
import shutil
import sys
from pathlib import Path
sys.dont_write_bytecode=True
sys.path.insert(0,str(Path(__file__).resolve().parent))
import bpy
from mathutils import Matrix, Quaternion, Vector
import prepare_cast as cast
import prepare_crowd as crowd
import export_anims as anim

SCRATCH=crowd.SCRATCH
ANIMS=cast.ANIMS
CLIPS=["ShoulderCarry","ShoulderIdle","BagWalk","BagIdle","Documents","HandOver","Wave","LeanRail",
       "Squat","SitChair","Drink","Talk2","Listen","TrayCarry","Sweep","Pull"]
ONESHOT={"HandOver","Wave"}
SPECS={
    "Idle":("static","m_idle_neutral_01",(1,121)),
    "Walk":("xy","m_walk_neutral",None),
    "Bag":("static","m_hold_bag_idle",(151,331)),
    "Documents":("static","m_documents_note",(61,241)),
    "HandOver":("static","m_documents_out",None),
    "Wave":("static","m_wave_01",None),
    "SitChair":("static","m_sit_table_idle_neutral_01",(301,481)),
    "Drink":("static","m_drink_drinking",(241,481)),
    "Talk2":("static","m_gestic_talk_neutral_02",(181,421)),
    "Listen":("static","m_gestic_listen_accept_01",None),
    "Crouch":("static","m_crouch_gestic",(1,121)),
    "Work":("static","m_work_mid",(1,121)),
}
PREVIEW_ROLES={"ShoulderCarry":"PorterVN_A","ShoulderIdle":"PorterVN_B","BagWalk":"PassengerFR_M1",
    "BagIdle":"PassengerFR_M2","Documents":"Purser","HandOver":"Purser","Wave":"Le","LeanRail":"SailorFR",
    "Squat":"PorterVN_C","SitChair":"PassengerFR_M2","Drink":"PassengerFR_M2","Talk2":"Mai","Listen":"Mai",
    "TrayCarry":"Waiter","Sweep":"VendorVN_F","Pull":"DockerMRS"}


def resample(samples, count):
    result=[]
    for i in range(count):
        pos=i/(count-1)*(len(samples)-1)
        lo=int(pos);hi=min(lo+1,len(samples)-1);t=pos-lo
        result.append({n:[samples[lo][n][0].lerp(samples[hi][n][0],t),samples[lo][n][1].slerp(samples[hi][n][1],t)] for n in samples[lo]})
    return result


def overlay(pose, upper, names):
    for name in names:pose[name]=[upper[name][0].copy(),upper[name][1].copy()]


def breathing(rig, pose, theta, amount=.002):
    pose["Hips"][0]+=rig.data.bones["Hips"].matrix_local.to_quaternion().conjugated() @ Vector((0,0,amount*math.sin(theta)))


def hand(rig, pose, label, goal, pole, direction=(0,-1,0), grip=True):
    error=anim.arm_ik(rig,pose,label,goal,pole,direction)
    if grip:anim.hold_fingers(pose,label)
    return error


def generate():
    bpy.ops.wm.open_mainfile(filepath=str(cast.SCRATCH/"Cook.blend"))
    rig=next(o for o in bpy.data.objects if o.type=="ARMATURE")
    rig.data.pose_position="POSE"
    skeleton=json.loads((cast.CHARACTERS/"skeleton.json").read_text())
    raw,source={},{}
    for name,(folder,stem,trim) in SPECS.items():
        path=anim.ROCKET/("all_animations_max_motextr_"+folder)/(stem+".max.fbx")
        raw[name],source[name]=anim.retarget(rig,skeleton,path,frame_range=trim)
        print("CROWD_RETARGET",name,len(raw[name]),flush=True)
    clips={};manifest={};errors={}
    arms={label:[n for n in cast.PARENTS if n.startswith(label) and any(part in n for part in ["UpperArm","LowerArm","Hand","Thumb","Index","Middle","Ring","Little"])] for label in ["Left","Right"]}
    nonlegs=[n for n in cast.PARENTS if n!="Hips" and not any(n.startswith(label+part) for label in ["Left","Right"] for part in ["UpperLeg","LowerLeg","Foot","Toes"])]
    def record(name, bases, edits):
        manifest[name]={"sources":[source[b] for b in bases],"edits":["Existing world-space Rocketbox retarget on the canonical 52-bone skeleton; planar Hips and armature drift removed."]+edits}
    def track(name,value):errors.setdefault(name,[]).append(value)
    for name,base in [("ShoulderIdle","Idle"),("ShoulderCarry","Walk")]:
        clips[name]=[anim.copy_pose(p) for p in raw[base]]
        for i,p in enumerate(clips[name]):
            if name=="ShoulderCarry":overlay(p,raw["Idle"][i%len(raw["Idle"])],nonlegs)
            theta=i/(len(clips[name])-1)*math.tau
            if name=="ShoulderIdle":breathing(rig,p,theta)
            hip=anim.world_pose(rig,p)["Hips"].translation.z-rig.data.bones["Hips"].head_local.z
            track(name,hand(rig,p,"Right",(-.255,-.16,1.525+hip),(-.45,.03,1.19),direction=(0,-.8,.4)))
        record(name,[base,"Idle"] if name.endswith("Carry") else [base],["Right arm and gripping fingers authored to brace a sack on the right shoulder; prop not exported."])
    clips["BagIdle"]=[anim.copy_pose(p) for p in raw["Bag"]]
    clips["BagWalk"]=[anim.copy_pose(p) for p in raw["Walk"]]
    for name in ["BagIdle","BagWalk"]:
        for i,p in enumerate(clips[name]):
            bag=raw["Bag"][round(i/(len(clips[name])-1)*(len(raw["Bag"])-1))]
            overlay(p,bag,arms["Right"])
            theta=i/(len(clips[name])-1)*math.tau
            hip=anim.world_pose(rig,p)["Hips"].translation.z-rig.data.bones["Hips"].head_local.z
            track(name,hand(rig,p,"Right",(-.29,-.055+.012*math.sin(theta),.963+hip),(-.39,.06,1.25),direction=(0,0,-1)))
        record(name,["Bag","Walk"] if name=="BagWalk" else ["Bag"],["Source bag gesture on right arm; adult wrist reach at z=0.963 m plus pelvis bob, closed handle grip. Walking variant keeps source walk legs and left arm."])
    clips["Documents"]=[anim.copy_pose(p) for p in raw["Documents"]]
    for i,p in enumerate(clips["Documents"]):
        theta=i/180*math.tau
        # Ledger left palm, writing right wrist; tabletop height is 1.065 m.
        track("Documents",hand(rig,p,"Left",(.16,-.30,1.098),(.43,-.02,1.19),grip=False))
        track("Documents",hand(rig,p,"Right",(-.04+.025*math.sin(theta*3),-.32+.012*math.cos(theta*3),1.101),(-.39,-.02,1.16),grip=False))
        anim.hold_fingers(p,"Right",point=True)
    record("Documents",["Documents"],["Source writing at a ledger; wrist contact re-authored for a standing table at 1.065 m, left hand steady and right hand writing."])
    clips["HandOver"]=[anim.copy_pose(p) for p in raw["HandOver"]]
    for i,p in enumerate(clips["HandOver"]):
        phase=i/(len(clips["HandOver"])-1)
        t=min(1,phase/.38) if phase<.62 else max(0,(1-phase)/.38)
        t=t*t*(3-2*t)
        for label,sign in [("Left",1),("Right",-1)]:
            track("HandOver",hand(rig,p,label,(sign*.14,-.19-.195*t,1.06+.09*t),(sign*.41,-.06,1.18),grip=False))
            for n in arms[label]:
                if any(f in n for f in ["Thumb","Index","Middle","Ring","Little"]):p[n][1]=Quaternion((1,0,0),.18)
    record("HandOver",["HandOver"],["Document-out timing retained; both arms authored to present a small item together, hold, release, then return. One-shot."])
    clips["Wave"]=[anim.copy_pose(p) for p in raw["Wave"]]
    record("Wave",["Wave"],["Source male greeting wave; full 86-frame one-shot retained."])
    clips["LeanRail"]=[anim.copy_pose(p) for p in raw["Idle"]]
    for i,p in enumerate(clips["LeanRail"]):
        theta=i/120*math.tau
        p["Spine"][1]=Quaternion((1,0,0),.18+.01*math.sin(theta))
        p["Chest"][1]=Quaternion((1,0,0),.08)
        for label,sign in [("Left",1),("Right",-1)]:
            track("LeanRail",hand(rig,p,label,(sign*.225,-.385,1.06),(sign*.39,-.13,1.23),grip=False))
    record("LeanRail",["Idle"],["Forward torso lean, both wrists held at a 1.045 m rail, periodic breathing. Feet from source idle."])
    clips["Squat"]=[anim.copy_pose(raw["Crouch"][i]) for i in range(121)]
    squat_feet=[]
    for i,p in enumerate(clips["Squat"]):
        theta=i/120*math.tau
        p["Hips"][0]=rig.data.bones["Hips"].matrix_local.to_quaternion().conjugated() @ Vector((0,0,-.552+.002*math.sin(theta)))
        p["Hips"][1]=Quaternion()
        overlay(p,raw["Idle"][i],["Head","Neck"])
        for bone,angle in [("Spine",.23),("Chest",.06),("UpperChest",-.08)]:p[bone][1]=Quaternion((1,0,0),angle)
        for label,sign in [("Left",1),("Right",-1)]:
            foot=rig.data.bones[label+"Foot"].head_local.copy();foot.x=sign*.18;foot.y=-.05
            squat_feet.append(anim.two_bone_ik(rig,p,label+"UpperLeg",label+"LowerLeg",label+"Foot",foot,(sign*.27,-.65,.32)))
            for part in ["Foot","Toes"]:anim.set_world_rotation(rig,p,label+part,rig.data.bones[label+part].matrix_local.to_quaternion())
            track("Squat",hand(rig,p,label,(sign*.18,-.285,.475),(sign*.35,-.11,.67),grip=False))
    record("Squat",["Crouch","Idle"],["Deep squat authored: pelvis at about 0.343 m, knees forward/out, heels and soles kept flat by analytic leg IK, relaxed hands near knees. Idle head/neck replace the upward-looking crouch head."])
    clips["SitChair"]=[anim.copy_pose(p) for p in raw["SitChair"]]
    for i,p in enumerate(clips["SitChair"]):
        theta=i/180*math.tau
        # Static motion files encode the seated height in their bind pose; the
        # common standing bind must receive that vertical offset explicitly.
        p["Hips"][0]+=rig.data.bones["Hips"].matrix_local.to_quaternion().conjugated() @ Vector((0,0,-.319))
        for label,sign in [("Left",1),("Right",-1)]:track("SitChair",hand(rig,p,label,(sign*.18,-.35,.806+.001*math.sin(theta)),(sign*.39,-.04,.89),grip=False))
    record("SitChair",["SitChair"],["Stable source seated section; source static bind height restored by lowering Hips 0.319 m; both hands contact a cafe tabletop at 0.785 m."])
    clips["Drink"]=resample(clips["SitChair"],241)
    drinking=resample(raw["Drink"],241)
    for i,p in enumerate(clips["Drink"]):
        phase=i/240
        t=max(0,min(1,(phase-.12)/.22,(.86-phase)/.22))
        t=t*t*(3-2*t)
        overlay(p,drinking[i],["Head","Neck"])
        # Track the mouth through the source head tilt, rather than using the
        # neck-joint height. Cup rim's rear edge meets this bind-space point.
        mouth_local=rig.data.bones["Head"].matrix_local.inverted() @ Vector((0,-.115,1.633))
        mouth=anim.world_pose(rig,p)["Head"] @ mouth_local
        sip_wrist=mouth-Vector((.032,.016,.0685))
        goal=Vector((-.17,-.34,.806)).lerp(sip_wrist,t)
        track("Drink",hand(rig,p,"Right",goal,(-.39,-.03,.96),direction=(.2,-1,0),grip=True))
        track("Drink",hand(rig,p,"Left",(.18,-.35,.806),(.39,-.04,.89),grip=False))
    record("Drink",["SitChair","Drink"],["Seated pelvis/legs and left hand on table; source drink head motion, right hand tracks the posed mouth with a 67 mm cup, pauses for sip and returns. Cup is a separate game prop."])
    for name in ["Talk2","Listen"]:
        clips[name]=[anim.copy_pose(p) for p in raw[name]]
        record(name,[name],["Different neutral conversation take from Talk; 181..421 selected." if name=="Talk2" else "Source accepting/listening nod; complete 112-frame gesture made into a loop."])
    clips["TrayCarry"]=[anim.copy_pose(p) for p in raw["Walk"]]
    for i,p in enumerate(clips["TrayCarry"]):
        hip=anim.world_pose(rig,p)["Hips"].translation.z-rig.data.bones["Hips"].head_local.z
        track("TrayCarry",hand(rig,p,"Right",(-.385,-.16,1.445+hip),(-.47,.0,1.14),grip=False))
        # The fingers support rather than squeeze the tray.
        for n in arms["Right"]:
            if any(f in n for f in ["Thumb","Index","Middle","Ring","Little"]):p[n][1]=Quaternion((1,0,0),.15)
    record("TrayCarry",["Walk"],["Walk legs and left arm; right elbow bent under a one-handed tray at shoulder height, open supporting fingers. Tray not exported."])
    clips["Sweep"]=[anim.copy_pose(p) for p in raw["Idle"]]
    for i,p in enumerate(clips["Sweep"]):
        theta=i/120*math.tau
        x=.08*math.sin(theta)
        overlay(p,raw["Work"][i],["Head","Neck"])
        p["Spine"][1]=Quaternion((1,0,0),.14)
        for label,goal,pole in [("Left",(.035+x,-.21,1.22),(.41,-.03,1.21)),("Right",(-.055+x,-.33,1.065),(-.38,-.03,1.10))]:
            track("Sweep",hand(rig,p,label,goal,pole,direction=(.1,-.2,-1)))
    record("Sweep",["Idle","Work"],["Idle stance and Work-mid head adapted to two-handed broom grip; periodic side-to-side sweep, wrists along the slanted handle; broom head near floor."])
    clips["Pull"]=resample(raw["Walk"],61)
    for i,p in enumerate(clips["Pull"]):
        theta=i/60*math.tau
        p["Spine"][1]=Quaternion((1,0,0),.20)
        p["Chest"][1]=Quaternion((1,0,0),.08)
        for label,sign in [("Left",1),("Right",-1)]:
            track("Pull",hand(rig,p,label,(sign*.16,-.37,.99+.015*math.sin(theta)),(sign*.39,-.11,1.13),direction=(0,-1,-.1)))
    record("Pull",["Walk"],["Neutral walk slowed from 33 to 61 frames; forward body lean and both hands gripping an imagined rope/cart handle, in place."])
    checks={"max_wrist_error_m":{n:max(v) for n,v in errors.items()},"squat_max_foot_error_m":max(squat_feet),
        "guide_contacts_m":{"standing_ledger_table":1.065,"rail":1.045,"cafe_table":.785,"bag_wrist":.963,"tray_wrist":1.445,"squat_hips":.343}}
    (SCRATCH/"anim_authoring_checks2.json").write_text(json.dumps(checks,indent=2))
    for name,value in checks["max_wrist_error_m"].items():assert value<.025,(name,"unreachable wrist",value)
    assert max(squat_feet)<.005,("squat feet",max(squat_feet))
    for name in CLIPS:
        if name not in ONESHOT:
            anim.cyclic(clips[name],width=4 if name in {"ShoulderCarry","BagWalk","TrayCarry","Pull"} else 12)
            manifest[name]["edits"].append("Blended loop seam, final frame duplicates first exactly.")
        action=anim.bake(rig,name,clips[name]);action.use_fake_user=True
        scene=bpy.context.scene;scene.render.fps=30;scene.frame_start=1;scene.frame_end=len(clips[name]);scene.frame_set(0)
        cast.export_fbx(ANIMS/(name+".fbx"),[rig],animation=True)
        manifest[name].update({"frames":len(clips[name]),"fps":30,"loop":name not in ONESHOT})
        print("CROWD_CLIP_EXPORTED",name,len(clips[name]),flush=True)
    prior=json.loads((ANIMS/"clip_manifest.json").read_text());prior.update(manifest)
    (ANIMS/"clip_manifest.json").write_text(json.dumps(prior,indent=2))
    (ANIMS/"authoring_checks2.json").write_text(json.dumps(checks,indent=2))
    rig.animation_data.action=bpy.data.actions["ShoulderIdle"];scene.frame_set(0)
    bpy.ops.wm.save_as_mainfile(filepath=str(SCRATCH/"crowd_animations.blend"),check_existing=False)
    return manifest


def guide_box(name, position, size, color):
    bpy.ops.mesh.primitive_cube_add(size=1,location=position)
    obj=bpy.context.object;obj.name=name;obj.scale=size
    obj.data.materials.append(crowd.plain(name+"_Guide",color))
    bevel=obj.modifiers.new("Soft prop edges","BEVEL");bevel.width=.015;bevel.segments=3
    obj.modifiers.new("Prop normals","WEIGHTED_NORMAL")
    return obj


def guides(name, rig):
    right=rig.matrix_world@rig.pose.bones["RightHand"].matrix.translation
    left=rig.matrix_world@rig.pose.bones["LeftHand"].matrix.translation
    guide=crowd.plain("GuideWood",(.28,.19,.10))
    if name.startswith("Shoulder"):
        p=Vector((-.27,.015,1.585));p.z+=rig.pose.bones["Hips"].head.z-rig.data.bones["Hips"].head_local.z
        sack=crowd.ellipsoid("Sack_pose_guide",p,(.17,.27,.18),crowd.plain("SackGuide",(.42,.31,.17)),rig,"RightShoulder",seg=24,rows=16)
        # Guide props are static meshes baked in the current pose, never delivered.
        sack.modifiers.clear();sack.parent=None
    if name.startswith("Bag"):
        guide_box("Suitcase_pose_guide",right+Vector((0,0,-.285)),(.31,.18,.40),(.20,.13,.075))
        crowd.tube("CaseHandle_pose_guide",[right+Vector((-.055,0,-.09)),right+Vector((-.055,0,0)),right+Vector((.055,0,0)),right+Vector((.055,0,-.09))],.01,guide,rig,"Hips")
    if name in {"Documents","HandOver"}:
        guide_box("Ledger_pose_guide",(left+right)/2+Vector((0,-.03,-.03)),(.26,.19,.024),(.30,.24,.14))
    if name=="Documents":
        guide_box("Desk_pose_guide",(0,-.46,1.045),(.75,.46,.04),(.28,.21,.13))
        for x in [-.29,.29]:guide_box("DeskLeg_pose_guide",(x,-.50,.52),(.035,.035,1.04),(.23,.17,.10))
        pen=crowd.tube("Pen_pose_guide",[right+Vector((-.015,.02,.025)),right+Vector((.035,-.052,-.024))],.003,guide,rig,"Hips")
        pen.modifiers.clear();pen.parent=None
    if name=="LeanRail":
        guide_box("Rail_pose_guide",(0,-.4,1.045),(.91,.052,.04),(.30,.28,.25))
        for x in [-.38,.38]:guide_box("RailPost_pose_guide",(x,-.4,.58),(.035,.035,.92),(.30,.28,.25))
    if name in {"SitChair","Drink"}:
        guide_box("Table_pose_guide",(0,-.46,.76),(.66,.44,.05),(.32,.24,.15))
        guide_box("TableLeg_pose_guide",(0,-.46,.37),(.055,.055,.74),(.24,.17,.11))
        hips=rig.pose.bones["Hips"].head
        guide_box("ChairSeat_pose_guide",(0,hips.y+.02,hips.z-.09),(.38,.34,.035),(.24,.17,.10))
        guide_box("ChairBack_pose_guide",(0,hips.y+.19,hips.z+.20),(.38,.04,.48),(.24,.17,.10))
    if name=="Drink":
        bpy.ops.mesh.primitive_cylinder_add(vertices=32,radius=.043,depth=.067,location=right+Vector((.032,-.027,.035)))
        cup=bpy.context.object;cup.name="Cup_pose_guide";cup.data.materials.append(crowd.plain("PorcelainGuide",(.78,.75,.66)))
    if name=="TrayCarry":
        guide_box("Tray_pose_guide",right+Vector((0,-.035,.035)),(.39,.28,.026),(.36,.34,.30))
        for dx,dy in [(-.10,0),(.08,.045)]:
            bpy.ops.mesh.primitive_cylinder_add(vertices=24,radius=.031,depth=.07,location=right+Vector((dx,dy,.083)))
            bpy.context.object.data.materials.append(crowd.plain("TrayCupGuide",(.78,.75,.67)))
    if name=="Sweep":
        direction=(right-left).normalized()
        end=right+direction*((right.z-.055)/-direction.z)
        obj=crowd.tube("BroomHandle_pose_guide",[end,left-direction*.20],.011,guide,rig,"Hips")
        obj.modifiers.clear();obj.parent=None
        guide_box("BroomHead_pose_guide",end,(.25,.09,.10),(.50,.39,.20))
    if name=="Pull":
        obj=crowd.tube("Rope_pose_guide",[(left+right)/2+Vector((0,.04,0)),(left+right)/2+Vector((0,-.62,-.08))],.009,guide,rig,"Hips")
        obj.modifiers.clear();obj.parent=None


def preview(pass_number):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    manifest=json.loads((ANIMS/"clip_manifest.json").read_text())
    with bpy.data.libraries.load(str(SCRATCH/"crowd_animations.blend"),link=False) as (src,dst):
        dst.actions=[n for n in src.actions if n in CLIPS]
    for i,name in enumerate(CLIPS):
        before=set(bpy.data.objects)
        rig,meshes=crowd.load_role(SCRATCH/(PREVIEW_ROLES[name]+".blend"))
        rig.data.pose_position="POSE";rig.animation_data_create().action=bpy.data.actions[name]
        frame=1+(manifest[name]["frames"]-1)//2
        if name=="Wave":frame=40
        if name in {"Talk2","Listen"}:
            # Show the strongest conversation/nod pose rather than a rest frame.
            names=["LeftHand","RightHand"] if name=="Talk2" else ["Head"]
            first=None;best=-1
            for candidate in range(1,manifest[name]["frames"]+1):
                bpy.context.scene.frame_set(candidate);bpy.context.view_layer.update()
                values=[rig.pose.bones[n].matrix.copy() for n in names]
                if first is None:first=values
                score=sum((a.translation-b.translation).length+a.to_quaternion().rotation_difference(b.to_quaternion()).angle*.05 for a,b in zip(values,first))
                if score>best:best=score;frame=candidate
        bpy.context.scene.frame_set(frame);bpy.context.view_layer.update()
        guides(name,rig)
        offset=Vector(((i%4-1.5)*1.65,0,(3-i//4)*2.23))
        depsgraph=bpy.context.evaluated_depsgraph_get()
        for obj in list(set(bpy.data.objects)-before):
            if obj.type!="MESH":continue
            data=bpy.data.meshes.new_from_object(obj.evaluated_get(depsgraph))
            copy=bpy.data.objects.new(name+"_"+obj.name,data);bpy.context.collection.objects.link(copy)
            copy.matrix_world=obj.matrix_world.copy();copy.location+=offset
        for obj in set(bpy.data.objects)-before:
            if obj.name.startswith(name+"_"):continue
            bpy.data.objects.remove(obj,do_unlink=True)
        cast.caption(name,offset.x,offset.z-.17,.115)
    cast.caption("16 shared Humanoid clips | 30 fps | Poses + separate prop/contact guides",0,8.84,.105)
    path=SCRATCH/f"anim_preview2_pass{pass_number}.png"
    cast.studio(path,(0,0,4.35),9.4,9.4,(2300,3000),samples=24)
    shutil.copyfile(path,ANIMS/"anim_preview2.png")
    print("CROWD_ANIM_PREVIEW",path,flush=True)


if __name__=="__main__":
    parser=argparse.ArgumentParser();parser.add_argument("--pass",dest="pass_number",type=int,default=1)
    parser.add_argument("--preview-only",action="store_true");parser.add_argument("--no-preview",action="store_true")
    args=parser.parse_args(sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else [])
    if not args.preview_only:generate()
    if not args.no_preview:preview(args.pass_number)
