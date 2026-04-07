// Copyright (c) Microsoft. All rights reserved.

using Microsoft.Extensions.AI;

#pragma warning disable MEAI001 // FunctionApprovalRequestContent is experimental

namespace UtilityBillingChatbot.Tests.Confirmation;

/// <summary>
/// Single-slot state for a pending confirmation across a turn boundary.
/// </summary>
public sealed record PendingConfirmation(
    FunctionApprovalRequestContent Request,
    DateTimeOffset CreatedAt,
    int ProposedInTurn)
{
    public bool IsExpired(TimeSpan ttl, DateTimeOffset now) =>
        now - CreatedAt > ttl;
}

#pragma warning restore MEAI001
