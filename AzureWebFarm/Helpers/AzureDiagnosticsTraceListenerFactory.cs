using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Castle.Core.Logging;

namespace AzureWebFarm.Helpers
{
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
    public class AzureDiagnosticsLogger : TraceLogger
    {
        public AzureDiagnosticsLogger(string name) : base(name) { }

        public AzureDiagnosticsLogger(string name, LoggerLevel level) : base(name, level) { }

        protected override void Log(LoggerLevel loggerLevel, string loggerName, string message, Exception exception)
        {
            Trace.Write(string.Format("{0}: {1}", loggerName, message), loggerLevel.ToString());
        }
    }
}
