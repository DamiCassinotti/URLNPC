using System.Text;

// Per-mode step counter: of the decision steps spent under each commanded mode,
// how many satisfied some condition. Shared by mode compliance (#45) and the
// target-in-sight fraction (#87), which are read against each other — a low
// compliance rate under a mode whose steps the target was never visible in is
// low engagement rather than a policy that ignores the mode one-hot.
public class ModeTally
{
    readonly int[] steps = new int[NpcModes.All.Length];
    readonly int[] eligible = new int[NpcModes.All.Length];
    readonly int[] hits = new int[NpcModes.All.Length];

    public int TotalSteps { get; private set; }

    public void Reset()
    {
        System.Array.Clear(steps, 0, steps.Length);
        System.Array.Clear(eligible, 0, eligible.Length);
        System.Array.Clear(hits, 0, hits.Length);
        TotalSteps = 0;
    }

    public void Record(NpcMode mode, bool hit) => Record(mode, hit, true);

    // An ineligible step is one the condition had no chance to hold on (#88);
    // it counts as a step but stays out of the rate's denominator, and its hit
    // is dropped so the count can never exceed the steps it is divided by.
    public void Record(NpcMode mode, bool hit, bool stepIsEligible)
    {
        int i = (int)mode;
        steps[i]++;
        TotalSteps++;
        if (!stepIsEligible) return;
        eligible[i]++;
        if (hit) hits[i]++;
    }

    public int Steps(NpcMode mode) => steps[(int)mode];

    public int EligibleSteps(NpcMode mode) => eligible[(int)mode];

    public int Hits(NpcMode mode) => hits[(int)mode];

    // 0 for a mode that was never commanded this episode, and for one whose
    // steps were all ineligible; check Steps/EligibleSteps to tell those apart
    // from a mode that hit on no step at all.
    public float Rate(NpcMode mode)
    {
        int n = eligible[(int)mode];
        return n > 0 ? (float)hits[(int)mode] / n : 0f;
    }

    // JSONL fragment for the per-episode telemetry event, same shape as
    // EpisodeLog.SidesJson. Modes the episode never commanded are left out
    // rather than reported as a zero rate. "eligible" is always written, and
    // equals "steps" for a tally with no eligibility rule.
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
              .Append(JsonLine.Field("eligible", eligible[(int)mode])).Append(',')
              .Append(JsonLine.Field(hitsKey, hits[(int)mode])).Append(',')
              .Append(JsonLine.Field("rate", Rate(mode))).Append('}');
        }
        return sb.Append('}').ToString();
    }
}
