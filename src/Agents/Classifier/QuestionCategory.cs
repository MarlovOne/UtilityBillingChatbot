// Copyright (c) Microsoft. All rights reserved.

namespace UtilityBillingChatbot.Agents.Classifier;

/// <summary>
/// Category of a utility billing customer question
/// </summary>
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
