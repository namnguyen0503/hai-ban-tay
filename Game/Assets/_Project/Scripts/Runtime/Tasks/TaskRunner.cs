using System.Collections;
using HaiBanTay.Core;
using UnityEngine;

namespace HaiBanTay.Tasks
{
    /// <summary>Chạy một tác vụ tới khi xong. Ở chế độ Sim, tác vụ tự hoàn thành sau Sim.StepSeconds.</summary>
    public static class TaskRunner
    {
        public static IEnumerator Run(TaskBase task)
        {
            task.Activate();
            float simAt = Time.time + Sim.StepSeconds;
            while (task.State != TaskState.Done)
            {
                if (Sim.Enabled && Time.time >= simAt)
                    task.ForceComplete();
                yield return null;
            }
        }
    }
}
