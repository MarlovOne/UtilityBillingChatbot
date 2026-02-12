// Copyright (c) Microsoft. All rights reserved.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using UtilityBillingChatbot.Agents.Classifier;
using UtilityBillingChatbot.Infrastructure;
using UtilityBillingChatbot.Orchestration;

namespace UtilityBillingChatbot.Tests;

/// <summary>
/// Integration tests for the ChatbotOrchestrator.
/// These tests require a configured LLM endpoint (see appsettings.json).
/// </summary>
[Collection("Sequential")]
public class OrchestratorTests : IAsyncLifetime
{
    private IHost _host = null!;
    private ChatbotOrchestrator _orchestrator = null!;

    public Task InitializeAsync()
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Configuration.SetBasePath(AppContext.BaseDirectory);
        builder.Configuration.AddJsonFile("appsettings.json", optional: false);
        builder.Configuration.AddEnvironmentVariables();

        builder.Services.AddUtilityBillingChatbot(builder.Configuration);

        _host = builder.Build();
        _orchestrator = _host.Services.GetRequiredService<ChatbotOrchestrator>();

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _host.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Orchestrator_RoutesBillingFAQ_ToFAQAgent()
    {
        var sessionId = Guid.NewGuid().ToString();

        var response = await _orchestrator.ProcessMessageAsync(
            sessionId,
            "What are my payment options?");

        Assert.Equal(QuestionCategory.BillingFAQ, response.Category);
        Assert.Equal(RequiredAction.None, response.RequiredAction);
        Assert.True(
            response.Message.Contains("pay", StringComparison.OrdinalIgnoreCase) ||
            response.Message.Contains("bill", StringComparison.OrdinalIgnoreCase) ||
            response.Message.Contains("online", StringComparison.OrdinalIgnoreCase),
            $"Expected FAQ response about payment. Got: {response.Message}");
    }

    [Fact]
    public async Task Orchestrator_InitiatesAuth_ForAccountData()
    {
        var sessionId = Guid.NewGuid().ToString();

        var response = await _orchestrator.ProcessMessageAsync(
            sessionId,
            "What is my current balance?");

        Assert.Equal(QuestionCategory.AccountData, response.Category);
        Assert.Equal(RequiredAction.AuthenticationInProgress, response.RequiredAction);
        Assert.True(
            response.Message.Contains("verify", StringComparison.OrdinalIgnoreCase) ||
            response.Message.Contains("phone", StringComparison.OrdinalIgnoreCase) ||
            response.Message.Contains("identity", StringComparison.OrdinalIgnoreCase),
            $"Expected auth prompt. Got: {response.Message}");
    }

    [Fact]
    public async Task Orchestrator_CompletesAuthFlow_AndAnswersQuery()
    {
        var sessionId = Guid.NewGuid().ToString();

        // Step 1: Ask for account data (triggers auth)
        var r1 = await _orchestrator.ProcessMessageAsync(sessionId, "What is my balance?");
        Assert.Equal(RequiredAction.AuthenticationInProgress, r1.RequiredAction);

        // Step 2: Provide phone number
        var r2 = await _orchestrator.ProcessMessageAsync(sessionId, "555-1234");
        Assert.Equal(RequiredAction.AuthenticationInProgress, r2.RequiredAction);

        // Step 3: Provide SSN
        var r3 = await _orchestrator.ProcessMessageAsync(sessionId, "1234");

        // After successful auth, should answer the pending query
        Assert.Equal(QuestionCategory.AccountData, r3.Category);
        Assert.Equal(RequiredAction.None, r3.RequiredAction);
        Assert.True(
            r3.Message.Contains("John", StringComparison.OrdinalIgnoreCase) ||
            r3.Message.Contains("187", StringComparison.OrdinalIgnoreCase),
            $"Expected response with customer name or balance. Got: {r3.Message}");
    }

    [Fact]
    public async Task Orchestrator_HandlesOutOfScope()
    {
        var sessionId = Guid.NewGuid().ToString();

        var response = await _orchestrator.ProcessMessageAsync(
            sessionId,
            "What's the weather like today?");

        Assert.Equal(QuestionCategory.OutOfScope, response.Category);
        Assert.Equal(RequiredAction.ClarificationNeeded, response.RequiredAction);
        Assert.True(
            response.Message.Contains("utility", StringComparison.OrdinalIgnoreCase) ||
            response.Message.Contains("bill", StringComparison.OrdinalIgnoreCase) ||
            response.Message.Contains("account", StringComparison.OrdinalIgnoreCase),
            $"Expected out-of-scope response. Got: {response.Message}");
    }

    [Fact]
    public async Task Orchestrator_InitiatesHandoff_ForHumanRequested()
    {
        var sessionId = Guid.NewGuid().ToString();

        // Request to speak to a human
        var r1 = await _orchestrator.ProcessMessageAsync(
            sessionId,
            "I want to speak to a representative");

        // Fire-and-forget handoff returns None (conversation continues)
        Assert.Equal(RequiredAction.None, r1.RequiredAction);
        Assert.True(
            r1.Message.Contains("representative", StringComparison.OrdinalIgnoreCase) ||
            r1.Message.Contains("reach out", StringComparison.OrdinalIgnoreCase) ||
            r1.Message.Contains("forwarded", StringComparison.OrdinalIgnoreCase),
            $"Expected handoff acknowledgment. Got: {r1.Message}");

        // User can continue chatting after handoff
        var r2 = await _orchestrator.ProcessMessageAsync(
            sessionId,
            "What are my payment options?");

        Assert.Equal(QuestionCategory.BillingFAQ, r2.Category);
        Assert.Equal(RequiredAction.None, r2.RequiredAction);
    }

    [Fact]
    public async Task Orchestrator_EscalatesToHuman_WhenFAQCannotAnswer()
    {
        var sessionId = Guid.NewGuid().ToString();

        // Ask about late fees - billing related but not in FAQ knowledge base
        var response = await _orchestrator.ProcessMessageAsync(
            sessionId,
            "What are the late payment fees if I miss my due date?");

        // Should auto-escalate to human since FAQ can't answer (FoundAnswer=false)
        Assert.Equal(RequiredAction.None, response.RequiredAction);
        Assert.True(
            response.Message.Contains("representative", StringComparison.OrdinalIgnoreCase) ||
            response.Message.Contains("reach out", StringComparison.OrdinalIgnoreCase) ||
            response.Message.Contains("forwarded", StringComparison.OrdinalIgnoreCase) ||
            response.Message.Contains("customer service", StringComparison.OrdinalIgnoreCase),
            $"Expected handoff response when FAQ can't answer. Got: {response.Message}");
    }
}
