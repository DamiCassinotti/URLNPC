// The ML-Agents brain for the agent-driven player (issue #10). Deriving from
// EnemyAgent shares the enemy's observation/action/reward contract, so one
// policy can drive both sides for self-play. A distinct type keeps the two
// sides apart in the Inspector and logs, and lets the player side diverge later
// without touching the enemy.
public class PlayerAgent : EnemyAgent
{
}
