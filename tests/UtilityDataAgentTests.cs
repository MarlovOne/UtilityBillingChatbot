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
    public async Task DataAgent_ThrowsException_WhenNoAuthSession()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in _utilityDataAgent.StreamAsync("What is my balance?"))
            {
            }
        });
    }

    [Fact]
    public async Task DataAgent_ThrowsException_WhenNotAuthenticated()
    {
        // Get a session that's in Verifying state (not yet authenticated)
        var authSession = await _authAgent.CreateSessionAsync();

        await StreamingTestHelper.CollectAsync(
            _authAgent.StreamAsync("I need help", authSession));
        await StreamingTestHelper.CollectAsync(
            _authAgent.StreamAsync("555-1234", authSession));

        Assert.Equal(AuthenticationState.Verifying, authSession.Provider.AuthState);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _utilityDataAgent.CreateSessionAsync(authSession));
    }

    [Fact]
    public async Task DataAgent_FetchesBalance_WhenAuthenticated()
    {
        // Authenticate John Smith
        var authSession = await _authAgent.CreateSessionAsync();

        await StreamingTestHelper.CollectAsync(
            _authAgent.StreamAsync("I need help with my account", authSession));
        await StreamingTestHelper.CollectAsync(
            _authAgent.StreamAsync("555-1234", authSession));
        var (_, events3) = await StreamingTestHelper.CollectAsync(
            _authAgent.StreamAsync("1234", authSession));
        var m3 = events3.OfType<AuthStateEvent>().Single();
        Assert.Equal(AuthenticationState.Authenticated, m3.State);

        // Query balance
        var (text, events) = await StreamingTestHelper.CollectAsync(
            _utilityDataAgent.StreamAsync("What is my current balance?",
                authSession: authSession));

        Assert.Contains("187", text);
    }

    [Fact]
    public async Task DataAgent_AnalyzesUsage_WithComparison()
    {
        // Authenticate John Smith (has significant usage increase)
        var authSession = await _authAgent.CreateSessionAsync();

        await StreamingTestHelper.CollectAsync(
            _authAgent.StreamAsync("Check my bill", authSession));
        await StreamingTestHelper.CollectAsync(
            _authAgent.StreamAsync("555-1234", authSession));
        var (_, events3) = await StreamingTestHelper.CollectAsync(
            _authAgent.StreamAsync("1234", authSession));
        var m3 = events3.OfType<AuthStateEvent>().Single();
        Assert.Equal(AuthenticationState.Authenticated, m3.State);

        // Ask about high bill
        var (text, events) = await StreamingTestHelper.CollectAsync(
            _utilityDataAgent.StreamAsync("Why is my bill so high?",
                authSession: authSession));

        // Should mention usage or increase
        Assert.True(
            text.Contains("usage", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("increase", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("higher", StringComparison.OrdinalIgnoreCase),
            $"Expected response about usage. Got: {text}");
    }
}
