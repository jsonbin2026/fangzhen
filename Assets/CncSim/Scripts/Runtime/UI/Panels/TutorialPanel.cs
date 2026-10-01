using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using CncSim.Core.Tutorial;

namespace CncSim.Runtime.UI.Panels
{
    /// <summary>教程面板（功能 94）：教程列表、分步引导（标题/正文/上一步/下一步/进度）。</summary>
    public class TutorialPanel : CncUiPanel
    {
        private TutorialLibrary.Session _session;
        private TextMeshProUGUI _stepTitle, _stepBody;
        private RectTransform _stepButtons;
        private TextMeshProUGUI _progressText;

        protected override void Build()
        {
            Section("ui.tutorial.list");

            var scroll = UIFactory.ScrollList(Root, Theme, out var list);
            UIFactory.Size(scroll.gameObject, minH: 90f, prefH: 130f, flexH: 0f);
            foreach (var tut in Ctx.Facade.Tutorials)
            {
                var t = tut;
                var btn = UIFactory.Button(list, Ctx.T(t.TitleKey), Theme, () => StartTutorial(t), 28f, false, 12f);
                var txt = btn.GetComponentInChildren<TextMeshProUGUI>();
                txt.alignment = TextAlignmentOptions.MidlineLeft;
                txt.enableWordWrapping = false;
                txt.overflowMode = TextOverflowModes.Ellipsis;
            }

            UIFactory.HLine(Root, Theme.Divider);

            _stepTitle = UIFactory.Label(Root, "", Theme, 14f, false);
            _stepTitle.fontStyle = FontStyles.Bold;
            UIFactory.Size(_stepTitle.gameObject, minH: 22f, prefH: 22f);

            _stepBody = UIFactory.Label(Root, "", Theme, 12f, true);
            _stepBody.enableWordWrapping = true;
            UIFactory.Size(_stepBody.gameObject, minH: 80f, prefH: 80f);

            _progressText = UIFactory.Label(Root, "", Theme, 11f, true);
            UIFactory.Size(_progressText.gameObject, minH: 18f, prefH: 18f);

            _stepButtons = UIFactory.Node(Root, "StepButtons");
            UIFactory.HStack(_stepButtons, 6f);
            UIFactory.Size(_stepButtons.gameObject, minH: 30f, prefH: 30f);

            Btn(_stepButtons, "ui.tutorial.prev", () => { _session?.Previous(); RefreshStep(); }, false, 28f);
            Btn(_stepButtons, "ui.tutorial.start", () => { _session?.Reset(); RefreshStep(); }, false, 28f);
            Btn(_stepButtons, "ui.tutorial.next", () => { _session?.Next(); RefreshStep(); }, true, 28f);

            RefreshStep();
        }

        private void StartTutorial(Tutorial tut)
        {
            _session = Ctx.Facade.StartTutorial(tut.Id);
            RefreshStep();
        }

        private void RefreshStep()
        {
            if (_session == null)
            {
                if (_stepTitle != null) _stepTitle.text = "";
                if (_stepBody != null) _stepBody.text = Ctx.T("ui.status.no_program");
                if (_progressText != null) _progressText.text = "";
                return;
            }

            var step = _session.CurrentStep;
            if (_stepTitle != null) _stepTitle.text = step != null ? step.Title : _session.Current.Title;
            if (_stepBody != null) _stepBody.text = step != null ? step.Body : _session.Current.Description;
            if (_progressText != null)
                _progressText.text = Mathf.Min(_session.Index + 1, _session.Current.Steps.Count) +
                                     " / " + _session.Current.Steps.Count;
        }
    }
}
