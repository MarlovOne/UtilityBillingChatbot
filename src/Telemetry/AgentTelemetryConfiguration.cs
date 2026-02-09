// Copyright (c) Microsoft. All rights reserved.

namespace UtilityBillingChatbot.Telemetry;

/// <summary>
/// Per-agent telemetry configuration.
/// </summary>
public class AgentTelemetryConfiguration
{
    /// <summary>
    /// Custom source name for this agent's telemetry.
    /// If not set, defaults to the service name from TelemetryOptions.
    /// </summary>
    public string? SourceName { get; set; }

    /// <summary>
    /// Whether to enable sensitive data logging for this specific agent.
    /// If not set, inherits from TelemetryOptions.EnableSensitiveData.
    /// </summary>
    public bool? EnableSensitiveData { get; set; }
}
