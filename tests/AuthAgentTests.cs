// Copyright (c) Microsoft. All rights reserved.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using UtilityBillingChatbot.Agents.Auth;
using UtilityBillingChatbot.Infrastructure;

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
        // Act
        var response1 = await _authAgent.RunAsync("I want to check my bill");
        var response2 = await _authAgent.RunAsync("555-1234", response1.Session);

        // Assert
        Assert.Equal(AuthenticationState.Verifying, response2.AuthState);
        Assert.Equal("John Smith", response2.CustomerName);
        Assert.Equal("1234567890", response2.CustomerId);
    }

    [Fact]
    public async Task AuthAgent_Authenticates_WithCorrectSSN()
    {
        // Act
        var r1 = await _authAgent.RunAsync("I need help with my account");
        var r2 = await _authAgent.RunAsync("555-1234", r1.Session);
        var r3 = await _authAgent.RunAsync("1234", r2.Session);

        // Assert
        Assert.True(r3.IsAuthenticated);
        Assert.Equal("John Smith", r3.CustomerName);
    }

    [Fact]
    public async Task AuthAgent_LocksOut_AfterThreeFailures()
    {
        // Act
        var r1 = await _authAgent.RunAsync("Check my bill");
        var r2 = await _authAgent.RunAsync("555-1234", r1.Session);
        var r3 = await _authAgent.RunAsync("0000", r2.Session);  // Wrong
        var r4 = await _authAgent.RunAsync("1111", r3.Session);  // Wrong
        var r5 = await _authAgent.RunAsync("2222", r4.Session);  // Wrong

        // Assert
        Assert.Equal(AuthenticationState.LockedOut, r5.AuthState);
    }

    [Fact]
    public async Task AuthAgent_FindsCustomer_ByEmail()
    {
        // Act
        var r1 = await _authAgent.RunAsync("Help with my account");
        var r2 = await _authAgent.RunAsync("maria.garcia@example.com", r1.Session);

        // Assert
        Assert.Equal(AuthenticationState.Verifying, r2.AuthState);
        Assert.Equal("Maria Garcia", r2.CustomerName);
    }

    [Fact]
    public async Task AuthAgent_FullFlow_PhoneAndSSN()
    {
        // Act
        var r1 = await _authAgent.RunAsync("Did you receive my payment?");
        var r2 = await _authAgent.RunAsync("My phone number is 555-1234", r1.Session);
        var r3 = await _authAgent.RunAsync("The last 4 digits are 1234", r2.Session);

        // Assert
        Assert.True(r3.IsAuthenticated);
        Assert.Equal("John Smith", r3.CustomerName);
        Assert.Equal("1234567890", r3.CustomerId);
    }
}
