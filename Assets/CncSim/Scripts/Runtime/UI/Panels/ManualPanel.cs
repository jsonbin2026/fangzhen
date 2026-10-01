using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using CncSim.Core.Parsing;

namespace CncSim.Runtime.UI.Panels
{
    /// <summary>速查手册面板（功能 68-69）：G/M 指令分类速查与搜索。</summary>
    public class ManualPanel : CncUiPanel
    {
        private RectTransform _list;
        private TMP_InputField _search;
        private string _filter = "";

        protected override void Build()
        {
            Section("ui.manual.search");
            _search = UIFactory.Input(Root, Theme, "", Ctx.T("ui.manual.search"), v =>
            {
                _filter = v ?? "";
                Rebuild();
            }, 30f);
            UIFactory.Size(_search.gameObject, minH: 30f, prefH: 30f);

            UIFactory.HLine(Root, Theme.Divider);

            var scroll = UIFactory.ScrollList(Root, Theme, out _list);
            UIFactory.Size(scroll.gameObject, minH: 200f, flexH: 1f);

            Rebuild();
        }

        private void Rebuild()
        {
            if (_list == null) return;
            for (int i = _list.childCount - 1; i >= 0; i--)
                Object.Destroy(_list.GetChild(i).gameObject);

            var groups = Ctx.Facade.Manual();
            foreach (var (titleKey, items) in groups)
            {
                var filtered = Filter(items);
                if (filtered.Count == 0) continue;

                var header = UIFactory.Label(_list, Ctx.T(titleKey), Theme, 13f, false);
                header.fontStyle = FontStyles.Bold;
                UIFactory.Size(header.gameObject, minH: 24f, prefH: 24f);

                foreach (var info in filtered)
                {
                    var row = UIFactory.Node(_list, "Code_" + info.CodeText);
                    UIFactory.Size(row.gameObject, minH: 34f, prefH: 34f);
                    var v = UIFactory.VStack(row, 1f, new RectOffset(6, 4, 2, 2));
                    v.childForceExpandWidth = true;

                    var codeLine = UIFactory.Node(row, "CodeLine");
                    UIFactory.HStack(codeLine, 6f);
                    UIFactory.Size(codeLine.gameObject, minH: 18f, prefH: 18f);
                    var code = UIFactory.Label(codeLine, info.CodeText, Theme, 13f, false);
                    code.fontStyle = FontStyles.Bold;
                    UIFactory.Size(code.gameObject, minW: 54f, prefW: 54f, flexW: 0f);
                    var name = UIFactory.Label(codeLine, info.Name, Theme, 12f, false);
                    UIFactory.Size(name.gameObject, flexW: 1f);
                    name.enableWordWrapping = false;
                    name.overflowMode = TextOverflowModes.Ellipsis;

                    var desc = UIFactory.Label(row, info.Description, Theme, 11f, true);
                    desc.enableWordWrapping = true;
                    UIFactory.Size(desc.gameObject, minH: 14f, prefH: 14f);

                    UIFactory.HLine(_list, Theme.Divider);
                }
            }
        }

        private List<CodeInfo> Filter(List<CodeInfo> items)
        {
            if (string.IsNullOrEmpty(_filter)) return items;
            var result = new List<CodeInfo>();
            foreach (var i in items)
            {
                if (i.CodeText.IndexOf(_filter, System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (i.Name ?? "").IndexOf(_filter, System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (i.Description ?? "").IndexOf(_filter, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    result.Add(i);
            }
            return result;
        }
    }
}
