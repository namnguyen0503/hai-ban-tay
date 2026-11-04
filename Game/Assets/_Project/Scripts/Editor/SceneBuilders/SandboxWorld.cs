using System.Collections.Generic;
using HaiBanTay.Editor.Setup;
using HaiBanTay.Performance;
using HaiBanTay.Ship;
using UnityEditor;
using UnityEngine;

namespace HaiBanTay.Editor.SceneBuilders
{
    /// <summary>Khu thử Track B ở x = -60; không chiếm vùng thử đám đông.</summary>
    public static class SandboxWorld
    {
        public static void Build(Transform parent)
        {
            var formerSun = RenderSettings.sun;
            if (formerSun != null) formerSun.enabled = false;
            var interiorLights = new List<Light>();
            foreach (var light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (light != formerSun && light.isActiveAndEnabled && light.lightmapBakeType != LightmapBakeType.Baked)
                {
                    BuilderUtils.SetLightLayer(light, WorldRenderingLayers.Interior);
                    interiorLights.Add(light);
                }

            var station = BuilderUtils.Group("SandboxWorld", parent).transform;
            station.localPosition = new Vector3(-60f, 0f, 0f);
            var boat = BuilderUtils.Group("MockShip", station).transform;
            var deck = AssetFactory.Lit("WorldTestDeck", new Color(0.35f, 0.25f, 0.16f));
            var white = AssetFactory.Lit("WorldTestCabin", new Color(0.65f, 0.62f, 0.52f));
            var iron = AssetFactory.Lit("WorldTestRail", new Color(0.16f, 0.19f, 0.2f));
            BuilderUtils.Block("Deck", boat, new Vector3(0f, -0.15f, 0f), new Vector3(8f, 0.3f, 24f), deck, false);
            foreach (float x in new[] { -3.85f, 3.85f })
            {
                BuilderUtils.Block("Rail", boat, new Vector3(x, 1f, 0f), new Vector3(0.08f, 0.08f, 24f), iron, false);
                for (int z = -11; z <= 11; z += 2)
                    BuilderUtils.Block("Stanchion", boat, new Vector3(x, 0.5f, z), new Vector3(0.06f, 1f, 0.06f), iron, false);
            }
            BuilderUtils.SetRenderingLayer(boat.gameObject, WorldRenderingLayers.Exterior);
            var cabin = BuilderUtils.Group("MockCabin", boat).transform;
            foreach (float x in new[] { -2.5f, 2.5f })
                BuilderUtils.Block("Side", cabin, new Vector3(x, 1.4f, 4f), new Vector3(0.15f, 2.8f, 6f), white, false);
            BuilderUtils.Block("Back", cabin, new Vector3(0f, 1.4f, 7f), new Vector3(5f, 2.8f, 0.15f), white, false);
            BuilderUtils.Block("Roof", cabin, new Vector3(0f, 2.8f, 4f), new Vector3(5.2f, 0.15f, 6.2f), white, false);
            BuilderUtils.Block("Floor", cabin, new Vector3(0f, 0.025f, 4f), new Vector3(5f, 0.05f, 6f), deck, false);
            foreach (float x in new[] { -1.8f, 1.8f })
                BuilderUtils.Block("DoorSide", cabin, new Vector3(x, 1.4f, 1f), new Vector3(1.4f, 2.8f, 0.15f), white, false);
            BuilderUtils.Block("DoorHeader", cabin, new Vector3(0f, 2.55f, 1f), new Vector3(2.2f, 0.5f, 0.15f), white, false);
            BuilderUtils.SetRenderingLayer(cabin.gameObject, WorldRenderingLayers.Interior);
            var zone = BuilderUtils.Trigger<ShipZone>("InteriorZone", cabin, new Vector3(0f, 1.4f, 4f), new Vector3(5f, 2.8f, 6f));
            zone.region = ShipRegion.Interior;
            var fire = BuilderUtils.PointLight("WorldTestFire", cabin, new Vector3(-1.4f, 1.2f, 5f),
                new Color(1f, 0.5f, 0.2f), 1.6f, 8f, true);
            BuilderUtils.SetLightLayer(fire, WorldRenderingLayers.Interior);
            interiorLights.Insert(0, fire);

            var ship = Object.FindAnyObjectByType<ShipMotion>();
            ship.intensity = 0.6f;
            var cfg = WorldConfig.Sea();
            cfg.rollPivot = boat;
            cfg.ship = ship;
            var world = WorldBuilder.Build(station, cfg);
            var budget = station.gameObject.AddComponent<LightBudget>();
            budget.exterior = new[] { world.GetComponent<TimeOfDay>().mainLight };
            budget.interior = interiorLights.ToArray();
        }

        /// <summary>Dựng riêng sandbox qua Tools/unity.sh, không dựng lại scene 01/02/03.</summary>
        public static void Rebuild()
        {
            ProjectSetup.ApplyRenderTiers();
            SandboxSceneBuilder.Build();
            AssetDatabase.SaveAssets();
            Debug.Log("SandboxWorld: dựng xong 90_Sandbox.");
        }
    }
}
