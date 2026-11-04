using UnityEngine;

namespace HaiBanTay.Core
{
    /// <summary>
    /// Mã bản dựng (Tools/build_id.py, PRODUCTION_PLAN W1.4). Bản build đọc Resources/BuildId do BuildScript ghi lúc build;
    /// Editor và test đọc biến môi trường HBT_BUILD_ID do Tools/unity.sh đặt. Ghi chú debug và log bot gắn mã này, để biết
    /// ảnh hay phản hồi thuộc bản dữ liệu nào.
    /// </summary>
    public static class BuildInfo
    {
        static string _id;

        public static string Id => _id ??= Load();

        static string Load()
        {
            var env = System.Environment.GetEnvironmentVariable("HBT_BUILD_ID");
            if (!string.IsNullOrWhiteSpace(env))
                return env.Trim();
            var asset = Resources.Load<TextAsset>("BuildId");
            if (asset != null && !string.IsNullOrWhiteSpace(asset.text))
                return asset.text.Trim();
            return Application.isEditor ? "editor" : "none";
        }
    }
}
