using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace SelectionTranslator
{
    internal sealed class AppSettings
    {
        public bool Enabled = true;
        public string Engine = "MyMemory";
        public string SourceLanguage = "en";
        public string TargetLanguage = "zh-CN";
        public bool AutoDetectSourceLanguage = true;
        public int MinCharacters = 2;
        public int MaxCharacters = 1500;
        public int MinDragMilliseconds = 60;
        public int DragThresholdPixels = 5;
        public int SelectionDelayMilliseconds = 90;
        public int UiaTimeoutMilliseconds = 500;
        public int AutoHideMilliseconds = 6500;
        public bool HideOnOutsideClick = true;
        public bool EnableClipboardFallback = true;
        public bool EnableWpsCompatibility = true;
        public string Whitelist = "";
        public string Blacklist = "SelectionTranslator";
        public string MyMemoryEmail = "";
        public string GoogleApiKey = "";
        public string GoogleEndpoint = "https://translation.googleapis.com/language/translate/v2";
        public string OpenAIApiKey = "";
        public string OpenAIEndpoint = "https://api.openai.com/v1/responses";
        public string OpenAIModel = "gpt-5.6-luna";
        public string DeepLApiKey = "";
        public string DeepLEndpoint = "https://api-free.deepl.com/v2/translate";

        public AppSettings Clone()
        {
            var serializer = new JavaScriptSerializer();
            return serializer.Deserialize<AppSettings>(serializer.Serialize(this));
        }
    }

    internal static class SettingsStore
    {
        private static readonly string DirectoryPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SelectionTranslator");
        internal static readonly string FilePath = Path.Combine(DirectoryPath, "settings.json");

        internal static AppSettings Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return new AppSettings();
                var json = File.ReadAllText(FilePath);
                var loaded = new JavaScriptSerializer().Deserialize<AppSettings>(json) ?? new AppSettings();
                loaded.GoogleApiKey = Unprotect(loaded.GoogleApiKey);
                loaded.OpenAIApiKey = Unprotect(loaded.OpenAIApiKey);
                loaded.DeepLApiKey = Unprotect(loaded.DeepLApiKey);
                return loaded;
            }
            catch
            {
                return new AppSettings();
            }
        }

        internal static void Save(AppSettings settings)
        {
            Directory.CreateDirectory(DirectoryPath);
            var serializer = new JavaScriptSerializer();
            var persisted = settings.Clone();
            persisted.GoogleApiKey = Protect(persisted.GoogleApiKey);
            persisted.OpenAIApiKey = Protect(persisted.OpenAIApiKey);
            persisted.DeepLApiKey = Protect(persisted.DeepLApiKey);
            File.WriteAllText(FilePath, serializer.Serialize(persisted));
        }

        private static string Protect(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            try
            {
                var bytes = Encoding.UTF8.GetBytes(value);
                var encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
                return "dpapi:" + Convert.ToBase64String(encrypted);
            }
            catch { return value; }
        }

        private static string Unprotect(string value)
        {
            if (string.IsNullOrEmpty(value) || !value.StartsWith("dpapi:", StringComparison.Ordinal)) return value ?? "";
            try
            {
                var encrypted = Convert.FromBase64String(value.Substring(6));
                return Encoding.UTF8.GetString(ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser));
            }
            catch { return ""; }
        }
    }
}
