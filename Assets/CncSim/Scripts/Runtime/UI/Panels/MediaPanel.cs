using System.IO;
using TMPro;
using UnityEngine;
using CncSim.Core.Recording;

namespace CncSim.Runtime.UI.Panels
{
    /// <summary>媒体/录制面板（功能 51, 62, 75, 76）：截图、录像帧序列、仿真录制/回放/保存/加载。</summary>
    public class MediaPanel : CncUiPanel
    {
        private TextMeshProUGUI _info;

        protected override void Build()
        {
            Section("ui.media.screenshot");
            Btn(Root, "ui.media.screenshot", () =>
            {
                string p = Ctx.Facade.SaveScreenshot();
                SetInfo(p != null ? Path.GetFileName(p) : Ctx.T("ui.analysis.none"));
            }, true, 30f);

            UIFactory.HLine(Root, Theme.Divider);

            Section("ui.media.video_record");
            Row(t =>
            {
                Btn(t, "ui.media.video_record", () => { Ctx.Facade.StartVideoCapture(); SetInfo("REC"); }, true, 28f);
                Btn(t, "ui.media.video_stop", () => { Ctx.Facade.StopVideoCapture(); SetInfo("STOP"); }, false, 28f);
            }, 30f);

            UIFactory.HLine(Root, Theme.Divider);

            Section("ui.record.start");
            Row(t =>
            {
                Btn(t, "ui.media.sim_start", () => { Ctx.Facade.StartRecording(); SetInfo(Ctx.T("ui.status.recording")); }, true, 28f);
                Btn(t, "ui.media.sim_stop", () => { Ctx.Facade.StopRecording(); SetInfo(""); }, false, 28f);
            }, 30f);

            Row(t =>
            {
                Btn(t, "ui.media.sim_replay", () => { Ctx.Facade.StartReplay(); SetInfo(Ctx.T("ui.status.replaying")); }, false, 28f);
                Btn(t, "ui.record.pause", () => { Ctx.Facade.PauseReplay(); SetInfo(""); }, false, 28f);
            }, 30f);

            Row(t =>
            {
                Btn(t, "ui.media.sim_save", () => { Ctx.Facade.SaveRecording(); SetInfo(Ctx.T("ui.status.saved")); }, false, 28f);
                Btn(t, "ui.media.sim_load", () => { /* 由宿主提供文件选择器 */ }, false, 28f);
            }, 30f);

            Sld("ui.ctrl.speed", 0.25f, 8f, 1f, v => Ctx.Facade.SetReplaySpeed(v), "0.##");

            _info = UIFactory.Label(Root, "", Theme, 12f, true);
            UIFactory.Size(_info.gameObject, minH: 20f, prefH: 20f);
        }

        private void SetInfo(string text)
        {
            if (_info != null) _info.text = text;
        }

        public override void Tick(float deltaTime)
        {
            if (_info == null) return;
            if (Ctx.Capture != null && Ctx.Capture.IsRecording)
                _info.text = "REC " + Ctx.Capture.CapturedFrameCount + " frames";
        }
    }
}
