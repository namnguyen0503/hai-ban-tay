using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using HaiBanTay.Audio;
using HaiBanTay.Content;
using HaiBanTay.Editor.SceneBuilders;
using HaiBanTay.Editor.Setup;
using HaiBanTay.Narrative;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace HaiBanTay.Tests
{
    public class AudioKeyTests
    {
        [Test]
        public void EveryContentVoKeyHasAnImportedClipInAudioLibrary()
        {
            var content = ContentDatabase.LoadFromResources();
            var library = AssetDatabase.LoadAssetAtPath<AudioLibrary>(AssetFactory.AudioLibraryPath);
            Assert.IsNotNull(library, "Phải kiểm thư viện được builder dựng thật.");
            var keys = new HashSet<string>();
            foreach (var line in content.Subtitles.Lines)
            {
                if (string.IsNullOrEmpty(line.Audio)) continue;
                Assert.IsTrue(line.Audio.StartsWith("VO/"), line.Id + ": " + line.Audio);
                Assert.IsTrue(keys.Add(line.Audio), "Khóa VO trùng: " + line.Audio);
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/_Project/Audio/" + line.Audio + ".wav");
                Assert.IsNotNull(clip, line.Id + ": WAV chưa được nhập.");
                Assert.AreSame(clip, library.Get(line.Audio), line.Id + ": AudioLibrary thiếu hoặc trỏ sai clip.");
                Assert.Greater(clip.length, 0f, line.Id + ": clip rỗng.");
            }
            Assert.Greater(keys.Count, 0);
            TestContext.WriteLine($"Đã kiểm {keys.Count}/{content.Subtitles.Lines.Count} câu có VO trong AudioLibrary.");
        }

        [TestCase("00_Prologue", "Ambience/saigon_evening")]
        [TestCase("01_NhaRong", "Ambience/nharong_quay")]
        public void OpeningScenesWireTheCorrectAmbienceClips(string scene, string key)
        {
            EditorSceneManager.OpenScene(BuilderUtils.ScenePath(scene), OpenSceneMode.Single);
            try
            {
                var library = AssetDatabase.LoadAssetAtPath<AudioLibrary>(AssetFactory.AudioLibraryPath);
                Assert.IsNotNull(library);
                Assert.AreSame(AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/_Project/Audio/" + key + ".ogg"), library.Get(key));
                Assert.IsNotNull(library.Get(key), "Clip phải tồn tại trong thư viện thật.");
                if (scene == "00_Prologue")
                {
                    var director = Object.FindAnyObjectByType<PrologueDirector>();
                    Assert.IsNotNull(director.ambience);
                    Assert.AreEqual(key, director.ambience.clipKey);
                    Assert.IsNotNull(director.ambience.area);
                    Assert.IsTrue(director.ambience.GetComponent<Collider>().isTrigger);
                }
                else
                {
                    var director = Object.FindAnyObjectByType<NhaRongDirector>();
                    Assert.IsNotNull(director.ambience);
                    Assert.AreEqual(key, director.ambience.quayKey);
                    Assert.AreSame(director.aboard, director.ambience.aboard);
                    Assert.IsFalse(director.ambience.departed);
                    foreach (var clip in new[] { "Ambience/harbor_water", "Ambience/sea_deck", "Ambience/engine" })
                        Assert.IsNotNull(library.Get(clip), clip);
                    Assert.IsNotNull(director.engine);
                    Assert.IsFalse(director.engine.playOnStart, "Máy chỉ bắt đầu sau nhổ neo.");
                    Assert.AreEqual(0.2f, director.engine.volume);
                    var layout = Object.FindAnyObjectByType<HaiBanTay.Ship.ShipLayout>();
                    Assert.IsTrue(layout.galleyDoor.audioEnabled);
                    Assert.IsTrue(layout.corridorDoor.audioEnabled);
                    var cart = Object.FindAnyObjectByType<MovingCartAudio>();
                    Assert.IsNotNull(cart);
                    Assert.AreEqual("PotatoCart", cart.name);
                    Assert.AreSame(director.backdrop.transform, cart.motionFrame);
                }
            }
            finally
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
        }

        [Test]
        public void EveryRuntimeAndSceneBuilderAudioKeyHasAFile()
        {
            string project = Path.Combine(Application.dataPath, "_Project");
            var available = new HashSet<string>();
            string audio = Path.Combine(project, "Audio");
            foreach (var file in Directory.GetFiles(audio, "*", SearchOption.AllDirectories))
            {
                string ext = Path.GetExtension(file).ToLowerInvariant();
                if (ext == ".wav" || ext == ".ogg" || ext == ".mp3" || ext == ".aiff")
                    available.Add(Path.ChangeExtension(file.Substring(audio.Length + 1), null).Replace('\\', '/'));
            }
            var literals = new Regex("\"((?:SFX|Ambience|Music)/[^\"]+)\"");
            var missing = new List<string>();
            int checkedKeys = 0;
            foreach (var folder in new[] { "Scripts/Runtime", "Scripts/Editor/SceneBuilders" })
                foreach (var file in Directory.GetFiles(Path.Combine(project, folder), "*.cs", SearchOption.AllDirectories))
                    foreach (Match match in literals.Matches(File.ReadAllText(file)))
                    {
                        checkedKeys++;
                        if (!available.Contains(match.Groups[1].Value))
                            missing.Add(file.Substring(project.Length + 1) + ": " + match.Groups[1].Value);
                    }
            Assert.Greater(checkedKeys, 0, "Phải quét được khóa âm thanh.");
            Assert.IsEmpty(missing, string.Join("\n", missing));
            TestContext.WriteLine($"Đã kiểm tra {checkedKeys} chuỗi khóa; {available.Count} file âm thanh.");
        }
    }
}
