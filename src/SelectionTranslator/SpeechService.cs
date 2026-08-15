using System;
using System.Globalization;
using System.Linq;
using System.Speech.Synthesis;

namespace SelectionTranslator
{
    internal sealed class SpeechStatusEventArgs : EventArgs
    {
        internal bool IsSpeaking;
        internal string Message;
    }

    internal sealed class OriginalTextSpeaker : IDisposable
    {
        private SpeechSynthesizer _synthesizer;
        private bool _isSpeaking;
        private bool _disposed;

        internal event EventHandler<SpeechStatusEventArgs> StatusChanged;

        internal OriginalTextSpeaker()
        {
        }

        internal void Toggle(string text, string language)
        {
            if (_disposed) return;
            if (_isSpeaking)
            {
                Stop();
                return;
            }
            if (string.IsNullOrWhiteSpace(text))
            {
                RaiseStatus(false, "没有可朗读的原文");
                return;
            }

            try
            {
                EnsureSynthesizer();
                var selection = SelectBestVoice(text, language);
                _isSpeaking = true;
                RaiseStatus(true, "正在朗读 · " + selection);
                _synthesizer.SpeakAsync(text);
            }
            catch (Exception exception)
            {
                _isSpeaking = false;
                RaiseStatus(false, "无法朗读：" + FriendlySpeechError(exception));
            }
        }

        internal void Stop()
        {
            if (_disposed || !_isSpeaking) return;
            _isSpeaking = false;
            try { if (_synthesizer != null) _synthesizer.SpeakAsyncCancelAll(); }
            catch { }
            RaiseStatus(false, "朗读已停止");
        }

        private void EnsureSynthesizer()
        {
            if (_synthesizer != null) return;

            SpeechSynthesizer synthesizer = null;
            try
            {
                synthesizer = new SpeechSynthesizer();
                synthesizer.Rate = 0;
                synthesizer.Volume = 100;
                synthesizer.SpeakCompleted += OnSpeakCompleted;
                _synthesizer = synthesizer;
            }
            catch
            {
                if (synthesizer != null) synthesizer.Dispose();
                throw;
            }
        }

        private string SelectBestVoice(string text, string language)
        {
            var culture = ResolveCulture(text, language);
            var voices = _synthesizer.GetInstalledVoices()
                .Where(delegate(InstalledVoice voice) { return voice.Enabled; })
                .ToList();
            var exact = voices.FirstOrDefault(delegate(InstalledVoice voice)
            {
                return string.Equals(voice.VoiceInfo.Culture.Name, culture.Name, StringComparison.OrdinalIgnoreCase);
            });
            var languageMatch = exact ?? voices.FirstOrDefault(delegate(InstalledVoice voice)
            {
                return string.Equals(voice.VoiceInfo.Culture.TwoLetterISOLanguageName,
                    culture.TwoLetterISOLanguageName, StringComparison.OrdinalIgnoreCase);
            });
            if (languageMatch != null)
            {
                _synthesizer.SelectVoice(languageMatch.VoiceInfo.Name);
                return languageMatch.VoiceInfo.Name;
            }
            return _synthesizer.Voice.Name + "（系统默认）";
        }

        private static CultureInfo ResolveCulture(string text, string language)
        {
            var normalized = (language ?? "").Trim().Replace('_', '-');
            if (!string.IsNullOrWhiteSpace(normalized)
                && !string.Equals(normalized, "auto", StringComparison.OrdinalIgnoreCase))
            {
                try { return CultureInfo.GetCultureInfo(normalized); }
                catch (CultureNotFoundException) { }
            }

            foreach (var character in text ?? "")
            {
                if (character >= 0x4E00 && character <= 0x9FFF) return CultureInfo.GetCultureInfo("zh-CN");
                if (character >= 0x3040 && character <= 0x30FF) return CultureInfo.GetCultureInfo("ja-JP");
                if (character >= 0xAC00 && character <= 0xD7AF) return CultureInfo.GetCultureInfo("ko-KR");
                if (character >= 0x0400 && character <= 0x04FF) return CultureInfo.GetCultureInfo("ru-RU");
            }
            return CultureInfo.GetCultureInfo("en-US");
        }

        private void OnSpeakCompleted(object sender, SpeakCompletedEventArgs eventArgs)
        {
            if (!_isSpeaking) return;
            _isSpeaking = false;
            if (eventArgs.Error != null)
                RaiseStatus(false, "朗读失败：" + FriendlySpeechError(eventArgs.Error));
            else if (!eventArgs.Cancelled)
                RaiseStatus(false, "朗读完成");
        }

        private static string FriendlySpeechError(Exception exception)
        {
            var message = exception == null ? "未知错误" : exception.Message;
            return string.IsNullOrWhiteSpace(message) ? "系统语音不可用" : message;
        }

        private void RaiseStatus(bool isSpeaking, string message)
        {
            var handler = StatusChanged;
            if (handler != null)
                handler(this, new SpeechStatusEventArgs { IsSpeaking = isSpeaking, Message = message });
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { if (_synthesizer != null) _synthesizer.SpeakAsyncCancelAll(); }
            catch { }
            if (_synthesizer != null)
            {
                _synthesizer.SpeakCompleted -= OnSpeakCompleted;
                _synthesizer.Dispose();
                _synthesizer = null;
            }
        }
    }
}
