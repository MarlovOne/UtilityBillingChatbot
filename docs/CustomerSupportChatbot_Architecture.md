# Utility Billing Customer Support Chatbot - Multi-Agent Architecture Document

## Overview

This document provides a staged implementation reference for a multi-agent customer support chatbot for **utility billing** using the Microsoft Agent Framework. The system routes customer questions through specialized agents, handles authentication for account-specific data (CIS/MDM), and provides seamless handoff to human representatives for complex requests.

**Domain**: Utility billing (electric, gas, water)
**Data Sources**: CIS (Customer Information System), MDM (Meter Data Management), InvoiceCloud

**Design Principles:**
- **Prototype-first**: Simple in-memory implementations with clean abstractions for production swap
- **Phone-compatible**: In-band authentication via conversation (no OAuth redirects)
- **Incrementally testable**: Each stage can be validated independently

---

## System Requirements

| Requirement | Solution |
|------------|----------|
| Answer billing FAQ questions | FAQ Agent with utility knowledge base |
| Account-specific queries | In-band auth + CIS/MDM mock data |
| Complex service requests | Human handoff with conversation summary |
| High-frequency questions | Optimized for top 20 utility billing questions |

---

## Architecture Overview

```
┌──────────────────────────────────────────────────────────────────────────────────────┐
│                                  CUSTOMER CHATBOT                                     │
├──────────────────────────────────────────────────────────────────────────────────────┤
│                                                                                       │
│  ┌──────────────┐                                                                    │
│  │    User      │                                                                    │
│  └──────┬───────┘                                                                    │
│         │                                                                             │
│         ▼                                                                             │
│  ┌───────────────────────────────────────────────────────────────────────────┐       │
│  │                         ORCHESTRATOR (Stage 5)                             │       │
│  │  - Routes conversations              - Manages session state               │       │
│  │  - Handles auth context              - Coordinates agent handoffs          │       │
│  └───────────────────────────────────────────────────────────────────────────┘       │
│         │                                                                             │
│         ▼                                                                             │
│  ┌───────────────────────────────────────────────────────────────────────────┐       │
│  │                       CLASSIFIER AGENT (Stage 1)                           │       │
│  │  - Analyzes user intent    - Categorizes questions    - Structured output  │       │
│  └───────────────────────────────────────────────────────────────────────────┘       │
│         │                                                                             │
│         ├────────────────┬────────────────┬────────────────┬────────────────┐        │
│         ▼                ▼                ▼                ▼                ▼        │
│  ┌────────────┐   ┌────────────┐   ┌────────────┐   ┌────────────┐   ┌────────────┐ │
│  │ FAQ Agent  │   │ In-Band    │   │  Utility   │   │Summartic'n │   │  Human     │ │
│  │ (Stage 2)  │   │ Auth Agent │   │ Data Agent │   │   Agent    │   │  Handoff   │ │
│  │            │   │ (Stage 3)  │   │ (Stage 4)  │   │ (Stage 6)  │   │ (Stage 6)  │ │
│  └────────────┘   └─────┬──────┘   └─────┬──────┘   └────────────┘   └────────────┘ │
│         │               │                │                │                │         │
│         │               │                ▼                │                │         │
│         │               │         ┌────────────┐          │                │         │
│         │               │         │  API/DB    │          │                │         │
│         │               │         │   Tools    │          │                │         │
│         │               │         └────────────┘          │                │         │
│         │               │                │                │                │         │
│         │               ▼                │                │                │         │
│         │        ┌────────────┐          │                │                │         │
│         │        │ Mock CIS   │          │                │                │         │
│         │        │ Database   │          │                │                │         │
│         │        └────────────┘          │                │                │         │
│         │                                │                │                │         │
│         └────────────────┴───────────────┴────────────────┴────────────────┘         │
│                                          │                                            │
│                                          ▼                                            │
│                                  ┌──────────────┐                                     │
│                                  │   Response   │                                     │
│                                  └──────────────┘                                     │
│                                                                                       │
│  Persistence (Stage 7): Session state saved to ISessionStore (Redis/SQL)             │
│                                                                                       │
└──────────────────────────────────────────────────────────────────────────────────────┘
```

---

## Data Flow

```
User Message
    │
    ▼
┌─────────────────────────────────────────────────────────────────┐
│ Session Management                                               │
│ - Create/restore AgentSession                                    │
│ - Load auth context via AIContextProvider                        │
│ - Track conversation history                                     │
└─────────────────────────────────────────────────────────────────┘
    │
    ▼
┌─────────────────────────────────────────────────────────────────┐
│ Classifier Agent                                                 │
│ Input: User message + conversation context                       │
│ Output: Classification { Category, RequiresAuth, Confidence }    │
└─────────────────────────────────────────────────────────────────┘
    │
    ├──► Category: "BillingFAQ" ───────────► FAQ Agent ──► Response
    │
    ├──► Category: "AccountData"           ┌──────────────────────┐
    │    RequiresAuth: true  ─────────────►│ Check Authentication │
    │                                       └──────────┬───────────┘
    │                                                  │
    │                        ┌─────────────────────────┼─────────────────────┐
    │                        │                         │                     │
    │                        ▼                         ▼                     │
    │                   Authenticated            Not Authenticated           │
    │                        │                         │                     │
    │                        ▼                         ▼                     │
    │               UtilityDataAgent           Verify Identity               │
    │                        │                         │                     │
    │                        ▼                         │                     │
    │               API/DB Tool Execution              │                     │
    │                        │                         │                     │
    │                        ▼                         │                     │
    │                    Response ◄────────────────────┘                     │
    │
    ├──► Category: "ServiceRequest"        ┌──────────────────────┐
    │    or "OutOfScope"   ───────────────►│ Summarization Agent  │
    │                                       └──────────┬───────────┘
    │                                                  │
    │                                                  ▼
    │                                       ┌──────────────────────┐
    │                                       │ Human Handoff Queue  │
    │                                       │ - Conversation summary│
    │                                       │ - User context        │
    │                                       │ - Wait for response   │
    │                                       └──────────────────────┘
    │
    └──► Category: "HumanRequested" ───────────────────► Human Handoff
```

---

## Core Components

### 1. Classification Schema

```csharp
/// <summary>
/// Structured output from the Classifier Agent for utility billing questions
/// </summary>
public class QuestionClassification
{
    /// <summary>
    /// Category of the user's question
    /// </summary>
    [JsonPropertyName("category")]
    public QuestionCategory Category { get; set; }

    /// <summary>
    /// Confidence score (0.0 - 1.0)
    /// </summary>
    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }

    /// <summary>
    /// Whether the question requires user authentication
    /// </summary>
    [JsonPropertyName("requiresAuth")]
    public bool RequiresAuth { get; set; }

    /// <summary>
    /// Specific question type from top 20 (for analytics)
    /// </summary>
    [JsonPropertyName("questionType")]
    public string? QuestionType { get; set; }

    /// <summary>
    /// Brief explanation of the classification decision
    /// </summary>
    [JsonPropertyName("reasoning")]
    public string Reasoning { get; set; } = string.Empty;
}

public enum QuestionCategory
{
    /// <summary>General billing questions answerable from FAQ/knowledge base
    /// Examples: "How can I pay my bill?", "What assistance programs are available?"</summary>
    BillingFAQ,

    /// <summary>Questions requiring customer's account data from CIS/MDM
    /// Examples: "Why is my bill so high?", "What's my balance?", "Did you receive my payment?"</summary>
    AccountData,

    /// <summary>Complex requests that may require CSR action or policy decisions
    /// Examples: "Can I get a payment extension?", "Check my meter", "Change my rate plan"</summary>
    ServiceRequest,

    /// <summary>Questions outside utility billing scope</summary>
    OutOfScope,

    /// <summary>User explicitly requests human assistance</summary>
    HumanRequested
}
```

### 2. Session Context Model (In-Band Auth)

```csharp
/// <summary>
/// User context maintained across the conversation.
/// Supports in-band (conversational) authentication for phone/chat compatibility.
/// </summary>
public class UserSessionContext
{
    /// <summary>Unique session identifier</summary>
    public string SessionId { get; set; } = Guid.NewGuid().ToString();

    /// <summary>User's display name (collected during auth)</summary>
    public string? UserName { get; set; }

    /// <summary>User's unique identifier (after successful auth)</summary>
    public string? UserId { get; set; }

    /// <summary>Current authentication state</summary>
    public AuthenticationState AuthState { get; set; } = AuthenticationState.Anonymous;

    /// <summary>Phone number or email used for identification</summary>
    public string? IdentifyingInfo { get; set; }

    /// <summary>Number of failed verification attempts (max 3)</summary>
    public int FailedAttempts { get; set; }

    /// <summary>What verification questions have been answered correctly</summary>
    public List<string> VerifiedFactors { get; set; } = [];

    /// <summary>The original query that triggered auth (to resume after)</summary>
    public string? PendingQuery { get; set; }

    /// <summary>When authentication was completed</summary>
    public DateTimeOffset? AuthenticatedAt { get; set; }

    /// <summary>Session timeout (re-auth required after this)</summary>
    public DateTimeOffset? SessionExpiry { get; set; }

    /// <summary>Timestamp of last user interaction</summary>
    public DateTimeOffset LastInteraction { get; set; } = DateTimeOffset.UtcNow;
}

public enum AuthenticationState
{
    /// <summary>User has not started authentication</summary>
    Anonymous,

    /// <summary>Authentication flow in progress (awaiting user input)</summary>
    InProgress,

    /// <summary>User provided identifier, awaiting verification questions</summary>
    IdentityProvided,

    /// <summary>User is answering verification questions</summary>
    Verifying,

    /// <summary>User successfully verified</summary>
    Authenticated,

    /// <summary>Too many failed attempts - locked out</summary>
    LockedOut,

    /// <summary>Session expired - needs re-auth</summary>
    Expired
}
```

### 3. Handoff Request Model

```csharp
/// <summary>
/// Data package sent to human representatives
/// </summary>
public class HumanHandoffRequest
{
    /// <summary>Unique handoff ticket ID</summary>
    public string TicketId { get; set; } = Guid.NewGuid().ToString();

    /// <summary>AI-generated summary of the conversation</summary>
    public string ConversationSummary { get; set; } = string.Empty;

    /// <summary>User's original question that triggered handoff</summary>
    public string OriginalQuestion { get; set; } = string.Empty;

    /// <summary>Why the system is escalating</summary>
    public string EscalationReason { get; set; } = string.Empty;

    /// <summary>User context and authentication state</summary>
    public UserSessionContext UserContext { get; set; } = new();

    /// <summary>Full conversation history</summary>
    public List<ConversationMessage> ConversationHistory { get; set; } = [];

    /// <summary>Suggested category for human routing</summary>
    public string? SuggestedDepartment { get; set; }

    /// <summary>Timestamp of handoff request</summary>
    public DateTimeOffset RequestedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class ConversationMessage
{
    public string Role { get; set; } = string.Empty;  // "user" or "assistant"
    public string Content { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; }
}
```

### 4. Service Abstractions (Loose Coupling)

To enable testability and flexible implementations, all agent factories and services are defined as interfaces:

```csharp
/// <summary>
/// Creates classifier agents for question categorization
/// </summary>
public interface IClassifierAgentFactory
{
    AIAgent CreateClassifierAgent();
}

/// <summary>
/// Creates FAQ agents for answering billing knowledge base questions
/// </summary>
public interface IFAQAgentFactory
{
    AIAgent CreateFAQAgent();
}

/// <summary>
/// Creates in-band authentication agents for identity verification.
/// Context is required so the agent's tools can modify authentication state.
/// </summary>
public interface IInBandAuthAgentFactory
{
    AIAgent CreateInBandAuthAgent(UserSessionContext context);
}

/// <summary>
/// Creates utility data agents for authenticated account queries
/// </summary>
public interface IUtilityDataAgentFactory
{
    AIAgent CreateUtilityDataAgent(UserSessionContext context);
}

/// <summary>
/// Creates summarization agents for handoff preparation
/// </summary>
public interface ISummarizationAgentFactory
{
    AIAgent CreateSummarizationAgent();
}

/// <summary>
/// Manages session persistence and retrieval
/// </summary>
public interface ISessionStore
{
    Task<ChatSession?> GetSessionAsync(string sessionId, CancellationToken ct = default);
    Task SaveSessionAsync(ChatSession session, CancellationToken ct = default);
    Task DeleteSessionAsync(string sessionId, CancellationToken ct = default);
    Task<IEnumerable<string>> GetActiveSessionIdsAsync(CancellationToken ct = default);
}

/// <summary>
/// Manages human handoff queue and notifications
/// </summary>
public interface IHandoffService
{
    Task<string> CreateHandoffTicketAsync(HumanHandoffRequest request, CancellationToken ct = default);
    Task<HumanResponse?> WaitForHumanResponseAsync(string ticketId, TimeSpan timeout, CancellationToken ct = default);
    Task NotifyCustomerAsync(string sessionId, string message, CancellationToken ct = default);
}
```

**Benefits of Interface-Based Design:**
- **Testability**: Mock any dependency in unit tests
- **Flexibility**: Swap implementations without changing orchestrator
- **Configuration**: Use different agents for different environments (dev/prod)
- **Composition**: Wrap factories with decorators (caching, logging, metrics)

---

## Stage 1: Classifier Agent

### Objective
Build and test a standalone classifier agent that analyzes user questions and outputs structured classifications.

### Implementation

```csharp
using Microsoft.Extensions.AI;

public class ClassifierAgentFactory : IClassifierAgentFactory
{
    private readonly IChatClient _chatClient;

    public ClassifierAgentFactory(IChatClient chatClient)
    {
        _chatClient = chatClient;
    }

    public AIAgent CreateClassifierAgent()
    {
        const string instructions = """
            You are a utility billing customer support classifier. Analyze the customer's
            message and classify it into one of these categories:

            ## BillingFAQ (No auth required)
            General questions answerable from knowledge base:
            - "How can I pay my bill?" → Payment options
            - "What assistance programs are available?" → LIHEAP, utility programs
            - "Why does my due date change?" → Billing cycle explanation
            - "How can I reduce my bill?" → Energy saving tips
            - "What is a demand charge?" → Rate/tariff explanations

            ## AccountData (Authentication required)
            Questions requiring customer's specific account data from CIS/MDM:
            - "Why is my bill so high?" → Needs usage data, billing history
            - "What's my current balance?" / "How much do I owe?" → Needs CIS balance
            - "Did you receive my payment?" → Needs payment status
            - "When is my payment due?" → Needs due date
            - "What is this charge on my bill?" → Needs bill line items
            - "Is my bill based on actual or estimated read?" → Needs meter read type
            - "Can I get a copy of my bill?" → Needs bill history
            - "Am I on AutoPay?" → Needs AutoPay status
            IMPORTANT: These ALWAYS require authentication

            ## ServiceRequest (May need human handoff)
            Complex requests that may require CSR action:
            - "Can I get a payment extension?" → High complexity, policy decision
            - "Set up a payment arrangement" → Requires CIS write access
            - "Enroll me in budget billing" → Enrollment workflow
            - "Sign me up for AutoPay" → Enrollment workflow
            - "I think my meter is wrong, can you check it?" → Field service dispatch
            - "Am I on the best rate plan?" → Rate comparison, eligibility check
            - "Update my address" → Identity verification + CIS update

            ## OutOfScope
            Questions not related to utility billing

            ## HumanRequested
            Customer explicitly asks for a human representative

            Provide your confidence level (0.0-1.0), the specific question type if
            it matches a known category, and brief reasoning.
            If confidence is below 0.6, classify as OutOfScope.
            """;

        return _chatClient.AsAIAgent(instructions: instructions);
    }
}
```

### Testing Stage 1

```csharp
public class ClassifierAgentTests
{
    [Fact]
    public async Task Classifier_CategorizesBillingFAQ_Correctly()
    {
        // Arrange
        var classifier = _factory.CreateClassifierAgent();
        var session = await classifier.CreateSessionAsync();

        // Act - Q5: "How can I pay my bill?"
        var response = await classifier.RunAsync<QuestionClassification>(
            "How can I pay my bill?",
            session);

        // Assert
        Assert.Equal(QuestionCategory.BillingFAQ, response.Result.Category);
        Assert.False(response.Result.RequiresAuth);
        Assert.True(response.Result.Confidence >= 0.6);
    }

    [Fact]
    public async Task Classifier_RequiresAuth_ForAccountBalance()
    {
        // Arrange
        var classifier = _factory.CreateClassifierAgent();
        var session = await classifier.CreateSessionAsync();

        // Act - Q2: "What is my current account balance?"
        var response = await classifier.RunAsync<QuestionClassification>(
            "What is my current account balance?",
            session);

        // Assert
        Assert.Equal(QuestionCategory.AccountData, response.Result.Category);
        Assert.True(response.Result.RequiresAuth);
    }

    [Fact]
    public async Task Classifier_RequiresAuth_ForHighBillQuestion()
    {
        // Arrange
        var classifier = _factory.CreateClassifierAgent();
        var session = await classifier.CreateSessionAsync();

        // Act - Q1: "Why is my bill so high?" (highest frequency question)
        var response = await classifier.RunAsync<QuestionClassification>(
            "Why is my bill so high this month?",
            session);

        // Assert
        Assert.Equal(QuestionCategory.AccountData, response.Result.Category);
        Assert.True(response.Result.RequiresAuth);
        Assert.Equal("HighBillInquiry", response.Result.QuestionType);
    }

    [Fact]
    public async Task Classifier_IdentifiesServiceRequest_ForPaymentExtension()
    {
        // Arrange
        var classifier = _factory.CreateClassifierAgent();
        var session = await classifier.CreateSessionAsync();

        // Act - Q6: "Can I get an extension or set up a payment arrangement?"
        var response = await classifier.RunAsync<QuestionClassification>(
            "Can I get an extension on my payment?",
            session);

        // Assert
        Assert.Equal(QuestionCategory.ServiceRequest, response.Result.Category);
    }

    [Fact]
    public async Task Classifier_IdentifiesServiceRequest_ForMeterCheck()
    {
        // Arrange
        var classifier = _factory.CreateClassifierAgent();
        var session = await classifier.CreateSessionAsync();

        // Act - Q14: "I think my bill is wrong – can someone check my meter?"
        var response = await classifier.RunAsync<QuestionClassification>(
            "I think my meter is broken, can someone come check it?",
            session);

        // Assert
        Assert.Equal(QuestionCategory.ServiceRequest, response.Result.Category);
    }

    [Fact]
    public async Task Classifier_DetectsHumanRequest()
    {
        // Arrange
        var classifier = _factory.CreateClassifierAgent();
        var session = await classifier.CreateSessionAsync();

        // Act
        var response = await classifier.RunAsync<QuestionClassification>(
            "I need to speak with a representative",
            session);

        // Assert
        Assert.Equal(QuestionCategory.HumanRequested, response.Result.Category);
    }

    [Fact]
    public async Task Classifier_HandlesOutOfScope()
    {
        // Arrange
        var classifier = _factory.CreateClassifierAgent();
        var session = await classifier.CreateSessionAsync();

        // Act
        var response = await classifier.RunAsync<QuestionClassification>(
            "What's the weather going to be tomorrow?",
            session);

        // Assert
        Assert.Equal(QuestionCategory.OutOfScope, response.Result.Category);
    }
}
```

### Validation Checklist - Stage 1
- [ ] Classifier correctly identifies BillingFAQ questions (payment options, programs)
- [ ] Classifier correctly identifies AccountData questions (balance, payment status, bill details)
- [ ] Classifier sets RequiresAuth=true for all AccountData questions
- [ ] Classifier correctly identifies ServiceRequest questions (extensions, meter checks)
- [ ] Classifier correctly identifies HumanRequested
- [ ] Classifier correctly identifies OutOfScope questions
- [ ] QuestionType is populated for known question patterns
- [ ] Multi-turn context is maintained in session

---

## Stage 2: FAQ Agent

### Objective
Build an FAQ agent with access to the utility billing knowledge base that answers general billing questions without requiring authentication.

### Implementation

```csharp
public class FAQAgentFactory : IFAQAgentFactory
{
    private readonly IChatClient _chatClient;
    private readonly string _knowledgeBase;

    public FAQAgentFactory(IChatClient chatClient, string knowledgeBasePath)
    {
        _chatClient = chatClient;
        _knowledgeBase = File.ReadAllText(knowledgeBasePath);
    }

    public AIAgent CreateFAQAgent()
    {
        string instructions = $"""
            You are a utility billing customer support assistant. Answer questions
            based ONLY on the following knowledge base. If the answer is not in the
            knowledge base, say "I don't have information about that specific topic."

            Be concise and helpful. If a question is partially covered, answer what
            you can and mention what's not covered.

            KNOWLEDGE BASE:
            {_knowledgeBase}

            IMPORTANT RULES:
            1. Never make up information not in the knowledge base
            2. If asked about their specific account (balance, usage, payments),
               explain you'll need to verify their identity first to access that
            3. Keep responses under 200 words unless more detail is requested
            4. For questions about payment arrangements or extensions, explain the
               general policy but note that specific eligibility requires account access
            """;

        return _chatClient.AsAIAgent(instructions: instructions);
    }
}
```

### Knowledge Base - Utility Billing FAQ

```markdown
# Utility Billing FAQ Knowledge Base

## Payment Options (Q5: "How can I pay my bill?")
You can pay your utility bill through several convenient methods:

**Online Payments**
- Customer portal at myaccount.utilitycompany.com
- Pay by credit/debit card (Visa, MasterCard, Discover, AMEX)
- Pay by bank account (ACH) - no fee
- Credit card payments may incur a $2.50 convenience fee

**Phone Payments**
- Call our automated payment line: 1-800-555-UTIL
- Available 24/7
- Have your account number and payment information ready

**In-Person**
- Visit any authorized payment center
- Pay at participating grocery stores and pharmacies
- Cash, check, or money order accepted

**Mail**
- Send check or money order to the address on your bill
- Allow 7-10 business days for processing
- Include your account number on the check

**AutoPay**
- Automatic monthly deduction from bank account or credit card
- Payments processed on your due date
- Enroll online or call customer service

## Assistance Programs (Q7: "What assistance programs can help me pay my bill?")
Several programs may help if you're having difficulty paying:

**LIHEAP (Low Income Home Energy Assistance Program)**
- Federal program for income-qualified households
- Helps with heating and cooling costs
- Apply through your local community action agency
- Typical benefit: $300-$500 per year

**Utility Hardship Programs**
- Company-sponsored bill assistance
- One-time grants for qualifying customers
- Income verification required
- Apply by calling customer service

**Medical Baseline Allowance**
- Discounted rates for customers with medical equipment
- Requires doctor certification
- Covers life-support equipment, dialysis, etc.

**Senior/Disability Discounts**
- Available in some service areas
- Age 65+ or documented disability
- Discount typically 10-15% off bill

## Billing Cycle Explanation (Q11: "Why does my due date change?")
Your due date may vary slightly each month because:
- Billing cycles are typically 28-32 days
- Meter reading schedules depend on route logistics
- Weekends and holidays can shift reading dates
- Your due date is always approximately 21 days after the bill date

This is normal and doesn't affect your average monthly charges.

## Energy Saving Tips (Q18: "How can I reduce my bill?")
**Heating & Cooling (50% of typical bill)**
- Set thermostat to 68°F in winter, 78°F in summer
- Use programmable/smart thermostat
- Change air filters monthly
- Seal windows and doors

**Water Heating (15% of typical bill)**
- Set water heater to 120°F
- Take shorter showers
- Fix leaky faucets
- Insulate hot water pipes

**Appliances & Electronics**
- Use ENERGY STAR appliances
- Unplug devices when not in use
- Run dishwasher/laundry with full loads
- Use cold water for laundry

**Lighting**
- Switch to LED bulbs
- Use natural light when possible
- Turn off lights when leaving room

**Free Programs**
- Request a free home energy audit
- Check for rebates on efficient appliances
- Ask about time-of-use rate plans

## Demand Charges (Q17: "What is a demand charge?")
Demand charges apply primarily to commercial/industrial customers:

**What It Measures**
- Your highest rate of electricity use (kW) during the billing period
- Usually measured in 15-minute intervals
- Reflects the infrastructure needed to serve your peak usage

**Why It Exists**
- The utility must have capacity ready for your highest usage
- Peaks often occur during hot afternoons (AC) or morning startups
- Infrastructure costs are recovered through demand charges

**How to Reduce It**
- Stagger startup of large equipment
- Avoid running multiple high-draw devices simultaneously
- Consider load management systems
- Shift flexible loads to off-peak hours

## Estimated vs. Actual Reads (Q13)
Your meter is typically read monthly by a meter reader or smart meter.

**Actual Read (Code: A)**
- Physical or electronic reading of your meter
- Most accurate billing method

**Estimated Read (Code: E)**
- Used when meter cannot be read (access issues, weather, etc.)
- Based on your historical usage patterns
- Corrected on next actual read

**Smart Meters**
- Transmit readings automatically
- Rarely require estimation
- Enable time-of-use rates and usage tracking

## Alternate Suppliers (Q16: "Why am I charged by a different supplier?")
In deregulated markets, you may choose your energy supplier:

**How It Works**
- Your utility delivers energy (distribution charges)
- A separate supplier provides the energy itself (supply charges)
- Both charges appear on your utility bill

**If You Have an Alternate Supplier**
- You signed up with a competitive supplier (ESCO)
- Supply charges are set by that company, not the utility
- To switch back, contact the supplier or call us

**Questions About Your Supplier**
- Supplier name appears on your bill
- Contact them directly about supply rates
- Utility cannot modify supplier charges
```

### Testing Stage 2

```csharp
public class FAQAgentTests
{
    [Fact]
    public async Task FAQAgent_AnswersPaymentOptions()
    {
        // Arrange - Q5: "How can I pay my bill?"
        var faqAgent = _factory.CreateFAQAgent();
        var session = await faqAgent.CreateSessionAsync();

        // Act
        var response = await faqAgent.RunAsync(
            "How can I pay my bill?",
            session);

        // Assert
        Assert.Contains("online", response.Text.ToLower());
        Assert.Contains("phone", response.Text.ToLower());
    }

    [Fact]
    public async Task FAQAgent_AnswersAssistancePrograms()
    {
        // Arrange - Q7: "What assistance programs can help me?"
        var faqAgent = _factory.CreateFAQAgent();
        var session = await faqAgent.CreateSessionAsync();

        // Act
        var response = await faqAgent.RunAsync(
            "What assistance programs are available to help pay my bill?",
            session);

        // Assert
        Assert.Contains("LIHEAP", response.Text);
    }

    [Fact]
    public async Task FAQAgent_AnswersEnergySavingTips()
    {
        // Arrange - Q18: "How can I reduce my bill?"
        var faqAgent = _factory.CreateFAQAgent();
        var session = await faqAgent.CreateSessionAsync();

        // Act
        var response = await faqAgent.RunAsync(
            "How can I lower my electric bill?",
            session);

        // Assert
        Assert.Contains("thermostat", response.Text.ToLower());
    }

    [Fact]
    public async Task FAQAgent_RedirectsBalanceQuestion()
    {
        // Arrange - Q2: Account-specific question
        var faqAgent = _factory.CreateFAQAgent();
        var session = await faqAgent.CreateSessionAsync();

        // Act
        var response = await faqAgent.RunAsync(
            "What's my current balance?",
            session);

        // Assert - Should redirect to account verification
        Assert.Contains("verify", response.Text.ToLower());
    }

    [Fact]
    public async Task FAQAgent_ExplainsDemandCharges()
    {
        // Arrange - Q17: "What is a demand charge?"
        var faqAgent = _factory.CreateFAQAgent();
        var session = await faqAgent.CreateSessionAsync();

        // Act
        var response = await faqAgent.RunAsync(
            "What is a demand charge on my bill?",
            session);

        // Assert
        Assert.Contains("peak", response.Text.ToLower());
        Assert.Contains("kW", response.Text);
    }

    [Fact]
    public async Task FAQAgent_MaintainsContext()
    {
        // Arrange
        var faqAgent = _factory.CreateFAQAgent();
        var session = await faqAgent.CreateSessionAsync();

        // Act
        await faqAgent.RunAsync("Tell me about AutoPay", session);
        var followUp = await faqAgent.RunAsync(
            "How do I sign up for that?",
            session);

        // Assert
        Assert.Contains("enroll", followUp.Text.ToLower());
    }
}
```

### Validation Checklist - Stage 2
- [ ] FAQ agent answers payment option questions (Q5)
- [ ] FAQ agent answers assistance program questions (Q7)
- [ ] FAQ agent answers energy saving questions (Q18)
- [ ] FAQ agent explains demand charges (Q17)
- [ ] FAQ agent explains estimated vs actual reads (Q13)
- [ ] FAQ agent redirects account-specific questions to verification
- [ ] Multi-turn conversation context is preserved
- [ ] Knowledge base can be updated without code changes

---

## Stage 3: In-Band Authentication Agent

### Objective
Build a conversational authentication agent that verifies user identity through security questions. This approach works for both chat and phone conversations (no OAuth redirects needed).

### Authentication Flow

```
User requests account data
         │
         ▼
┌─────────────────────────────────┐
│ "To access your account, I'll  │
│  need to verify your identity." │
└─────────────────────────────────┘
         │
         ▼
┌─────────────────────────────────┐
│ "What's the phone number or    │
│  email on your account?"        │
└─────────────────────────────────┘
         │
         ▼
┌─────────────────────────────────┐
│ Lookup user in mock database   │
│ Get verification questions      │
└─────────────────────────────────┘
         │
         ▼
┌─────────────────────────────────┐
│ "For security, what are the    │
│  last 4 digits of your SSN?"    │──► Wrong answer (max 3 tries)
│  OR "What's your date of birth?"│         │
└─────────────────────────────────┘         ▼
         │                          ┌───────────────────┐
         │ Correct                  │ Lock out / Handoff│
         ▼                          └───────────────────┘
┌─────────────────────────────────┐
│ AuthState = Authenticated       │
│ Resume original query           │
└─────────────────────────────────┘
```

### Implementation

```csharp
/// <summary>
/// Mock CIS (Customer Information System) database for utility billing prototyping
/// </summary>
public class MockCISDatabase
{
    private readonly Dictionary<string, UtilityCustomer> _customersByPhone = new()
    {
        ["555-1234"] = new UtilityCustomer
        {
            AccountNumber = "1234567890",
            Name = "John Smith",
            Phone = "555-1234",
            Email = "john.smith@example.com",
            ServiceAddress = "123 Main St, Anytown, ST 12345",
            LastFourSSN = "1234",
            DateOfBirth = new DateOnly(1985, 3, 15),
            AccountBalance = 187.43m,
            DueDate = DateOnly.FromDateTime(DateTime.Now.AddDays(12)),
            LastPaymentAmount = 142.50m,
            LastPaymentDate = DateOnly.FromDateTime(DateTime.Now.AddDays(-18)),
            IsOnAutoPay = false,
            RateCode = "R1",  // Residential Standard
            MeterNumber = "MTR-00123456",
            BillingHistory = [
                new BillRecord("2024-01", 892, 142.50m, "A", DateOnly.FromDateTime(DateTime.Now.AddDays(-48))),
                new BillRecord("2024-02", 1247, 187.43m, "A", DateOnly.FromDateTime(DateTime.Now.AddDays(-18)))
            ],
            UsageHistory = [
                new UsageRecord("2024-01", 892, 28.8m),  // ~29 kWh/day
                new UsageRecord("2024-02", 1247, 44.5m)  // ~45 kWh/day (winter spike)
            ],
            DelinquencyStatus = "Current",
            EligibleForExtension = true
        },
        ["555-5678"] = new UtilityCustomer
        {
            AccountNumber = "9876543210",
            Name = "Maria Garcia",
            Phone = "555-5678",
            Email = "maria.garcia@example.com",
            ServiceAddress = "456 Oak Ave, Anytown, ST 12345",
            LastFourSSN = "5678",
            DateOfBirth = new DateOnly(1990, 7, 22),
            AccountBalance = 0.00m,
            DueDate = DateOnly.FromDateTime(DateTime.Now.AddDays(5)),
            LastPaymentAmount = 98.50m,
            LastPaymentDate = DateOnly.FromDateTime(DateTime.Now.AddDays(-3)),
            IsOnAutoPay = true,
            RateCode = "R1",
            MeterNumber = "MTR-00789012",
            BillingHistory = [
                new BillRecord("2024-01", 654, 98.50m, "A", DateOnly.FromDateTime(DateTime.Now.AddDays(-33))),
                new BillRecord("2024-02", 687, 102.30m, "A", DateOnly.FromDateTime(DateTime.Now.AddDays(-3)))
            ],
            UsageHistory = [
                new UsageRecord("2024-01", 654, 23.4m),
                new UsageRecord("2024-02", 687, 24.5m)
            ],
            DelinquencyStatus = "Current",
            EligibleForExtension = false  // Already current
        },
        ["555-9999"] = new UtilityCustomer
        {
            AccountNumber = "5555555555",
            Name = "Robert Johnson",
            Phone = "555-9999",
            Email = "rjohnson@example.com",
            ServiceAddress = "789 Elm St, Anytown, ST 12345",
            LastFourSSN = "9999",
            DateOfBirth = new DateOnly(1972, 11, 8),
            AccountBalance = 423.67m,
            DueDate = DateOnly.FromDateTime(DateTime.Now.AddDays(-5)),  // Past due!
            LastPaymentAmount = 150.00m,
            LastPaymentDate = DateOnly.FromDateTime(DateTime.Now.AddDays(-45)),
            IsOnAutoPay = false,
            RateCode = "R1",
            MeterNumber = "MTR-00345678",
            BillingHistory = [
                new BillRecord("2024-01", 1456, 218.40m, "E", DateOnly.FromDateTime(DateTime.Now.AddDays(-60))),
                new BillRecord("2024-02", 1523, 228.45m, "A", DateOnly.FromDateTime(DateTime.Now.AddDays(-30)))
            ],
            UsageHistory = [
                new UsageRecord("2024-01", 1456, 52.0m),  // High usage
                new UsageRecord("2024-02", 1523, 54.4m)
            ],
            DelinquencyStatus = "PastDue",
            EligibleForExtension = true
        }
    };

    private readonly Dictionary<string, UtilityCustomer> _customersByEmail;
    private readonly Dictionary<string, UtilityCustomer> _customersByAccount;

    public MockCISDatabase()
    {
        _customersByEmail = _customersByPhone.Values.ToDictionary(u => u.Email.ToLower(), u => u);
        _customersByAccount = _customersByPhone.Values.ToDictionary(u => u.AccountNumber, u => u);
    }

    public UtilityCustomer? FindByIdentifier(string identifier)
    {
        identifier = identifier.Trim();

        // Try phone first
        if (_customersByPhone.TryGetValue(identifier, out var byPhone))
            return byPhone;

        // Try email
        if (_customersByEmail.TryGetValue(identifier.ToLower(), out var byEmail))
            return byEmail;

        // Try account number
        if (_customersByAccount.TryGetValue(identifier, out var byAccount))
            return byAccount;

        return null;
    }
}

public record UtilityCustomer
{
    public required string AccountNumber { get; init; }
    public required string Name { get; init; }
    public required string Phone { get; init; }
    public required string Email { get; init; }
    public required string ServiceAddress { get; init; }
    public required string LastFourSSN { get; init; }
    public required DateOnly DateOfBirth { get; init; }
    public required decimal AccountBalance { get; init; }
    public required DateOnly DueDate { get; init; }
    public required decimal LastPaymentAmount { get; init; }
    public required DateOnly LastPaymentDate { get; init; }
    public required bool IsOnAutoPay { get; init; }
    public required string RateCode { get; init; }
    public required string MeterNumber { get; init; }
    public required List<BillRecord> BillingHistory { get; init; }
    public required List<UsageRecord> UsageHistory { get; init; }
    public required string DelinquencyStatus { get; init; }  // Current, PastDue, Collections
    public required bool EligibleForExtension { get; init; }
}

/// <summary>Bill record from CIS</summary>
public record BillRecord(
    string BillingPeriod,
    int KwhUsage,
    decimal AmountDue,
    string ReadType,  // A=Actual, E=Estimated
    DateOnly BillDate);

/// <summary>Usage record from MDM</summary>
public record UsageRecord(
    string Period,
    int TotalKwh,
    decimal AvgDailyKwh);
```

```csharp
/// <summary>
/// Authentication agent that verifies utility customer identity through conversation.
/// Implements IInBandAuthAgentFactory for DI and testability.
/// </summary>
public class InBandAuthAgentFactory : IInBandAuthAgentFactory
{
    private readonly IChatClient _chatClient;
    private readonly MockCISDatabase _cisDatabase;

    public InBandAuthAgentFactory(IChatClient chatClient, MockCISDatabase cisDatabase)
    {
        _chatClient = chatClient;
        _cisDatabase = cisDatabase;
    }

    /// <summary>
    /// Creates an auth agent with tools that can modify the provided UserSessionContext.
    /// </summary>
    public AIAgent CreateInBandAuthAgent(UserSessionContext context)
    {
        // Tools for the auth flow
        var tools = new AIFunction[]
        {
            AIFunctionFactory.Create(
                (string identifier) => LookupCustomer(identifier, context),
                name: "LookupCustomerByIdentifier",
                description: "Look up a customer by phone number, email, or account number"),

            AIFunctionFactory.Create(
                (string answer) => VerifyLastFourSSN(answer, context),
                name: "VerifyLastFourSSN",
                description: "Verify the last 4 digits of customer's SSN"),

            AIFunctionFactory.Create(
                (string answer) => VerifyDateOfBirth(answer, context),
                name: "VerifyDateOfBirth",
                description: "Verify customer's date of birth (format: MM/DD/YYYY)"),

            AIFunctionFactory.Create(
                () => CompleteAuthentication(context),
                name: "CompleteAuthentication",
                description: "Mark customer as authenticated after successful verification")
        };

        string instructions = $"""
            You are a utility company security verification assistant. Your job is to verify
            the customer's identity before they can access their account information.

            Current auth state: {context.AuthState}
            Failed attempts: {context.FailedAttempts}/3
            Verified factors: {string.Join(", ", context.VerifiedFactors)}

            VERIFICATION FLOW:
            1. If Anonymous: Ask for phone number, email, or account number on their utility account
            2. If IdentityProvided: Use LookupCustomerByIdentifier to find them in CIS
            3. If Verifying: Ask ONE security question and verify the answer
               - Ask for last 4 digits of SSN OR date of birth
               - Use the appropriate verify tool
            4. If verified (1 correct answer): Call CompleteAuthentication

            RULES:
            - Be polite and professional
            - Never reveal what the correct answer should be
            - If they fail 3 times, say you need to transfer them to a customer service representative
            - After successful auth, tell them they're verified and you'll help with their billing question
            - Keep responses concise

            IMPORTANT: Only ask ONE question at a time. Wait for their response.
            """;

        return _chatClient.AsAIAgent(instructions: instructions, tools: tools);
    }

    private LookupResult LookupCustomer(string identifier, UserSessionContext context)
    {
        var customer = _cisDatabase.FindByIdentifier(identifier);

        if (customer == null)
        {
            return new LookupResult(false, "No account found with that phone, email, or account number.");
        }

        context.IdentifyingInfo = identifier;
        context.UserId = customer.AccountNumber;
        context.UserName = customer.Name;
        context.AuthState = AuthenticationState.Verifying;

        return new LookupResult(true, $"Found account for {customer.Name} at {customer.ServiceAddress}. Proceed with verification.");
    }

    private VerificationResult VerifyLastFourSSN(string answer, UserSessionContext context)
    {
        if (context.UserId == null)
            return new VerificationResult(false, "No customer to verify");

        var customer = _cisDatabase.FindByIdentifier(context.IdentifyingInfo!);
        if (customer == null)
            return new VerificationResult(false, "Customer not found");

        var cleaned = new string(answer.Where(char.IsDigit).ToArray());

        if (cleaned == customer.LastFourSSN)
        {
            context.VerifiedFactors.Add("SSN");
            return new VerificationResult(true, "SSN verified successfully");
        }

        context.FailedAttempts++;
        if (context.FailedAttempts >= 3)
        {
            context.AuthState = AuthenticationState.LockedOut;
            return new VerificationResult(false, "Too many failed attempts. Account locked.");
        }

        return new VerificationResult(false,
            $"Incorrect. {3 - context.FailedAttempts} attempts remaining.");
    }

    private VerificationResult VerifyDateOfBirth(string answer, UserSessionContext context)
    {
        if (context.UserId == null)
            return new VerificationResult(false, "No customer to verify");

        var customer = _cisDatabase.FindByIdentifier(context.IdentifyingInfo!);
        if (customer == null)
            return new VerificationResult(false, "Customer not found");

        // Try to parse various date formats
        if (DateOnly.TryParse(answer, out var dob) && dob == customer.DateOfBirth)
        {
            context.VerifiedFactors.Add("DOB");
            return new VerificationResult(true, "Date of birth verified successfully");
        }

        context.FailedAttempts++;
        if (context.FailedAttempts >= 3)
        {
            context.AuthState = AuthenticationState.LockedOut;
            return new VerificationResult(false, "Too many failed attempts. Account locked.");
        }

        return new VerificationResult(false,
            $"Incorrect. {3 - context.FailedAttempts} attempts remaining.");
    }

    private AuthResult CompleteAuthentication(UserSessionContext context)
    {
        if (context.VerifiedFactors.Count == 0)
            return new AuthResult(false, "No verification completed");

        context.AuthState = AuthenticationState.Authenticated;
        context.AuthenticatedAt = DateTimeOffset.UtcNow;
        context.SessionExpiry = DateTimeOffset.UtcNow.AddMinutes(30);

        return new AuthResult(true,
            $"Authentication complete. Customer {context.UserName} is now verified.");
    }
}

public record LookupResult(bool Found, string Message);
public record VerificationResult(bool Success, string Message);
public record AuthResult(bool Success, string Message);
```

### Testing Stage 3

```csharp
public class InBandAuthAgentTests
{
    private readonly MockCISDatabase _db = new();
    private readonly InBandAuthAgentFactory _factory;

    public InBandAuthAgentTests()
    {
        var chatClient = CreateMockChatClient();
        _factory = new InBandAuthAgentFactory(chatClient, _db);
    }

    [Fact]
    public async Task AuthAgent_FindsCustomerByPhone()
    {
        // Arrange
        var context = new UserSessionContext();
        var agent = _factory.CreateInBandAuthAgent(context);
        var session = await agent.CreateSessionAsync();

        // Act - Customer asks about their bill
        await agent.RunAsync("Why is my bill so high?", session);
        var response = await agent.RunAsync("555-1234", session);

        // Assert
        Assert.Equal(AuthenticationState.Verifying, context.AuthState);
        Assert.Equal("1234567890", context.UserId);  // Account number
        Assert.Equal("John Smith", context.UserName);
    }

    [Fact]
    public async Task AuthAgent_FindsCustomerByAccountNumber()
    {
        // Arrange
        var context = new UserSessionContext();
        var agent = _factory.CreateInBandAuthAgent(context);
        var session = await agent.CreateSessionAsync();

        // Act - Customer provides account number
        await agent.RunAsync("What's my balance?", session);
        var response = await agent.RunAsync("9876543210", session);

        // Assert
        Assert.Equal("Maria Garcia", context.UserName);
    }

    [Fact]
    public async Task AuthAgent_VerifiesSSN()
    {
        // Arrange
        var context = new UserSessionContext
        {
            AuthState = AuthenticationState.Verifying,
            UserId = "1234567890",
            IdentifyingInfo = "555-1234"
        };
        var agent = _factory.CreateInBandAuthAgent(context);
        var session = await agent.CreateSessionAsync();

        // Act
        await agent.RunAsync("1234", session);

        // Assert
        Assert.Contains("SSN", context.VerifiedFactors);
    }

    [Fact]
    public async Task AuthAgent_LocksAfterThreeFailures()
    {
        // Arrange
        var context = new UserSessionContext
        {
            AuthState = AuthenticationState.Verifying,
            UserId = "1234567890",
            IdentifyingInfo = "555-1234"
        };
        var agent = _factory.CreateInBandAuthAgent(context);
        var session = await agent.CreateSessionAsync();

        // Act - Three wrong answers
        await agent.RunAsync("0000", session);
        await agent.RunAsync("1111", session);
        await agent.RunAsync("2222", session);

        // Assert
        Assert.Equal(AuthenticationState.LockedOut, context.AuthState);
    }

    [Fact]
    public async Task AuthAgent_CompletesFullAuthFlow()
    {
        // Arrange
        var context = new UserSessionContext();
        var agent = _factory.CreateInBandAuthAgent(context);
        var session = await agent.CreateSessionAsync();

        // Act - Full conversation for utility billing
        await agent.RunAsync("Did you receive my payment?", session);  // Q3
        await agent.RunAsync("555-1234", session);  // Phone
        await agent.RunAsync("1234", session);       // Last 4 SSN

        // Assert
        Assert.Equal(AuthenticationState.Authenticated, context.AuthState);
        Assert.NotNull(context.AuthenticatedAt);
    }
}
```

### Validation Checklist - Stage 3
- [ ] Auth agent asks for identifier (phone/email/account number) when anonymous
- [ ] Auth agent finds customers in mock CIS database
- [ ] Auth agent asks verification questions one at a time
- [ ] Auth agent accepts correct answers and updates verified factors
- [ ] Auth agent tracks failed attempts
- [ ] Auth agent locks out after 3 failed attempts
- [ ] Auth agent completes authentication with 1 verified factor
- [ ] Auth state persists correctly across conversation turns

---

## Stage 4: Utility Data Agent

### Objective
Build an agent with tool access to the mock CIS database that answers account-specific utility billing questions for authenticated customers.

### Implementation

```csharp
/// <summary>
/// Data agent that fetches utility account data from the mock CIS database.
/// Requires customer to be authenticated first (via Stage 3).
/// Answers questions like: balance, payments, bill details, usage, AutoPay status
/// </summary>
public class UtilityDataAgentFactory : IUtilityDataAgentFactory
{
    private readonly IChatClient _chatClient;
    private readonly MockCISDatabase _cisDatabase;

    public UtilityDataAgentFactory(IChatClient chatClient, MockCISDatabase cisDatabase)
    {
        _chatClient = chatClient;
        _cisDatabase = cisDatabase;
    }

    public AIAgent CreateUtilityDataAgent(UserSessionContext userContext)
    {
        // Validate auth before creating agent
        if (userContext.AuthState != AuthenticationState.Authenticated)
            throw new InvalidOperationException("Customer must be authenticated to create data agent");

        // Get customer data from mock CIS
        var customer = _cisDatabase.FindByIdentifier(userContext.IdentifyingInfo!);
        if (customer == null)
            throw new InvalidOperationException("Authenticated customer not found in CIS");

        // Define tools that access the mock CIS/MDM data
        var tools = new AIFunction[]
        {
            // Q2: "What is my current account balance?"
            AIFunctionFactory.Create(
                () => GetAccountBalance(customer),
                name: "GetAccountBalance",
                description: "Gets current balance, due date, and last payment info"),

            // Q3: "Did you receive my payment?"
            AIFunctionFactory.Create(
                () => GetPaymentStatus(customer),
                name: "GetPaymentStatus",
                description: "Gets last payment date, amount, and current account status"),

            // Q4: "When is my payment due?"
            AIFunctionFactory.Create(
                () => GetDueDate(customer),
                name: "GetDueDate",
                description: "Gets payment due date and billing cycle info"),

            // Q1: "Why is my bill so high?"
            AIFunctionFactory.Create(
                () => GetUsageAnalysis(customer),
                name: "GetUsageAnalysis",
                description: "Compares current vs previous usage to explain bill changes"),

            // Q10: "Am I on AutoPay?"
            AIFunctionFactory.Create(
                () => GetAutoPayStatus(customer),
                name: "GetAutoPayStatus",
                description: "Gets AutoPay enrollment status"),

            // Q12: "What is this charge on my bill?"
            AIFunctionFactory.Create(
                () => GetBillDetails(customer),
                name: "GetBillDetails",
                description: "Gets detailed breakdown of current bill charges"),

            // Q13: "Is my bill based on actual or estimated read?"
            AIFunctionFactory.Create(
                () => GetMeterReadType(customer),
                name: "GetMeterReadType",
                description: "Gets whether last bill was actual (A) or estimated (E) read"),

            // Q19: "Can I get a copy of my bill?"
            AIFunctionFactory.Create(
                () => GetBillingHistory(customer),
                name: "GetBillingHistory",
                description: "Gets recent billing history with amounts and dates")
        };

        string instructions = $"""
            You are a utility billing account assistant. You help customers access their
            account information using the available tools.

            Customer: {userContext.UserName}
            Account: {customer.AccountNumber}
            Service Address: {customer.ServiceAddress}
            Authenticated at: {userContext.AuthenticatedAt:g}

            COMMON QUESTIONS YOU CAN ANSWER:
            - Q1: "Why is my bill so high?" → Use GetUsageAnalysis
            - Q2: "What's my balance?" → Use GetAccountBalance
            - Q3: "Did you receive my payment?" → Use GetPaymentStatus
            - Q4: "When is my payment due?" → Use GetDueDate
            - Q10: "Am I on AutoPay?" → Use GetAutoPayStatus
            - Q12: "What's this charge?" → Use GetBillDetails
            - Q13: "Is this an actual or estimated read?" → Use GetMeterReadType
            - Q19: "Can I get my bill history?" → Use GetBillingHistory

            RULES:
            1. Use the appropriate tool to fetch data the customer requests
            2. Present billing data clearly with dollar amounts and dates
            3. For high bill questions, compare usage periods and suggest reasons
            4. For actions requiring changes (payment arrangements, AutoPay enrollment),
               explain you can only view information and they need to speak to a representative
               or use self-service options
            5. Be empathetic about billing concerns
            """;

        return _chatClient.AsAIAgent(instructions: instructions, tools: tools);
    }

    private BalanceResult GetAccountBalance(UtilityCustomer customer)
    {
        return new BalanceResult(
            CurrentBalance: customer.AccountBalance,
            DueDate: customer.DueDate,
            LastPaymentAmount: customer.LastPaymentAmount,
            LastPaymentDate: customer.LastPaymentDate,
            DelinquencyStatus: customer.DelinquencyStatus);
    }

    private PaymentStatusResult GetPaymentStatus(UtilityCustomer customer)
    {
        bool paymentReceived = customer.LastPaymentDate > DateOnly.FromDateTime(DateTime.Now.AddDays(-30));
        return new PaymentStatusResult(
            LastPaymentReceived: paymentReceived,
            PaymentAmount: customer.LastPaymentAmount,
            PaymentDate: customer.LastPaymentDate,
            CurrentBalance: customer.AccountBalance,
            AccountStatus: customer.DelinquencyStatus);
    }

    private DueDateResult GetDueDate(UtilityCustomer customer)
    {
        var daysUntilDue = (customer.DueDate.ToDateTime(TimeOnly.MinValue) - DateTime.Now).Days;
        return new DueDateResult(
            DueDate: customer.DueDate,
            AmountDue: customer.AccountBalance,
            DaysUntilDue: daysUntilDue,
            IsPastDue: daysUntilDue < 0);
    }

    private UsageAnalysisResult GetUsageAnalysis(UtilityCustomer customer)
    {
        var currentBill = customer.BillingHistory.LastOrDefault();
        var previousBill = customer.BillingHistory.Count > 1
            ? customer.BillingHistory[^2]
            : null;

        var usageChange = previousBill != null
            ? ((currentBill?.KwhUsage ?? 0) - previousBill.KwhUsage) / (decimal)previousBill.KwhUsage * 100
            : 0;

        string explanation = usageChange switch
        {
            > 20 => "Your usage increased significantly, possibly due to seasonal heating/cooling or new appliances.",
            > 10 => "Your usage increased moderately compared to last month.",
            < -10 => "Your usage actually decreased compared to last month.",
            _ => "Your usage is similar to last month."
        };

        return new UsageAnalysisResult(
            CurrentPeriodKwh: currentBill?.KwhUsage ?? 0,
            CurrentPeriodAmount: currentBill?.AmountDue ?? 0,
            PreviousPeriodKwh: previousBill?.KwhUsage ?? 0,
            PreviousPeriodAmount: previousBill?.AmountDue ?? 0,
            UsageChangePercent: Math.Round(usageChange, 1),
            Explanation: explanation);
    }

    private AutoPayResult GetAutoPayStatus(UtilityCustomer customer)
    {
        return new AutoPayResult(
            IsEnrolled: customer.IsOnAutoPay,
            Message: customer.IsOnAutoPay
                ? "You are enrolled in AutoPay. Payments are automatically deducted on your due date."
                : "You are not currently enrolled in AutoPay.");
    }

    private BillDetailsResult GetBillDetails(UtilityCustomer customer)
    {
        var latestBill = customer.BillingHistory.LastOrDefault();
        return new BillDetailsResult(
            BillingPeriod: latestBill?.BillingPeriod ?? "N/A",
            TotalKwh: latestBill?.KwhUsage ?? 0,
            TotalAmount: latestBill?.AmountDue ?? 0,
            RateCode: customer.RateCode,
            ReadType: latestBill?.ReadType ?? "A",
            ServiceAddress: customer.ServiceAddress);
    }

    private MeterReadResult GetMeterReadType(UtilityCustomer customer)
    {
        var latestBill = customer.BillingHistory.LastOrDefault();
        var readType = latestBill?.ReadType ?? "A";
        return new MeterReadResult(
            ReadType: readType,
            ReadTypeDescription: readType == "A" ? "Actual meter reading" : "Estimated reading",
            MeterNumber: customer.MeterNumber,
            Note: readType == "E"
                ? "Your meter could not be read this month. The next bill will include a correction based on actual reading."
                : null);
    }

    private BillingHistoryResult GetBillingHistory(UtilityCustomer customer)
    {
        return new BillingHistoryResult(
            Bills: customer.BillingHistory.Select(b => new BillSummary(
                Period: b.BillingPeriod,
                Amount: b.AmountDue,
                Kwh: b.KwhUsage,
                BillDate: b.BillDate)).ToList());
    }
}

// Result records for utility billing data
public record BalanceResult(decimal CurrentBalance, DateOnly DueDate, decimal LastPaymentAmount, DateOnly LastPaymentDate, string DelinquencyStatus);
public record PaymentStatusResult(bool LastPaymentReceived, decimal PaymentAmount, DateOnly PaymentDate, decimal CurrentBalance, string AccountStatus);
public record DueDateResult(DateOnly DueDate, decimal AmountDue, int DaysUntilDue, bool IsPastDue);
public record UsageAnalysisResult(int CurrentPeriodKwh, decimal CurrentPeriodAmount, int PreviousPeriodKwh, decimal PreviousPeriodAmount, decimal UsageChangePercent, string Explanation);
public record AutoPayResult(bool IsEnrolled, string Message);
public record BillDetailsResult(string BillingPeriod, int TotalKwh, decimal TotalAmount, string RateCode, string ReadType, string ServiceAddress);
public record MeterReadResult(string ReadType, string ReadTypeDescription, string MeterNumber, string? Note);
public record BillingHistoryResult(List<BillSummary> Bills);
public record BillSummary(string Period, decimal Amount, int Kwh, DateOnly BillDate);
```

### Auth Guard Pattern

```csharp
/// <summary>
/// Guards data agent creation - ensures auth is completed first.
/// Use this in the orchestrator to check before routing to data agent.
/// </summary>
public static class AuthGuard
{
    public static bool IsAuthenticated(UserSessionContext context)
    {
        if (context.AuthState != AuthenticationState.Authenticated)
            return false;

        // Check session expiry
        if (context.SessionExpiry.HasValue && context.SessionExpiry.Value < DateTimeOffset.UtcNow)
        {
            context.AuthState = AuthenticationState.Expired;
            return false;
        }

        return true;
    }

    public static string GetAuthRequiredMessage(UserSessionContext context)
    {
        return context.AuthState switch
        {
            AuthenticationState.Expired =>
                "Your session has expired. Let me verify your identity again.",
            AuthenticationState.LockedOut =>
                "Your account is temporarily locked due to too many failed attempts. " +
                "Please contact a representative for assistance.",
            _ =>
                "To access your account information, I'll need to verify your identity first."
        };
    }
```

### Testing Stage 4

```csharp
public class UtilityDataAgentTests
{
    private readonly MockCISDatabase _db = new();
    private readonly UtilityDataAgentFactory _factory;

    public UtilityDataAgentTests()
    {
        var chatClient = CreateMockChatClient();
        _factory = new UtilityDataAgentFactory(chatClient, _db);
    }

    [Fact]
    public async Task DataAgent_FetchesBalance_WhenAuthenticated()
    {
        // Arrange - Q2: What is my balance / how much do I owe?
        var context = CreateAuthenticatedContext();
        var agent = _factory.CreateUtilityDataAgent(context);
        var session = await agent.CreateSessionAsync();

        // Act
        var response = await agent.RunAsync("What is my current balance?", session);

        // Assert
        Assert.Contains("247.83", response.Text); // Maria Garcia's balance
        Assert.Contains("2026-02-15", response.Text); // Due date
    }

    [Fact]
    public async Task DataAgent_ThrowsOnUnauthenticated()
    {
        // Arrange
        var context = new UserSessionContext { AuthState = AuthenticationState.Anonymous };

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() =>
            _factory.CreateUtilityDataAgent(context));
    }

    [Fact]
    public async Task DataAgent_AnalyzesHighBill()
    {
        // Arrange - Q1: Why is my bill so high?
        var context = CreateAuthenticatedContext();
        var agent = _factory.CreateUtilityDataAgent(context);
        var session = await agent.CreateSessionAsync();

        // Act
        var response = await agent.RunAsync("Why is my bill so high this month?", session);

        // Assert - Should show usage comparison
        Assert.Contains("usage", response.Text.ToLower());
        Assert.Contains("kWh", response.Text);
    }

    [Fact]
    public async Task DataAgent_ConfirmsPaymentReceived()
    {
        // Arrange - Q3: Did you receive my payment?
        var context = CreateAuthenticatedContext();
        var agent = _factory.CreateUtilityDataAgent(context);
        var session = await agent.CreateSessionAsync();

        // Act
        var response = await agent.RunAsync("Did you receive my payment?", session);

        // Assert
        Assert.Contains("185.42", response.Text); // Last payment amount
        Assert.Contains("2026-01-28", response.Text); // Last payment date
    }

    [Fact]
    public async Task DataAgent_ChecksAutoPayStatus()
    {
        // Arrange - Q10: Am I signed up for autopay?
        var context = CreateAuthenticatedContext();
        var agent = _factory.CreateUtilityDataAgent(context);
        var session = await agent.CreateSessionAsync();

        // Act
        var response = await agent.RunAsync("Am I enrolled in AutoPay?", session);

        // Assert
        Assert.Contains("not currently enrolled", response.Text.ToLower());
    }

    [Fact]
    public async Task DataAgent_ExplainsMeterReadType()
    {
        // Arrange - Q14: Was my meter actually read?
        var context = CreateAuthenticatedContext();
        var agent = _factory.CreateUtilityDataAgent(context);
        var session = await agent.CreateSessionAsync();

        // Act
        var response = await agent.RunAsync("Was my meter actually read this month?", session);

        // Assert
        Assert.Contains("AMI", response.Text); // Smart meter type
    }

    private UserSessionContext CreateAuthenticatedContext() => new()
    {
        AccountNumber = "ACC-2024-0042",
        CustomerName = "Maria Garcia",
        AuthState = AuthenticationState.Authenticated,
        IdentifyingInfo = "555-9876",
        AuthenticatedAt = DateTimeOffset.UtcNow,
        SessionExpiry = DateTimeOffset.UtcNow.AddMinutes(30)
    };
}
```

### Validation Checklist - Stage 4
- [ ] Data agent fetches account balance for authenticated users (Q2)
- [ ] Data agent throws exception for unauthenticated users
- [ ] Data agent analyzes high bill with usage comparison (Q1)
- [ ] Data agent confirms payment received with amount and date (Q3)
- [ ] Data agent checks AutoPay enrollment status (Q10)
- [ ] Data agent explains meter read type (Q14)
- [ ] AuthGuard correctly checks authentication state
- [ ] AuthGuard detects expired sessions

---

## Stage 5: Orchestrator

### Objective
Build the main orchestrator that routes user messages through the classifier and dispatches to appropriate agents.

### Architecture Pattern: Custom Orchestrator with Manual Routing

> **Note**: This implementation uses a custom orchestrator class with manual routing logic.
> For a declarative alternative using the framework's `WorkflowBuilder`, see **Appendix B**.
> The custom approach is suitable for prototyping; consider migrating for production.

```csharp
public class ChatbotOrchestrator
{
    private readonly IChatClient _chatClient;
    private readonly IClassifierAgentFactory _classifierFactory;
    private readonly IFAQAgentFactory _faqFactory;
    private readonly IUtilityDataAgentFactory _utilityDataFactory;
    private readonly IInBandAuthAgentFactory _authAgentFactory;
    private readonly ISessionStore _sessionStore;

    // In-memory cache for active sessions (backed by ISessionStore for persistence)
    private readonly ConcurrentDictionary<string, ChatSession> _sessionCache = new();

    public ChatbotOrchestrator(
        IChatClient chatClient,
        IClassifierAgentFactory classifierFactory,
        IFAQAgentFactory faqFactory,
        IUtilityDataAgentFactory utilityDataFactory,
        IInBandAuthAgentFactory authAgentFactory,
        ISessionStore sessionStore)
    {
        _chatClient = chatClient;
        _classifierFactory = classifierFactory;
        _faqFactory = faqFactory;
        _utilityDataFactory = utilityDataFactory;
        _authAgentFactory = authAgentFactory;
        _sessionStore = sessionStore;
    }

    public async Task<ChatResponse> ProcessMessageAsync(
        string sessionId,
        string userMessage,
        CancellationToken cancellationToken = default)
    {
        // Get or create session (check cache first, then persistent store)
        var chatSession = await GetOrCreateSessionAsync(sessionId, cancellationToken);

        // Add user message to history
        chatSession.ConversationHistory.Add(new ConversationMessage
        {
            Role = "user",
            Content = userMessage,
            Timestamp = DateTimeOffset.UtcNow
        });

        try
        {
            // MULTI-TURN SUPPORT: Check if user is in active authentication flow
            if (chatSession.UserContext.AuthState == AuthenticationState.InProgress)
            {
                return await ContinueAuthenticationFlowAsync(userMessage, chatSession, cancellationToken);
            }

            // Step 1: Classify the question
            var classification = await ClassifyQuestionAsync(userMessage, chatSession, cancellationToken);

            // Step 2: Route based on classification
            var response = classification.Category switch
            {
                QuestionCategory.BillingFAQ =>
                    await HandleBillingFAQAsync(userMessage, chatSession, cancellationToken),

                QuestionCategory.AccountData =>
                    await HandleAccountDataAsync(userMessage, chatSession, classification, cancellationToken),

                QuestionCategory.ServiceRequest =>
                    await InitiateHumanHandoffAsync(userMessage, chatSession, "Service request requires CSR assistance", cancellationToken),

                QuestionCategory.HumanRequested =>
                    await InitiateHumanHandoffAsync(userMessage, chatSession, "User requested human assistance", cancellationToken),

                QuestionCategory.OutOfScope =>
                    await HandleOutOfScopeAsync(userMessage, chatSession, classification, cancellationToken),

                _ => throw new InvalidOperationException($"Unknown category: {classification.Category}")
            };

            // Add response to history
            chatSession.ConversationHistory.Add(new ConversationMessage
            {
                Role = "assistant",
                Content = response.Message,
                Timestamp = DateTimeOffset.UtcNow
            });

            return response;
        }
        catch (Exception ex)
        {
            // Log error and attempt graceful degradation
            // In production, use proper logging
            Console.WriteLine($"Error processing message: {ex.Message}");

            return await InitiateHumanHandoffAsync(
                userMessage,
                chatSession,
                $"System error: {ex.Message}",
                cancellationToken);
        }
    }

    private async Task<QuestionClassification> ClassifyQuestionAsync(
        string message,
        ChatSession session,
        CancellationToken cancellationToken)
    {
        var classifier = _classifierFactory.CreateClassifierAgent();

        // Create session with conversation context for better classification
        var agentSession = await classifier.CreateSessionAsync(
            AIContextProviderFactory: (ctx, ct) => new ValueTask<AIContextProvider>(
                new ConversationContextProvider(session.ConversationHistory)));

        var response = await classifier.RunAsync<QuestionClassification>(
            message,
            agentSession,
            cancellationToken: cancellationToken);

        return response.Result;
    }

    private async Task<ChatResponse> HandleBillingFAQAsync(
        string message,
        ChatSession session,
        CancellationToken cancellationToken)
    {
        var faqAgent = _faqFactory.CreateFAQAgent();
        var agentSession = await faqAgent.CreateSessionAsync();

        // Inject conversation history for context
        foreach (var historyMessage in session.ConversationHistory.TakeLast(10))
        {
            // Add relevant history to session
        }

        var response = await faqAgent.RunAsync(message, agentSession, cancellationToken: cancellationToken);

        return new ChatResponse
        {
            Message = response.Text,
            Category = QuestionCategory.BillingFAQ,
            RequiredAction = RequiredAction.None
        };
    }

    private async Task<ChatResponse> HandleAccountDataAsync(
        string message,
        ChatSession session,
        QuestionClassification classification,
        CancellationToken cancellationToken)
    {
        // Check if user is authenticated
        if (session.UserContext.AuthState != AuthenticationState.Authenticated)
        {
            // INITIATE IN-BAND AUTH FLOW: Route to authentication agent
            return await InitiateAuthenticationFlowAsync(message, session, cancellationToken);
        }

        // Check session expiry
        if (session.UserContext.SessionExpiry.HasValue &&
            session.UserContext.SessionExpiry.Value < DateTimeOffset.UtcNow)
        {
            session.UserContext.AuthState = AuthenticationState.Expired;
            // Re-initiate authentication for expired session
            return await InitiateAuthenticationFlowAsync(message, session, cancellationToken);
        }

        // User is authenticated - proceed with data agent
        var dataAgent = _utilityDataFactory.CreateUtilityDataAgent(session.UserContext);
        var agentSession = await dataAgent.CreateSessionAsync(
            AIContextProviderFactory: (ctx, ct) => new ValueTask<AIContextProvider>(
                new AuthContextProvider(session.UserContext)));

        var response = await dataAgent.RunAsync(message, agentSession, cancellationToken: cancellationToken);

        return new ChatResponse
        {
            Message = response.Text,
            Category = QuestionCategory.AccountData,
            RequiredAction = RequiredAction.None
        };
    }

    /// <summary>
    /// Initiates the in-band authentication flow using the InBandAuthAgent.
    /// Stores the pending query to resume after successful authentication.
    /// </summary>
    private async Task<ChatResponse> InitiateAuthenticationFlowAsync(
        string pendingMessage,
        ChatSession session,
        CancellationToken cancellationToken)
    {
        // Store the pending query to resume after authentication
        session.PendingQuery = pendingMessage;

        // Mark session as in authentication flow
        session.UserContext.AuthState = AuthenticationState.InProgress;

        // Create auth agent and session (pass context so tools can modify auth state)
        var authAgent = _authAgentFactory.CreateInBandAuthAgent(session.UserContext);
        session.AuthAgentSession = await authAgent.CreateSessionAsync();

        // Initial prompt from auth agent
        var response = await authAgent.RunAsync(
            "Start identity verification for account access",
            session.AuthAgentSession,
            cancellationToken: cancellationToken);

        return new ChatResponse
        {
            Message = response.Text,
            Category = QuestionCategory.AccountData,
            RequiredAction = RequiredAction.AuthenticationInProgress
        };
    }

    /// <summary>
    /// Continues an in-progress authentication flow by routing user input to the InBandAuthAgent.
    /// Automatically resumes the pending query upon successful authentication.
    /// </summary>
    private async Task<ChatResponse> ContinueAuthenticationFlowAsync(
        string userMessage,
        ChatSession session,
        CancellationToken cancellationToken)
    {
        if (session.AuthAgentSession == null)
        {
            // Shouldn't happen, but handle gracefully
            session.UserContext.AuthState = AuthenticationState.Anonymous;
            return new ChatResponse
            {
                Message = "I apologize, there was an issue with the verification process. Let's start over. " +
                         "Can you please provide your account number or phone number?",
                Category = QuestionCategory.AccountData,
                RequiredAction = RequiredAction.AuthenticationRequired
            };
        }

        // Continue the auth conversation
        var authAgent = _authAgentFactory.CreateInBandAuthAgent(session.UserContext);
        var response = await authAgent.RunAsync(
            userMessage,
            session.AuthAgentSession,
            cancellationToken: cancellationToken);

        // Check if authentication completed (agent sets context)
        if (session.UserContext.AuthState == AuthenticationState.Authenticated)
        {
            // Clear auth session
            session.AuthAgentSession = null;

            // Resume pending query if exists
            if (!string.IsNullOrEmpty(session.PendingQuery))
            {
                var pendingQuery = session.PendingQuery;
                session.PendingQuery = null;

                // Add confirmation message to history
                session.ConversationHistory.Add(new ConversationMessage
                {
                    Role = "assistant",
                    Content = response.Text,
                    Timestamp = DateTimeOffset.UtcNow
                });

                // Process the original query now that we're authenticated
                return await ProcessMessageAsync(session.SessionId, pendingQuery, cancellationToken);
            }

            return new ChatResponse
            {
                Message = response.Text,
                Category = QuestionCategory.AccountData,
                RequiredAction = RequiredAction.None
            };
        }

        // Check if authentication failed (locked out)
        if (session.UserContext.AuthState == AuthenticationState.LockedOut)
        {
            session.AuthAgentSession = null;
            session.PendingQuery = null;

            // Escalate to human
            return await InitiateHumanHandoffAsync(
                session.PendingQuery ?? "Authentication assistance needed",
                session,
                "User locked out after failed verification attempts",
                cancellationToken);
        }

        // Still in progress - return agent's response (asking for next verification item)
        return new ChatResponse
        {
            Message = response.Text,
            Category = QuestionCategory.AccountData,
            RequiredAction = RequiredAction.AuthenticationInProgress
        };
    }

    private async Task<ChatResponse> HandleOutOfScopeAsync(
        string message,
        ChatSession session,
        QuestionClassification classification,
        CancellationToken cancellationToken)
    {
        // Low confidence - try to help but offer human handoff
        if (classification.Confidence < 0.3)
        {
            return await InitiateHumanHandoffAsync(
                message,
                session,
                $"Low confidence classification: {classification.Reasoning}",
                cancellationToken);
        }

        // Medium confidence - try to help
        return new ChatResponse
        {
            Message = "I'm not entirely sure I understand your question. Could you rephrase it, " +
                     "or would you like me to connect you with a customer service representative?",
            Category = QuestionCategory.OutOfScope,
            RequiredAction = RequiredAction.ClarificationNeeded
        };
    }

    private async Task<ChatResponse> InitiateHumanHandoffAsync(
        string message,
        ChatSession session,
        string reason,
        CancellationToken cancellationToken)
    {
        // This will be fully implemented in Stage 5
        // For now, return a placeholder response
        return new ChatResponse
        {
            Message = "I'm connecting you with a customer service representative. " +
                     "Please hold while I prepare a summary of our conversation.",
            Category = QuestionCategory.HumanRequested,
            RequiredAction = RequiredAction.HumanHandoffInitiated
        };
    }

    // Authentication callback from UI (legacy - for external OAuth flows)
    public async Task<ChatResponse> CompleteAuthenticationAsync(
        string sessionId,
        string accessToken,
        string userId,
        string userName,
        DateTimeOffset tokenExpiry,
        CancellationToken cancellationToken = default)
    {
        if (!_sessionCache.TryGetValue(sessionId, out var session))
        {
            throw new InvalidOperationException("Session not found");
        }

        // Update auth state
        session.UserContext.AccessToken = accessToken;
        session.UserContext.UserId = userId;
        session.UserContext.UserName = userName;
        session.UserContext.TokenExpiry = tokenExpiry;
        session.UserContext.AuthState = AuthenticationState.Authenticated;

        // If there was a pending query, process it now
        if (!string.IsNullOrEmpty(session.PendingQuery))
        {
            var pendingQuery = session.PendingQuery;
            session.PendingQuery = null;

            return await ProcessMessageAsync(sessionId, pendingQuery, cancellationToken);
        }

        return new ChatResponse
        {
            Message = $"Thank you, {userName}! Your identity has been verified. How can I help you with your utility account today?",
            Category = QuestionCategory.BillingFAQ,
            RequiredAction = RequiredAction.None
        };
    }

    // ========== Session Management Helpers ==========

    /// <summary>
    /// Gets session from cache or persistent store, creating new if not found.
    /// </summary>
    private async Task<ChatSession> GetOrCreateSessionAsync(
        string sessionId,
        CancellationToken cancellationToken)
    {
        // Check in-memory cache first
        if (_sessionCache.TryGetValue(sessionId, out var cachedSession))
        {
            cachedSession.UserContext.LastInteraction = DateTimeOffset.UtcNow;
            return cachedSession;
        }

        // Check persistent store
        var persistedSession = await _sessionStore.GetSessionAsync(sessionId, cancellationToken);
        if (persistedSession != null)
        {
            persistedSession.UserContext.LastInteraction = DateTimeOffset.UtcNow;
            _sessionCache[sessionId] = persistedSession;
            return persistedSession;
        }

        // Create new session
        var newSession = new ChatSession
        {
            SessionId = sessionId,
            UserContext = new UserSessionContext { SessionId = sessionId },
            ConversationHistory = []
        };

        _sessionCache[sessionId] = newSession;
        return newSession;
    }

    /// <summary>
    /// Persists session to store (call periodically or on important state changes).
    /// </summary>
    public async Task SaveSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        if (_sessionCache.TryGetValue(sessionId, out var session))
        {
            await _sessionStore.SaveSessionAsync(session, cancellationToken);
        }
    }

    /// <summary>
    /// Gets session from cache (for read-only access).
    /// </summary>
    public ChatSession? GetSession(string sessionId)
    {
        _sessionCache.TryGetValue(sessionId, out var session);
        return session;
    }
}

// Response models
public class ChatResponse
{
    public string Message { get; set; } = string.Empty;
    public QuestionCategory Category { get; set; }
    public RequiredAction RequiredAction { get; set; }
    public string? PendingQuery { get; set; }
}

public enum RequiredAction
{
    None,
    AuthenticationRequired,
    AuthenticationInProgress,  // Multi-turn auth flow active - route next message to auth agent
    HumanHandoffInitiated,
    ClarificationNeeded,
    HumanConversationActive,   // Human agent has joined and is actively chatting
    TransferInProgress,        // Being transferred to specialist
    CallbackScheduled          // Callback has been scheduled
}

// Session storage
public class ChatSession
{
    public string SessionId { get; set; } = string.Empty;
    public UserSessionContext UserContext { get; set; } = new();
    public List<ConversationMessage> ConversationHistory { get; set; } = [];
    public AgentSession? ClassifierSession { get; set; }
    public AgentSession? FAQSession { get; set; }
    public AgentSession? AuthAgentSession { get; set; } // For multi-turn in-band auth flow
    public AgentSession? DataSession { get; set; }
    public string? PendingQuery { get; set; }
}
```

### Conversation Context Provider

```csharp
/// <summary>
/// Provides conversation history context to agents
/// </summary>
public class ConversationContextProvider : AIContextProvider
{
    private readonly List<ConversationMessage> _history;

    public ConversationContextProvider(List<ConversationMessage> history)
    {
        _history = history;
    }

    public override ValueTask<AIContext> InvokingAsync(
        InvokingContext context,
        CancellationToken cancellationToken = default)
    {
        // Provide conversation summary to agent
        var recentHistory = _history.TakeLast(5);
        var summary = string.Join("\n", recentHistory.Select(m =>
            $"{m.Role}: {m.Content}"));

        return new ValueTask<AIContext>(new AIContext
        {
            Messages = [new ChatMessage(ChatRole.System,
                $"[Recent conversation history for context:\n{summary}]")]
        });
    }
}
```

### Testing Stage 5

```csharp
public class OrchestratorTests
{
    [Fact]
    public async Task Orchestrator_RoutesBillingFAQ_ToFAQAgent()
    {
        // Arrange - Q6: What are my payment options?
        var orchestrator = CreateOrchestrator();
        var sessionId = Guid.NewGuid().ToString();

        // Act
        var response = await orchestrator.ProcessMessageAsync(
            sessionId,
            "What are my payment options?");

        // Assert
        Assert.Equal(QuestionCategory.BillingFAQ, response.Category);
        Assert.Contains("online", response.Message.ToLower());
    }

    [Fact]
    public async Task Orchestrator_RequiresAuth_ForAccountData()
    {
        // Arrange - Q2: What is my balance?
        var orchestrator = CreateOrchestrator();
        var sessionId = Guid.NewGuid().ToString();

        // Act
        var response = await orchestrator.ProcessMessageAsync(
            sessionId,
            "What is my current balance?");

        // Assert
        Assert.Equal(RequiredAction.AuthenticationRequired, response.RequiredAction);
        Assert.Contains("verify", response.Message.ToLower());
    }

    [Fact]
    public async Task Orchestrator_ProcessesAccountData_WhenAuthenticated()
    {
        // Arrange
        var orchestrator = CreateOrchestrator();
        var sessionId = Guid.NewGuid().ToString();

        // First, authenticate via in-band flow
        await orchestrator.CompleteAuthenticationAsync(
            sessionId,
            accountNumber: "ACC-2024-0042",
            customerName: "Maria Garcia",
            tokenExpiry: DateTimeOffset.UtcNow.AddMinutes(30));

        // Act - Q1: Why is my bill so high?
        var response = await orchestrator.ProcessMessageAsync(
            sessionId,
            "Why is my bill so high this month?");

        // Assert
        Assert.Equal(QuestionCategory.AccountData, response.Category);
        Assert.Equal(RequiredAction.None, response.RequiredAction);
    }

    [Fact]
    public async Task Orchestrator_MaintainsSession_AcrossMessages()
    {
        // Arrange
        var orchestrator = CreateOrchestrator();
        var sessionId = Guid.NewGuid().ToString();

        // Act - Multi-turn conversation about assistance programs
        await orchestrator.ProcessMessageAsync(sessionId, "Hi there");
        await orchestrator.ProcessMessageAsync(sessionId, "Do you have any assistance programs?");
        var response = await orchestrator.ProcessMessageAsync(sessionId, "How do I apply?");

        // Assert - should understand context from previous message (Q7: LIHEAP)
        Assert.Contains("LIHEAP", response.Message);
    }

    [Fact]
    public async Task Orchestrator_InitiatesHandoff_ForHumanRequest()
    {
        // Arrange
        var orchestrator = CreateOrchestrator();
        var sessionId = Guid.NewGuid().ToString();

        // Act
        var response = await orchestrator.ProcessMessageAsync(
            sessionId,
            "I need to speak with a customer service representative");

        // Assert
        Assert.Equal(RequiredAction.HumanHandoffInitiated, response.RequiredAction);
    }

    [Fact]
    public async Task Orchestrator_ResumesQuery_AfterAuthentication()
    {
        // Arrange
        var orchestrator = CreateOrchestrator();
        var sessionId = Guid.NewGuid().ToString();

        // Try to access account data (triggers auth required)
        var authRequired = await orchestrator.ProcessMessageAsync(
            sessionId,
            "What is my current balance?");
        Assert.Equal(RequiredAction.AuthenticationRequired, authRequired.RequiredAction);

        // Complete authentication via in-band flow
        var afterAuth = await orchestrator.CompleteAuthenticationAsync(
            sessionId,
            accountNumber: "ACC-2024-0042",
            customerName: "Maria Garcia",
            tokenExpiry: DateTimeOffset.UtcNow.AddMinutes(30));

        // Assert - should have automatically processed pending query
        Assert.Contains("balance", afterAuth.Message.ToLower());
    }

    [Fact]
    public async Task Orchestrator_RoutesServiceRequest_ToHandoff()
    {
        // Arrange - Q17: Can I set up a payment arrangement?
        var orchestrator = CreateOrchestrator();
        var sessionId = Guid.NewGuid().ToString();

        // Act
        var response = await orchestrator.ProcessMessageAsync(
            sessionId,
            "I need to set up a payment arrangement");

        // Assert
        Assert.Equal(QuestionCategory.ServiceRequest, response.Category);
        Assert.Equal(RequiredAction.HumanHandoffInitiated, response.RequiredAction);
    }
}
```

### Validation Checklist - Stage 5
- [ ] Orchestrator correctly routes BillingFAQ questions to FAQ agent
- [ ] Orchestrator correctly routes AccountData questions to auth flow
- [ ] Orchestrator correctly routes ServiceRequest questions to handoff
- [ ] Orchestrator prompts for authentication when needed
- [ ] Orchestrator resumes pending query after successful authentication
- [ ] Orchestrator handles authentication expiry gracefully
- [ ] Orchestrator maintains conversation context across messages
- [ ] Orchestrator initiates handoff for human requests
- [ ] Orchestrator handles errors gracefully with fallback to human handoff
- [ ] Session data persists correctly across the conversation

---

## Stage 6: Human Handoff with Summarization (WebSocket)

### Objective
Build the summarization agent and human handoff flow using WebSocket for real-time communication between customer, AI, and human agents.

### Architecture: WebSocket + RequestPort Pattern

```
┌─────────────────────────────────────────────────────────────────────────┐
│                        HUMAN HANDOFF ARCHITECTURE                        │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                          │
│  Customer                    Server                      Human Agent     │
│  ┌──────┐                ┌───────────┐                   ┌──────┐       │
│  │ Web  │◄──WebSocket──►│ SignalR   │◄────WebSocket────►│ Agent│       │
│  │Client│                │   Hub     │                   │  UI  │       │
│  └──────┘                └─────┬─────┘                   └──────┘       │
│                                │                                         │
│                                ▼                                         │
│                    ┌────────────────────┐                               │
│                    │ Handoff Manager    │                               │
│                    │ - Ticket Queue     │                               │
│                    │ - Session Mapping  │                               │
│                    │ - Event Routing    │                               │
│                    └─────────┬──────────┘                               │
│                              │                                           │
│                    ┌─────────▼──────────┐                               │
│                    │ Summarization      │                               │
│                    │ Agent              │                               │
│                    └────────────────────┘                               │
│                                                                          │
└─────────────────────────────────────────────────────────────────────────┘
```

### Implementation - Summarization Agent

```csharp
public class SummarizationAgentFactory : ISummarizationAgentFactory
{
    private readonly IChatClient _chatClient;

    public SummarizationAgentFactory(IChatClient chatClient)
    {
        _chatClient = chatClient;
    }

    public AIAgent CreateSummarizationAgent()
    {
        const string instructions = """
            You are a conversation summarizer for customer support handoffs.
            Your job is to create a concise but complete summary for human agents.

            Include in your summary:
            1. User's main issue/question (1-2 sentences)
            2. What the AI tried (briefly)
            3. Why escalation is happening
            4. User's emotional state (if apparent)
            5. Any relevant account/order info mentioned

            Format:
            ## Issue Summary
            [Main issue]

            ## AI Attempts
            [What was tried]

            ## Escalation Reason
            [Why human needed]

            ## User Context
            - Name: [if known]
            - Account: [if authenticated]
            - Mood: [neutral/frustrated/urgent]

            ## Recommended Next Steps
            [Suggestions for human agent]

            Keep the summary under 200 words. Be factual and neutral in tone.
            """;

        return _chatClient.AsAIAgent(instructions: instructions);
    }
}
```

### Handoff Service

> **Note**: The `IHandoffService` interface is defined in Section 4 (Core Components - Service Abstractions).

```csharp
public class HumanHandoffService : IHandoffService
{
    private readonly IHandoffQueue _queue;
    private readonly INotificationService _notifications;

    public async Task<string> CreateHandoffTicketAsync(
        HumanHandoffRequest request,
        CancellationToken cancellationToken)
    {
        // Add to human agent queue
        await _queue.EnqueueAsync(request, cancellationToken);

        // Notify available agents
        await _notifications.NotifyAgentsAsync(
            $"New handoff ticket: {request.TicketId}",
            cancellationToken);

        return request.TicketId;
    }

    public async Task<HumanResponse?> WaitForHumanResponseAsync(
        string ticketId,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, timeoutCts.Token);

        try
        {
            return await _queue.WaitForResponseAsync(ticketId, linkedCts.Token);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            return null; // Timeout
        }
    }

    public async Task NotifyCustomerAsync(
        string sessionId,
        string message,
        CancellationToken cancellationToken)
    {
        await _notifications.SendToSessionAsync(sessionId, message, cancellationToken);
    }
}

public class HumanResponse
{
    public string TicketId { get; set; } = string.Empty;
    public string AgentId { get; set; } = string.Empty;
    public string AgentName { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public HandoffResolution Resolution { get; set; }
    public DateTimeOffset RespondedAt { get; set; }
}

public enum HandoffResolution
{
    Resolved,
    ContinueConversation,
    TransferToSpecialist,
    ScheduleCallback
}
```

### Updated Orchestrator with Full Handoff Flow

```csharp
public partial class ChatbotOrchestrator
{
    private readonly SummarizationAgentFactory _summarizationFactory;
    private readonly IHandoffService _handoffService;

    public async Task<ChatResponse> InitiateHumanHandoffAsync(
        string message,
        ChatSession session,
        string reason,
        CancellationToken cancellationToken)
    {
        // Step 1: Generate conversation summary
        var summarizer = _summarizationFactory.CreateSummarizationAgent();
        var summarySession = await summarizer.CreateSessionAsync();

        var conversationText = string.Join("\n",
            session.ConversationHistory.Select(m => $"{m.Role}: {m.Content}"));

        var summaryPrompt = $"""
            Summarize this customer support conversation for handoff to a human agent.

            Escalation reason: {reason}
            Current user question: {message}

            Conversation:
            {conversationText}
            """;

        var summaryResponse = await summarizer.RunAsync(
            summaryPrompt,
            summarySession,
            cancellationToken: cancellationToken);

        // Step 2: Create handoff request
        var handoffRequest = new HumanHandoffRequest
        {
            ConversationSummary = summaryResponse.Text,
            OriginalQuestion = message,
            EscalationReason = reason,
            UserContext = session.UserContext,
            ConversationHistory = session.ConversationHistory,
            SuggestedDepartment = DetermineDepartment(session, reason)
        };

        // Step 3: Create ticket and queue for human
        var ticketId = await _handoffService.CreateHandoffTicketAsync(
            handoffRequest,
            cancellationToken);

        // Store ticket ID in session for tracking
        session.CurrentHandoffTicketId = ticketId;
        session.HandoffState = HandoffState.WaitingForHuman;

        return new ChatResponse
        {
            Message = $"I've connected you to our support team. A representative will be with you shortly. " +
                     $"Your ticket number is {ticketId}. While you wait, is there anything else I can help clarify?",
            Category = QuestionCategory.HumanRequested,
            RequiredAction = RequiredAction.HumanHandoffInitiated,
            TicketId = ticketId
        };
    }

    /// <summary>
    /// Called when human agent responds to handoff
    /// </summary>
    public async Task<ChatResponse> HandleHumanResponseAsync(
        string sessionId,
        HumanResponse humanResponse,
        CancellationToken cancellationToken)
    {
        if (!_sessionCache.TryGetValue(sessionId, out var session))
        {
            throw new InvalidOperationException("Session not found");
        }

        // Update session state
        session.HandoffState = HandoffState.HumanResponded;

        // Add human response to history
        session.ConversationHistory.Add(new ConversationMessage
        {
            Role = "agent", // Distinguish from AI assistant
            Content = $"[Support Agent {humanResponse.AgentName}]: {humanResponse.Message}",
            Timestamp = humanResponse.RespondedAt
        });

        return humanResponse.Resolution switch
        {
            HandoffResolution.Resolved => new ChatResponse
            {
                Message = humanResponse.Message,
                Category = QuestionCategory.HumanRequested,
                RequiredAction = RequiredAction.None
            },

            HandoffResolution.ContinueConversation => new ChatResponse
            {
                Message = humanResponse.Message +
                         "\n\n[You're now chatting with our support team. I'll be here if you need AI assistance again.]",
                Category = QuestionCategory.HumanRequested,
                RequiredAction = RequiredAction.HumanConversationActive
            },

            HandoffResolution.TransferToSpecialist => new ChatResponse
            {
                Message = humanResponse.Message,
                Category = QuestionCategory.HumanRequested,
                RequiredAction = RequiredAction.TransferInProgress
            },

            HandoffResolution.ScheduleCallback => new ChatResponse
            {
                Message = humanResponse.Message,
                Category = QuestionCategory.HumanRequested,
                RequiredAction = RequiredAction.CallbackScheduled
            },

            _ => throw new InvalidOperationException($"Unknown resolution: {humanResponse.Resolution}")
        };
    }

    /// <summary>
    /// Polls for human response (for non-realtime integrations)
    /// </summary>
    public async Task<ChatResponse?> PollHandoffStatusAsync(
        string sessionId,
        CancellationToken cancellationToken)
    {
        if (!_sessionCache.TryGetValue(sessionId, out var session))
        {
            return null;
        }

        if (session.HandoffState != HandoffState.WaitingForHuman ||
            string.IsNullOrEmpty(session.CurrentHandoffTicketId))
        {
            return null;
        }

        var response = await _handoffService.WaitForHumanResponseAsync(
            session.CurrentHandoffTicketId,
            TimeSpan.FromSeconds(5), // Short poll interval
            cancellationToken);

        if (response != null)
        {
            return await HandleHumanResponseAsync(sessionId, response, cancellationToken);
        }

        return null; // No response yet
    }

    private string DetermineDepartment(ChatSession session, string reason)
    {
        // Simple routing logic - could be enhanced with ML
        if (reason.Contains("payment", StringComparison.OrdinalIgnoreCase) ||
            reason.Contains("balance", StringComparison.OrdinalIgnoreCase) ||
            reason.Contains("bill", StringComparison.OrdinalIgnoreCase))
            return "Billing";
        if (reason.Contains("outage", StringComparison.OrdinalIgnoreCase) ||
            reason.Contains("meter", StringComparison.OrdinalIgnoreCase) ||
            reason.Contains("service", StringComparison.OrdinalIgnoreCase))
            return "Field Services";
        if (reason.Contains("start", StringComparison.OrdinalIgnoreCase) ||
            reason.Contains("stop", StringComparison.OrdinalIgnoreCase) ||
            reason.Contains("transfer", StringComparison.OrdinalIgnoreCase))
            return "New Service";
        if (reason.Contains("disconnect", StringComparison.OrdinalIgnoreCase) ||
            reason.Contains("shutoff", StringComparison.OrdinalIgnoreCase))
            return "Collections";

        return "General Support";
    }
}

// Extended session model
public partial class ChatSession
{
    public string? CurrentHandoffTicketId { get; set; }
    public HandoffState HandoffState { get; set; } = HandoffState.None;
}

public enum HandoffState
{
    None,
    WaitingForHuman,
    HumanResponded,
    HumanConversationActive,
    Resolved
}

// Note: RequiredAction enum is defined in Stage 5 (Orchestrator section)
// with all values including: HumanConversationActive, TransferInProgress, CallbackScheduled
```

### WebSocket Hub (SignalR)

```csharp
using Microsoft.AspNetCore.SignalR;

/// <summary>
/// SignalR hub for real-time communication between customers and human agents.
/// Handles message routing, typing indicators, and connection management.
/// </summary>
public class ChatHub : Hub
{
    private readonly HandoffManager _handoffManager;
    private readonly ILogger<ChatHub> _logger;

    public ChatHub(HandoffManager handoffManager, ILogger<ChatHub> logger)
    {
        _handoffManager = handoffManager;
        _logger = logger;
    }

    // ========== Customer Methods ==========

    /// <summary>
    /// Customer joins their chat session
    /// </summary>
    public async Task JoinSession(string sessionId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"session:{sessionId}");
        _handoffManager.RegisterCustomerConnection(sessionId, Context.ConnectionId);
        _logger.LogInformation("Customer joined session {SessionId}", sessionId);
    }

    /// <summary>
    /// Customer sends a message (during handoff, goes to human agent)
    /// </summary>
    public async Task SendMessage(string sessionId, string message)
    {
        var ticket = _handoffManager.GetActiveTicket(sessionId);

        if (ticket != null && ticket.AssignedAgentId != null)
        {
            // Route to assigned human agent
            await Clients.Group($"agent:{ticket.AssignedAgentId}")
                .SendAsync("CustomerMessage", new
                {
                    TicketId = ticket.TicketId,
                    SessionId = sessionId,
                    Message = message,
                    Timestamp = DateTimeOffset.UtcNow
                });
        }
    }

    // ========== Human Agent Methods ==========

    /// <summary>
    /// Human agent joins the agent pool
    /// </summary>
    public async Task AgentJoin(string agentId, string agentName)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, "agents");
        await Groups.AddToGroupAsync(Context.ConnectionId, $"agent:{agentId}");
        _handoffManager.RegisterAgent(agentId, agentName, Context.ConnectionId);

        // Send pending tickets to new agent
        var pendingTickets = _handoffManager.GetPendingTickets();
        await Clients.Caller.SendAsync("PendingTickets", pendingTickets);

        _logger.LogInformation("Agent {AgentName} ({AgentId}) connected", agentName, agentId);
    }

    /// <summary>
    /// Human agent claims a ticket
    /// </summary>
    public async Task ClaimTicket(string ticketId, string agentId)
    {
        var ticket = _handoffManager.ClaimTicket(ticketId, agentId);

        if (ticket != null)
        {
            // Notify customer that agent has joined
            await Clients.Group($"session:{ticket.SessionId}")
                .SendAsync("AgentJoined", new
                {
                    AgentName = ticket.AssignedAgentName,
                    Message = $"Hi! I'm {ticket.AssignedAgentName} from customer support. I've reviewed your conversation and I'm here to help."
                });

            // Notify all agents that ticket is claimed
            await Clients.Group("agents")
                .SendAsync("TicketClaimed", new { TicketId = ticketId, AgentId = agentId });

            // Send full context to claiming agent
            await Clients.Caller.SendAsync("TicketDetails", ticket);
        }
    }

    /// <summary>
    /// Human agent sends message to customer
    /// </summary>
    public async Task AgentMessage(string ticketId, string message)
    {
        var ticket = _handoffManager.GetTicket(ticketId);

        if (ticket != null)
        {
            await Clients.Group($"session:{ticket.SessionId}")
                .SendAsync("AgentMessage", new
                {
                    AgentName = ticket.AssignedAgentName,
                    Message = message,
                    Timestamp = DateTimeOffset.UtcNow
                });

            // Record in history
            _handoffManager.AddMessage(ticketId, "agent", message);
        }
    }

    /// <summary>
    /// Human agent resolves the ticket
    /// </summary>
    public async Task ResolveTicket(string ticketId, string resolution, string? notes)
    {
        var ticket = _handoffManager.ResolveTicket(ticketId, resolution, notes);

        if (ticket != null)
        {
            // Notify customer
            await Clients.Group($"session:{ticket.SessionId}")
                .SendAsync("ConversationResolved", new
                {
                    Resolution = resolution,
                    Message = "Is there anything else I can help you with today?"
                });

            // Notify agents
            await Clients.Group("agents")
                .SendAsync("TicketResolved", new { TicketId = ticketId });
        }
    }

    /// <summary>
    /// Handle disconnection
    /// </summary>
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _handoffManager.HandleDisconnection(Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }
}
```

### Handoff Manager (In-Memory for Prototyping)

```csharp
/// <summary>
/// Manages handoff tickets and routing between customers and human agents.
/// In-memory implementation for prototyping - replace with Redis/database for production.
/// </summary>
public class HandoffManager
{
    private readonly ConcurrentDictionary<string, HandoffTicket> _tickets = new();
    private readonly ConcurrentDictionary<string, AgentInfo> _agents = new();
    private readonly ConcurrentDictionary<string, string> _sessionToTicket = new();
    private readonly ConcurrentDictionary<string, string> _connectionToSession = new();
    private readonly IHubContext<ChatHub> _hubContext;
    private readonly ILogger<HandoffManager> _logger;

    public HandoffManager(IHubContext<ChatHub> hubContext, ILogger<HandoffManager> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    /// <summary>
    /// Create a new handoff ticket from the orchestrator
    /// </summary>
    public async Task<string> CreateTicketAsync(HumanHandoffRequest request)
    {
        var ticket = new HandoffTicket
        {
            TicketId = request.TicketId,
            SessionId = request.UserContext.SessionId,
            CustomerName = request.UserContext.UserName,
            Summary = request.ConversationSummary,
            EscalationReason = request.EscalationReason,
            ConversationHistory = request.ConversationHistory,
            SuggestedDepartment = request.SuggestedDepartment,
            Status = TicketStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _tickets[ticket.TicketId] = ticket;
        _sessionToTicket[request.UserContext.SessionId] = ticket.TicketId;

        // Notify all connected agents about new ticket
        await _hubContext.Clients.Group("agents")
            .SendAsync("NewTicket", new
            {
                ticket.TicketId,
                ticket.CustomerName,
                ticket.Summary,
                ticket.EscalationReason,
                ticket.SuggestedDepartment,
                ticket.CreatedAt
            });

        _logger.LogInformation("Created handoff ticket {TicketId} for session {SessionId}",
            ticket.TicketId, ticket.SessionId);

        return ticket.TicketId;
    }

    public HandoffTicket? ClaimTicket(string ticketId, string agentId)
    {
        if (!_tickets.TryGetValue(ticketId, out var ticket))
            return null;

        if (ticket.Status != TicketStatus.Pending)
            return null;

        if (!_agents.TryGetValue(agentId, out var agent))
            return null;

        ticket.AssignedAgentId = agentId;
        ticket.AssignedAgentName = agent.Name;
        ticket.Status = TicketStatus.Active;
        ticket.AssignedAt = DateTimeOffset.UtcNow;

        return ticket;
    }

    public HandoffTicket? ResolveTicket(string ticketId, string resolution, string? notes)
    {
        if (!_tickets.TryGetValue(ticketId, out var ticket))
            return null;

        ticket.Status = TicketStatus.Resolved;
        ticket.Resolution = resolution;
        ticket.ResolutionNotes = notes;
        ticket.ResolvedAt = DateTimeOffset.UtcNow;

        // Clean up mappings
        _sessionToTicket.TryRemove(ticket.SessionId, out _);

        return ticket;
    }

    public void RegisterAgent(string agentId, string name, string connectionId)
    {
        _agents[agentId] = new AgentInfo
        {
            AgentId = agentId,
            Name = name,
            ConnectionId = connectionId,
            Status = AgentStatus.Available
        };
    }

    public void RegisterCustomerConnection(string sessionId, string connectionId)
    {
        _connectionToSession[connectionId] = sessionId;
    }

    public HandoffTicket? GetActiveTicket(string sessionId) =>
        _sessionToTicket.TryGetValue(sessionId, out var ticketId) &&
        _tickets.TryGetValue(ticketId, out var ticket) &&
        ticket.Status == TicketStatus.Active
            ? ticket : null;

    public HandoffTicket? GetTicket(string ticketId) =>
        _tickets.TryGetValue(ticketId, out var ticket) ? ticket : null;

    public IEnumerable<HandoffTicket> GetPendingTickets() =>
        _tickets.Values.Where(t => t.Status == TicketStatus.Pending)
            .OrderBy(t => t.CreatedAt);

    public void AddMessage(string ticketId, string role, string content)
    {
        if (_tickets.TryGetValue(ticketId, out var ticket))
        {
            ticket.ConversationHistory.Add(new ConversationMessage
            {
                Role = role,
                Content = content,
                Timestamp = DateTimeOffset.UtcNow
            });
        }
    }

    public void HandleDisconnection(string connectionId)
    {
        // Handle customer disconnection
        if (_connectionToSession.TryRemove(connectionId, out var sessionId))
        {
            _logger.LogInformation("Customer disconnected from session {SessionId}", sessionId);
        }

        // Handle agent disconnection
        var agent = _agents.Values.FirstOrDefault(a => a.ConnectionId == connectionId);
        if (agent != null)
        {
            _agents.TryRemove(agent.AgentId, out _);
            _logger.LogInformation("Agent {AgentId} disconnected", agent.AgentId);
        }
    }
}

public class HandoffTicket
{
    public string TicketId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string? CustomerName { get; set; }
    public string Summary { get; set; } = string.Empty;
    public string EscalationReason { get; set; } = string.Empty;
    public string? SuggestedDepartment { get; set; }
    public TicketStatus Status { get; set; }
    public string? AssignedAgentId { get; set; }
    public string? AssignedAgentName { get; set; }
    public string? Resolution { get; set; }
    public string? ResolutionNotes { get; set; }
    public List<ConversationMessage> ConversationHistory { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? AssignedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
}

public enum TicketStatus { Pending, Active, Resolved, Abandoned }

public class AgentInfo
{
    public string AgentId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ConnectionId { get; set; } = string.Empty;
    public AgentStatus Status { get; set; }
}

public enum AgentStatus { Available, Busy, Away }
```

### Program.cs Setup

```csharp
var builder = WebApplication.CreateBuilder(args);

// Add SignalR
builder.Services.AddSignalR();

// Add chat client (configure your provider)
builder.Services.AddSingleton<IChatClient>(sp =>
{
    // Configure OpenAI, Azure OpenAI, or other provider
    return new OpenAIChatClient("gpt-4o", Environment.GetEnvironmentVariable("OPENAI_API_KEY")!);
});

// Add mock data layer (replace with real CIS integration in production)
builder.Services.AddSingleton<MockCISDatabase>();

// Register agent factories via interfaces (enables testing and swapping implementations)
builder.Services.AddSingleton<IClassifierAgentFactory, ClassifierAgentFactory>();
builder.Services.AddSingleton<IFAQAgentFactory, FAQAgentFactory>();
builder.Services.AddSingleton<IInBandAuthAgentFactory, InBandAuthAgentFactory>();
builder.Services.AddSingleton<IUtilityDataAgentFactory, UtilityDataAgentFactory>();
builder.Services.AddSingleton<ISummarizationAgentFactory, SummarizationAgentFactory>();

// Register session store (use Redis/SQL implementation in production)
builder.Services.AddSingleton<ISessionStore, InMemorySessionStore>();

// Register handoff services
builder.Services.AddSingleton<IHandoffService, HumanHandoffService>();
builder.Services.AddSingleton<HandoffManager>();

// Register orchestrator
builder.Services.AddSingleton<ChatbotOrchestrator>();

var app = builder.Build();

// Map SignalR hub
app.MapHub<ChatHub>("/chat");

app.Run();
```

### Testing Stage 6

```csharp
public class HandoffTests
{
    [Fact]
    public async Task Summarizer_CreatesCompleteSummary()
    {
        // Arrange
        var factory = new SummarizationAgentFactory(_chatClient);
        var agent = factory.CreateSummarizationAgent();
        var session = await agent.CreateSessionAsync();

        var conversation = """
            user: Hi, I need help with my bill
            assistant: I'd be happy to help! Can I get your account number?
            user: It's ACC-2024-0042. My bill is way too high this month!
            assistant: I can see your usage increased significantly. Would you like me to explain the charges?
            user: This is ridiculous! I want to talk to a supervisor about this!
            """;

        // Act
        var response = await agent.RunAsync(
            $"Summarize this conversation:\n{conversation}",
            session);

        // Assert
        Assert.Contains("high bill", response.Text.ToLower());
        Assert.Contains("ACC-2024-0042", response.Text);
        Assert.Contains("frustrated", response.Text.ToLower()); // Should detect mood
    }

    [Fact]
    public async Task Orchestrator_CreatesHandoffTicket()
    {
        // Arrange
        var orchestrator = CreateOrchestrator();
        var sessionId = Guid.NewGuid().ToString();

        // Build some history - Q17: Payment arrangement request
        await orchestrator.ProcessMessageAsync(sessionId, "Hi, I have a problem");
        await orchestrator.ProcessMessageAsync(sessionId, "I can't pay my bill right now and I'm worried about disconnection");

        // Act
        var response = await orchestrator.ProcessMessageAsync(
            sessionId,
            "I need to set up a payment arrangement with someone");

        // Assert
        Assert.Equal(RequiredAction.HumanHandoffInitiated, response.RequiredAction);
        Assert.NotNull(response.TicketId);
    }

    [Fact]
    public async Task Orchestrator_ProcessesHumanResponse()
    {
        // Arrange
        var orchestrator = CreateOrchestrator();
        var sessionId = Guid.NewGuid().ToString();

        // Initiate handoff
        await orchestrator.ProcessMessageAsync(sessionId, "I need to speak to a representative");

        // Simulate human response
        var humanResponse = new HumanResponse
        {
            AgentName = "Maria",
            Message = "Hi, I'm Maria from Billing. I can help you set up a payment arrangement today.",
            Resolution = HandoffResolution.ContinueConversation,
            RespondedAt = DateTimeOffset.UtcNow
        };

        // Act
        var response = await orchestrator.HandleHumanResponseAsync(
            sessionId, humanResponse, CancellationToken.None);

        // Assert
        Assert.Contains("Maria", response.Message);
        Assert.Equal(RequiredAction.HumanConversationActive, response.RequiredAction);
    }

    [Fact]
    public async Task HandoffService_QueuesAndWaitsForResponse()
    {
        // Arrange
        var handoffService = new HumanHandoffService(_queue, _notifications);
        var request = new HumanHandoffRequest
        {
            ConversationSummary = "Test summary",
            OriginalQuestion = "Test question"
        };

        // Act
        var ticketId = await handoffService.CreateHandoffTicketAsync(request, CancellationToken.None);

        // Simulate human response in background
        _ = Task.Run(async () =>
        {
            await Task.Delay(1000);
            await _queue.RespondToTicketAsync(ticketId, new HumanResponse
            {
                Message = "I can help",
                Resolution = HandoffResolution.Resolved
            });
        });

        var response = await handoffService.WaitForHumanResponseAsync(
            ticketId,
            TimeSpan.FromSeconds(5),
            CancellationToken.None);

        // Assert
        Assert.NotNull(response);
        Assert.Equal("I can help", response.Message);
    }
}
```

### Validation Checklist - Stage 6
- [ ] Summarization agent creates complete, structured summaries
- [ ] Summary includes user issue, AI attempts, and escalation reason
- [ ] Summary correctly detects user emotional state
- [ ] Handoff tickets are created with all required information
- [ ] Human agents receive notifications for new tickets
- [ ] Customers receive ticket number and queue position
- [ ] Human responses are delivered to customers
- [ ] Timeout handling works when no human available
- [ ] Conversation history is preserved for human agent review
- [ ] Resolution types are handled correctly (Resolved, Continue, Transfer, Callback)

---

## Stage 7: Session Persistence & Recovery

### Objective
Implement session serialization for persistence across service restarts and for horizontal scaling.

### Implementation

> **Note**: The `ISessionStore` interface is defined in Section 4 (Core Components - Service Abstractions).
> This section shows the serialization format and extended implementation details.

```csharp
/// <summary>
/// Serialization format for persisting ChatSession to storage.
/// Used internally by ISessionStore implementations.
/// </summary>
public class SerializedSession
{
    public string SessionId { get; set; } = string.Empty;
    public UserSessionContext UserContext { get; set; } = new();
    public List<ConversationMessage> ConversationHistory { get; set; } = [];
    public string? PendingQuery { get; set; }
    public string? CurrentHandoffTicketId { get; set; }
    public HandoffState HandoffState { get; set; }
    public JsonElement? ClassifierAgentState { get; set; }
    public JsonElement? FAQAgentState { get; set; }
    public JsonElement? DataAgentState { get; set; }
    public DateTimeOffset LastInteraction { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// Extended orchestrator with session persistence
/// </summary>
public partial class ChatbotOrchestrator
{
    private readonly ISessionStore _sessionStore;

    public async Task<ChatSession> GetOrCreateSessionAsync(
        string sessionId,
        CancellationToken cancellationToken)
    {
        // Try to load from store
        var serialized = await _sessionStore.LoadSessionAsync(sessionId, cancellationToken);

        if (serialized != null)
        {
            return await DeserializeSessionAsync(serialized, cancellationToken);
        }

        // Create new session
        var session = new ChatSession
        {
            SessionId = sessionId,
            UserContext = new UserSessionContext { SessionId = sessionId },
            ConversationHistory = [],
            CreatedAt = DateTimeOffset.UtcNow
        };

        await SaveSessionAsync(session, cancellationToken);
        return session;
    }

    public async Task SaveSessionAsync(
        ChatSession session,
        CancellationToken cancellationToken)
    {
        session.UserContext.LastInteraction = DateTimeOffset.UtcNow;

        var serialized = new SerializedSession
        {
            SessionId = session.SessionId,
            UserContext = session.UserContext,
            ConversationHistory = session.ConversationHistory,
            PendingQuery = session.PendingQuery,
            CurrentHandoffTicketId = session.CurrentHandoffTicketId,
            HandoffState = session.HandoffState,
            LastInteraction = session.UserContext.LastInteraction,
            CreatedAt = session.CreatedAt
        };

        // Serialize agent sessions
        if (session.ClassifierSession != null)
        {
            serialized.ClassifierAgentState =
                _classifierFactory.CreateClassifierAgent().SerializeSession(session.ClassifierSession);
        }

        if (session.FAQSession != null)
        {
            serialized.FAQAgentState =
                _faqFactory.CreateFAQAgent().SerializeSession(session.FAQSession);
        }

        await _sessionStore.SaveSessionAsync(session.SessionId, serialized, cancellationToken);
        _sessionCache[session.SessionId] = session;
    }

    private async Task<ChatSession> DeserializeSessionAsync(
        SerializedSession serialized,
        CancellationToken cancellationToken)
    {
        var session = new ChatSession
        {
            SessionId = serialized.SessionId,
            UserContext = serialized.UserContext,
            ConversationHistory = serialized.ConversationHistory,
            PendingQuery = serialized.PendingQuery,
            CurrentHandoffTicketId = serialized.CurrentHandoffTicketId,
            HandoffState = serialized.HandoffState,
            CreatedAt = serialized.CreatedAt
        };

        // Restore agent sessions
        if (serialized.ClassifierAgentState.HasValue)
        {
            var classifier = _classifierFactory.CreateClassifierAgent();
            session.ClassifierSession = await classifier.DeserializeSessionAsync(
                serialized.ClassifierAgentState.Value);
        }

        if (serialized.FAQAgentState.HasValue)
        {
            var faq = _faqFactory.CreateFAQAgent();
            session.FAQSession = await faq.DeserializeSessionAsync(
                serialized.FAQAgentState.Value);
        }

        _sessionCache[session.SessionId] = session;
        return session;
    }
}

/// <summary>
/// Redis implementation for production use
/// </summary>
public class RedisSessionStore : ISessionStore
{
    private readonly IConnectionMultiplexer _redis;
    private readonly TimeSpan _sessionTtl = TimeSpan.FromHours(24);

    public async Task SaveSessionAsync(
        string sessionId,
        SerializedSession data,
        CancellationToken cancellationToken)
    {
        var db = _redis.GetDatabase();
        var json = JsonSerializer.Serialize(data);
        await db.StringSetAsync($"session:{sessionId}", json, _sessionTtl);
    }

    public async Task<SerializedSession?> LoadSessionAsync(
        string sessionId,
        CancellationToken cancellationToken)
    {
        var db = _redis.GetDatabase();
        var json = await db.StringGetAsync($"session:{sessionId}");

        if (json.IsNullOrEmpty)
            return null;

        return JsonSerializer.Deserialize<SerializedSession>(json!);
    }

    public async Task DeleteSessionAsync(
        string sessionId,
        CancellationToken cancellationToken)
    {
        var db = _redis.GetDatabase();
        await db.KeyDeleteAsync($"session:{sessionId}");
    }

    public async Task<IEnumerable<string>> GetActiveSessionIdsAsync(
        CancellationToken cancellationToken)
    {
        var server = _redis.GetServers().First();
        var keys = server.Keys(pattern: "session:*");
        return keys.Select(k => k.ToString().Replace("session:", ""));
    }
}
```

### Testing Stage 7

```csharp
public class SessionPersistenceTests
{
    [Fact]
    public async Task Session_SerializesAndDeserializes()
    {
        // Arrange
        var orchestrator = CreateOrchestrator();
        var sessionId = Guid.NewGuid().ToString();

        // Build session with history
        await orchestrator.ProcessMessageAsync(sessionId, "Hello");
        await orchestrator.ProcessMessageAsync(sessionId, "What are my payment options?");

        // Authenticate via in-band flow
        await orchestrator.CompleteAuthenticationAsync(
            sessionId,
            accountNumber: "ACC-2024-0042",
            customerName: "Maria Garcia",
            tokenExpiry: DateTimeOffset.UtcNow.AddMinutes(30));

        // Act - Save session
        var session = orchestrator.GetSession(sessionId);
        await orchestrator.SaveSessionAsync(session!, CancellationToken.None);

        // Clear in-memory cache
        orchestrator.ClearCache();

        // Reload session
        var restored = await orchestrator.GetOrCreateSessionAsync(sessionId, CancellationToken.None);

        // Assert
        Assert.Equal(sessionId, restored.SessionId);
        Assert.Equal(2, restored.ConversationHistory.Count);
        Assert.Equal("Maria Garcia", restored.UserContext.CustomerName);
        Assert.Equal(AuthenticationState.Authenticated, restored.UserContext.AuthState);
    }

    [Fact]
    public async Task Session_PreservesAgentState()
    {
        // Arrange
        var orchestrator = CreateOrchestrator();
        var sessionId = Guid.NewGuid().ToString();

        // Build multi-turn context about payment assistance
        await orchestrator.ProcessMessageAsync(sessionId, "Do you have any assistance programs?");
        await orchestrator.ProcessMessageAsync(sessionId, "What if I can't pay my bill?");

        // Save and restore
        var session = orchestrator.GetSession(sessionId);
        await orchestrator.SaveSessionAsync(session!, CancellationToken.None);
        orchestrator.ClearCache();
        await orchestrator.GetOrCreateSessionAsync(sessionId, CancellationToken.None);

        // Act - Continue conversation (should have context)
        var response = await orchestrator.ProcessMessageAsync(
            sessionId,
            "How do I apply for help?");

        // Assert - Should understand context (Q7: LIHEAP)
        Assert.Contains("LIHEAP", response.Message);
    }

    [Fact]
    public async Task Session_SurvivesRestart()
    {
        // Arrange
        var sessionId = Guid.NewGuid().ToString();

        // First orchestrator instance
        using (var orchestrator1 = CreateOrchestrator())
        {
            await orchestrator1.ProcessMessageAsync(sessionId, "Hi, I need help with my bill");
            await orchestrator1.CompleteAuthenticationAsync(
                sessionId,
                accountNumber: "ACC-2024-0099",
                customerName: "Jane Wilson",
                tokenExpiry: DateTimeOffset.UtcNow.AddMinutes(30));

            var session = orchestrator1.GetSession(sessionId);
            await orchestrator1.SaveSessionAsync(session!, CancellationToken.None);
        }

        // Second orchestrator instance (simulating restart)
        using (var orchestrator2 = CreateOrchestrator())
        {
            // Act
            var session = await orchestrator2.GetOrCreateSessionAsync(sessionId, CancellationToken.None);

            // Assert - Session restored
            Assert.Equal("Jane Wilson", session.UserContext.CustomerName);
            Assert.Equal(AuthenticationState.Authenticated, session.UserContext.AuthState);
        }
    }
}
```

### Validation Checklist - Stage 7
- [ ] Sessions serialize to persistent storage correctly
- [ ] Sessions deserialize and restore state correctly
- [ ] Conversation history is preserved across restarts
- [ ] Authentication state is preserved correctly
- [ ] Agent sessions maintain their context after restore
- [ ] Pending queries are preserved and resumable
- [ ] Handoff state survives restarts
- [ ] Session TTL/expiration works correctly
- [ ] Multiple orchestrator instances share session state

---

## Complete Flow Diagram

```
┌───────────────────────────────────────────────────────────────────────────────────────┐
│                                COMPLETE CHATBOT FLOW                                   │
└───────────────────────────────────────────────────────────────────────────────────────┘

User Message
      │
      ▼
┌─────────────────┐
│ Load/Create     │──────────────────────────────────────────────────────┐
│ Session         │                                                       │
└────────┬────────┘                                                       │
         │                                                                │
         ▼                                                                │
┌─────────────────┐                                                       │
│ Classifier      │                                                       │
│ Agent           │                                                       │
└────────┬────────┘                                                       │
         │                                                                │
         ├──────────────┬──────────────┬──────────────┬──────────────┐   │
         ▼              ▼              ▼              ▼              ▼   │
    ┌─────────┐   ┌──────────┐   ┌──────────┐   ┌──────────┐   ┌─────────┤
    │   FAQ   │   │ General  │   │ Account  │   │ Unknown  │   │ Human   │
    │  Agent  │   │Knowledge │   │   Data   │   │   Low    │   │Requested│
    └────┬────┘   └────┬─────┘   └────┬─────┘   │Confidence│   └────┬────┘
         │             │              │         └────┬─────┘        │
         │             │              │              │              │
         │             │              ▼              │              │
         │             │        ┌──────────┐         │              │
         │             │        │  Auth    │         │              │
         │             │        │  Check   │         │              │
         │             │        └────┬─────┘         │              │
         │             │              │              │              │
         │             │         ┌────┴────┐         │              │
         │             │         ▼         ▼         │              │
         │             │    ┌────────┐ ┌────────┐    │              │
         │             │    │ Verify │ │ Data   │    │              │
         │             │    │Identity│ │ Agent  │    │              │
         │             │    └───┬────┘ └───┬────┘    │              │
         │             │        │          │         │              │
         │             │        │    ┌─────┴─────┐   │              │
         │             │        │    │ API / DB  │   │              │
         │             │        │    │   Tools   │   │              │
         │             │        │    └─────┬─────┘   │              │
         │             │        │          │         │              │
         │             │        │          │         │              │
         │             │        ▼          │         ▼              │
         │             │   ┌─────────┐     │    ┌─────────┐         │
         │             │   │  Auth   │     │    │Clarify/ │         │
         │             │   │Callback │     │    │ Retry   │         │
         │             │   └────┬────┘     │    └────┬────┘         │
         │             │        │          │         │              │
         │             │        ▼          │         │              │
         │             │   Resume Query    │         │              │
         │             │        │          │         │              │
         │             │        │          │         │              │
         └─────┬───────┴────────┴──────────┴─────────┤              │
               │                                      │              │
               ▼                                      ▼              ▼
         ┌──────────┐                          ┌───────────────────────┐
         │ Response │                          │   Human Handoff Flow  │
         │  Direct  │                          │                       │
         └────┬─────┘                          │  1. Summarize Conv    │
              │                                │  2. Create Ticket     │
              │                                │  3. Queue for Human   │
              │                                │  4. Wait/Timeout      │
              │                                │  5. Deliver Response  │
              │                                └───────────┬───────────┘
              │                                            │
              └─────────────────┬───────────────────────────┘
                                │
                                ▼
                         ┌──────────┐
                         │  Update  │
                         │ History  │
                         └────┬─────┘
                              │
                              ▼
                         ┌──────────┐
                         │  Save    │
                         │ Session  │
                         └────┬─────┘
                              │
                              ▼
                         ┌──────────┐
                         │ Return   │
                         │ Response │
                         └──────────┘
```

---

## Testing Summary By Stage

| Stage | Focus | Key Tests |
|-------|-------|-----------|
| 1 | Classifier Agent | Category accuracy, confidence scoring, auth detection |
| 2 | FAQ Agent | Knowledge base answers, unknown handling, context |
| 3 | In-Band Auth Agent | Identity lookup, SSN/DOB verification, lockout |
| 4 | Data Agent | Balance/usage fetch, auth guard, MockCIS data access |
| 5 | Orchestrator | Routing logic, auth flow, session management |
| 6 | Human Handoff | WebSocket, summarization, ticket creation, agent routing |
| 7 | Persistence | Serialization, restoration, multi-instance |

---

## Appendix A: Suggested Project Structure

```
UtilityBillingChatbot/
├── src/
│   ├── UtilityBillingChatbot.Core/
│   │   ├── Agents/
│   │   │   ├── ClassifierAgentFactory.cs
│   │   │   ├── FAQAgentFactory.cs
│   │   │   ├── InBandAuthAgentFactory.cs
│   │   │   ├── UtilityDataAgentFactory.cs
│   │   │   └── SummarizationAgentFactory.cs
│   │   ├── Models/
│   │   │   ├── QuestionClassification.cs
│   │   │   ├── UserSessionContext.cs
│   │   │   ├── ChatSession.cs
│   │   │   ├── ChatResponse.cs
│   │   │   ├── HandoffTicket.cs
│   │   │   └── HumanHandoffRequest.cs
│   │   ├── Mock/
│   │   │   ├── MockCISDatabase.cs
│   │   │   ├── UtilityCustomer.cs
│   │   │   ├── BillRecord.cs
│   │   │   └── UsageRecord.cs
│   │   ├── Services/
│   │   │   ├── ChatbotOrchestrator.cs
│   │   │   ├── HandoffManager.cs
│   │   │   └── AuthGuard.cs
│   │   └── Storage/
│   │       ├── ISessionStore.cs
│   │       └── InMemorySessionStore.cs
│   │
│   └── UtilityBillingChatbot.Api/
│       ├── Hubs/
│       │   └── ChatHub.cs
│       └── Program.cs
│
├── tests/
│   ├── UtilityBillingChatbot.Core.Tests/
│   │   ├── Agents/
│   │   │   ├── ClassifierAgentTests.cs
│   │   │   ├── FAQAgentTests.cs
│   │   │   ├── InBandAuthAgentTests.cs
│   │   │   └── UtilityDataAgentTests.cs
│   │   ├── Services/
│   │   │   ├── OrchestratorTests.cs
│   │   │   └── HandoffManagerTests.cs
│   │   └── Storage/
│   │       └── SessionStoreTests.cs
│   │
│   └── UtilityBillingChatbot.Integration.Tests/
│       ├── EndToEndTests.cs
│       └── WebSocketHandoffTests.cs
│
├── data/
│   └── utility_billing_faq.md
│
└── UtilityBillingChatbot.sln
```

---

## Appendix B: Pattern Analysis & Future Considerations

### Current Pattern: Custom Orchestrator with Manual Routing

The current implementation uses a **custom orchestrator class** with manual routing logic:

```
┌─────────────────────────────────────────────────────────────────┐
│                  ChatbotOrchestrator                             │
├─────────────────────────────────────────────────────────────────┤
│  ProcessMessageAsync(sessionId, message)                         │
│      │                                                           │
│      ├─► Check: AuthState == InProgress?                        │
│      │       YES → ContinueAuthenticationFlowAsync()            │
│      │                                                           │
│      ├─► ClassifyQuestionAsync() → QuestionClassification       │
│      │                                                           │
│      └─► switch (classification.Category)                        │
│              BillingFAQ    → HandleBillingFAQAsync()            │
│              AccountData   → HandleAccountDataAsync()            │
│              ServiceRequest→ InitiateHumanHandoffAsync()        │
│              HumanRequested→ InitiateHumanHandoffAsync()        │
│              OutOfScope    → HandleOutOfScopeAsync()            │
└─────────────────────────────────────────────────────────────────┘
```

### Comparison with Framework Patterns

| Aspect | Our Implementation | Framework WorkflowBuilder |
|--------|-------------------|---------------------------|
| **Definition** | Imperative (C# switch statements) | Declarative (graph-based) |
| **Routing** | Manual `switch` in `ProcessMessageAsync` | `AddSwitch()` with predicates |
| **State Management** | `ConcurrentDictionary<string, ChatSession>` | `IWorkflowContext.StateAsync()` |
| **Human-in-Loop** | Custom `AuthAgentSession` + polling | `RequestPort` with `StreamingRun` |
| **Session Persistence** | Manual serialization | Built-in with Durable Tasks |
| **Multi-turn** | Manual state tracking | Automatic with AgentSession |
| **Testability** | Mock each factory | Mock executors |

### Framework Alternative: WorkflowBuilder Pattern

The same chatbot can be implemented using the framework's `WorkflowBuilder` for a more declarative,
maintainable approach. Below is a **complete, runnable implementation**.

#### Step 1: Define Signal and Response Types

```csharp
using Microsoft.Agents.AI.Workflows;

// ========== Signals for RequestPorts ==========

/// <summary>
/// Signal sent to the auth RequestPort when authentication is needed
/// </summary>
public record AuthSignal(
    string OriginalMessage,
    UserSessionContext Context,
    AuthPromptType PromptType);

public enum AuthPromptType
{
    RequestIdentifier,    // Ask for phone/email/account number
    VerifySSN,           // Ask for last 4 SSN
    VerifyDOB,           // Ask for date of birth
    AuthComplete,        // Authentication successful
    AuthFailed           // Too many failures
}

/// <summary>
/// Response from user during authentication flow
/// </summary>
public record AuthResponse(
    string UserInput,
    UserSessionContext UpdatedContext);

/// <summary>
/// Signal sent to human handoff RequestPort
/// </summary>
public record HandoffSignal(
    string ConversationSummary,
    string OriginalQuestion,
    string EscalationReason,
    UserSessionContext Context);

/// <summary>
/// Response from human agent
/// </summary>
public record HandoffResponse(
    string AgentMessage,
    HandoffResolution Resolution);
```

#### Step 2: Implement Executors

```csharp
/// <summary>
/// Classifier executor - entry point for all messages.
/// Outputs QuestionClassification to drive routing.
/// </summary>
public class ClassifierExecutor : Executor<string, QuestionClassification>
{
    private readonly IClassifierAgentFactory _factory;
    private AgentSession? _session;

    public ClassifierExecutor(IClassifierAgentFactory factory) : base("Classifier")
    {
        _factory = factory;
    }

    public override async ValueTask HandleAsync(
        string message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        var agent = _factory.CreateClassifierAgent();

        // Reuse session for conversation context
        _session ??= await agent.CreateSessionAsync();

        var result = await agent.RunAsync<QuestionClassification>(
            message, _session, cancellationToken: cancellationToken);

        // Send classification downstream - WorkflowBuilder routes based on this
        await context.SendMessageAsync(result.Result, cancellationToken: cancellationToken);
    }
}

/// <summary>
/// FAQ executor - handles BillingFAQ questions.
/// Yields output directly (terminal node).
/// </summary>
public class FAQExecutor : Executor<QuestionClassification>
{
    private readonly IFAQAgentFactory _factory;
    private AgentSession? _session;

    public FAQExecutor(IFAQAgentFactory factory) : base("FAQ")
    {
        _factory = factory;
    }

    public override async ValueTask HandleAsync(
        QuestionClassification classification,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        var agent = _factory.CreateFAQAgent();
        _session ??= await agent.CreateSessionAsync();

        // Get the original message from workflow state
        var originalMessage = await context.StateAsync<string>("OriginalMessage", cancellationToken);

        var response = await agent.RunAsync(
            originalMessage ?? classification.Reasoning,
            _session,
            cancellationToken: cancellationToken);

        // Yield output - this is a terminal node
        await context.YieldOutputAsync(response.Text, cancellationToken);
    }
}

/// <summary>
/// Utility Data executor - handles authenticated account queries.
/// Only receives messages after successful authentication.
/// </summary>
public class UtilityDataExecutor : Executor<AuthResponse>
{
    private readonly IUtilityDataAgentFactory _factory;

    public UtilityDataExecutor(IUtilityDataAgentFactory factory) : base("UtilityData")
    {
        _factory = factory;
    }

    public override async ValueTask HandleAsync(
        AuthResponse authResponse,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        // User is now authenticated - create data agent with their context
        var agent = _factory.CreateUtilityDataAgent(authResponse.UpdatedContext);
        var session = await agent.CreateSessionAsync();

        // Get the original query that triggered auth
        var pendingQuery = await context.StateAsync<string>("PendingQuery", cancellationToken);

        var response = await agent.RunAsync(
            pendingQuery ?? "What can I help you with?",
            session,
            cancellationToken: cancellationToken);

        // Yield output - terminal node
        await context.YieldOutputAsync(response.Text, cancellationToken);
    }
}

/// <summary>
/// Summarization executor - prepares conversation summary for human handoff.
/// Outputs HandoffSignal to the handoff RequestPort.
/// </summary>
public class SummarizationExecutor : Executor<QuestionClassification>
{
    private readonly ISummarizationAgentFactory _factory;

    public SummarizationExecutor(ISummarizationAgentFactory factory) : base("Summarization")
    {
        _factory = factory;
    }

    public override async ValueTask HandleAsync(
        QuestionClassification classification,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        var agent = _factory.CreateSummarizationAgent();
        var session = await agent.CreateSessionAsync();

        // Get conversation history from workflow state
        var history = await context.StateAsync<string>("ConversationHistory", cancellationToken);
        var userContext = await context.StateAsync<UserSessionContext>("UserContext", cancellationToken);
        var originalMessage = await context.StateAsync<string>("OriginalMessage", cancellationToken);

        var summaryPrompt = $"""
            Summarize this conversation for handoff:
            Reason: {classification.Category} - {classification.Reasoning}

            {history}
            """;

        var summary = await agent.RunAsync(summaryPrompt, session, cancellationToken: cancellationToken);

        // Send to handoff port
        await context.SendMessageAsync(new HandoffSignal(
            ConversationSummary: summary.Text,
            OriginalQuestion: originalMessage ?? "",
            EscalationReason: classification.Reasoning,
            Context: userContext ?? new UserSessionContext()
        ), cancellationToken: cancellationToken);
    }
}
```

#### Step 3: Build the Workflow

```csharp
/// <summary>
/// Factory that builds the complete chatbot workflow using WorkflowBuilder.
/// This replaces the manual orchestrator with a declarative graph.
/// </summary>
public class UtilityBillingWorkflowFactory
{
    private readonly IClassifierAgentFactory _classifierFactory;
    private readonly IFAQAgentFactory _faqFactory;
    private readonly IUtilityDataAgentFactory _dataFactory;
    private readonly ISummarizationAgentFactory _summarizationFactory;

    public UtilityBillingWorkflowFactory(
        IClassifierAgentFactory classifierFactory,
        IFAQAgentFactory faqFactory,
        IUtilityDataAgentFactory dataFactory,
        ISummarizationAgentFactory summarizationFactory)
    {
        _classifierFactory = classifierFactory;
        _faqFactory = faqFactory;
        _dataFactory = dataFactory;
        _summarizationFactory = summarizationFactory;
    }

    public Workflow BuildChatbotWorkflow()
    {
        // Create executors
        var classifier = new ClassifierExecutor(_classifierFactory);
        var faq = new FAQExecutor(_faqFactory);
        var summarizer = new SummarizationExecutor(_summarizationFactory);
        var dataAgent = new UtilityDataExecutor(_dataFactory);

        // Create RequestPorts for human-in-the-loop interactions
        var authPort = RequestPort.Create<AuthSignal, AuthResponse>("InBandAuth");
        var handoffPort = RequestPort.Create<HandoffSignal, HandoffResponse>("HumanHandoff");

        // Build the workflow graph
        return new WorkflowBuilder(classifier)

            // Route based on classification category
            .AddSwitch(classifier, switchBuilder => switchBuilder
                .AddCase(
                    c => c.Category == QuestionCategory.BillingFAQ,
                    faq)
                .AddCase(
                    c => c.Category == QuestionCategory.AccountData,
                    authPort)  // Needs authentication first
                .AddCase(
                    c => c.Category == QuestionCategory.ServiceRequest,
                    summarizer)  // Goes to human
                .AddCase(
                    c => c.Category == QuestionCategory.HumanRequested,
                    summarizer)  // Goes to human
                .WithDefault(summarizer))  // Unknown -> human

            // Authentication flow with multi-turn loop
            .AddEdge(authPort, dataAgent,
                condition: auth => auth.UpdatedContext.AuthState == AuthenticationState.Authenticated)
            .AddEdge(authPort, authPort,  // Loop back for more auth steps
                condition: auth => auth.UpdatedContext.AuthState == AuthenticationState.InProgress ||
                                   auth.UpdatedContext.AuthState == AuthenticationState.Verifying)
            .AddEdge(authPort, handoffPort,  // Failed auth -> human
                condition: auth => auth.UpdatedContext.AuthState == AuthenticationState.LockedOut)

            // Summarization flows to handoff
            .AddEdge(summarizer, handoffPort)

            // Define output nodes (terminal states)
            .WithOutputFrom(faq, dataAgent, handoffPort)

            .Build();
    }
}
```

#### Step 4: Execute the Workflow with Human-in-the-Loop

```csharp
/// <summary>
/// Service that runs the workflow and handles RequestPort interactions.
/// This replaces the manual orchestrator's ProcessMessageAsync.
/// </summary>
public class WorkflowChatbotService
{
    private readonly UtilityBillingWorkflowFactory _workflowFactory;
    private readonly MockCISDatabase _cisDatabase;
    private readonly IHandoffService _handoffService;

    public WorkflowChatbotService(
        UtilityBillingWorkflowFactory workflowFactory,
        MockCISDatabase cisDatabase,
        IHandoffService handoffService)
    {
        _workflowFactory = workflowFactory;
        _cisDatabase = cisDatabase;
        _handoffService = handoffService;
    }

    /// <summary>
    /// Process a user message through the workflow.
    /// Handles RequestPort events for authentication and human handoff.
    /// </summary>
    public async Task<string> ProcessMessageAsync(
        string sessionId,
        string userMessage,
        UserSessionContext userContext,
        Func<string, Task<string>> getUserInput,  // Callback to get user input
        CancellationToken cancellationToken = default)
    {
        var workflow = _workflowFactory.BuildChatbotWorkflow();

        // Set initial workflow state
        var initialState = new Dictionary<string, object>
        {
            ["OriginalMessage"] = userMessage,
            ["UserContext"] = userContext,
            ["PendingQuery"] = userMessage,
            ["ConversationHistory"] = $"User: {userMessage}"
        };

        // Start workflow execution with streaming
        await using var handle = await InProcessExecution.StreamAsync(
            workflow,
            userMessage,  // Initial input to classifier
            initialState,
            cancellationToken);

        // Process workflow events
        await foreach (var evt in handle.WatchStreamAsync(cancellationToken))
        {
            switch (evt)
            {
                // ===== Authentication RequestPort =====
                case RequestInfoEvent { Request.PortInfo.PortName: "InBandAuth" } authEvent:
                    var authResponse = await HandleAuthRequestAsync(
                        authEvent.Request,
                        userContext,
                        getUserInput,
                        cancellationToken);
                    await handle.SendResponseAsync(authResponse);
                    break;

                // ===== Human Handoff RequestPort =====
                case RequestInfoEvent { Request.PortInfo.PortName: "HumanHandoff" } handoffEvent:
                    var handoffResponse = await HandleHandoffRequestAsync(
                        handoffEvent.Request,
                        sessionId,
                        cancellationToken);
                    await handle.SendResponseAsync(handoffResponse);
                    break;

                // ===== Workflow Complete =====
                case WorkflowOutputEvent outputEvent:
                    return outputEvent.Data?.ToString() ?? "I'm sorry, I couldn't process your request.";
            }
        }

        return "I'm sorry, something went wrong.";
    }

    /// <summary>
    /// Handle authentication RequestPort - implements in-band auth flow.
    /// </summary>
    private async Task<ExternalResponse> HandleAuthRequestAsync(
        ExternalRequest request,
        UserSessionContext context,
        Func<string, Task<string>> getUserInput,
        CancellationToken cancellationToken)
    {
        var authSignal = request.DataAs<AuthSignal>();

        switch (authSignal.PromptType)
        {
            case AuthPromptType.RequestIdentifier:
                var identifier = await getUserInput(
                    "To access your account, please provide the phone number or email on your account:");

                var customer = _cisDatabase.FindByIdentifier(identifier);
                if (customer != null)
                {
                    context.IdentifyingInfo = identifier;
                    context.UserId = customer.AccountNumber;
                    context.UserName = customer.Name;
                    context.AuthState = AuthenticationState.Verifying;

                    // Continue auth - ask verification question
                    return request.CreateResponse(new AuthResponse(identifier, context));
                }
                else
                {
                    return request.CreateResponse(new AuthResponse(
                        "Account not found",
                        context with { AuthState = AuthenticationState.Anonymous }));
                }

            case AuthPromptType.VerifySSN:
                var ssn = await getUserInput(
                    "For security, please provide the last 4 digits of your SSN:");

                var customerForSsn = _cisDatabase.FindByIdentifier(context.IdentifyingInfo!);
                if (customerForSsn != null && ssn == customerForSsn.LastFourSSN)
                {
                    context.AuthState = AuthenticationState.Authenticated;
                    context.AuthenticatedAt = DateTimeOffset.UtcNow;
                    context.SessionExpiry = DateTimeOffset.UtcNow.AddMinutes(30);
                    context.VerifiedFactors.Add("SSN");
                }
                else
                {
                    context.FailedAttempts++;
                    if (context.FailedAttempts >= 3)
                        context.AuthState = AuthenticationState.LockedOut;
                }
                return request.CreateResponse(new AuthResponse(ssn, context));

            case AuthPromptType.AuthComplete:
                return request.CreateResponse(new AuthResponse("Authenticated", context));

            default:
                return request.CreateResponse(new AuthResponse("", context));
        }
    }

    /// <summary>
    /// Handle human handoff RequestPort - creates ticket and waits for agent.
    /// </summary>
    private async Task<ExternalResponse> HandleHandoffRequestAsync(
        ExternalRequest request,
        string sessionId,
        CancellationToken cancellationToken)
    {
        var handoffSignal = request.DataAs<HandoffSignal>();

        // Create handoff ticket
        var handoffRequest = new HumanHandoffRequest
        {
            ConversationSummary = handoffSignal.ConversationSummary,
            OriginalQuestion = handoffSignal.OriginalQuestion,
            EscalationReason = handoffSignal.EscalationReason,
            UserContext = handoffSignal.Context
        };

        var ticketId = await _handoffService.CreateHandoffTicketAsync(
            handoffRequest, cancellationToken);

        // Wait for human response (with timeout)
        var humanResponse = await _handoffService.WaitForHumanResponseAsync(
            ticketId,
            TimeSpan.FromMinutes(5),
            cancellationToken);

        if (humanResponse != null)
        {
            return request.CreateResponse(new HandoffResponse(
                humanResponse.Message,
                humanResponse.Resolution));
        }

        // Timeout - no human available
        return request.CreateResponse(new HandoffResponse(
            "I apologize, but all our representatives are currently busy. " +
            $"Your ticket number is {ticketId}. We'll contact you shortly.",
            HandoffResolution.ScheduleCallback));
    }
}
```

#### Step 5: Program.cs with WorkflowBuilder

```csharp
var builder = WebApplication.CreateBuilder(args);

// Add chat client
builder.Services.AddSingleton<IChatClient>(sp =>
    new OpenAIChatClient("gpt-4o", Environment.GetEnvironmentVariable("OPENAI_API_KEY")!));

// Add mock database
builder.Services.AddSingleton<MockCISDatabase>();

// Register agent factories (same as before)
builder.Services.AddSingleton<IClassifierAgentFactory, ClassifierAgentFactory>();
builder.Services.AddSingleton<IFAQAgentFactory, FAQAgentFactory>();
builder.Services.AddSingleton<IInBandAuthAgentFactory, InBandAuthAgentFactory>();
builder.Services.AddSingleton<IUtilityDataAgentFactory, UtilityDataAgentFactory>();
builder.Services.AddSingleton<ISummarizationAgentFactory, SummarizationAgentFactory>();

// Register services
builder.Services.AddSingleton<ISessionStore, InMemorySessionStore>();
builder.Services.AddSingleton<IHandoffService, HumanHandoffService>();

// Register workflow factory and service (NEW - replaces ChatbotOrchestrator)
builder.Services.AddSingleton<UtilityBillingWorkflowFactory>();
builder.Services.AddSingleton<WorkflowChatbotService>();

var app = builder.Build();

// Example API endpoint using the workflow
app.MapPost("/chat", async (
    ChatRequest request,
    WorkflowChatbotService chatService,
    ISessionStore sessionStore) =>
{
    var session = await sessionStore.GetSessionAsync(request.SessionId)
        ?? new ChatSession { SessionId = request.SessionId };

    var response = await chatService.ProcessMessageAsync(
        request.SessionId,
        request.Message,
        session.UserContext,
        async prompt =>
        {
            // In real app, this would wait for WebSocket message from client
            Console.WriteLine(prompt);
            return Console.ReadLine() ?? "";
        });

    return Results.Ok(new { response });
});

app.Run();

public record ChatRequest(string SessionId, string Message);
```

#### Workflow Visualization

```
                                    ┌─────────────────┐
                                    │  User Message   │
                                    └────────┬────────┘
                                             │
                                             ▼
                                    ┌─────────────────┐
                                    │   Classifier    │
                                    │    Executor     │
                                    └────────┬────────┘
                                             │
                    ┌────────────────────────┼────────────────────────┐
                    │                        │                        │
                    ▼                        ▼                        ▼
           ┌───────────────┐        ┌───────────────┐        ┌───────────────┐
           │  FAQ Executor │        │   AuthPort    │        │ Summarization │
           │   (Output)    │        │ (RequestPort) │        │   Executor    │
           └───────────────┘        └───────┬───────┘        └───────┬───────┘
                                            │                        │
                                   ┌────────┴────────┐               │
                                   │                 │               │
                              Authenticated     InProgress           │
                                   │                 │               │
                                   ▼                 ▼               ▼
                          ┌───────────────┐    (loop back)   ┌───────────────┐
                          │  UtilityData  │                  │  HandoffPort  │
                          │   Executor    │                  │ (RequestPort) │
                          │   (Output)    │                  │   (Output)    │
                          └───────────────┘                  └───────────────┘
```

#### Benefits of WorkflowBuilder Approach

| Aspect | Manual Orchestrator | WorkflowBuilder |
|--------|--------------------|-----------------|
| **Routing Logic** | `switch` statements in code | Declarative `AddSwitch()` |
| **Multi-turn Auth** | Manual `AuthenticationState` tracking | `RequestPort` loops automatically |
| **Human-in-Loop** | Custom polling/WebSocket | `RequestInfoEvent` pattern |
| **Visualization** | Read code to understand flow | Graph can be visualized |
| **Testing** | Mock factories | Mock executors + test workflow |
| **State Management** | Manual `ConcurrentDictionary` | Built-in `IWorkflowContext.StateAsync()` |
| **Durability** | Manual `ISessionStore` | Framework handles with Durable Tasks |

### Drawbacks of Current Approach

#### 1. ~~**No Built-in Persistence/Durability**~~ ✅ ADDRESSED
- ~~Sessions stored in `ConcurrentDictionary` (memory-only)~~
- **Solution**: Added `ISessionStore` interface with `GetSessionAsync`, `SaveSessionAsync`, `DeleteSessionAsync`
- Orchestrator now uses `_sessionCache` backed by `ISessionStore` for persistence
- **Remaining**: Need to implement concrete `ISessionStore` (Redis, SQL, etc.)

#### 2. ~~**Tight Coupling**~~ ✅ ADDRESSED
- ~~Orchestrator directly instantiates agents via factories~~
- **Solution**: All dependencies now use interfaces (`IClassifierAgentFactory`, `IFAQAgentFactory`, etc.)
- Constructor injection enables easy mocking and testing
- **See**: Section 4 "Service Abstractions (Loose Coupling)" in Core Components

#### 3. **Manual State Machine**
- Authentication flow managed via `AuthenticationState` enum + `AuthAgentSession`
- Easy to introduce bugs in state transitions
- No visualization of conversation flow
- **Impact**: Complex debugging, state inconsistencies possible

#### 4. **No Streaming Support**
- Responses are returned as complete messages
- No support for token-by-token streaming to UI
- **Impact**: Poor UX for long responses

#### 5. **Limited Observability**
- No built-in tracing/metrics
- No workflow visualization
- Conversation history stored but not indexed
- **Impact**: Difficult to debug production issues

#### 6. **Single-threaded Processing**
- One message processed at a time per session
- No concurrent agent execution
- **Impact**: Slower for complex queries that could parallelize

#### 7. **No Compensation/Rollback**
- If handoff fails mid-conversation, state is inconsistent
- No saga pattern for multi-step operations
- **Impact**: Potential for orphaned sessions

### Nice-to-Have Additions

#### Immediate Priorities (P0)

1. **Streaming Response Support**
```csharp
public async IAsyncEnumerable<string> ProcessMessageStreamingAsync(
    string sessionId,
    string userMessage,
    [EnumeratorCancellation] CancellationToken ct = default)
{
    // ... classification ...
    await foreach (var token in agent.RunStreamingAsync(message, session, ct))
    {
        yield return token;
    }
}
```

2. **Session Timeout & Cleanup**
```csharp
public class SessionCleanupService : BackgroundService
{
    private readonly ISessionStore _sessionStore;
    private readonly TimeSpan _sessionTimeout = TimeSpan.FromMinutes(30);

    public SessionCleanupService(ISessionStore sessionStore)
    {
        _sessionStore = sessionStore;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var sessionIds = await _sessionStore.GetActiveSessionIdsAsync(ct);

            foreach (var sessionId in sessionIds)
            {
                var session = await _sessionStore.GetSessionAsync(sessionId, ct);
                if (session?.UserContext.LastInteraction < DateTimeOffset.UtcNow - _sessionTimeout)
                {
                    await _sessionStore.DeleteSessionAsync(sessionId, ct);
                }
            }

            await Task.Delay(TimeSpan.FromMinutes(5), ct);
        }
    }
}
```

3. **Retry with Exponential Backoff**
```csharp
private async Task<T> WithRetryAsync<T>(Func<Task<T>> operation, int maxRetries = 3)
{
    for (int i = 0; i < maxRetries; i++)
    {
        try { return await operation(); }
        catch (Exception ex) when (i < maxRetries - 1 && IsTransient(ex))
        {
            await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, i)));
        }
    }
    throw new InvalidOperationException("Max retries exceeded");
}
```

#### Medium Priority (P1)

4. **Conversation Analytics**
```csharp
public class ConversationMetrics
{
    public int TotalMessages { get; set; }
    public Dictionary<QuestionCategory, int> CategoryBreakdown { get; set; }
    public TimeSpan AverageResponseTime { get; set; }
    public int HandoffRate { get; set; }
    public int AuthSuccessRate { get; set; }
    public List<string> TopQuestions { get; set; }
}
```

5. **Proactive Notifications**
```csharp
// Notify customer of outages, payment due dates, etc.
public async Task SendProactiveMessageAsync(
    string accountNumber,
    string message,
    NotificationType type)
{
    var session = await FindSessionByAccountAsync(accountNumber);
    if (session != null)
    {
        await _signalR.Clients.Group($"session:{session.SessionId}")
            .SendAsync("ProactiveMessage", new { message, type });
    }
}
```

6. **Sentiment Analysis Integration**
```csharp
public class SentimentAwareOrchestrator : ChatbotOrchestrator
{
    public override async Task<ChatResponse> ProcessMessageAsync(...)
    {
        var sentiment = await _sentimentAnalyzer.AnalyzeAsync(userMessage);

        // Escalate frustrated customers faster
        if (sentiment.Score < -0.5 && sentiment.Confidence > 0.8)
        {
            return await InitiateHumanHandoffAsync(
                userMessage, session,
                $"Customer appears frustrated (sentiment: {sentiment.Score})",
                cancellationToken);
        }

        return await base.ProcessMessageAsync(...);
    }
}
```

7. **Caching for FAQ Responses**
```csharp
public class CachedFAQAgent
{
    private readonly IDistributedCache _cache;

    public async Task<string> GetAnswerAsync(string question)
    {
        var cacheKey = $"faq:{ComputeHash(question)}";
        var cached = await _cache.GetStringAsync(cacheKey);

        if (cached != null) return cached;

        var answer = await _faqAgent.RunAsync(question, session);
        await _cache.SetStringAsync(cacheKey, answer.Text,
            new DistributedCacheEntryOptions { SlidingExpiration = TimeSpan.FromHours(24) });

        return answer.Text;
    }
}
```

#### Future Enhancements (P2)

8. **Multi-Language Support**
```csharp
public class MultilingualOrchestrator : ChatbotOrchestrator
{
    public override async Task<ChatResponse> ProcessMessageAsync(...)
    {
        var detectedLanguage = await _languageDetector.DetectAsync(userMessage);

        if (detectedLanguage != "en")
        {
            userMessage = await _translator.TranslateToEnglishAsync(userMessage);
        }

        var response = await base.ProcessMessageAsync(sessionId, userMessage, ct);

        if (detectedLanguage != "en")
        {
            response.Message = await _translator.TranslateFromEnglishAsync(
                response.Message, detectedLanguage);
        }

        return response;
    }
}
```

9. **Voice Channel Support (IVR Integration)**
```csharp
public interface IChannelAdapter
{
    Task<string> TranscribeAsync(Stream audioStream);
    Task<Stream> SynthesizeAsync(string text);
}

public class VoiceOrchestrator : ChatbotOrchestrator
{
    public async Task<Stream> ProcessVoiceAsync(Stream audioInput, string sessionId)
    {
        var text = await _channelAdapter.TranscribeAsync(audioInput);
        var response = await ProcessMessageAsync(sessionId, text);
        return await _channelAdapter.SynthesizeAsync(response.Message);
    }
}
```

10. **A/B Testing Framework**
```csharp
public class ABTestingOrchestrator : ChatbotOrchestrator
{
    public override async Task<ChatResponse> ProcessMessageAsync(...)
    {
        var variant = _abTestService.GetVariant(sessionId, "faq-prompt-v2");

        var factory = variant == "control"
            ? _faqFactoryV1
            : _faqFactoryV2;

        var response = await ProcessWithFactory(factory, ...);

        await _abTestService.RecordOutcome(sessionId, response.RequiredAction);

        return response;
    }
}
```

11. **Workflow Visualization Dashboard**
```
┌─────────────────────────────────────────────────────────────┐
│  Session: abc-123  |  Status: Auth In Progress  |  2:34    │
├─────────────────────────────────────────────────────────────┤
│  [Classifier] → AccountData                                 │
│       ↓                                                     │
│  [AuthAgent] → Awaiting DOB verification                   │
│       ↓                                                     │
│  [Pending] "What is my balance?"                           │
└─────────────────────────────────────────────────────────────┘
```

### Migration Path to WorkflowBuilder

If you decide to migrate to the framework's `WorkflowBuilder` pattern:

1. **Phase 1**: Wrap existing agents as `Executor<TIn, TOut>` classes
2. **Phase 2**: Replace `switch` routing with `AddSwitch()`
3. **Phase 3**: Replace manual auth flow with `RequestPort`
4. **Phase 4**: Add `StreamingRun` for real-time updates
5. **Phase 5**: Integrate Durable Tasks for persistence

### Recommendation

For a **prototype/MVP**, the current custom orchestrator approach is acceptable:
- Simpler to understand and debug
- Faster to implement changes
- No additional framework dependencies

For **production deployment**, consider migrating to `WorkflowBuilder`:
- Built-in durability and persistence
- Better observability and tracing
- Declarative workflow definition
- Easier to visualize and test

---

## Appendix C: Dependencies

```xml
<ItemGroup>
  <!-- Core Agent Framework -->
  <PackageReference Include="Microsoft.Extensions.AI" Version="9.0.0" />
  <PackageReference Include="Microsoft.Agents.AI" Version="0.1.0" />

  <!-- AI Provider (choose one) -->
  <PackageReference Include="Microsoft.Extensions.AI.OpenAI" Version="9.0.0" />
  <!-- OR -->
  <PackageReference Include="Microsoft.Extensions.AI.AzureAIInference" Version="9.0.0" />

  <!-- SignalR for real-time WebSocket communication -->
  <PackageReference Include="Microsoft.AspNetCore.SignalR" Version="1.1.0" />

  <!-- Optional: Session persistence (for production) -->
  <PackageReference Include="StackExchange.Redis" Version="2.7.0" />
</ItemGroup>
```

---

## Next Steps

1. **Start with Stage 1** - Build and thoroughly test the Classifier Agent
2. **Proceed sequentially** - Each stage builds on the previous
3. **Validate each stage** - Use the validation checklists before moving on
4. **Create integration tests** - As you complete stages, add integration tests that span multiple components
5. **Consider observability** - Add logging and metrics at each stage
6. **Plan for scale** - Stage 7 enables horizontal scaling

**Recommended Testing Order:**
- Stages 1-4: Unit tests with mock chat client
- Stage 5: Integration tests with in-memory session
- Stage 6: Manual testing with two browser windows (customer + agent)
- Stage 7: Test session persistence across app restarts

This architecture provides a solid foundation for a prototype multi-agent customer support chatbot that can evolve into production.
