// Copyright (c) Microsoft. All rights reserved.

using System.ComponentModel;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;

#pragma warning disable MEAI001 // FunctionApprovalRequestContent is experimental

namespace UtilityBillingChatbot.Tests;

/// <summary>
/// Standalone integration tests demonstrating the ApprovalRequiredAIFunction pattern
/// from Microsoft Agent Framework. No dependency on the chatbot project — creates its
/// own IChatClient directly from appsettings.json.
///
/// Pattern summary:
/// 1. Wrap any AIFunction with <c>new ApprovalRequiredAIFunction(tool)</c>
/// 2. When the LLM calls the wrapped tool, the agent pauses and returns
///    <c>FunctionApprovalRequestContent</c> in the response messages
/// 3. You inspect the request, decide to approve/deny, then send back
///    <c>request.CreateResponse(approved: true/false)</c>
/// 4. The agent resumes — executing the tool if approved, or telling the LLM
///    the call was denied if not.
/// </summary>
[Collection("Sequential")]
public class ApprovalRequiredToolTests : IAsyncLifetime
{
    /// <summary>
    /// Delay between tests to avoid rate limiting on free-tier endpoints (15 req/60s).
    /// </summary>
    private const int CooldownMs = 5_000;

    private readonly IChatClient _chatClient;

    public ApprovalRequiredToolTests()
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .AddEnvironmentVariables()
            .Build();

        var apiKey = config["LLM:OpenAI:ApiKey"];
        if (string.IsNullOrEmpty(apiKey))
            throw new InvalidOperationException(
                "OpenAI ApiKey required. Set LLM:OpenAI:ApiKey in appsettings.json or LLM__OpenAI__ApiKey env var.");

        var endpoint = config["LLM:OpenAI:Endpoint"];
        var model = config["LLM:DefaultModel"] ?? "gpt-4o-mini";

        OpenAI.OpenAIClient openAiClient;
        if (!string.IsNullOrEmpty(endpoint))
        {
            openAiClient = new OpenAI.OpenAIClient(
                new System.ClientModel.ApiKeyCredential(apiKey),
                new OpenAI.OpenAIClientOptions { Endpoint = new Uri(endpoint) });
        }
        else
        {
            openAiClient = new OpenAI.OpenAIClient(apiKey);
        }

        _chatClient = openAiClient.GetChatClient(model).AsIChatClient();
    }

    public Task InitializeAsync() => Task.Delay(CooldownMs);
    public Task DisposeAsync() => Task.CompletedTask;

    // ──────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────

    /// <summary>
    /// Extracts FunctionApprovalRequestContent from an AgentResponse.
    /// </summary>
    private static List<FunctionApprovalRequestContent> GetApprovalRequests(AgentResponse response) =>
        response.Messages
            .SelectMany(m => m.Contents)
            .OfType<FunctionApprovalRequestContent>()
            .ToList();

    /// <summary>
    /// Extracts FunctionApprovalRequestContent from streaming updates.
    /// </summary>
    private static List<FunctionApprovalRequestContent> GetApprovalRequests(
        IEnumerable<AgentResponseUpdate> updates) =>
        updates
            .SelectMany(u => u.Contents)
            .OfType<FunctionApprovalRequestContent>()
            .ToList();

    // ──────────────────────────────────────────────────
    // Test tools
    // ──────────────────────────────────────────────────

    [Description("Get the current account balance")]
    static string GetBalance() => "Your current balance is $187.43, due March 15, 2026.";

    [Description("Submit a payment for the customer's outstanding balance")]
    static string MakePayment(
        [Description("Amount to pay in dollars")] decimal amount)
        => $"Payment of ${amount:F2} submitted. Confirmation #: ABC123.";

    [Description("Disconnect the customer's service immediately")]
    static string DisconnectService(
        [Description("The account number to disconnect")] string accountNumber)
        => $"Service for account {accountNumber} has been disconnected.";

    // ──────────────────────────────────────────────────
    // 1. Basic: wrapped tool produces approval request
    // ──────────────────────────────────────────────────

    /// <summary>
    /// Demonstrates that wrapping a tool with ApprovalRequiredAIFunction
    /// causes the agent to pause with a FunctionApprovalRequestContent
    /// instead of executing the tool immediately.
    /// </summary>
    [Fact]
    public async Task ApprovalRequired_PausesBeforeExecution()
    {
        var agent = _chatClient.AsAIAgent(
            instructions: "You help with account billing. Use tools when relevant.",
            tools:
            [
                AIFunctionFactory.Create(GetBalance),
                new ApprovalRequiredAIFunction(AIFunctionFactory.Create(MakePayment))
            ]);

        var session = await agent.CreateSessionAsync();
        var response = await agent.RunAsync("Please pay my bill of $187.43", session);

        var approvalRequests = GetApprovalRequests(response);

        // The agent should have paused waiting for approval
        Assert.NotEmpty(approvalRequests);
        Assert.Contains(approvalRequests, r => r.FunctionCall.Name == "MakePayment");

        // The function should NOT have been called yet
        Assert.DoesNotContain("ABC123", response.ToString());
    }

    // ──────────────────────────────────────────────────
    // 2. Approve: tool executes after approval
    // ──────────────────────────────────────────────────

    /// <summary>
    /// Full approve flow: agent requests approval -> we approve -> agent
    /// executes the tool and returns the result.
    /// </summary>
    [Fact]
    public async Task ApprovalRequired_ApproveExecutesTool()
    {
        var agent = _chatClient.AsAIAgent(
            instructions: "You help with account billing. Use tools when relevant.",
            tools:
            [
                AIFunctionFactory.Create(GetBalance),
                new ApprovalRequiredAIFunction(AIFunctionFactory.Create(MakePayment))
            ]);

        var session = await agent.CreateSessionAsync();

        // Step 1: Initial request — agent tries to call the tool
        var response = await agent.RunAsync("Please pay my bill of $187.43", session);
        var approvalRequests = GetApprovalRequests(response);
        Assert.NotEmpty(approvalRequests);

        // Step 2: Approve each request
        var approvalMessages = approvalRequests
            .Select(req => new ChatMessage(ChatRole.User, [req.CreateResponse(approved: true)]))
            .ToList();

        // Step 3: Send approvals back to continue execution
        response = await agent.RunAsync(approvalMessages, session);

        // The tool should have executed and the agent should report success
        var text = response.ToString();
        Assert.Contains("ABC123", text); // Confirmation number from MakePayment
    }

    // ──────────────────────────────────────────────────
    // 3. Deny: tool is NOT executed
    // ──────────────────────────────────────────────────

    /// <summary>
    /// Deny flow: agent requests approval -> we deny -> agent does NOT
    /// execute the tool and informs the user.
    /// </summary>
    [Fact]
    public async Task ApprovalRequired_DenyBlocksExecution()
    {
        var agent = _chatClient.AsAIAgent(
            instructions: "You help with account billing. Use tools when relevant. If a tool call is denied, let the user know.",
            tools:
            [
                AIFunctionFactory.Create(GetBalance),
                new ApprovalRequiredAIFunction(AIFunctionFactory.Create(MakePayment))
            ]);

        var session = await agent.CreateSessionAsync();

        // Step 1: Initial request
        var response = await agent.RunAsync("Please pay my bill of $187.43", session);
        var approvalRequests = GetApprovalRequests(response);
        Assert.NotEmpty(approvalRequests);

        // Step 2: Deny
        var denialMessages = approvalRequests
            .Select(req => new ChatMessage(ChatRole.User, [req.CreateResponse(approved: false)]))
            .ToList();

        // Step 3: Send denials back
        response = await agent.RunAsync(denialMessages, session);

        // The tool should NOT have executed — no confirmation number
        var text = response.ToString();
        Assert.DoesNotContain("ABC123", text);
    }

    // ──────────────────────────────────────────────────
    // 4. Mixed: some tools require approval, others don't
    // ──────────────────────────────────────────────────

    /// <summary>
    /// When only some tools are wrapped, non-wrapped tools execute
    /// immediately while wrapped ones pause for approval.
    /// </summary>
    [Fact]
    public async Task ApprovalRequired_OnlyWrappedToolsPause()
    {
        var agent = _chatClient.AsAIAgent(
            instructions: "You help with account billing. Always check the balance first before making payments.",
            tools:
            [
                AIFunctionFactory.Create(GetBalance), // No approval needed
                new ApprovalRequiredAIFunction(AIFunctionFactory.Create(MakePayment)) // Needs approval
            ]);

        var session = await agent.CreateSessionAsync();

        // Ask to check balance — should execute immediately, no approval needed
        var response = await agent.RunAsync("What is my current balance?", session);
        var approvalRequests = GetApprovalRequests(response);

        Assert.Empty(approvalRequests);
        Assert.Contains("187.43", response.ToString());
    }

    // ──────────────────────────────────────────────────
    // 5. Streaming: approval loop with RunStreamingAsync
    // ──────────────────────────────────────────────────

    /// <summary>
    /// Same approval pattern but using the streaming API. Approval requests
    /// appear in the streaming updates rather than the final response.
    /// </summary>
    [Fact]
    public async Task ApprovalRequired_StreamingApproveFlow()
    {
        var agent = _chatClient.AsAIAgent(
            instructions: "You help with account billing. Use tools when relevant.",
            tools:
            [
                AIFunctionFactory.Create(GetBalance),
                new ApprovalRequiredAIFunction(AIFunctionFactory.Create(MakePayment))
            ]);

        var session = await agent.CreateSessionAsync();

        // Step 1: Stream initial request
        var updates = await agent.RunStreamingAsync("Pay my bill of $187.43", session).ToListAsync();
        var approvalRequests = GetApprovalRequests(updates);

        Assert.NotEmpty(approvalRequests);

        // Step 2: Approve
        var approvalMessages = approvalRequests
            .Select(req => new ChatMessage(ChatRole.User, [req.CreateResponse(approved: true)]))
            .ToList();

        // Step 3: Stream the continuation
        updates = await agent.RunStreamingAsync(approvalMessages, session).ToListAsync();
        var finalText = updates.ToAgentResponse().ToString();

        Assert.Contains("ABC123", finalText);
    }

    // ──────────────────────────────────────────────────
    // 6. Multiple approval-required tools
    // ──────────────────────────────────────────────────

    /// <summary>
    /// When multiple tools require approval, each generates its own
    /// FunctionApprovalRequestContent. You can approve some and deny others.
    /// </summary>
    [Fact]
    public async Task ApprovalRequired_MultipleToolsSelectiveApproval()
    {
        var agent = _chatClient.AsAIAgent(
            instructions: """
                You manage utility accounts. When asked to make a payment AND disconnect,
                call both tools. Do not ask the user for confirmation yourself — the system
                handles approvals automatically.
                """,
            tools:
            [
                AIFunctionFactory.Create(GetBalance),
                new ApprovalRequiredAIFunction(AIFunctionFactory.Create(MakePayment)),
                new ApprovalRequiredAIFunction(AIFunctionFactory.Create(DisconnectService))
            ]);

        var session = await agent.CreateSessionAsync();

        // Ask for both actions
        var response = await agent.RunAsync(
            "Pay my bill of $187.43 and then disconnect account ACC-001", session);

        var approvalRequests = GetApprovalRequests(response);

        // At least one approval request should appear
        Assert.NotEmpty(approvalRequests);

        // Approve payment, deny disconnect
        var approvalMessages = approvalRequests
            .Select(req =>
            {
                var approved = req.FunctionCall.Name == "MakePayment";
                return new ChatMessage(ChatRole.User, [req.CreateResponse(approved)]);
            })
            .ToList();

        response = await agent.RunAsync(approvalMessages, session);

        // Handle any remaining approval requests (LLM may call tools sequentially)
        var remaining = GetApprovalRequests(response);
        while (remaining.Count > 0)
        {
            approvalMessages = remaining
                .Select(req =>
                {
                    var approved = req.FunctionCall.Name == "MakePayment";
                    return new ChatMessage(ChatRole.User, [req.CreateResponse(approved)]);
                })
                .ToList();

            response = await agent.RunAsync(approvalMessages, session);
            remaining = GetApprovalRequests(response);
        }

        var text = response.ToString();

        // Payment should have gone through
        Assert.Contains("ABC123", text);
        // Disconnect should NOT have gone through
        Assert.DoesNotContain("has been disconnected", text);
    }

    // ──────────────────────────────────────────────────
    // 7. Middleware: approval handling via agent middleware
    // ──────────────────────────────────────────────────

    /// <summary>
    /// Instead of manually checking for approval requests in a loop, you can
    /// use agent-level middleware to handle approvals automatically.
    /// This encapsulates the confirm-then-act loop as reusable middleware.
    /// </summary>
    [Fact]
    public async Task ApprovalRequired_MiddlewareAutoApproves()
    {
        // Create base agent with approval-required tool
        var baseAgent = _chatClient.AsAIAgent(
            instructions: "You help with account billing. Use tools when relevant.",
            tools:
            [
                AIFunctionFactory.Create(GetBalance),
                new ApprovalRequiredAIFunction(AIFunctionFactory.Create(MakePayment))
            ]);

        // Wrap with auto-approve middleware (simulates a policy that approves everything)
        var agent = baseAgent.AsBuilder()
            .Use(AutoApproveMiddleware, null)
            .Build();

        var session = await agent.CreateSessionAsync();

        // The middleware handles approval automatically — no manual loop needed
        var response = await agent.RunAsync("Pay my bill of $187.43", session);

        // Tool should have executed through the middleware
        Assert.Contains("ABC123", response.ToString());
        Assert.Empty(GetApprovalRequests(response));
    }

    /// <summary>
    /// Middleware that automatically approves all function approval requests.
    /// In production, this could check policies, call an external approval service, etc.
    /// </summary>
    private static async Task<AgentResponse> AutoApproveMiddleware(
        IEnumerable<ChatMessage> messages,
        AgentSession? session,
        AgentRunOptions? options,
        AIAgent innerAgent,
        CancellationToken ct)
    {
        var response = await innerAgent.RunAsync(messages, session, options, ct);
        var approvalRequests = GetApprovalRequests(response);

        while (approvalRequests.Count > 0)
        {
            var approvalMessages = approvalRequests
                .Select(req => new ChatMessage(ChatRole.User,
                    [req.CreateResponse(approved: true)]))
                .ToList();

            response = await innerAgent.RunAsync(approvalMessages, session, options, ct);
            approvalRequests = GetApprovalRequests(response);
        }

        return response;
    }

    // ──────────────────────────────────────────────────
    // 8. Middleware: policy-based selective approval
    // ──────────────────────────────────────────────────

    /// <summary>
    /// Demonstrates a middleware that applies a policy: approve payments
    /// under $500, deny anything above that threshold.
    /// </summary>
    [Fact]
    public async Task ApprovalRequired_MiddlewarePolicyBasedApproval()
    {
        var baseAgent = _chatClient.AsAIAgent(
            instructions: "You help with billing. Use tools when relevant. If a payment is denied, tell the user it exceeded the auto-approval limit.",
            tools:
            [
                AIFunctionFactory.Create(GetBalance),
                new ApprovalRequiredAIFunction(AIFunctionFactory.Create(MakePayment))
            ]);

        // Wrap with policy middleware: auto-approve payments under $500
        var agent = baseAgent.AsBuilder()
            .Use(PolicyApprovalMiddleware, null)
            .Build();

        var session = await agent.CreateSessionAsync();

        // Small payment — should be auto-approved by policy
        var response = await agent.RunAsync("Pay $187.43 on my bill", session);
        Assert.Contains("ABC123", response.ToString());
    }

    /// <summary>
    /// Policy middleware: auto-approve MakePayment calls under $500, deny above.
    /// </summary>
    private static async Task<AgentResponse> PolicyApprovalMiddleware(
        IEnumerable<ChatMessage> messages,
        AgentSession? session,
        AgentRunOptions? options,
        AIAgent innerAgent,
        CancellationToken ct)
    {
        var response = await innerAgent.RunAsync(messages, session, options, ct);
        var approvalRequests = GetApprovalRequests(response);

        while (approvalRequests.Count > 0)
        {
            var approvalMessages = approvalRequests
                .Select(req =>
                {
                    var approved = ShouldAutoApprove(req);
                    return new ChatMessage(ChatRole.User,
                        [req.CreateResponse(approved)]);
                })
                .ToList();

            response = await innerAgent.RunAsync(approvalMessages, session, options, ct);
            approvalRequests = GetApprovalRequests(response);
        }

        return response;
    }

    private static bool ShouldAutoApprove(FunctionApprovalRequestContent req)
    {
        if (req.FunctionCall.Name != "MakePayment")
            return false;

        if (req.FunctionCall.Arguments?.TryGetValue("amount", out var amountObj) == true)
        {
            // Arguments arrive as JsonElement from the LLM response
            var amount = amountObj is System.Text.Json.JsonElement je
                ? je.GetDecimal()
                : Convert.ToDecimal(amountObj);
            return amount < 500m;
        }

        return false;
    }
}

#pragma warning restore MEAI001
