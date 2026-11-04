using System.Collections;
using HaiBanTay.Core;
using UnityEngine;

namespace HaiBanTay.Narrative
{
    /// <summary>90_Sandbox: mở khóa một trang sổ, hiện một thẻ tư liệu và một câu phụ đề để thử UI.</summary>
    public sealed class SandboxDirector : MonoBehaviour
    {
        IEnumerator Start()
        {
            var root = GameRoot.Instance;
            if (root == null)
                yield break;
            yield return new WaitForSeconds(1f);
            root.Events.Raise(EventIds.S1NotebookIntro);
            root.UI.Prompt.ShowHint(root.Content.GetString("prompt.notebook"), 6f);
            yield return new WaitForSeconds(2f);
            root.Subtitles.Play("chef_potatoes");
            root.UI.Cards.Show("sc_hands");
        }
    }
}
