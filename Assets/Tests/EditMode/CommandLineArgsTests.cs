using NUnit.Framework;

// The shared launch-argument scan behind -runSeed, -playerDriver and
// -trainModes. It has to be lenient about the arguments Unity is launched with
// anyway and never read off the end of the array.
public class CommandLineArgsTests
{
    static bool TryParseInt(string value, out int parsed) => int.TryParse(value, out parsed);

    [Test]
    public void ReadsTheValueAfterTheFlag()
    {
        string[] args = { "Unity", "-batchmode", "-runSeed", "42", "-logFile", "-" };

        Assert.That(CommandLineArgs.TryRead(args, "-runSeed", TryParseInt, out int seed), Is.True);
        Assert.That(seed, Is.EqualTo(42));
    }

    [Test]
    public void AnUnparseableOccurrence_IsSkippedForTheNextOne()
    {
        string[] args = { "-runSeed", "banana", "-runSeed", "7" };

        Assert.That(CommandLineArgs.TryRead(args, "-runSeed", TryParseInt, out int seed), Is.True);
        Assert.That(seed, Is.EqualTo(7));
    }

    [Test]
    public void WithNothingUsable_TheOutputIsLeftAtDefault()
    {
        // A failed parse can write to its out parameter; a caller reading the
        // value on a false return must not see that leftover.
        string[] args = { "-runSeed", "banana" };

        Assert.That(CommandLineArgs.TryRead(args, "-runSeed", TryParseInt, out int seed), Is.False);
        Assert.That(seed, Is.EqualTo(0));
    }

    [Test]
    public void MissingFlagNullsAndEdges_AreSkipped()
    {
        Assert.That(CommandLineArgs.TryRead(null, "-runSeed", TryParseInt, out int _), Is.False);
        Assert.That(CommandLineArgs.TryRead(new string[0], "-runSeed", TryParseInt, out int _), Is.False);
        Assert.That(CommandLineArgs.TryRead(new[] { "-batchmode" }, "-runSeed", TryParseInt, out int _), Is.False);
        // A trailing flag has no value to read; reading past the end must not throw.
        Assert.That(CommandLineArgs.TryRead(new[] { "-batchmode", "-runSeed" }, "-runSeed", TryParseInt, out int _), Is.False);
        Assert.That(CommandLineArgs.TryRead(new[] { "-runSeed", null }, "-runSeed", TryParseInt, out int _), Is.False,
            "a null argument value must be skipped, not handed to the parser");
    }

    [Test]
    public void Contains_SeesAFlagWithNoUsableValue()
    {
        // What tells a bad value apart from an absent flag, so the caller can
        // complain about the one and stay quiet about the other.
        Assert.That(CommandLineArgs.Contains(new[] { "-trainModes", "sprint" }, "-trainModes"), Is.True);
        Assert.That(CommandLineArgs.Contains(new[] { "-trainModes" }, "-trainModes"), Is.True);
        Assert.That(CommandLineArgs.Contains(new[] { "-batchmode" }, "-trainModes"), Is.False);
        Assert.That(CommandLineArgs.Contains(null, "-trainModes"), Is.False);
    }
}
