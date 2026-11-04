using System.Collections;
using HaiBanTay.Audio;
using HaiBanTay.Core;
using HaiBanTay.Narrative;
using HaiBanTay.Player;
using HaiBanTay.Ship;
using HaiBanTay.Shift;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace HaiBanTay.Tests
{
    public class AudioGameplayTests
    {
        [TearDown]
        public void TearDown()
        {
            PlayTestUtil.ExitSim();
            SceneFlow.FromPrologue = false;
            GameRoot.Instance.Audio.SetMusicDuck(false);
            GameRoot.Instance.Audio.StopMusic(0f);
            GameSettings.ResetForTests();
        }

        [UnityTest]
        public IEnumerator NhaRongCrossfadesOnBoardingAndDepartureStartsTheQuietEngine()
        {
            var root = PlayTestUtil.Root();
            root.ResetProgress();
            root.Events.Raise(EventIds.S1JobAccepted);
            SceneFlow.FromPrologue = true;
            yield return PlayTestUtil.LoadAndSettle(SceneFlow.GameScenes[0]);
            var director = Object.FindAnyObjectByType<NhaRongDirector>();
            yield return PlayTestUtil.WaitUntil(() => director.Beat == "B1.3" && root.Input.GameplayEnabled, 30f);
            Assert.AreEqual("B1.3", director.Beat);
            var ambience = director.ambience;
            var quayPosition = PlayerRig.Current.Position;
            var cart = Object.FindAnyObjectByType<MovingCartAudio>();
            yield return new WaitForSecondsRealtime(2f);
            var sources = ambience.GetComponents<AudioSource>();
            var quay = System.Array.Find(sources, s => s.clip == root.Audio.Clip("Ambience/nharong_quay"));
            var harbor = System.Array.Find(sources, s => s.clip == root.Audio.Clip("Ambience/harbor_water"));
            var deck = System.Array.Find(sources, s => s.clip == root.Audio.Clip("Ambience/sea_deck"));
            Assert.IsNotNull(quay);
            Assert.IsNotNull(harbor);
            Assert.IsNotNull(deck);
            Assert.AreEqual("Ambience/nharong_quay", ambience.CurrentKey);
            Assert.Greater(quay.volume, 0f);
            Assert.Greater(harbor.volume, 0f);
            Assert.AreEqual(0f, deck.volume, 0.001f);
            Assert.IsFalse(director.engine.Playing);
            Assert.AreEqual(0f, cart.GetComponent<AudioSource>().volume, 0.001f);

            // Chỉ xe tự dịch chuyển mới có tiếng bánh; dừng xe thì nguồn trở về 0.
            cart.transform.position += Vector3.right * 0.1f;
            yield return null;
            Assert.Greater(cart.GetComponent<AudioSource>().volume, 0f);
            yield return null;
            Assert.AreEqual(0f, cart.GetComponent<AudioSource>().volume, 0.001f);

            PlayerRig.Current.motor.Teleport(director.boardPoint.position, 0f);
            yield return new WaitForSecondsRealtime(2f);
            Assert.IsTrue(director.aboard.PlayerInside);
            Assert.AreEqual("Ambience/sea_deck", ambience.CurrentKey);
            Assert.Greater(deck.volume, 0f);
            Assert.AreEqual(0f, quay.volume, 0.001f);
            Assert.AreEqual(0f, harbor.volume, 0.001f);
            Assert.IsFalse(director.engine.Playing, "Lên tàu trước nhổ neo chưa bật máy.");

            PlayerRig.Current.motor.Teleport(quayPosition, 0f);
            yield return new WaitForSecondsRealtime(2f);
            Assert.AreEqual("Ambience/nharong_quay", ambience.CurrentKey);
            Assert.Greater(quay.volume, 0f);
            Assert.AreEqual(0f, deck.volume, 0.001f);

            PlayerRig.Current.motor.Teleport(director.boardPoint.position, 0f);
            yield return new WaitForSecondsRealtime(0.3f);
            director.notebookPromptSeconds = 0f;
            director.sackSeconds = 0f;
            director.lingerMaxSeconds = 120f;
            yield return PlayTestUtil.WaitUntil(() => director.Departed, 10f);
            Assert.IsTrue(director.Departed);
            Assert.IsTrue(director.engine.Playing);
            Assert.IsTrue(ambience.departed);
            yield return new WaitForSecondsRealtime(2f);
            Assert.AreEqual("Ambience/sea_deck", ambience.CurrentKey);
            Assert.Greater(deck.volume, 0f);
            Assert.AreEqual(0f, quay.volume, 0.001f);
            Assert.AreEqual(0f, harbor.volume, 0.001f);
            Assert.AreEqual(0f, cart.GetComponent<AudioSource>().volume, 0.001f, "Phông bờ lùi không làm xe đỗ kêu.");
            Assert.IsTrue(director.backdrop.Moving);
            var engine = director.engine.GetComponent<AudioSource>();
            Assert.AreSame(root.Audio.Clip("Ambience/engine"), engine.clip);
            Assert.AreEqual(0.2f * root.Audio.Volume(AudioChannel.Ambience), engine.volume, 0.001f);
            var music = root.Audio.transform.Find("Audio_Music").GetComponent<AudioSource>();
            Assert.AreSame(root.Audio.Clip("Music/voyage"), music.clip);
            Assert.Greater(music.volume, 0f);
            Assert.LessOrEqual(music.volume, 0.25f * root.Audio.Volume(AudioChannel.Music));
            var settings = GameSettings.Current.Clone();
            settings.AmbienceVolume = 0f;
            GameSettings.Apply(settings);
            yield return null;
            foreach (var source in sources)
                Assert.AreEqual(0f, source.volume, 0.001f);
            Assert.AreEqual(0f, engine.volume, 0.001f);
        }

        [UnityTest]
        public IEnumerator PrologueStreetAmbienceUsesTheRealClipAndFadesOut()
        {
            var root = PlayTestUtil.Root();
            yield return PlayTestUtil.LoadAndSettle(SceneFlow.PrologueScene);
            var ambience = Object.FindAnyObjectByType<PrologueDirector>().ambience;
            yield return new WaitForSecondsRealtime(2f);
            var source = ambience.GetComponent<AudioSource>();
            Assert.IsNotNull(source);
            Assert.AreSame(root.Audio.Clip("Ambience/saigon_evening"), source.clip);
            Assert.IsTrue(source.loop);
            Assert.Greater(source.volume, 0f);
            var music = root.Audio.transform.Find("Audio_Music").GetComponent<AudioSource>();
            Assert.AreSame(root.Audio.Clip("Music/menu"), music.clip);
            Assert.LessOrEqual(music.volume, 0.2f * root.Audio.Volume(AudioChannel.Music));
            var settings = GameSettings.Current.Clone();
            settings.AmbienceVolume = 0f;
            GameSettings.Apply(settings);
            yield return null;
            Assert.AreEqual(0f, source.volume, 0.001f);
            settings.AmbienceVolume = 1f;
            GameSettings.Apply(settings);
            yield return null;
            Assert.Greater(source.volume, 0f);
            ambience.FadeOut();
            yield return new WaitForSecondsRealtime(2f);
            Assert.AreEqual(0f, source.volume, 0.001f);
            yield return new WaitForSecondsRealtime(0.3f);
            Assert.AreEqual(0f, source.volume, 0.001f, "Còn trong phố cũng không bật lại sau fade cuối P4.");
        }

        /// <summary>
        /// DIRECTOR §6/§9: tối ngày 3 tàu đậu ngoài Colombo, đèn bến xa và tiếng hò khuân vác vọng qua mặt nước. Lớp hò chỉ lên khi đèn bến
        /// bật và Ba ở ngoài boong; vào bếp thì tắt dần.
        /// </summary>
        [UnityTest]
        public IEnumerator ColomboDockersCarryOverTheWaterOnlyOnDeckWhileTheShoreLightsAreOn()
        {
            var root = PlayTestUtil.Root();
            root.ResetProgress();
            root.State.Day = 3;
            yield return PlayTestUtil.LoadAndSettle(SceneFlow.GameScenes[1]);
            var layout = Object.FindAnyObjectByType<ShipLayout>();
            var ambience = Object.FindAnyObjectByType<ShipAmbience>();
            var director = Object.FindAnyObjectByType<ShiftDirector>();
            Assert.IsNotNull(root.Audio.Clip("Ambience/colombo_dockers"), "AudioLibrary phải có Ambience/colombo_dockers");
            Assert.IsNotNull(director.colomboLights);
            Assert.AreSame(director.colomboLights, ambience.farShore, "lớp hò gắn với đèn bến Colombo");
            var chant = System.Array.Find(ambience.GetComponents<AudioSource>(), s => s.clip != null && s.clip.name == "colombo_dockers");
            Assert.IsNotNull(chant);
            PlayerRig.Current.motor.Teleport(layout.transform.TransformPoint(new Vector3(14f, 5.6f, -4.5f)), 0f);
            director.colomboLights.SetActive(false);
            yield return new WaitForSeconds(2f);
            Assert.AreEqual(0f, ambience.FarShoreLevel, 0.001f, "ban ngày không có hò bến xa");
            director.colomboLights.SetActive(true);
            yield return new WaitForSeconds(4f);
            Assert.AreEqual("Ambience/sea_deck", ambience.CurrentKey);
            Assert.Greater(ambience.FarShoreLevel, 0.2f, "trên boong nghe hò khuân vác");
            Assert.Greater(chant.volume, 0f);
            Assert.IsTrue(chant.isPlaying);
            PlayerRig.Current.motor.Teleport(director.galleyBeacon.position, 0f);
            yield return new WaitForSeconds(4f);
            Assert.AreEqual(0f, ambience.FarShoreLevel, 0.001f, "trong bếp không nghe hò bến xa");
            director.colomboLights.SetActive(false);
        }

        [UnityTest]
        public IEnumerator SingaporeDeckLayersHarborAndHoldCrossfades()
        {
            var root = PlayTestUtil.Root();
            root.ResetProgress();
            root.State.Day = 2;
            yield return PlayTestUtil.LoadAndSettle(SceneFlow.GameScenes[1]);
            var layout = Object.FindAnyObjectByType<ShipLayout>();
            var ambience = Object.FindAnyObjectByType<ShipAmbience>();
            Assert.IsTrue(ambience.scenery.Docked);
            PlayerRig.Current.motor.Teleport(layout.transform.TransformPoint(new Vector3(14f, 5.6f, -4.5f)), 0f);
            yield return new WaitForSeconds(2f);
            Assert.AreEqual("Ambience/sea_deck", ambience.CurrentKey);
            var sources = ambience.GetComponents<AudioSource>();
            var harbor = System.Array.Find(sources, s => s.clip.name == "harbor_water");
            Assert.IsNotNull(harbor);
            Assert.Greater(harbor.volume, 0f);
            PlayerRig.Current.motor.Teleport(layout.transform.TransformPoint(new Vector3(12.3f, 2.8f, 1.2f)), 0f);
            yield return new WaitForSeconds(2f);
            Assert.AreEqual("Ambience/hold_cold", ambience.CurrentKey);
            Assert.AreEqual(0f, harbor.volume, 0.001f);
            var hold = System.Array.Find(sources, s => s.clip.name == "hold_cold");
            Assert.IsNotNull(hold);
            Assert.Greater(hold.volume, 0f);
        }

        [UnityTest]
        public IEnumerator MarseilleQuayAndStreetUseDifferentLayersAndCartIsSilentAtRest()
        {
            PlayTestUtil.Root().ResetProgress();
            yield return PlayTestUtil.LoadAndSettle(SceneFlow.GameScenes[2]);
            var ambience = Object.FindAnyObjectByType<ShipAmbience>();
            var cart = Object.FindAnyObjectByType<MovingCartAudio>();
            Assert.IsNotNull(cart, "PortCart phải có hook bánh xe.");
            PlayerRig.Current.motor.Teleport(new Vector3(-10f, -2.5f, -13f), 0f);
            yield return new WaitForSeconds(2f);
            Assert.AreEqual("Ambience/harbor_water", ambience.CurrentKey);
            Assert.AreEqual(0f, cart.GetComponent<AudioSource>().volume, 0.001f);
            PlayerRig.Current.motor.Teleport(new Vector3(-10f, -2.4f, -25f), 0f);
            yield return new WaitForSeconds(2f);
            Assert.AreEqual("Ambience/marseille_street", ambience.CurrentKey);
            var street = System.Array.Find(ambience.GetComponents<AudioSource>(), s => s.clip.name == "marseille_street");
            Assert.IsNotNull(street);
            Assert.Greater(street.volume, 0f);
        }

        [UnityTest]
        public IEnumerator VoTracksSubtitleSettingsAndStopsWithTheSubtitleQueue()
        {
            var root = PlayTestUtil.Root();
            yield return PlayTestUtil.LoadAndSettle(SceneFlow.MenuScene);
            root.Subtitles.StopAll();
            root.Subtitles.Play("chef_depart");
            yield return null;
            var vo = root.Audio.transform.Find("Audio_VO").GetComponent<AudioSource>();
            Assert.AreEqual("chef_depart", root.Subtitles.CurrentLineId);
            Assert.AreSame(root.Audio.Clip("VO/chef_depart"), vo.clip);
            var settings = GameSettings.Current.Clone();
            settings.VoVolume = 0f;
            GameSettings.Apply(settings);
            Assert.AreEqual(0f, vo.volume, "Tắt tiếng phải áp dụng ngay cho câu đang phát.");
            root.Subtitles.StopAll();
            Assert.IsNull(root.Subtitles.CurrentLineId);
            Assert.IsNull(vo.clip, "Dừng phụ đề phải gỡ clip VO đang phát.");
            Assert.IsFalse(vo.isPlaying);
            // Từ 15/10 mọi câu đều có VO riêng (Tools/audit_vo.py chặn câu thiếu audio), không còn câu chỉ phụ đề để thử ở đây.
        }

        [UnityTest]
        public IEnumerator NewMusicCancelsOldFadeAndDuckRespectsSettings()
        {
            var root = PlayTestUtil.Root();
            yield return PlayTestUtil.LoadAndSettle(SceneFlow.MenuScene);
            root.Subtitles.StopAll();
            var audio = root.Audio;
            audio.PlayMusic("Music/voyage", true);
            audio.StopMusic(0.2f);
            audio.PlayMusic("Music/ending", true, 0.75f);
            var music = audio.transform.Find("Audio_Music").GetComponent<AudioSource>();
            yield return new WaitForSecondsRealtime(0.3f);
            Assert.AreEqual("ending", music.clip.name);
            Assert.Greater(music.volume, 0f, "Fade cũ không được tắt nhạc mới.");
            float normal = music.volume;
            audio.SetMusicDuck(true);
            yield return new WaitForSecondsRealtime(0.3f);
            Assert.AreEqual(normal * 0.65f, music.volume, 0.001f);
            var settings = GameSettings.Current.Clone();
            settings.MusicVolume = 0f;
            GameSettings.Apply(settings);
            yield return null;
            Assert.AreEqual(0f, music.volume, 0.001f);
        }
    }
}
