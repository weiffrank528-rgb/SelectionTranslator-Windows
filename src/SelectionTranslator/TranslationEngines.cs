using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Web.Script.Serialization;

namespace SelectionTranslator
{
    internal interface ITranslationEngine
    {
        string DisplayName { get; }
        Task<string> TranslateAsync(string text, AppSettings settings, CancellationToken token);
    }

    internal static class TranslationEngineFactory
    {
        internal static ITranslationEngine Create(AppSettings settings)
        {
            if (string.Equals(settings.Engine, "Google", StringComparison.OrdinalIgnoreCase))
                return new GoogleTranslationEngine();
            if (string.Equals(settings.Engine, "OpenAI", StringComparison.OrdinalIgnoreCase))
                return new OpenAITranslationEngine();
            if (string.Equals(settings.Engine, "DeepL", StringComparison.OrdinalIgnoreCase))
                return new DeepLTranslationEngine();
            return new MyMemoryTranslationEngine();
        }
    }

    internal sealed class GoogleTranslationEngine : HttpTranslationEngine, ITranslationEngine
    {
        public string DisplayName { get { return "Google Cloud Translation"; } }

        public async Task<string> TranslateAsync(string text, AppSettings settings, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(settings.GoogleApiKey))
                throw new InvalidOperationException("请先在设置中填写 Google Cloud Translation API Key。" );
            if (string.IsNullOrWhiteSpace(settings.GoogleEndpoint))
                throw new InvalidOperationException("请先在设置中填写 Google API 地址。" );

            var body = new Dictionary<string, object>();
            body["q"] = text;
            body["target"] = MapGoogleLanguage(settings.TargetLanguage);
            body["format"] = "text";
            if (!string.IsNullOrWhiteSpace(settings.SourceLanguage)
                && !string.Equals(settings.SourceLanguage, "auto", StringComparison.OrdinalIgnoreCase))
                body["source"] = MapGoogleLanguage(settings.SourceLanguage);

            var separator = settings.GoogleEndpoint.IndexOf('?') >= 0 ? "&" : "?";
            var requestUrl = settings.GoogleEndpoint + separator + "key=" + Uri.EscapeDataString(settings.GoogleApiKey.Trim());
            using (var request = new HttpRequestMessage(HttpMethod.Post, requestUrl))
            {
                request.Content = new StringContent(Json.Serialize(body), Encoding.UTF8, "application/json");
                using (var response = await Client.SendAsync(request, token).ConfigureAwait(false))
                {
                    var json = await ReadResponseAsync(response).ConfigureAwait(false);
                    var root = Json.DeserializeObject(json) as IDictionary<string, object>;
                    object dataRaw;
                    object translationsRaw;
                    if (root == null || !root.TryGetValue("data", out dataRaw))
                        throw new InvalidOperationException("Google 返回了无法识别的数据。" );
                    var data = dataRaw as IDictionary<string, object>;
                    if (data == null || !data.TryGetValue("translations", out translationsRaw))
                        throw new InvalidOperationException("Google 没有返回译文。" );
                    var translations = translationsRaw as object[];
                    if (translations == null || translations.Length == 0)
                        throw new InvalidOperationException("Google 没有返回译文。" );
                    var first = translations[0] as IDictionary<string, object>;
                    object translatedTextRaw;
                    if (first == null || !first.TryGetValue("translatedText", out translatedTextRaw))
                        throw new InvalidOperationException("Google 没有返回译文。" );
                    return HttpUtility.HtmlDecode(Convert.ToString(translatedTextRaw)).Trim();
                }
            }
        }

        private static string MapGoogleLanguage(string language)
        {
            if (string.Equals(language, "zh", StringComparison.OrdinalIgnoreCase)) return "zh-CN";
            return language.Trim();
        }
    }

    internal abstract class HttpTranslationEngine
    {
        protected static readonly HttpClient Client = CreateClient();
        protected static readonly JavaScriptSerializer Json = new JavaScriptSerializer();

        private static HttpClient CreateClient()
        {
            var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(18);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("SelectionTranslator/0.1");
            return client;
        }

        protected static async Task<string> ReadResponseAsync(HttpResponseMessage response)
        {
            var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var detail = content.Length > 300 ? content.Substring(0, 300) + "…" : content;
                throw new InvalidOperationException("翻译服务返回 " + (int)response.StatusCode + "：" + detail);
            }
            return content;
        }
    }

    internal sealed class MyMemoryTranslationEngine : HttpTranslationEngine, ITranslationEngine
    {
        public string DisplayName { get { return "MyMemory（免 Key）"; } }

        public async Task<string> TranslateAsync(string text, AppSettings settings, CancellationToken token)
        {
            var chunks = SplitByUtf8Bytes(text, 430);
            var translated = new List<string>();
            foreach (var chunk in chunks)
            {
                token.ThrowIfCancellationRequested();
                var url = "https://api.mymemory.translated.net/get?q=" + Uri.EscapeDataString(chunk)
                    + "&langpair=" + Uri.EscapeDataString(NormalizeSource(settings.SourceLanguage) + "|" + settings.TargetLanguage)
                    + "&mt=1";
                if (!string.IsNullOrWhiteSpace(settings.MyMemoryEmail))
                    url += "&de=" + Uri.EscapeDataString(settings.MyMemoryEmail.Trim());

                using (var response = await Client.GetAsync(url, token).ConfigureAwait(false))
                {
                    var json = await ReadResponseAsync(response).ConfigureAwait(false);
                    var root = Json.DeserializeObject(json) as IDictionary<string, object>;
                    object responseDataRaw;
                    object translatedTextRaw;
                    if (root == null || !root.TryGetValue("responseData", out responseDataRaw))
                        throw new InvalidOperationException("MyMemory 返回了无法识别的数据。" );
                    var responseData = responseDataRaw as IDictionary<string, object>;
                    if (responseData == null || !responseData.TryGetValue("translatedText", out translatedTextRaw))
                        throw new InvalidOperationException("MyMemory 没有返回译文。" );
                    translated.Add(HttpUtility.HtmlDecode(Convert.ToString(translatedTextRaw)));
                }
            }
            return string.Join(" ", translated.ToArray()).Trim();
        }

        private static string NormalizeSource(string language)
        {
            if (string.IsNullOrWhiteSpace(language) || string.Equals(language, "auto", StringComparison.OrdinalIgnoreCase))
                return "en";
            return language.Trim();
        }

        private static IList<string> SplitByUtf8Bytes(string text, int maxBytes)
        {
            var result = new List<string>();
            var current = new StringBuilder();
            foreach (var character in text)
            {
                var candidate = current.ToString() + character;
                if (current.Length > 0 && Encoding.UTF8.GetByteCount(candidate) > maxBytes)
                {
                    result.Add(current.ToString().Trim());
                    current.Clear();
                }
                current.Append(character);
            }
            if (current.Length > 0) result.Add(current.ToString().Trim());
            return result.Where(delegate(string part) { return part.Length > 0; }).ToList();
        }
    }

    internal sealed class OpenAITranslationEngine : HttpTranslationEngine, ITranslationEngine
    {
        public string DisplayName { get { return "OpenAI"; } }

        public async Task<string> TranslateAsync(string text, AppSettings settings, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(settings.OpenAIApiKey))
                throw new InvalidOperationException("请先在设置中填写 OpenAI API Key。" );

            var body = new Dictionary<string, object>();
            body["model"] = settings.OpenAIModel;
            body["instructions"] = "You are a precise translation engine. Translate the user's text into Simplified Chinese. Return only the translation, preserving useful formatting.";
            body["input"] = text;
            body["max_output_tokens"] = 1800;

            using (var request = new HttpRequestMessage(HttpMethod.Post, settings.OpenAIEndpoint))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.OpenAIApiKey.Trim());
                request.Content = new StringContent(Json.Serialize(body), Encoding.UTF8, "application/json");
                using (var response = await Client.SendAsync(request, token).ConfigureAwait(false))
                {
                    var json = await ReadResponseAsync(response).ConfigureAwait(false);
                    var root = Json.DeserializeObject(json) as IDictionary<string, object>;
                    var result = ExtractOpenAIText(root);
                    if (string.IsNullOrWhiteSpace(result))
                        throw new InvalidOperationException("OpenAI 返回成功，但未找到文本译文。" );
                    return result.Trim();
                }
            }
        }

        private static string ExtractOpenAIText(IDictionary<string, object> root)
        {
            if (root == null) return "";
            object direct;
            if (root.TryGetValue("output_text", out direct) && direct != null) return Convert.ToString(direct);

            object outputRaw;
            if (!root.TryGetValue("output", out outputRaw)) return "";
            var output = outputRaw as object[];
            if (output == null) return "";
            var pieces = new List<string>();
            foreach (var itemRaw in output)
            {
                var item = itemRaw as IDictionary<string, object>;
                object contentRaw;
                if (item == null || !item.TryGetValue("content", out contentRaw)) continue;
                var content = contentRaw as object[];
                if (content == null) continue;
                foreach (var partRaw in content)
                {
                    var part = partRaw as IDictionary<string, object>;
                    object textRaw;
                    if (part != null && part.TryGetValue("text", out textRaw) && textRaw != null)
                        pieces.Add(Convert.ToString(textRaw));
                }
            }
            return string.Join("", pieces.ToArray());
        }
    }

    internal sealed class DeepLTranslationEngine : HttpTranslationEngine, ITranslationEngine
    {
        public string DisplayName { get { return "DeepL"; } }

        public async Task<string> TranslateAsync(string text, AppSettings settings, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(settings.DeepLApiKey))
                throw new InvalidOperationException("请先在设置中填写 DeepL API Key。" );

            var values = new List<KeyValuePair<string, string>>();
            values.Add(new KeyValuePair<string, string>("text", text));
            values.Add(new KeyValuePair<string, string>("target_lang", MapDeepLLanguage(settings.TargetLanguage)));
            if (!string.IsNullOrWhiteSpace(settings.SourceLanguage)
                && !string.Equals(settings.SourceLanguage, "auto", StringComparison.OrdinalIgnoreCase))
                values.Add(new KeyValuePair<string, string>("source_lang", settings.SourceLanguage.ToUpperInvariant()));

            using (var request = new HttpRequestMessage(HttpMethod.Post, settings.DeepLEndpoint))
            {
                request.Headers.TryAddWithoutValidation("Authorization", "DeepL-Auth-Key " + settings.DeepLApiKey.Trim());
                request.Content = new FormUrlEncodedContent(values);
                using (var response = await Client.SendAsync(request, token).ConfigureAwait(false))
                {
                    var json = await ReadResponseAsync(response).ConfigureAwait(false);
                    var root = Json.DeserializeObject(json) as IDictionary<string, object>;
                    object translationsRaw;
                    if (root == null || !root.TryGetValue("translations", out translationsRaw))
                        throw new InvalidOperationException("DeepL 返回了无法识别的数据。" );
                    var translations = translationsRaw as object[];
                    if (translations == null || translations.Length == 0)
                        throw new InvalidOperationException("DeepL 没有返回译文。" );
                    var first = translations[0] as IDictionary<string, object>;
                    object textRaw;
                    if (first == null || !first.TryGetValue("text", out textRaw))
                        throw new InvalidOperationException("DeepL 没有返回译文。" );
                    return Convert.ToString(textRaw).Trim();
                }
            }
        }

        private static string MapDeepLLanguage(string language)
        {
            if (string.Equals(language, "zh-CN", StringComparison.OrdinalIgnoreCase)
                || string.Equals(language, "zh", StringComparison.OrdinalIgnoreCase)) return "ZH-HANS";
            return language.ToUpperInvariant();
        }
    }
}
