using System.Collections;
using HaiBanTay.Interaction;
using UnityEngine;

namespace HaiBanTay.Shift
{
    /// <summary>
    /// Tờ giấy trắng sau bức thư ngày 6 (DIRECTOR §6): Ba viết thư hộ người khác, không viết cho nhà mình. Người chơi có thể cầm
    /// bút lên; bút dừng trên giấy một lúc rồi đặt xuống. Không chữ, không lời, không bắt buộc, không đổi camera.
    /// </summary>
    public sealed class BlankPaper : MonoBehaviour
    {
        public InteractTarget target;
        public Transform pen;
        [Tooltip("Bút nhấc lên và dừng trên giấy chừng này giây rồi đặt xuống.")]
        public float holdSeconds = 2.6f;

        Vector3 _restPos;
        Quaternion _restRot;
        bool _used;

        public bool Used => _used;

        void Awake()
        {
            if (pen != null)
            {
                _restPos = pen.localPosition;
                _restRot = pen.localRotation;
            }
            if (target != null)
                target.Interacted += OnInteract;
        }

        void OnDestroy()
        {
            if (target != null)
                target.Interacted -= OnInteract;
        }

        /// <summary>Bày giấy bút lên bàn (tối ngày 6, sau bức thư).</summary>
        public void Offer()
        {
            gameObject.SetActive(true);
            _used = false;
            if (target != null)
                target.available = true;
        }

        void OnInteract(Interactor who)
        {
            if (_used)
                return;
            _used = true;
            target.available = false;
            StartCoroutine(Hesitate());
        }

        IEnumerator Hesitate()
        {
            if (pen == null)
                yield break;
            var up = _restPos + new Vector3(0f, 0.035f, 0f);
            var poised = _restRot * Quaternion.Euler(-35f, 0f, 0f);
            yield return Move(_restPos, up, _restRot, poised, 0.7f);
            yield return new WaitForSeconds(holdSeconds);
            yield return Move(up, _restPos, poised, _restRot, 0.9f);
        }

        IEnumerator Move(Vector3 fromPos, Vector3 toPos, Quaternion fromRot, Quaternion toRot, float seconds)
        {
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, t / seconds);
                pen.localPosition = Vector3.Lerp(fromPos, toPos, k);
                pen.localRotation = Quaternion.Slerp(fromRot, toRot, k);
                yield return null;
            }
            pen.localPosition = toPos;
            pen.localRotation = toRot;
        }
    }
}
