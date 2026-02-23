// Copyright (c) Microsoft. All rights reserved.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using UtilityBillingChatbot.Agents.Classifier;
using UtilityBillingChatbot.Infrastructure;
using UtilityBillingChatbot.Orchestration;

namespace UtilityBillingChatbot.Tests;

/// <summary>
/// Integration tests for the Classifier Agent.
/// These tests require a configured LLM endpoint (see appsettings.json).
/// </summary>
[Collection("Sequential")]
public class ClassifierAgentTests : IAsyncLifetime
{
    private IHost _host = null!;
    private ClassifierAgent _classifierAgent = null!;

    public Task InitializeAsync()
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Configuration.SetBasePath(AppContext.BaseDirectory);
        builder.Configuration.AddJsonFile("appsettings.json", optional: false);
        builder.Configuration.AddEnvironmentVariables();

        builder.Services.AddUtilityBillingChatbot(builder.Configuration);

        _host = builder.Build();
        _classifierAgent = _host.Services.GetRequiredService<ClassifierAgent>();

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _host.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Classifier_CategorizesBillingFAQ()
    {
        var (text, events) = await StreamingTestHelper.CollectAsync(
            _classifierAgent.StreamAsync("How can I pay my bill?"));

        var classification = events.OfType<ClassificationEvent>().Single();
        Assert.Equal(QuestionCategory.BillingFAQ, classification.Category);
    }

    [Fact]
    public async Task Classifier_CategoriesAccountData()
    {
        var (text, events) = await StreamingTestHelper.CollectAsync(
            _classifierAgent.StreamAsync("What is my current account balance?"));

        var classification = events.OfType<ClassificationEvent>().Single();
        Assert.Equal(QuestionCategory.AccountData, classification.Category);
    }

    [Fact]
    public async Task Classifier_HandlesOutOfScope()
    {
        var (text, events) = await StreamingTestHelper.CollectAsync(
            _classifierAgent.StreamAsync("What's the weather tomorrow?"));

        var classification = events.OfType<ClassificationEvent>().Single();
        Assert.Equal(QuestionCategory.OutOfScope, classification.Category);
    }
}
