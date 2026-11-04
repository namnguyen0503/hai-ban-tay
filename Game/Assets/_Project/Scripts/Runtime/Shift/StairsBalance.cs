using System.Collections;
using HaiBanTay.Core;
using HaiBanTay.Interaction;
using HaiBanTay.Player;
using HaiBanTay.Ship;
using UnityEngine;

namespace HaiBanTay.Shift
{
    /// <summary>
    /// Thang hầm 14 bậc giữa boong giếng trước và hầm thực phẩm (F05): vác bao trên thang khi tàu chòng chành thì bao nghiêng theo tàu
    /// (BalanceLogic); rê chuột ngang ngược chiều nghiêng để đỡ, chuột ngang lúc này xoay camera rất ít. Bao nghiêng hết
    /// mức thì tuột khỏi tay, lăn xuống chân cầu thang; người loạng choạng chứ không ngã. Tàu êm thì không cần giữ.
    /// </summary>
    public sealed class StairsBalance : MonoBehaviour
    {
        public PlayerArea stairs;
        public Transform slipSpot;
        public string carryKind = "sack";
        [Tooltip("Tàu lắc từ mức này trở lên mới phải giữ thăng bằng.")]
        public float minIntensity = 0.25f;
        public float yawScale = 0.25f;
        public float tiltDegrees = 22f;

        GameRoot _root;
        PlayerRig _rig;
        Transform _hold;
        Quaternion _holdRot;
        bool _active;
        bool _hinted;

        public BalanceLogic Logic { get; } = new BalanceLogic();
        public bool Active => _active;

        void Start()
        {
            _root = GameRoot.Instance;
            _rig = PlayerRig.Current;
            if (_rig != null)
            {
                _hold = _rig.interactor.holdTwoHands;
                if (_hold != null)
                    _holdRot = _hold.localRotation;
            }
        }

        void Update()
        {
            if (_rig == null || _root == null || stairs == null)
                return;
            var ship = ShipMotion.Current;
            var held = _rig.interactor.Holding;
            bool active = held != null && held.kind == carryKind && stairs.PlayerInside &&
                          ship != null && ship.Intensity >= minIntensity && _rig.ControlEnabled;
            if (active != _active)
                SetActive(active);
            if (!active)
                return;
            float roll = ship.maxRollDegrees > 0f ? ship.Roll / ship.maxRollDegrees : 0f;
            float dx = _root.Input.GameplayEnabled ? _root.Input.LookRaw.x : 0f;
            if (Logic.Tick(roll, dx, Time.deltaTime))
            {
                Slip(held);
                return;
            }
            _root.UI.Shift.SetBalance(Logic.Lean);
            if (_hold != null)
                _hold.localRotation = _holdRot * Quaternion.Euler(0f, 0f, -Logic.Lean * tiltDegrees);
        }

        void SetActive(bool active)
        {
            _active = active;
            Logic.Reset();
            _rig.look.YawScale = active ? yawScale : 1f;
            if (_hold != null)
                _hold.localRotation = _holdRot;
            _root.UI.Shift.SetBalance(active ? 0f : (float?)null);
            if (active && !_hinted)
            {
                _hinted = true;
                _root.UI.Prompt.ShowHint(_root.Content.GetString("hint.balance"), 7f);
            }
        }

        void Slip(Holdable held)
        {
            SetActive(false);
            var h = _rig.interactor.ReleaseHeld();
            if (h != null && slipSpot != null)
                h.Detach(slipSpot.position, slipSpot.rotation, physics: true);
            _root.Audio.PlaySfx("SFX/sack_thud");
            BodyState.Current?.Exert(0.01f);
            StartCoroutine(Stumble());
        }

        /// <summary>Loạng choạng: cúi đầu nhìn xuống, đứng khựng nửa giây.</summary>
        IEnumerator Stumble()
        {
            _rig.motor.MovementLocked = true;
            float yaw = _rig.look.Yaw, pitch = _rig.look.Pitch;
            for (float t = 0f; t < 0.5f; t += Time.deltaTime)
            {
                _rig.look.SetAngles(yaw, pitch + Mathf.Sin(t / 0.5f * Mathf.PI) * 14f);
                yield return null;
            }
            _rig.look.SetAngles(yaw, pitch);
            _rig.motor.MovementLocked = false;
        }

        void OnDisable()
        {
            if (_active && _rig != null)
            {
                _rig.look.YawScale = 1f;
                if (_hold != null)
                    _hold.localRotation = _holdRot;
            }
            _active = false;
        }
    }
}
