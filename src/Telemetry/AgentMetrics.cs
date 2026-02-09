// Copyright (c) Microsoft. All rights reserved.

using System.Diagnostics.Metrics;

namespace UtilityBillingChatbot.Telemetry;

/// <summary>
/// Provides metrics instrumentation for agent interactions.
/// </summary>
public sealed class AgentMetrics : IDisposable
{
    /// <summary>
    /// The name of the meter used for agent metrics.
    /// </summary>
    public const string MeterName = "UtilityBillingChatbot.Agents";

    private readonly Meter _meter;

    /// <summary>
    /// Counter for total agent interactions, tagged by agent ID and status.
    /// </summary>
    public Counter<int> AgentInteractions { get; }

    /// <summary>
    /// Histogram for agent response time in seconds, tagged by agent ID.
    /// </summary>
    public Histogram<double> AgentResponseTime { get; }

    /// <summary>
    /// Counter for classification results, tagged by category.
    /// </summary>
    public Counter<int> ClassificationResults { get; }

    /// <summary>
    /// Counter for agent errors, tagged by agent ID and error type.
    /// </summary>
    public Counter<int> AgentErrors { get; }

    public AgentMetrics()
    {
        _meter = new Meter(MeterName);

        AgentInteractions = _meter.CreateCounter<int>(
            "agent.interactions.total",
            unit: "{interactions}",
            description: "Total number of agent interactions");

        AgentResponseTime = _meter.CreateHistogram<double>(
            "agent.response_time.seconds",
            unit: "s",
            description: "Agent response time in seconds");

        ClassificationResults = _meter.CreateCounter<int>(
            "agent.classification.total",
            unit: "{classifications}",
            description: "Total number of classification results by category");

        AgentErrors = _meter.CreateCounter<int>(
            "agent.errors.total",
            unit: "{errors}",
            description: "Total number of agent errors");
    }

    public void Dispose()
    {
        _meter.Dispose();
    }
}
