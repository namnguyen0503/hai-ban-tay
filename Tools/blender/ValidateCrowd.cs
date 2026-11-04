// Import/playback contract for the isolated crowd Unity project only.
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEditor;

public static class ValidateCrowd
{
    [Serializable] public class ModelContract { public string role; public int[] triangles, zeroTriangles; }
    [Serializable] public class ClipContract { public string name; public int frames; public bool loop; }
    [Serializable] public class Contract { public ModelContract[] models; public ClipContract[] clips; }
    [Serializable] public class ModelEvidence
    {
        public string role; public int lod, triangles, removedDegenerates, bones, renderers;
        public bool valid, humanoid, commonMapping, rebindToLOD0;
        public float height;
    }
    [Serializable] public class ClipEvidence { public string name; public bool humanoid, loop; public int frames; public float fps; }
    [Serializable] public class Evidence
    {
        public string unityVersion; public ModelEvidence[] models; public ClipEvidence[] clips;
        public int sharedAvatarPlaybackSamples, lodGroups, commonSkeletons;
        public bool passed;
    }
    static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    static string ModelPath(string role, int lod) => "Assets/Cast/"+role+"/"+role+(lod==0?"":"_LOD"+lod)+".fbx";
    static bool Finite(Vector3 v) => !float.IsNaN(v.x)&&!float.IsNaN(v.y)&&!float.IsNaN(v.z)
        &&!float.IsInfinity(v.x)&&!float.IsInfinity(v.y)&&!float.IsInfinity(v.z);

    public static void Run()
    {
        try
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var contract=JsonUtility.FromJson<Contract>(File.ReadAllText("Assets/Editor/validation_contract.json"));
            Require(contract.models.Length==20&&contract.clips.Length==31,"Wrong delivery contract counts");
            var models=new List<ModelEvidence>(); var clips=new List<ClipEvidence>();
            var playback=new List<AnimationClip>(); Avatar shared=null; string[] mapping=null;
            foreach(var model in contract.models)
            for(int lod=0;lod<3;lod++)
            {
                var path=ModelPath(model.role,lod); var importer=(ModelImporter)AssetImporter.GetAtPath(path);
                Require(importer!=null,"Missing model "+path);
                importer.animationType=ModelImporterAnimationType.Human;
                importer.avatarSetup=ModelImporterAvatarSetup.CreateFromThisModel;
                importer.importAnimation=false; importer.isReadable=true; importer.preserveHierarchy=true;
                importer.optimizeGameObjects=false; importer.SaveAndReimport();
                var avatar=AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().Single();
                Require(avatar.isValid&&avatar.isHuman,"Invalid auto Humanoid "+path);
                if(shared==null)shared=avatar;
                var human=importer.humanDescription.human.OrderBy(h=>h.humanName).Select(h=>h.humanName+"="+h.boneName).ToArray();
                if(mapping==null)mapping=human;
                Require(mapping.SequenceEqual(human),"Different Humanoid mapping "+path);
                var instance=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path));
                var skins=instance.GetComponentsInChildren<SkinnedMeshRenderer>();Require(skins.Length>0,"Missing skins "+path);
                if(lod>0)Require(skins.Length==1,"LOD should have one joined renderer "+path);
                var bounds=skins[0].bounds;foreach(var skin in skins)bounds.Encapsulate(skin.bounds);
                Require(bounds.size.y>1.7f&&bounds.size.y<2.2f,"Wrong meter/Y-up scale "+path+" "+bounds.size.y);
                int triangles=skins.Sum(s=>s.sharedMesh.triangles.Length/3);
                int dropped=model.triangles[lod]-triangles;
                Require(dropped>=0&&dropped<=model.zeroTriangles[lod],"Unexpected triangles "+path+" expected="+model.triangles[lod]+" actual="+triangles+" knownDegenerates="+model.zeroTriangles[lod]);
                int bones=instance.GetComponentsInChildren<Transform>().Count(t=>mapping.Any(m=>m.EndsWith("="+t.name)));
                Require(bones==52,"Lost skeleton bones "+path+" "+bones);
                models.Add(new ModelEvidence{role=model.role,lod=lod,triangles=triangles,removedDegenerates=dropped,bones=bones,
                    renderers=skins.Length,height=bounds.size.y,valid=true,humanoid=true,commonMapping=true});
                UnityEngine.Object.DestroyImmediate(instance);Debug.Log("CROWD_AUTO_AVATAR_OK "+path);
            }
            foreach(var entry in contract.clips)
            {
                var path="Assets/CharacterAnims/"+entry.name+".fbx";var importer=(ModelImporter)AssetImporter.GetAtPath(path);
                Require(importer!=null,"Missing clip "+path);
                importer.animationType=ModelImporterAnimationType.Human;importer.avatarSetup=ModelImporterAvatarSetup.CopyFromOther;
                importer.sourceAvatar=shared;importer.importAnimation=true;importer.preserveHierarchy=true;
                importer.animationCompression=ModelImporterAnimationCompression.Off;importer.SaveAndReimport();
                var settings=importer.defaultClipAnimations;Require(settings.Length==1,"Extra takes "+entry.name);
                settings[0].name=entry.name;settings[0].loopTime=entry.loop;importer.clipAnimations=settings;importer.SaveAndReimport();
                var imported=AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Where(c=>!c.name.StartsWith("__preview__")).ToArray();
                Require(imported.Length==1&&imported[0].humanMotion,"Invalid copied-avatar clip "+entry.name);
                var clip=imported[0];int frames=Mathf.RoundToInt(clip.length*30)+1;
                Require(frames==entry.frames&&Mathf.Abs(clip.frameRate-30)<.01f,"Wrong clip frames/fps "+entry.name+" "+frames);
                Require(clip.isLooping==entry.loop,"Wrong imported loop flag "+entry.name);
                clips.Add(new ClipEvidence{name=entry.name,frames=frames,fps=clip.frameRate,loop=entry.loop,humanoid=true});playback.Add(clip);
                Debug.Log("CROWD_HUMANOID_CLIP_OK "+entry.name);
            }
            int samples=0,groups=0;
            var walkers=new HashSet<string>{"Walk","CarryWalk","ShoulderCarry","BagWalk","TrayCarry","Pull"};
            foreach(var model in contract.models)
            {
                var root=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath(model.role,0)));
                var animator=root.GetComponent<Animator>();animator.avatar=shared;animator.applyRootMotion=false;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
                var common=root.GetComponentsInChildren<Transform>().ToDictionary(t=>t.name);
                var renderers=new List<SkinnedMeshRenderer[]> {root.GetComponentsInChildren<SkinnedMeshRenderer>()};
                var containers=new List<GameObject>();
                for(int lod=1;lod<3;lod++)
                {
                    var other=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath(model.role,lod)));
                    var skin=other.GetComponentsInChildren<SkinnedMeshRenderer>().Single();
                    var container=new GameObject("LOD"+lod);container.transform.SetParent(root.transform,false);containers.Add(container);
                    var target=container.AddComponent<SkinnedMeshRenderer>();target.sharedMesh=skin.sharedMesh;target.sharedMaterials=skin.sharedMaterials;
                    target.bones=skin.bones.Select(b=>common[b.name]).ToArray();target.rootBone=common[skin.rootBone.name];target.localBounds=skin.localBounds;
                    target.updateWhenOffscreen=true;
                    Require(target.bones.All(b=>common.Values.Contains(b)),"LOD bone rebind failed "+model.role);
                    renderers.Add(new[]{target});UnityEngine.Object.DestroyImmediate(other);
                }
                // Standalone _LOD FBXs can acquire partial importer LODGroups.
                foreach(var inherited in root.GetComponentsInChildren<LODGroup>())UnityEngine.Object.DestroyImmediate(inherited);
                var group=root.AddComponent<LODGroup>();group.SetLODs(new[]{new LOD(.50f,renderers[0]),new LOD(.20f,renderers[1]),new LOD(.04f,renderers[2])});
                group.RecalculateBounds();group.ForceLOD(0);groups++;
                Require(root.GetComponentsInChildren<LODGroup>().Length==1&&group.GetLODs().Length==3,"Incorrect common LODGroup "+model.role);
                Require(root.GetComponentsInChildren<Animator>().Length==1,"Extra Animator after LOD assembly "+model.role);
                foreach(var entry in models.Where(m=>m.role==model.role))entry.rebindToLOD0=true;
                foreach(var clip in playback)
                {
                    animator.Rebind();var graph=PlayableGraph.Create("CrowdValidation");graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                    var playable=AnimationClipPlayable.Create(graph,clip);AnimationPlayableOutput.Create(graph,"Humanoid",animator).SetSourcePlayable(playable);graph.Play();
                    var mesh=new Mesh();Vector3 firstFoot=Vector3.zero;float movement=0;
                    for(int i=0;i<9;i++)
                    {
                        playable.SetTime(clip.length*i/8.0);graph.Evaluate(0);
                        var foot=animator.GetBoneTransform(HumanBodyBones.LeftFoot).position;
                        if(i==0)firstFoot=foot;movement=Mathf.Max(movement,Vector3.Distance(firstFoot,foot));
                        for(int lod=0;lod<3;lod++)
                        {
                            foreach(var skin in renderers[lod])
                            {
                                skin.BakeMesh(mesh);foreach(var vertex in mesh.vertices)Require(Finite(vertex),"Nonfinite skin "+model.role+"/LOD"+lod+"/"+clip.name);
                                Require(mesh.bounds.size.magnitude<4.1f,"Exploded skin "+model.role+"/LOD"+lod+"/"+clip.name);
                            }
                            samples++;
                        }
                    }
                    if(walkers.Contains(clip.name))Require(movement>.08f,"Walk legs static "+model.role+"/"+clip.name+" "+movement);
                    UnityEngine.Object.DestroyImmediate(mesh);graph.Destroy();
                }
                UnityEngine.Object.DestroyImmediate(root);Debug.Log("CROWD_SHARED_AVATAR_LOD_PLAYBACK_OK "+model.role);
            }
            var evidence=new Evidence{unityVersion=Application.unityVersion,models=models.ToArray(),clips=clips.ToArray(),
                sharedAvatarPlaybackSamples=samples,lodGroups=groups,commonSkeletons=groups,passed=true};
            var output=Path.GetFullPath(Path.Combine(Application.dataPath,"../../unity_validation.json"));
            File.WriteAllText(output,JsonUtility.ToJson(evidence,true));Debug.Log("CROWD_UNITY_VERIFIED "+output);EditorApplication.Exit(0);
        }
        catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}
    }
}
