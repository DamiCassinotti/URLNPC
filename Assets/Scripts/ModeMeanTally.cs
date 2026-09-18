using System.Text;

// Per-mode running mean of a signed per-step quantity, the float counterpart to
// ModeTally's step counter. Written for the per-mode range delta (#120): visible
// fraction and time-to-kill conflate Hunt and Retreat, since both engage, while
// the net metres a mode moves the range apart them cleanly.
public class ModeMeanTally
{
    readonly int[] steps = new int[NpcModes.All.Length];
    // Doubled: an episode is hundreds of steps of a few centimetres each, and
    // the sum is what the mean and the reported total are read off.
    readonly double[] totals = new double[NpcModes.All.Length];
    readonly int[] eligible = new int[NpcModes.All.Length];
    readonly double[] eligibleTotals = new double[NpcModes.All.Length];

    public int TotalSteps { get; private set; }

    public void Reset()
    {
        System.Array.Clear(steps, 0, steps.Length);
        System.Array.Clear(totals, 0, totals.Length);
        System.Array.Clear(eligible, 0, eligible.Length);
        System.Array.Clear(eligibleTotals, 0, eligibleTotals.Length);
        TotalSteps = 0;
    }

    public void Record(NpcMode mode, float value) => Record(mode, value, true);

    // Unlike ModeTally, an ineligible step still counts into the primary total:
    // the two are reported side by side rather than one narrowing the other
    // (#120). For the range delta the eligible half is the steps the target was
    // visible on, which is in-contact positioning — a pursuit closes most of its
    // range walking to a remembered position, with nobody in sight.
    public void Record(NpcMode mode, float value, bool stepIsEligible)
    {
        int i = (int)mode;
        steps[i]++;
        TotalSteps++;
        totals[i] += value;
        if (!stepIsEligible) return;
        eligible[i]++;
        eligibleTotals[i] += value;
    }

    public int Steps(NpcMode mode) => steps[(int)mode];

    public int EligibleSteps(NpcMode mode) => eligible[(int)mode];

    // Summed over the episode's steps under this mode, i.e. metres of range the
    // mode netted — the scale the seed batch's -5 to -7 m Hunt figures are on.
    public float Total(NpcMode mode) => (float)totals[(int)mode];

    // 0 for a mode this episode never commanded; check Steps to tell that apart
    // from a mode that netted nothing.
    public float Mean(NpcMode mode)
    {
        int n = steps[(int)mode];
        return n > 0 ? (float)(totals[(int)mode] / n) : 0f;
    }

    // The same two figures over eligible steps only.
    public float EligibleTotal(NpcMode mode) => (float)eligibleTotals[(int)mode];

    public float EligibleMean(NpcMode mode)
    {
        int n = eligible[(int)mode];
        return n > 0 ? (float)(eligibleTotals[(int)mode] / n) : 0f;
    }

    // JSONL fragment for the per-episode telemetry event, same shape as
    // ModeTally.Json. Modes the episode never commanded are left out rather than
    // reported as a zero mean. "total" is the precise figure — "mean" is written
    // to three decimals, which on a per-step value of a few centimetres is there
    // to eyeball, not to average.
    public string Json(string objectKey)
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
              .Append(JsonLine.Field("total", Total(mode))).Append(',')
              .Append(JsonLine.Field("mean", Mean(mode))).Append(',')
              .Append(JsonLine.Field("eligible", eligible[(int)mode])).Append(',')
              .Append(JsonLine.Field("eligibleTotal", EligibleTotal(mode))).Append(',')
              .Append(JsonLine.Field("eligibleMean", EligibleMean(mode))).Append('}');
        }
        return sb.Append('}').ToString();
    }
}
