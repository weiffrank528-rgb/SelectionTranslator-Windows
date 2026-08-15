using System;

namespace SelectionTranslator
{
    internal static class LanguageDetection
    {
        internal static bool IsAutomatic(AppSettings settings)
        {
            return settings != null && (settings.AutoDetectSourceLanguage
                || string.IsNullOrWhiteSpace(settings.SourceLanguage)
                || string.Equals(settings.SourceLanguage, "auto", StringComparison.OrdinalIgnoreCase));
        }

        internal static string ResolveSourceLanguage(string text, AppSettings settings)
        {
            if (!IsAutomatic(settings)) return settings.SourceLanguage.Trim();
            return DetectChineseOrEnglish(text);
        }

        internal static string ResolveTargetLanguage(string sourceLanguage, AppSettings settings)
        {
            var configuredTarget = settings == null ? "" : (settings.TargetLanguage ?? "").Trim();
            if (!IsAutomatic(settings)) return configuredTarget;

            if (IsEnglish(sourceLanguage) && IsEnglish(configuredTarget)) return "zh-CN";
            if (IsChinese(sourceLanguage) && IsChinese(configuredTarget)) return "en";
            return configuredTarget;
        }

        internal static string DetectChineseOrEnglish(string text)
        {
            if (!string.IsNullOrEmpty(text))
            {
                foreach (var character in text)
                {
                    if ((character >= '\u3400' && character <= '\u4DBF')
                        || (character >= '\u4E00' && character <= '\u9FFF')
                        || (character >= '\uF900' && character <= '\uFAFF'))
                        return "zh-CN";
                }
            }
            return "en";
        }

        internal static string DisplayName(string language)
        {
            var normalized = (language ?? "").Trim().ToLowerInvariant();
            if (normalized == "zh" || normalized == "zh-cn" || normalized == "zh-hans") return "中文";
            if (normalized == "en" || normalized.StartsWith("en-", StringComparison.Ordinal)) return "英文";
            return string.IsNullOrWhiteSpace(language) ? "未知语言" : language.Trim();
        }

        internal static string TranslationTitle(string targetLanguage)
        {
            return DisplayName(targetLanguage) + "翻译";
        }

        private static bool IsChinese(string language)
        {
            var normalized = (language ?? "").Trim().ToLowerInvariant();
            return normalized == "zh" || normalized == "zh-cn" || normalized == "zh-hans";
        }

        private static bool IsEnglish(string language)
        {
            var normalized = (language ?? "").Trim().ToLowerInvariant();
            return normalized == "en" || normalized.StartsWith("en-", StringComparison.Ordinal);
        }
    }
}
