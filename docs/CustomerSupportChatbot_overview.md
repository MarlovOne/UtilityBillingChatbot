# Utility Billing Customer Support Chatbot - Overview & Core Components

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

## Stage Documents

For detailed implementation of each stage, see:

| Stage | Document | Description |
|-------|----------|-------------|
| 1 | [Stage 1: Classifier](CustomerSupportChatbot_stage01_classifier.md) | Question categorization agent |
| 2 | [Stage 2: FAQ Agent](CustomerSupportChatbot_stage02_faq.md) | Knowledge base Q&A |
| 3 | [Stage 3: Auth Agent](CustomerSupportChatbot_stage03_auth.md) | In-band identity verification |
| 4 | [Stage 4: Data Agent](CustomerSupportChatbot_stage04_data.md) | Account data access |
| 5 | [Stage 5: Orchestrator](CustomerSupportChatbot_stage05_orchestrator.md) | Message routing & session management |
| 6 | [Stage 6: Handoff](CustomerSupportChatbot_stage06_handoff.md) | Human agent escalation |
| 7 | [Stage 7: Persistence](CustomerSupportChatbot_stage07_persistence.md) | Session serialization |

---

## Related Documents

- [Appendices](CustomerSupportChatbot_appendices.md) - Project structure, pattern analysis, dependencies, next steps