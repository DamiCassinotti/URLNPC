using System.Text;

// Per-mode step counter: of the decision steps spent under each commanded mode,
// how many satisfied some condition. Shared by mode compliance (#45) and the
// target-in-sight fraction (#87), which are read against each other — a low
// compliance rate under a mode whose steps the target was never visible in is
// low engagement rather than a policy that ignores the mode one-hot.
public class ModeTally
{
    readonly int[] steps = new int[NpcModes.All.Length];
    readonly int[] hits = new int[NpcModes.All.Length];

    public int TotalSteps { get; private set; }

    public void Reset()
    {
        System.Array.Clear(steps, 0, steps.Length);
        System.Array.Clear(hits, 0, hits.Length);
        TotalSteps = 0;
    }

    public void Record(NpcMode mode, bool hit)
    {
        int i = (int)mode;
        steps[i]++;
        TotalSteps++;
        if (hit) hits[i]++;
    }

    public int Steps(NpcMode mode) => steps[(int)mode];

    public int Hits(NpcMode mode) => hits[(int)mode];

    // 0 for a mode that was never commanded this episode; check Steps to tell
    // that apart from a mode that hit on no step at all.
    public float Rate(NpcMode mode)
    {
        int n = steps[(int)mode];
        return n > 0 ? (float)hits[(int)mode] / n : 0f;
    }

    // JSONL fragment for the per-episode telemetry event, same shape as
    // EpisodeLog.SidesJson. Modes the episode never commanded are left out
    // rather than reported as a zero rate.
    public string Json(string objectKey, string hitsKey)
    {
        var sb = new StringBuilder(64);
        sb.Append('"').Append(objectKey).Append("\":{");
        bool first = true;
        foreach (NpcMode mode in NpcModes.All)
        {
            if (steps[(int)mode] == 0) continue;
            if (!first) sb.Append(',');
            first = false;
            sb.Append('"').Append(mode.ToString()).Append("\":{")
              .Append(JsonLine.Field("steps", steps[(int)mode])).Append(',')
              .Append(JsonLine.Field(hitsKey, hits[(int)mode])).Append(',')
              .Append(JsonLine.Field("rate", Rate(mode))).Append('}');
        }
        return sb.Append('}').ToString();
    }
}
