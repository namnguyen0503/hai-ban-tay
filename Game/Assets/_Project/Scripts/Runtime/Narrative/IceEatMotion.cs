using HaiBanTay.Player;
using UnityEngine;

namespace HaiBanTay.Narrative
{
    /// <summary>
    /// Nhịp ăn kem của anh Ba ở prologue. Mỗi thìa: với tay sang cốc, xúc một miếng, đưa lên miệng, ăn, trả tay về chỗ nghỉ.
    /// Hàm thuần theo (thìa thứ mấy, pha 0..1 trong thìa) nên tua ngược được (thả chuột giữa thìa thì thìa trả miếng kem về cốc)
    /// và test được không cần scene. Tọa độ tay theo khung của HandsRig (gắn vào camera). Kích thước cốc/thìa dùng chung với
    /// <c>IceCreamProps</c> (Editor) để mesh và chuyển động khớp nhau.
    /// </summary>
    public static class IceEatMotion
    {
        public const int Bites = 3;

        // Hình học (m). Gốc cốc ở tâm đáy; gốc viên kem ở mặt phẳng miệng cốc; gốc thìa ở điểm cầm, +Y về phía lòng thìa.
        public const float CupHeight = .085f, CupFloor = .0036f, CupBottomRadius = .0245f, CupTopRadius = .0455f;
        // Viên kem đầy nhô khỏi miệng cốc ~3 cm (≈ 0,3 đường kính miệng như ảnh tham chiếu), không chạm thành cốc ở bất kỳ độ sâu nào.
        public const float ScoopRadius = .040f, ScoopCenter = -.012f, BiteRadius = .015f, BiteCenter = .003f;
        // Thìa nhựa dài 14 cm: đuôi cán 6,75 cm sau điểm cầm (gác qua kẽ ngón cái–trỏ), cổ thìa 2,3 cm trước, lòng thìa 4,8 × 2,8 cm.
        public const float SpoonTailY = -.0675f, SpoonTipY = .0729f, SpoonBowlY = .050f, SpoonBowlZ = -.005f;

        // Khung bàn tay FP (Tools/blender/make_fp_hands.py, make_fp_hand_rig.py): +Z ngón tay, −Y lòng bàn tay, ngón cái trái ở +X (tay phải ở −X).
        // Kiểu cầm và mọi khóa dưới đây giải ngoài Unity (FK trên chính Hand_{L,R}_Rig.json, khớp bóng tay trong 5 ảnh tham chiếu 16/10):
        // tay trái ôm thân cốc từ bên trái, mu tay quay về phía mắt, ngón cái vắt ngang mặt trước cốc, bốn ngón khép tới khi chạm thành cốc
        // phía sau; tay phải cầm thìa như HandGrips.SpoonRight. Không đỉnh da nào xuyên cốc/cán thìa; tay phải cách tay trái ≥ 2 cm suốt cả ba thìa.
        // Cổ tay hai bên gập ≤ ~16° ở mọi pha (FK cánh tay như PlayerBody): gập 35° làm mu tay gãy gập vào cổ tay áo (ảnh 17/10).
        public const float CupScale = 1.05f;

        /// <summary>Cốc (gốc ở tâm đáy, +Y trục cốc) trong khung tay trái.</summary>
        public static HandGrip CupGrip = new HandGrip(new Vector3(-.0433f, -.0725f, .0723f), new Vector3(13.74f, -12.61f, -85.02f), new FingerPose
        {
            thumb = new DigitPose(69.3f, 13.5f, 2.5f, -91.5f, 59.2f),
            index = new DigitPose(22.7f, 25.2f, 17.6f),
            middle = new DigitPose(33.4f, 37.2f, 26f),
            ring = new DigitPose(35.8f, 39.8f, 27.8f),
            little = new DigitPose(35.6f, 39.5f, 27.7f)
        });

        public static HandGrip SpoonGrip = HandGrips.SpoonRight;

        // Không phải hằng để test ảnh thử biến thể mà không dựng lại scene.
        public static Vector3 LeftHandPosition = new Vector3(-.1382f, -.1635f, .3113f);
        public static Vector3 LeftHandEuler = new Vector3(-33.85f, 21.47f, 76.33f);

        /// <summary>Nội sáng của da tay khi ăn kem (đèn quán đã chiếu tay), 0–1.</summary>
        public static float HandEmission = .1f;

        public struct Key
        {
            public Vector3 position, euler;
            public Key(float x, float y, float z, float ex, float ey, float ez) { position = new Vector3(x, y, z); euler = new Vector3(ex, ey, ez); }
            public Quaternion Rotation => Quaternion.Euler(euler);
        }

        // Khóa tay phải (khung camera): nghỉ = ảnh "Empty Cup"/"Half-Eaten" (thìa ngang bên phải cốc, lòng thìa ngửa);
        // đưa lên = ảnh "Ice Cream Bite", lòng thìa ngửa lên (không quay về mắt, ngón cong nằm phía trước cán);
        // sát miệng = lòng thìa dưới mắt trước miệng, xoay −30° quanh cán so với ảnh "Tasting": quét 21 phương án (xoay −90…+90°, 3 khoảng
        // cách) thì phương án này có bóng tay phải trùng ảnh ref nhiều nhất (IoU 0,39; hướng y ảnh ref 0,29). Thìa lệch ảnh ref vài chục độ để cổ tay thẳng.
        public static Key Rest = new Key(.1647f, -.1233f, .2631f, -56.36f, -24.79f, -74.42f);
        public static Key Lift = new Key(.1386f, -.0809f, .195f, -47.99f, -55.43f, -76.89f);
        public static Key Mouth = new Key(.1412f, -.0752f, .1509f, -52.32f, -67.85f, -49.92f);
        /// <summary>Lòng thìa chạm đỉnh phần kem còn lại của từng thìa: kem tụt dần vào cốc nên thìa sau thấp hơn, chúc dốc hơn.</summary>
        public static Key[] Dips =
        {
            new Key(.0889f, -.0963f, .3351f, -46.05f, -43.56f, -50.57f),
            new Key(.0946f, -.1222f, .3454f, -46.05f, -43.56f, -50.57f),
            // Thìa cuối: kem đã tụt sâu, thìa chúc dốc và vào từ phía trước bên phải để mu tay không quệt miệng cốc.
            new Key(.0487f, -.0854f, .2775f, -37.37f, -47.73f, 4.78f)
        };

        /// <summary>Trục cốc (khung camera) để nhấc thìa thẳng ra khỏi kem.</summary>
        static Vector3 CupAxis => Quaternion.Euler(LeftHandEuler) * CupGrip.Rotation * Vector3.up;

        /// <summary>Phần kem còn trong cốc sau n thìa: đầy, còn nửa, còn một nắm nhỏ ở đáy, hết.</summary>
        static readonly float[] Remaining = { 1f, .62f, .3f, 0f };

        public struct Frame
        {
            public Vector3 rightPosition, rightEuler;
            /// <summary>1 = viên kem đầy, 0 = cốc rỗng.</summary>
            public float cupScoop;
            /// <summary>Cỡ miếng kem trên thìa; 0 = thìa trống.</summary>
            public float spoonScoop;
        }

        static float Ease(float t) => t * t * (3f - 2f * t);

        static float Phase(float u, float from, float to) => Mathf.Clamp01((u - from) / (to - from));

        static Frame At(Vector3 position, Quaternion rotation) => new Frame { rightPosition = position, rightEuler = rotation.eulerAngles };

        /// <summary>
        /// Một thìa, u = 0..1: với sang cốc theo cung vòng qua miệng cốc, xúc (nhấc lòng thìa dọc trục cốc, miếng kem rời cốc lên thìa),
        /// đưa lên miệng theo đường cong qua khóa Lift, ăn (miếng kem nhỏ dần), trả tay về chỗ nghỉ.
        /// </summary>
        public static Frame Evaluate(int bite, float u)
        {
            bite = Mathf.Clamp(bite, 0, Bites - 1);
            u = Mathf.Clamp01(u);
            const float reachEnd = .2f, scoopEnd = .3f, liftEnd = .62f, mouthEnd = .74f;
            var dip = Dips[bite];
            var up = dip.position + CupAxis * .03f;
            Frame frame;
            float spoon;
            if (u < reachEnd)
            {
                float k = Phase(u, 0f, reachEnd), t = Ease(k);
                frame = At(Vector3.Lerp(Rest.position, dip.position, t) + Vector3.up * (.025f * Mathf.Sin(k * Mathf.PI)),
                    Quaternion.Slerp(Rest.Rotation, dip.Rotation, t));
                spoon = 0f;
            }
            else if (u < scoopEnd)
            {
                float t = Ease(Phase(u, reachEnd, scoopEnd));
                frame = At(Vector3.Lerp(dip.position, up, t), dip.Rotation);
                spoon = t;
            }
            else if (u < liftEnd)
            {
                // Bézier bậc hai đi qua Lift ở giữa đường; hướng tay nội suy hai tầng (de Casteljau).
                float t = Ease(Phase(u, scoopEnd, liftEnd));
                var control = 2f * Lift.position - (up + Mouth.position) * .5f;
                var position = (1f - t) * (1f - t) * up + 2f * (1f - t) * t * control + t * t * Mouth.position;
                var rotation = Quaternion.Slerp(Quaternion.Slerp(dip.Rotation, Lift.Rotation, t), Quaternion.Slerp(Lift.Rotation, Mouth.Rotation, t), t);
                frame = At(position, rotation);
                spoon = 1f;
            }
            else if (u < mouthEnd)
            {
                frame = At(Mouth.position, Mouth.Rotation);
                spoon = 1f - Ease(Phase(u, liftEnd, mouthEnd));
            }
            else
            {
                float t = Ease(Phase(u, mouthEnd, 1f));
                frame = At(Vector3.Lerp(Mouth.position, Rest.position, t), Quaternion.Slerp(Mouth.Rotation, Rest.Rotation, t));
                spoon = 0f;
            }
            frame.spoonScoop = spoon;
            // Miếng kem rời cốc cùng lúc xuất hiện trên thìa.
            float taken = Ease(Phase(u, reachEnd, scoopEnd));
            frame.cupScoop = Mathf.Lerp(Remaining[bite], Remaining[bite + 1], taken);
            return frame;
        }

        /// <summary>Tư thế nghỉ lúc chưa ăn (và lúc ăn xong thìa cuối).</summary>
        public static Frame Idle(int bitesEaten)
        {
            var frame = Evaluate(Mathf.Clamp(bitesEaten, 0, Bites - 1), 0f);
            frame.cupScoop = Remaining[Mathf.Clamp(bitesEaten, 0, Bites)];
            return frame;
        }

        /// <summary>Độ cao đỉnh viên kem (khung cốc chưa scale, tính từ đáy) khi còn <paramref name="remaining"/> phần, khớp ApplyCupScoop.</summary>
        public static float CreamTop(float remaining)
        {
            float s = Mathf.Pow(Mathf.Clamp01(remaining), .55f);
            return CupHeight - (1f - s) * (CupHeight - CupFloor) + (ScoopCenter + ScoopRadius) * s * (.75f + .25f * s);
        }

        /// <summary>Phần kem còn lại lúc bắt đầu thìa thứ <paramref name="bite"/>.</summary>
        public static float RemainingBefore(int bite) => Remaining[Mathf.Clamp(bite, 0, Bites)];

        /// <summary>Đặt viên kem trong cốc theo phần còn lại: nhỏ dần và tụt xuống đáy cốc; hết thì tắt.</summary>
        public static void ApplyCupScoop(Transform cream, float remaining)
        {
            bool visible = remaining > .01f;
            if (cream.gameObject.activeSelf != visible) cream.gameObject.SetActive(visible);
            if (!visible) return;
            float s = Mathf.Pow(remaining, .55f);
            cream.localScale = new Vector3(s, s * (.75f + .25f * s), s);
            cream.localPosition = Vector3.up * (CupHeight - (1f - s) * (CupHeight - CupFloor));
        }

        /// <summary>Miếng kem trên thìa: scale theo cỡ, tắt khi thìa trống.</summary>
        public static void ApplySpoonScoop(Transform bite, float size)
        {
            bool visible = size > .02f;
            if (bite.gameObject.activeSelf != visible) bite.gameObject.SetActive(visible);
            if (visible) bite.localScale = Vector3.one * size;
        }
    }
}
