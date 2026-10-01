using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace CncSim.EditorTools
{
    /// <summary>
    /// 命令行/CI 用的 Android 构建入口。
    /// 用法：
    ///   Unity -quit -batchmode -nographics -projectPath . \
    ///     -executeMethod CncSim.EditorTools.BuildAndroid.Build \
    ///     -logFile build.log
    /// 产物默认输出到 build/Android/CncSim.apk。
    /// </summary>
    public static class BuildAndroid
    {
        private const string Scene = "Assets/Scenes/Main.unity";

        /// <summary>
        /// 确保 TextMeshPro 基础资源已导入（TMP Settings、默认字体等）。
        /// 缺失时运行期 TMP 文本无法显示，因此构建前必须保证存在。
        /// 使用反射调用 TMP 的导入器，避免因不同 TMP 版本 API 可见性差异导致编译失败。
        /// </summary>
        private static void EnsureTmpEssentials()
        {
            const string tmpSettings = "Assets/TextMesh Pro/Resources/TMP Settings.asset";
            if (File.Exists(tmpSettings)) return;

            // 1) 尝试反射调用 TMP_PackageResourceImporter（editor 程序集）
            try
            {
                var importerType = Type.GetType(
                    "TMPro.TMP_PackageResourceImporter, Unity.TextMeshPro.Editor");
                if (importerType != null)
                {
                    var method = importerType.GetMethod(
                        "ImportProjectResources",
                        System.Reflection.BindingFlags.Static |
                        System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.NonPublic);
                    if (method != null)
                    {
                        method.Invoke(null, new object[] { false, false, false });
                        AssetDatabase.Refresh();
                        Debug.Log("[BuildAndroid] Imported TMP Essential Resources via reflection.");
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[BuildAndroid] TMP reflection import failed: " + e.Message);
            }

            // 2) 兜底：执行菜单项
            if (!File.Exists(tmpSettings))
            {
                try
                {
                    EditorApplication.ExecuteMenuItem("Window/TextMeshPro/Import TMP Essential Resources");
                    AssetDatabase.Refresh();
                    Debug.Log("[BuildAndroid] Imported TMP Essential Resources via menu.");
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[BuildAndroid] TMP menu import failed: " + e.Message);
                }
            }

            if (!File.Exists(tmpSettings))
            {
                Debug.LogError("[BuildAndroid] TMP Essential Resources still missing. " +
                               "Please open the project once in the Unity Editor and " +
                               "run Window > TextMeshPro > Import TMP Essential Resources.");
            }
        }

        public static void Build()
        {
            EnsureTmpEssentials();

            var outDir = Path.Combine(Directory.GetCurrentDirectory(), "build", "Android");
            Directory.CreateDirectory(outDir);
            var apkPath = Path.Combine(outDir, "CncSim.apk");

            PlayerSettings.SetApplicationIdentifier(
                BuildTargetGroup.Android, "com.monkeycode.cncsim");
            PlayerSettings.SetScriptingBackend(
                BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel24;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToPortrait = true;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;

            var options = new BuildPlayerOptions
            {
                scenes = new[] { Scene },
                locationPathName = apkPath,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.None
            };

            var report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;
            Debug.Log($"[BuildAndroid] result={summary.result} size={summary.totalSize} " +
                      $"errors={summary.totalErrors} output={summary.outputPath}");

            if (summary.result != BuildResult.Succeeded)
            {
                EditorApplication.Exit(1);
            }
        }
    }
}
