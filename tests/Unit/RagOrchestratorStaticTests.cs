using NUnit.Framework;

namespace ContactsRag.Tests.Unit;

[TestFixture]
public class RagOrchestratorStaticTests
{
    [Test]
    public void ParseYearMonthWithMonthThenYearReturnsCorrectFormat()
    {
        Assert.That(DomainRagOrchestrator.ParseYearMonth("What happened in March 2024?"), Is.EqualTo("2024-03"));
    }

    [Test]
    public void ParseYearMonthWithYearThenMonthReturnsCorrectFormat()
    {
        Assert.That(DomainRagOrchestrator.ParseYearMonth("In 2024 January who talked?"), Is.EqualTo("2024-01"));
    }

    [Test]
    public void ParseYearMonthWithNoMatchReturnsNull()
    {
        Assert.That(DomainRagOrchestrator.ParseYearMonth("Who is Aaron?"), Is.Null);
    }

    [Test]
    public void ParseYearMonthWithDecemberReturnsCorrect()
    {
        Assert.That(DomainRagOrchestrator.ParseYearMonth("December 2024 activity"), Is.EqualTo("2024-12"));
    }

    [Test]
    public void ExtractKeyTermsWithCapitalizedWordsExtractsThem()
    {
        var terms = DomainRagOrchestrator.ExtractKeyTerms("Who mentioned Tower Hamlets?");

        Assert.That(terms, Does.Contain("Tower Hamlets"));
    }

    [Test]
    public void ExtractKeyTermsWithCommonWordsExcludesThem()
    {
        var terms = DomainRagOrchestrator.ExtractKeyTerms("What did they discuss?");

        Assert.That(terms, Is.Empty);
    }

    [Test]
    public void ExtractKeyTermsWithSingleCapitalizedWordExtractsIt()
    {
        var terms = DomainRagOrchestrator.ExtractKeyTerms("Who mentioned Jake?");

        Assert.That(terms, Does.Contain("Jake"));
    }

    [Test]
    public void IsTopicListQuestionMatchesTopicKeywords()
    {
        Assert.That(DomainRagOrchestrator.IsTopicListQuestion("What topics were discussed?"), Is.True);
        Assert.That(DomainRagOrchestrator.IsTopicListQuestion("What did they talk about?"), Is.True);
        Assert.That(DomainRagOrchestrator.IsTopicListQuestion("Who is Aaron?"), Is.False);
    }

    [Test]
    public void IsAggregateQueryMatchesAggregateKeywords()
    {
        Assert.That(DomainRagOrchestrator.IsAggregateQuery("Show all contacts"), Is.True);
        Assert.That(DomainRagOrchestrator.IsAggregateQuery("The busiest day"), Is.True);
        Assert.That(DomainRagOrchestrator.IsAggregateQuery("How many messages total?"), Is.True);
        Assert.That(DomainRagOrchestrator.IsAggregateQuery("What is Lee's name?"), Is.False);
    }

    [Test]
    public void IsPhoneBookIndexQuestionMatchesChatFileKeywords()
    {
        Assert.That(DomainRagOrchestrator.IsPhoneBookIndexQuestion("Name me all the chat file ids"), Is.True);
        Assert.That(DomainRagOrchestrator.IsPhoneBookIndexQuestion("What is Aaron's phone number?"), Is.False);
    }

    [Test]
    public void BuildMetadataFilterWithChatFileCreatesFilter()
    {
        var opts = new RagQueryOptions { ChatFile = "AB9C" };
        var filter = DomainRagOrchestrator.BuildMetadataFilter(opts);

        Assert.That(filter, Is.Not.Null);
        Assert.That(filter![MetadataKeys.ChatFile], Is.EqualTo("AB9C"));
    }

    [Test]
    public void BuildMetadataFilterWithEmptyOptionsReturnsNull()
    {
        var opts = new RagQueryOptions();
        var filter = DomainRagOrchestrator.BuildMetadataFilter(opts);

        Assert.That(filter, Is.Null);
    }

    [Test]
    public void BuildMetadataFilterWithParticipantCreatesFilter()
    {
        var opts = new RagQueryOptions { ParticipantName = "Aaron" };
        var filter = DomainRagOrchestrator.BuildMetadataFilter(opts);

        Assert.That(filter, Is.Not.Null);
        Assert.That(filter![MetadataKeys.Participants], Is.EqualTo("Aaron"));
    }

    [Test]
    public void BuildHyDePromptWithSystemPromptIncludesContext()
    {
        var prompt = DomainRagOrchestrator.BuildHyDePrompt("Who is Lee?", "You analyze forensic data.");

        Assert.That(prompt, Does.Contain("You analyze forensic data."));
        Assert.That(prompt, Does.Contain("Who is Lee?"));
    }

    [Test]
    public void BuildHyDePromptWithEmptySystemPromptOmitsContext()
    {
        var prompt = DomainRagOrchestrator.BuildHyDePrompt("Who is Lee?", "");

        Assert.That(prompt, Does.Not.Contain("Context about the data"));
        Assert.That(prompt, Does.Contain("Who is Lee?"));
    }

    [Test]
    public void ExtractSingleNameCandidateWithLinkingWordExtractsName()
    {
        Assert.That(DomainRagOrchestrator.ExtractSingleNameCandidate("What topics with Aaron?"), Is.EqualTo("Aaron"));
    }

    [Test]
    public void ExtractSingleNameCandidateWithNoLinkingWordReturnsNull()
    {
        Assert.That(DomainRagOrchestrator.ExtractSingleNameCandidate("Who is the owner?"), Is.Null);
    }

    [Test]
    public void IsBusiestDayQueryMatchesKeywords()
    {
        Assert.That(DomainRagOrchestrator.IsBusiestDayQuery("What was the busiest day?"), Is.True);
        Assert.That(DomainRagOrchestrator.IsBusiestDayQuery("Who had the most messages?"), Is.True);
        Assert.That(DomainRagOrchestrator.IsBusiestDayQuery("What is Aaron's number?"), Is.False);
    }

    [Test]
    public void IsSingleChatMaxQueryMatchesKeywords()
    {
        Assert.That(DomainRagOrchestrator.IsSingleChatMaxQuery("In a single chat who had the most messages?"), Is.True);
        Assert.That(DomainRagOrchestrator.IsSingleChatMaxQuery("What was the busiest day?"), Is.False);
    }

    [Test]
    public void IsTotalMessageCountQueryMatchesKeywords()
    {
        Assert.That(DomainRagOrchestrator.IsTotalMessageCountQuery("How many total messages?"), Is.True);
        Assert.That(DomainRagOrchestrator.IsTotalMessageCountQuery("Compare messages between Aaron and Jade"), Is.True);
    }

    [Test]
    public void IsDailyMultiContactQueryMatchesKeywords()
    {
        Assert.That(DomainRagOrchestrator.IsDailyMultiContactQuery("Were there days with more than 3 different contacts?"), Is.True);
        Assert.That(DomainRagOrchestrator.IsDailyMultiContactQuery("Who is Aaron?"), Is.False);
    }

    [Test]
    public void IsMonthlyActivityQueryMatchesKeywords()
    {
        Assert.That(DomainRagOrchestrator.IsMonthlyActivityQuery("Were there months with no chat activity?"), Is.True);
        Assert.That(DomainRagOrchestrator.IsMonthlyActivityQuery("Who is Aaron?"), Is.False);
    }

    [Test]
    public void BuildGroundedContextIncludesSystemPrompt()
    {
        var context = DomainRagOrchestrator.BuildGroundedContext("question", "system prompt text", []);

        Assert.That(context, Does.Contain("system prompt text"));
    }

    [Test]
    public void BuildGroundedContextIncludesPreComputedAggregate()
    {
        var context = DomainRagOrchestrator.BuildGroundedContext("question", "", [], "BUSIEST DAY: 2024-07-17");

        Assert.That(context, Does.Contain("BUSIEST DAY: 2024-07-17"));
        Assert.That(context, Does.Contain("PRE-COMPUTED FORENSIC FACT"));
    }

    [Test]
    public void BuildGroundedContextIncludesExcerpts()
    {
        var results = new List<VectorSearchResult>
        {
            new("id1", 0.9, "excerpt text here", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [MetadataKeys.ChatFile] = "AB9C",
                [MetadataKeys.SessionId] = "session_01",
                [MetadataKeys.SessionDate] = "2024-03-15",
                [MetadataKeys.MessageCount] = "5",
                [MetadataKeys.Participants] = "Alice|Bob"
            })
        };

        var context = DomainRagOrchestrator.BuildGroundedContext("question", "", results);

        Assert.That(context, Does.Contain("chat=AB9C"));
        Assert.That(context, Does.Contain("session=session_01"));
        Assert.That(context, Does.Contain("excerpt text here"));
    }

    [Test]
    public void NeedsPhoneBookContactsMatchesKeywords()
    {
        Assert.That(DomainRagOrchestrator.NeedsPhoneBookContacts("What is the phone number?"), Is.True);
        Assert.That(DomainRagOrchestrator.NeedsPhoneBookContacts("List all contacts"), Is.True);
    }

    [Test]
    public void BuildChatHistoryIncludesHistoryAndQuestion()
    {
        var history = new List<ConversationTurn>
        {
            new("prev question", "prev answer", [])
        };

        var chatHistory = DomainRagOrchestrator.BuildChatHistory("context", history, "new question");

        Assert.That(chatHistory, Has.Count.EqualTo(4));
    }
}
