// Copyright (c) Microsoft. All rights reserved.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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

        var response = await StreamingTestHelper.CollectAsync(
            _orchestrator.ProcessMessageStreamingAsync(sessionId, "What are my payment options?"));

        Assert.True(
            response.Contains("pay", StringComparison.OrdinalIgnoreCase) ||
            response.Contains("bill", StringComparison.OrdinalIgnoreCase) ||
            response.Contains("online", StringComparison.OrdinalIgnoreCase),
            $"Expected FAQ response about payment. Got: {response}");
    }

    [Fact]
    public async Task Orchestrator_InitiatesAuth_ForAccountData()
    {
        var sessionId = Guid.NewGuid().ToString();

        var response = await StreamingTestHelper.CollectAsync(
            _orchestrator.ProcessMessageStreamingAsync(sessionId, "What is my current balance?"));

        Assert.True(
            response.Contains("verify", StringComparison.OrdinalIgnoreCase) ||
            response.Contains("phone", StringComparison.OrdinalIgnoreCase) ||
            response.Contains("identity", StringComparison.OrdinalIgnoreCase),
            $"Expected auth prompt. Got: {response}");
    }

    [Fact]
    public async Task Orchestrator_CompletesAuthFlow_AndAnswersQuery()
    {
        var sessionId = Guid.NewGuid().ToString();

        // Step 1: Ask for account data (triggers auth)
        var r1 = await StreamingTestHelper.CollectAsync(
            _orchestrator.ProcessMessageStreamingAsync(sessionId, "What is my balance?"));
        Assert.Contains("verify", r1, StringComparison.OrdinalIgnoreCase);

        // Step 2: Provide phone number
        var r2 = await StreamingTestHelper.CollectAsync(
            _orchestrator.ProcessMessageStreamingAsync(sessionId, "555-1234"));

        // Step 3: Provide SSN
        var r3 = await StreamingTestHelper.CollectAsync(
            _orchestrator.ProcessMessageStreamingAsync(sessionId, "1234"));

        // After successful auth, should answer the pending query
        Assert.True(
            r3.Contains("John", StringComparison.OrdinalIgnoreCase) ||
            r3.Contains("187", StringComparison.OrdinalIgnoreCase) ||
            r3.Contains("verified", StringComparison.OrdinalIgnoreCase),
            $"Expected response with customer name or balance. Got: {r3}");
    }

    [Fact]
    public async Task Orchestrator_HandlesOutOfScope()
    {
        var sessionId = Guid.NewGuid().ToString();

        var response = await StreamingTestHelper.CollectAsync(
            _orchestrator.ProcessMessageStreamingAsync(sessionId, "What's the weather like today?"));

        Assert.True(
            response.Contains("utility", StringComparison.OrdinalIgnoreCase) ||
            response.Contains("bill", StringComparison.OrdinalIgnoreCase) ||
            response.Contains("account", StringComparison.OrdinalIgnoreCase),
            $"Expected out-of-scope response. Got: {response}");
    }

    [Fact]
    public async Task Orchestrator_InitiatesHandoff_ForHumanRequested()
    {
        var sessionId = Guid.NewGuid().ToString();

        var r1 = await StreamingTestHelper.CollectAsync(
            _orchestrator.ProcessMessageStreamingAsync(sessionId, "I want to speak to a representative"));

        Assert.True(
            r1.Contains("representative", StringComparison.OrdinalIgnoreCase) ||
            r1.Contains("reach out", StringComparison.OrdinalIgnoreCase) ||
            r1.Contains("forwarded", StringComparison.OrdinalIgnoreCase),
            $"Expected handoff acknowledgment. Got: {r1}");

        // User can continue chatting after handoff
        var r2 = await StreamingTestHelper.CollectAsync(
            _orchestrator.ProcessMessageStreamingAsync(sessionId, "What are my payment options?"));

        Assert.True(
            r2.Contains("pay", StringComparison.OrdinalIgnoreCase) ||
            r2.Contains("online", StringComparison.OrdinalIgnoreCase),
            $"Expected FAQ response. Got: {r2}");
    }

    [Fact]
    public async Task Orchestrator_ExcludesSuggestedActions_DuringAuthFlow()
    {
        var sessionId = Guid.NewGuid().ToString();

        var response = await StreamingTestHelper.CollectAsync(
            _orchestrator.ProcessMessageStreamingAsync(sessionId, "What is my current balance?"));

        // During auth flow, should not include "You might also want to ask" suggestions
        Assert.DoesNotContain("You might also want to ask", response);
    }
}
