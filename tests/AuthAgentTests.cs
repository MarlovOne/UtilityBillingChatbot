// Copyright (c) Microsoft. All rights reserved.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using UtilityBillingChatbot.Agents.Auth;
using UtilityBillingChatbot.Infrastructure;
using UtilityBillingChatbot.Orchestration;

namespace UtilityBillingChatbot.Tests;

/// <summary>
/// Integration tests for the Auth Agent.
/// These tests require a configured LLM endpoint (see appsettings.json).
/// </summary>
[Collection("Sequential")]
public class AuthAgentTests : IAsyncLifetime
{
    private IHost _host = null!;
    private AuthAgent _authAgent = null!;

    public Task InitializeAsync()
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Configuration.SetBasePath(AppContext.BaseDirectory);
        builder.Configuration.AddJsonFile("appsettings.json", optional: false);
        builder.Configuration.AddEnvironmentVariables();

        builder.Services.AddUtilityBillingChatbot(builder.Configuration);

        _host = builder.Build();
        _authAgent = _host.Services.GetRequiredService<AuthAgent>();

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _host.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task AuthAgent_FindsCustomer_ByPhone()
    {
        var session = StreamingTestHelper.CreateTestSession();

        var (_, events1) = await StreamingTestHelper.RunTurnAsync(
            session, "I want to check my bill",
            msgs => _authAgent.StreamAsync(msgs, session.AuthFlowState));
        SaveAuthState(session, events1);

        var (_, events2) = await StreamingTestHelper.RunTurnAsync(
            session, "555-1234",
            msgs => _authAgent.StreamAsync(msgs, session.AuthFlowState));

        var meta2 = events2.OfType<AuthStateEvent>().Single();
        Assert.Equal(AuthenticationState.Verifying, meta2.State);
        Assert.Equal("John Smith", meta2.CustomerName);
        Assert.Equal("1234567890", meta2.CustomerId);
    }

    [Fact]
    public async Task AuthAgent_Authenticates_WithCorrectSSN()
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

        var m3 = events3.OfType<AuthStateEvent>().Single();
        Assert.Equal(AuthenticationState.Authenticated, m3.State);
        Assert.Equal("John Smith", m3.CustomerName);
    }

    [Fact]
    public async Task AuthAgent_LocksOut_AfterThreeFailures()
    {
        var session = StreamingTestHelper.CreateTestSession();

        var (_, events1) = await StreamingTestHelper.RunTurnAsync(
            session, "Check my bill",
            msgs => _authAgent.StreamAsync(msgs, session.AuthFlowState));
        SaveAuthState(session, events1);

        var (_, events2) = await StreamingTestHelper.RunTurnAsync(
            session, "555-1234",
            msgs => _authAgent.StreamAsync(msgs, session.AuthFlowState));
        SaveAuthState(session, events2);

        var (_, events3) = await StreamingTestHelper.RunTurnAsync(
            session, "0000",
            msgs => _authAgent.StreamAsync(msgs, session.AuthFlowState));
        SaveAuthState(session, events3);

        var (_, events4) = await StreamingTestHelper.RunTurnAsync(
            session, "1111",
            msgs => _authAgent.StreamAsync(msgs, session.AuthFlowState));
        SaveAuthState(session, events4);

        var (_, events5) = await StreamingTestHelper.RunTurnAsync(
            session, "2222",
            msgs => _authAgent.StreamAsync(msgs, session.AuthFlowState));

        var m5 = events5.OfType<AuthStateEvent>().Single();
        Assert.Equal(AuthenticationState.LockedOut, m5.State);
    }

    [Fact]
    public async Task AuthAgent_FindsCustomer_ByEmail()
    {
        var session = StreamingTestHelper.CreateTestSession();

        var (_, events1) = await StreamingTestHelper.RunTurnAsync(
            session, "Help with my account",
            msgs => _authAgent.StreamAsync(msgs, session.AuthFlowState));
        SaveAuthState(session, events1);

        var (_, events2) = await StreamingTestHelper.RunTurnAsync(
            session, "maria.garcia@example.com",
            msgs => _authAgent.StreamAsync(msgs, session.AuthFlowState));

        var m2 = events2.OfType<AuthStateEvent>().Single();
        Assert.Equal(AuthenticationState.Verifying, m2.State);
        Assert.Equal("Maria Garcia", m2.CustomerName);
    }

    [Fact]
    public async Task AuthAgent_FullFlow_PhoneAndSSN()
    {
        var session = StreamingTestHelper.CreateTestSession();

        var (_, events1) = await StreamingTestHelper.RunTurnAsync(
            session, "Did you receive my payment?",
            msgs => _authAgent.StreamAsync(msgs, session.AuthFlowState));
        SaveAuthState(session, events1);

        var (_, events2) = await StreamingTestHelper.RunTurnAsync(
            session, "My phone number is 555-1234",
            msgs => _authAgent.StreamAsync(msgs, session.AuthFlowState));
        SaveAuthState(session, events2);

        var (_, events3) = await StreamingTestHelper.RunTurnAsync(
            session, "The last 4 digits are 1234",
            msgs => _authAgent.StreamAsync(msgs, session.AuthFlowState));

        var m3 = events3.OfType<AuthStateEvent>().Single();
        Assert.Equal(AuthenticationState.Authenticated, m3.State);
        Assert.Equal("John Smith", m3.CustomerName);
        Assert.Equal("1234567890", m3.CustomerId);
    }

    /// <summary>
    /// Saves the auth flow state from events back to the session,
    /// mimicking what the orchestrator does between turns.
    /// </summary>
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
