using System.Threading;
using System.Threading.Tasks;

// Uniform draw over the four modes (issue #125): the sanity floor of the
// selector comparisons. Pure logic with the draw injected, like
// RandomActionPolicy; the default draw is RunRng's own selector stream, so a
// seeded run replays the same mode sequence without shifting the other domains.
public class RandomModeSelector : IModeSelector
{
    // pick(count) returns [0, count).
    readonly System.Func<int, int> pickIndex;

    public RandomModeSelector()
        : this(count => RunRng.Range(RunRng.Stream.Selector, 0, count))
    {
    }

    public RandomModeSelector(System.Func<int, int> pickIndex)
    {
        this.pickIndex = pickIndex;
    }

    public Task<NpcMode> SelectModeAsync(GameStateSnapshot snapshot, CancellationToken cancellation)
    {
        return Task.FromResult(NpcModes.All[pickIndex(NpcModes.All.Length)]);
    }
}
