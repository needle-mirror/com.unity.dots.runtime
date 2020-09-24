using System;
using Unity.Entities.Conversion;
using UnityEngine;
using UnityDebug = UnityEngine.Debug;
using UnityLogType = UnityEngine.LogType;
using UnityObject = UnityEngine.Object;

namespace Unity.Entities.Runtime.Build
{
    internal class ExportConfigurationLogHandler : ILogHandler
    {
        ILogHandler m_HookedLogger;
        ConversionJournalData m_JournalData;
        bool m_FailureLogs;
        public bool ContainsFailureLogs => m_FailureLogs;
        public ref ConversionJournalData JournalData => ref m_JournalData;

        public void Hook()
        {
            if(m_HookedLogger != null)
                throw new InvalidOperationException($"{nameof(ExportConfigurationLogHandler)} has already been hooked into the logger.");
            m_JournalData = new ConversionJournalData();
            m_JournalData.Init();
            m_HookedLogger = UnityDebug.unityLogger.logHandler;
            UnityDebug.unityLogger.logHandler = this;
        }

        public void Unhook()
        {
            if (UnityDebug.unityLogger.logHandler != this)
                throw new InvalidOperationException($"{nameof(ExportConfigurationLogHandler)} is not currently hooked into the logger.");

            UnityDebug.unityLogger.logHandler = m_HookedLogger;
            m_HookedLogger = null;
            m_JournalData.Dispose();
        }

        public void LogFormat(UnityLogType logType, UnityObject context, string format, object[] args)
        {
            if(m_HookedLogger == null)
                throw new InvalidOperationException($"{nameof(ExportConfigurationLogHandler)} is not hooked into the logger. Logs can't be recorded.");

            m_HookedLogger.LogFormat(logType, context, format, args);
            m_JournalData.RecordLogEvent(context, logType, string.Format(format, args));
            if (logType == UnityLogType.Error)
                m_FailureLogs = true;
        }

        public void LogException(Exception exception, UnityObject context)
        {
            if(m_HookedLogger == null)
                throw new InvalidOperationException($"{nameof(ExportConfigurationLogHandler)} is not hooked into the logger. Exceptions can't be recorded.");

            m_HookedLogger?.LogException(exception, context);
            m_JournalData.RecordExceptionEvent(context, exception);
            m_FailureLogs = true;
        }
    }
}
