using System.Collections.Generic;
using HaiBanTay.Core;
using UnityEngine;

namespace HaiBanTay.Player
{
    /// <summary>
    /// Ngồi nghỉ (phím C): ngồi bệt xuống sàn tại chỗ, hạ tầm mắt, đứng yên; thể lực hồi nhanh hơn đứng nghỉ nhưng đồng hồ ca
    /// vẫn chạy. Nhấn C lần nữa hoặc bấm đi thì đứng dậy. Chỉ ngồi được khi tay không và không đang làm việc khóa chân
    /// (đứng trên ghế cọ nồi, cảnh dàn dựng).
    /// </summary>
    public sealed class SitController : MonoBehaviour
    {
        /// <summary>Tầm mắt khi ngồi bệt thấp hơn khi đứng (m).</summary>
        public float seatedDrop = 0.78f;

        PlayerRig _rig;
        GameRoot _root;
        string _shown;
        CharacterController _controller;
        Vector3 _standingPosition;
        float _standingYaw;
        public bool StudySeated { get; private set; }
        Animator _bodyAnimator;
        RuntimeAnimatorController _bodyController;
        AnimatorOverrideController _studyController;

        public bool Seated { get; private set; }

        void Start()
        {
            _rig = GetComponent<PlayerRig>();
            _root = GameRoot.Instance;
            _controller = GetComponent<CharacterController>();
        }

        void Update()
        {
            if (_rig == null || _root == null)
                return;
            if (StudySeated) return;
            var input = _root.Input;
            bool pressed = input.SitPressed;
            if (Seated)
            {
                if (pressed || input.Move.sqrMagnitude > 0.04f || !input.GameplayEnabled)
                    Stand();
            }
            else if (pressed && CanSit())
            {
                Sit();
            }
            ShowPrompt(Seated ? "prompt.stand_up" : null);
        }

        bool CanSit() =>
            _rig.interactor.HandsFree && !_rig.motor.MovementLocked && Mathf.Approximately(_rig.motor.ExtraHeadHeight, 0f);

        public void Sit()
        {
            Seated = true;
            _rig.motor.MovementLocked = true;
            _rig.motor.ExtraHeadHeight = -seatedDrop;
            // Ngồi thì không với tới đồ: tắt nhắm tương tác.
            _rig.interactor.ClearPrompt();
            _rig.interactor.enabled = false;
            if (BodyState.Current != null)
                BodyState.Current.Logic.Seated = true;
        }

        /// <summary>Ngồi ghế học: giữ trạng thái ngồi xuyên các màn bài học, đứng lên khi StudyTask kết thúc.</summary>
        /// <summary>
        /// Clip ngồi ghế trên thân Ba: hông ở sau gốc 0,23 m (thân lùi sau mắt) và cao 0,44 m trên gốc. Gốc đặt trước tâm ghế
        /// SeatForward, cao hơn sàn SeatLift thì hông ngồi giữa-sau mặt ghế 0,45 m, đùi trên ván, bắp chân thả trước mép ghế; bàn chân
        /// vẫn bám sàn. Đặt gốc ở tâm ghế thì bắp chân xuyên ván ghế 6,8 cm (PlayerClipProbe, ClipFit17.SoupSeat, 17/10).
        /// </summary>
        public const float SeatForward = 0.19f, SeatLift = 0.06f;

        public void SitForStudy(Transform chair, Vector3 lookAt, AnimationClip chairClip = null)
        {
            if (_rig == null) _rig = GetComponent<PlayerRig>();
            if (_controller == null) _controller = GetComponent<CharacterController>();
            Stand();
            _bodyAnimator = _rig.GetComponentInChildren<PlayerBody>().animator;
            if (chairClip != null)
            {
                // Dùng clip ngồi ghế, không sửa controller/thân của lượt trang phục.
                _bodyController = _bodyAnimator.runtimeAnimatorController;
                _studyController = new AnimatorOverrideController(_bodyController);
                var overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>();
                _studyController.GetOverrides(overrides);
                for (int i = 0; i < overrides.Count; i++)
                    if (overrides[i].Key.name == "Squat")
                        overrides[i] = new KeyValuePair<AnimationClip, AnimationClip>(overrides[i].Key, chairClip);
                _studyController.ApplyOverrides(overrides);
                _bodyAnimator.runtimeAnimatorController = _studyController;
            }
            _standingPosition = _rig.Position;
            _standingYaw = _rig.look.Yaw;
            var d = lookAt - chair.position - Vector3.up * 1.12f;
            float yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            float pitch = -Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg;
            _rig.motor.Teleport(chair.position + Quaternion.Euler(0f, yaw, 0f) * Vector3.forward * SeatForward + Vector3.up * SeatLift, yaw);
            _controller.enabled = false;
            Seated = StudySeated = true;
            _rig.motor.MovementLocked = true;
            _rig.motor.ExtraHeadHeight = -0.5f;
            _rig.look.SetAngles(yaw, pitch);
            _rig.interactor.ClearPrompt();
            _rig.interactor.enabled = false;
            if (BodyState.Current != null) BodyState.Current.Logic.Seated = true;
        }

        public void Stand()
        {
            if (!Seated)
                return;
            if (StudySeated)
            {
                if (_studyController != null)
                {
                    _bodyAnimator.runtimeAnimatorController = _bodyController;
                    Destroy(_studyController); _studyController = null;
                }
                _controller.enabled = true;
                _rig.motor.Teleport(_standingPosition, _standingYaw);
                StudySeated = false;
            }
            Seated = false;
            _rig.motor.MovementLocked = false;
            _rig.motor.ExtraHeadHeight = 0f;
            _rig.interactor.enabled = true;
            if (BodyState.Current != null)
                BodyState.Current.Logic.Seated = false;
        }

        void OnDisable()
        {
            Stand();
            ShowPrompt(null);
        }

        void ShowPrompt(string key)
        {
            if (key == _shown || _root == null || _root.UI == null)
                return;
            _shown = key;
            _root.UI.Prompt.SetWorldPrompt(key != null ? _root.Content.GetString(key) : null);
        }
    }
}
