using System.Collections;
using System.Linq;
using System.Text;
using HaiBanTay.Player;
using HaiBanTay.Shift;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace HaiBanTay.Tests
{
    /// <summary>Đo số thật của người chơi và đồ trong phòng rửa để chọn hình học bàn chải, chỗ nắm nồi cao (brush17, Explicit).</summary>
    [Explicit, Category("Probe")]
    public class Brush17Probe
    {
        [UnityTest, Timeout(300000)]
        public IEnumerator MeasureBodyAndProps()
        {
            yield return ChoreTestUtil.Galley();
            var rig = PlayerRig.Current;
            var sb = new StringBuilder();
            ChoreTestUtil.Look(new Vector3(0f, 0f, 0f), new Vector3(0f, 1.2f, 2f));
            yield return new WaitForSeconds(0.5f);
            void Body(string tag)
            {
                var root = rig.transform;
                sb.AppendLine($"{tag}: eye {root.InverseTransformPoint(rig.head.position):F3} " +
                              $"vaiT {root.InverseTransformPoint(rig.body.Shoulder(true).position):F3} vaiP {root.InverseTransformPoint(rig.body.Shoulder(false).position):F3} " +
                              $"tayT {rig.body.ArmLength(true):F3} tayP {rig.body.ArmLength(false):F3} bodyZ {rig.body.transform.localPosition:F3}");
            }
            Body("đứng");
            rig.motor.ExtraHeadHeight = 0.35f;
            yield return new WaitForSeconds(1.5f);
            Body("ghế +0,35");
            rig.motor.ExtraHeadHeight = 0f;
            yield return new WaitForSeconds(1.5f);
            foreach (var name in new[] { "Hand_R_Grip", "Hand_L_Grip", "Hand_R_Carry", "Hand_R_Open" })
            {
                var t = rig.hands.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name.StartsWith(name));
                if (t == null) { sb.AppendLine(name + ": không có"); continue; }
                var mf = t.GetComponentInChildren<MeshFilter>(true);
                var smr = t.GetComponentInChildren<SkinnedMeshRenderer>(true);
                var mesh = mf != null ? mf.sharedMesh : smr != null ? smr.sharedMesh : null;
                if (mesh == null) { sb.AppendLine(name + ": không mesh"); continue; }
                var hand = t.parent;
                var min = Vector3.positiveInfinity; var max = Vector3.negativeInfinity;
                var tr = (mf != null ? mf.transform : smr.transform);
                foreach (var v in mesh.vertices)
                {
                    var p = hand.InverseTransformPoint(tr.TransformPoint(v));
                    min = Vector3.Min(min, p); max = Vector3.Max(max, p);
                }
                sb.AppendLine($"{name}: khung {hand.name} min {min:F3} max {max:F3}");
            }
            var d = Object.FindAnyObjectByType<ShiftDirector>();
            var tall = Object.FindAnyObjectByType<HaiBanTay.Shift.TallPotStation>();
            if (tall != null)
            {
                foreach (var r in tall.potRoot.GetComponentsInChildren<Renderer>(true))
                    sb.AppendLine($"nồi cao {r.name} active {r.gameObject.activeInHierarchy} bounds {r.bounds.center:F3} {r.bounds.size:F3}");
            }
            for (int i = 0; i < d.meals.hobs.Length; i++)
                sb.AppendLine($"hob {i} {d.meals.hobs[i].position:F3}");
            foreach (var p in d.meals.pots)
            {
                var b = p.GetComponentsInChildren<Renderer>(true).Select(r => r.bounds).Aggregate((a, c) => { a.Encapsulate(c); return a; });
                sb.AppendLine($"nồi {p.name} {p.transform.position:F3} bounds {b.size:F3}");
            }
            sb.AppendLine($"chậu {d.wash.basin.transform.position:F3} yaw {d.wash.basin.transform.eulerAngles.y:F1}; chồng {d.wash.pile.transform.position:F3}; giá {d.wash.rack.transform.position:F3}");
            sb.AppendLine($"chảo {d.pan.pan.position:F3} stops {d.pan.stops[0].position:F3} {d.pan.stops[1].position:F3}");
            var range = GameObject.Find("ArtSlot_PotRange");
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None).Where(r => r.bounds.center.z > 1.9f && r.bounds.center.z < 3f && r.bounds.center.x < 0f && r.bounds.center.x > -4f && r.bounds.size.y > 0.5f).Take(30))
                sb.AppendLine($"lò bắc? {r.name} {r.bounds.center:F3} {r.bounds.size:F3}");
            Debug.Log("BRUSH17_PROBE\n" + sb);
            System.IO.File.WriteAllText(System.IO.Path.Combine(Application.dataPath, "../../Logs/brush17_probe.txt"), sb.ToString());
        }
    }
}
