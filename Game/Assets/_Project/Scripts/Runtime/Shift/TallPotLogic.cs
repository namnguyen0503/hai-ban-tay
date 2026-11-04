using System;

namespace HaiBanTay.Shift
{
    /// <summary>Nơi chiếc nồi cao đang nằm trong vòng đời của nó (một vật duy nhất, luôn đúng một trạng thái).</summary>
    public enum TallPotState
    {
        /// <summary>Ở góc phòng rửa, sạch.</summary>
        HomeClean,
        /// <summary>Đã kéo lê ra đặt trên sàn trước lò, sạch, chưa nhấc.</summary>
        AtStoveClean,
        /// <summary>Nằm trên ô bếp (nấu ở đây), chưa phục vụ.</summary>
        OnHobClean,
        /// <summary>Đã phục vụ xong, bẩn, vẫn trên ô bếp.</summary>
        OnHobDirty,
        /// <summary>Nhấc xuống đặt trên sàn trước lò, bẩn.</summary>
        AtStoveDirty,
        /// <summary>Kéo lê về góc phòng rửa, bẩn: chờ cọ và tráng.</summary>
        HomeDirty
    }

    /// <summary>Bước chuyển hợp lệ của vòng đời nồi cao; mỗi trạng thái chỉ có đúng một bước kế.</summary>
    public enum TallPotStep
    {
        /// <summary>Kéo lê từ góc rửa ra trước lò.</summary>
        TowOut,
        /// <summary>Ba nhấc nồi lên ô bếp.</summary>
        LiftUp,
        /// <summary>Nấu xong và phục vụ: nồi bám cặn.</summary>
        Serve,
        /// <summary>Ba nhấc nồi xuống sàn.</summary>
        LiftDown,
        /// <summary>Kéo lê về góc rửa.</summary>
        TowBack,
        /// <summary>Cọ sạch và tráng: nồi lại sạch ở chỗ cũ.</summary>
        Wash
    }

    /// <summary>
    /// Vòng đời nồi cao (Ø 0,8 × 1 m, kéo lê và nhấc, không thể xách): chỗ rửa → kéo ra lò → nhấc lên ô bếp → nấu, phục vụ → nhấc
    /// xuống → kéo về → cọ, tráng → chỗ rửa. Bảng việc, hướng dẫn, lượt tua nhanh và Sim chỉ đọc trạng thái này; chỉ cho các
    /// chuyển hợp lệ nên nồi không thể vừa ở bếp vừa ở chậu hay bẩn mà không qua nấu.
    /// </summary>
    public sealed class TallPotLogic
    {
        static readonly TallPotStep[] Cycle =
        {
            TallPotStep.TowOut, TallPotStep.LiftUp, TallPotStep.Serve, TallPotStep.LiftDown, TallPotStep.TowBack, TallPotStep.Wash
        };

        public TallPotState State { get; private set; } = TallPotState.HomeClean;
        /// <summary>Số vòng đã khép kín (nồi cọ xong trở về chỗ).</summary>
        public int Cycles { get; private set; }
        public bool Scrubbed { get; private set; }
        public bool Rinsed { get; private set; }

        public bool IsClean => State == TallPotState.HomeClean || State == TallPotState.AtStoveClean || State == TallPotState.OnHobClean;
        public bool IsHome => State == TallPotState.HomeClean || State == TallPotState.HomeDirty;
        public bool IsAtStove => State == TallPotState.AtStoveClean || State == TallPotState.AtStoveDirty;
        public bool IsOnHob => State == TallPotState.OnHobClean || State == TallPotState.OnHobDirty;

        /// <summary>Bước hợp lệ duy nhất từ trạng thái hiện tại.</summary>
        public TallPotStep Next => Cycle[(int)State];

        public bool CanApply(TallPotStep step) => step == Next && (step != TallPotStep.Wash || (Scrubbed && Rinsed));

        /// <summary>Thực hiện một bước; trả false (không đổi gì) nếu bước không hợp lệ ở trạng thái này.</summary>
        public bool Apply(TallPotStep step)
        {
            if (!CanApply(step))
                return false;
            if (step == TallPotStep.Wash)
            {
                Cycles++;
                Scrubbed = Rinsed = false;
            }
            State = (TallPotState)(((int)State + 1) % Cycle.Length);
            return true;
        }

        /// <summary>Cọ xong lớp bẩn (chỉ có nghĩa khi nồi đã kéo về chỗ rửa).</summary>
        public bool MarkScrubbed()
        {
            if (State != TallPotState.HomeDirty)
                return false;
            Scrubbed = true;
            return true;
        }

        /// <summary>Tráng xong (sau khi cọ sạch).</summary>
        public bool MarkRinsed()
        {
            if (State != TallPotState.HomeDirty || !Scrubbed)
                return false;
            Rinsed = true;
            return true;
        }

        /// <summary>
        /// Tua nhanh (lượt montage, Sim, bot): đi tiếp từng bước hợp lệ cho tới khi gặp trạng thái đích, kể cả vòng qua chỗ rửa.
        /// Cọ và tráng coi như đã làm. Trả về số bước đã đi.
        /// </summary>
        public int FastForwardTo(TallPotState target)
        {
            int steps = 0;
            while (State != target && steps < Cycle.Length)
            {
                if (State == TallPotState.HomeDirty)
                {
                    Scrubbed = true;
                    Rinsed = true;
                }
                Apply(Next);
                steps++;
            }
            return steps;
        }

        /// <summary>Đặt lại (ngày mới, tải lại cảnh): nồi sạch ở chỗ rửa.</summary>
        public void Reset()
        {
            State = TallPotState.HomeClean;
            Scrubbed = Rinsed = false;
        }
    }
}
