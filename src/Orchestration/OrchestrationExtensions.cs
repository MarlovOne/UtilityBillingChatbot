// Copyright (c) Microsoft. All rights reserved.

using Microsoft.Extensions.DependencyInjection;

namespace UtilityBillingChatbot.Orchestration;

/// <summary>
/// Extension methods for registering orchestration services.
/// </summary>
public static class OrchestrationExtensions
{
    /// <summary>
    /// Adds orchestration services to the service collection.
    /// </summary>
    public static IServiceCollection AddOrchestration(this IServiceCollection services)
    {
        services.AddSingleton<ISessionStore, InMemorySessionStore>();
        services.AddSingleton<ChatbotOrchestrator>();
        return services;
    }
}
