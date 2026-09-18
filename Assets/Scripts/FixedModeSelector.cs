using System.Threading;
using System.Threading.Tasks;

// Pins one mode (issue #125): the debugging selector and the per-mode eval
// conditions. Synchronous under the async signature, like the other baselines —
// the driver must not assume latency.
public class FixedModeSelector : IModeSelector
{
    // One task for the lifetime: the answer never changes, so no allocation
    // per decision.
    readonly Task<NpcMode> answer;

    public FixedModeSelector(NpcMode mode)
    {
        answer = Task.FromResult(mode);
    }

    public Task<NpcMode> SelectModeAsync(GameStateSnapshot snapshot, CancellationToken cancellation)
    {
        return answer;
    }
}
