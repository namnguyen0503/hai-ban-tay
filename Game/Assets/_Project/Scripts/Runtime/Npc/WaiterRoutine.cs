using System.Collections;
using UnityEngine;

namespace HaiBanTay.Npc
{
    /// <summary>Khách đã ngồi mới mang khay; tách rời khay, tới đúng mặt bàn rồi mới báo phục vụ xong. Khay phẳng bưng
    /// trước ngực: IK hai đoạn đặt tay phải dưới khay sau Animator (clip TrayCarry giữ chân/tay trái).</summary>
    public sealed class WaiterRoutine : MonoBehaviour
    {
        public NpcAgent agent;
        public Transform counter;
        public Transform servePoint;
        public Transform tablePoint;
        public Transform table;
        public Transform tray;
        public Transform carryHand;
        [Tooltip("Xương cánh tay/cẳng tay phải (Humanoid) cho IK đưa khay ra trước ngực.")]
        public Transform upperArm;
        public Transform lowerArm;
        [Tooltip("Tâm khay trong hệ bồi bàn (x phải, y lên từ chân, z trước).")]
        public Vector3 trayChest = new Vector3(0f, 1.24f, 0.36f);
        [Tooltip("Cổ tay phải so với tâm khay (hệ bồi bàn): dưới khay, lệch phải.")]
        public Vector3 handUnderTray = new Vector3(0.06f, -0.02f, -0.06f);
        public Transform cup;
        public bool CupPlaced { get; private set; }
        public int Servings { get; private set; }
        bool _started;

        // Khay dựng sẵn dưới điểm bê: ẩn tới lúc phục vụ, không thì khay lơ lửng trước bụng bồi bàn đứng chờ ở quầy (bot 04/11: tay cách
        // khay 65 cm).
        void Start()
        {
            if (!_started && tray != null && agent != null && agent.Carrying != tray)
                tray.gameObject.SetActive(false);
        }

        void LateUpdate()
        {
            if (agent.Carrying != tray || !tray.gameObject.activeSelf) return;
            var yaw = Quaternion.Euler(0f, agent.transform.eulerAngles.y, 0f);
            if (upperArm != null && lowerArm != null)
            {
                // Khay phẳng trước ngực: kéo cổ tay phải về dưới tâm khay, khuỷu tay khép bên sườn.
                var root = agent.transform.position;
                Reach(upperArm, lowerArm, carryHand, root + yaw * (trayChest + handUnderTray),
                    root + yaw * new Vector3(0.5f, 0.85f, -0.15f));
                tray.SetPositionAndRotation(carryHand.position - yaw * handUnderTray, yaw);
                return;
            }
            // Thiếu xương tay: khay theo tay nâng trong TrayCarry, giữ mặt phẳng ngang khi animation xoay cổ tay.
            tray.SetPositionAndRotation(carryHand.position + Vector3.up * 0.018f + agent.transform.forward * 0.08f, yaw);
        }

        /// <summary>IK hai đoạn giải tích; giữ hướng bàn tay (ngửa đỡ khay) của clip.</summary>
        static void Reach(Transform upper, Transform lower, Transform hand, Vector3 target, Vector3 pole)
        {
            var handRotation = hand.rotation;
            Vector3 a = upper.position;
            float la = Vector3.Distance(a, lower.position), lb = Vector3.Distance(lower.position, hand.position);
            var toTarget = target - a;
            float d = Mathf.Clamp(toTarget.magnitude, 0.01f, (la + lb) * 0.999f);
            var dir = toTarget.normalized;
            var bend = Vector3.ProjectOnPlane(pole - a, dir);
            if (bend.sqrMagnitude < 1e-6f)
                bend = Vector3.down;
            float cos = Mathf.Clamp((la * la + d * d - lb * lb) / (2f * la * d), -1f, 1f);
            var elbow = a + dir * (la * cos) + bend.normalized * (la * Mathf.Sqrt(1f - cos * cos));
            upper.rotation = Quaternion.FromToRotation(lower.position - a, elbow - a) * upper.rotation;
            lower.rotation = Quaternion.FromToRotation(hand.position - lower.position, a + dir * d - lower.position) * lower.rotation;
            hand.rotation = handRotation;
        }

        public IEnumerator Serve()
        {
            if (_started)
                yield break;
            _started = true;
            tray.gameObject.SetActive(true);
            agent.PickUp(tray);
            agent.carryWalkState = "TrayCarry";
            agent.Play("TrayCarry");
            yield return agent.WalkTo(servePoint);
            yield return agent.Face(servePoint.eulerAngles.y);
            agent.Play("HandOver");
            var from = cup.position;
            var rotation = cup.rotation;
            cup.SetParent(table, true);
            const float seconds = 0.85f;
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, t / seconds);
                cup.SetPositionAndRotation(Vector3.Lerp(from, tablePoint.position, k), Quaternion.Slerp(rotation, tablePoint.rotation, k));
                yield return null;
            }
            cup.SetPositionAndRotation(tablePoint.position, tablePoint.rotation);
            Core.GameRoot.Instance?.Audio.PlaySfxAt("SFX/cup_saucer", cup.position, 0.65f);
            CupPlaced = true;
            Servings++;
            // Đứng chờ cạnh bàn: Idle (khay vẫn theo IK tay phải ở LateUpdate). TrayCarry là clip đi bưng khay, đứng yên mà chạy nó thì
            // chân bước tại chỗ suốt lúc ông Mai nói (người dùng 04/11, trailer). Đi về quầy lại dùng carryWalkState TrayCarry.
            agent.Play("Idle");
            // Đợi crossfade khỏi HandOver trước khi báo phục vụ xong; tay/khay đã trở lại tư thế bê.
            yield return new WaitForSeconds(0.3f);
        }

        public IEnumerator ReturnToCounter()
        {
            yield return agent.WalkTo(counter);
            agent.PutDown(counter.position + Vector3.up, counter.rotation, counter.parent);
            tray.gameObject.SetActive(false);
            agent.Play("IdleLook");
        }
    }
}
