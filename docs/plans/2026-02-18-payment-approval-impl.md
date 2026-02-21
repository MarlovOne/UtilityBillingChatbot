# Payment Approval Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add a payment tool to UtilityDataAgent that requires user approval before execution using the Agent Framework's `ApprovalRequiredAIFunction` pattern.

**Architecture:** The payment tool is wrapped with `ApprovalRequiredAIFunction` in the context provider. When the agent calls it, the framework emits a `FunctionApprovalRequestContent` in `response.UserInputRequests`. The orchestrator handles the approval loop by prompting the user and interpreting their natural language response.

**Tech Stack:** Microsoft Agent Framework (`ApprovalRequiredAIFunction`, `FunctionApprovalRequestContent`), existing `UtilityDataContextProvider` pattern

---

## Task 1: Add PaymentResult Model

**Files:**
- Modify: `src/Agents/UtilityData/UtilityDataModels.cs`

**Step 1: Add PaymentResult record**

Add to end of file:

```csharp
/// <summary>
/// Result from MakePayment tool.
/// </summary>
public record PaymentResult(
    bool Success,
    decimal Amount,
    string BillingPeriod,
    string ConfirmationNumber,
    string Message);
```

**Step 2: Build to verify**

Run: `dotnet build`
Expected: Build succeeded

---

## Task 2: Add MakePayment Tool to ContextProvider

**Files:**
- Modify: `src/Agents/UtilityData/UtilityDataContextProvider.cs`

**Step 1: Add logger field and update constructor**

Add logger field at top of class (after `_customer`):

```csharp
private readonly ILogger<UtilityDataContextProvider>? _logger;

public UtilityDataContextProvider(UtilityCustomer customer, ILogger<UtilityDataContextProvider>? logger = null)
{
    _customer = customer;
    _logger = logger;
}
```

**Step 2: Add MakePayment tool method**

Add after `GetBillingHistory` method (inside the `#region Tool Methods`):

```csharp
[Description("Submit a payment for the customer's outstanding balance")]
public PaymentResult MakePayment(
    [Description("Amount to pay in dollars")] decimal amount,
    [Description("Bill period being paid (e.g., 'February 2024')")] string billingPeriod)
{
    _logger?.LogInformation(
        "Payment submitted: ${Amount} for {Period} by {Customer} ({Account})",
        amount, billingPeriod, _customer.Name, _customer.AccountNumber);

    return new PaymentResult(
        Success: true,
        Amount: amount,
        BillingPeriod: billingPeriod,
        ConfirmationNumber: Guid.NewGuid().ToString()[..8].ToUpperInvariant(),
        Message: $"Payment of ${amount:F2} for {billingPeriod} has been submitted successfully.");
}
```

**Step 3: Add MakePayment to tools list in BuildTools**

In `BuildTools()` method, add the payment tool to the list. This tool needs `ApprovalRequiredAIFunction` wrapper:

```csharp
private List<AITool> BuildTools()
{
    var paymentTool = AIFunctionFactory.Create(MakePayment,
        description: "Submit a payment for the customer's outstanding balance");

    return
    [
        AIFunctionFactory.Create(GetAccountBalance,
            description: "Get the current account balance, due date, and last payment info"),
        AIFunctionFactory.Create(GetPaymentStatus,
            description: "Check if a recent payment has been received"),
        AIFunctionFactory.Create(GetDueDate,
            description: "Get the bill due date and days until due"),
        AIFunctionFactory.Create(GetUsageAnalysis,
            description: "Compare current usage to previous period and analyze changes"),
        AIFunctionFactory.Create(GetAutoPayStatus,
            description: "Check if the customer is enrolled in AutoPay"),
        AIFunctionFactory.Create(GetBillDetails,
            description: "Get details of the most recent bill"),
        AIFunctionFactory.Create(GetMeterReadType,
            description: "Check if the last meter read was actual or estimated"),
        AIFunctionFactory.Create(GetBillingHistory,
            description: "Get a list of recent bills"),
        new ApprovalRequiredAIFunction(paymentTool)
    ];
}
```

**Step 4: Add using statement**

Add at top of file:

```csharp
using Microsoft.Extensions.Logging;
```

**Step 5: Build to verify**

Run: `dotnet build`
Expected: Build succeeded

---

## Task 3: Update UtilityDataAgent to Pass Logger

**Files:**
- Modify: `src/Agents/UtilityData/UtilityDataAgent.cs`

**Step 1: Add ILoggerFactory dependency**

Update constructor to accept `ILoggerFactory`:

```csharp
private readonly ILoggerFactory _loggerFactory;

public UtilityDataAgent(
    IChatClient chatClient,
    MockCISDatabase cisDatabase,
    ILoggerFactory loggerFactory,
    ILogger<UtilityDataAgent> logger)
{
    _chatClient = chatClient;
    _cisDatabase = cisDatabase;
    _loggerFactory = loggerFactory;
    _logger = logger;
}
```

**Step 2: Pass logger to UtilityDataContextProvider**

In `CreateSessionAsync`, update provider creation:

```csharp
var provider = new UtilityDataContextProvider(
    customer,
    _loggerFactory.CreateLogger<UtilityDataContextProvider>());
```

**Step 3: Build to verify**

Run: `dotnet build`
Expected: Build succeeded

---

## Task 4: Add Approval Handler Interface and Implementation

**Files:**
- Create: `src/Orchestration/IApprovalHandler.cs`

**Step 1: Create IApprovalHandler interface**

```csharp
// Copyright (c) Microsoft. All rights reserved.

namespace UtilityBillingChatbot.Orchestration;

/// <summary>
/// Handles approval requests for sensitive operations.
/// </summary>
public interface IApprovalHandler
{
    /// <summary>
    /// Prompts the user for approval and returns their decision.
    /// </summary>
    /// <param name="prompt">The prompt to show the user.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if approved, false if denied.</returns>
    Task<bool> RequestApprovalAsync(string prompt, CancellationToken cancellationToken = default);
}
```

**Step 2: Build to verify**

Run: `dotnet build`
Expected: Build succeeded

---

## Task 5: Create ConsoleApprovalHandler

**Files:**
- Create: `src/Orchestration/ConsoleApprovalHandler.cs`

**Step 1: Create ConsoleApprovalHandler class**

```csharp
// Copyright (c) Microsoft. All rights reserved.

using Microsoft.Extensions.Logging;

namespace UtilityBillingChatbot.Orchestration;

/// <summary>
/// Console-based approval handler that prompts for user input.
/// </summary>
public class ConsoleApprovalHandler : IApprovalHandler
{
    private readonly ILogger<ConsoleApprovalHandler> _logger;

    public ConsoleApprovalHandler(ILogger<ConsoleApprovalHandler> logger)
    {
        _logger = logger;
    }

    public async Task<bool> RequestApprovalAsync(string prompt, CancellationToken cancellationToken = default)
    {
        Console.WriteLine();
        Console.WriteLine(prompt);
        Console.Write("> ");

        var inputTask = Task.Run(() => Console.ReadLine()?.Trim() ?? string.Empty, cancellationToken);
        var input = await inputTask;

        var approved = IsApprovalResponse(input);

        _logger.LogInformation("Approval request: {Approved} (input: {Input})", approved, input);

        return approved;
    }

    /// <summary>
    /// Interprets natural language response as approval or denial.
    /// </summary>
    private static bool IsApprovalResponse(string input)
    {
        var normalized = input.Trim().ToLowerInvariant();

        // Empty input = denial (safe default)
        if (string.IsNullOrEmpty(normalized))
            return false;

        // Denial keywords (check first - explicit denial takes precedence)
        string[] denyKeywords = ["no", "cancel", "stop", "don't", "dont", "wait", "nevermind", "never mind", "nope", "nah"];
        if (denyKeywords.Any(k => normalized.Contains(k)))
            return false;

        // Approval keywords
        string[] approveKeywords = ["yes", "yeah", "sure", "ok", "okay", "proceed", "go ahead", "do it", "confirm", "yep", "yup", "please", "approved"];
        return approveKeywords.Any(k => normalized.Contains(k));
    }
}
```

**Step 2: Build to verify**

Run: `dotnet build`
Expected: Build succeeded

---

## Task 6: Add Approval Handling to ChatbotOrchestrator

**Files:**
- Modify: `src/Orchestration/ChatbotOrchestrator.cs`

**Step 1: Add IApprovalHandler dependency**

Add field and update constructor:

```csharp
private readonly IApprovalHandler _approvalHandler;

public ChatbotOrchestrator(
    ClassifierAgent classifierAgent,
    FAQAgent faqAgent,
    AuthAgent authAgent,
    UtilityDataAgent utilityDataAgent,
    SummarizationAgent summarizationAgent,
    NextBestActionAgent nextBestActionAgent,
    ISessionStore sessionStore,
    IApprovalHandler approvalHandler,
    ILogger<ChatbotOrchestrator> logger)
{
    _classifierAgent = classifierAgent;
    _faqAgent = faqAgent;
    _authAgent = authAgent;
    _utilityDataAgent = utilityDataAgent;
    _summarizationAgent = summarizationAgent;
    _nextBestActionAgent = nextBestActionAgent;
    _sessionStore = sessionStore;
    _approvalHandler = approvalHandler;
    _logger = logger;
}
```

**Step 2: Add using statements**

Add at top of file:

```csharp
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
```

**Step 3: Update GetAccountDataAsync to handle approvals**

Replace the `GetAccountDataAsync` method:

```csharp
private async Task<ChatResponse> GetAccountDataAsync(
    ChatSession session,
    string userMessage,
    CancellationToken cancellationToken)
{
    try
    {
        // Create or get the UtilityData session
        var utilitySession = session.UtilityDataSession;
        if (utilitySession is null)
        {
            utilitySession = await _utilityDataAgent.CreateSessionAsync(
                session.AuthSession!,
                cancellationToken);
            session.UtilityDataSession = utilitySession;
        }

        // Run the agent
        var response = await utilitySession.Agent.RunAsync<UtilityDataStructuredOutput>(
            message: userMessage,
            session: utilitySession.AgentSession);

        // Handle approval requests
        response = await HandleApprovalRequestsAsync(utilitySession, response, cancellationToken);

        var output = response.Result;

        // If the UtilityData agent can't answer, escalate to human
        if (output is null || !output.FoundAnswer)
        {
            _logger.LogInformation("UtilityData agent cannot answer (FoundAnswer=false), escalating to human");
            return await InitiateHumanHandoffAsync(
                session,
                userMessage,
                "Account question outside agent capabilities",
                cancellationToken);
        }

        return new ChatResponse
        {
            Message = output.Response,
            Category = QuestionCategory.AccountData,
            RequiredAction = RequiredAction.None
        };
    }
    catch (InvalidOperationException ex)
    {
        _logger.LogWarning(ex, "UtilityData agent error, may need re-authentication");
        // Session may have expired, need to re-auth
        session.UserContext.AuthState = AuthenticationState.Expired;
        session.AuthSession = null;
        session.UtilityDataSession = null;
        return await InitiateAuthenticationFlowAsync(session, userMessage, cancellationToken);
    }
}
```

**Step 4: Add HandleApprovalRequestsAsync method**

Add this new method after `GetAccountDataAsync`:

```csharp
private async Task<AgentResponse<UtilityDataStructuredOutput>> HandleApprovalRequestsAsync(
    UtilityDataSession utilitySession,
    AgentResponse<UtilityDataStructuredOutput> response,
    CancellationToken cancellationToken)
{
    var userInputRequests = response.UserInputRequests.ToList();

    while (userInputRequests.Count > 0)
    {
        var approvalMessages = new List<ChatMessage>();

        foreach (var request in userInputRequests.OfType<FunctionApprovalRequestContent>())
        {
            var prompt = FormatApprovalPrompt(request);
            var approved = await _approvalHandler.RequestApprovalAsync(prompt, cancellationToken);

            _logger.LogInformation("Payment approval: {Approved} for {Tool}",
                approved, request.FunctionCall.Name);

            approvalMessages.Add(new ChatMessage(ChatRole.User, [request.CreateResponse(approved)]));
        }

        if (approvalMessages.Count > 0)
        {
            response = await utilitySession.Agent.RunAsync<UtilityDataStructuredOutput>(
                approvalMessages,
                utilitySession.AgentSession);

            userInputRequests = response.UserInputRequests.ToList();
        }
        else
        {
            break;
        }
    }

    return response;
}

private static string FormatApprovalPrompt(FunctionApprovalRequestContent request)
{
    // Extract payment details from function arguments if available
    var args = request.FunctionCall.Arguments;

    if (args is not null &&
        args.TryGetProperty("amount", out var amountProp) &&
        args.TryGetProperty("billingPeriod", out var periodProp))
    {
        var amount = amountProp.GetDecimal();
        var period = periodProp.GetString();
        return $"I'm about to submit a payment of ${amount:F2} for {period}. Should I proceed?";
    }

    return $"I need your approval to proceed with {request.FunctionCall.Name}. Should I continue?";
}
```

**Step 5: Build to verify**

Run: `dotnet build`
Expected: Build succeeded

---

## Task 7: Add UtilityDataSession to ChatSession

**Files:**
- Modify: `src/Orchestration/ChatSession.cs`

**Step 1: Read current ChatSession.cs**

First, read the file to understand its structure.

**Step 2: Add UtilityDataSession property**

Add to `ChatSession` class:

```csharp
/// <summary>
/// The UtilityData session for account queries (requires authentication).
/// </summary>
public UtilityDataSession? UtilityDataSession { get; set; }
```

**Step 3: Add using statement**

Add at top:

```csharp
using UtilityBillingChatbot.Agents.UtilityData;
```

**Step 4: Build to verify**

Run: `dotnet build`
Expected: Build succeeded

---

## Task 8: Register Services

**Files:**
- Modify: `src/Infrastructure/ServiceCollectionExtensions.cs`

**Step 1: Register ConsoleApprovalHandler**

Add to the service registration:

```csharp
services.AddSingleton<IApprovalHandler, ConsoleApprovalHandler>();
```

**Step 2: Build to verify**

Run: `dotnet build`
Expected: Build succeeded

---

## Task 9: Update Instructions for Payment Capability

**Files:**
- Modify: `src/Agents/UtilityData/UtilityDataContextProvider.cs`

**Step 1: Update BuildInstructions method**

Update the instructions to mention payment capability:

```csharp
private string BuildInstructions()
{
    return $"""
        You are a utility billing customer service assistant helping {_customer.Name}
        with their account ({_customer.AccountNumber}) at {_customer.ServiceAddress}.

        You have access to tools to look up account information and make payments.
        Use them to answer the customer's questions accurately.

        GUIDELINES:
        - Be helpful and professional
        - Use the tools to get accurate information before answering
        - Format currency amounts clearly (e.g., $187.43)
        - When discussing dates, be specific (e.g., "February 15, 2024")
        - Keep responses concise but complete
        - For payments, use the MakePayment tool with the customer's current balance
        - Set foundAnswer to true if you can answer using your available tools
        - Set foundAnswer to false if the question is outside your capabilities
          (e.g., service changes, complaints, technical issues, rate changes)
        """;
}
```

**Step 2: Build to verify**

Run: `dotnet build`
Expected: Build succeeded

---

## Task 10: Test End-to-End

**Step 1: Run the application**

Run: `dotnet run`

**Step 2: Test the approval flow**

1. Authenticate first:
   ```
   > What's my balance?
   [Provide account number and last 4 of SSN when prompted]
   ```

2. Request payment:
   ```
   > Pay my bill
   ```

3. Expected: Agent prompts for approval with amount
   ```
   I'm about to submit a payment of $187.43 for February 2024. Should I proceed?
   > yes please
   ```

4. Expected: Payment confirmation message with confirmation number

**Step 3: Test denial flow**

```
> Pay my bill
I'm about to submit a payment of $187.43 for February 2024. Should I proceed?
> no, wait
```

Expected: Agent acknowledges cancellation

---

## Summary

| Task | Component | Action |
|------|-----------|--------|
| 1 | UtilityDataModels | Add `PaymentResult` record |
| 2 | UtilityDataContextProvider | Add `MakePayment` tool with `ApprovalRequiredAIFunction` |
| 3 | UtilityDataAgent | Pass logger factory for context provider logging |
| 4 | IApprovalHandler | Create interface for approval handling |
| 5 | ConsoleApprovalHandler | Implement console-based approval with NL interpretation |
| 6 | ChatbotOrchestrator | Add approval loop in `GetAccountDataAsync` |
| 7 | ChatSession | Add `UtilityDataSession` property |
| 8 | ServiceCollectionExtensions | Register approval handler |
| 9 | UtilityDataContextProvider | Update instructions for payment capability |
| 10 | Integration test | Verify end-to-end flow |
