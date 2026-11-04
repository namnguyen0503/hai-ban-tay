using UnityEngine.UIElements;

namespace HaiBanTay.UI
{
    /// <summary>Hàm dựng phần tử UI Toolkit gọn; giao diện nằm trong theme.uss.</summary>
    public static class Ui
    {
        public static VisualElement Box(string cls, VisualElement parent = null)
        {
            var e = new VisualElement { pickingMode = PickingMode.Ignore };
            if (!string.IsNullOrEmpty(cls))
                e.AddToClassList(cls);
            parent?.Add(e);
            return e;
        }

        public static Label Text(string cls, VisualElement parent = null, string text = "")
        {
            var l = new Label(text) { pickingMode = PickingMode.Ignore };
            if (!string.IsNullOrEmpty(cls))
                l.AddToClassList(cls);
            parent?.Add(l);
            return l;
        }

        public static Button Button(string text, System.Action onClick, VisualElement parent = null, string cls = "hbt-button")
        {
            var b = new Button(() =>
            {
                Core.GameRoot.Instance?.Audio.PlaySfx("SFX/ui_click", 0.5f);
                onClick?.Invoke();
            }) { text = text };
            b.AddToClassList(cls);
            parent?.Add(b);
            return b;
        }

        public static void Show(VisualElement e, bool visible)
        {
            if (e != null)
                e.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        public static bool IsShown(VisualElement e) => e != null && e.resolvedStyle.display != DisplayStyle.None
            && e.style.display != DisplayStyle.None;
    }
}
