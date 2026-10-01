using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using CncSim.Core.Parsing;
using CncSim.Core.Stock;
using CncSim.Core.Tooling;
using CncSim.Core.Trajectory;

namespace CncSim.Runtime.UI
{
    /// <summary>
    /// UI 面板基类：提供主题、本地化、上下文引用，以及"构建一次、语言/主题变化时刷新"的通用机制。
    /// 子类实现 Build() 组装控件；调用 RefreshTexts() 重设多语言文本。中文/英文切换时，
    /// 通过 RegisterText 注册的委托会被重新执行。
    /// </summary>
    public abstract class CncUiPanel
    {
        protected RectTransform Root { get; private set; }
        protected CncUiContext Ctx { get; private set; }
        protected CncUiTheme Theme => Ctx.Theme;

        private readonly List<Action> _textRefreshers = new List<Action>();

        public void Attach(RectTransform root, CncUiContext ctx)
        {
            Root = root;
            Ctx = ctx;
            _textRefreshers.Clear();
            Build();
        }

        protected abstract void Build();

        /// <summary>注册一个文本刷新委托（语言切换时重新执行，用于设置 TMP 文本）。</summary>
        protected void RegisterText(Action refresh) => _textRefreshers.Add(refresh);

        /// <summary>注册一个 TMP 文本与本地化键的绑定。</summary>
        protected TextMeshProUGUI BindText(TextMeshProUGUI label, string key) 
        {
            RegisterText(() => label.text = Ctx.T(key));
            label.text = Ctx.T(key);
            return label;
        }

        public virtual void RefreshTexts()
        {
            foreach (var r in _textRefreshers)
            {
                try { r(); } catch (Exception e) { Debug.LogWarning(e); }
            }
        }

        /// <summary>面板可见时每帧调用，用于轮询仿真进度、状态、坐标等实时数据。默认空实现。</summary>
        public virtual void Tick(float deltaTime)
        {
        }

        /// <summary>面板被切换隐藏时调用，可用于退订事件。</summary>
        public virtual void OnHide()
        {
        }

        /// <summary>语言或主题变化后整体重建（默认仅刷新文本；需要换肤时子类可重写）。</summary>
        public virtual void OnThemeChanged()
        {
        }

        // ---------- 常用组合控件 ----------

        protected TextMeshProUGUI Section(string key)
        {
            var t = UIFactory.Label(Root, Ctx.T(key), Theme, 13f, true);
            UIFactory.Size(t.gameObject, minH: 22f, prefH: 22f);
            BindText(t, key);
            return t;
        }

        protected Button Btn(Transform parent, string key, Action onClick, bool primary = false, float h = 30f)
        {
            var b = UIFactory.Button(parent, Ctx.T(key), Theme, onClick, h, primary);
            var t = b.GetComponentInChildren<TextMeshProUGUI>();
            if (t != null) BindText(t, key);
            return b;
        }

        protected void Row(Action<Transform> content, float height = -1, float spacing = 6f)
        {
            var row = UIFactory.Node(Root, "Row");
            var h = UIFactory.HStack(row, spacing, new RectOffset(0, 0, 0, 0));
            if (height > 0) UIFactory.Size(row.gameObject, minH: height, prefH: height);
            content(row);
        }

        protected Toggle Chk(string key, bool value, Action<bool> onChange)
        {
            var t = UIFactory.Toggle(Root, Ctx.T(key), Theme, value, onChange);
            var label = t.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null) BindText(label, key);
            return t;
        }

        protected Slider Sld(string key, float min, float max, float value, Action<float> onChange,
            string fmt = "0.##")
        {
            var s = UIFactory.Slider(Root, Ctx.T(key), Theme, min, max, value, onChange, fmt);
            var labels = s.GetComponentsInChildren<TextMeshProUGUI>();
            if (labels.Length > 0) BindText(labels[0], key);
            return s;
        }
    }
}
