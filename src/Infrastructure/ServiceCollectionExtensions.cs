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
            CreateChatClient(sp, (string)key!));

        // Default (unkeyed) IChatClient resolves to the configured LLM:Default provider
        services.AddSingleton<LlmProviderInfo>(sp =>
        {
            var provider = ResolveProvider(sp, GetDefaultProviderName(sp));
            return new LlmProviderInfo(provider.Name, provider.ModelDisplayName);
        });

        services.AddSingleton<IChatClient>(sp =>
            CreateChatClient(sp, GetDefaultProviderName(sp)));

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
        sp.GetRequiredService<IConfiguration>()[$"{ILlmProvider.ConfigSection}:Default"] ?? "OpenAI";

    private static ILlmProvider ResolveProvider(IServiceProvider sp, string providerName)
    {
        var providers = sp.GetRequiredService<IEnumerable<ILlmProvider>>();
        return providers.FirstOrDefault(p =>
                   string.Equals(p.Name, providerName, StringComparison.OrdinalIgnoreCase))
               ?? throw new InvalidOperationException(
                   $"Unknown LLM provider '{providerName}'. " +
                   $"Available: {string.Join(", ", providers.Select(p => p.Name))}");
    }

    private static IChatClient CreateChatClient(IServiceProvider sp, string providerName) =>
        ResolveProvider(sp, providerName).CreateClient()
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
