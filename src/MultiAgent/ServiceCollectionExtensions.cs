// Copyright (c) Microsoft. All rights reserved.

using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using UtilityBillingChatbot.Models;
using UtilityBillingChatbot.MultiAgent.Agents;

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
    public static AgentRegistry BuildAgentRegistry(this IServiceProvider services)
    {
        var chatClient = services.GetRequiredService<IChatClient>();
        var definitions = services.GetServices<IAgentDefinition>();
        var registry = services.GetRequiredService<AgentRegistry>();

        foreach (var definition in definitions)
        {
            var agent = definition.Build(chatClient);
            registry.Register(definition, agent);
        }

        return registry;
    }
}
