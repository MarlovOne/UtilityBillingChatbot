// Copyright (c) Microsoft. All rights reserved.

using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UtilityBillingChatbot.Agents.Auth;
using UtilityBillingChatbot.Agents.Classifier;
using UtilityBillingChatbot.Agents.FAQ;
using UtilityBillingChatbot.Agents.NextBestAction;
using UtilityBillingChatbot.Agents.Summarization;
using UtilityBillingChatbot.Agents.UtilityData;
using UtilityBillingChatbot.Infrastructure.Providers;
using UtilityBillingChatbot.Orchestration;
using UtilityBillingChatbot.Telemetry;

namespace UtilityBillingChatbot.Infrastructure;

/// <summary>
/// Extension methods for configuring the utility billing chatbot services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds all services required for the utility billing chatbot.
    /// </summary>
    public static IServiceCollection AddUtilityBillingChatbot(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Add OpenTelemetry observability
        services.AddOpenTelemetryObservability(configuration);

        // Load verified questions
        services.AddVerifiedQuestions(configuration);

        // Register LLM providers
        services.AddSingleton<ILlmProvider, OpenAIProvider>();
        services.AddSingleton<ILlmProvider, AzureOpenAIProvider>();
        services.AddSingleton<ILlmProvider, HuggingFaceProvider>();

        // Keyed IChatClient: any provider name works (e.g. [FromKeyedServices("AzureOpenAI")])
        services.AddKeyedSingleton<IChatClient>(KeyedService.AnyKey, (sp, key) =>
            CreateChatClient(sp, (string)key!, GetDefaultModel(sp)));

        // Default (unkeyed) IChatClient resolves to the configured default provider + model
        services.AddSingleton<IChatClient>(sp =>
            CreateChatClient(sp, GetDefaultProviderName(sp), GetDefaultModel(sp)));

        // Add agents
        services.AddClassifierAgent();
        services.AddFAQAgent();
        services.AddAuthAgent();
        services.AddUtilityDataAgent();
        services.AddSummarizationAgent();
        services.AddNextBestActionAgent();

        // Add orchestration
        services.AddSingleton<IApprovalHandler, ConsoleApprovalHandler>();
        services.AddOrchestration();

        // Add the chatbot background service
        services.AddHostedService<ChatbotService>();

        return services;
    }

    private static string GetDefaultProviderName(IServiceProvider sp) =>
        sp.GetRequiredService<IConfiguration>()[$"{ILlmProvider.ConfigSection}:DefaultProvider"] ?? "OpenAI";

    internal static string GetDefaultModel(IServiceProvider sp) =>
        sp.GetRequiredService<IConfiguration>()[$"{ILlmProvider.ConfigSection}:DefaultModel"] ?? "gpt-4o-mini";

    /// <summary>
    /// Resolves an IChatClient for a specific agent, using per-agent provider/model overrides
    /// from LLM:AgentProviders:{agentName}, falling back to the global defaults.
    /// </summary>
    internal static IChatClient GetAgentChatClient(IServiceProvider sp, string agentName)
    {
        var config = sp.GetRequiredService<IConfiguration>();
        var agentSection = config.GetSection($"{ILlmProvider.ConfigSection}:AgentProviders:{agentName}");

        var providerName = agentSection["Provider"] ?? GetDefaultProviderName(sp);
        var model = agentSection["Model"] ?? GetDefaultModel(sp);

        return CreateChatClient(sp, providerName, model);
    }

    private static ILlmProvider ResolveProvider(IServiceProvider sp, string providerName)
    {
        var providers = sp.GetRequiredService<IEnumerable<ILlmProvider>>();
        return providers.FirstOrDefault(p =>
                   string.Equals(p.Name, providerName, StringComparison.OrdinalIgnoreCase))
               ?? throw new InvalidOperationException(
                   $"Unknown LLM provider '{providerName}'. " +
                   $"Available: {string.Join(", ", providers.Select(p => p.Name))}");
    }

    private static IChatClient CreateChatClient(IServiceProvider sp, string providerName, string model) =>
        ResolveProvider(sp, providerName).CreateClient(model)
            .AsBuilder()
            .UseOpenTelemetry(
                sourceName: TelemetryServiceCollectionExtensions.ServiceName,
                configure: cfg => cfg.EnableSensitiveData = false)
            .Build();

    /// <summary>
    /// Loads verified questions from JSON and registers them as a singleton.
    /// </summary>
    private static IServiceCollection AddVerifiedQuestions(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton<IReadOnlyList<VerifiedQuestion>>(sp =>
        {
            var verifiedQuestionsPath = configuration["VerifiedQuestionsPath"] ?? "Data/verified-questions.json";
            if (!Path.IsPathRooted(verifiedQuestionsPath))
            {
                verifiedQuestionsPath = Path.Combine(AppContext.BaseDirectory, verifiedQuestionsPath);
            }

            var json = File.ReadAllText(verifiedQuestionsPath);
            return JsonSerializer.Deserialize<List<VerifiedQuestion>>(json, JsonSerializerOptions.Web)
                ?? throw new InvalidOperationException("Failed to load verified questions");
        });

        return services;
    }
}
