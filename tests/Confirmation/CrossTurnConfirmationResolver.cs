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

    private Task<string> ResumeAsync(string userMessage, CancellationToken ct) =>
        throw new NotImplementedException("Implemented in Task 4.");
}

#pragma warning restore MEAI001
