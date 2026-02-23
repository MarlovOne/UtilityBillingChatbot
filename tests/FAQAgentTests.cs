// Copyright (c) Microsoft. All rights reserved.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using UtilityBillingChatbot.Agents.FAQ;
using UtilityBillingChatbot.Infrastructure;
using UtilityBillingChatbot.Orchestration;

namespace UtilityBillingChatbot.Tests;

/// <summary>
/// Integration tests for the FAQ Agent.
/// These tests require a configured LLM endpoint (see appsettings.json).
/// </summary>
[Collection("Sequential")]
public class FAQAgentTests : IAsyncLifetime
{
    private IHost _host = null!;
    private FAQAgent _faqAgent = null!;

    public Task InitializeAsync()
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Configuration.SetBasePath(AppContext.BaseDirectory);
        builder.Configuration.AddJsonFile("appsettings.json", optional: false);
        builder.Configuration.AddEnvironmentVariables();

        builder.Services.AddUtilityBillingChatbot(builder.Configuration);

        _host = builder.Build();
        _faqAgent = _host.Services.GetRequiredService<FAQAgent>();

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _host.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task FAQAgent_AnswersPaymentOptions()
    {
        var (text, events) = await StreamingTestHelper.CollectAsync(
            _faqAgent.StreamAsync("How can I pay my bill?"));

        var confidence = events.OfType<AnswerConfidenceEvent>().Single();
        Assert.True(confidence.FoundAnswer);
        Assert.Contains("online", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FAQAgent_AnswersAssistancePrograms()
    {
        var (text, events) = await StreamingTestHelper.CollectAsync(
            _faqAgent.StreamAsync("What assistance programs are available to help pay my bill?"));

        var confidence = events.OfType<AnswerConfidenceEvent>().Single();
        Assert.True(confidence.FoundAnswer);
        Assert.Contains("LIHEAP", text);
    }

    [Fact]
    public async Task FAQAgent_RedirectsAccountSpecificQuestions()
    {
        var (text, events) = await StreamingTestHelper.CollectAsync(
            _faqAgent.StreamAsync("What's my current balance?"));

        Assert.Contains("verify", text, StringComparison.OrdinalIgnoreCase);
    }
}
