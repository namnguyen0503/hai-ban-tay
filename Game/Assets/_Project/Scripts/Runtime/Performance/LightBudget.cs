using System;
using System.Collections.Generic;
using HaiBanTay.Ship;
using UnityEngine;

namespace HaiBanTay.Performance
{
    public readonly struct LightBudgetAllocation
    {
        public readonly int ExteriorCount, InteriorCount;
        public readonly ShipRegion ShadowRegion;
        public bool HasShadow => ExteriorCount + InteriorCount > 0;

        public LightBudgetAllocation(int exterior, int interior, ShipRegion shadowRegion)
        {
            ExteriorCount = exterior;
            InteriorCount = interior;
            ShadowRegion = shadowRegion;
        }
    }

    /// <summary>Ưu tiên nhóm người chơi đứng; tối đa 3 đèn, chỉ đèn đầu của nhóm ưu tiên có bóng.</summary>
    public static class LightBudgetLogic
    {
        public static LightBudgetAllocation Resolve(ShipRegion region, int exterior, int interior)
        {
            if (exterior < 0 || interior < 0)
                throw new ArgumentOutOfRangeException("Số đèn không được âm.");
            int e, i;
            if (region == ShipRegion.Interior)
            {
                i = Math.Min(interior, 3);
                e = Math.Min(exterior, 3 - i);
            }
            else
            {
                e = Math.Min(exterior, 3);
                i = Math.Min(interior, 3 - e);
            }
            var shadow = region == ShipRegion.Interior && i > 0 || e == 0 ? ShipRegion.Interior : ShipRegion.Exterior;
            return new LightBudgetAllocation(e, i, shadow);
        }
    }

    /// <summary>Quản lý các đèn realtime đã truyền vào; đèn bake không thuộc hai nhóm này.</summary>
    public sealed class LightBudget : MonoBehaviour
    {
        public Light[] exterior = Array.Empty<Light>();
        public Light[] interior = Array.Empty<Light>();
        public LightShadows shadowType = LightShadows.Hard;
        /// <summary>
        /// Đèn đường/đèn bến ngoài trời nhiều hơn số suất: giữ nguyên `exteriorPinned` đèn đầu (mặt trời/trăng) rồi chọn các đèn còn
        /// lại gần camera nhất (xếp lại mỗi nửa giây). Tắt thì thứ tự mảng là thứ tự ưu tiên.
        /// </summary>
        public bool sortExteriorByDistance;
        public int exteriorPinned = 1;
        public ShipRegion Region { get; private set; }

        Light[] _exterior, _interior;
        Light _lent;
        Transform _focus;
        Light[] _sortBuffer;
        float _nextSort;

        void OnEnable()
        {
            var seen = new HashSet<Light>();
            _exterior = Unique(exterior, seen);
            _interior = Unique(interior, seen);
            ShipZone.RegionChanged += SetRegion;
            SetRegion(ShipZone.CurrentRegion);
        }

        static Light[] Unique(Light[] group, HashSet<Light> seen)
        {
            var result = new List<Light>();
            if (group != null)
                foreach (var light in group)
                    // bakingOutput có ở bản build (Light.lightmapBakeType chỉ có trong Editor); đèn chưa bake tính là realtime.
                    if (light != null && light.bakingOutput.lightmapBakeType != LightmapBakeType.Baked && seen.Add(light))
                        result.Add(light);
            return result.ToArray();
        }

        void OnDisable() => ShipZone.RegionChanged -= SetRegion;

        void Update()
        {
            if (!sortExteriorByDistance || _exterior == null || _exterior.Length <= exteriorPinned + 1 || Time.unscaledTime < _nextSort)
                return;
            _nextSort = Time.unscaledTime + 0.5f;
            if (_focus == null)
            {
                var cam = Camera.main;
                if (cam == null)
                    return;
                _focus = cam.transform;
            }
            var origin = _focus.position;
            // Sắp xếp chèn tại chỗ trên bản sao (vài đèn): không cấp phát mỗi nửa giây.
            if (_sortBuffer == null || _sortBuffer.Length != _exterior.Length)
                _sortBuffer = new Light[_exterior.Length];
            Array.Copy(_exterior, _sortBuffer, _exterior.Length);
            for (int i = exteriorPinned + 1; i < _sortBuffer.Length; i++)
            {
                var item = _sortBuffer[i];
                float d = (item.transform.position - origin).sqrMagnitude;
                int j = i - 1;
                while (j >= exteriorPinned && (_sortBuffer[j].transform.position - origin).sqrMagnitude > d)
                {
                    _sortBuffer[j + 1] = _sortBuffer[j];
                    j--;
                }
                _sortBuffer[j + 1] = item;
            }
            for (int i = exteriorPinned; i < _sortBuffer.Length; i++)
                if (_sortBuffer[i] != _exterior[i])
                {
                    (_exterior, _sortBuffer) = (_sortBuffer, _exterior);
                    SetRegion(Region);
                    return;
                }
        }

        /// <summary>Đổi nhóm đèn trong khoang lúc chạy (đèn treo ban ngày ↔ đèn dầu buổi tối) rồi cấp lại theo vùng hiện tại.</summary>
        public void SetInterior(params Light[] lights)
        {
            interior = lights ?? Array.Empty<Light>();
            if (_exterior == null || _interior == null)
                return;
            Disable(_interior);
            _interior = Unique(interior, new HashSet<Light>(_exterior));
            SetRegion(Region);
        }

        public void SetRegion(ShipRegion region)
        {
            if (_exterior == null || _interior == null)
                return;
            Region = region;
            // Tắt hết trước khi cấp bóng: chuyển vùng không có thời điểm hai đèn cùng đổ bóng.
            Disable(_exterior);
            Disable(_interior);
            var choice = LightBudgetLogic.Resolve(region, _exterior.Length, _interior.Length);
            Enable(_exterior, choice.ExteriorCount, choice.HasShadow && choice.ShadowRegion == ShipRegion.Exterior);
            Enable(_interior, choice.InteriorCount, choice.HasShadow && choice.ShadowRegion == ShipRegion.Interior);
        }

        /// <summary>Nhường một suất cho đèn fill tạm (khung hai bàn tay): nếu đã bật đủ 3 đèn thì tắt đèn không bóng cuối.</summary>
        public void Lend()
        {
            if (_exterior == null || _interior == null || _lent != null)
                return;
            int on = 0;
            Light last = null;
            foreach (var group in new[] { _exterior, _interior })
                foreach (var light in group)
                    if (light != null && light.isActiveAndEnabled)
                    {
                        on++;
                        if (light.shadows == LightShadows.None)
                            last = light;
                    }
            if (on >= 3 && last != null)
            {
                last.enabled = false;
                _lent = last;
            }
        }

        /// <summary>Trả lại suất đã nhường (cấp lại theo vùng hiện tại).</summary>
        public void Restore()
        {
            if (_lent == null)
                return;
            _lent = null;
            SetRegion(Region);
        }

        static void Disable(Light[] group)
        {
            foreach (var light in group)
                if (light != null)
                {
                    light.enabled = false;
                    light.shadows = LightShadows.None;
                }
        }

        void Enable(Light[] group, int count, bool shadow)
        {
            for (int i = 0; i < count; i++)
                if (group[i] != null)
                {
                    group[i].shadows = shadow && i == 0 ? shadowType : LightShadows.None;
                    group[i].enabled = true;
                }
        }
    }
}
