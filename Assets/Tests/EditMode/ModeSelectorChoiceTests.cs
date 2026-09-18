using NUnit.Framework;

// The -modeSelector priority chain (issue #124), mirroring DriverSelector's:
// command line over code override over Inspector, with the lenient scan that
// skips junk values. The deeper scan mechanics are CommandLineArgsTests'.
public class ModeSelectorChoiceTests
{
    static readonly string[] NoArgs = new string[0];

    [Test]
    public void InspectorDefault_WinsWhenNothingElseIsSet()
    {
        Assert.That(ModeSelectorChoice.Resolve(NoArgs, null, ModeSelectorKind.None),
            Is.EqualTo(ModeSelectorKind.None));
        Assert.That(ModeSelectorChoice.Resolve(NoArgs, null, ModeSelectorKind.Llm),
            Is.EqualTo(ModeSelectorKind.Llm));
    }

    [Test]
    public void CodeOverride_BeatsTheInspectorDefault()
    {
        Assert.That(ModeSelectorChoice.Resolve(NoArgs, ModeSelectorKind.Fsm, ModeSelectorKind.None),
            Is.EqualTo(ModeSelectorKind.Fsm));
    }

    [Test]
    public void CommandLine_BeatsBothOtherSources()
    {
        string[] args = { "Unity", "-modeSelector", "llm" };
        Assert.That(ModeSelectorChoice.Resolve(args, ModeSelectorKind.Fsm, ModeSelectorKind.Random),
            Is.EqualTo(ModeSelectorKind.Llm),
            "the command line must outrank a code override and the Inspector");
    }

    [TestCase("none", ModeSelectorKind.None)]
    [TestCase("fixed", ModeSelectorKind.Fixed)]
    [TestCase("random", ModeSelectorKind.Random)]
    [TestCase("fsm", ModeSelectorKind.Fsm)]
    [TestCase("LLM", ModeSelectorKind.Llm)] // case is the shell's, not ours
    public void EveryDocumentedKind_Parses(string value, ModeSelectorKind expected)
    {
        ModeSelectorKind inspector = expected == ModeSelectorKind.None
            ? ModeSelectorKind.Llm : ModeSelectorKind.None;
        Assert.That(ModeSelectorChoice.Resolve(new[] { "-modeSelector", value }, null, inspector),
            Is.EqualTo(expected));
    }

    [Test]
    public void UnrecognizedValue_FallsThroughToTheNextSource()
    {
        string[] args = { "-modeSelector", "banana" };
        Assert.That(ModeSelectorChoice.Resolve(args, ModeSelectorKind.Random, ModeSelectorKind.None),
            Is.EqualTo(ModeSelectorKind.Random),
            "a value naming no selector must not consume the selection");
    }

    [Test]
    public void UnrecognizedValue_DoesNotHideALaterValidFlag()
    {
        string[] args = { "-modeSelector", "banana", "-modeSelector", "fsm" };
        Assert.That(ModeSelectorChoice.Resolve(args, null, ModeSelectorKind.None),
            Is.EqualTo(ModeSelectorKind.Fsm));
    }

    [Test]
    public void TrailingFlagWithNoValue_IsIgnored()
    {
        string[] args = { "-batchmode", "-modeSelector" };
        Assert.That(ModeSelectorChoice.Resolve(args, null, ModeSelectorKind.Random),
            Is.EqualTo(ModeSelectorKind.Random));
    }
}
