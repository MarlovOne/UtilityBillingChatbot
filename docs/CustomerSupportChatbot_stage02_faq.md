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
