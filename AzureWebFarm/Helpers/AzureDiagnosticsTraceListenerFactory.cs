using System;
using System.Diagnostics;
using Castle.Core.Logging;

namespace AzureWebFarm.Helpers
{
    /// <summary>
    /// Castle.Core Logger Factory that provides a logger that uses Trace.Write in a way compatible with the Azure Diagnostics Listener.
    /// </summary>
    public class AzureDiagnosticsTraceListenerFactory : AbstractLoggerFactory
    {
        public override ILogger Create(string name)
        {
            return new AzureDiagnosticsLogger(name);
        }

        public override ILogger Create(string name, LoggerLevel level)
        {
            return new AzureDiagnosticsLogger(name, level);
        }
    }

    /// <summary>
    /// Redirects all Trace messages to the "Default" TraceSource so that the standard Azure Diagnostics Listener picks up the messages.
    /// </summary>
    public class AzureDiagnosticsLogger : LevelFilteredLogger
    {
        private readonly TraceSource _traceSource;
        public AzureDiagnosticsLogger(string name) : base(name)
        {
            _traceSource = new TraceSource("Default", MapSourceLevels(Level));
        }

        public AzureDiagnosticsLogger(string name, LoggerLevel level) : base(name, level)
        {
            _traceSource = new TraceSource("Default", MapSourceLevels(Level));
        }

        public override ILogger CreateChildLogger(string loggerName)
        {
            return new AzureDiagnosticsLogger(loggerName, Level);
        }

        protected override void Log(LoggerLevel loggerLevel, string loggerName, string message, Exception exception)
        {
            if (exception == null)
                _traceSource.TraceEvent(MapTraceEventType(loggerLevel), 0, "[{0}] {1}", loggerName, message);
            else
                _traceSource.TraceData(MapTraceEventType(loggerLevel), 0, (object) string.Format("[{0}] {1}", loggerName, message), exception);
        }

        private static SourceLevels MapSourceLevels(LoggerLevel level)
        {
            switch (level)
            {
                case LoggerLevel.Debug:
                    return SourceLevels.Verbose;
                case LoggerLevel.Info:
                    return SourceLevels.Information;
                case LoggerLevel.Warn:
                    return SourceLevels.Warning;
                case LoggerLevel.Error:
                    return SourceLevels.Error;
                case LoggerLevel.Fatal:
                    return SourceLevels.Critical;
            }
            return SourceLevels.Off;
        }

        private static TraceEventType MapTraceEventType(LoggerLevel level)
        {
            switch (level)
            {
                case LoggerLevel.Fatal:
                    return TraceEventType.Critical;
                case LoggerLevel.Error:
                    return TraceEventType.Error;
                case LoggerLevel.Warn:
                    return TraceEventType.Warning;
                case LoggerLevel.Info:
                    return TraceEventType.Information;
                case LoggerLevel.Debug:
                    return TraceEventType.Verbose;
                default:
                    return TraceEventType.Verbose;
            }
        }
    }
}
