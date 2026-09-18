using System.Text;

// How one decision ended. Applied is the only outcome that wrote the channel;
// Invalid is an answer naming no mode, Timeout the cancellation at the next
// decision, Error an exception or a selector that cancelled itself.
public enum ModeDecisionOutcome
{
    Applied = 0,
    Invalid = 1,
    Timeout = 2,
    Error = 3,
}

// One selector call as a mode_decision telemetry line (issue #126): the raw
// material the selector summary and the qualitative timeline are read from.
// Engine-free formatting the way ModeTally.Json is; ModeSelectorDriver builds
// one per decision and hands the fields to TelemetryLogger.LogEvent.
public class ModeDecisionRecord
{
    public int decisionId;
    public string entity = "";
    // The resolved kind ("fsm", "llm", ...), or "code" for an assigned
    // instance; the fallback flag below says who actually answered.
    public string selectorKind = "";
    public string modelName = "";
    // As sent — null on a body without a snapshot builder.
    public GameStateSnapshot snapshot;
    // The channel's mode when the decision landed: chosen != from is a switch,
    // which with the ids is the timeline the acceptance asks for.
    public NpcMode fromMode;
    // Null on every outcome but Applied.
    public NpcMode? chosenMode;
    public string reason = "";
    public int latencyMs;
    public bool parsed;
    public bool retryUsed;
    // This call was answered by the fallback selector, not the primary.
    public bool fallback;
    public ModeDecisionOutcome outcome;

    public string[] Fields()
    {
        return new[]
        {
            JsonLine.Field("entity", entity),
            JsonLine.Field("id", decisionId),
            JsonLine.Field("selector", selectorKind),
            JsonLine.Field("model", modelName),
            SnapshotJson(snapshot),
            JsonLine.Field("from", fromMode.ToString()),
            JsonLine.Field("chosen", chosenMode.HasValue ? chosenMode.Value.ToString() : ""),
            JsonLine.Field("reason", reason),
            JsonLine.Field("latencyMs", latencyMs),
            JsonLine.Field("parsed", parsed),
            JsonLine.Field("retry", retryUsed),
            JsonLine.Field("fallback", fallback),
            JsonLine.Field("outcome", Name(outcome)),
        };
    }

    internal static string Name(ModeDecisionOutcome outcome)
    {
        switch (outcome)
        {
            case ModeDecisionOutcome.Applied: return "applied";
            case ModeDecisionOutcome.Invalid: return "invalid";
            case ModeDecisionOutcome.Timeout: return "timeout";
            default: return "error";
        }
    }

    // The snapshot exactly as the selector received it — this is telemetry's
    // own serialization; the prompt's JSON is the prompt issue's.
    public static string SnapshotJson(GameStateSnapshot s)
    {
        if (s == null) return "\"snapshot\":null";
        var sb = new StringBuilder(256);
        sb.Append("\"snapshot\":{")
          .Append(JsonLine.Field("hpPercent", s.hpPercent)).Append(',')
          .Append(JsonLine.Field("targetVisible", s.targetVisible)).Append(',')
          .Append(JsonLine.Field("targetDistance", s.targetDistance.ToString())).Append(',')
          .Append(JsonLine.Field("secondsSinceSeen", s.secondsSinceSeen)).Append(',')
          .Append(JsonLine.Field("recentlyDamaged", s.recentlyDamaged)).Append(',')
          .Append(JsonLine.Field("damagedFrom", s.damagedFrom.ToString())).Append(',')
          .Append(JsonLine.Field("roundSecondsRemaining", s.roundSecondsRemaining)).Append(',')
          .Append(JsonLine.Field("playerWins", s.playerWins)).Append(',')
          .Append(JsonLine.Field("npcWins", s.npcWins)).Append(',')
          .Append(JsonLine.Field("draws", s.draws)).Append(',')
          .Append(JsonLine.Field("arenaIndex", s.arenaIndex)).Append(',')
          .Append(JsonLine.Field("arenaName", s.arenaName)).Append(',')
          .Append(JsonLine.Field("coverDensity", s.coverDensity)).Append(',')
          .Append(JsonLine.Field("observedSeconds", s.observedSeconds)).Append(',')
          .Append(JsonLine.Field("meanEngagementDistanceMetres", s.meanEngagementDistanceMetres)).Append(',')
          .Append(JsonLine.Field("shotsHeardPer10Seconds", s.shotsHeardPer10Seconds)).Append(',')
          .Append(JsonLine.Field("observedMeanSpeed", s.observedMeanSpeed)).Append(',')
          .Append(JsonLine.Field("mode", s.mode.ToString())).Append(',')
          .Append(JsonLine.Field("secondsInMode", s.secondsInMode))
          .Append('}');
        return sb.ToString();
    }
}
