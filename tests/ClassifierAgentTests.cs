// Copyright (c) Microsoft. All rights reserved.

using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UtilityBillingChatbot.Models;
using UtilityBillingChatbot.MultiAgent;
using UtilityBillingChatbot.MultiAgent.Agents;

namespace UtilityBillingChatbot.Tests;

/// <summary>
/// Integration tests for the Classifier Agent.
/// These tests require a configured LLM endpoint (see appsettings.json).
/// </summary>
public class ClassifierAgentTests : IAsyncLifetime
{
    private AgentRegistry _registry = null!;

    public async Task InitializeAsync()
    {
        // Load configuration
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .AddEnvironmentVariables()
            .Build();

        var llmOptions = configuration.GetSection("LLM").Get<LlmOptions>()
            ?? throw new InvalidOperationException("LLM configuration not found");

        // Fallback: check common HuggingFace environment variable names if ApiKey not set
        if (llmOptions.HuggingFace is not null && string.IsNullOrEmpty(llmOptions.HuggingFace.ApiKey))
        {
            llmOptions.HuggingFace.ApiKey =
                Environment.GetEnvironmentVariable("HF_TOKEN") ??
                Environment.GetEnvironmentVariable("HUGGINGFACE_API_KEY") ??
                Environment.GetEnvironmentVariable("HUGGINGFACE_TOKEN");
        }

        // Load verified questions
        var verifiedQuestionsPath = configuration["VerifiedQuestionsPath"] ?? "Data/verified-questions.json";
        if (!Path.IsPathRooted(verifiedQuestionsPath))
        {
            verifiedQuestionsPath = Path.Combine(AppContext.BaseDirectory, verifiedQuestionsPath);
        }

        var json = await File.ReadAllTextAsync(verifiedQuestionsPath);
        var verifiedQuestions = JsonSerializer.Deserialize<List<VerifiedQuestion>>(json, JsonSerializerOptions.Web)
            ?? throw new InvalidOperationException("Failed to load verified questions");

        // Create HuggingFace chat client
        var hfOptions = llmOptions.HuggingFace
            ?? throw new InvalidOperationException("HuggingFace configuration not found");

        var endpointStr = hfOptions.Endpoint;
        if (!endpointStr.EndsWith("/v1/", StringComparison.Ordinal) && !endpointStr.EndsWith("/v1", StringComparison.Ordinal))
        {
            endpointStr = endpointStr.TrimEnd('/') + "/v1/";
        }

        var clientOptions = new OpenAI.OpenAIClientOptions { Endpoint = new Uri(endpointStr) };
        var client = new OpenAI.OpenAIClient(
            new System.ClientModel.ApiKeyCredential(hfOptions.ApiKey!),
            clientOptions);
        var chatClient = client.GetChatClient(hfOptions.Model).AsIChatClient();

        // Configure services using DI
        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(chatClient);
        services.AddMultiAgentServices(verifiedQuestions);

        var serviceProvider = services.BuildServiceProvider();
        _registry = serviceProvider.BuildAgentRegistry();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Classifier_CategorizesBillingFAQ()
    {
        var classifier = _registry.GetAgent("classifier");
        var response = await classifier.RunAsync<QuestionClassification>("How can I pay my bill?");

        Assert.Equal(QuestionCategory.BillingFAQ, response.Result.Category);
        Assert.False(response.Result.RequiresAuth);
        Assert.Equal("payment-options", response.Result.QuestionType);
    }

    [Fact]
    public async Task Classifier_RequiresAuth_ForAccountData()
    {
        var classifier = _registry.GetAgent("classifier");
        var response = await classifier.RunAsync<QuestionClassification>("What is my current account balance?");

        Assert.Equal(QuestionCategory.AccountData, response.Result.Category);
        Assert.True(response.Result.RequiresAuth);
        Assert.Equal("balance-inquiry", response.Result.QuestionType);
    }

    [Fact]
    public async Task Classifier_HandlesOutOfScope()
    {
        var classifier = _registry.GetAgent("classifier");
        var response = await classifier.RunAsync<QuestionClassification>("What's the weather tomorrow?");

        Assert.Equal(QuestionCategory.OutOfScope, response.Result.Category);
    }
}
