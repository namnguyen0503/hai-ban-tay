using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using HaiBanTay.Editor.Setup;
using HaiBanTay.Editor.SceneBuilders;
using UnityEditor;
using UnityEditor.Build.Reporting;
using Debug = UnityEngine.Debug;

namespace HaiBanTay.Editor.CI
{
    public static class BuildScript
    {
        public static void BuildWindows() =>
            Build(BuildTarget.StandaloneWindows64, "Builds/Windows", "HaiBanTay.exe");

        /// <summary>Bản Linux để chơi thử trên máy dev; bản nộp vẫn là Windows.</summary>
        public static void BuildLinux() =>
            Build(BuildTarget.StandaloneLinux64, "Builds/Linux", "HaiBanTay.x86_64");

        /// <summary>APK ARM64 IL2CPP/Vulkan; Android mặc định tầng Laptop, texture ASTC và âm thanh Vorbis.</summary>
        public static void BuildAndroid()
        {
            EditorUserBuildSettings.androidBuildSubtarget = MobileTextureSubtarget.ASTC;
            EditorUserBuildSettings.buildAppBundle = false;
            EditorUserBuildSettings.exportAsGoogleAndroidProject = false;
            Build(BuildTarget.Android, "Builds/Android", "HaiBanTay.apk");
        }

        /// <summary>Mã bản dựng mang theo trong bản build (đọc bằng BuildInfo); xóa ngay sau khi build, không vào git.</summary>
        const string BuildIdAsset = "Assets/_Project/Resources/BuildId.txt";

        static string Git(string root, string args)
        {
            using var process = Process.Start(new ProcessStartInfo("git", args)
            {
                WorkingDirectory = root, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true
            });
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
                throw new InvalidOperationException("Không đọc được lịch sử git: " + error);
            return output.Trim();
        }

        static string RepoRoot => Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, "..", ".."));
        static string BuildId => string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("HBT_BUILD_ID"))
            ? "none" : Environment.GetEnvironmentVariable("HBT_BUILD_ID").Trim();

        /// <summary>Sinh version/changelog độc lập, không gọi BuildPipeline. Cùng hàm được gọi trước mỗi build.</summary>
        [MenuItem("HaiBanTay/Release/Prepare Metadata")]
        public static void PrepareReleaseMetadata()
        {
            string root = RepoRoot;
            int revision = int.Parse(Git(root, "rev-list --count HEAD"), CultureInfo.InvariantCulture);
            PlayerSettings.bundleVersion = "0.1." + revision.ToString(CultureInfo.InvariantCulture);
            PlayerSettings.Android.bundleVersionCode = Math.Max(revision, PlayerSettings.Android.bundleVersionCode);
            string history = Git(root, "log -20 --format=\"- %h %s\"");
            File.WriteAllText(Path.Combine(root, "Docs", "CHANGELOG.md"),
                "# Changelog\n\nVersion: " + PlayerSettings.bundleVersion + "\n\nBuild ID: " + BuildId +
                "\n\nGit: " + Git(root, "rev-parse HEAD") +
                "\n\nSinh tự động từ 20 commit gần nhất. Thay đổi chưa commit không nằm trong lịch sử dưới đây.\n\n" + history + "\n");
            Debug.Log($"BuildScript: version {PlayerSettings.bundleVersion}, Build ID {BuildId}");
        }

        /// <summary>Chuẩn bị phát hành trong Editor: import, player/render settings, version và changelog. Không build player.</summary>
        public static void PrepareRelease()
        {
            ProjectSetup.ApplyPlayerSettings();
            ProjectSetup.ApplyRenderTiers();
            Import.ImportBudget.ApplyAndroidBudget();
            PrepareReleaseMetadata();
            AssetDatabase.SaveAssets();
        }

        static void Build(BuildTarget target, string outputRelative, string exeName)
        {
            var watch = Stopwatch.StartNew();
            string repoRoot = RepoRoot;
            string outDir = Path.Combine(repoRoot, outputRelative);
            string reportPath = Path.Combine(repoRoot, "Logs", "build-report.txt");
            bool dev = Array.IndexOf(Environment.GetCommandLineArgs(), "-hbtDev") >= 0;
            // Tools/unity.sh đặt HBT_BUILD_ID theo Tools/build_id.py (cổng sinh lại); thiếu thì "none".
            string buildId = BuildId;

            try
            {
                PrepareRelease();
                if (Directory.Exists(outDir))
                    Directory.Delete(outDir, true);
                Directory.CreateDirectory(outDir);

                var scenes = new string[SceneBuildAll.BuildScenes.Length];
                for (int i = 0; i < scenes.Length; i++)
                    scenes[i] = BuilderUtils.ScenePath(SceneBuildAll.BuildScenes[i]);

                var options = new BuildPlayerOptions
                {
                    scenes = scenes,
                    locationPathName = Path.Combine(outDir, exeName),
                    target = target,
                    options = dev ? BuildOptions.Development : BuildOptions.None
                };

                File.WriteAllText(Path.Combine(repoRoot, "Game", BuildIdAsset), buildId);
                AssetDatabase.ImportAsset(BuildIdAsset, ImportAssetOptions.ForceSynchronousImport);
                BuildReport report;
                try { report = BuildPipeline.BuildPlayer(options); }
                finally { AssetDatabase.DeleteAsset(BuildIdAsset); }
                BuildSummary s = report.summary;
                bool ok = s.result == BuildResult.Succeeded;
                // Thư mục debug của Burst không được phát hành; bỏ đi để chép cả thư mục build sang laptop cho an toàn.
                foreach (var dir in Directory.GetDirectories(outDir, "*_DoNotShip"))
                    Directory.Delete(dir, true);
                Directory.CreateDirectory(Path.GetDirectoryName(reportPath));
                File.WriteAllText(reportPath,
                    $"Target: {target}\nKết quả: {s.result}\nDev build: {dev}\nVersion: {PlayerSettings.bundleVersion}\nBuild ID: {buildId}\nBuild GUID: {s.guid}\n" +
                    $"Thời gian: {watch.Elapsed.TotalSeconds:0.0} s\n" +
                    $"Kích thước: {s.totalSize / (1024.0 * 1024.0):0.0} MB\nLỗi: {s.totalErrors}\nCảnh báo: {s.totalWarnings}\n");
                // Sổ các bản build: ghi chú debug từ máy thật mang build_guid/build_id tra ngược về đây.
                File.AppendAllText(Path.Combine(repoRoot, "Logs", "builds.tsv"),
                    $"{DateTime.Now:yyyy-MM-dd HH:mm}\t{target}\t{(dev ? "dev" : "release")}\t{buildId}\t{s.guid}\t{s.result}\t{PlayerSettings.bundleVersion}\n");
                Debug.Log($"BuildScript: {s.result}");
                EditorApplication.Exit(ok ? 0 : 1);
            }
            catch (Exception e)
            {
                Debug.LogError("BuildScript thất bại: " + e);
                EditorApplication.Exit(1);
            }
        }
    }
}
