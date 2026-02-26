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
        // Authenticate John Smith via the auth agent
        var customerId = await AuthenticateJohnSmithAsync();

        // Query balance
        var session = await _utilityDataAgent.CreateSessionAsync(customerId);
        var (text, _) = await StreamingTestHelper.CollectAsync(
            _utilityDataAgent.StreamAsync("What is my current balance?", session));

        Assert.Contains("187", text);
    }

    [Fact]
    public async Task DataAgent_AnalyzesUsage_WithComparison()
    {
        // Authenticate John Smith (has significant usage increase)
        var customerId = await AuthenticateJohnSmithAsync();

        // Ask about high bill
        var session = await _utilityDataAgent.CreateSessionAsync(customerId);
        var (text, _) = await StreamingTestHelper.CollectAsync(
            _utilityDataAgent.StreamAsync("Why is my bill so high?", session));

        // Should mention usage or increase
        Assert.True(
            text.Contains("usage", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("increase", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("higher", StringComparison.OrdinalIgnoreCase),
            $"Expected response about usage. Got: {text}");
    }

    /// <summary>
    /// Runs the full auth flow for John Smith and returns the authenticated customer ID.
    /// </summary>
    private async Task<string> AuthenticateJohnSmithAsync()
    {
        var (_, events1) = await StreamingTestHelper.CollectAsync(
            _authAgent.StreamAsync("I need help with my account", state: null));
        var state = events1.OfType<AuthStateEvent>().Single().FlowState;

        var (_, events2) = await StreamingTestHelper.CollectAsync(
            _authAgent.StreamAsync("555-1234", state));
        state = events2.OfType<AuthStateEvent>().Single().FlowState;

        var (_, events3) = await StreamingTestHelper.CollectAsync(
            _authAgent.StreamAsync("1234", state));
        var authEvent = events3.OfType<AuthStateEvent>().Single();
        Assert.Equal(AuthenticationState.Authenticated, authEvent.State);

        return authEvent.CustomerId!;
    }
}
