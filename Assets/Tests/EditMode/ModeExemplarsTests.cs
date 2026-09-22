using System.Collections.Generic;
using NUnit.Framework;

// The few-shot exemplar bank (issue #132): the parsing and shot-selection
// rules, and the shipped bank held to what the ablation needs from it. The
// bank is the tuned artifact of this tier, so a check on the asset is a check
// on the condition a result is reported under.
public class ModeExemplarsTests
{
    const string State = "{\"hpPercent\":70,\"targetVisible\":true}";

    static string Entry(string id, string mode) =>
        $"{id}\n{State}\n{{\"mode\":\"{mode}\",\"reason\":\"because\"}}";

    static ModeExemplars Parse(string text)
    {
        Assert.That(ModeExemplars.TryParse("test", text, out ModeExemplars bank, out string error),
            Is.True, error);
        return bank;
    }

    [Test]
    public void ItReadsIdStateAndAnswer_IgnoringCommentsAndBlankLines()
    {
        ModeExemplars bank = Parse("# a header\n\n" + Entry("first", "Hunt") +
            "\n\n# a note\n" + Entry("second", "Patrol") + "\n");

        Assert.That(bank.Count, Is.EqualTo(2));
        Assert.That(bank.Entries[0].Id, Is.EqualTo("first"));
        Assert.That(bank.Entries[0].State, Is.EqualTo(State));
        Assert.That(bank.Entries[0].Mode, Is.EqualTo(NpcMode.Hunt));
        Assert.That(bank.Entries[1].Mode, Is.EqualTo(NpcMode.Patrol));
    }

    // A bank quietly one exemplar short would be a shot count the run's label
    // disagrees with, so a malformed entry fails the whole file.
    [TestCase("lonely\n" + State + "\n", TestName = "MissingAnswer")]
    [TestCase("{\"hpPercent\":70}\n", TestName = "StateWithNoId")]
    [TestCase("", TestName = "Empty")]
    public void AMalformedBank_FailsRatherThanParsingPart(string text)
    {
        Assert.That(ModeExemplars.TryParse("test", text, out ModeExemplars bank, out string error),
            Is.False);
        Assert.That(bank, Is.Null);
        Assert.That(error, Is.Not.Empty);
    }

    // The production parser reads the answers, so an exemplar the selector
    // could not have read is not one the model is taught to imitate.
    [Test]
    public void AnAnswerNamingNoMode_FailsTheBank()
    {
        string text = "odd\n" + State + "\n{\"mode\":\"Flank\",\"reason\":\"because\"}";

        Assert.That(ModeExemplars.TryParse("test", text, out _, out string error), Is.False);
        Assert.That(error, Does.Contain("odd"));
    }

    [Test]
    public void ADuplicateId_FailsTheBank()
    {
        string text = Entry("same", "Hunt") + "\n" + Entry("same", "Patrol");

        Assert.That(ModeExemplars.TryParse("test", text, out _, out string error), Is.False);
        Assert.That(error, Does.Contain("same"));
    }

    // Four shots has to be one of each mode, not four Hunts: the sweep compares
    // bank sizes, and an unbalanced subset would confound size with coverage.
    [Test]
    public void Take_GoesRoundRobinOverTheModes()
    {
        var text = new List<string>();
        foreach (NpcMode mode in NpcModes.All)
        {
            text.Add(Entry(mode + "-1", mode.ToString()));
            text.Add(Entry(mode + "-2", mode.ToString()));
        }
        ModeExemplars bank = Parse(string.Join("\n", text));

        IList<ModeExemplar> four = bank.Take(4);
        Assert.That(four.Count, Is.EqualTo(4));
        CollectionAssert.AreEquivalent(NpcModes.All, Modes(four));

        IList<ModeExemplar> five = bank.Take(5);
        Assert.That(five[4].Id, Is.EqualTo(NpcModes.All[0] + "-2"), "the second round starts over");
    }

    [Test]
    public void Take_WithNoLimitOrTooHighOne_ReturnsTheWholeBank()
    {
        ModeExemplars bank = Parse(Entry("a", "Hunt") + "\n" + Entry("b", "Retreat"));

        Assert.That(bank.Take(0).Count, Is.EqualTo(2));
        Assert.That(bank.Take(99).Count, Is.EqualTo(2));
    }

    [Test]
    public void Render_ShowsEachPairVerbatimUnderOneHeading()
    {
        ModeExemplars bank = Parse(Entry("a", "Hunt") + "\n" + Entry("b", "Retreat"));

        string rendered = bank.Render(1);

        Assert.That(rendered, Does.StartWith(ModeExemplars.Heading));
        Assert.That(rendered, Does.Contain("State: " + State));
        Assert.That(rendered, Does.Contain("\"mode\":\"Hunt\""));
        Assert.That(rendered, Does.Not.Contain("\"mode\":\"Retreat\""), "one shot is one exemplar");
    }

    [Test]
    public void TheShippedBank_LoadsWithEveryModeCoveredMoreThanOnce()
    {
        ModeExemplarLibrary.ClearCache();

        Assert.That(ModeExemplarLibrary.TryLoad(ModeExemplars.DefaultId, out ModeExemplars bank, out string error),
            Is.True, error);
        Assert.That(bank.Id, Is.EqualTo(ModeExemplars.DefaultId));
        foreach (NpcMode mode in NpcModes.All)
        {
            int count = 0;
            foreach (ModeExemplar entry in bank.Entries)
            {
                if (entry.Mode == mode) count++;
            }
            Assert.That(count, Is.GreaterThan(1), $"{mode} is shown {count} time(s)");
        }
    }

    // The bank is curated against battery/snapshots.json; the disjointness
    // check itself lives in scripts/battery.py, which is where the accuracy it
    // protects is computed.
    [Test]
    public void TheShippedBank_CarriesFullSnapshotStates()
    {
        ModeExemplarLibrary.ClearCache();
        ModeExemplarLibrary.TryLoad(ModeExemplars.DefaultId, out ModeExemplars bank, out _);

        string live = ModeDecisionRecord.SnapshotObject(new GameStateSnapshot());
        foreach (ModeExemplar entry in bank.Entries)
        {
            foreach (string field in live.Split(','))
            {
                string key = field.Substring(0, field.IndexOf(':') + 1).TrimStart('{');
                Assert.That(entry.State, Does.Contain(key),
                    $"exemplar '{entry.Id}' does not carry {key} — it must read as a live state does");
            }
        }
    }

    // The arm the ablation shipped has to be a pairing that actually runs: the
    // prompt with the slot, a bank that loads, and enough of it for the shots.
    [Test]
    public void TheShippedDefaults_AreARunnablePairing()
    {
        ModePromptLibrary.ClearCache();
        ModeExemplarLibrary.ClearCache();

        Assert.That(ModePromptLibrary.TryLoad(ModePrompt.FewShotId, out ModePrompt prompt), Is.True);
        Assert.That(prompt.ShowsExemplars, Is.True);
        Assert.That(ModeExemplarLibrary.TryLoad(ModeExemplars.DefaultId, out ModeExemplars bank, out string error),
            Is.True, error);
        Assert.That(bank.Take(ModeExemplars.DefaultShots).Count, Is.EqualTo(ModeExemplars.DefaultShots),
            "the bank has to hold as many exemplars as the shipped shot count shows");
    }

    [Test]
    public void AMissingBank_FailsRatherThanRunningZeroShot()
    {
        ModeExemplarLibrary.ClearCache();

        Assert.That(ModeExemplarLibrary.TryLoad("no-such-bank", out ModeExemplars bank, out string error),
            Is.False);
        Assert.That(bank, Is.Null);
        Assert.That(error, Is.Not.Empty);
    }

    static List<NpcMode> Modes(IList<ModeExemplar> entries)
    {
        var modes = new List<NpcMode>();
        foreach (ModeExemplar entry in entries) modes.Add(entry.Mode);
        return modes;
    }
}
