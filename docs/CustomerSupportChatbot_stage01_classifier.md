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