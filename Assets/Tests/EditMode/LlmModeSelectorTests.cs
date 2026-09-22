using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

// The LLM selector's rules with the transport faked (issue #130): what the
// request carries, the retry-then-give-up ladder over output that names no
// mode, what a transport failure does, and the request body that goes on the
// wire. Every answer here is already complete, so the whole call finishes
// synchronously — the timeout, whose continuation needs real frames, is
// LlmSelectorRuntimeTests (PlayMode).
public class LlmModeSelectorTests
{
    class FakeEndpoint : ILlmEndpoint
    {
        public readonly List<LlmRequest> Requests = new List<LlmRequest>();
        public readonly List<CancellationToken> Tokens = new List<CancellationToken>();
        public readonly Queue<LlmCompletion> Answers = new Queue<LlmCompletion>();

        public FakeEndpoint Answering(params string[] texts)
        {
            foreach (string text in texts) Answers.Enqueue(LlmCompletion.Answer(text));
            return this;
        }

        public FakeEndpoint Failing(string error)
        {
            Answers.Enqueue(LlmCompletion.Failure(error));
            return this;
        }

        // Burns wall clock inside the call — the budget is a stopwatch, and a
        // fake that answers instantly can never spend it.
        public int DelayMs;

        public Task<LlmCompletion> CompleteAsync(LlmRequest request, CancellationToken cancellation)
        {
            Requests.Add(request);
            Tokens.Add(cancellation);
            if (DelayMs > 0) Thread.Sleep(DelayMs);
            return Task.FromResult(Answers.Count > 0 ? Answers.Dequeue() : LlmCompletion.Answer(""));
        }
    }

    static LlmSelectorConfig Config(int retries = 1)
    {
        return new LlmSelectorConfig
        {
            Endpoint = "http://localhost:11434",
            Model = "test-model",
            TimeoutSeconds = 2f,
            Retries = retries,
            Temperature = 0f,
            Seed = 5,
        };
    }

    // A stand-in variant: the shipped v1 text is ModePromptTests'. What matters
    // here is that both moving parts reach the model.
    static ModePrompt Prompt()
    {
        return new ModePrompt("test-v0", "Modes: Hunt, HoldCover, Retreat, Patrol.\n\n" +
            "{{EXEMPLARS}}\n\nRecent: {{HISTORY}}\nNow: {{STATE}}\nJSON only.");
    }

    static LlmModeSelector Make(FakeEndpoint endpoint, LlmSelectorConfig config)
    {
        return new LlmModeSelector(endpoint, config, Prompt());
    }

    // The same selector with a bank behind the prompt's exemplar slot (#132).
    static LlmModeSelector MakeFewShot(FakeEndpoint endpoint, LlmSelectorConfig config, int shots)
    {
        const string bankText =
            "one\n{\"hpPercent\":100}\n{\"mode\":\"Hunt\",\"reason\":\"press\"}\n\n" +
            "two\n{\"hpPercent\":20}\n{\"mode\":\"Retreat\",\"reason\":\"hurt\"}\n\n" +
            "three\n{\"hpPercent\":50}\n{\"mode\":\"Patrol\",\"reason\":\"lost them\"}";
        Assert.That(ModeExemplars.TryParse("test-bank", bankText, out ModeExemplars bank, out string error),
            Is.True, error);
        config.Shots = shots;
        return new LlmModeSelector(endpoint, config, Prompt(), bank);
    }

    static GameStateSnapshot Snapshot()
    {
        return new GameStateSnapshot
        {
            hpPercent = 40,
            targetVisible = true,
            targetDistance = DistanceBucket.Mid,
            secondsSinceSeen = 0,
            arenaName = "Courtyard",
            mode = NpcMode.Patrol,
        };
    }

    // Every case's answer is already complete, so the task is too by the time
    // SelectModeAsync returns — the assertion protects the Result read below.
    static NpcMode Select(LlmModeSelector selector, ModeDecisionReport report, GameStateSnapshot snapshot = null)
    {
        Task<NpcMode> task = selector.SelectModeAsync(snapshot ?? Snapshot(), report, CancellationToken.None);
        Assert.That(task.IsCompleted, Is.True, "a completed answer must not leave the call pending");
        return task.Result;
    }

    [Test]
    public void AValidAnswer_IsTheModeAndFillsTheReport()
    {
        var endpoint = new FakeEndpoint().Answering("{\"mode\":\"Retreat\",\"reason\":\"badly hurt\"}");
        var report = new ModeDecisionReport();

        NpcMode mode = Select(Make(endpoint, Config()), report);

        Assert.That(mode, Is.EqualTo(NpcMode.Retreat));
        Assert.That(report.Parsed, Is.True);
        Assert.That(report.RetryUsed, Is.False);
        Assert.That(report.Reason, Is.EqualTo("badly hurt"));
        Assert.That(report.ModelName, Is.EqualTo("test-model"));
        Assert.That(report.RawResponse, Does.Contain("Retreat"));
        Assert.That(report.Prompt, Does.Contain("hpPercent"), "the snapshot has to reach the prompt");
        Assert.That(report.PromptId, Is.EqualTo("test-v0"), "the decision has to say which prompt produced it");
    }

    [Test]
    public void TheRequest_CarriesTheModelTemperatureSeedAndSchema()
    {
        var endpoint = new FakeEndpoint().Answering("{\"mode\":\"Hunt\",\"reason\":\"\"}");
        var cancellation = new CancellationTokenSource();

        Make(endpoint, Config()).SelectModeAsync(Snapshot(), cancellation.Token).Wait();

        LlmRequest request = endpoint.Requests[0];
        Assert.That(request.Model, Is.EqualTo("test-model"));
        Assert.That(request.Temperature, Is.EqualTo(0f));
        Assert.That(request.Seed, Is.EqualTo(5));
        Assert.That(request.JsonSchema, Is.EqualTo(LlmModeResponse.Schema()),
            "the schema is the first line of defence against an unusable answer");
        Assert.That(request.TimeoutSeconds, Is.GreaterThan(0));
        Assert.That(endpoint.Tokens[0].CanBeCanceled, Is.True, "the driver's cancellation has to reach the transport");
    }

    [Test]
    public void UnusableOutput_IsRetriedOnce_AndTheSecondAnswerStands()
    {
        var endpoint = new FakeEndpoint().Answering("I'd rather not.", "{\"mode\":\"Patrol\",\"reason\":\"no contact\"}");
        var report = new ModeDecisionReport();

        NpcMode mode = Select(Make(endpoint, Config(retries: 1)), report);

        Assert.That(mode, Is.EqualTo(NpcMode.Patrol));
        Assert.That(endpoint.Requests.Count, Is.EqualTo(2));
        Assert.That(report.RetryUsed, Is.True);
        Assert.That(report.Parsed, Is.True);
        Assert.That(report.RawResponse, Does.Contain("rather not").And.Contain("Patrol"),
            "both attempts belong in the decisions file");
    }

    [Test]
    public void UnusableOutputTwice_AnswersNoMode_SoTheDriverKeepsTheChannel()
    {
        var endpoint = new FakeEndpoint().Answering("no idea", "still no idea");
        var report = new ModeDecisionReport();

        NpcMode mode = Select(Make(endpoint, Config(retries: 1)), report);

        Assert.That(System.Enum.IsDefined(typeof(NpcMode), mode), Is.False,
            "the driver reads an undefined mode as an invalid decision");
        Assert.That(mode, Is.EqualTo(LlmModeSelector.NoMode));
        Assert.That(endpoint.Requests.Count, Is.EqualTo(2), "one retry, not a loop");
        Assert.That(report.Parsed, Is.False);
        Assert.That(report.RetryUsed, Is.True);
    }

    [Test]
    public void AnOutOfVocabularyMode_CountsAsUnusable()
    {
        var endpoint = new FakeEndpoint().Answering("{\"mode\":\"Flank\",\"reason\":\"\"}",
            "{\"mode\":\"HoldCover\",\"reason\":\"\"}");

        NpcMode mode = Select(Make(endpoint, Config(retries: 1)), new ModeDecisionReport());

        Assert.That(mode, Is.EqualTo(NpcMode.HoldCover));
        Assert.That(endpoint.Requests.Count, Is.EqualTo(2));
    }

    [Test]
    public void WithRetriesOff_UnusableOutput_IsNotAskedTwice()
    {
        var endpoint = new FakeEndpoint().Answering("no idea");

        NpcMode mode = Select(Make(endpoint, Config(retries: 0)), new ModeDecisionReport());

        Assert.That(mode, Is.EqualTo(LlmModeSelector.NoMode));
        Assert.That(endpoint.Requests.Count, Is.EqualTo(1));
    }

    // The retry shares the decision's budget rather than getting a fresh one:
    // two full timeouts would outlive the driver's decision period, which
    // cancels the call and reports a timeout over the verdict it was reaching.
    // At temperature 0 the decode is greedy, so an identical prompt returns the
    // identical unusable text and the retry would only spend budget.
    [Test]
    public void TheRetry_AsksSomethingDifferent()
    {
        var endpoint = new FakeEndpoint().Answering("I'd rather not.", "{\"mode\":\"Hunt\",\"reason\":\"\"}");

        Select(Make(endpoint, Config(retries: 1)), new ModeDecisionReport());

        Assert.That(endpoint.Requests[1].Prompt, Is.Not.EqualTo(endpoint.Requests[0].Prompt));
        Assert.That(endpoint.Requests[1].Prompt, Does.Contain("rather not"),
            "the retry says what was wrong with the last answer");
        Assert.That(endpoint.Requests[1].Seed, Is.Not.EqualTo(endpoint.Requests[0].Seed));
    }

    [Test]
    public void AnUnusableAnswerThatSpentTheBudget_IsNotRetried()
    {
        // Slow enough to spend the whole budget, and answers synchronously, so
        // the attempt itself never trips the timeout.
        var endpoint = new FakeEndpoint { DelayMs = 120 }
            .Answering("no idea", "{\"mode\":\"Hunt\",\"reason\":\"\"}");
        LlmSelectorConfig config = Config(retries: 1);
        config.TimeoutSeconds = 0.1f;

        NpcMode mode = Select(Make(endpoint, config), new ModeDecisionReport());

        Assert.That(endpoint.Requests.Count, Is.EqualTo(1),
            "a retry the driver would cancel mid-flight is not worth issuing");
        Assert.That(mode, Is.EqualTo(LlmModeSelector.NoMode));
    }

    [Test]
    public void ATransportFailure_Throws_AndIsNotRetried()
    {
        var endpoint = new FakeEndpoint().Failing("connection refused");
        var report = new ModeDecisionReport();

        Task<NpcMode> task = Make(endpoint, Config(retries: 1))
            .SelectModeAsync(Snapshot(), report, CancellationToken.None);

        Assert.That(task.IsFaulted, Is.True, "a dead server is a failure to report, not a mode");
        Assert.That(task.Exception.GetBaseException().Message, Does.Contain("connection refused"));
        Assert.That(endpoint.Requests.Count, Is.EqualTo(1),
            "the retry is for unusable output, not for a broken transport");
    }

    [Test]
    public void WithNoSnapshot_ItFailsInsteadOfPromptingWithNothing()
    {
        var endpoint = new FakeEndpoint().Answering("{\"mode\":\"Hunt\",\"reason\":\"\"}");

        Task<NpcMode> task = Make(endpoint, Config())
            .SelectModeAsync(null, new ModeDecisionReport(), CancellationToken.None);

        Assert.That(task.IsFaulted, Is.True);
        Assert.That(endpoint.Requests, Is.Empty);
    }

    [Test]
    public void ThePrompt_CarriesTheStateItIsDecidingOn()
    {
        var report = new ModeDecisionReport();

        Select(Make(new FakeEndpoint().Answering("{\"mode\":\"Hunt\",\"reason\":\"\"}"), Config()), report);

        Assert.That(report.Prompt, Does.Contain("\"hpPercent\":40"));
        Assert.That(report.Prompt, Does.Not.Contain("\"snapshot\""),
            "the state goes in as the flat object the prompt describes");
        Assert.That(report.Prompt, Does.Contain("Courtyard"));
        Assert.That(report.Prompt, Does.Not.Contain(ModePrompt.StateToken), "the template was not rendered");
    }

    // The few-shot arm (#132): the examples reach the model, and the decision
    // line says which bank and how many shots were shown — a few-shot run
    // labelled as the zero-shot one it is compared against would be worthless.
    [Test]
    public void WithABank_TheExamplesGoIntoThePrompt_AndTheLabelSaysSo()
    {
        var endpoint = new FakeEndpoint().Answering("{\"mode\":\"Hunt\",\"reason\":\"\"}");
        var report = new ModeDecisionReport();

        Select(MakeFewShot(endpoint, Config(), shots: 2), report);

        Assert.That(report.Prompt, Does.Contain(ModeExemplars.Heading));
        Assert.That(report.Prompt, Does.Contain("\"hpPercent\":100"), "the example state is shown");
        Assert.That(report.Prompt, Does.Not.Contain(ModePrompt.ExemplarsToken));
        Assert.That(report.PromptId, Is.EqualTo("test-v0+test-bankx2"));
    }

    [Test]
    public void WithNoBank_TheSlotCollapses_AndTheLabelIsThePromptAlone()
    {
        var endpoint = new FakeEndpoint().Answering("{\"mode\":\"Hunt\",\"reason\":\"\"}");
        var report = new ModeDecisionReport();

        Select(Make(endpoint, Config()), report);

        Assert.That(report.Prompt, Does.Not.Contain(ModeExemplars.Heading));
        Assert.That(report.Prompt, Does.Not.Contain(ModePrompt.ExemplarsToken));
        Assert.That(report.PromptId, Is.EqualTo("test-v0"));
    }

    // The history is the whole basis for reading a trend across calls (#131):
    // the mode a call chose has to show up in the next call's prompt.
    [Test]
    public void EachAnswer_JoinsTheHistoryTheNextPromptCarries()
    {
        var endpoint = new FakeEndpoint().Answering(
            "{\"mode\":\"Retreat\",\"reason\":\"hurt\"}",
            "{\"mode\":\"Hunt\",\"reason\":\"healthy\"}");
        LlmModeSelector selector = Make(endpoint, Config());

        Select(selector, new ModeDecisionReport());
        Select(selector, new ModeDecisionReport());

        Assert.That(endpoint.Requests[0].Prompt, Does.Contain(ModePrompt.NoHistory),
            "the first decision of a round has no history to show");
        Assert.That(endpoint.Requests[1].Prompt, Does.Contain("-> Retreat"));
        Assert.That(endpoint.Requests[1].Prompt, Does.Not.Contain(ModePrompt.NoHistory));
    }

    [Test]
    public void ResetState_DropsTheHistory_SoARoundDoesNotInheritTheLastOne()
    {
        var endpoint = new FakeEndpoint().Answering(
            "{\"mode\":\"Retreat\",\"reason\":\"\"}", "{\"mode\":\"Hunt\",\"reason\":\"\"}");
        LlmModeSelector selector = Make(endpoint, Config());

        Select(selector, new ModeDecisionReport());
        selector.ResetState();
        Select(selector, new ModeDecisionReport());

        Assert.That(endpoint.Requests[1].Prompt, Does.Contain(ModePrompt.NoHistory));
    }

    // An answer the driver stopped waiting for was never commanded; telling the
    // next prompt about it would describe a decision that never happened.
    [Test]
    public void AnAnswerToACancelledCall_StaysOutOfTheHistory()
    {
        var endpoint = new FakeEndpoint().Answering(
            "{\"mode\":\"Retreat\",\"reason\":\"\"}", "{\"mode\":\"Hunt\",\"reason\":\"\"}");
        LlmModeSelector selector = Make(endpoint, Config());
        var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        selector.SelectModeAsync(Snapshot(), new ModeDecisionReport(), cancelled.Token).Wait();
        Select(selector, new ModeDecisionReport());

        Assert.That(endpoint.Requests[1].Prompt, Does.Contain(ModePrompt.NoHistory));
    }

    [Test]
    public void TheRequestBody_EmbedsTheSchemaAsJson_NotAsAString()
    {
        string body = OllamaEndpoint.BuildBody(new LlmRequest
        {
            Model = "test-model",
            Prompt = "pick a mode\nnow",
            Temperature = 0.7f,
            Seed = 5,
            JsonSchema = LlmModeResponse.Schema(),
        });

        Assert.That(body, Does.Contain("\"format\":{\"type\":\"object\""));
        Assert.That(body, Does.Contain("\"stream\":false"));
        Assert.That(body, Does.Contain("\"keep_alive\""), "a model unloaded between episodes costs a decision");
        Assert.That(body, Does.Contain("\"temperature\":0.7"));
        Assert.That(body, Does.Contain("\"seed\":5"));
        Assert.That(body, Does.Contain("pick a mode\\nnow"), "the prompt's newline must not split the body");
        Assert.That(body.Split('\n').Length, Is.EqualTo(1));
    }
}
