using HaiBanTay.Editor.SceneBuilders;
using HaiBanTay.Npc;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace HaiBanTay.Tests
{
    /// <summary>Vai của người trên tàu (cảnh 02): mỗi người một vai, không mượn mặt ông Mai.</summary>
    public class GalleyCastTests
    {
        [SetUp]
        public void OpenGalley() => EditorSceneManager.OpenScene(BuilderUtils.ScenePath("02_Galley"), OpenSceneMode.Single);

        /// <summary>QA-0011: người bạn mù chữ nhờ Ba viết thư (ngày 6, S16 "những người bạn mù chữ của tôi") là người khác,
        /// không dùng vai và mặt ông Mai.</summary>
        [Test]
        public void LetterFriendIsNotMai()
        {
            var people = Object.FindAnyObjectByType<VoyagePeople>(FindObjectsInactive.Include);
            Assert.IsNotNull(people);
            var friend = people.letterFriend.GetComponentsInChildren<NpcAgent>(true);
            Assert.AreEqual(1, friend.Length, "một người bạn ngồi chờ viết thư");
            StringAssert.DoesNotStartWith("Npc_Mai_", friend[0].name, "bạn mù chữ không mượn vai ông Mai");
            StringAssert.StartsWith("Npc_Helper_", friend[0].name, "bạn viết thư là người đã thì thầm ở bến (DIRECTOR §5)");
        }
    }
}
