using System.Collections;
using HaiBanTay.Interaction;
using HaiBanTay.Ship;
using UnityEngine;

namespace HaiBanTay.Tasks
{
    /// <summary>B2.3 Bão: E vào từng chiếc nồi đang trượt để chằng lại (3 nồi).</summary>
    public sealed class SecurePotsTask : TaskBase
    {
        public LooseObjectSway[] pots = new LooseObjectSway[0];
        public TaskTarget[] targets = new TaskTarget[0];

        Vector3[] _homes;
        Quaternion[] _homeRot;
        bool[] _secured;

        public int SecuredCount { get; private set; }

        void Awake()
        {
            _homes = new Vector3[pots.Length];
            _homeRot = new Quaternion[pots.Length];
            _secured = new bool[pots.Length];
            for (int i = 0; i < pots.Length; i++)
            {
                _homes[i] = pots[i].transform.position;
                _homeRot[i] = pots[i].transform.rotation;
                pots[i].Secure(true);
            }
        }

        protected override void OnActivated()
        {
            for (int i = 0; i < pots.Length; i++)
            {
                pots[i].transform.SetPositionAndRotation(_homes[i], _homeRot[i]);
                pots[i].enabled = true;
                foreach (var collider in pots[i].GetComponentsInChildren<Collider>()) collider.enabled = true;
                pots[i].Secure(false);
            }
        }

        int IndexOf(TaskTarget t) => System.Array.IndexOf(targets, t);

        public override string PromptFor(TaskTarget target, Interactor who)
        {
            int i = IndexOf(target);
            return i >= 0 && !_secured[i] ? "prompt.secure" : null;
        }

        public override bool Guide(Interactor who, out Transform target, out string key)
        {
            for (int i = 0; i < targets.Length; i++)
                if (!_secured[i] && targets[i] != null)
                    return Step(targets[i], "guide.secure", out target, out key);
            return Step(null, null, out target, out key);
        }

        public override bool CanInteract(TaskTarget target, Interactor who)
        {
            int i = IndexOf(target);
            return i >= 0 && !_secured[i];
        }

        public override void OnInteract(TaskTarget target, Interactor who)
        {
            int i = IndexOf(target);
            if (i < 0 || _secured[i])
                return;
            SecureAt(i);
        }

        void SecureAt(int i)
        {
            _secured[i] = true;
            SecuredCount++;
            pots[i].Secure(true);
            StartCoroutine(SlideHome(i));
            Core.GameRoot.Instance?.Audio.PlaySfx("SFX/metal_clank");
            ReportProgress();
            if (SecuredCount >= pots.Length)
                Complete();
        }

        IEnumerator SlideHome(int i)
        {
            var t = pots[i].transform;
            var from = t.position;
            var fromRot = t.rotation;
            for (float k = 0f; k < 1f; k += Time.deltaTime / 0.4f)
            {
                t.SetPositionAndRotation(Vector3.Lerp(from, _homes[i], k), Quaternion.Slerp(fromRot, _homeRot[i], k));
                yield return null;
            }
            t.SetPositionAndRotation(_homes[i], _homeRot[i]);
        }

        protected override void OnCompleted()
        {
            for (int i = 0; i < pots.Length; i++)
                if (!_secured[i])
                {
                    _secured[i] = true;
                    pots[i].Secure(true);
                    pots[i].transform.SetPositionAndRotation(_homes[i], _homeRot[i]);
                }
        }
    }
}
