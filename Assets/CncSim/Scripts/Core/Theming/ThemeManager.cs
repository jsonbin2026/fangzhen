using System;
using System.Collections.Generic;
using CncSim.Core.CodeEditor;

namespace CncSim.Core.Theming
{
    public enum AppTheme
    {
        Dark,
        Light
    }

    /// <summary>
    /// 主题管理（功能 93 暗色/亮色主题）。Core 层只提供颜色值，UI 层负责应用到具体控件。
    /// </summary>
    public class ThemeManager
    {
        public AppTheme Current { get; private set; } = AppTheme.Dark;

        public event Action<ThemeManager> ThemeChanged;

        /// <summary>编辑器语法高亮方案，随主题切换。</summary>
        public HighlightScheme HighlightScheme { get; private set; }

        public ThemePalette Palette { get; private set; }

        public ThemeManager(AppTheme theme = AppTheme.Dark)
        {
            Apply(theme, raiseEvent: false);
        }

        public void Toggle()
        {
            Apply(Current == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark);
        }

        public void Apply(AppTheme theme, bool raiseEvent = true)
        {
            Current = theme;
            HighlightScheme = theme == AppTheme.Dark ? HighlightScheme.Dark() : HighlightScheme.Light();
            Palette = theme == AppTheme.Dark ? ThemePalette.Dark() : ThemePalette.Light();
            if (raiseEvent) ThemeChanged?.Invoke(this);
        }

        public void ApplyHighlight(HighlightScheme scheme)
        {
            HighlightScheme = scheme ?? HighlightScheme;
            ThemeChanged?.Invoke(this);
        }
    }

    /// <summary>界面配色板。</summary>
    [Serializable]
    public class ThemePalette
    {
        public string Background = "#1E1E1E";
        public string PanelBackground = "#252526";
        public string ToolbarBackground = "#333333";
        public string Foreground = "#D4D4D4";
        public string Accent = "#0E639C";
        public string AccentText = "#FFFFFF";
        public string Border = "#3E3E42";
        public string Warning = "#D7BA7D";
        public string Error = "#F44747";
        public string Success = "#4EC9B0";
        public string GizmoX = "#E03C31";
        public string GizmoY = "#3CCB3C";
        public string GizmoZ = "#3C5CCB";

        public static ThemePalette Dark() => new ThemePalette();

        public static ThemePalette Light() => new ThemePalette
        {
            Background = "#FFFFFF",
            PanelBackground = "#F3F3F3",
            ToolbarBackground = "#E8E8E8",
            Foreground = "#1E1E1E",
            Accent = "#007ACC",
            AccentText = "#FFFFFF",
            Border = "#CCCCCC",
            Warning = "#795E26",
            Error = "#E51400",
            Success = "#098658",
            GizmoX = "#C62828",
            GizmoY = "#2E7D32",
            GizmoZ = "#1565C0"
        };
    }
}
