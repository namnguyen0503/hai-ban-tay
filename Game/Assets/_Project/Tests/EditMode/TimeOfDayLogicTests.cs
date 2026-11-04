using System;
using HaiBanTay.Ship;
using NUnit.Framework;
using UnityEngine;

namespace HaiBanTay.Tests
{
    public class TimeOfDayLogicTests
    {
        static void Same(Color expected, Color actual)
        {
            Assert.Less((new Vector4(expected.r, expected.g, expected.b, expected.a)
                - new Vector4(actual.r, actual.g, actual.b, actual.a)).magnitude, 0.0001f);
        }

        [Test]
        public void DefaultKeyframesAndMidpointsInterpolate()
        {
            var keys = TimeOfDayLogic.DefaultKeyframes;
            foreach (var key in keys)
            {
                var state = TimeOfDayLogic.Evaluate(key.hour);
                Same(key.sky, state.Sky);
                Same(key.horizon, state.Horizon);
                Same(key.ground, state.Ground);
                Same(key.light, state.LightColor);
                Same(key.deepWater, state.DeepWater);
                Same(key.shallowWater, state.ShallowWater);
                Assert.AreEqual(key.fogDensity, state.FogDensity, 0.00001f);
                Assert.AreEqual(key.night, state.Night, 0.00001f);
            }
            for (int i = 1; i < keys.Length; i++)
            {
                var mid = TimeOfDayLogic.Evaluate((keys[i - 1].hour + keys[i].hour) * 0.5f);
                Same(Color.Lerp(keys[i - 1].sky, keys[i].sky, 0.5f), mid.Sky);
                Assert.AreEqual((keys[i - 1].fogDensity + keys[i].fogDensity) * 0.5f, mid.FogDensity, 0.00001f);
            }
        }

        [Test]
        public void MidnightIsContinuousAndHoursWrap()
        {
            var before = TimeOfDayLogic.Evaluate(23.9999f);
            var after = TimeOfDayLogic.Evaluate(0.0001f);
            Same(before.Sky, after.Sky);
            Assert.Less(Vector3.Distance(before.SunDirection, after.SunDirection), 0.0001f);
            Same(TimeOfDayLogic.Evaluate(0f).Sky, TimeOfDayLogic.Evaluate(24f).Sky);
            Same(TimeOfDayLogic.Evaluate(23f).Sky, TimeOfDayLogic.Evaluate(-1f).Sky);
            Same(TimeOfDayLogic.Evaluate(1f).Sky, TimeOfDayLogic.Evaluate(49f).Sky);
        }

        [TestCase(0f)]
        [TestCase(4f)]
        [TestCase(19f)]
        [TestCase(21f)]
        public void NightSunIsBelowHorizonAndMoonLightsDown(float hour)
        {
            var state = TimeOfDayLogic.Evaluate(hour);
            Assert.Less(state.SunDirection.y, 0f);
            Assert.Greater(state.MoonDirection.y, 0f);
            Assert.Less(state.MainLightDirection.y, 0f);
            Assert.IsTrue(state.MoonIsMain);
        }

        [Test]
        public void MainLightFadesAtBodySwitchAndPresetsRejectInvalidOrder()
        {
            foreach (float hour in new[] { 5.5f, 18.5f })
            {
                Assert.Less(TimeOfDayLogic.Evaluate(hour - 0.001f).LightIntensity, 0.001f);
                Assert.Less(TimeOfDayLogic.Evaluate(hour + 0.001f).LightIntensity, 0.001f);
            }
            var keys = TimeOfDayLogic.DefaultKeyframes;
            keys[1].hour = keys[0].hour;
            Assert.Throws<ArgumentException>(() => TimeOfDayLogic.Evaluate(12f, keys));
            Assert.Throws<ArgumentOutOfRangeException>(() => TimeOfDayLogic.Evaluate(float.NaN));
            Assert.AreEqual(1.4f, TimeOfDayLogic.Evaluate(12f).LightIntensity, 0.0001f);
        }
    }
}
