// Copyright (c) Microsoft. All rights reserved.

using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UtilityBillingChatbot.Models;
using UtilityBillingChatbot.MultiAgent;
using UtilityBillingChatbot.Telemetry;

namespace UtilityBillingChatbot.Hosting;

/// <summary>
/// Builder for configuring and creating the chatbot host.
/// </summary>
public class ChatbotHostBuilder
{
    private IConfiguration? _configuration;
    private LlmOptions? _llmOptions;
    private TelemetryOptions? _telemetryOptions;
    private IReadOnlyList<VerifiedQuestion>? _verifiedQuestions;

    /// <summary>
    /// Gets the configuration. Available after Build() is called.
    /// </summary>
    public IConfiguration Configuration => _configuration
        ?? throw new InvalidOperationException("Configuration not loaded. Call Build() first.");

    /// <summary>
    /// Gets the LLM options. Available after Build() is called.
    /// </summary>
    public LlmOptions LlmOptions => _llmOptions
        ?? throw new InvalidOperationException("LLM options not loaded. Call Build() first.");

    /// <summary>
    /// Gets the telemetry options. Available after Build() is called.
    /// </summary>
    public TelemetryOptions TelemetryOptions => _telemetryOptions
        ?? throw new InvalidOperationException("Telemetry options not loaded. Call Build() first.");

    /// <summary>
    /// Gets the verified questions. Available after Build() is called.
    /// </summary>
    public IReadOnlyList<VerifiedQuestion> VerifiedQuestions => _verifiedQuestions
        ?? throw new InvalidOperationException("Verified questions not loaded. Call Build() first.");

    /// <summary>
    /// Builds the chatbot host with all services configured.
    /// </summary>
    /// <param name="enableTelemetry">Whether to enable telemetry. If false, uses minimal setup.</param>
    /// <returns>The chatbot host containing the service provider and agent registry.</returns>
    public async Task<ChatbotHost> BuildAsync(bool enableTelemetry = true)
    {
        // Load configuration
        _configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddEnvironmentVariables()
            .Build();

        // Load and configure LLM options
        _llmOptions = _configuration.GetSection("LLM").Get<LlmOptions>()
            ?? throw new InvalidOperationException("LLM configuration not found in appsettings.json");

        ApplyHuggingFaceApiKeyFallback(_llmOptions);

        // Load telemetry options
        _telemetryOptions = _configuration.GetSection("Telemetry").Get<TelemetryOptions>()
            ?? new TelemetryOptions();

        if (!enableTelemetry)
        {
            _telemetryOptions.Enabled = false;
        }

        // Load verified questions
        _verifiedQuestions = await LoadVerifiedQuestionsAsync(_configuration);

        // Configure services
        var services = new ServiceCollection();

        if (_telemetryOptions.Enabled)
        {
            services.AddTelemetryServices(_telemetryOptions);
        }

        var chatClient = ChatClientFactory.Create(_llmOptions);
        services.AddSingleton<IChatClient>(chatClient);
        services.AddMultiAgentServices(_verifiedQuestions);

        // Build service provider and agent registry
        var serviceProvider = services.BuildServiceProvider();
        var agentRegistry = serviceProvider.BuildAgentRegistry(
            _telemetryOptions.Enabled ? _telemetryOptions : null);

        return new ChatbotHost(serviceProvider, agentRegistry);
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

    private static async Task<IReadOnlyList<VerifiedQuestion>> LoadVerifiedQuestionsAsync(IConfiguration configuration)
    {
        var verifiedQuestionsPath = configuration["VerifiedQuestionsPath"] ?? "Data/verified-questions.json";
        if (!Path.IsPathRooted(verifiedQuestionsPath))
        {
            verifiedQuestionsPath = Path.Combine(AppContext.BaseDirectory, verifiedQuestionsPath);
        }

        var json = await File.ReadAllTextAsync(verifiedQuestionsPath);
        return JsonSerializer.Deserialize<List<VerifiedQuestion>>(json, JsonSerializerOptions.Web)
            ?? throw new InvalidOperationException("Failed to load verified questions");
    }
}

/// <summary>
/// Represents the configured chatbot host with services and agent registry.
/// </summary>
public sealed class ChatbotHost : IDisposable
{
    /// <summary>
    /// Gets the service provider for resolving services.
    /// </summary>
    public IServiceProvider Services { get; }

    /// <summary>
    /// Gets the agent registry containing all configured agents.
    /// </summary>
    public AgentRegistry AgentRegistry { get; }

    public ChatbotHost(IServiceProvider services, AgentRegistry agentRegistry)
    {
        Services = services;
        AgentRegistry = agentRegistry;
    }

    public void Dispose()
    {
        if (Services is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
