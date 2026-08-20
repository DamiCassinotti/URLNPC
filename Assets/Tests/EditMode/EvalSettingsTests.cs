using NUnit.Framework;

// What a headless eval run was asked for (issue #52). The scan has to be
// lenient about the arguments the player is launched with anyway (-batchmode,
// -logFile, ...) and strict about its own: a mistyped value must fail the run
// rather than quietly evaluate a different condition.
public class EvalSettingsTests
{
    static string[] MinimalRun(params string[] extra)
    {
        var args = new System.Collections.Generic.List<string>
        {
            "URLNPC.x86_64", "-batchmode", "-evalEpisodes", "10", "-evalModel", "EvalModels/eval",
        };
        args.AddRange(extra);
        return args.ToArray();
    }

    [Test]
    public void WithoutTheEpisodesFlag_EvalIsOff()
    {
        EvalSettings settings = EvalSettings.Parse(new[] { "URLNPC.x86_64", "-playerDriver", "agent" });
        Assert.That(settings.Enabled, Is.False);
        Assert.That(settings.Error, Is.Null, "an ordinary play or training session is not an error");
    }

    [Test]
    public void EpisodesAndModel_AreRead()
    {
        EvalSettings settings = EvalSettings.Parse(MinimalRun());
        Assert.That(settings.Enabled, Is.True);
        Assert.That(settings.Error, Is.Null);
        Assert.That(settings.Episodes, Is.EqualTo(10));
        Assert.That(settings.ModelResource, Is.EqualTo("EvalModels/eval"));
    }

    [Test]
    public void Defaults_AreSelfPlayScriptedModesAtGameSpeed()
    {
        EvalSettings settings = EvalSettings.Parse(MinimalRun());
        Assert.That(settings.Subject, Is.EqualTo(EvalSettings.SubjectKind.Policy));
        Assert.That(settings.Opponent, Is.EqualTo(EvalSettings.OpponentKind.Policy));
        Assert.That(settings.ModeSource, Is.EqualTo(EvalSettings.ModeSourceKind.Scripted));
        Assert.That(settings.TimeScale, Is.EqualTo(1f));
    }

    [Test]
    public void Opponent_IsReadCaseInsensitively()
    {
        Assert.That(EvalSettings.Parse(MinimalRun("-evalOpponent", "Heuristic")).Opponent,
            Is.EqualTo(EvalSettings.OpponentKind.Heuristic));
        Assert.That(EvalSettings.Parse(MinimalRun("-evalOpponent", "policy")).Opponent,
            Is.EqualTo(EvalSettings.OpponentKind.Policy));
    }

    // Issue #97: the control runs the model's numbers are read against.
    [Test]
    public void Subject_TakesThePolicyOrEitherControl()
    {
        Assert.That(EvalSettings.Parse(MinimalRun("-evalSubject", "Heuristic")).Subject,
            Is.EqualTo(EvalSettings.SubjectKind.Heuristic));
        Assert.That(EvalSettings.Parse(MinimalRun("-evalSubject", "random")).Subject,
            Is.EqualTo(EvalSettings.SubjectKind.Random));
        Assert.That(EvalSettings.Parse(MinimalRun("-evalSubject", "policy")).Subject,
            Is.EqualTo(EvalSettings.SubjectKind.Policy));
        Assert.That(EvalSettings.Parse(MinimalRun("-evalSubject", "llm")).Error, Is.Not.Null);
    }

    // The two sides are independent: -evalOpponent must not move the subject.
    [Test]
    public void SubjectAndOpponent_AreSetSeparately()
    {
        EvalSettings settings = EvalSettings.Parse(
            MinimalRun("-evalSubject", "random", "-evalOpponent", "heuristic"));
        Assert.That(settings.Error, Is.Null);
        Assert.That(settings.Subject, Is.EqualTo(EvalSettings.SubjectKind.Random));
        Assert.That(settings.Opponent, Is.EqualTo(EvalSettings.OpponentKind.Heuristic));
    }

    [Test]
    public void Modes_TakeTheTwoSelectorsOrOnePinnedMode()
    {
        EvalSettings scripted = EvalSettings.Parse(MinimalRun("-evalModes", "scripted"));
        Assert.That(scripted.ModeSource, Is.EqualTo(EvalSettings.ModeSourceKind.Scripted));

        EvalSettings none = EvalSettings.Parse(MinimalRun("-evalModes", "none"));
        Assert.That(none.ModeSource, Is.EqualTo(EvalSettings.ModeSourceKind.None));

        EvalSettings pinned = EvalSettings.Parse(MinimalRun("-evalModes", "holdcover"));
        Assert.That(pinned.ModeSource, Is.EqualTo(EvalSettings.ModeSourceKind.Fixed));
        Assert.That(pinned.FixedMode, Is.EqualTo(NpcMode.HoldCover));
    }

    [Test]
    public void TimeScale_IsParsedInvariantly()
    {
        // The player inherits whatever locale the machine has; a comma-decimal
        // culture must not turn 2.5 into an error or a 25.
        EvalSettings settings = EvalSettings.Parse(MinimalRun("-evalTimeScale", "2.5"));
        Assert.That(settings.Error, Is.Null);
        Assert.That(settings.TimeScale, Is.EqualTo(2.5f));
    }

    [TestCase("0")]
    [TestCase("-4")]
    [TestCase("many")]
    public void BadEpisodeCount_IsAnError(string value)
    {
        EvalSettings settings = EvalSettings.Parse(new[] { "-evalEpisodes", value, "-evalModel", "m" });
        Assert.That(settings.Enabled, Is.True, "the run was still asked for, so it must fail loudly");
        Assert.That(settings.Error, Is.Not.Null);
    }

    [Test]
    public void BadOpponentOrModeOrTimeScale_IsAnError()
    {
        Assert.That(EvalSettings.Parse(MinimalRun("-evalOpponent", "llm")).Error, Is.Not.Null);
        Assert.That(EvalSettings.Parse(MinimalRun("-evalModes", "sprint")).Error, Is.Not.Null);
        Assert.That(EvalSettings.Parse(MinimalRun("-evalTimeScale", "0")).Error, Is.Not.Null);
    }

    [Test]
    public void MissingModel_IsAnError()
    {
        EvalSettings settings = EvalSettings.Parse(new[] { "-evalEpisodes", "5" });
        Assert.That(settings.Enabled, Is.True);
        Assert.That(settings.Error, Is.Not.Null, "an eval run has to name the policy it scores");
    }

    [Test]
    public void UnrelatedArgumentsAndEdges_AreSkipped()
    {
        Assert.That(EvalSettings.Parse(null).Enabled, Is.False);
        Assert.That(EvalSettings.Parse(new string[0]).Enabled, Is.False);
        // A trailing flag has no value to read; reading past the end must not throw.
        Assert.That(EvalSettings.Parse(new[] { "-batchmode", "-evalEpisodes" }).Enabled, Is.False);
        Assert.That(EvalSettings.Parse(new[] { "-evalEpisodes", null }).Enabled, Is.False,
            "a null argument value must be skipped, not dereferenced");
        // The value only counts directly after the flag.
        Assert.That(EvalSettings.Parse(new[] { "--run-id", "-evalEpisodes" }).Enabled, Is.False);
    }
}
