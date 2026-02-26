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
        var (_, events1) = await StreamingTestHelper.CollectAsync(
            _authAgent.StreamAsync("I want to check my bill", state: null));
        var state1 = events1.OfType<AuthStateEvent>().Single().FlowState;

        var (_, events2) = await StreamingTestHelper.CollectAsync(
            _authAgent.StreamAsync("555-1234", state1));

        var meta2 = events2.OfType<AuthStateEvent>().Single();
        Assert.Equal(AuthenticationState.Verifying, meta2.State);
        Assert.Equal("John Smith", meta2.CustomerName);
        Assert.Equal("1234567890", meta2.CustomerId);
    }

    [Fact]
    public async Task AuthAgent_Authenticates_WithCorrectSSN()
    {
        var (_, events1) = await StreamingTestHelper.CollectAsync(
            _authAgent.StreamAsync("I need help with my account", state: null));
        var state1 = events1.OfType<AuthStateEvent>().Single().FlowState;

        var (_, events2) = await StreamingTestHelper.CollectAsync(
            _authAgent.StreamAsync("555-1234", state1));
        var state2 = events2.OfType<AuthStateEvent>().Single().FlowState;

        var (_, events3) = await StreamingTestHelper.CollectAsync(
            _authAgent.StreamAsync("1234", state2));

        var m3 = events3.OfType<AuthStateEvent>().Single();
        Assert.Equal(AuthenticationState.Authenticated, m3.State);
        Assert.Equal("John Smith", m3.CustomerName);
    }

    [Fact]
    public async Task AuthAgent_LocksOut_AfterThreeFailures()
    {
        var (_, events1) = await StreamingTestHelper.CollectAsync(
            _authAgent.StreamAsync("Check my bill", state: null));
        var state = events1.OfType<AuthStateEvent>().Single().FlowState;

        var (_, events2) = await StreamingTestHelper.CollectAsync(
            _authAgent.StreamAsync("555-1234", state));
        state = events2.OfType<AuthStateEvent>().Single().FlowState;

        var (_, events3) = await StreamingTestHelper.CollectAsync(
            _authAgent.StreamAsync("0000", state));
        state = events3.OfType<AuthStateEvent>().Single().FlowState;

        var (_, events4) = await StreamingTestHelper.CollectAsync(
            _authAgent.StreamAsync("1111", state));
        state = events4.OfType<AuthStateEvent>().Single().FlowState;

        var (_, events5) = await StreamingTestHelper.CollectAsync(
            _authAgent.StreamAsync("2222", state));

        var m5 = events5.OfType<AuthStateEvent>().Single();
        Assert.Equal(AuthenticationState.LockedOut, m5.State);
    }

    [Fact]
    public async Task AuthAgent_FindsCustomer_ByEmail()
    {
        var (_, events1) = await StreamingTestHelper.CollectAsync(
            _authAgent.StreamAsync("Help with my account", state: null));
        var state = events1.OfType<AuthStateEvent>().Single().FlowState;

        var (_, events2) = await StreamingTestHelper.CollectAsync(
            _authAgent.StreamAsync("maria.garcia@example.com", state));

        var m2 = events2.OfType<AuthStateEvent>().Single();
        Assert.Equal(AuthenticationState.Verifying, m2.State);
        Assert.Equal("Maria Garcia", m2.CustomerName);
    }

    [Fact]
    public async Task AuthAgent_FullFlow_PhoneAndSSN()
    {
        var (_, events1) = await StreamingTestHelper.CollectAsync(
            _authAgent.StreamAsync("Did you receive my payment?", state: null));
        var state = events1.OfType<AuthStateEvent>().Single().FlowState;

        var (_, events2) = await StreamingTestHelper.CollectAsync(
            _authAgent.StreamAsync("My phone number is 555-1234", state));
        state = events2.OfType<AuthStateEvent>().Single().FlowState;

        var (_, events3) = await StreamingTestHelper.CollectAsync(
            _authAgent.StreamAsync("The last 4 digits are 1234", state));

        var m3 = events3.OfType<AuthStateEvent>().Single();
        Assert.Equal(AuthenticationState.Authenticated, m3.State);
        Assert.Equal("John Smith", m3.CustomerName);
        Assert.Equal("1234567890", m3.CustomerId);
    }
}
