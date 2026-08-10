# Review rules

Injected into the PR review prompt by `.github/workflows/claude-code-review.yml`.

Note: the review plugin's compliance subagents audit against `CLAUDE.md`, not this
file. A rule that must be enforced reliably belongs in `CLAUDE.md`; this file is for
review-process rules and for checks that would be noise in the coding instructions.

## What to report

The bar is a defect a reviewer would ask to be fixed before merge: wrong logic,
an unhandled case, something that breaks at runtime, or a requirement in the
linked issue the diff does not meet. Quote the rule or criterion you mean.

Style, naming and "could be cleaner" are not worth a comment here. Neither is
anything a compiler or the Unity test suites already catch.

## Cap the volume

At most 8 inline comments per review. If there are more, post the strongest 8 and
say "plus N similar" in the summary. A wall of comments gets skimmed, not read.

## Do not report

- Pre-existing problems the diff merely moves or touches
- Missing test coverage on trivial glue; see below for where tests are expected
- Third-party code: `Assets/StarterAssets/`, `Packages/com.unity.ml-agents/`
- Comment density or wording, unless it breaks a rule in `CLAUDE.md`

## Always check

These are URLNPC invariants that are easy to break and expensive to catch later.

- **Serialized fields.** The `FPS.unity` scene is force-binary, so renaming, moving
  or retyping a `[SerializeField]` field silently drops whatever the scene has
  serialized for it. Flag any rename/move of a serialized field, and any attempt to
  fold tunables (reward values, `roundDurationSeconds`, `runSeed`) into a POCO or a
  nested serializable class.
- **POCO + adapter.** New game rules belong in an engine-free class with EditMode
  tests, with the MonoBehaviour as a thin adapter feeding it `Time`, raycasts and
  actions — the shape of `RoundClock`, `RewardComputer`, `PerceptionState`,
  `EpisodeLog`. Flag new rule logic written directly into a MonoBehaviour.
- **Sensory contract.** Policy inputs and NPC actions read the target only through
  `PerceptionMemory` — never the player's true position or HP. Environment code
  (spawn separation, reward computation, the sight check) may read true state.
- **Reproducibility.** Randomness that affects an evaluation run goes through
  `RunRng` on its own sub-stream, not `UnityEngine.Random`. The documented
  exception is `EnemyWeapon` aim spread.
- **Assembly wiring.** New game code stays in the `URLNPC` assembly; a new package
  dependency needs its asmdef name added to `Assets/Scripts/URLNPC.asmdef`
  references, and a new `internal` test seam needs `AssemblyInfo.cs` updated.
- **Test process args.** Nothing in `scripts/run-tests.sh` or `.github/workflows/`
  may pass `-runSeed` — it outranks the inspector seeds the reproducibility tests
  set on purpose.
- **PlayerPrefs keys.** `CounterData.cs` key strings are duplicated in
  `CounterDataTests` and `PlayModeTestBase`; a rename must hit all three.
- **Tests.** New logic, bug fixes and edge cases should come with tests in the
  existing structure (EditMode for POCOs, PlayMode for anything needing real
  geometry, NavMesh or Academy). Flag a non-trivial behavior change with no test
  and no stated reason for skipping one.
