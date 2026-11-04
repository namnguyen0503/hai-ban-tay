using HaiBanTay.Editor.Setup;
using HaiBanTay.Shift;
using HaiBanTay.Ship;
using System.Linq;
using UnityEngine;
using Mats = HaiBanTay.Editor.SceneBuilders.Ship.ShipInterior.Mats;

namespace HaiBanTay.Editor.SceneBuilders
{
    public static partial class GalleySceneBuilder
    {
        /// <summary>Bếp gang ba nắp tròn, một buồng đốt kín dưới hotplate; giữ tọa độ trạm giữ nồi.</summary>
        static GameObject BuildPotRange(Transform env, Mats m)
        {
            var range = BuilderUtils.Group("ArtSlot_PotRange", env);
            range.transform.localPosition = new Vector3(-3f, 0f, -2.65f);
            // Tấm chắn nhiệt gắn vào tường, tách khỏi thân lò; không thêm vật chắn lối.
            BuilderUtils.Block("PotRangeWallShield", env, new Vector3(-3f, 1.25f, -2.985f),
                new Vector3(3.2f, 1.5f, 0.02f), AssetFactory.RangeIron(), true, false);
            // Ba ô bếp, mỗi ô một buồng đốt riêng dưới lưới; mức lửa do CoalStation điều khiển.
            FireBuilder.CastRange(range.transform, 3.2f, 0.7f, new[] { -1f, 0f, 1f });
            return range;
        }

        static void BindRangeCells(CoalStation coal, Transform range, int stoveIndex)
        {
            var lids = range.GetComponentsInChildren<Transform>().Where(t => t.name == "StoveLid").ToArray();
            var doors = range.GetComponentsInChildren<Transform>().Where(t => t.name == "AshDoor").ToArray();
            var ash = range.GetComponentsInChildren<Transform>().Where(t => t.name == "Ash").ToArray();
            var flickers = range.GetComponentsInChildren<FireFlicker>();
            var all = new System.Collections.Generic.List<CoalStation.FireCell>(coal.cells);
            for (int i = 0; i < lids.Length; i++)
            {
                var mouth = BuilderUtils.Group(all.Count == 0 ? "Mouth" : "Mouth_" + all.Count, range);
                mouth.transform.localPosition = new Vector3(lids[i].localPosition.x, 0.57f, doors[i].localPosition.z + 0.027f);
                var col = mouth.AddComponent<BoxCollider>();
                col.isTrigger = true;
                col.size = new Vector3(0.58f, 0.34f, 0.04f);
                var hob = BuilderUtils.Group("Hob_" + all.Count, range).transform;
                hob.localPosition = lids[i].localPosition + Vector3.up * 0.004f;
                var flicker = flickers[i];
                flicker.level = 0f;
                foreach (var glow in flicker.glows) BuilderUtils.ClearStatic(glow.renderer.gameObject);
                BuilderUtils.ClearStatic(ash[i].gameObject);
                all.Add(new CoalStation.FireCell
                {
                    mouth = AddTarget(mouth, coal, "stove" + all.Count), flicker = flicker,
                    hob = hob, ashPile = ash[i], ashDoorFrame = doors[i], ashHinge = doors[i].Find("AshHinge"), stoveIndex = stoveIndex
                });
            }
            coal.cells = all.ToArray();
            coal.stove = coal.cells[0].mouth;
        }
    }
}
