using TMPro;
using UnityEngine;
using UnityEngine.UI;
using CncSim.Core.Parsing;

namespace CncSim.Runtime.UI.Panels
{
    /// <summary>文件面板（功能 1-8）：新建/打开/保存/另存/导出、最近文件、会话恢复、诊断概览、编辑器偏好。</summary>
    public class FilePanel : CncUiPanel
    {
        private const int MaxInlineDiagnostics = 30;

        protected override void Build()
        {
            Section("ui.menu.file");

            Row(t =>
            {
                Btn(t, "ui.file.new", () => { Ctx.Facade.NewFile(); RefreshDiagnostics(); }, true, 30f);
                Btn(t, "ui.file.clear", () => { Ctx.Facade.SetText(string.Empty); RefreshDiagnostics(); }, false, 30f);
            });

            Row(t =>
            {
                Btn(t, "ui.menu.open", () => { Ctx.Facade.OpenFile(DefaultSamplePath()); RefreshDiagnostics(); });
                Btn(t, "ui.menu.save", () => { Ctx.Facade.SaveFile(); },
                    false, 30f);
            }, 30f);

            Row(t =>
            {
                Btn(t, "ui.file.export", () =>
                {
                    string p = System.IO.Path.Combine(Application.persistentDataPath, "cncsim_export.nc");
                    Ctx.Facade.ExportFile(p);
                }, false, 30f);
                Btn(t, "ui.menu.share", () => { Ctx.Facade.ExportForShare(); }, false, 30f);
            }, 30f);

            UIFactory.HLine(Root, Theme.Divider);

            // 诊断概览
            Section("ui.file.diagnostics");
            _diagText = UIFactory.Label(Root, "", Theme, 12f, true);
            _diagText.enableWordWrapping = true;
            UIFactory.Size(_diagText.gameObject, minH: 40f, prefH: 40f);
            RegisterText(RefreshDiagnostics);

            UIFactory.HLine(Root, Theme.Divider);

            // 最近文件
            Section("ui.file.recent");
            var scroll = UIFactory.ScrollList(Root, Theme, out var content);
            UIFactory.Size(scroll.gameObject, minH: 90f, prefH: 140f, flexH: 0f);
            _recentList = content;
            RebuildRecent();

            UIFactory.HLine(Root, Theme.Divider);

            // 会话恢复
            Section("ui.file.recover");
            var recScroll = UIFactory.ScrollList(Root, Theme, out var recContent);
            UIFactory.Size(recScroll.gameObject, minH: 60f, prefH: 100f, flexH: 0f);
            RebuildRecoverable(recContent);

            RefreshDiagnostics();
        }

        private TextMeshProUGUI _diagText;
        private RectTransform _recentList;

        private static string DefaultSamplePath() =>
            System.IO.Path.Combine(Application.persistentDataPath, "sample.nc");

        private void RefreshDiagnostics()
        {
            if (_diagText == null) return;
            var diags = Ctx.Facade.Diagnostics;
            if (diags == null)
            {
                _diagText.text = Ctx.T("ui.analysis.none");
                return;
            }
            _diagText.text = string.Format("E: {0}   W: {1}", diags.ErrorCount, diags.WarningCount);
            RebuildRecent();
        }

        private void RebuildRecent()
        {
            if (_recentList == null) return;
            for (int i = _recentList.childCount - 1; i >= 0; i--)
                Object.Destroy(_recentList.GetChild(i).gameObject);

            var files = Ctx.Facade.GetRecentFiles();
            if (files.Count == 0)
            {
                var empty = UIFactory.Label(_recentList, Ctx.T("ui.analysis.none"), Theme, 12f, true);
                UIFactory.Size(empty.gameObject, minH: 24f, prefH: 24f);
                return;
            }
            foreach (var f in files)
            {
                string path = f;
                var btn = UIFactory.Button(_recentList, System.IO.Path.GetFileName(path), Theme,
                    () => { Ctx.Facade.OpenFile(path); RefreshDiagnostics(); }, 26f, false, 12f);
                var t = btn.GetComponentInChildren<TextMeshProUGUI>();
                t.alignment = TextAlignmentOptions.MidlineLeft;
                t.enableWordWrapping = false;
                t.overflowMode = TextOverflowModes.Ellipsis;
            }
        }

        private void RebuildRecoverable(RectTransform parent)
        {
            var sessions = Ctx.Facade.GetRecoverableSessions();
            if (sessions.Count == 0)
            {
                var empty = UIFactory.Label(parent, Ctx.T("ui.analysis.none"), Theme, 12f, true);
                UIFactory.Size(empty.gameObject, minH: 24f, prefH: 24f);
                return;
            }
            foreach (var s in sessions)
            {
                var session = s;
                string label = session.SavedAtUtc.ToLocalTime().ToString("MM-dd HH:mm");
                UIFactory.Button(parent, label, Theme, () => { Ctx.Facade.RecoverSession(session); RefreshDiagnostics(); },
                    26f, false, 12f).GetComponentInChildren<TextMeshProUGUI>().alignment = TextAlignmentOptions.MidlineLeft;
            }
        }
    }
}
