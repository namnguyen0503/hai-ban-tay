using System.Collections;
using UnityEngine;

namespace HaiBanTay.Narrative
{
    /// <summary>Cánh tay NPC (Jacques, người bồi bàn, phụ bếp người Pháp): đưa vào khung hình rồi rút ra.
    /// Không có thoại riêng — thoại phát qua SubtitlePlayer.</summary>
    public sealed class NpcArm : MonoBehaviour
    {
        public Transform hiddenPoint;
        public Transform hand;
        /// <summary>Ẩn renderer khi ở điểm chờ (NPC cả người đứng chờ trong phòng, không được thấy trước).</summary>
        public bool hideWhenRetracted;

        Renderer[] _renderers;

        public bool Extended { get; private set; }

        void Awake()
        {
            _renderers = GetComponentsInChildren<Renderer>(true);
            if (hiddenPoint != null)
                transform.SetPositionAndRotation(hiddenPoint.position, hiddenPoint.rotation);
            if (hideWhenRetracted)
                SetVisible(false);
        }

        public IEnumerator Reach(Transform target, float seconds)
        {
            Extended = true;
            SetVisible(true);
            yield return MoveTo(target.position, target.rotation, seconds);
        }

        public IEnumerator Retract(float seconds)
        {
            if (hiddenPoint != null)
                yield return MoveTo(hiddenPoint.position, hiddenPoint.rotation, seconds);
            Extended = false;
            if (hideWhenRetracted)
                SetVisible(false);
        }

        void SetVisible(bool visible)
        {
            if (_renderers == null)
                return;
            foreach (var r in _renderers)
                if (r != null)
                    r.enabled = visible;
        }

        IEnumerator MoveTo(Vector3 pos, Quaternion rot, float seconds)
        {
            var p0 = transform.position;
            var r0 = transform.rotation;
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, t / seconds);
                transform.SetPositionAndRotation(Vector3.Lerp(p0, pos, k), Quaternion.Slerp(r0, rot, k));
                yield return null;
            }
            transform.SetPositionAndRotation(pos, rot);
        }
    }
}
