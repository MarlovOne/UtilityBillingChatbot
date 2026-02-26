// Copyright (c) Microsoft. All rights reserved.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using UtilityBillingChatbot.Agents.Auth;
using UtilityBillingChatbot.Agents.UtilityData;
using UtilityBillingChatbot.Infrastructure;
using UtilityBillingChatbot.Orchestration;

namespace UtilityBillingChatbot.Tests;

/// <summary>
/// Integration tests for the UtilityData Agent.
/// These tests require a configured LLM endpoint (see appsettings.json).
/// </summary>
[Collection("Sequential")]
public class UtilityDataAgentTests : IAsyncLifetime
{
    private IHost _host = null!;
    private AuthAgent _authAgent = null!;
    private UtilityDataAgent _utilityDataAgent = null!;

    public Task InitializeAsync()
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Configuration.SetBasePath(AppContext.BaseDirectory);
        builder.Configuration.AddJsonFile("appsettings.json", optional: false);
        builder.Configuration.AddEnvironmentVariables();

        builder.Services.AddUtilityBillingChatbot(builder.Configuration);

        _host = builder.Build();
        _authAgent = _host.Services.GetRequiredService<AuthAgent>();
        _utilityDataAgent = _host.Services.GetRequiredService<UtilityDataAgent>();

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _host.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task DataAgent_ThrowsException_WhenCustomerNotFound()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _utilityDataAgent.CreateSessionAsync("nonexistent-id"));
    }

    [Fact]
    public async Task DataAgent_FetchesBalance_WhenAuthenticated()
    {
        var session = await AuthenticateJohnSmithAsync();
        var customerId = session.UserContext.CustomerId!;

        var utilitySession = await _utilityDataAgent.CreateSessionAsync(customerId);
        var (text, _) = await StreamingTestHelper.RunTurnAsync(
            session, "What is my current balance?",
            msgs => _utilityDataAgent.StreamAsync(msgs, utilitySession));

        Assert.Contains("187", text);
    }

    [Fact]
    public async Task DataAgent_AnalyzesUsage_WithComparison()
    {
        var session = await AuthenticateJohnSmithAsync();
        var customerId = session.UserContext.CustomerId!;

        var utilitySession = await _utilityDataAgent.CreateSessionAsync(customerId);
        var (text, _) = await StreamingTestHelper.RunTurnAsync(
            session, "Why is my bill so high?",
            msgs => _utilityDataAgent.StreamAsync(msgs, utilitySession));

        Assert.True(
            text.Contains("usage", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("increase", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("higher", StringComparison.OrdinalIgnoreCase),
            $"Expected response about usage. Got: {text}");
    }

    /// <summary>
    /// Runs the full auth flow for John Smith and returns the ChatSession
    /// with authenticated state.
    /// </summary>
    private async Task<ChatSession> AuthenticateJohnSmithAsync()
    {
        var session = StreamingTestHelper.CreateTestSession();

        var (_, events1) = await StreamingTestHelper.RunTurnAsync(
            session, "I need help with my account",
            msgs => _authAgent.StreamAsync(msgs, session.AuthFlowState));
        SaveAuthState(session, events1);

        var (_, events2) = await StreamingTestHelper.RunTurnAsync(
            session, "555-1234",
            msgs => _authAgent.StreamAsync(msgs, session.AuthFlowState));
        SaveAuthState(session, events2);

        var (_, events3) = await StreamingTestHelper.RunTurnAsync(
            session, "1234",
            msgs => _authAgent.StreamAsync(msgs, session.AuthFlowState));
        var authEvent = events3.OfType<AuthStateEvent>().Single();
        Assert.Equal(AuthenticationState.Authenticated, authEvent.State);
        SaveAuthState(session, events3);

        return session;
    }

    private static void SaveAuthState(ChatSession session, List<ChatEvent> events)
    {
        var authEvent = events.OfType<AuthStateEvent>().SingleOrDefault();
        if (authEvent is null) return;

        session.AuthFlowState = authEvent.FlowState;
        if (authEvent.CustomerId is not null)
            session.UserContext.CustomerId = authEvent.CustomerId;
        if (authEvent.CustomerName is not null)
            session.UserContext.CustomerName = authEvent.CustomerName;
    }
}
