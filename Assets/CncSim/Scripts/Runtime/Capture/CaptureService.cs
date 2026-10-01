using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace CncSim.Runtime
{
    /// <summary>
    /// 截图与仿真视频导出（功能 75 导出视频、功能 76 截图保存）。
    /// - 截图：RenderTexture + ReadPixels + PNG。
    /// - 视频：按固定帧率逐帧抓取 PNG 序列，写入目录；若工程安装了 FFmpeg/插件，
    ///   可订阅 <see cref="FrameCaptured"/> 把帧喂给编码器（如 Unity Recorder / OpenCV）。
    ///   Core 层不依赖任何视频库，保持可编译。
    /// </summary>
    public class CaptureService : MonoBehaviour
    {
        [Header("References")]
        public Camera TargetCamera;

        [Header("Screenshot")]
        [Tooltip("截图超采样倍数，1 表示原分辨率")]
        public int SuperSampling = 2;

        [Header("Video")]
        public int VideoFps = 30;
        public int VideoWidth;
        public int VideoHeight;

        public bool IsRecording { get; private set; }
        public int CapturedFrameCount { get; private set; }

        /// <summary>每抓到一帧视频时触发（参数：帧序号、RGBA 像素、宽、高）。</summary>
        public event Action<int, byte[], int, int> FrameCaptured;
        /// <summary>视频录制结束（参数：输出目录）。</summary>
        public event Action<string> VideoCompleted;

        private string _videoOutputDir;

        private void Awake()
        {
            if (TargetCamera == null) TargetCamera = Camera.main;
        }

        /// <summary>保存单张截图到指定路径（PNG）。默认路径为 persistentDataPath 下带时间戳的文件。</summary>
        public string SaveScreenshot(string path = null)
        {
            if (TargetCamera == null) return null;
            int ss = Mathf.Max(1, SuperSampling);
            int w = Screen.width * ss;
            int h = Screen.height * ss;
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
            var previous = TargetCamera.targetTexture;
            TargetCamera.targetTexture = rt;
            TargetCamera.Render();
            RenderTexture.active = rt;

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();

            TargetCamera.targetTexture = previous;
            RenderTexture.active = null;
            Destroy(rt);

            byte[] png = tex.EncodeToPNG();
            Destroy(tex);

            if (string.IsNullOrEmpty(path))
            {
                string dir = Path.Combine(Application.persistentDataPath, "CncSim", "Screenshots");
                Directory.CreateDirectory(dir);
                path = Path.Combine(dir, "cncsim_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png");
            }
            else
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            }
            File.WriteAllBytes(path, png);
            return path;
        }

        /// <summary>把一个 Texture 编码为 PNG 字节。</summary>
        public static byte[] EncodePNG(Texture2D texture) => texture.EncodeToPNG();

        /// <summary>开始录制视频帧序列（PNG 序列写到目录）。</summary>
        public void StartVideoCapture(string outputDirectory = null)
        {
            _videoOutputDir = string.IsNullOrEmpty(outputDirectory)
                ? Path.Combine(Application.persistentDataPath, "CncSim", "Video", DateTime.Now.ToString("yyyyMMdd_HHmmss"))
                : outputDirectory;
            Directory.CreateDirectory(_videoOutputDir);
            CapturedFrameCount = 0;
            IsRecording = true;
            StartCoroutine(CaptureLoop());
        }

        public void StopVideoCapture()
        {
            IsRecording = false;
            StopAllCoroutines();
            VideoCompleted?.Invoke(_videoOutputDir);
        }

        private IEnumerator CaptureLoop()
        {
            var wait = new WaitForSecondsRealtime(1f / Mathf.Max(1, VideoFps));
            while (IsRecording)
            {
                yield return new WaitForEndOfFrame();
                CaptureOneVideoFrame();
                yield return wait;
            }
        }

        private void CaptureOneVideoFrame()
        {
            if (TargetCamera == null) return;
            int w = VideoWidth > 0 ? VideoWidth : Screen.width;
            int h = VideoHeight > 0 ? VideoHeight : Screen.height;

            var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32);
            var previous = TargetCamera.targetTexture;
            TargetCamera.targetTexture = rt;
            TargetCamera.Render();
            RenderTexture.active = rt;

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();

            TargetCamera.targetTexture = previous;
            RenderTexture.active = null;
            RenderTexture.ReleaseTemporary(rt);

            byte[] pixels = tex.GetRawTextureData();
            string framePath = Path.Combine(_videoOutputDir, "frame_" + CapturedFrameCount.ToString("D5") + ".png");
            File.WriteAllBytes(framePath, tex.EncodeToPNG());
            Destroy(tex);

            FrameCaptured?.Invoke(CapturedFrameCount, pixels, w, h);
            CapturedFrameCount++;
        }

        /// <summary>取最近一次视频输出目录。</summary>
        public string VideoOutputDirectory => _videoOutputDir;
    }
}
