using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CncSim.Runtime.UI
{
    /// <summary>
    /// UGUI 控件工厂：以代码方式创建常用控件（面板、文本、按钮、开关、滑杆、下拉框、滚动列表、输入框），
    /// 统一套用 CncUiTheme 主题色，避免依赖 Prefab，便于在任意分辨率下自适应。
    /// 所有控件都返回其根 RectTransform 或组件，调用方自行设置布局。
    /// </summary>
    public static class UIFactory
    {
        public const float TopBarHeight = 44f;
        public const float BottomBarHeight = 64f;
        public const float TabRailWidth = 92f;
        public const float PanelWidth = 340f;

        // ---------------- 基础节点 ----------------

        public static RectTransform Node(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        public static Image Panel(Transform parent, string name, Color color, bool raycast = true)
        {
            var rt = Node(parent, name);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = raycast;
            return img;
        }

        public static Graphic Stretch(Graphic graphic)
        {
            var rt = graphic.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return graphic;
        }

        public static RectTransform HLine(Transform parent, Color color, float thickness = 1f)
        {
            var img = Panel(parent, "Divider", color, false);
            var le = img.gameObject.AddComponent<LayoutElement>();
            le.minHeight = thickness;
            le.preferredHeight = thickness;
            le.flexibleHeight = 0f;
            return img.rectTransform;
        }

        public static RectTransform Spacer(Transform parent, float height, float flex = 0f)
        {
            var rt = Node(parent, "Spacer");
            var le = rt.gameObject.AddComponent<LayoutElement>();
            le.minHeight = height;
            le.preferredHeight = height;
            le.flexibleHeight = flex;
            return rt;
        }

        // ---------------- 文本 ----------------

        public static TextMeshProUGUI Text(Transform parent, string content, CncUiTheme theme,
            float size = 14f, TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft, bool muted = false)
        {
            var rt = Node(parent, "Text");
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.text = content;
            t.fontSize = size;
            t.color = muted ? theme.TextMuted : theme.Text;
            t.alignment = align;
            t.raycastTarget = false;
            t.enableWordWrapping = true;
            return t;
        }

        public static TextMeshProUGUI Label(Transform parent, string content, CncUiTheme theme, float size = 14f,
            bool muted = false)
        {
            return Text(parent, content, theme, size, TextAlignmentOptions.MidlineLeft, muted);
        }

        // ---------------- 布局辅助 ----------------

        public static VerticalLayoutGroup VStack(RectTransform rt, float spacing = 6f, RectOffset padding = null,
            TextAnchor align = TextAnchor.UpperLeft)
        {
            var g = rt.gameObject.AddComponent<VerticalLayoutGroup>();
            g.spacing = spacing;
            g.padding = padding ?? new RectOffset(8, 8, 8, 8);
            g.childControlWidth = true;
            g.childControlHeight = false;
            g.childForceExpandWidth = true;
            g.childForceExpandHeight = false;
            g.childAlignment = align;
            return g;
        }

        public static HorizontalLayoutGroup HStack(RectTransform rt, float spacing = 6f, RectOffset padding = null,
            TextAnchor align = TextAnchor.MiddleLeft)
        {
            var g = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
            g.spacing = spacing;
            g.padding = padding ?? new RectOffset(0, 0, 0, 0);
            g.childControlWidth = false;
            g.childControlHeight = true;
            g.childForceExpandWidth = false;
            g.childForceExpandHeight = false;
            g.childAlignment = align;
            return g;
        }

        public static LayoutElement Size(GameObject go, float minH = -1, float prefH = -1,
            float minW = -1, float prefW = -1, float flexW = -1, float flexH = -1)
        {
            var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            if (minH >= 0) le.minHeight = minH;
            if (prefH >= 0) le.preferredHeight = prefH;
            if (minW >= 0) le.minWidth = minW;
            if (prefW >= 0) le.preferredWidth = prefW;
            if (flexW >= 0) le.flexibleWidth = flexW;
            if (flexH >= 0) le.flexibleHeight = flexH;
            return le;
        }

        // ---------------- 按钮 ----------------

        public static Button Button(Transform parent, string label, CncUiTheme theme, Action onClick,
            float height = 30f, bool primary = false, float fontSize = 14f)
        {
            var img = Panel(parent, "Button_" + label, primary ? theme.Accent : theme.Button);
            var btn = img.gameObject.AddComponent<Button>();
            var colors = btn.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = primary ? Shade(theme.Accent, 1.12f) : theme.ButtonHover;
            colors.pressedColor = Shade(primary ? theme.Accent : theme.Button, 0.85f);
            colors.selectedColor = colors.normalColor;
            colors.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.4f);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.06f;
            btn.colors = colors;
            btn.targetGraphic = img;
            if (onClick != null) btn.onClick.AddListener(() => onClick());

            var t = Text(img.transform, label, theme, fontSize, TextAlignmentOptions.Center, false);
            t.color = primary ? theme.AccentText : theme.Text;
            Stretch(t);
            Size(img.gameObject, minH: height, prefH: height);
            return btn;
        }

        public static Button IconButton(Transform parent, string glyph, CncUiTheme theme, Action onClick,
            float size = 34f, string tooltip = null)
        {
            var btn = Button(parent, glyph, theme, onClick, size, false, 18f);
            Size(btn.gameObject, minW: size, prefW: size, flexW: 0f);
            if (!string.IsNullOrEmpty(tooltip))
            {
                var trigger = btn.gameObject.AddComponent<SimpleTooltip>();
                trigger.Text = tooltip;
            }
            return btn;
        }

        // ---------------- 开关 ----------------

        public static Toggle Toggle(Transform parent, string label, CncUiTheme theme, bool value, Action<bool> onChange)
        {
            var row = Node(parent, "Toggle_" + label);
            var h = HStack(row, 8f, new RectOffset(0, 0, 0, 0));
            Size(row.gameObject, minH: 28f, prefH: 28f);

            var box = Panel(row, "Box", theme.Button);
            Size(box.gameObject, minW: 24f, prefW: 24f, flexW: 0f, minH: 24f, prefH: 24f);
            var check = Panel(box.transform, "Check", theme.Accent);
            check.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            check.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            check.rectTransform.sizeDelta = new Vector2(14f, 14f);
            var toggle = box.gameObject.AddComponent<Toggle>();
            toggle.targetGraphic = box;
            toggle.graphic = check;

            var t = Label(row, label, theme, 14f);
            Size(t.gameObject, flexW: 1f);

            var le = box.gameObject.GetComponent<LayoutElement>();
            le.flexibleWidth = 0f;

            toggle.isOn = value;
            toggle.onValueChanged.AddListener(v => onChange?.Invoke(v));
            return toggle;
        }

        // ---------------- 滑杆 ----------------

        public static Slider Slider(Transform parent, string label, CncUiTheme theme,
            float min, float max, float value, Action<float> onChange, string valueFormat = "0.##")
        {
            var row = Node(parent, "Slider_" + label);
            Size(row.gameObject, minH: 44f, prefH: 44f);
            var v = VStack(row, 2f, new RectOffset(0, 0, 0, 0));
            v.childForceExpandWidth = true;

            var head = Node(row, "Head");
            var hh = HStack(head, 6f, new RectOffset(0, 0, 0, 0));
            Size(head.gameObject, minH: 18f, prefH: 18f);
            var lt = Label(head, label, theme, 13f);
            Size(lt.gameObject, flexW: 1f);
            var vt = Label(head, value.ToString(valueFormat), theme, 13f, true);
            vt.alignment = TextAlignmentOptions.MidlineRight;
            Size(vt.gameObject, minW: 56f, prefW: 56f, flexW: 0f);

            var sliderRt = Node(row, "Slider");
            Size(sliderRt.gameObject, minH: 20f, prefH: 20f);
            var slider = sliderRt.gameObject.AddComponent<Slider>();

            var bg = Panel(sliderRt, "Background", theme.Button, false);
            bg.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            bg.rectTransform.anchorMax = new Vector2(1f, 0.5f);
            bg.rectTransform.sizeDelta = new Vector2(0f, 6f);
            var fillArea = Node(sliderRt, "Fill Area");
            fillArea.anchorMin = new Vector2(0f, 0.5f);
            fillArea.anchorMax = new Vector2(1f, 0.5f);
            fillArea.sizeDelta = new Vector2(-16f, 6f);
            var fill = Panel(fillArea, "Fill", theme.Accent, false);
            fill.rectTransform.anchorMin = Vector2.zero;
            fill.rectTransform.anchorMax = new Vector2(1f, 1f);
            fill.rectTransform.sizeDelta = Vector2.zero;
            var handleArea = Node(sliderRt, "Handle Slide Area");
            handleArea.anchorMin = Vector2.zero;
            handleArea.anchorMax = Vector2.one;
            handleArea.sizeDelta = new Vector2(-16f, 0f);
            var handle = Panel(handleArea, "Handle", theme.Text);
            handle.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            handle.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            handle.rectTransform.sizeDelta = new Vector2(16f, 16f);

            slider.fillRect = fill.rectTransform;
            slider.handleRect = handle.rectTransform;
            slider.targetGraphic = handle;
            slider.direction = UnityEngine.UI.Slider.Direction.LeftToRight;
            slider.minValue = min;
            slider.maxValue = max;
            slider.wholeNumbers = false;
            slider.value = value;
            slider.onValueChanged.AddListener(x =>
            {
                vt.text = x.ToString(valueFormat);
                onChange?.Invoke(x);
            });
            return slider;
        }

        // ---------------- 下拉框 ----------------

        public static TMP_Dropdown Dropdown(Transform parent, string label, CncUiTheme theme,
            string[] options, int index, Action<int> onChange)
        {
            var row = Node(parent, "Dropdown_" + label);
            Size(row.gameObject, minH: 28f, prefH: 28f);
            var h = HStack(row, 8f, new RectOffset(0, 0, 0, 0));

            if (!string.IsNullOrEmpty(label))
            {
                var lt = Label(row, label, theme, 13f);
                Size(lt.gameObject, minW: 84f, prefW: 84f, flexW: 0f);
            }

            var box = Panel(row, "Dropdown", theme.Button);
            Size(box.gameObject, flexW: 1f, minH: 28f, prefH: 28f);
            var dd = box.gameObject.AddComponent<TMP_Dropdown>();

            var captionRt = Node(box.transform, "Label");
            captionRt.anchorMin = Vector2.zero;
            captionRt.anchorMax = Vector2.one;
            captionRt.offsetMin = new Vector2(8f, 0f);
            captionRt.offsetMax = new Vector2(-22f, 0f);
            var caption = captionRt.gameObject.AddComponent<TextMeshProUGUI>();
            caption.color = theme.Text;
            caption.fontSize = 13f;
            caption.alignment = TextAlignmentOptions.MidlineLeft;
            caption.raycastTarget = false;

            var arrow = Text(box.transform, "▾", theme, 12f, TextAlignmentOptions.Center, true);
            arrow.rectTransform.anchorMin = new Vector2(1f, 0f);
            arrow.rectTransform.anchorMax = new Vector2(1f, 1f);
            arrow.rectTransform.pivot = new Vector2(1f, 0.5f);
            arrow.rectTransform.sizeDelta = new Vector2(22f, 0f);
            arrow.rectTransform.anchoredPosition = new Vector2(-4f, 0f);

            var template = BuildDropdownTemplate(dd, theme);
            dd.template = template;
            dd.captionText = caption;
            dd.targetGraphic = box;

            dd.options.Clear();
            foreach (var o in options) dd.options.Add(new TMP_Dropdown.OptionData(o));
            if (options.Length > 0)
            {
                index = Mathf.Clamp(index, 0, options.Length - 1);
                dd.value = index;
                dd.RefreshShownValue();
            }
            dd.onValueChanged.AddListener(i => onChange?.Invoke(i));
            return dd;
        }

        private static RectTransform BuildDropdownTemplate(TMP_Dropdown dd, CncUiTheme theme)
        {
            var templateRt = Node(dd.transform, "Template");
            var bg = templateRt.gameObject.AddComponent<Image>();
            bg.color = theme.PanelAlt;
            var scroll = templateRt.gameObject.AddComponent<ScrollRect>();
            templateRt.anchorMin = new Vector2(0f, 0f);
            templateRt.anchorMax = new Vector2(1f, 0f);
            templateRt.pivot = new Vector2(0.5f, 1f);
            templateRt.anchoredPosition = new Vector2(0f, 2f);
            templateRt.sizeDelta = new Vector2(0f, 160f);
            var le = templateRt.gameObject.AddComponent<LayoutElement>();
            le.ignoreLayout = true;

            var viewport = Node(templateRt, "Viewport");
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.offsetMin = Vector2.zero;
            viewport.offsetMax = Vector2.zero;
            viewport.gameObject.AddComponent<RectMask2D>();
            var vpImg = viewport.gameObject.AddComponent<Image>();
            vpImg.color = new Color(0f, 0f, 0f, 0.001f);

            var content = Node(viewport, "Content");
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.sizeDelta = new Vector2(0f, 28f);
            var vlayout = VStack(content, 0f, new RectOffset(0, 0, 0, 0));
            vlayout.childControlHeight = true;

            var item = Node(content, "Item");
            var itemBg = item.gameObject.AddComponent<Image>();
            itemBg.color = new Color(0f, 0f, 0f, 0.001f);
            Size(item.gameObject, minH: 28f, prefH: 28f);
            var itemToggle = item.gameObject.AddComponent<Toggle>();

            var itemCheck = Panel(item, "Item Checkmark", theme.Accent, false);
            itemCheck.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            itemCheck.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            itemCheck.rectTransform.pivot = new Vector2(0f, 0.5f);
            itemCheck.rectTransform.anchoredPosition = new Vector2(6f, 0f);
            itemCheck.rectTransform.sizeDelta = new Vector2(10f, 10f);

            var itemLabelRt = Node(item, "Item Label");
            itemLabelRt.anchorMin = Vector2.zero;
            itemLabelRt.anchorMax = Vector2.one;
            itemLabelRt.offsetMin = new Vector2(22f, 0f);
            itemLabelRt.offsetMax = new Vector2(-6f, 0f);
            var itemLabel = itemLabelRt.gameObject.AddComponent<TextMeshProUGUI>();
            itemLabel.color = theme.Text;
            itemLabel.fontSize = 13f;
            itemLabel.alignment = TextAlignmentOptions.MidlineLeft;

            itemToggle.targetGraphic = itemBg;
            itemToggle.graphic = itemCheck;

            scroll.content = content;
            scroll.viewport = viewport;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;

            dd.template = templateRt;
            dd.itemText = itemLabel;
            dd.itemImage = itemBg;
            templateRt.gameObject.SetActive(false);
            return templateRt;
        }

        // ---------------- 滚动列表 ----------------

        public static ScrollRect ScrollList(Transform parent, CncUiTheme theme, out RectTransform content)
        {
            var root = Node(parent, "ScrollList");
            var scroll = root.gameObject.AddComponent<ScrollRect>();
            var bg = root.gameObject.AddComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0f);
            Size(root.gameObject, flexH: 1f, minH: 80f);

            var viewport = Node(root, "Viewport");
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.offsetMin = Vector2.zero;
            viewport.offsetMax = new Vector2(-6f, 0f);
            viewport.gameObject.AddComponent<RectMask2D>();
            var vpImg = viewport.gameObject.AddComponent<Image>();
            vpImg.color = new Color(0f, 0f, 0f, 0.001f);

            content = Node(viewport, "Content");
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.sizeDelta = new Vector2(0f, 0f);
            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var vlayout = VStack(content, 4f, new RectOffset(4, 4, 4, 4));
            vlayout.childControlHeight = true;
            vlayout.childForceExpandHeight = false;

            scroll.content = content;
            scroll.viewport = viewport;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.scrollSensitivity = 24f;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            return scroll;
        }

        // ---------------- 输入框 ----------------

        public static TMP_InputField Input(Transform parent, CncUiTheme theme, string text, string placeholder,
            Action<string> onSubmit, float height = 30f, bool multiline = false)
        {
            var box = Panel(parent, "Input", theme.PanelAlt);
            Size(box.gameObject, minH: height, prefH: height);
            var field = box.gameObject.AddComponent<TMP_InputField>();

            var textArea = Node(box.transform, "Text Area");
            textArea.anchorMin = Vector2.zero;
            textArea.anchorMax = Vector2.one;
            textArea.offsetMin = new Vector2(8f, 2f);
            textArea.offsetMax = new Vector2(-8f, -2f);
            if (!multiline) textArea.gameObject.AddComponent<RectMask2D>();

            var phRt = Node(textArea, "Placeholder");
            phRt.anchorMin = Vector2.zero;
            phRt.anchorMax = Vector2.one;
            phRt.offsetMin = Vector2.zero;
            phRt.offsetMax = Vector2.zero;
            var ph = phRt.gameObject.AddComponent<TextMeshProUGUI>();
            ph.text = placeholder;
            ph.color = theme.TextMuted;
            ph.fontSize = 14f;
            ph.raycastTarget = false;
            ph.alignment = TextAlignmentOptions.MidlineLeft;

            var txRt = Node(textArea, "Text");
            txRt.anchorMin = Vector2.zero;
            txRt.anchorMax = Vector2.one;
            txRt.offsetMin = Vector2.zero;
            txRt.offsetMax = Vector2.zero;
            var tx = txRt.gameObject.AddComponent<TextMeshProUGUI>();
            tx.color = theme.Text;
            tx.fontSize = 14f;
            tx.alignment = TextAlignmentOptions.TopLeft;
            tx.raycastTarget = false;
            tx.enableWordWrapping = multiline;

            field.textViewport = textArea;
            field.textComponent = tx;
            field.placeholder = ph;
            field.lineType = multiline ? TMP_InputField.LineType.MultiLineNewline : TMP_InputField.LineType.SingleLine;
            field.targetGraphic = box;
            field.text = text ?? string.Empty;
            if (onSubmit != null)
            {
                field.onEndEdit.AddListener(v => onSubmit(v));
            }
            return field;
        }

        // ---------------- 文本输入行 ----------------

        public static TMP_InputField NumberRow(Transform parent, CncUiTheme theme, string label,
            string value, Action<string> onSubmit)
        {
            var row = Node(parent, "Number_" + label);
            Size(row.gameObject, minH: 28f, prefH: 28f);
            var h = HStack(row, 8f, new RectOffset(0, 0, 0, 0));
            var lt = Label(row, label, theme, 13f);
            Size(lt.gameObject, minW: 84f, prefW: 84f, flexW: 0f);
            var input = Input(row, theme, value, label, onSubmit, 28f);
            Size(input.gameObject, flexW: 1f);
            return input;
        }

        private static Color Shade(Color c, float f)
        {
            return new Color(Mathf.Clamp01(c.r * f), Mathf.Clamp01(c.g * f), Mathf.Clamp01(c.b * f), c.a);
        }
    }

    /// <summary>轻量 tooltip：鼠标/触摸悬停时显示文本。挂到按钮上即可。</summary>
    public class SimpleTooltip : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public string Text;
        private static TextMeshProUGUI _label;

        public void OnPointerEnter(PointerEventData e)
        {
            if (string.IsNullOrEmpty(Text) || _label == null) return;
            _label.transform.parent.gameObject.SetActive(true);
            _label.text = Text;
        }

        public void OnPointerExit(PointerEventData e)
        {
            if (_label != null) _label.transform.parent.gameObject.SetActive(false);
        }

        public static void Bind(TextMeshProUGUI label)
        {
            _label = label;
            if (_label != null) _label.transform.parent.gameObject.SetActive(false);
        }
    }
}
