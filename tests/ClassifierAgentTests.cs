// Copyright (c) Microsoft. All rights reserved.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using UtilityBillingChatbot.Agents.Classifier;
using UtilityBillingChatbot.Infrastructure;

namespace UtilityBillingChatbot.Tests;

/// <summary>
/// Integration tests for the Classifier Agent.
/// These tests require a configured LLM endpoint (see appsettings.json).
/// </summary>
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
        var result = await _classifierAgent.ClassifyAsync("How can I pay my bill?");

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(QuestionCategory.BillingFAQ, result.Classification!.Category);
        Assert.False(result.Classification.RequiresAuth);
        Assert.Equal("payment-options", result.Classification.QuestionType);
    }

    [Fact]
    public async Task Classifier_RequiresAuth_ForAccountData()
    {
        var result = await _classifierAgent.ClassifyAsync("What is my current account balance?");

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(QuestionCategory.AccountData, result.Classification!.Category);
        Assert.True(result.Classification.RequiresAuth);
        Assert.Equal("balance-inquiry", result.Classification.QuestionType);
    }

    [Fact]
    public async Task Classifier_HandlesOutOfScope()
    {
        var result = await _classifierAgent.ClassifyAsync("What's the weather tomorrow?");

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(QuestionCategory.OutOfScope, result.Classification!.Category);
    }
}
