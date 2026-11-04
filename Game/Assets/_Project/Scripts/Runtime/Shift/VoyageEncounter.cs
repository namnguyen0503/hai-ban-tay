using System.Collections;
using HaiBanTay.Core;
using HaiBanTay.Interaction;
using HaiBanTay.Npc;
using HaiBanTay.Player;
using HaiBanTay.Tasks;
using UnityEngine;

namespace HaiBanTay.Shift
{
    /// <summary>
    /// Bữa xúp hoặc cuộc gặp ông Chiêu: thao tác thật, lời đáp cố định, không lựa chọn ý kiến. Bữa xúp (S16: "mới đầu anh
    /// không ăn được bánh mì và bơ. Ăn xúp thì anh dùng nĩa"): ngồi xuống thì ông Mai bắt chuyện, Ba thử nĩa, ông đưa thìa,
    /// ăn xong hai người nói tiếp.
    /// </summary>
    public sealed class VoyageEncounter : WorkStation
    {
        static readonly string[] SoupIntro = { "mai_meal_1", "ba_meal_1", "mai_meal_2" };
        static readonly string[] SoupOutro = { "ba_meal_2", "mai_meal_3", "mai_meal_4", "ba_meal_3", "mai_meal_5" };
        public string orderKind;
        public TaskTarget target;
        public GameObject fork, spoon;
        public Transform forkGrip, spoonGrip;
        public NpcAgent speaker;
        public Vector3 stand;
        [Tooltip("Bữa xúp: ghế Ba ngồi đối diện ông Mai, chỗ mắt nhìn khi ngồi xuống và clip ngồi ghế của thân.")]
        public Transform seat, seatLook;
        public AnimationClip seatClip;
        public override string Kind => orderKind;
        public int Pending { get; private set; }
        public int Stage { get; private set; }
        public bool Busy { get; private set; }
        public override void OnOrder(int count) { Pending += count; Stage = 0; }
        public override string PromptFor(TaskTarget t, Interactor who) => t == target && Pending > 0 && !Busy && who.HandsFree
            ? (Kind == OrderKinds.Soup ? Stage == 0 ? seat != null ? "prompt.soup_sit" : "prompt.soup_fork" : "prompt.soup_spoon" : "prompt.chieu") : null;
        public override bool Guide(Interactor who, out Transform t, out string key) =>
            Step(target, Pending > 0 && !Busy ? "guide." + Kind : null, out t, out key);
        public override bool CanInteract(TaskTarget t, Interactor who) => PromptFor(t, who) != null;
        public override void OnInteract(TaskTarget t, Interactor who) { if (CanInteract(t, who)) StartCoroutine(Run()); }
        IEnumerator Run()
        {
            Busy = true;
            var root = GameRoot.Instance;
            foreach (var id in new[] { "mai_meal_1", "crew_spoon", "chieu_1" })
                TalkGaze.BindLine(id, speaker);
            if (Kind == OrderKinds.Soup && Stage == 0)
            {
                // Ngồi xuống ghế đối diện ông Mai rồi mới ăn; tay vẫn với được bát (bấm lại để cầm thìa).
                var rig = PlayerRig.Current;
                if (seat != null && rig != null)
                {
                    rig.GetComponent<SitController>().SitForStudy(seat, seatLook != null ? seatLook.position : speaker.Position + Vector3.up, seatClip);
                    rig.interactor.enabled = true;
                    yield return new WaitForSeconds(0.5f);
                }
                foreach (var id in SoupIntro)
                    yield return root.Subtitles.PlayAndWait(id);
                fork.SetActive(true); spoon.SetActive(false);
                yield return TrySoup(fork.transform, forkGrip);
                speaker.Play("Point");
                yield return root.Subtitles.PlayAndWait("crew_spoon");
                fork.SetActive(false); spoon.SetActive(true); Stage = 1;
            }
            else
            {
                if (Kind == OrderKinds.Chieu)
                {
                    speaker.Play("Talk");
                    foreach (var id in new[] { "chieu_1", "chieu_2", "chieu_3", "ba_chieu" })
                        yield return root.Subtitles.PlayAndWait(id);
                    speaker.Play("Idle");
                    yield return new WaitForSeconds(2f);
                }
                else
                {
                    yield return TrySoup(spoon.transform, spoonGrip);
                    foreach (var id in SoupOutro)
                        yield return root.Subtitles.PlayAndWait(id);
                    // Ăn xong, nói chuyện xong thì đứng dậy.
                    var sit = PlayerRig.Current != null ? PlayerRig.Current.GetComponent<SitController>() : null;
                    if (sit != null && sit.StudySeated)
                        sit.Stand();
                }
                Pending--; Work(); Stage = 2;
            }
            Busy = false;
        }
        IEnumerator TrySoup(Transform utensil, Transform grip)
        {
            var rig = PlayerRig.Current;
            // Đứng thì bước sát bàn trước khi cầm (tốc độ đi bộ, giữ góc nhìn đang nhắm bát); đang ngồi ghế thì đã ở sát bàn.
            if (!rig.GetComponent<SitController>().StudySeated)
                yield return rig.StepToward(stand);
            var start = utensil.position;
            // Đích theo thân người: cúi camera xuống bát không kéo dụng cụ xuyên ngực.
            var toMouth = rig.transform.TransformPoint(new Vector3(0.18f, rig.head.localPosition.y - 0.18f, 0.35f));
            rig.look.Locked = true;
            // Chỉ tay phải cầm nĩa/thìa (dáng nắm); tay trái thả lỏng (HandsRig.SetWorkGrips một tay).
            rig.hands.SetWorkGrips(null, grip, HandPose.Carry);
            for (float t = 0f; t < 1.5f; t += Time.deltaTime)
            {
                // Nhấc lên trước rồi mới đưa về miệng: đi thẳng thì tay lướt qua quai nồi canh cạnh bát.
                float k = Mathf.Sin(t / 1.5f * Mathf.PI);
                utensil.position = Vector3.Lerp(start, toMouth, k) + Vector3.up * (0.1f * Mathf.Sin(k * Mathf.PI));
                yield return null;
            }
            utensil.position = start;
            rig.hands.SetWorkGrips(null, null); rig.hands.SetPose(HandPose.Lowered);
            rig.look.Locked = false;
        }
        public override void SimulateWork() { if (Pending > 0) { Stage = 2; Pending--; Work(); } }
    }
}
