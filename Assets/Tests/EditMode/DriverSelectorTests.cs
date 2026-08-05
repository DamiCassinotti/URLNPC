using NUnit.Framework;
using DriverKind = CombatantRig.DriverKind;

/// <summary>
/// The player-driver priority chain (issue #10): command line beats a
/// code-level override, which beats the Inspector default. Also the leniency
/// the command line scan needs — the editor and player are launched with many
/// unrelated arguments, so a malformed occurrence of the flag must be skipped
/// rather than crash or hijack the selection.
/// </summary>
public class DriverSelectorTests
{
    static readonly string[] NoArgs = new string[0];

    [Test]
    public void InspectorDefault_WinsWhenNothingElseIsSet()
    {
        Assert.That(DriverSelector.Resolve(NoArgs, null, DriverKind.Human), Is.EqualTo(DriverKind.Human));
        Assert.That(DriverSelector.Resolve(NoArgs, null, DriverKind.Agent), Is.EqualTo(DriverKind.Agent));
    }

    [Test]
    public void CodeOverride_BeatsTheInspectorDefault()
    {
        Assert.That(DriverSelector.Resolve(NoArgs, DriverKind.Agent, DriverKind.Human), Is.EqualTo(DriverKind.Agent));
        Assert.That(DriverSelector.Resolve(NoArgs, DriverKind.Human, DriverKind.Agent), Is.EqualTo(DriverKind.Human));
    }

    [Test]
    public void CommandLine_BeatsBothOtherSources()
    {
        string[] args = { "Unity", "-playerDriver", "agent" };
        Assert.That(DriverSelector.Resolve(args, DriverKind.Human, DriverKind.Human), Is.EqualTo(DriverKind.Agent),
            "the command line must outrank a code override and the Inspector");

        string[] human = { "Unity", "-playerDriver", "human" };
        Assert.That(DriverSelector.Resolve(human, DriverKind.Agent, DriverKind.Agent), Is.EqualTo(DriverKind.Human));
    }

    [Test]
    public void CommandLineValue_IsCaseInsensitive()
    {
        string[] args = { "-playerDriver", "AGENT" };
        Assert.That(DriverSelector.Resolve(args, null, DriverKind.Human), Is.EqualTo(DriverKind.Agent));

        string[] mixed = { "-playerDriver", "Human" };
        Assert.That(DriverSelector.Resolve(mixed, null, DriverKind.Agent), Is.EqualTo(DriverKind.Human));
    }

    [Test]
    public void FlagIsFoundAnywhereInTheArgumentList()
    {
        string[] args = { "Unity", "-batchmode", "-projectPath", "/tmp/x", "-playerDriver", "agent", "-logFile", "-" };
        Assert.That(DriverSelector.Resolve(args, null, DriverKind.Human), Is.EqualTo(DriverKind.Agent));
    }

    [Test]
    public void UnrecognizedValue_FallsThroughToTheNextSource()
    {
        string[] args = { "-playerDriver", "banana" };
        Assert.That(DriverSelector.Resolve(args, DriverKind.Agent, DriverKind.Human), Is.EqualTo(DriverKind.Agent),
            "a value naming no real driver must not consume the selection");
        Assert.That(DriverSelector.Resolve(args, null, DriverKind.Human), Is.EqualTo(DriverKind.Human));
    }

    [Test]
    public void UnrecognizedValue_DoesNotHideALaterValidFlag()
    {
        string[] args = { "-playerDriver", "banana", "-playerDriver", "agent" };
        Assert.That(DriverSelector.Resolve(args, null, DriverKind.Human), Is.EqualTo(DriverKind.Agent),
            "the scan must keep looking after a junk value");
    }

    [Test]
    public void FirstValidOccurrence_Wins()
    {
        string[] args = { "-playerDriver", "human", "-playerDriver", "agent" };
        Assert.That(DriverSelector.Resolve(args, null, DriverKind.Agent), Is.EqualTo(DriverKind.Human));
    }

    [Test]
    public void TrailingFlagWithNoValue_IsIgnored()
    {
        string[] args = { "-batchmode", "-playerDriver" };
        Assert.That(DriverSelector.Resolve(args, DriverKind.Agent, DriverKind.Human), Is.EqualTo(DriverKind.Agent),
            "reading past the end of the argument list must not throw or misfire");
    }

    [Test]
    public void DriverNameAppearingAsSomeOtherArgument_IsNotMistakenForTheFlag()
    {
        // A path or run-id that merely contains the word "agent" must not flip
        // the driver — only the value directly after the flag counts.
        string[] args = { "--run-id", "agent", "-logFile", "human" };
        Assert.That(DriverSelector.Resolve(args, null, DriverKind.Human), Is.EqualTo(DriverKind.Human));
    }

    [Test]
    public void EmptyAndNullArguments_AreHandled()
    {
        Assert.That(DriverSelector.Resolve(null, null, DriverKind.Agent), Is.EqualTo(DriverKind.Agent));
        Assert.That(DriverSelector.Resolve(NoArgs, null, DriverKind.Agent), Is.EqualTo(DriverKind.Agent));
        Assert.That(DriverSelector.Resolve(new[] { "-playerDriver", null }, null, DriverKind.Agent), Is.EqualTo(DriverKind.Agent),
            "a null argument value must be skipped, not dereferenced");
    }
}
