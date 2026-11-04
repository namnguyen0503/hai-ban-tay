using System.Collections;
using System.IO;
using HaiBanTay.Core;
using HaiBanTay.Narrative;
using HaiBanTay.Player;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace HaiBanTay.Tests
{
    /// <summary>Chụp nhanh thân/tay từ camera người chơi và camera ngoài; chỉ để soát hình, chạy qua Tools/screenshots.sh BodyQuickShots.</summary>
    [Category("Screenshots"), Explicit]
    public class BodyQuickShots
    {
        [TearDown] public void TearDown() => PlayTestUtil.ExitSim();

        static IEnumerator Save(Camera cam, string name)
        {
            yield return null;
            var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var req = new RenderPipeline.StandardRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(cam, req)) RenderPipeline.SubmitRenderRequest(cam, req);
            else { cam.targetTexture = rt; cam.Render(); cam.targetTexture = null; }
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(Path.Combine(ScreenshotCapture.Folder, "q_" + name + ".png"), tex.EncodeToPNG());
            Object.Destroy(tex);
            rt.Release();
        }

        [UnityTest]
        public IEnumerator Quick()
        {
            PlayTestUtil.Root().ResetProgress();
            yield return PlayTestUtil.LoadAndSettle(SceneFlow.GameScenes[1]);
            SceneDirector.Current.StopAllCoroutines();
            var rig = PlayerRig.Current;
            rig.SetControl(false);
            rig.SetNoise(0f);
            var cam = rig.cam;
            rig.hands.SetPose(HandPose.Lowered);
            foreach (float pitch in new[] { 80f, 62f, 40f, 0f })
            {
                rig.look.SetAngles(rig.look.Yaw, pitch);
                yield return null; yield return null;
                yield return Save(cam, "pitch" + (int)pitch);
            }
            rig.look.SetAngles(rig.look.Yaw, 10f);
            foreach (var pose in new[] { HandPose.Open, HandPose.Grip, HandPose.Carry, HandPose.Shovel })
            {
                rig.hands.SetPose(pose);
                if (pose == HandPose.Shovel) rig.hands.SetTool(Tool.Shovel);
                rig.hands.Snap();
                yield return null; yield return null; yield return null;
                yield return Save(cam, "pose_" + pose);
            }
            rig.hands.SetPose(HandPose.Lowered);
            rig.look.SetAngles(rig.look.Yaw, 0f);
            yield return null; yield return null;
            // Camera ngoài: ba góc quanh người.
            var go = new GameObject("DbgCam");
            var dbg = go.AddComponent<Camera>();
            dbg.fieldOfView = 40f; dbg.nearClipPlane = 0.05f; dbg.clearFlags = CameraClearFlags.SolidColor;
            dbg.backgroundColor = new Color(0.2f, 0.3f, 0.4f);
            var c = rig.transform.position + Vector3.up * 0.95f;
            var f = rig.transform.forward; var r = rig.transform.right;
            var views = new[] { ("front", f * 2.6f + Vector3.up * 0.2f), ("side", r * 2.6f), ("front34", (f + r) * 1.9f + Vector3.up * 0.3f) };
            foreach (var (name, offset) in views)
            {
                go.transform.position = c + offset;
                go.transform.LookAt(c);
                yield return Save(dbg, "ext_" + name);
            }
            Object.Destroy(go);
        }
    }
}
