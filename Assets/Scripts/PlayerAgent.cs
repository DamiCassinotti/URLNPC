using Unity.MLAgents;
using Unity.MLAgents.Policies;

// The ML-Agents brain for the agent-driven player (issue #10). Deriving from
// EnemyAgent shares the enemy's observation/action/reward contract, so one
// policy can drive both sides for self-play. A distinct type keeps the two
// sides apart in the Inspector and logs, and lets the player side diverge later
// without touching the enemy.
//
// It is also the side the scripted heuristic is injected on during training
// (#109): the enemy is what the eval scores, so the opponent is this body.
public class PlayerAgent : EnemyAgent
{
    // Test seams: the fraction comes from a launch argument the test process
    // must not be given, and the test runner has no communicator to turn on.
    internal OpponentSchedule schedule;
    internal System.Func<bool> isTraining = () => Academy.IsInitialized && Academy.Instance.IsCommunicatorOn;

    BehaviorParameters parameters;

    public override void Initialize()
    {
        base.Initialize();
        parameters = GetComponent<BehaviorParameters>();
        schedule = new OpponentSchedule(OpponentSchedule.CommandLineFraction);
    }

    public override void OnEpisodeBegin()
    {
        base.OnEpisodeBegin();
        ApplyEpisodeOpponent();
    }

    // Swapped between episodes, never within one: the terminal experience of the
    // episode just ended has already been sent by the time EndEpisode gets here,
    // and BehaviorType's setter disposes the current policy.
    void ApplyEpisodeOpponent()
    {
        if (schedule == null || parameters == null) return;
        if (schedule.HeuristicFraction <= 0f) return;
        // Training only. Outside it nothing learns from this body, and it is the
        // eval session that decides which policy each side runs.
        if (!isTraining()) return;

        bool heuristic = schedule.NextEpisodeIsHeuristic();
        // HeuristicOnly plays EnemyAgent.Heuristic, i.e. the hunt-and-shoot
        // baseline; Default is the shared policy over the communicator.
        BehaviorType wanted = heuristic ? BehaviorType.HeuristicOnly : BehaviorType.Default;
        if (parameters.BehaviorType != wanted) parameters.BehaviorType = wanted;
        // The director goes on commanding modes this body's heuristic doesn't
        // read, so its per-mode rows would be labelled with a mode that drove
        // nothing. Drop them for the episode rather than average them in.
        reportsModeStats = !heuristic;
        // Its mean over a summary interval is the realized fraction, so a run's
        // opponent mix is readable next to its reward in TensorBoard.
        if (Academy.IsInitialized)
        {
            Academy.Instance.StatsRecorder.Add("Run/HeuristicOpponent", heuristic ? 1f : 0f);
        }
    }
}
