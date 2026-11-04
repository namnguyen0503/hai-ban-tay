using HaiBanTay.Shift;
using UnityEngine;
using UnityEngine.UIElements;

namespace HaiBanTay.UI
{
    /// <summary>Biểu tượng loại việc vẽ bằng Painter2D (không cần ảnh): ngọn lửa, bao khoai, nồi, dây chằng, nồi cao,
    /// cào tro, chổi, lá rau, chảo kéo, rổ rau buộc dây.
    /// Người chưa đọc được chữ Pháp vẫn biết phải làm gì.</summary>
    public sealed class OrderIcon : VisualElement
    {
        readonly string _kind;
        public Color Color = new Color(0.96f, 0.91f, 0.8f);

        public OrderIcon(string kind, float size = 44f)
        {
            _kind = kind;
            pickingMode = PickingMode.Ignore;
            style.width = size;
            style.height = size;
            style.flexShrink = 0;
            generateVisualContent += Draw;
        }

        void Draw(MeshGenerationContext ctx)
        {
            var r = contentRect;
            float w = r.width, h = r.height;
            if (w <= 0f || h <= 0f)
                return;
            var p = ctx.painter2D;
            p.fillColor = Color;
            p.strokeColor = Color;
            p.lineWidth = Mathf.Max(2f, w * 0.07f);
            p.lineJoin = LineJoin.Round;
            p.lineCap = LineCap.Round;
            Vector2 P(float x, float y) => new Vector2(r.x + x * w, r.y + y * h);
            switch (_kind)
            {
                case OrderKinds.Fire:
                    p.BeginPath();
                    p.MoveTo(P(0.5f, 0.06f));
                    p.BezierCurveTo(P(0.78f, 0.32f), P(0.9f, 0.55f), P(0.82f, 0.75f));
                    p.BezierCurveTo(P(0.74f, 0.95f), P(0.26f, 0.95f), P(0.18f, 0.75f));
                    p.BezierCurveTo(P(0.1f, 0.55f), P(0.28f, 0.42f), P(0.34f, 0.26f));
                    p.BezierCurveTo(P(0.42f, 0.38f), P(0.46f, 0.3f), P(0.5f, 0.06f));
                    p.ClosePath();
                    p.Fill();
                    break;
                case OrderKinds.Sack:
                    p.BeginPath();
                    p.MoveTo(P(0.36f, 0.22f));
                    p.LineTo(P(0.64f, 0.22f));
                    p.BezierCurveTo(P(0.92f, 0.4f), P(0.92f, 0.92f), P(0.5f, 0.94f));
                    p.BezierCurveTo(P(0.08f, 0.92f), P(0.08f, 0.4f), P(0.36f, 0.22f));
                    p.ClosePath();
                    p.Fill();
                    p.BeginPath();
                    p.MoveTo(P(0.34f, 0.08f));
                    p.LineTo(P(0.5f, 0.2f));
                    p.LineTo(P(0.66f, 0.08f));
                    p.Stroke();
                    break;
                case OrderKinds.Wash:
                    Pot(p, P, 0.18f, 0.82f, 0.42f, 0.86f);
                    p.BeginPath();
                    p.MoveTo(P(0.36f, 0.3f));
                    p.BezierCurveTo(P(0.3f, 0.2f), P(0.42f, 0.12f), P(0.36f, 0.04f));
                    p.MoveTo(P(0.62f, 0.3f));
                    p.BezierCurveTo(P(0.56f, 0.2f), P(0.68f, 0.12f), P(0.62f, 0.04f));
                    p.Stroke();
                    break;
                case OrderKinds.ScrubTall:
                    Pot(p, P, 0.24f, 0.76f, 0.14f, 0.92f);
                    break;
                case OrderKinds.Secure:
                    Pot(p, P, 0.24f, 0.76f, 0.4f, 0.86f);
                    p.BeginPath();
                    p.MoveTo(P(0.08f, 0.62f));
                    p.BezierCurveTo(P(0.3f, 0.5f), P(0.7f, 0.74f), P(0.92f, 0.6f));
                    p.Stroke();
                    break;
                case OrderKinds.Ash:
                    // Cào: cán chéo, lưỡi ngang; đống tro bên dưới.
                    p.BeginPath();
                    p.MoveTo(P(0.82f, 0.08f));
                    p.LineTo(P(0.36f, 0.62f));
                    p.MoveTo(P(0.2f, 0.52f));
                    p.LineTo(P(0.5f, 0.74f));
                    p.Stroke();
                    p.BeginPath();
                    p.MoveTo(P(0.08f, 0.94f));
                    p.BezierCurveTo(P(0.22f, 0.72f), P(0.5f, 0.72f), P(0.64f, 0.94f));
                    p.ClosePath();
                    p.Fill();
                    break;
                case OrderKinds.Sweep:
                    // Chổi: cán đứng, bó chổi xòe.
                    p.BeginPath();
                    p.MoveTo(P(0.5f, 0.06f));
                    p.LineTo(P(0.5f, 0.56f));
                    p.Stroke();
                    p.BeginPath();
                    p.MoveTo(P(0.38f, 0.56f));
                    p.LineTo(P(0.62f, 0.56f));
                    p.LineTo(P(0.8f, 0.94f));
                    p.LineTo(P(0.2f, 0.94f));
                    p.ClosePath();
                    p.Fill();
                    break;
                case OrderKinds.Greens:
                    // Lá rau: hình lá có gân giữa.
                    p.BeginPath();
                    p.MoveTo(P(0.18f, 0.86f));
                    p.BezierCurveTo(P(0.1f, 0.4f), P(0.5f, 0.1f), P(0.88f, 0.1f));
                    p.BezierCurveTo(P(0.88f, 0.5f), P(0.6f, 0.9f), P(0.18f, 0.86f));
                    p.ClosePath();
                    p.Fill();
                    p.strokeColor = new Color(0.1f, 0.08f, 0.06f, 0.8f);
                    p.BeginPath();
                    p.MoveTo(P(0.22f, 0.82f));
                    p.LineTo(P(0.74f, 0.26f));
                    p.Stroke();
                    break;
                case OrderKinds.Pan:
                    // Chảo rộng, mũi tên kéo.
                    p.BeginPath();
                    p.MoveTo(P(0.12f, 0.6f));
                    p.LineTo(P(0.88f, 0.6f));
                    p.LineTo(P(0.78f, 0.86f));
                    p.LineTo(P(0.22f, 0.86f));
                    p.ClosePath();
                    p.Fill();
                    p.BeginPath();
                    p.MoveTo(P(0.7f, 0.3f));
                    p.LineTo(P(0.2f, 0.3f));
                    p.MoveTo(P(0.34f, 0.16f));
                    p.LineTo(P(0.2f, 0.3f));
                    p.LineTo(P(0.34f, 0.44f));
                    p.Stroke();
                    break;
                case OrderKinds.Haul:
                    // Rổ rau (thân thang, lá nhô) và dây kéo chéo lên góc phải.
                    p.BeginPath();
                    p.MoveTo(P(0.08f, 0.56f));
                    p.LineTo(P(0.6f, 0.56f));
                    p.LineTo(P(0.54f, 0.92f));
                    p.LineTo(P(0.14f, 0.92f));
                    p.ClosePath();
                    p.Fill();
                    p.BeginPath();
                    p.Arc(P(0.24f, 0.5f), w * 0.1f, Angle.Degrees(180f), Angle.Degrees(360f));
                    p.Arc(P(0.44f, 0.48f), w * 0.11f, Angle.Degrees(180f), Angle.Degrees(360f));
                    p.Fill();
                    p.BeginPath();
                    p.MoveTo(P(0.6f, 0.6f));
                    p.BezierCurveTo(P(0.72f, 0.5f), P(0.8f, 0.3f), P(0.94f, 0.08f));
                    p.Stroke();
                    break;
                default:
                    p.BeginPath();
                    p.Arc(P(0.5f, 0.5f), w * 0.3f, Angle.Degrees(0f), Angle.Degrees(360f));
                    p.Fill();
                    break;
            }
        }

        static void Pot(Painter2D p, System.Func<float, float, Vector2> P, float left, float right, float top, float bottom)
        {
            p.BeginPath();
            p.MoveTo(P(left, top));
            p.LineTo(P(right, top));
            p.LineTo(P(right - 0.04f, bottom));
            p.LineTo(P(left + 0.04f, bottom));
            p.ClosePath();
            p.Fill();
            p.BeginPath();
            p.MoveTo(P(left - 0.12f, top + 0.08f));
            p.LineTo(P(left, top + 0.08f));
            p.MoveTo(P(right, top + 0.08f));
            p.LineTo(P(right + 0.12f, top + 0.08f));
            p.Stroke();
        }
    }
}
