using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace CncSim.Core.Localization
{
    /// <summary>
    /// 多语言服务。内置 zh-CN 与 en-US 词条，也可以从 "key=value" 格式文本加载/覆盖任意语言。
    /// 找不到当前语言词条时回退 en-US，仍找不到时返回 key 本身。
    /// </summary>
    public static class Loc
    {
        public const string Chinese = "zh-CN";
        public const string English = "en-US";

        private static readonly Dictionary<string, Dictionary<string, string>> Tables =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        private static string _language = Chinese;

        public static event Action<string> LanguageChanged;

        static Loc()
        {
            BuiltinStrings.Register();
        }

        public static string Language
        {
            get => _language;
            set
            {
                if (string.Equals(_language, value, StringComparison.OrdinalIgnoreCase)) return;
                _language = value;
                LanguageChanged?.Invoke(value);
            }
        }

        public static IEnumerable<string> AvailableLanguages => Tables.Keys;

        public static void Add(string language, string key, string value)
        {
            if (!Tables.TryGetValue(language, out var table))
            {
                table = new Dictionary<string, string>(StringComparer.Ordinal);
                Tables[language] = table;
            }
            table[key] = value;
        }

        public static void AddRange(string language, IDictionary<string, string> entries)
        {
            foreach (var kv in entries) Add(language, kv.Key, kv.Value);
        }

        /// <summary>从 "key=value" 文本加载（# 开头为注释，value 中 \n 转为换行）。</summary>
        public static void LoadFromText(string language, string content)
        {
            using (var reader = new StringReader(content ?? string.Empty))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    line = line.Trim();
                    if (line.Length == 0 || line[0] == '#') continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    Add(language, line.Substring(0, eq).Trim(), line.Substring(eq + 1).Trim().Replace("\\n", "\n"));
                }
            }
        }

        public static bool Has(string key) => TryGet(_language, key, out _) || TryGet(English, key, out _);

        public static string Get(string key)
        {
            if (TryGet(_language, key, out var v)) return v;
            if (TryGet(English, key, out v)) return v;
            return key;
        }

        public static string Format(string key, params object[] args)
        {
            string fmt = Get(key);
            if (args == null || args.Length == 0) return fmt;
            try
            {
                return string.Format(CultureInfo.InvariantCulture, fmt, args);
            }
            catch (FormatException)
            {
                return fmt;
            }
        }

        private static bool TryGet(string lang, string key, out string value)
        {
            value = null;
            return lang != null && Tables.TryGetValue(lang, out var t) && t.TryGetValue(key, out value);
        }
    }
}
