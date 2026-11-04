// Isolated Unity 6 import check; copied into _scratch/cast/unity_validation/Assets/Editor.
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEditor;

public static class ValidateCast
{
    [Serializable] public class ModelEvidence
    {
        public string role;
        public bool valid, humanoid;
        public int boneCount, triangles;
        public float height;
        public string[] mapping;
    }
    [Serializable] public class ClipEvidence
    {
        public string name;
        public bool humanoid, loop;
        public float fps, seconds;
        public int frames;
    }
    [Serializable] public class Evidence
    {
        public string unityVersion;
        public ModelEvidence[] models;
        public ClipEvidence[] clips;
        public int sharedAvatarPlaybackSamples;
        public bool passed;
    }

    static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    public static void Run()
    {
        try
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var models = new List<ModelEvidence>();
            var roleNames = new[] {"Cook", "Helper", "SoldierA", "SoldierB", "Jacques"};
            Avatar shared = null;
            string[] mapping = null;
            foreach (var role in roleNames)
            {
                var path = "Assets/Cast/" + role + "/" + role + ".fbx";
                var importer = (ModelImporter)AssetImporter.GetAtPath(path);
                Require(importer != null, "Missing model " + path);
                importer.animationType = ModelImporterAnimationType.Human;
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                importer.importAnimation = false;
                importer.isReadable = true;
                importer.preserveHierarchy = true;
                importer.SaveAndReimport();
                var avatar = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().Single();
                Require(avatar.isValid && avatar.isHuman, "Auto Avatar invalid: " + role);
                if (shared == null) shared = avatar;
                var human = importer.humanDescription.human.OrderBy(h => h.humanName)
                    .Select(h => h.humanName + "=" + h.boneName).ToArray();
                if (mapping == null) mapping = human;
                Require(mapping.SequenceEqual(human), "Avatar mapping differs: " + role);
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path));
                var animator = instance.GetComponent<Animator>();
                Require(animator && animator.avatar.isValid && animator.avatar.isHuman, "Animator invalid: " + role);
                var skins = instance.GetComponentsInChildren<SkinnedMeshRenderer>();
                Require(skins.Length > 0, "No skin: " + role);
                var bounds = skins[0].bounds;
                foreach (var skin in skins) bounds.Encapsulate(skin.bounds);
                Require(bounds.size.y > 1.7f && bounds.size.y < 2.1f, "Wrong meter scale/Y-up: " + role + " height=" + bounds.size.y);
                Require(skins.SelectMany(s => s.bones).Distinct().Count() <= 52, "Extra skin bones: " + role);
                models.Add(new ModelEvidence {role=role, valid=avatar.isValid, humanoid=avatar.isHuman,
                    boneCount=instance.GetComponentsInChildren<Transform>().Count(t => mapping.Any(m => m.EndsWith("="+t.name))),
                    triangles=skins.Sum(s => s.sharedMesh.triangles.Length/3), height=bounds.size.y, mapping=human});
                UnityEngine.Object.DestroyImmediate(instance);
                Debug.Log("CAST_AVATAR_OK " + role + " automatic mapping=" + human.Length);
            }
            var clips = new List<ClipEvidence>();
            var playback = new List<AnimationClip>();
            var oneShots = new HashSet<string> {"PickUp", "Point", "SitDown", "StandUp"};
            foreach (var path in Directory.GetFiles("Assets/CharacterAnims", "*.fbx").OrderBy(p => p))
            {
                var name = Path.GetFileNameWithoutExtension(path);
                var importer = (ModelImporter)AssetImporter.GetAtPath(path);
                importer.animationType = ModelImporterAnimationType.Human;
                importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
                importer.sourceAvatar = shared;
                importer.importAnimation = true;
                importer.preserveHierarchy = true;
                importer.animationCompression = ModelImporterAnimationCompression.Off;
                importer.SaveAndReimport();
                var settings = importer.defaultClipAnimations;
                Require(settings.Length == 1, "Expected one take: " + name + " got=" + settings.Length);
                settings[0].name = name;
                settings[0].loopTime = !oneShots.Contains(name);
                importer.clipAnimations = settings;
                importer.SaveAndReimport();
                var imported = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                    .Where(c => !c.name.StartsWith("__preview__")).ToArray();
                Require(imported.Length == 1 && imported[0].humanMotion, "Humanoid clip invalid: " + name);
                var clip = imported[0];
                playback.Add(clip);
                Require(Mathf.Abs(clip.frameRate-30) < .01f && clip.length > 0, "Clip timing invalid: " + name);
                clips.Add(new ClipEvidence {name=name, humanoid=clip.humanMotion, loop=settings[0].loopTime,
                    fps=clip.frameRate, seconds=clip.length, frames=Mathf.RoundToInt(clip.length*30)+1});
                Debug.Log("CAST_CLIP_OK " + name + " frames=" + (Mathf.RoundToInt(clip.length*30)+1));
            }
            Require(clips.Count == 15, "Expected 15 clips, got=" + clips.Count);
            int samples = 0;
            foreach (var role in roleNames)
            {
                var path = "Assets/Cast/" + role + "/" + role + ".fbx";
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path));
                var animator = instance.GetComponent<Animator>();
                animator.avatar = shared;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                foreach (var clip in playback)
                {
                    animator.Rebind();
                    var graph = PlayableGraph.Create("CastVerification");
                    graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                    var playable = AnimationClipPlayable.Create(graph, clip);
                    AnimationPlayableOutput.Create(graph, "Humanoid", animator).SetSourcePlayable(playable);
                    graph.Play();
                    Vector3 firstFoot = Vector3.zero;
                    float footMovement = 0;
                    var baked = new Mesh();
                    for (int i=0; i<9; i++)
                    {
                        playable.SetTime(clip.length*i/8.0);
                        graph.Evaluate(0);
                        var foot = animator.GetBoneTransform(HumanBodyBones.LeftFoot).position;
                        if (i==0) firstFoot = foot;
                        footMovement = Mathf.Max(footMovement, Vector3.Distance(firstFoot, foot));
                        foreach (var skin in instance.GetComponentsInChildren<SkinnedMeshRenderer>())
                        {
                            skin.BakeMesh(baked);
                            foreach (var vertex in baked.vertices)
                                Require(!float.IsNaN(vertex.x) && !float.IsNaN(vertex.y) && !float.IsNaN(vertex.z)
                                    && !float.IsInfinity(vertex.x) && !float.IsInfinity(vertex.y) && !float.IsInfinity(vertex.z),
                                    "Nonfinite skin: " + role + "/" + clip.name);
                            Require(baked.bounds.size.magnitude < 4, "Exploded skin: " + role + "/" + clip.name);
                        }
                        samples++;
                    }
                    if (clip.name == "Walk" || clip.name == "CarryWalk")
                        Require(footMovement > .08f, "Walk legs did not animate: " + role + "/" + clip.name);
                    UnityEngine.Object.DestroyImmediate(baked);
                    graph.Destroy();
                }
                UnityEngine.Object.DestroyImmediate(instance);
                Debug.Log("CAST_SHARED_AVATAR_PLAYBACK_OK " + role);
            }
            var evidence = new Evidence {unityVersion=Application.unityVersion, models=models.ToArray(), clips=clips.ToArray(),
                sharedAvatarPlaybackSamples=samples, passed=true};
            var output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../unity_validation.json"));
            File.WriteAllText(output, JsonUtility.ToJson(evidence,true));
            Debug.Log("CAST_UNITY_VERIFIED " + output);
            EditorApplication.Exit(0);
        }
        catch (Exception error)
        {
            Debug.LogException(error);
            EditorApplication.Exit(1);
        }
    }
}
