using System;
using System.Threading;

namespace SelectionTranslator
{
    internal sealed class InstanceCoordinator : IDisposable
    {
        private const string MutexName = "Local\\SelectionTranslator-MVP";
        private const string OpenSettingsRequestName = "Local\\SelectionTranslator-OpenSettings";
        private const string OpenSettingsAcknowledgedName = "Local\\SelectionTranslator-OpenSettingsAck";
        private const string ExitRequestName = "Local\\SelectionTranslator-ExitRequest";

        private readonly Mutex _mutex;
        private readonly EventWaitHandle _openSettingsRequest;
        private readonly EventWaitHandle _openSettingsAcknowledged;
        private readonly EventWaitHandle _exitRequest;
        private bool _ownsMutex;
        private bool _disposed;

        internal InstanceCoordinator() : this("")
        {
        }

        internal InstanceCoordinator(string nameSuffix)
        {
            bool createdNew;
            var suffix = nameSuffix ?? "";
            _mutex = new Mutex(true, MutexName + suffix, out createdNew);
            _ownsMutex = createdNew;
            _openSettingsRequest = new EventWaitHandle(false, EventResetMode.AutoReset, OpenSettingsRequestName + suffix);
            _openSettingsAcknowledged = new EventWaitHandle(false, EventResetMode.AutoReset, OpenSettingsAcknowledgedName + suffix);
            _exitRequest = new EventWaitHandle(false, EventResetMode.AutoReset, ExitRequestName + suffix);
        }

        internal bool IsPrimary { get { return _ownsMutex; } }

        internal bool RequestOpenSettings(int acknowledgementTimeoutMilliseconds)
        {
            while (_openSettingsAcknowledged.WaitOne(0)) { }
            _openSettingsRequest.Set();
            return _openSettingsAcknowledged.WaitOne(acknowledgementTimeoutMilliseconds);
        }

        internal void RequestExit() { _exitRequest.Set(); }
        internal bool ConsumeOpenSettingsRequest() { return _openSettingsRequest.WaitOne(0); }
        internal void AcknowledgeOpenSettingsRequest() { _openSettingsAcknowledged.Set(); }
        internal bool ConsumeExitRequest() { return _exitRequest.WaitOne(0); }

        internal bool TryBecomePrimary(int timeoutMilliseconds)
        {
            if (_ownsMutex) return true;
            try { _ownsMutex = _mutex.WaitOne(timeoutMilliseconds); }
            catch (AbandonedMutexException) { _ownsMutex = true; }
            return _ownsMutex;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_ownsMutex)
            {
                try { _mutex.ReleaseMutex(); }
                catch (ApplicationException) { }
                _ownsMutex = false;
            }
            _exitRequest.Dispose();
            _openSettingsAcknowledged.Dispose();
            _openSettingsRequest.Dispose();
            _mutex.Dispose();
        }
    }
}
