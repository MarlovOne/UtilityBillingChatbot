# Payment Tool with Approval Flow Design

**Date:** 2026-02-18
**Stage:** 7 (Payment with Approval)
**Status:** Approved

## Overview

Add a payment tool to the UtilityDataAgent that requires user approval before execution. This demonstrates the Microsoft Agent Framework's `ApprovalRequiredAIFunction` pattern for sensitive operations.

## Requirements

- Authenticated users can request to pay outstanding bills
- Payment requires explicit user confirmation before execution
- User approval is interpreted via natural language (not literal Y/N)
- Payment is logged only (no mock data updates) - focus is on approval mechanics

## Architecture

### Components Modified

| Component | Change |
|-----------|--------|
| `UtilityDataContextProvider` | Add `MakePayment` tool method |
| `UtilityDataAgent` | Wrap payment tool with `ApprovalRequiredAIFunction` |
| `ChatbotOrchestrator` | Add approval handling loop for `UserInputRequests` |
| `UtilityDataModels` | Add `PaymentResult` record |

### Flow

```
User: "Pay my bill"
    ↓
UtilityDataAgent calls MakePayment tool
    ↓
ApprovalRequiredAIFunction intercepts → emits FunctionApprovalRequestContent
    ↓
Orchestrator detects UserInputRequests → prompts user
    "I'll submit a payment of $187.43 for February 2024. Should I proceed?"
    ↓
User: "yes please" / "go ahead" / "no wait"
    ↓
Orchestrator interprets response → calls request.CreateResponse(bool)
    ↓
On approval: agent executes MakePayment → logs payment
On denial: agent responds that payment was cancelled
    ↓
Agent returns final response to user
```

## Implementation Details

### Payment Tool

Added to `UtilityDataContextProvider`:

```csharp
[Description("Submit a payment for the customer's outstanding balance")]
public PaymentResult MakePayment(
    [Description("Amount to pay")] decimal amount,
    [Description("Bill period being paid (e.g., 'February 2024')")] string billingPeriod)
{
    _logger.LogInformation("Payment submitted: ${Amount} for {Period} by {Customer}",
        amount, billingPeriod, _customer.Name);

    return new PaymentResult(
        Success: true,
        Amount: amount,
        BillingPeriod: billingPeriod,
        ConfirmationNumber: Guid.NewGuid().ToString()[..8].ToUpper(),
        Message: $"Payment of ${amount:F2} for {billingPeriod} has been submitted.");
}
```

### Approval Wrapping

In `UtilityDataAgent` or `UtilityDataContextProvider.BuildTools()`:

```csharp
var paymentTool = AIFunctionFactory.Create(MakePayment);
var approvalRequiredPayment = new ApprovalRequiredAIFunction(paymentTool);
tools.Add(approvalRequiredPayment);
```

### Approval Interpretation

Simple keyword matching (no LLM call):

```csharp
private static bool IsApprovalResponse(string input)
{
    var normalized = input.Trim().ToLowerInvariant();

    // Denial keywords (check first)
    string[] denyKeywords = ["no", "cancel", "stop", "don't", "wait", "nevermind"];
    if (denyKeywords.Any(k => normalized.Contains(k)))
        return false;

    // Approval keywords
    string[] approveKeywords = ["yes", "yeah", "sure", "ok", "okay", "proceed", "go ahead", "do it", "confirm"];
    return approveKeywords.Any(k => normalized.Contains(k));
}
```

### Orchestrator Approval Loop

```csharp
var userInputRequests = response.UserInputRequests.ToList();

while (userInputRequests.Count > 0)
{
    foreach (var request in userInputRequests.OfType<FunctionApprovalRequestContent>())
    {
        var prompt = FormatApprovalPrompt(request);
        var userResponse = await GetUserInputAsync(prompt);

        bool approved = IsApprovalResponse(userResponse);
        var approvalMessage = new ChatMessage(ChatRole.User, [request.CreateResponse(approved)]);

        response = await session.Agent.RunAsync(approvalMessage, session.AgentSession);
    }

    userInputRequests = response.UserInputRequests.ToList();
}
```

## Key Framework Types

| Type | Purpose |
|------|---------|
| `ApprovalRequiredAIFunction` | Wraps a tool to require approval before execution |
| `FunctionApprovalRequestContent` | The pending approval request in `response.UserInputRequests` |
| `request.CreateResponse(bool)` | Creates approval/denial response message |

## Testing

- User asks to pay bill → agent pauses for approval
- User approves ("yes", "go ahead") → payment logged, confirmation returned
- User denies ("no", "cancel") → payment cancelled, agent acknowledges
- Ambiguous response → treated as denial (safe default)

## References

- Agent Framework samples: `/home/lmark/git/agent-framework/dotnet/samples/GettingStarted/Agents/Agent_Step04_UsingFunctionToolsWithApprovals/`
- Existing pattern: `AuthenticationContextProvider` for tool injection via `AIContextProvider`
