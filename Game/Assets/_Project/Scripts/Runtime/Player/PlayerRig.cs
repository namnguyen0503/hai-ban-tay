using System.Collections;
using HaiBanTay.Interaction;
using Unity.Cinemachine;
using UnityEngine;

namespace HaiBanTay.Player
{
    /// <summary>Gom tham chiếu của người chơi trong cảnh (do builder gán) và các thao tác dàn cảnh chung.</summary>
    public sealed class PlayerRig : MonoBehaviour
    {
        public static PlayerRig Current { get; private set; }

        public FirstPersonMotor motor;
        public LookController look;
        public Interactor interactor;
        public HandsRig hands;
        public PlayerBody body;
        public Transform head;
        public Camera cam;
        public CinemachineCamera vcam;
        public CinemachineBasicMultiChannelPerlin noise;
        public ParticleSystem breath;
        [Tooltip("Đèn fill không bóng, tầm ngắn, chỉ bật trong khung hai bàn tay (B1.2/P3/E2).")]
        public Light handsFill;

        bool _controlEnabled = true;
        Performance.LightBudget _budget;
        bool _budgetSearched;
        CharacterController _cc;
        bool _moving;
        int _moveFrame;

        /// <summary>Hệ số rung camera do mệt (BodyState đặt); nhân thêm cài đặt chống say.</summary>
        public float ShakeScale { get; set; } = 1f;

        public bool ControlEnabled => _controlEnabled;
        /// <summary>Đang MoveTo (ngồi ghế, bước lên ghế…): camera dời theo phiên chuyển.</summary>
        public bool Moving => _moving;
        public Vector3 Position => transform.position;

        public void SetOutfit(TrangPhuc outfit) => body.SetOutfit(outfit);

        void OnEnable()
        {
            Current = this;
            FirstPersonMotor.Teleported += EndStanceHere;
        }

        void OnDisable()
        {
            if (Current == this)
                Current = null;
            FirstPersonMotor.Teleported -= EndStanceHere;
            EndMove();
            EndStanceHere();
        }

        /// <summary>MoveTo bị dừng giữa chừng (StopCoroutine, đổi lượt, tắt cảnh) thì trả lại CharacterController (QA-0003).</summary>
        void LateUpdate()
        {
            if (_moving && Time.frameCount - _moveFrame > 1)
                EndMove();
            KeepStance();
        }

        void EndMove()
        {
            if (!_moving)
                return;
            _moving = false;
            if (_cc != null)
                _cc.enabled = true;
        }

        /// <summary>Bật/tắt di chuyển, nhìn và tương tác (đoạn dàn cảnh).</summary>
        public void SetControl(bool enabled)
        {
            _controlEnabled = enabled;
            motor.MovementLocked = !enabled;
            look.Locked = !enabled;
            interactor.enabled = enabled;
            if (!enabled)
                interactor.ClearPrompt();
        }

        public void SetMovement(bool enabled)
        {
            motor.MovementLocked = !enabled;
        }

        public void SetNoise(float amplitude)
        {
            if (noise != null)
                noise.AmplitudeGain = amplitude * ShakeScale * Core.GameSettings.Current.CameraShake;
        }

        public void SetBreath(bool on)
        {
            if (breath == null)
                return;
            if (on && !breath.isPlaying)
                breath.Play();
            else if (!on && breath.isPlaying)
                breath.Stop();
        }

        /// <summary>
        /// Mặc định hai tay ẩn (17/10, người dùng: "làm bếp ẩn tay cho đỡ lỗi"; ArmsVisibility chỉ tắt phần vẽ). Test dò tay hay khi làm
        /// lại tay thì đặt true để tay hiện như trước.
        /// </summary>
        public static bool ArmsShownByDefault;
        /// <summary>Đang vẽ hai tay (bàn tay góc nhìn thứ nhất, cánh tay thân): chỉ lúc giơ hai bàn tay (WaitForRaise → khung hai bàn tay).</summary>
        public bool ArmsShown { get; set; } = ArmsShownByDefault;

        /// <summary>Đưa camera và đôi tay về khung hình "hai bàn tay" (HandsShotPose).</summary>
        public IEnumerator EnterHandsShot(HandsShotPose pose) => EnterHandsShot(pose, false);

        /// <summary>prologue: khung riêng của P3 (cúi ít hơn, hai tay nâng cao hơn) để thấy mặt anh Lê; mặc định giữ nguyên cho B1.2 và E2.</summary>
        public IEnumerator EnterHandsShot(HandsShotPose pose, bool prologue)
        {
            SetControl(false);
            ArmsShown = true;
            hands.SetOverride(prologue ? pose.PrologueTarget : pose.Target);
            SetHandsFill(true);
            float pitch = prologue ? pose.prologuePitch : pose.pitch;
            float startPitch = look.Pitch;
            float seconds = Mathf.Max(0.01f, pose.blendSeconds);
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, t / seconds);
                look.SetAngles(look.Yaw, Mathf.Lerp(startPitch, pitch, k));
                yield return null;
            }
            look.SetAngles(look.Yaw, pitch);
        }

        public void ExitHandsShot()
        {
            hands.SetPose(HandPose.Lowered);
            ArmsShown = ArmsShownByDefault;
            SetHandsFill(false);
            look.SetAngles(look.Yaw, 0f);
            SetControl(true);
        }

        /// <summary>Bật đèn fill tay trong ngân sách PLAN 3.3: LightBudget nhường một suất đèn không bóng nếu đã đủ 3 đèn.</summary>
        void SetHandsFill(bool on)
        {
            if (handsFill == null || handsFill.gameObject.activeSelf == on)
                return;
            if (!_budgetSearched)
            {
                _budget = FindAnyObjectByType<Performance.LightBudget>();
                _budgetSearched = true;
            }
            if (on)
            {
                if (_budget != null) _budget.Lend();
                handsFill.gameObject.SetActive(true);
            }
            else
            {
                handsFill.gameObject.SetActive(false);
                if (_budget != null) _budget.Restore();
            }
        }

        /// <summary>Di chuyển mượt tới một điểm (ngồi ghế, bước lên ghế) với điều khiển bị khóa.</summary>
        public IEnumerator MoveTo(Vector3 position, float yaw, float pitch, float seconds)
        {
            _cc ??= motor.GetComponent<CharacterController>();
            _cc.enabled = false;
            _moving = true;
            _moveFrame = Time.frameCount;
            var startPos = transform.position;
            float startYaw = look.Yaw, startPitch = look.Pitch;
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, t / seconds);
                transform.position = Vector3.Lerp(startPos, position, k);
                look.SetAngles(Mathf.LerpAngle(startYaw, yaw, k), Mathf.Lerp(startPitch, pitch, k));
                _moveFrame = Time.frameCount;
                yield return null;
            }
            transform.position = position;
            look.SetAngles(yaw, pitch);
            EndMove();
        }

        /// <summary>
        /// Bước lại gần chỗ làm việc (theo phương ngang, tối đa <paramref name="maxStep"/> m) ở tốc độ đi bộ, dừng ở vật cản đầu
        /// tiên. Không đổi hướng nhìn, không đổi chiều cao mắt: camera đi theo người như khi người chơi tự bước (17/10: game không được
        /// dịch chuyển tức thời hay tự xoay camera vì tư thế làm việc).
        /// </summary>
        public Coroutine StepToward(Vector3 position, float maxStep = 1.2f)
        {
            if (_step != null) StopCoroutine(_step);
            _step = StartCoroutine(Step(position, maxStep));
            return _step;
        }

        Coroutine _step;
        const float StepSpeed = 1.3f;

        /// <summary>
        /// Bước sát chỗ làm việc (bụng chạm mép bàn, chậu, bục) ở tốc độ đi bộ, giữ hướng nhìn và chiều cao mắt. Capsule va chạm rộng
        /// hơn thân thật ~16 cm (bán kính 0,28 + skin) nên StepToward dừng quá xa với tay đứng thẳng: lúc làm việc tắt
        /// CharacterController; tay rời điểm cầm StanceRelease giây thì tự lùi về chỗ cũ rồi bật lại.
        /// </summary>
        public Coroutine StepToWork(Vector3 position) => StepToWork(position, -1f);

        /// <summary>Như StepToWork nhưng bước trong đúng <paramref name="seconds"/> giây (người đi cùng nhịp với vật đang nhấc, đẩy).</summary>
        public Coroutine StepToWork(Vector3 position, float seconds)
        {
            _cc ??= motor.GetComponent<CharacterController>();
            if (!_inStance)
            {
                _stanceHome = transform.position;
                _inStance = true;
            }
            _stanceIdle = 0f;
            if (_step != null) StopCoroutine(_step);
            _step = StartCoroutine(Glide(position, false, seconds));
            return _step;
        }

        /// <summary>
        /// Đang đứng sát chỗ làm việc: dời người theo vật đang nhấc/đẩy trong khung này (trạm gọi mỗi khung, quãng nhỏ ở tốc độ đi bộ), giữ
        /// hướng nhìn và chiều cao mắt; tay không phải chờ như khi đang bước (Stepping).
        /// </summary>
        public void MoveInStance(Vector3 position)
        {
            if (!_inStance)
                return;
            if (_step != null) { StopCoroutine(_step); _step = null; }
            position.y = transform.position.y;
            transform.position = position;
            _stanceAt = position;
            _stanceIdle = 0f;
        }

        /// <summary>Đang đứng sát chỗ làm việc (CharacterController tắt).</summary>
        public bool InWorkStance => _inStance;

        bool _inStance;
        Vector3 _stanceHome, _stanceAt;
        float _stanceIdle;
        const float StanceRelease = 1.2f;

        IEnumerator Glide(Vector3 position, bool leaving, float fixedSeconds = -1f)
        {
            _cc.enabled = false;
            var start = transform.position;
            position.y = start.y;
            float seconds = fixedSeconds > 0f ? fixedSeconds : Mathf.Clamp(Vector3.Distance(start, position) / StepSpeed, 0.12f, 0.9f);
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                transform.position = Vector3.Lerp(start, position, Mathf.SmoothStep(0f, 1f, t / seconds));
                _stanceAt = transform.position;
                yield return null;
            }
            transform.position = position;
            _stanceAt = position;
            _step = null;
            if (leaving)
            {
                _inStance = false;
                if (!_moving) _cc.enabled = true;
            }
        }

        void KeepStance()
        {
            if (!_inStance || _step != null)
                return;
            // Bị đặt chỗ khác (cảnh truyện, MoveTo): thôi đứng sát, không kéo về chỗ cũ.
            if ((transform.position - _stanceAt).sqrMagnitude > 0.05f * 0.05f)
            {
                EndStanceHere();
                return;
            }
            _stanceIdle = hands != null && hands.Working ? 0f : _stanceIdle + Time.deltaTime;
            if (_stanceIdle > StanceRelease)
                _step = StartCoroutine(Glide(_stanceHome, true));
        }

        /// <summary>
        /// Thôi đứng sát chỗ làm việc ngay tại chỗ (bật lại CharacterController ở đây, không lùi về chỗ cũ): trạm đã dời vật vào chỗ người
        /// đứng lúc trước (nồi cao nhấc xuống đúng chỗ Ba đứng chờ), lùi về đó thì vỏ va chạm chồng lên vật và bị đẩy lên nóc.
        /// </summary>
        public void EndWorkStanceHere() => EndStanceHere();

        void EndStanceHere()
        {
            if (!_inStance)
                return;
            if (_step != null) { StopCoroutine(_step); _step = null; }
            _inStance = false;
            if (_cc != null && !_moving) _cc.enabled = true;
        }

        /// <summary>Đang bước lại gần chỗ làm việc (StepToward): tay chưa bám điểm cầm của trạm, bước xong mới với tới.</summary>
        public bool Stepping => _step != null;

        IEnumerator Step(Vector3 position, float maxStep)
        {
            _cc ??= motor.GetComponent<CharacterController>();
            var flat = position - transform.position;
            flat.y = 0f;
            var move = PourMotion.ClearStep(_cc, Vector3.ClampMagnitude(flat, maxStep));
            float seconds = Mathf.Clamp(move.magnitude / StepSpeed, 0.12f, 0.9f);
            var done = Vector3.zero;
            for (float t = 0f; t < seconds && move.sqrMagnitude > 1e-6f; t += Time.deltaTime)
            {
                var want = move * Mathf.SmoothStep(0f, 1f, t / seconds);
                if (_cc != null && _cc.enabled) _cc.Move(want - done); else transform.position += want - done;
                done = want;
                yield return null;
            }
            if (_cc != null && _cc.enabled) _cc.Move(move - done); else transform.position += move - done;
            _step = null;
        }
    }
}
