using UnityEngine;

namespace HaiBanTay.Npc
{
    /// <summary>Chỉnh xương sau Animator để ôm bụng khi say sóng, không ngã và không nôn.</summary>
    public sealed class SeasickPose : MonoBehaviour
    {
        public bool floorSeated;
        Animator _anim;
        Transform _spine, _left, _right, _hips, _leftThigh, _leftKnee, _leftFoot, _rightThigh, _rightKnee, _rightFoot;
        void Awake()
        {
            _anim = GetComponentInChildren<Animator>();
            if (_anim == null || !_anim.isHuman) return;
            _spine = _anim.GetBoneTransform(HumanBodyBones.Spine);
            _hips = _anim.GetBoneTransform(HumanBodyBones.Hips);
            _leftThigh = _anim.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
            _leftKnee = _anim.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
            _leftFoot = _anim.GetBoneTransform(HumanBodyBones.LeftFoot);
            _rightThigh = _anim.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            _rightKnee = _anim.GetBoneTransform(HumanBodyBones.RightLowerLeg);
            _rightFoot = _anim.GetBoneTransform(HumanBodyBones.RightFoot);
            _left = _anim.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            _right = _anim.GetBoneTransform(HumanBodyBones.RightUpperArm);
        }
        void LateUpdate()
        {
            if (floorSeated && _hips != null)
            {
                // Ngồi trên boong, hai chân duỗi phía trước; gốc và collider vẫn đặt trên sàn thật.
                var p = _hips.position; p.y = transform.position.y + 0.28f; _hips.position = p;
                SeatLeg(_leftThigh, _leftKnee, _leftFoot);
                SeatLeg(_rightThigh, _rightKnee, _rightFoot);
            }
            if (_spine != null) _spine.localRotation *= Quaternion.Euler(18f + Mathf.Sin(Time.time) * 2f, 0f, 0f);
            if (_left != null) _left.localRotation *= Quaternion.Euler(15f, 0f, -35f);
            if (_right != null) _right.localRotation *= Quaternion.Euler(15f, 0f, 35f);
        }
        void SeatLeg(Transform thigh, Transform knee, Transform foot)
        {
            if (thigh == null || knee == null || foot == null) return;
            var forward = transform.forward;
            var kneeTarget = thigh.position + forward * 0.42f + Vector3.down * 0.06f;
            thigh.rotation = Quaternion.FromToRotation(knee.position - thigh.position, kneeTarget - thigh.position) * thigh.rotation;
            var footTarget = knee.position + forward * 0.4f + Vector3.down * 0.1f;
            knee.rotation = Quaternion.FromToRotation(foot.position - knee.position, footTarget - knee.position) * knee.rotation;
        }
    }
}
