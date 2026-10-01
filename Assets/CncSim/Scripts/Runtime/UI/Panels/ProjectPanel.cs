using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using CncSim.Core.Projects;

namespace CncSim.Runtime.UI.Panels
{
    /// <summary>项目面板（功能 77）：项目列表、新建/载入/保存/复制/删除/重命名。</summary>
    public class ProjectPanel : CncUiPanel
    {
        private RectTransform _list;
        private TMP_InputField _nameField;

        protected override void Build()
        {
            Section("ui.project.name");
            _nameField = UIFactory.Input(Root, Theme, "", "Part A", null, 30f);
            UIFactory.Size(_nameField.gameObject, minH: 30f, prefH: 30f);

            Row(t =>
            {
                Btn(t, "ui.project.create", () =>
                {
                    var p = Ctx.Facade.CreateProject(_nameField.text);
                    if (p != null) { Ctx.Facade.SaveToProject(p); }
                    Rebuild();
                }, true, 28f);
            }, 30f);

            UIFactory.HLine(Root, Theme.Divider);
            Section("ui.project.manager");

            var scroll = UIFactory.ScrollList(Root, Theme, out _list);
            UIFactory.Size(scroll.gameObject, minH: 140f, prefH: 220f, flexH: 0f);
            Rebuild();
        }

        public override void Tick(float deltaTime)
        {
        }

        private void Rebuild()
        {
            if (_list == null) return;
            for (int i = _list.childCount - 1; i >= 0; i--)
                Object.Destroy(_list.GetChild(i).gameObject);

            var projects = Ctx.Facade.ProjectList;
            if (projects == null || projects.Count == 0)
            {
                var empty = UIFactory.Label(_list, Ctx.T("ui.project.empty"), Theme, 12f, true);
                UIFactory.Size(empty.gameObject, minH: 24f, prefH: 24f);
                return;
            }

            foreach (var p in projects)
            {
                var project = p;
                var card = UIFactory.Node(_list, "Project_" + project.Id);
                UIFactory.VStack(card, 3f, new RectOffset(4, 4, 4, 4));
                UIFactory.Size(card.gameObject, minH: 74f, prefH: 74f);

                var title = UIFactory.Label(card, project.DisplayName, Theme, 13f, false);
                UIFactory.Size(title.gameObject, minH: 20f, prefH: 20f);

                var sub = UIFactory.Label(card, project.ModifiedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
                    Theme, 11f, true);
                UIFactory.Size(sub.gameObject, minH: 16f, prefH: 16f);

                var actions = UIFactory.Node(card, "Actions");
                UIFactory.HStack(actions, 4f);
                UIFactory.Size(actions.gameObject, minH: 26f, prefH: 26f);

                var load = UIFactory.Button(actions, Ctx.T("ui.project.load"), Theme, () =>
                {
                    Ctx.Facade.LoadFromProject(project);
                }, 24f, true, 11f);
                UIFactory.Size(load.gameObject, flexW: 1f, minH: 24f, prefH: 24f);

                var save = UIFactory.Button(actions, Ctx.T("ui.project.save"), Theme, () =>
                {
                    Ctx.Facade.SaveToProject(project);
                    Rebuild();
                }, 24f, false, 11f);
                UIFactory.Size(save.gameObject, flexW: 1f, minH: 24f, prefH: 24f);

                var dup = UIFactory.Button(actions, Ctx.T("ui.project.duplicate"), Theme, () =>
                {
                    Ctx.Facade.DuplicateProject(project);
                    Rebuild();
                }, 24f, false, 11f);
                UIFactory.Size(dup.gameObject, flexW: 1f, minH: 24f, prefH: 24f);

                var del = UIFactory.Button(actions, Ctx.T("ui.project.delete"), Theme, () =>
                {
                    Ctx.Facade.DeleteProject(project);
                    Rebuild();
                }, 24f, false, 11f);
                UIFactory.Size(del.gameObject, flexW: 1f, minH: 24f, prefH: 24f);

                UIFactory.HLine(card, Theme.Divider);
            }
        }
    }
}
