// Copyright (c) Microsoft. All rights reserved.

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using UtilityBillingChatbot.Models;
using UtilityBillingChatbot.MultiAgent.Agents;
using UtilityBillingChatbot.Telemetry;
using UtilityBillingChatbot.Telemetry.Middleware;

namespace UtilityBillingChatbot.MultiAgent;

/// <summary>
/// Extension methods for registering multi-agent services with dependency injection.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the AgentRegistry and all agent definitions with the service collection.
    /// </summary>
    public static IServiceCollection AddMultiAgentServices(
        this IServiceCollection services,
        IReadOnlyList<VerifiedQuestion> verifiedQuestions)
    {
        // Register agent definitions
        services.AddSingleton<IAgentDefinition>(new ClassifierAgentDefinition(verifiedQuestions));

        // Register the AgentRegistry as singleton
        services.AddSingleton<AgentRegistry>();

        return services;
    }

    /// <summary>
    /// Builds all registered agent definitions and populates the AgentRegistry.
    /// Call this after the service provider is built and IChatClient is available.
    /// </summary>
    /// <param name="services">The service provider</param>
    /// <param name="telemetryOptions">Optional telemetry options. If provided and enabled, applies logging middleware.</param>
    public static AgentRegistry BuildAgentRegistry(
        this IServiceProvider services,
        TelemetryOptions? telemetryOptions = null)
    {
        var chatClient = services.GetRequiredService<IChatClient>();
        var definitions = services.GetServices<IAgentDefinition>();
        var registry = services.GetRequiredService<AgentRegistry>();

        // Get telemetry services if telemetry is enabled
        var loggerFactory = telemetryOptions?.Enabled == true
            ? services.GetService<ILoggerFactory>()
            : null;
        var metrics = telemetryOptions?.Enabled == true
            ? services.GetService<AgentMetrics>()
            : null;

        foreach (var definition in definitions)
        {
            var baseAgent = definition.Build(chatClient);
            AIAgent instrumentedAgent = baseAgent;

            // Apply telemetry middleware if enabled
            if (telemetryOptions?.Enabled == true && loggerFactory != null && metrics != null)
            {
                var logger = loggerFactory.CreateLogger($"Agent.{definition.Id}");
                var enableSensitiveData = definition.TelemetryConfiguration?.EnableSensitiveData
                    ?? telemetryOptions.EnableSensitiveData;
                var sourceName = definition.TelemetryConfiguration?.SourceName
                    ?? telemetryOptions.ServiceName;

                instrumentedAgent = baseAgent.AsBuilder()
                    .UseOpenTelemetry(sourceName, cfg => cfg.EnableSensitiveData = enableSensitiveData)
                    .Use(AgentLoggingMiddleware.Create(logger, metrics, definition.Id), runStreamingFunc: null)
                    .Use(FunctionLoggingMiddleware.Create(logger, enableSensitiveData))
                    .Build();
            }

            registry.Register(definition, baseAgent, instrumentedAgent);
        }

        return registry;
    }
}
