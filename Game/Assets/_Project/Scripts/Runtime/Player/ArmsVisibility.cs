using System.Collections.Generic;
using UnityEngine;

namespace HaiBanTay.Player
{
    /// <summary>
    /// Ẩn hai tay người chơi ngoài các khung "giơ hai bàn tay" (17/10, người dùng: "Ẩn toàn bộ tay ... trừ lúc giơ 2 bàn tay, còn làm bếp
    /// ẩn tay cho đỡ lỗi", giữ công sức để làm lại tay sau). Chỉ tắt phần vẽ: bàn tay góc nhìn thứ nhất không vẽ (forceRenderingOff), thân
    /// dùng bản sao lưới đã bỏ tam giác cánh tay (xương giữ nguyên). IK, chỗ nắm, tầm với, đổ, bê và test tay vẫn chạy như cũ; dụng cụ, đồ
    /// đang bê vẫn hiện. Bật lại: PlayerRig.ArmsShownByDefault = true.
    /// </summary>
    [DefaultExecutionOrder(32000)]
    public sealed class ArmsVisibility : MonoBehaviour
    {
        /// <summary>Tam giác có trung bình trọng số xương tay (bắp tay trở xuống) từ mức này thì bỏ: vai áo còn nguyên, tay áo mất từ nách.</summary>
        const float ArmWeight = 0.5f;

        PlayerBody _body;
        Renderer[] _hands;
        bool _handsOff;
        readonly Dictionary<Mesh, Mesh> _armless = new Dictionary<Mesh, Mesh>();
        readonly Dictionary<Mesh, Mesh> _original = new Dictionary<Mesh, Mesh>();

        void Awake() => _body = GetComponent<PlayerBody>();

        void LateUpdate()
        {
            if (_body == null || _body.rig == null)
                return;
            bool shown = _body.rig.ArmsShown;
            SetHands(!shown);
            SetBody(!shown);
        }

        void SetHands(bool off)
        {
            if (_hands == null)
            {
                var hands = _body.rig.hands;
                if (hands == null) return;
                // Mọi mesh dưới hai bàn tay (dáng tay, tay rig, ống tay áo, cẳng tay nối) trừ dụng cụ: ảnh ArmsHidden 17/10 cúi 50° vẫn
                // thấy hai ống tay áo khi chỉ ẩn dáng tay và tay rig.
                var tools = new[] { hands.shovelProp, hands.shovelLoad, hands.rakeProp, hands.broomProp, hands.brushProp, hands.longBrushProp };
                var list = new List<Renderer>();
                foreach (var side in new[] { hands.left, hands.right })
                {
                    if (side == null) continue;
                    foreach (var r in side.GetComponentsInChildren<Renderer>(true))
                        if (System.Array.TrueForAll(tools, t => t == null || !r.transform.IsChildOf(t.transform))) list.Add(r);
                }
                void Add(GameObject g)
                {
                    if (g != null) foreach (var r in g.GetComponentsInChildren<Renderer>(true)) if (!list.Contains(r)) list.Add(r);
                }
                foreach (var g in hands.leftPoseMeshes) Add(g);
                foreach (var g in hands.rightPoseMeshes) Add(g);
                Add(hands.riggedLeft);
                Add(hands.riggedRight);
                _hands = list.ToArray();
                _handsOff = !off;
            }
            if (off == _handsOff)
                return;
            _handsOff = off;
            foreach (var r in _hands)
                if (r != null) r.forceRenderingOff = off;
        }

        /// <summary>
        /// Thân không tay: đổi sharedMesh sang bản không tay (tạo một lần cho mỗi lưới, kể cả khi đổi trang phục). Thu xương bắp tay về vai
        /// thì chỉ đúng lúc vẽ cuối khung và kéo vai áo thành hai cục trắng khi cúi (ảnh ArmsHidden 17/10), nên bỏ tam giác chứ không động xương.
        /// </summary>
        void SetBody(bool armless)
        {
            var skin = _body.skin;
            var mesh = skin != null ? skin.sharedMesh : null;
            if (mesh == null)
                return;
            bool isArmless = _original.TryGetValue(mesh, out var source);
            if (armless == isArmless)
                return;
            if (!armless)
            {
                skin.sharedMesh = source;
                return;
            }
            if (!_armless.TryGetValue(mesh, out var copy))
            {
                copy = Armless(mesh);
                _armless[mesh] = copy;
                if (copy != mesh) _original[copy] = mesh;
            }
            skin.sharedMesh = copy;
        }

        Mesh Armless(Mesh mesh)
        {
            if (!mesh.isReadable || _body.animator == null)
                return mesh;
            var upper = new[] { _body.animator.GetBoneTransform(HumanBodyBones.LeftUpperArm), _body.animator.GetBoneTransform(HumanBodyBones.RightUpperArm) };
            var bones = _body.skin.bones;
            var arm = new bool[bones.Length];
            for (int b = 0; b < bones.Length; b++)
                arm[b] = bones[b] != null && System.Array.Exists(upper, u => u != null && bones[b].IsChildOf(u));
            var weights = mesh.boneWeights;
            var w = new float[weights.Length];
            for (int i = 0; i < weights.Length; i++)
            {
                var bw = weights[i];
                w[i] = (arm[bw.boneIndex0] ? bw.weight0 : 0f) + (arm[bw.boneIndex1] ? bw.weight1 : 0f)
                     + (arm[bw.boneIndex2] ? bw.weight2 : 0f) + (arm[bw.boneIndex3] ? bw.weight3 : 0f);
            }
            var copy = Instantiate(mesh);
            copy.name = mesh.name + "_Armless";
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                var tris = mesh.GetTriangles(s);
                var kept = new List<int>(tris.Length);
                for (int t = 0; t < tris.Length; t += 3)
                    if ((w[tris[t]] + w[tris[t + 1]] + w[tris[t + 2]]) / 3f < ArmWeight)
                    {
                        kept.Add(tris[t]); kept.Add(tris[t + 1]); kept.Add(tris[t + 2]);
                    }
                copy.SetTriangles(kept, s, false);
            }
            return copy;
        }

        void OnDisable()
        {
            if (_body != null && _body.skin != null) SetBody(false);
            if (_hands != null) SetHands(false);
        }

        void OnDestroy()
        {
            foreach (var m in _original.Keys) if (m != null) Destroy(m);
        }
    }
}
