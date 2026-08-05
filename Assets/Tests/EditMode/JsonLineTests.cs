using System.Globalization;
using System.Threading;
using NUnit.Framework;
using UnityEngine;

// The JSONL wire format: one line per event, invariant numbers, and escaping
// strong enough that a value can never split a line in two.
public class JsonLineTests
{
    CultureInfo savedCulture;

    [SetUp]
    public void SaveCulture() => savedCulture = Thread.CurrentThread.CurrentCulture;

    [TearDown]
    public void RestoreCulture() => Thread.CurrentThread.CurrentCulture = savedCulture;

    [Test]
    public void Build_EmitsHeaderThenFieldsInOrder()
    {
        string line = JsonLine.Build(12.5f, "2026-08-05T10:00:00Z", 3, "shot",
            JsonLine.Field("shooter", "NPC"),
            JsonLine.Field("hit", true));

        Assert.That(line, Is.EqualTo(
            "{\"t\":12.5,\"wall\":\"2026-08-05T10:00:00Z\",\"episode\":3,\"type\":\"shot\",\"shooter\":\"NPC\",\"hit\":true}"));
    }

    [Test]
    public void Build_WithoutFields_IsStillValidJson()
    {
        Assert.That(JsonLine.Build(0f, "w", 1, "session_end"),
            Is.EqualTo("{\"t\":0,\"wall\":\"w\",\"episode\":1,\"type\":\"session_end\"}"));
        Assert.That(JsonLine.Build(0f, "w", 1, "session_end", null),
            Is.EqualTo("{\"t\":0,\"wall\":\"w\",\"episode\":1,\"type\":\"session_end\"}"));
    }

    [Test]
    public void Numbers_StayInvariant_UnderACommaDecimalLocale()
    {
        Thread.CurrentThread.CurrentCulture = new CultureInfo("es-AR");

        string line = JsonLine.Build(1.5f, "w", 1, "damage",
            JsonLine.Field("amount", 12.25f),
            JsonLine.Field("pos", new Vector3(1.5f, -2.25f, 0f)));

        Assert.That(line, Does.Not.Contain("1,5"), "a comma decimal separator would corrupt every line");
        Assert.That(line, Does.Contain("\"t\":1.5"));
        Assert.That(line, Does.Contain("\"amount\":12.25"));
        Assert.That(line, Does.Contain("\"pos\":[1.5,-2.25,0]"));
    }

    [Test]
    public void Field_FormatsEachType()
    {
        Assert.That(JsonLine.Field("k", "v"), Is.EqualTo("\"k\":\"v\""));
        Assert.That(JsonLine.Field("k", 7), Is.EqualTo("\"k\":7"));
        Assert.That(JsonLine.Field("k", false), Is.EqualTo("\"k\":false"));
        Assert.That(JsonLine.Field("k", 0.5f), Is.EqualTo("\"k\":0.5"));
        Assert.That(JsonLine.Field("k", 1.23456f), Is.EqualTo("\"k\":1.235"), "floats are trimmed to 3 decimals");
    }

    [Test]
    public void Escape_NeutralisesQuotesBackslashesAndLineBreaks()
    {
        Assert.That(JsonLine.Escape("a\"b"), Is.EqualTo("a\\\"b"));
        Assert.That(JsonLine.Escape("a\\b"), Is.EqualTo("a\\\\b"));
        Assert.That(JsonLine.Escape("a\nb\r\tc"), Is.EqualTo("a\\nb\\r\\tc"));
        Assert.That(JsonLine.Escape(null), Is.Empty);
    }

    [Test]
    public void Build_KeepsMultilineValuesOnOneLine()
    {
        string line = JsonLine.Build(0f, "w", 1, "llm_decision",
            JsonLine.Field("reason", "line one\nline two"));

        Assert.That(line, Does.Not.Contain("\n"), "one event per line is the whole file format");
    }
}
