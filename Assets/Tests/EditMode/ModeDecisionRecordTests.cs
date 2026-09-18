using NUnit.Framework;

// The mode_decision line's fields (issue #126): every field the issue names is
// present, the snapshot serializes as sent, and free text can't break the
// one-line-per-event format.
public class ModeDecisionRecordTests
{
    static ModeDecisionRecord FullRecord()
    {
        return new ModeDecisionRecord
        {
            decisionId = 7,
            entity = "NPC",
            selectorKind = "llm",
            modelName = "llama3.1:8b",
            snapshot = new GameStateSnapshot
            {
                hpPercent = 65,
                targetVisible = true,
                targetDistance = DistanceBucket.Mid,
                secondsSinceSeen = 0,
                recentlyDamaged = true,
                damagedFrom = HitDirection.Left,
                roundSecondsRemaining = 42,
                playerWins = 1,
                npcWins = 2,
                draws = 0,
                arenaIndex = 3,
                arenaName = "Crossfire",
                coverDensity = 0.12f,
                observedSeconds = 8.5f,
                meanEngagementDistanceMetres = 18,
                shotsHeardPer10Seconds = 2.5f,
                observedMeanSpeed = 3.1f,
                mode = NpcMode.Patrol,
                secondsInMode = 9,
            },
            fromMode = NpcMode.Patrol,
            chosenMode = NpcMode.Hunt,
            reason = "target visible at mid range",
            latencyMs = 830,
            parsed = true,
            retryUsed = true,
            fallback = false,
            outcome = ModeDecisionOutcome.Applied,
        };
    }

    [Test]
    public void Fields_CarryEverythingTheIssueNames()
    {
        string line = JsonLine.Build(1f, "wall", 0, "mode_decision", FullRecord().Fields());

        Assert.That(line, Does.Contain("\"entity\":\"NPC\""));
        Assert.That(line, Does.Contain("\"id\":7"));
        Assert.That(line, Does.Contain("\"selector\":\"llm\""));
        Assert.That(line, Does.Contain("\"model\":\"llama3.1:8b\""));
        Assert.That(line, Does.Contain("\"from\":\"Patrol\""));
        Assert.That(line, Does.Contain("\"chosen\":\"Hunt\""));
        Assert.That(line, Does.Contain("\"reason\":\"target visible at mid range\""));
        Assert.That(line, Does.Contain("\"latencyMs\":830"));
        Assert.That(line, Does.Contain("\"parsed\":true"));
        Assert.That(line, Does.Contain("\"retry\":true"));
        Assert.That(line, Does.Contain("\"fallback\":false"));
        Assert.That(line, Does.Contain("\"outcome\":\"applied\""));
    }

    [Test]
    public void SnapshotJson_SerializesTheSnapshotAsSent()
    {
        string json = ModeDecisionRecord.SnapshotJson(FullRecord().snapshot);

        Assert.That(json, Does.StartWith("\"snapshot\":{"));
        Assert.That(json, Does.Contain("\"hpPercent\":65"));
        Assert.That(json, Does.Contain("\"targetVisible\":true"));
        Assert.That(json, Does.Contain("\"targetDistance\":\"Mid\""));
        Assert.That(json, Does.Contain("\"secondsSinceSeen\":0"));
        Assert.That(json, Does.Contain("\"damagedFrom\":\"Left\""));
        Assert.That(json, Does.Contain("\"roundSecondsRemaining\":42"));
        Assert.That(json, Does.Contain("\"arenaName\":\"Crossfire\""));
        Assert.That(json, Does.Contain("\"coverDensity\":0.12"));
        Assert.That(json, Does.Contain("\"meanEngagementDistanceMetres\":18"));
        Assert.That(json, Does.Contain("\"mode\":\"Patrol\""));
        Assert.That(json, Does.Contain("\"secondsInMode\":9"));
    }

    [Test]
    public void ANullSnapshot_SerializesAsNull()
    {
        Assert.That(ModeDecisionRecord.SnapshotJson(null), Is.EqualTo("\"snapshot\":null"));
    }

    [Test]
    public void AFailedDecision_HasNoChosenMode()
    {
        ModeDecisionRecord record = FullRecord();
        record.chosenMode = null;
        record.outcome = ModeDecisionOutcome.Timeout;

        string line = JsonLine.Build(1f, "wall", 0, "mode_decision", record.Fields());
        Assert.That(line, Does.Contain("\"chosen\":\"\""));
        Assert.That(line, Does.Contain("\"outcome\":\"timeout\""));
    }

    [Test]
    public void OutcomeNames_AreStableLowercase()
    {
        Assert.That(ModeDecisionRecord.Name(ModeDecisionOutcome.Applied), Is.EqualTo("applied"));
        Assert.That(ModeDecisionRecord.Name(ModeDecisionOutcome.Invalid), Is.EqualTo("invalid"));
        Assert.That(ModeDecisionRecord.Name(ModeDecisionOutcome.Timeout), Is.EqualTo("timeout"));
        Assert.That(ModeDecisionRecord.Name(ModeDecisionOutcome.Error), Is.EqualTo("error"));
    }

    // A model's free-text reason must not be able to split the line.
    [Test]
    public void AMultilineReason_StaysOnOneLine()
    {
        ModeDecisionRecord record = FullRecord();
        record.reason = "line one\nline \"two\"";

        string line = JsonLine.Build(1f, "wall", 0, "mode_decision", record.Fields());
        Assert.That(line, Does.Not.Contain("\n"));
        Assert.That(line, Does.Contain("line one\\nline \\\"two\\\""));
    }
}
