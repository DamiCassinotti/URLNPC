using System.Threading;
using System.Threading.Tasks;

// The FSM baseline (issue #125): the arm the LLM selector is compared against,
// so it has to be a fair opponent rather than a strawman. Pure rules with the
// snapshot as the only input; the thresholds are serialized on
// ModeSelectorDriver and wired in when it builds the selector.
public class HeuristicModeSelector : IModeSelector
{
    // At or under this own-HP percent, survival outranks everything.
    // Matches the driver's lowHealthFraction event threshold.
    public int LowHealthPercent = 35;

    // A sighting older than this is stale: patrol for the target instead of
    // pursuing the memory. One decision period plus a step; well inside the
    // 10 s perception horizon, past which the snapshot reads never-seen anyway.
    public int UnseenSecondsForPatrol = 6;

    // Priority order, first rule wins. The one case the issue's table leaves
    // open — healthy, unhurt, target remembered but not visible — pursues the
    // memory: Hunt's Advance walks to the last-seen position.
    public NpcMode Decide(GameStateSnapshot snapshot)
    {
        if (snapshot.hpPercent <= LowHealthPercent) return NpcMode.Retreat;
        if (snapshot.targetVisible) return NpcMode.Hunt;
        if (snapshot.recentlyDamaged) return NpcMode.HoldCover;
        if (snapshot.targetDistance == DistanceBucket.None
            || snapshot.secondsSinceSeen >= UnseenSecondsForPatrol)
        {
            return NpcMode.Patrol;
        }
        return NpcMode.Hunt;
    }

    public Task<NpcMode> SelectModeAsync(GameStateSnapshot snapshot, CancellationToken cancellation)
    {
        return Task.FromResult(Decide(snapshot));
    }
}
