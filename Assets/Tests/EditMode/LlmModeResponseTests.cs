using NUnit.Framework;

// The defensive half of the LLM selector (issue #130): the request constrains
// decoding to the mode schema, but nothing downstream may assume the answer
// obeyed it. Valid, fenced, prose-wrapped, truncated, empty and
// out-of-vocabulary output all have a defined verdict here.
public class LlmModeResponseTests
{
    static bool Parse(string text, out LlmModeResponse parsed) =>
        LlmModeResponse.TryParse(text, out parsed);

    [Test]
    public void AWellFormedAnswer_YieldsItsModeAndReason()
    {
        Assert.That(Parse("{\"mode\": \"HoldCover\", \"reason\": \"hurt and blind\"}",
            out LlmModeResponse parsed), Is.True);
        Assert.That(parsed.Mode, Is.EqualTo(NpcMode.HoldCover));
        Assert.That(parsed.Reason, Is.EqualTo("hurt and blind"));
    }

    [Test]
    public void TheModeName_IsCaseInsensitive()
    {
        Assert.That(Parse("{\"mode\":\"holdcover\",\"reason\":\"\"}", out LlmModeResponse parsed), Is.True);
        Assert.That(parsed.Mode, Is.EqualTo(NpcMode.HoldCover));
    }

    [Test]
    public void AMissingReason_IsNotAFailure()
    {
        Assert.That(Parse("{\"mode\":\"Patrol\"}", out LlmModeResponse parsed), Is.True);
        Assert.That(parsed.Mode, Is.EqualTo(NpcMode.Patrol));
        Assert.That(parsed.Reason, Is.Empty);
    }

    [Test]
    public void FencesAndProse_AroundTheObject_AreStrippedPast()
    {
        Assert.That(Parse("Sure!\n```json\n{\"mode\": \"Retreat\", \"reason\": \"low hp\"}\n```",
            out LlmModeResponse parsed), Is.True);
        Assert.That(parsed.Mode, Is.EqualTo(NpcMode.Retreat));
        Assert.That(parsed.Reason, Is.EqualTo("low hp"));
    }

    [Test]
    public void EscapesInTheReason_AreUndone()
    {
        Assert.That(Parse("{\"mode\":\"Hunt\",\"reason\":\"he is \\\"there\\\"\\nclose in\"}",
            out LlmModeResponse parsed), Is.True);
        Assert.That(parsed.Reason, Is.EqualTo("he is \"there\"\nclose in"));
    }

    [Test]
    public void ATruncatedObject_StillYieldsTheModeItNamed()
    {
        // Cut off inside the reason: the mode is complete and usable.
        Assert.That(Parse("{\"mode\": \"Retreat\", \"reason\": \"taking fire fro",
            out LlmModeResponse parsed), Is.True);
        Assert.That(parsed.Mode, Is.EqualTo(NpcMode.Retreat));
        Assert.That(parsed.Reason, Is.Empty, "an unterminated reason is no reason");
    }

    [Test]
    public void ATruncatedModeValue_FallsBackToScanningTheText()
    {
        Assert.That(Parse("{\"mode\": \"Patrol", out LlmModeResponse parsed), Is.True);
        Assert.That(parsed.Mode, Is.EqualTo(NpcMode.Patrol));
    }

    [Test]
    public void ABareModeName_IsAccepted()
    {
        Assert.That(Parse("Hunt", out LlmModeResponse parsed), Is.True);
        Assert.That(parsed.Mode, Is.EqualTo(NpcMode.Hunt));
    }

    [Test]
    public void ProseNamingTwoModes_Fails_RatherThanGuessingByWordOrder()
    {
        // No "mode" key to trust, and "the target is retreating, so Hunt" reads
        // the opposite way round to a first-name-wins scan.
        Assert.That(Parse("The target is Retreat-ing from me, so I should Hunt.", out _), Is.False);
    }

    // Both are single-mode texts the scan would otherwise commit.
    [TestCase("I should not Patrol, the target is right there.")]
    [TestCase("The Hunter class would love this.")]
    [TestCase("Anything rather than Retreat.")]
    public void AScannedModeThatIsNegatedOrAFragment_IsNotAnAnswer(string text)
    {
        Assert.That(Parse(text, out _), Is.False);
    }

    [Test]
    public void AUnicodeEscapeInTheReason_IsDecoded()
    {
        Assert.That(Parse("{\"mode\":\"Hunt\",\"reason\":\"close in \\u2014 he is hurt\"}",
            out LlmModeResponse parsed), Is.True);
        Assert.That(parsed.Reason, Is.EqualTo("close in — he is hurt"));
    }

    [TestCase("")]
    [TestCase("   \n ")]
    [TestCase(null)]
    [TestCase("{}")]
    [TestCase("I cannot answer that.")]
    [TestCase("{\"mode\": 2, \"reason\": \"low hp\"}")]
    public void OutputThatNamesNoMode_Fails(string text)
    {
        Assert.That(Parse(text, out _), Is.False);
    }

    [Test]
    public void AnUnknownMode_FailsRatherThanTakingAWordFromTheReason()
    {
        // "hunt" appears in the reason; the key's value is the answer, and it
        // is out of vocabulary.
        Assert.That(Parse("{\"mode\": \"Sprint\", \"reason\": \"hunt him down\"}", out _), Is.False);
    }

    [Test]
    public void TheSchema_EnumeratesEveryModeAndRequiresBothFields()
    {
        string schema = LlmModeResponse.Schema();
        foreach (NpcMode mode in NpcModes.All)
        {
            Assert.That(schema, Does.Contain($"\"{mode}\""), $"{mode} missing from the schema");
        }
        Assert.That(schema, Does.Contain("\"required\":[\"mode\",\"reason\"]"));
    }
}
