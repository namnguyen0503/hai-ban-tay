using System;
using System.Collections;
using HaiBanTay.Player;
using UnityEngine;

namespace HaiBanTay.Interaction
{
    /// <summary>
    /// Đổ vật đang cầm (sọt than vào thùng, xô tro vào máng): đưa miệng vật tới trên chỗ đổ, nghiêng dần từ thẳng đứng tới
    /// úp ngược 180° (lật ra phía trước, xa người; hoặc lật sang trái để vật to không che hết tầm nhìn), giữ một nhịp cho
    /// đồ rơi hết rồi đưa về tay. Tay bám vành theo vật; xô xách quai thì tay phải nắm quai nhấc xô lên trước người, trong lúc nhấc
    /// hai bàn tay trượt dần sang hai bên vành ngay từ lúc nhấc (một tay nắm quai không lật úp được xô), lật xong trượt về quai và chỗ thả lỏng rồi mới hạ
    /// xô. Đứng xa chỗ đổ thì người bước lại gần trong lúc nhấc vật; vật luôn nằm trong tầm với của hai vai và không vào sát mặt (ghi chú
    /// 15/10: đổ tro, cổ tay xương cách bàn tay 69 cm, vật bay ra một mình). Trong lúc đổ góc nhìn khóa, E không thả vật.
    /// </summary>
    public static class PourMotion
    {
        public const float MoveSeconds = 0.35f, BackSeconds = 0.4f, HoldSeconds = 0.3f;
        /// <summary>Xô xách quai: thêm chừng này lúc đưa về cho hai tay trượt từ vành về quai và chỗ thả lỏng.</summary>
        public const float HandOverSeconds = 0.25f;
        /// <summary>Chỗ bám cách vai tối đa phần này của dài cánh tay.</summary>
        public const float ReachShare = 0.94f;
        /// <summary>Tâm vật cách trục thân ít nhất chừng này theo hướng nhìn (m), không lùi vào ngực.</summary>
        public const float MinFront = 0.42f;
        /// <summary>Giữa thân vật cách mắt ít nhất bề ngang vật cộng chừng này (m).</summary>
        public const float EyeGap = 0.3f;
        /// <summary>Bước lại gần chỗ đổ tối đa (m).</summary>
        public const float MaxStep = 0.9f;

        /// <param name="rim">Tâm miệng vật (tọa độ của vật).</param>
        /// <param name="mouth">Điểm thế giới miệng vật tới khi úp hẳn.</param>
        /// <param name="tiltSeconds">Thời gian nghiêng 0 → 180°.</param>
        /// <param name="onTilt">Gọi mỗi khung lúc nghiêng với góc 0–180 (đồ bắt đầu rơi từ khoảng 90°).</param>
        /// <param name="sideways">Lật sang trái (quanh trục nhìn) thay vì ra trước.</param>
        /// <param name="reach">Miệng vật không ra xa người quá chừng này (theo phương ngang) để tay còn bám tới.</param>
        public static IEnumerator Run(Holdable h, PlayerRig rig, Vector3 rim, Vector3 mouth, float tiltSeconds, Action<float> onTilt = null,
            bool sideways = false, float reach = float.PositiveInfinity)
        {
            if (h == null || rig == null)
                yield break;
            h.Busy = true;
            // Vật có mốc miệng thật (BuilderUtils.PourRimMarkers) thì dùng mốc thay cho miệng truyền vào.
            var rimMark = h.transform.Find("PourRim");
            if (rimMark != null)
                rim = rimMark.localPosition;
            bool hang = h.hang && h.hangGrip != null && rig.hands != null;
            var (left, right) = Grips(h, rim);
            Vector3 leftRim = left != null ? left.localPosition : Vector3.zero, rightRim = right != null ? right.localPosition : Vector3.zero;
            Quaternion leftRimRot = left != null ? left.localRotation : Quaternion.identity, rightRimRot = right != null ? right.localRotation : Quaternion.identity;
            (Vector3 pos, Quaternion rot) leftFrom = default, rightFrom = default;
            bool onRim = !hang;
            // Chỗ bám (tọa độ vật) dùng để tính tầm với: cố định suốt lúc đổ, kể cả khi bàn tay đang trượt giữa quai và vành.
            Vector3? leftAt = left != null ? h.transform.InverseTransformPoint(left.position) : (Vector3?)null;
            Vector3? rightAt = right != null ? h.transform.InverseTransformPoint(right.position) : (Vector3?)null;
            bool moveLocked = rig.motor.MovementLocked, lookLocked = rig.look.Locked;
            rig.motor.MovementLocked = true;
            rig.look.Locked = true;
            var startPos = h.transform.position;
            var startRot = h.transform.rotation;
            // Chỗ cầm theo người: người bước lại gần chỗ đổ thì lúc đưa về vật về đúng tay, không về chỗ cũ trong thế giới (xô tro về sau
            // lưng 0,9 m, cổ tay hở 7 cm).
            var homeLocal = rig.transform.InverseTransformPoint(startPos);
            var homeRotLocal = Quaternion.Inverse(rig.transform.rotation) * startRot;
            var upright = Quaternion.Euler(0f, rig.transform.eulerAngles.y, 0f);
            var axis = sideways ? rig.transform.forward : rig.transform.right;
            float radius = SideRadius(h);
            var controller = rig.GetComponent<CharacterController>();
            var step = ClearStep(controller, StepIn(h, rig, rim, mouth, axis, upright, leftAt, rightAt, radius));
            var stepped = Vector3.zero;
            var flat = mouth - (rig.transform.position + step);
            flat.y = 0f;
            if (flat.magnitude > reach)
                mouth -= flat - flat.normalized * reach;
            // Miệng vật đứng trên chỗ đổ, thẳng đứng, trước khi nghiêng.
            var above = mouth + Vector3.up * 0.18f;
            Vector3 PosFor(Quaternion rot, Vector3 rimAt) => Reach(h, rig, rimAt - rot * rim, rot, rim, leftAt, rightAt, radius);

            for (float t = 0f; t < MoveSeconds; t += Time.deltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, t / MoveSeconds);
                if (controller != null && step.sqrMagnitude > 0f)
                {
                    controller.Move(step * k - stepped);
                    stepped = step * k;
                }
                var rot = Quaternion.Slerp(startRot, upright, k);
                h.WorldPose = OutOfBody(h, rig, Vector3.Lerp(startPos, PosFor(upright, above), k), rot);
                if (hang)
                {
                    // Suốt lúc nhấc: hai bàn tay rời quai/chỗ thả lỏng, trượt dần tới hai bên vành. Giữ hướng tay xách quai trong lúc cánh
                    // tay vung ra trước thì cẳng tay phải vặn quá 180° (ClipFit17.AshBucketTrace 17/10, khung 8–15).
                    float j = t / MoveSeconds;
                    if (j > 0f && !onRim)
                    {
                        leftFrom = (rig.hands.left.position, rig.hands.left.rotation);
                        rightFrom = (rig.hands.right.position, rig.hands.right.rotation);
                        TakeRim(rig, left, right);
                        onRim = true;
                    }
                    if (onRim)
                    {
                        Slide(h, left, leftFrom, leftRim, leftRimRot, rot, h.WorldPose.Value.position, j);
                        Slide(h, right, rightFrom, rightRim, rightRimRot, rot, h.WorldPose.Value.position, j);
                    }
                }
                yield return null;
            }
            if (hang)
            {
                if (!onRim)
                {
                    TakeRim(rig, left, right);
                    onRim = true;
                }
                left.SetLocalPositionAndRotation(leftRim, leftRimRot);
                right.SetLocalPositionAndRotation(rightRim, rightRimRot);
            }
            bool twoHand = !hang && h.twoHanded && left != null && right != null;
            // Xô xách cũng trượt tay xuống đáy khi úp: bám vành tới 180° thì bàn tay chĩa ngược về phía người, cẳng tay vặn ngược
            // (đổ xô tro, ClipFit17.AshBucketRim 17/10).
            bool toBase = left != null && right != null && (twoHand || hang);
            float full = toBase ? TwoHandTilt : 180f;
            if (twoHand) Carried[h] = (leftRim, rightRim);
            for (float t = 0f; t < tiltSeconds; t += Time.deltaTime)
            {
                float k = t / tiltSeconds;
                float angle = full * Mathf.SmoothStep(0f, 1f, k);
                var rot = Quaternion.AngleAxis(angle, axis) * upright;
                if (toBase) BaseGrips(h, left, right, leftRim, rightRim, radius, angle);
                h.WorldPose = OutOfBody(h, rig, PosFor(rot, Vector3.Lerp(above, mouth, k)), rot);
                onTilt?.Invoke(angle * 180f / full);
                yield return null;
            }
            var down = Quaternion.AngleAxis(full, axis) * upright;
            if (toBase) BaseGrips(h, left, right, leftRim, rightRim, radius, full);
            h.WorldPose = OutOfBody(h, rig, PosFor(down, mouth), down);
            onTilt?.Invoke(180f);
            yield return new WaitForSeconds(HoldSeconds);
            // Xách quai: lật về đứng thẳng, giữ trên chỗ đổ cho hai tay trượt về quai và chỗ thả lỏng, rồi mới hạ. Ôm hai tay: lật về
            // rồi hạ luôn.
            float back = hang ? BackSeconds + HandOverSeconds : BackSeconds;
            float upAt = hang ? 0.45f : 0.6f, handsAt = hang ? 0.72f : 0.6f;
            for (float t = 0f; t < back; t += Time.deltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, t / back);
                // Lật ngược lại quanh đúng trục cũ (slerp giữa hai góc cách 180° không xác định chiều) về đứng trên chỗ đổ, trong tầm
                // tay; rồi về góc và chỗ cầm.
                Quaternion rot;
                Vector3 pos;
                if (k < upAt)
                {
                    rot = Quaternion.AngleAxis(full * (1f - k / upAt), axis) * upright;
                    if (toBase) BaseGrips(h, left, right, leftRim, rightRim, radius, full * (1f - k / upAt));
                    pos = PosFor(rot, Vector3.Lerp(mouth, above, k / upAt));
                }
                else if (k < handsAt)
                {
                    rot = upright;
                    pos = PosFor(upright, above);
                }
                else
                {
                    float j = (k - handsAt) / (1f - handsAt);
                    rot = Quaternion.Slerp(upright, rig.transform.rotation * homeRotLocal, j);
                    pos = Vector3.Lerp(PosFor(upright, above), rig.transform.TransformPoint(homeLocal), j);
                }
                h.WorldPose = OutOfBody(h, rig, pos, rot);
                pos = h.WorldPose.Value.position;
                if (hang && onRim && k >= upAt)
                {
                    // Tay phải trượt về quai, tay trái về chỗ thả lỏng bên hông; xong thì trả lại tư thế xách.
                    float j = Mathf.InverseLerp(upAt, handsAt, k);
                    var rest = rig.body != null ? rig.body.RestPose(-1f) : (rig.hands.left.position, rig.hands.left.rotation);
                    var handle = h.hangGrip;
                    var handleAt = (pos + rot * Vector3.Scale(h.transform.InverseTransformPoint(handle.position), h.transform.lossyScale),
                        rot * (Quaternion.Inverse(h.transform.rotation) * handle.rotation));
                    Toward(h, left, leftRim, leftRimRot, rot, pos, rest, j);
                    Toward(h, right, rightRim, rightRimRot, rot, pos, handleAt, j);
                    if (k >= handsAt)
                    {
                        RestoreHands(h, rig);
                        onRim = false;
                    }
                }
                yield return null;
            }
            h.WorldPose = null;
            h.Busy = false;
            RestoreGrips(h);
            RestoreHands(h, rig);
            // Đoạn dàn cảnh lấy quyền điều khiển giữa lúc đổ thì giữ nguyên khóa của nó.
            if (rig.ControlEnabled)
            {
                rig.motor.MovementLocked = moveLocked;
                rig.look.Locked = lookLocked;
            }
        }

        /// <summary>
        /// Chỗ hai tay bám khi đổ: vật ôm hai tay dùng sẵn chỗ bám của nó; xô xách quai thì tạo hai chỗ bám hai bên vành (dưới miệng 4 cm,
        /// theo bề ngang thân xô); tay chuyển sang trong lúc nhấc.
        /// </summary>
        static (Transform left, Transform right) Grips(Holdable h, Vector3 rim)
        {
            if (!h.hang || h.hangGrip == null)
                return (h.leftGrip, h.rightGrip);
            float radius = SideRadius(h);
            var left = h.transform.Find(PourGripLeft) ?? new GameObject(PourGripLeft).transform;
            var right = h.transform.Find(PourGripRight) ?? new GameObject(PourGripRight).transform;
            left.SetParent(h.transform, false);
            right.SetParent(h.transform, false);
            var markL = h.transform.Find("PourRimL");
            var markR = h.transform.Find("PourRimR");
            if (markL != null && markR != null)
            {
                left.SetLocalPositionAndRotation(markL.localPosition, markL.localRotation);
                right.SetLocalPositionAndRotation(markR.localPosition, markR.localRotation);
            }
            else
            {
                // Xô xách: tay bám vành từ trên (không như đồ ôm hai bên; lật kiểu SideGrip thì cẳng tay phải vặn ngược −129°, 17/10).
                left.SetLocalPositionAndRotation(new Vector3(-radius, rim.y - 0.04f, 0f), Quaternion.Euler(0f, 0f, 80f));
                right.SetLocalPositionAndRotation(new Vector3(radius, rim.y - 0.04f, 0f), Quaternion.Euler(0f, 0f, -80f));
            }
            return (left, right);
        }

        /// <summary>Đặt chỗ bám (con của vật) giữa tư thế bàn tay lúc rời chỗ cũ (thế giới) và chỗ trên vành (tọa độ vật), theo tư thế vật
        /// khung này (WorldPose chưa áp vào transform).</summary>
        static void Slide(Holdable h, Transform grip, (Vector3 pos, Quaternion rot) from, Vector3 rimLocal, Quaternion rimRot, Quaternion itemRot,
            Vector3 itemPos, float j)
        {
            var scale = h.transform.lossyScale;
            var onRim = itemPos + itemRot * Vector3.Scale(rimLocal, scale);
            var pos = Vector3.Lerp(from.pos, onRim, Mathf.SmoothStep(0f, 1f, j));
            var rot = Quaternion.Slerp(from.rot, itemRot * rimRot, Mathf.SmoothStep(0f, 1f, j));
            grip.localPosition = Vector3.Scale(Quaternion.Inverse(itemRot) * (pos - itemPos), new Vector3(1f / scale.x, 1f / scale.y, 1f / scale.z));
            grip.localRotation = Quaternion.Inverse(itemRot) * rot;
        }

        /// <summary>Ngược với Slide: từ chỗ trên vành trượt dần tới tư thế đích (thế giới).</summary>
        static void Toward(Holdable h, Transform grip, Vector3 rimLocal, Quaternion rimRot, Quaternion itemRot, Vector3 itemPos,
            (Vector3 pos, Quaternion rot) to, float j)
        {
            var scale = h.transform.lossyScale;
            var onRim = itemPos + itemRot * Vector3.Scale(rimLocal, scale);
            var pos = Vector3.Lerp(onRim, to.pos, Mathf.SmoothStep(0f, 1f, j));
            var rot = Quaternion.Slerp(itemRot * rimRot, to.rot, Mathf.SmoothStep(0f, 1f, j));
            grip.localPosition = Vector3.Scale(Quaternion.Inverse(itemRot) * (pos - itemPos), new Vector3(1f / scale.x, 1f / scale.y, 1f / scale.z));
            grip.localRotation = Quaternion.Inverse(itemRot) * rot;
        }

        static readonly System.Collections.Generic.List<Vector3> Shape = new System.Collections.Generic.List<Vector3>();

        /// <summary>Tư thế vật khung này, dời ra trước theo hướng người chơi nếu lọt vào vỏ thân (ngực, bụng, tạp dề, đùi): đường nhấc
        /// và đưa về đi thẳng giữa chỗ cầm và trên chỗ đổ có thể cắt qua thân (ghi chú 15/10: bụng xuyên xô, sọt).</summary>
        static (Vector3, Quaternion) OutOfBody(Holdable h, PlayerRig rig, Vector3 pos, Quaternion rot)
        {
            if (rig.body == null) return (pos, rot);
            h.transform.SetPositionAndRotation(pos, rot);
            // Hai bàn tay bám điểm nắm trên vật suốt lượt đổ: vật phải trong tầm cả hai cánh tay. Kéo vật về phía vai (không dời
            // khung tay, dời khung thì lòng bàn tay rời vật), rồi mới đẩy ra khỏi thân.
            var hands = rig.hands;
            var pull = Vector3.zero;
            float worst = 0f;
            for (int side = 0; side < 2; side++)
            {
                var grip = hands != null ? hands.GripAnchor(side == 0) : null;
                if (grip == null || !grip.IsChildOf(h.transform)) continue;
                var shoulder = rig.body.Shoulder(side == 0).position;
                var toShoulder = shoulder - grip.position;
                float excess = toShoulder.magnitude - rig.body.ArmLength(side == 0) * ReachShare;
                if (excess > worst)
                {
                    worst = excess;
                    pull = toShoulder.normalized * excess;
                }
            }
            if (worst > 0f)
            {
                pos += pull;
                h.transform.position = pos;
            }
            h.ShapePoints(Shape);
            // Đẩy theo phương ngang từ trục thân ra phía vật: sọt ôm trước ngực ra trước, xô đang nhấc từ hông ra ngang/chéo, không kéo
            // xô bên hông ra trước xa tầm tay.
            var center = Vector3.zero;
            foreach (var p in Shape) center += p;
            var axis = Vector3.ProjectOnPlane(center / Mathf.Max(1, Shape.Count) - rig.transform.position, rig.transform.up);
            axis = axis.sqrMagnitude > 1e-4f ? axis.normalized : rig.transform.forward;
            // Vỏ thân lấy mẫu thưa (khớp xương, lưới tạp dề): xô xách nhấc từ hông lướt sát gấu tạp dề, giữ cách thêm PourGap. Đồ ôm
            // trước ngực không thêm (đã sát giới hạn tầm tay).
            float need = rig.body.Clearance(Shape, rig.transform.position, axis, rig.transform.up) + (h.hang ? PourGap : 0f);
            if (need > 0f)
            {
                pos += axis * need;
                h.transform.position = pos; // cùng khung, trước khi Holdable áp WorldPose: không để vật nằm ở chỗ lấy mẫu
            }
            return (pos, rot);
        }

        const float PourGap = 0.03f;
        /// <summary>
        /// Vật đổ bằng hai tay bám thân (sọt ôm, xô xách lúc đổ bám hai bên vành) chỉ nghiêng tới chừng này (đồ đã trượt hết từ 165° quy
        /// đổi): úp hẳn 180° thì đáy sọt lên ngang mặt và nằm giữa hai cẳng tay (ClipFit17.CoalBasketGrips: 180° xuyên 6 cm, 150° có tay
        /// xuống đáy 1,3 cm); xô tro quá 170° thì cẳng tay trái lật ngược +48° (ClipFit17.AshBucketTrace 17/10).
        /// </summary>
        public const float TwoHandTilt = 150f;
        /// <summary>Chỗ bám bên đáy khi đổ: ra ngoài bề ngang vật chừng này, cao hơn đáy chừng này (m, ClipFit17.CoalBasketGrips).</summary>
        const float BaseGripOut = 0.05f, BaseGripLift = 0.08f;
        /// <summary>Chỗ ôm gốc của vật đang đổ (trả lại khi xong hay bị hủy giữa chừng).</summary>
        static readonly System.Collections.Generic.Dictionary<Holdable, (Vector3 left, Vector3 right)> Carried =
            new System.Collections.Generic.Dictionary<Holdable, (Vector3, Vector3)>();

        /// <summary>
        /// Vật ôm hai tay (sọt than) nghiêng đổ ra trước: đáy lật về phía người, nên nếu tay vẫn bám vành thì thân vật nằm giữa hai cẳng
        /// tay và cẳng tay xuyên thành sọt (5,7 cm, PlayerClipProbe 17/10). Hai tay trượt từ chỗ ôm xuống hai bên đáy theo góc nghiêng
        /// (tới đáy ở 120°), lật về thì trượt ngược lại; thân vật luôn ở phía trước bàn tay.
        /// </summary>
        static void BaseGrips(Holdable h, Transform left, Transform right, Vector3 leftRim, Vector3 rightRim, float radius, float angle)
        {
            float j = Mathf.SmoothStep(0f, 1f, angle / 120f);
            float y = BottomY(h) + BaseGripLift;
            left.localPosition = Vector3.Lerp(leftRim, new Vector3(-(radius + BaseGripOut), y, leftRim.z), j);
            right.localPosition = Vector3.Lerp(rightRim, new Vector3(radius + BaseGripOut, y, rightRim.z), j);
        }

        static void RestoreGrips(Holdable h)
        {
            if (!Carried.TryGetValue(h, out var grips)) return;
            Carried.Remove(h);
            if (h.leftGrip != null) h.leftGrip.localPosition = grips.left;
            if (h.rightGrip != null) h.rightGrip.localPosition = grips.right;
        }

        /// <summary>Đáy vật (y thấp nhất của các mesh, tọa độ của vật).</summary>
        static float BottomY(Holdable h)
        {
            float min = float.PositiveInfinity;
            foreach (var r in h.GetComponentsInChildren<MeshRenderer>())
            {
                var filter = r.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null) continue;
                var b = filter.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    min = Mathf.Min(min, h.transform.InverseTransformPoint(r.transform.TransformPoint(corner)).y);
                }
            }
            return float.IsInfinity(min) ? 0f : min;
        }
        const string PourGripLeft = "PourGripLeft", PourGripRight = "PourGripRight";

        /// <summary>Nửa bề ngang thân vật theo trục X của nó (hộp bao các mesh, tọa độ của vật).</summary>
        static float SideRadius(Holdable h)
        {
            float max = 0.05f;
            foreach (var r in h.GetComponentsInChildren<MeshRenderer>())
            {
                var filter = r.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null) continue;
                var b = filter.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    max = Mathf.Max(max, Mathf.Abs(h.transform.InverseTransformPoint(r.transform.TransformPoint(corner)).x));
                }
            }
            return max;
        }

        /// <summary>Hai tay sang hai chỗ bám trên vành như vật ôm (SetHeldGrips): không phải việc ở bàn nên thân không cúi về trước.</summary>
        static void TakeRim(PlayerRig rig, Transform left, Transform right)
        {
            rig.hands.SetHangCarry(null);
            rig.hands.SetHeldGrips(left, right);
        }

        static void RestoreHands(Holdable h, PlayerRig rig)
        {
            if (!h.hang || h.hangGrip == null || rig.hands == null || rig.interactor == null || rig.interactor.Holding != h)
                return;
            rig.hands.SetWorkGrips(null, null);
            rig.hands.SetHangCarry(h.hangGrip);
        }

        /// <summary>
        /// Bước lại gần chỗ đổ (m, theo phương ngang): đủ cho chỗ bám ở các tư thế đứng trên miệng, nghiêng 90°, úp hẳn nằm trong tầm với
        /// của vai (tính như vai dời theo người); không bước tới sát hơn bề ngang vật + 0,4 m.
        /// </summary>
        static Vector3 StepIn(Holdable h, PlayerRig rig, Vector3 rim, Vector3 mouth, Vector3 axis, Quaternion upright, Vector3? left, Vector3? right,
            float radius)
        {
            var body = rig.body;
            var toward = mouth - rig.transform.position;
            toward.y = 0f;
            float distance = toward.magnitude;
            if (body == null || distance < 0.01f)
                return Vector3.zero;
            toward /= distance;
            float need = 0f;
            foreach (float angle in new[] { 0f, 90f, 180f })
            {
                var rot = Quaternion.AngleAxis(angle, axis) * upright;
                var at = (angle == 0f ? mouth + Vector3.up * 0.18f : mouth) - rot * rim;
                foreach (var (grip, isLeft) in new[] { (left, true), (right, false) })
                {
                    if (grip == null || body.Shoulder(isLeft) == null) continue;
                    var g = at + rot * Vector3.Scale(grip.Value, h.transform.lossyScale);
                    var d = g - body.Shoulder(isLeft).position;
                    float arm = body.ArmLength(isLeft) * ReachShare;
                    float allowed = Mathf.Sqrt(Mathf.Max(0f, arm * arm - d.y * d.y));
                    need = Mathf.Max(need, new Vector2(d.x, d.z).magnitude - allowed);
                }
            }
            float most = Mathf.Min(MaxStep, distance - (radius + 0.4f));
            return toward * Mathf.Clamp(need + 0.03f, 0f, Mathf.Max(0f, most));
        }

        /// <summary>
        /// Cắt bước lại gần tại chỗ chạm vật đầu tiên cao hơn bước chân (sọt, thùng nằm dưới sàn): capsule trượt lên vật thấp hơn thân
        /// thì cả camera trồi lên theo. Dùng chung cho bước đổ (StepIn) và PlayerRig.StepToward.
        /// </summary>
        public static Vector3 ClearStep(CharacterController cc, Vector3 step)
        {
            float length = step.magnitude;
            if (cc == null || length < 0.001f)
                return step;
            var dir = step / length;
            var root = cc.transform.position;
            var low = root + Vector3.up * (cc.stepOffset + cc.radius);
            var high = root + Vector3.up * (cc.center.y + cc.height * 0.5f - cc.radius);
            if (Physics.CapsuleCast(low, high, cc.radius, dir, out var hit, length + StepSkin, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                return dir * Mathf.Max(0f, hit.distance - StepSkin);
            return step;
        }

        const float StepSkin = 0.02f;

        /// <summary>Dời vật (giữ góc) cho các chỗ bám nằm trong tầm với của vai cùng bên, tâm vật không lùi vào ngực, giữa thân vật không vào
        /// sát mắt.</summary>
        static Vector3 Reach(Holdable h, PlayerRig rig, Vector3 position, Quaternion rotation, Vector3 rim, Vector3? left, Vector3? right, float radius)
        {
            var body = rig.body;
            if (body == null)
                return position;
            var forward = rig.transform.forward;
            var eye = rig.cam != null ? rig.cam.transform.position : rig.head.position;
            for (int pass = 0; pass < 3; pass++)
            {
                foreach (var (grip, isLeft) in new[] { (left, true), (right, false) })
                {
                    if (grip == null || body.Shoulder(isLeft) == null) continue;
                    var at = position + rotation * Vector3.Scale(grip.Value, h.transform.lossyScale);
                    var reach = at - body.Shoulder(isLeft).position;
                    float excess = reach.magnitude - body.ArmLength(isLeft) * ReachShare;
                    if (excess > 0f)
                        position -= reach.normalized * excess;
                }
                float front = Vector3.Dot(position - rig.transform.position, forward);
                if (front < MinFront)
                    position += forward * (MinFront - front);
                var away = position + rotation * (rim * 0.5f) - eye;
                float gap = radius + EyeGap;
                if (away.magnitude < gap)
                    position += away.normalized * (gap - away.magnitude);
            }
            return position;
        }

        /// <summary>Dừng ngang (hết ca, đổi cảnh): vật về tay, mở khóa người chơi nếu không có đoạn dàn cảnh giữ.</summary>
        public static void Cancel(Holdable h, PlayerRig rig)
        {
            if (h == null || !h.Busy)
                return;
            h.WorldPose = null;
            h.Busy = false;
            RestoreGrips(h);
            if (rig != null) RestoreHands(h, rig);
            if (rig != null && rig.ControlEnabled)
            {
                rig.motor.MovementLocked = false;
                rig.look.Locked = false;
            }
        }
    }
}
