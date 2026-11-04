using System.Collections;
using HaiBanTay.Content;
using HaiBanTay.Core;
using HaiBanTay.Shift;
using UnityEngine;

namespace HaiBanTay.Npc
{
    /// <summary>Những nhóm nhỏ theo cảnh. Các nhân vật dùng cùng avatar/LOD Rocketbox của tàu.</summary>
    public sealed class VoyagePeople : MonoBehaviour
    {
        public GameObject cooks, cardPlayers, seasick, firstClass, letterFriend, crewMeal;
        /// <summary>Hai đầu bếp ăn ở phòng ăn: đi từ boong vào ngồi, ăn xong đứng dậy đi ra (QA-0131), không hiện/biến tại chỗ.</summary>
        public NpcAgent[] diners = new NpcAgent[0];
        /// <summary>Độ hạ thân (m, thường âm) builder đặt cho tư thế ngồi của từng người trong diners.</summary>
        public float[] dinerDrop = new float[0];
        /// <summary>Đường từ trong phòng ăn (cạnh bàn) ra tới boong, khuất tầm nhìn từ bếp.</summary>
        public Transform[] dinerExit = new Transform[0];
        /// <summary>Đứng sau ghế chừng này (m) trước khi ngồi xuống / sau khi đứng dậy.</summary>
        public float behindSeat = 0.35f;
        /// <summary>Giữ cửa bếp mở khi hai đầu bếp đi qua lúc tới/rời phòng ăn (cửa chỉ tự mở cho người chơi; bot 22/10 thân xuyên DoorLeaf).</summary>
        public DayCast cast;

        const float SitSeconds = 2.9f, StandSeconds = 2.5f;

        Vector3[] _seat;
        float[] _yaw;
        Transform[] _body;
        Vector3[] _seatedBody;
        Coroutine _cooksMove;
        bool _cooksWanted;

        void Awake()
        {
            int n = diners.Length;
            _seat = new Vector3[n];
            _yaw = new float[n];
            _body = new Transform[n];
            _seatedBody = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                if (diners[i] == null)
                    continue;
                _seat[i] = diners[i].transform.position;
                _yaw[i] = diners[i].transform.eulerAngles.y;
                var animator = diners[i].GetComponentInChildren<Animator>(true);
                _body[i] = animator != null ? animator.transform : null;
                if (_body[i] != null)
                    _seatedBody[i] = _body[i].localPosition;
            }
        }

        public void Phase(PhaseData phase)
        {
            SetCooks(phase.Orders.Exists(o => o.Task == OrderKinds.Serve));
            crewMeal.SetActive(phase.Orders.Exists(o => o.Task == OrderKinds.Soup));
            firstClass.SetActive(phase.Orders.Exists(o => o.Task == OrderKinds.Chieu));
            if (phase.Storm) seasick.SetActive(true);
            if (phase.Calm) seasick.SetActive(false);
        }

        public void Evening(int day)
        {
            StopCooks();
            cooks.SetActive(false); crewMeal.SetActive(false); firstClass.SetActive(false); seasick.SetActive(false);
            cardPlayers.SetActive(true); letterFriend.SetActive(day == 6);
        }

        float _nextDoorCheck;

        void Update()
        {
            if (_cooksMove == null || cast == null || Time.time < _nextDoorCheck)
                return;
            _nextDoorCheck = Time.time + 0.5f;
            foreach (var a in diners)
                if (a != null && a.gameObject.activeInHierarchy && cast.HoldDoorNear(a.Position))
                    break;
        }

        void SetCooks(bool want)
        {
            if (want == _cooksWanted && cooks.activeSelf == want && _cooksMove == null)
                return;
            _cooksWanted = want;
            StopCooks();
            var root = GameRoot.Instance;
            // Sim, lượt montage (màn đã đen) hay thiếu dữ liệu đường đi: đặt thẳng như cũ.
            bool instant = Sim.Enabled || !isActiveAndEnabled || diners.Length == 0 || dinerExit.Length == 0
                           || (root != null && root.UI != null && root.UI.FadeAlpha > 0.5f);
            if (instant)
            {
                cooks.SetActive(want);
                return;
            }
            if (want == cooks.activeSelf)
                return;
            _cooksMove = StartCoroutine(want ? CooksArrive() : CooksLeave());
        }

        /// <summary>Coroutine đi của từng người (Arrive/Leave): dừng cùng CooksArrive/CooksLeave, không thì chúng chạy tiếp song song với lượt đi ngược lại
        /// và đầu bếp đi thẳng xuyên bếp, vách (bot 23/10, log [Diners] 24/10: SetCooks want=False moving=True giữa lúc Arrive).</summary>
        readonly System.Collections.Generic.List<Coroutine> _walks = new System.Collections.Generic.List<Coroutine>();

        void StopCooks()
        {
            if (_cooksMove == null)
                return;
            StopCoroutine(_cooksMove);
            _cooksMove = null;
            foreach (var w in _walks)
                if (w != null)
                    StopCoroutine(w);
            _walks.Clear();
            for (int i = 0; i < diners.Length; i++)
                Reseat(i);
        }

        /// <summary>Về đúng tư thế ngồi builder dựng (routine ngồi chạy lại khi nhóm hiện).</summary>
        void Reseat(int i)
        {
            if (diners[i] == null)
                return;
            diners[i].ClearWalkState();
            diners[i].SetBodyActive(true);
            diners[i].TeleportTo(_seat[i], _yaw[i]);
            if (_body[i] != null)
                _body[i].localPosition = _seatedBody[i];
            var routine = diners[i].GetComponent<NpcRoutine>();
            if (routine != null)
                routine.enabled = true;
        }

        /// <summary>Thân đứng: bỏ phần hạ thân của tư thế ngồi (clip StandUp và đi bộ đặt chân đúng sàn khi thân không hạ).</summary>
        void Stand(int i)
        {
            if (_body[i] == null)
                return;
            _body[i].localPosition = _seatedBody[i];
            _body[i].position -= Vector3.up * (i < dinerDrop.Length ? dinerDrop[i] : 0f);
        }

        Vector3 Behind(int i) => _seat[i] - Quaternion.Euler(0f, _yaw[i], 0f) * Vector3.forward * behindSeat;

        IEnumerator CooksArrive()
        {
            for (int i = 0; i < diners.Length; i++)
            {
                var routine = diners[i] != null ? diners[i].GetComponent<NpcRoutine>() : null;
                if (routine != null)
                    routine.enabled = false;
            }
            cooks.SetActive(true);
            var walks = new Coroutine[diners.Length];
            for (int i = 0; i < diners.Length; i++)
                if (diners[i] != null)
                    _walks.Add(walks[i] = StartCoroutine(Arrive(i, i * 1.4f)));
            foreach (var w in walks)
                if (w != null)
                    yield return w;
            _walks.Clear();
            _cooksMove = null;
        }

        IEnumerator Arrive(int i, float delay)
        {
            var a = diners[i];
            Stand(i);
            var start = dinerExit[dinerExit.Length - 1].position;
            var next = dinerExit.Length > 1 ? dinerExit[dinerExit.Length - 2].position : Behind(i);
            var d = next - start;
            a.TeleportTo(start, Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg);
            a.Play("Idle");
            // Người đi sau chờ khuất (thân và capsule tắt) ở điểm xuất phát, không đứng chồng lên người đi trước.
            if (delay > 0f)
            {
                a.SetBodyActive(false);
                yield return new WaitForSeconds(delay);
                a.SetBodyActive(true);
            }
            // dinerExit[0] chỉ cho hàng z dọc mép bắc bàn: người ngồi phía đông đi thẳng tới sau ghế mình, không vòng qua điểm tây rồi quay lại
            // giữa đường người kia (bot 23/10: người phía tây lách, thân chồng Wall_S).
            for (int k = dinerExit.Length - 2; k >= 1; k--)
            {
                yield return WalkSure(a, dinerExit[k].position);
            }
            var behind = Behind(i);
            // Vòng ra sau ghế theo hướng song song với bàn, không cắt chéo qua ghế. Đã tới cửa phòng ăn (WalkSure) thì đi đúng điểm đặt sẵn
            // trong phòng: tránh vật tĩnh tự chọn góc vòng ra ngoài vách tây (bot 23/10: Cook_4111 thân chồng DiningWest).
            a.avoidStatic = false;
            yield return a.WalkTo(new Vector3(behind.x, behind.y, dinerExit[0].position.z));
            yield return a.WalkTo(behind);
            a.avoidStatic = true;
            yield return a.Face(_yaw[i]);
            // SitDown chạy với thân đã hạ: khung đầu đứng chân chạm sàn, khung cuối ngồi đúng ghế; gốc trượt từ sau ghế vào ghế
            // trong lúc hông hạ xuống.
            if (_body[i] != null)
                _body[i].localPosition = _seatedBody[i];
            a.Play("SitDown", 0.1f);
            for (float t = 0f; t < SitSeconds; t += Time.deltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.2f, 1.5f, t));
                a.TeleportTo(Vector3.Lerp(behind, _seat[i], k), _yaw[i]);
                yield return null;
            }
            Reseat(i);
        }

        /// <summary>
        /// Đi tới điểm đường, chưa tới gần (WalkTo coi như tới khi đích có người đứng chắn) thì chờ rồi đi lại: bỏ qua điểm cửa phòng ăn
        /// thì đoạn sau đi thẳng xuyên lò, vách (bot 23/10, Cook_4111). Phụ bếp đứng chắn lối quá lâu thì đặt người tới điểm đó khi Ba ở
        /// xa (khuất tầm nhìn), không bao giờ đi xuyên vật.
        /// </summary>
        static IEnumerator WalkSure(NpcAgent a, Vector3 point)
        {
            float start = Time.time;
            while (true)
            {
                yield return a.WalkTo(point);
                var d = a.Position - point;
                d.y = 0f;
                if (d.sqrMagnitude < 0.35f * 0.35f)
                    yield break;
                float waited = Time.time - start;
                var player = HaiBanTay.Player.PlayerRig.Current;
                bool far = player == null || (player.Position - a.Position).sqrMagnitude > 6f * 6f;
                if (waited > 90f || (waited > 20f && far))
                {
                    var to = point - a.Position;
                    a.TeleportTo(point, Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg);
                    yield break;
                }
                yield return new WaitForSeconds(1f);
            }
        }

        IEnumerator CooksLeave()
        {
            var walks = new Coroutine[diners.Length];
            for (int i = 0; i < diners.Length; i++)
                if (diners[i] != null)
                    _walks.Add(walks[i] = StartCoroutine(Leave(i, i * 1.2f)));
            foreach (var w in walks)
                if (w != null)
                    yield return w;
            _walks.Clear();
            cooks.SetActive(false);
            for (int i = 0; i < diners.Length; i++)
                Reseat(i);
            _cooksMove = null;
        }

        IEnumerator Leave(int i, float delay)
        {
            var a = diners[i];
            if (delay > 0f)
                yield return new WaitForSeconds(delay);
            var routine = a.GetComponent<NpcRoutine>();
            if (routine != null)
                routine.enabled = false;
            // StandUp chạy với thân không hạ: khung đầu ngồi đúng ghế, khung cuối đứng chân chạm sàn.
            Stand(i);
            a.Play("StandUp", 0.1f);
            var behind = Behind(i);
            for (float t = 0f; t < StandSeconds; t += Time.deltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.9f, 2.2f, t));
                a.TeleportTo(Vector3.Lerp(_seat[i], behind, k), _yaw[i]);
                yield return null;
            }
            a.avoidStatic = false;
            yield return a.WalkTo(new Vector3(behind.x, behind.y, dinerExit[0].position.z));
            a.avoidStatic = true;
            for (int k = 1; k < dinerExit.Length; k++)
            {
                yield return WalkSure(a, dinerExit[k].position);
            }
            // Ra tới boong là khuất: tắt thân để người sau đi tới cùng điểm không bị chắn.
            a.Play("Idle");
            a.SetBodyActive(false);
        }
    }

}
