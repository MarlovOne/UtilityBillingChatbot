// Copyright (c) Microsoft. All rights reserved.

using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UtilityBillingChatbot.Agents.Classifier;
using UtilityBillingChatbot.Agents.FAQ;
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
        // Configure options
        services.Configure<LlmOptions>(configuration.GetSection("LLM"));
        services.Configure<TelemetryOptions>(configuration.GetSection("Telemetry"));

        // Load verified questions
        services.AddVerifiedQuestions(configuration);

        // Add chat client
        services.AddSingleton<IChatClient>(sp =>
        {
            var llmOptions = configuration.GetSection("LLM").Get<LlmOptions>()
                ?? throw new InvalidOperationException("LLM configuration not found");

            ApplyHuggingFaceApiKeyFallback(llmOptions);
            return ChatClientFactory.Create(llmOptions);
        });

        // Add telemetry services (uses IOptions<TelemetryOptions> internally)
        services.AddTelemetryServices(configuration);

        // Add agents
        services.AddClassifierAgent();
        services.AddFAQAgent();

        // Add the chatbot background service
        services.AddHostedService<ChatbotService>();

        return services;
    }

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

    private static void ApplyHuggingFaceApiKeyFallback(LlmOptions llmOptions)
    {
        if (llmOptions.HuggingFace is not null && string.IsNullOrEmpty(llmOptions.HuggingFace.ApiKey))
        {
            llmOptions.HuggingFace.ApiKey =
                Environment.GetEnvironmentVariable("HF_TOKEN") ??
                Environment.GetEnvironmentVariable("HUGGINGFACE_API_KEY") ??
                Environment.GetEnvironmentVariable("HUGGINGFACE_TOKEN");
        }
    }
}
