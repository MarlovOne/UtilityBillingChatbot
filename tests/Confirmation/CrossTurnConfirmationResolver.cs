// Copyright (c) Microsoft. All rights reserved.

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

#pragma warning disable MEAI001

namespace UtilityBillingChatbot.Tests.Confirmation;

/// <summary>
/// Orchestrates confirm-then-act over a turn boundary using a decision sub-agent.
///
/// Lifecycle per user turn:
///  1. Propose turn  — runs the main agent; if it emits FunctionApprovalRequestContent,
///     stores it as PendingConfirmation and returns the proposal text.
///  2. Resume turn   — feeds the user's reply to the decision sub-agent, which returns
///     a ConfirmationDecision. The resolver then calls request.CreateResponse(approved)
///     and re-runs the main agent to produce the final user-facing text.
/// </summary>
public sealed class CrossTurnConfirmationResolver
{
    private readonly AIAgent _mainAgent;
    private readonly ChatClientAgent _decisionAgent;
    private readonly AgentSession _session;
    private readonly ConfirmationOptions _options;
    private readonly Func<DateTimeOffset> _clock;

    private int _turnCounter;
    public PendingConfirmation? Pending { get; private set; }

    public CrossTurnConfirmationResolver(
        AIAgent mainAgent,
        ChatClientAgent decisionAgent,
        AgentSession session,
        ConfirmationOptions? options = null,
        Func<DateTimeOffset>? clock = null)
    {
        _mainAgent = mainAgent;
        _decisionAgent = decisionAgent;
        _session = session;
        _options = options ?? ConfirmationOptions.Default;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public async Task<string> HandleTurnAsync(string userMessage, CancellationToken ct = default)
    {
        _turnCounter++;

        if (Pending is null)
        {
            return await ProposeAsync(userMessage, ct);
        }

        return await ResumeAsync(userMessage, ct);
    }

    private async Task<string> ProposeAsync(string userMessage, CancellationToken ct)
    {
        var response = await _mainAgent.RunAsync(userMessage, _session, cancellationToken: ct);

        var approvalRequest = response.Messages
            .SelectMany(m => m.Contents)
            .OfType<FunctionApprovalRequestContent>()
            .FirstOrDefault();

        if (approvalRequest is not null)
        {
            Pending = new PendingConfirmation(
                Request: approvalRequest,
                CreatedAt: _clock(),
                ProposedInTurn: _turnCounter);
        }

        return response.ToString();
    }

    private async Task<string> ResumeAsync(string userMessage, CancellationToken ct)
    {
        var pending = Pending!;

        // Turn-boundary invariant: must be a later turn than the one that proposed.
        if (pending.ProposedInTurn >= _turnCounter)
        {
            Pending = null;
            throw new InvalidOperationException("Same-turn resume is a bug.");
        }

        try
        {
            // TTL: expire without inference cost.
            if (pending.IsExpired(_options.Ttl, _clock()))
            {
                await InjectApprovalResponseAsync(pending, approved: false, ct);
                return "That request expired. Please ask again if you'd like to proceed.";
            }

            // L1 + L2: decision sub-agent has no sensitive tools and only emits the schema.
            var decisionInput = BuildDecisionInput(pending, userMessage);
            ConfirmationDecision decision;
            try
            {
                var decisionResponse = await _decisionAgent.RunAsync<ConfirmationDecision>(
                    decisionInput, session: null, serializerOptions: null, options: null, cancellationToken: ct);
                decision = decisionResponse.Result
                    ?? new ConfirmationDecision(Decision.Deny, "null result");
            }
            catch (Exception ex)
            {
                // L2 fail-closed: any structured-output failure is treated as Deny.
                decision = new ConfirmationDecision(Decision.Deny, $"parse failure: {ex.Message}");
            }

            var approved = decision.Decision == Decision.Approve;
            var finalText = await InjectApprovalResponseAsync(pending, approved, ct);

            if (!approved)
            {
                // Deterministic deny text so the deny path is cheap and predictable.
                return string.IsNullOrWhiteSpace(finalText)
                    ? "Cancelled. Let me know if you'd like to try again."
                    : finalText;
            }

            return finalText;
        }
        finally
        {
            Pending = null;
        }
    }

    private async Task<string> InjectApprovalResponseAsync(
        PendingConfirmation pending, bool approved, CancellationToken ct)
    {
        // L3: framework binds the response to the original FunctionCallId.
        var approvalResponse = pending.Request.CreateResponse(approved);
        var message = new ChatMessage(ChatRole.User, [approvalResponse]);
        var response = await _mainAgent.RunAsync([message], _session, cancellationToken: ct);
        return response.ToString();
    }

    private static string BuildDecisionInput(PendingConfirmation pending, string userReply)
    {
        var call = pending.Request.FunctionCall;
        var args = call.Arguments is null
            ? "(none)"
            : string.Join(", ", call.Arguments.Select(kv => $"{kv.Key}={kv.Value}"));

        return $"""
            Proposed action: {call.Name}({args})
            User reply: "{userReply}"

            Decide Approve or Deny. When in doubt, Deny.
            """;
    }
}

#pragma warning restore MEAI001
