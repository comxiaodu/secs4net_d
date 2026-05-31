using Serilog;
using Serilog.Core;
using Serilog.Events;
using System;
using System.IO;

namespace SecsUtil;

public static class LoggingConfig
{
    public static LogEventLevel MinimumLevel { get; set; } = LogEventLevel.Debug;

    public static void Configure()
    {
        var logsDir = Path.Combine(AppContext.BaseDirectory, "logs");
        Directory.CreateDirectory(logsDir);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Is(MinimumLevel)
            .Enrich.FromLogContext()
            .WriteTo.File(
                path: Path.Combine(logsDir, "secsutil_.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30,
                outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{Level:u3}] [{SourceContext}] {Message}{NewLine}{Exception}"
            )
            .CreateLogger();
    }
}