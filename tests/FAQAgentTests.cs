// Copyright (c) Microsoft. All rights reserved.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using UtilityBillingChatbot.Agents.FAQ;
using UtilityBillingChatbot.Infrastructure;

namespace UtilityBillingChatbot.Tests;

/// <summary>
/// Integration tests for the FAQ Agent.
/// These tests require a configured LLM endpoint (see appsettings.json).
/// </summary>
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
        // Arrange - Q5: "How can I pay my bill?"
        // Act
        var response = await _faqAgent.AnswerAsync("How can I pay my bill?");

        // Assert - should mention multiple payment methods
        Assert.Contains("online", response.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FAQAgent_AnswersAssistancePrograms()
    {
        // Arrange - Q7: "What assistance programs can help me?"
        // Act
        var response = await _faqAgent.AnswerAsync("What assistance programs are available to help pay my bill?");

        // Assert - should mention LIHEAP
        Assert.Contains("LIHEAP", response.Text);
    }

    [Fact]
    public async Task FAQAgent_RedirectsAccountSpecificQuestions()
    {
        // Arrange - Account-specific question requiring auth
        // Act
        var response = await _faqAgent.AnswerAsync("What's my current balance?");

        // Assert - should redirect to account verification
        Assert.Contains("verify", response.Text, StringComparison.OrdinalIgnoreCase);
    }
}
