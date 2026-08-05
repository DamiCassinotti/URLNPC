/// <summary>
/// The ML-Agents brain for the agent-driven player (issue #10). The player
/// body reuses the whole enemy combat stack — an <see cref="EnemyBehavior"/>
/// with <c>targetTag = "NPC"</c>, an <see cref="EnemyWeapon"/> and NavMesh
/// locomotion — so the agent shares the enemy's observation/action/reward
/// contract and can train against it (self-play) or evaluate it with no
/// human input. A distinct type so the two sides are distinguishable in the
/// Inspector and logs, and can diverge later (e.g. a different reward shape
/// or the mode-conditioned policy) without touching the enemy.
/// </summary>
public class PlayerAgent : EnemyAgent
{
}
