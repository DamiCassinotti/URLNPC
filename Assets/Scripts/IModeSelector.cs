using System.Threading;
using System.Threading.Tasks;

// The seam every mode selector implements (issue #124): given the bucketed
// game state, answer with the mode to command. ModeSelectorDriver calls it on
// the main thread whenever a decision is due and polls the task — it never
// blocks a frame on the answer — so an implementation that does real IO (the
// LLM) must hop off the thread itself (Task.Run, async HTTP). The token is
// cancelled when the answer stops mattering: a newer decision came due, or the
// episode ended. Honour it rather than letting the work run on. Every real AI
// body carries a GameStateSnapshotBuilder, so the snapshot is only null on a
// bare test body without one.
public interface IModeSelector
{
    Task<NpcMode> SelectModeAsync(GameStateSnapshot snapshot, CancellationToken cancellation);
}

// Optional extension: a selector that carries state across calls — the LLM
// tier's snapshot history (issue #131) — clears it here when the episode
// restarts. The driver calls it from its own ResetState; the stateless
// baselines don't implement it.
public interface IStatefulModeSelector : IModeSelector
{
    void ResetState();
}
