// Copyright (c) Microsoft. All rights reserved.

using Microsoft.Extensions.Logging;

namespace UtilityBillingChatbot.Telemetry;

/// <summary>
/// Configuration options for telemetry and logging.
/// </summary>
public class TelemetryOptions
{
    /// <summary>
    /// Whether telemetry is enabled. Default is true.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// The service name used for identifying this application in telemetry.
    /// </summary>
    public string ServiceName { get; set; } = "UtilityBillingChatbot";

    /// <summary>
    /// The OTLP endpoint for exporting telemetry data.
    /// Falls back to OTEL_EXPORTER_OTLP_ENDPOINT environment variable if not set.
    /// </summary>
    public string? OtlpEndpoint { get; set; }

    /// <summary>
    /// Whether to enable the console exporter for telemetry. Default is false.
    /// </summary>
    public bool EnableConsoleExporter { get; set; } = false;

    /// <summary>
    /// Whether to include sensitive data (like message content) in telemetry. Default is false.
    /// </summary>
    public bool EnableSensitiveData { get; set; } = false;

    /// <summary>
    /// The minimum log level for logging. Default is Information.
    /// </summary>
    public LogLevel MinimumLogLevel { get; set; } = LogLevel.Information;
}
