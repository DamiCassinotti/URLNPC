using System.Collections.Generic;
using NUnit.Framework;

// Prompt v1 (issue #131): the rendering rules, and the shipped asset held to
// what the selector needs from it. The asset is the text a run is scored
// against, so a check on the template is a check on the condition.
public class ModePromptTests
{
    static GameStateSnapshot Snapshot(int hp = 60, bool visible = true,
        DistanceBucket distance = DistanceBucket.Mid, int seen = 0)
    {
        return new GameStateSnapshot
        {
            hpPercent = hp,
            targetVisible = visible,
            targetDistance = distance,
            secondsSinceSeen = seen,
            arenaName = "Courtyard",
            mode = NpcMode.Hunt,
        };
    }

    static ModePrompt Template(string text) => new ModePrompt("test", text);

    [Test]
    public void Render_SubstitutesBothTokens()
    {
        var history = new ModePromptHistory();
        history.Add(Snapshot(hp: 80), NpcMode.Patrol);

        string rendered = Template("H:\n{{HISTORY}}\nS: {{STATE}}").Render(Snapshot(), history.Turns);

        Assert.That(rendered, Does.Not.Contain(ModePrompt.StateToken));
        Assert.That(rendered, Does.Not.Contain(ModePrompt.HistoryToken));
        Assert.That(rendered, Does.Contain("\"hpPercent\":60"), "the current state is the one decided on");
        Assert.That(rendered, Does.Contain("hp 80%"), "the past state is the history's");
        Assert.That(rendered, Does.Contain("-> Patrol"));
    }

    [Test]
    public void WithNoHistory_ItSaysSoRatherThanLeavingTheSectionEmpty()
    {
        string rendered = Template("{{HISTORY}}|{{STATE}}").Render(Snapshot(), new ModePromptHistory().Turns);

        Assert.That(rendered, Does.StartWith(ModePrompt.NoHistory));
    }

    // The memoryless ablation is a variant, not a code path: a template without
    // the token simply never shows the history.
    [Test]
    public void ATemplateWithoutTheHistoryToken_RendersWithoutIt()
    {
        var history = new ModePromptHistory();
        history.Add(Snapshot(), NpcMode.Retreat);

        string rendered = Template("S: {{STATE}}").Render(Snapshot(), history.Turns);

        Assert.That(rendered, Does.Not.Contain("Retreat"));
        Assert.That(rendered, Does.Contain("\"hpPercent\":60"));
    }

    [Test]
    public void ATemplateWithoutTheStateToken_IsUnusable()
    {
        Assert.That(Template("pick a mode: {{HISTORY}}").IsUsable, Is.False);
        Assert.That(Template("pick a mode for {{STATE}}").IsUsable, Is.True);
    }

    [Test]
    public void TheHistory_KeepsTheLastFewTurnsOldestFirst()
    {
        var history = new ModePromptHistory();
        history.Add(Snapshot(hp: 100), NpcMode.Hunt);
        for (int i = 0; i < ModePromptHistory.Capacity; i++)
        {
            history.Add(Snapshot(hp: 90 - i * 10), NpcMode.Patrol);
        }

        Assert.That(history.Turns.Count, Is.EqualTo(ModePromptHistory.Capacity));
        Assert.That(history.Turns[0].Snapshot.hpPercent, Is.EqualTo(90), "the oldest turn should have dropped");
        Assert.That(history.Turns[ModePromptHistory.Capacity - 1].Snapshot.hpPercent,
            Is.EqualTo(90 - (ModePromptHistory.Capacity - 1) * 10));

        history.Clear();
        Assert.That(history.Turns, Is.Empty);
    }

    [Test]
    public void RenderHistory_CountsTurnsBackFromNow()
    {
        var turns = new List<ModePromptTurn>
        {
            new ModePromptTurn(Snapshot(hp: 100, visible: false, distance: DistanceBucket.None, seen: -1), NpcMode.Patrol),
            new ModePromptTurn(Snapshot(hp: 70), NpcMode.Hunt),
        };

        string[] lines = ModePrompt.RenderHistory(turns).Split('\n');

        Assert.That(lines.Length, Is.EqualTo(2));
        Assert.That(lines[0], Does.StartWith("-2 decisions"));
        Assert.That(lines[0], Does.Contain("visible no").And.Contain("dist None").And.Contain("-> Patrol"));
        Assert.That(lines[1], Does.StartWith("-1 decision"));
        Assert.That(lines[1], Does.Contain("hp 70%").And.Contain("-> Hunt"));
    }

    [Test]
    public void TheShippedV1_LoadsAndCarriesBothTokens()
    {
        ModePromptLibrary.ClearCache();

        Assert.That(ModePromptLibrary.TryLoad(ModePrompt.DefaultId, out ModePrompt prompt), Is.True,
            $"Resources/{ModePromptLibrary.ResourceFolder}{ModePrompt.DefaultId}.txt is missing");
        Assert.That(prompt.Id, Is.EqualTo(ModePrompt.DefaultId));
        Assert.That(prompt.Template, Does.Contain(ModePrompt.HistoryToken));
        Assert.That(prompt.IsUsable, Is.True);
    }

    // The catalog is what tells the model what a mode does in this game; a mode
    // the prompt never names is one the model can only guess at.
    [Test]
    public void TheShippedV1_NamesEveryMode()
    {
        ModePromptLibrary.ClearCache();
        ModePromptLibrary.TryLoad(ModePrompt.DefaultId, out ModePrompt prompt);

        foreach (NpcMode mode in NpcModes.All)
        {
            Assert.That(prompt.Template, Does.Contain(mode.ToString()), $"{mode} is not described");
        }
    }

    [Test]
    public void AMissingVariant_FailsRatherThanFallingBackToAnotherPrompt()
    {
        ModePromptLibrary.ClearCache();

        Assert.That(ModePromptLibrary.TryLoad("no-such-variant", out ModePrompt prompt), Is.False);
        Assert.That(prompt, Is.Null);
    }
}
