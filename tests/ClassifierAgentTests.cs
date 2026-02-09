// Copyright (c) Microsoft. All rights reserved.

using UtilityBillingChatbot.Extensions;
using UtilityBillingChatbot.Hosting;
using UtilityBillingChatbot.Models;

namespace UtilityBillingChatbot.Tests;

/// <summary>
/// Integration tests for the Classifier Agent.
/// These tests require a configured LLM endpoint (see appsettings.json).
/// </summary>
public class ClassifierAgentTests : IAsyncLifetime
{
    private ChatbotHost _host = null!;

    public async Task InitializeAsync()
    {
        var builder = new ChatbotHostBuilder();
        _host = await builder.BuildAsync(enableTelemetry: false);
    }

    public Task DisposeAsync()
    {
        _host.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Classifier_CategorizesBillingFAQ()
    {
        var classifier = _host.AgentRegistry.GetBaseAgent("classifier");
        var response = await classifier.RunAsync<QuestionClassification>("How can I pay my bill?");

        if (!response.TryGetResult(out var classification, out var error))
        {
            Assert.Fail(error);
            return;
        }

        Assert.Equal(QuestionCategory.BillingFAQ, classification.Category);
        Assert.False(classification.RequiresAuth);
        Assert.Equal("payment-options", classification.QuestionType);
    }

    [Fact]
    public async Task Classifier_RequiresAuth_ForAccountData()
    {
        var classifier = _host.AgentRegistry.GetBaseAgent("classifier");
        var response = await classifier.RunAsync<QuestionClassification>("What is my current account balance?");

        if (!response.TryGetResult(out var classification, out var error))
        {
            Assert.Fail(error);
            return;
        }

        Assert.Equal(QuestionCategory.AccountData, classification.Category);
        Assert.True(classification.RequiresAuth);
        Assert.Equal("balance-inquiry", classification.QuestionType);
    }

    [Fact]
    public async Task Classifier_HandlesOutOfScope()
    {
        var classifier = _host.AgentRegistry.GetBaseAgent("classifier");
        var response = await classifier.RunAsync<QuestionClassification>("What's the weather tomorrow?");

        if (!response.TryGetResult(out var classification, out var error))
        {
            Assert.Fail(error);
            return;
        }

        Assert.Equal(QuestionCategory.OutOfScope, classification.Category);
    }
}
