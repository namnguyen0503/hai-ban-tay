using System.Collections;
using HaiBanTay.Content;
using HaiBanTay.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace HaiBanTay.Narrative
{
    /// <summary>Bản đồ hải trình giữa các cảnh: các điểm dừng hiện lần lượt; chỉ hiện ngày có trong JSON (FACTS).</summary>
    public sealed class MapTransitionView
    {
        readonly VisualElement _root;
        readonly VisualElement _route;
        readonly VisualElement _line;

        public bool Visible { get; private set; }

        public MapTransitionView(VisualElement parent)
        {
            _root = Ui.Box("hbt-fullcard", parent);
            _route = Ui.Box("hbt-map-route", _root);
            _line = Ui.Box("hbt-map-line");
            Hide();
        }

        public IEnumerator Play(MapTransition data)
        {
            Core.GameRoot.Instance?.Audio.PlayMusic("Music/voyage", loop: true);
            Visible = true;
            Ui.Show(_root, true);
            _route.Clear();
            _route.Add(_line);
            var items = new VisualElement[data.Stops.Count];
            for (int i = 0; i < data.Stops.Count; i++)
            {
                var stop = data.Stops[i];
                var e = Ui.Box("hbt-map-stop", _route);
                Ui.Box("hbt-map-dot", e);
                Ui.Text("hbt-map-name", e, stop.Name);
                if (!string.IsNullOrEmpty(stop.Date))
                    Ui.Text("hbt-map-date", e, stop.Date);
                e.style.opacity = 0f;
                items[i] = e;
            }

            float duration = Mathf.Max(1f, data.Duration);
            float per = duration / (items.Length + 1);
            _line.style.width = new Length(0f, LengthUnit.Percent);
            for (float t = 0f; t < duration; t += Time.deltaTime)
            {
                float k = t / duration;
                // Đường nối từ tâm điểm đầu (12%) tới tâm điểm cuối (88%).
                _line.style.width = new Length(76f * Mathf.Clamp01(k * (items.Length + 1) / items.Length), LengthUnit.Percent);
                for (int i = 0; i < items.Length; i++)
                    items[i].style.opacity = Mathf.Clamp01((t - per * (i + 0.5f)) / 0.8f);
                yield return null;
            }
            Hide();
        }

        public void Hide()
        {
            if (Visible)
                Core.GameRoot.Instance?.Audio.StopMusic();
            Visible = false;
            Ui.Show(_root, false);
        }
    }
}
