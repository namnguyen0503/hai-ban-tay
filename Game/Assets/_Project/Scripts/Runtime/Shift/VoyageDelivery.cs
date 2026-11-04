using HaiBanTay.Core;
using HaiBanTay.Interaction;
using HaiBanTay.Npc;
using HaiBanTay.Player;
using HaiBanTay.Ship;
using HaiBanTay.Tasks;
using UnityEngine;

namespace HaiBanTay.Shift
{
    /// <summary>Đồ thật đi từ hầm/két/bếp tới nơi dùng. Xô phải rót; các món khác đặt xuống bằng E.</summary>
    public sealed class VoyageDelivery : WorkStation
    {
        public string orderKind;
        public Holdable load;
        public TaskTarget destination;
        public Transform home, placed;
        public Transform waterSurface;
        public GameObject servedTable;
        public NpcAgent recipient;
        public bool pour;
        public bool fromHold;
        [Tooltip("Câu Ba nói lần đầu nhấc món hàng (một lần mỗi cảnh), ví dụ khối đá trong hầm.")]
        public string pickupLine;
        bool _pickupSaid;
        public Vector3 pickupStand, deliveryStand;
        public override string Kind => orderKind;
        public int Pending { get; private set; }
        public int Delivered { get; private set; }
        public float Progress { get; private set; }
        public bool Placed { get; private set; }
        GameRoot _root;
        PlayerRig _rig;
        Transform _leftGrip, _rightGrip;
        LineRenderer _stream;
        bool _pouring;

        protected override void Awake()
        {
            base.Awake();
            load.Locked = true;
        }
        protected override void OnActivated()
        {
            _root = GameRoot.Instance; _rig = PlayerRig.Current;
            if (!pour) return;
            _leftGrip = new GameObject("WaterLeftGrip").transform; _leftGrip.SetParent(load.transform, false);
            _rightGrip = new GameObject("WaterRightGrip").transform; _rightGrip.SetParent(load.transform, false);
            _leftGrip.localPosition = new Vector3(-0.15f, 0.38f, 0f);
            _rightGrip.localPosition = new Vector3(0.15f, 0.38f, 0f);
            _leftGrip.localRotation = HandsRig.SideGrip(true); _rightGrip.localRotation = HandsRig.SideGrip(false);
            _stream = new GameObject("WaterPourStream").AddComponent<LineRenderer>();
            _stream.transform.SetParent(destination.transform, false); _stream.useWorldSpace = true;
            _stream.sharedMaterial = waterSurface.GetComponent<Renderer>().sharedMaterial;
            _stream.renderingLayerMask = WorldRenderingLayers.Interior;
            _stream.widthMultiplier = 0.015f; _stream.positionCount = 3; _stream.enabled = false;
        }
        void PourHands(bool on)
        {
            if (on == _pouring) return;
            _pouring = on;
            if (on)
            {
                // Chỉ bước lại khi đứng xa chỗ rót (tốc độ đi bộ, giữ hướng nhìn); đứng gần rồi thì không đụng tới người.
                var flat = _rig.Position - deliveryStand; flat.y = 0f;
                if (flat.magnitude > 0.45f)
                    _rig.StepToward(deliveryStand);
            }
            _rig.look.Locked = on;
            _rig.hands.SetWorkGrips(on ? _leftGrip : null, on ? _rightGrip : null);
            _rig.hands.SetPose(on ? HandPose.Carry : HandPose.Lowered);
            _stream.enabled = on;
        }
        public override void OnOrder(int count)
        {
            Pending += count;
            ResetLoad();
        }
        void ResetLoad()
        {
            Placed = false; Progress = 0f;
            load.gameObject.SetActive(true);
            load.Detach(home.position, home.rotation, false);
            load.Locked = Pending == 0;
        }
        public override string PromptFor(TaskTarget target, Interactor who)
        {
            if (target != destination || Pending == 0) return null;
            if (who.Holding == load) return "prompt.story_place";
            return Placed && pour ? "prompt.story_pour" : null;
        }
        public override bool CanInteract(TaskTarget target, Interactor who) => target == destination && Pending > 0 && who.Holding == load;
        public override void OnInteract(TaskTarget target, Interactor who)
        {
            if (!CanInteract(target, who)) return;
            who.ReleaseHeld();
            load.Detach(placed.position, placed.rotation, false);
            load.Locked = true; load.SetCollidersEnabled(false);
            Placed = true;
            if (!pour) FinishDelivery();
        }
        protected override void Update()
        {
            base.Update();
            if (_rig == null || _root == null || State != TaskState.Active) return;
            if (!_pickupSaid && load.IsHeld && !string.IsNullOrEmpty(pickupLine))
            {
                _pickupSaid = true;
                _root.Subtitles.Play(pickupLine);
            }
            if (waterSurface != null && load.IsHeld)
            {
                var ship = ShipMotion.Current;
                waterSurface.localRotation = Quaternion.Euler(0f, 0f, ship != null ? -ship.Roll * 2f : 0f);
            }
            if (!Placed || !pour || Pending == 0) return;
            // Đã bắt đầu rót thì giữ chuột là đủ: camera khóa và tư thế rót đổi mắt nên tia nhìn có thể trượt khỏi nồi; bắt nhìn lại
            // mỗi khung thì trạng thái rót đảo liên tục và hình nhấp nháy.
            bool held = _root.Input.UseHeld && (_pouring || _rig.interactor.IsLookingAt(destination));
            PourHands(held);
            if (held)
            {
                Progress = Mathf.Min(1f, Progress + Time.deltaTime / 2f);
                load.transform.rotation = placed.rotation * Quaternion.Euler(0f, 0f, Progress * 65f);
                var lip = load.transform.TransformPoint(new Vector3(-0.14f, 0.3f, 0f));
                var end = destination.transform.position + Vector3.up * 0.18f;
                _stream.SetPosition(0, lip); _stream.SetPosition(1, Vector3.Lerp(lip, end, 0.5f)); _stream.SetPosition(2, end);
                _root.UI.Prompt.SetProgress(Progress);
                ReportProgress();
                if (Progress >= 1f) FinishDelivery();
            }
        }
        void FinishDelivery()
        {
            Pending--; Delivered++;
            if (pour) PourHands(false);
            BodyState.Current?.Exert(0.015f);
            if (servedTable != null) servedTable.SetActive(true);
            if (recipient != null) { recipient.PickUp(load.transform); recipient.Play("HoldCarry"); }
            _root?.UI.Prompt.SetProgress(null);
            _root?.Audio.PlaySfx(pour ? "SFX/water_wash" : "SFX/metal_clank", 0.3f);
            Work();
            if (Pending > 0) ResetLoad();
            else if (pour) load.gameObject.SetActive(false);
        }
        protected override void OnCompleted()
        {
            if (pour && _rig != null && _stream != null) PourHands(false);
        }
        public override bool Engaged(Interactor who) => who.Holding == load || (Placed && pour && Pending > 0);

        public override bool Guide(Interactor who, out Transform target, out string key)
        {
            if (Pending == 0)
                return Step(null, null, out target, out key);
            if (who.Holding == load)
                return Step(destination, "guide.put." + orderKind, out target, out key);
            if (Placed && pour)
                return Step(destination, "guide.pour_water", out target, out key);
            return Step(load, "guide.take." + orderKind, out target, out key);
        }
        public override void SimulateWork() { if (Pending > 0) FinishDelivery(); }
    }
}
