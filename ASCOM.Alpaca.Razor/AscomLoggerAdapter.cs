using ASCOM.Common;
using ASCOM.Common.Interfaces;
using MelLoggerExtensions = Microsoft.Extensions.Logging.LoggerExtensions;
using MelILogger = Microsoft.Extensions.Logging.ILogger;
using MelLogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace ASCOM.Alpaca
{
    /// <summary>
    /// Adapts a <see cref="Microsoft.Extensions.Logging.ILogger"/> to the
    /// <see cref="ASCOM.Common.Interfaces.ILogger"/> interface required by ASCOM discovery components.
    /// </summary>
    internal sealed class AscomLoggerAdapter : ASCOM.Common.Interfaces.ILogger
    {
        private readonly MelILogger _inner;

        internal AscomLoggerAdapter(MelILogger inner)
        {
            _inner = inner;
        }

        public LogLevel LoggingLevel { get; set; } = LogLevel.Information;

        public void Log(LogLevel level, string message)
        {
            switch (level)
            {
                case LogLevel.Error:
                    MelLoggerExtensions.LogError(_inner, message);
                    break;
                case LogLevel.Warning:
                    MelLoggerExtensions.LogWarning(_inner, message);
                    break;
                case LogLevel.Information:
                    MelLoggerExtensions.LogInformation(_inner, message);
                    break;
                case LogLevel.Debug:
                    MelLoggerExtensions.LogDebug(_inner, message);
                    break;
                case LogLevel.Verbose:
                    MelLoggerExtensions.LogTrace(_inner, message);
                    break;
                default:
                    MelLoggerExtensions.LogDebug(_inner, message);
                    break;
            }
        }

        public void LogError(string message) => MelLoggerExtensions.LogError(_inner, message);
        public void LogWarning(string message) => MelLoggerExtensions.LogWarning(_inner, message);
        public void LogMessage(string identifier, string message) => MelLoggerExtensions.LogInformation(_inner, "{Identifier} {Message}", identifier, message);
        public void LogInformation(string message) => MelLoggerExtensions.LogInformation(_inner, message);
        public void LogDebug(string message) => MelLoggerExtensions.LogDebug(_inner, message);
        public void LogTrace(string message) => MelLoggerExtensions.LogTrace(_inner, message);
        public void LogVerbose(string message) => MelLoggerExtensions.LogTrace(_inner, message);
        public void SetMinimumLoggingLevel(LogLevel level) => LoggingLevel = level;
    }
}
