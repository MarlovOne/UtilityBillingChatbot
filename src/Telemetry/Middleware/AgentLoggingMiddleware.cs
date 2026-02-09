// Copyright (c) Microsoft. All rights reserved.

using System.Diagnostics;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace UtilityBillingChatbot.Telemetry.Middleware;

/// <summary>
/// Middleware for logging agent runs and recording metrics.
/// </summary>
public static class AgentLoggingMiddleware
{
    /// <summary>
    /// Creates an agent run middleware function that logs execution and records metrics.
    /// </summary>
    public static Func<IEnumerable<ChatMessage>, AgentSession?, AgentRunOptions?, AIAgent, CancellationToken, Task<AgentResponse>> Create(
        ILogger logger,
        AgentMetrics metrics,
        string agentId)
    {
        return async (messages, session, options, innerAgent, ct) =>
        {
            using var scope = logger.BeginScope(new Dictionary<string, object?>
            {
                ["AgentId"] = agentId,
                ["HasSession"] = session is not null
            });

            var messageCount = messages.Count();
            logger.LogInformation("Agent {AgentId} starting with {MessageCount} messages", agentId, messageCount);

            var sw = Stopwatch.StartNew();

            try
            {
                var response = await innerAgent.RunAsync(messages, session, options, ct);
                sw.Stop();

                logger.LogInformation(
                    "Agent {AgentId} completed in {Duration:F3}s",
                    agentId,
                    sw.Elapsed.TotalSeconds);

                metrics.AgentInteractions.Add(1,
                    new KeyValuePair<string, object?>("agent.id", agentId),
                    new KeyValuePair<string, object?>("status", "success"));

                metrics.AgentResponseTime.Record(sw.Elapsed.TotalSeconds,
                    new KeyValuePair<string, object?>("agent.id", agentId));

                return response;
            }
            catch (Exception ex)
            {
                sw.Stop();

                logger.LogError(ex, "Agent {AgentId} failed after {Duration:F3}s: {Error}",
                    agentId,
                    sw.Elapsed.TotalSeconds,
                    ex.Message);

                metrics.AgentInteractions.Add(1,
                    new KeyValuePair<string, object?>("agent.id", agentId),
                    new KeyValuePair<string, object?>("status", "error"));

                metrics.AgentErrors.Add(1,
                    new KeyValuePair<string, object?>("agent.id", agentId),
                    new KeyValuePair<string, object?>("error.type", ex.GetType().Name));

                throw;
            }
        };
    }
}
