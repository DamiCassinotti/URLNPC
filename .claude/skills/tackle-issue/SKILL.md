---
name: tackle-issue
description: >-
  End-to-end workflow to implement a GitHub issue in this repo: sync main, branch off it
  with the issue number in the name, implement the fix per CLAUDE.md, self-review the diff
  the same way CI does (.github/review-rules.md + issue-satisfaction check) and fix what
  applies, then open the PR. Use whenever the user says "tackle issue N", "implement issue
  #N", "do issue N", "start on issue N", "pick up #N", or otherwise asks to take a GitHub
  issue from unstarted to an open pull request. Handles the whole branch → implement →
  review → PR loop, not just the coding step.
---

# Tackle a GitHub issue

Take a GitHub issue number from an unstarted state all the way to an open PR: sync `main`,
branch, implement, self-review the way CI will, and open the PR. The goal is that by the
time CI's reviewer (`.github/workflows/claude-code-review.yml`) sees the PR, it finds
nothing you didn't already consider.

The issue number is the argument. If none was given, ask which issue before doing anything.

Work through the steps in order. Don't batch them — each gate exists to catch a problem
before it becomes expensive (a wrong-base branch, a diff that misses the issue, a PR that
trips the review gate).

## Step 1 — Sync main and branch off it

The branch must come from a fresh `main`, or the PR diff carries unrelated commits.

```bash
git switch main
git pull --ff-only origin main
```

If `git pull` can't fast-forward or the working tree is dirty, stop and tell the user —
don't force it or stash silently.

Then read the issue so the branch name and the work reflect what it actually asks:

```bash
gh issue view <N> --json number,title,body,labels
```

**Branch name: `<prefix>/<N>-<slug>`.** The number *must* be preceded by `/` and followed
by `-` — CI extracts the issue from the branch with `grep -oE '(^|/)[0-9]+-'`, so
`rl/41-movement-primitives` links and `issue-41-foo` does not.

- `<prefix>` is a short area code. Reuse the convention already in the repo's history —
  `git log --oneline --all | head -40` or `git branch -a` shows it: `rl/` for the ML
  agent / rewards / movement / mode work, `ci/` for workflows and CI, `docs/` for docs.
  Pick the one that fits; if nothing fits, choose a short new prefix in the same spirit.
- `<slug>` is 2–4 kebab-case words from the issue title.

```bash
git switch -c <prefix>/<N>-<slug>
```

## Step 2 — Implement the issue

Implement what the issue asks, following the repo's own rules — they are not optional
style, they're what the review in Step 4 checks against:

- **`CLAUDE.md`** (project + the global one) is the source of truth for how code is
  structured here. The invariants in `.github/review-rules.md` under "Always check" are the
  ones easiest to break and worth keeping in mind *while* you write, not just at review:
  new game rules go in an engine-free POCO with a thin MonoBehaviour adapter; policy inputs
  read the target only through `PerceptionMemory`; evaluation randomness goes through
  `RunRng`; don't rename/move/retype `[SerializeField]` fields (the binary scene silently
  drops them); new package deps need the asmdef reference, new `internal` test seams need
  `AssemblyInfo.cs`.
- **Tests.** New logic, bug fixes and edge cases come with tests in the existing structure —
  EditMode for POCOs, PlayMode for anything needing real geometry/NavMesh/Academy. If a
  change genuinely doesn't warrant a test (trivial glue), that's fine, but say so and why.
  Run the relevant suite with `scripts/run-tests.sh [editmode|playmode|all]` **only if the
  Unity editor is closed** (single instance per project); otherwise note that tests should
  be run in-editor and don't fight the lock.
- Match the surrounding file's conventions and comment density. Comment the non-obvious
  "why", not what the code plainly does.

Read the pieces of the codebase the issue touches before editing — the architecture
section of `CLAUDE.md` maps the systems (`EnemyAgent` / `EnemyBehavior` / `PerceptionMemory`,
`ArenaManager`, `ModeChannel` / `ModeDirector`, `TelemetryLogger`, `RunRng`) to their files.

When the work is done, commit it:

```bash
git add -A
git commit
```

**Commit authorship:** author under the user's git `user.name` / `user.email` (the default).
Do **not** add a `Co-Authored-By: Claude` trailer or any Claude attribution — the global
CLAUDE.md forbids it. Message: short and plain, what it does or why. Don't reference "the
thesis" or list research phases.

**Keep it inside the writing-style hook's budget.** A `gh-writing-style.sh` hook rejects
over-long commit messages and PR bodies (roughly 14 lines / 160 words for a commit). Draft
tight the first time — a one-line subject, a sentence or two of what and why, and a short
bullet list only if the change really has separable parts — so you don't burn a retry
trimming it. The same budget applies to the PR body in Step 5.

## Step 3 — Sanity-check before self-review

Confirm the diff is what you think it is and nothing stray got committed:

```bash
git log --oneline main..HEAD
git diff main...HEAD --stat
```

## Step 4 — Self-review the diff the way CI will

CI runs a reviewer on every PR (`.github/workflows/claude-code-review.yml`). Running the
same review locally *before* the PR exists means findings get fixed in the same session
instead of coming back as PR comments. Reproduce it in two parts.

**4a — Review the diff at high recall.** The point is to surface what CI's reviewer would.
For a substantial diff, invoke the `code-review` skill so it fans out across its finder
angles:

```
/code-review high
```

`code-review high` expands into a multi-subagent fan-out. That's worth it for a large or
unfamiliar diff, but for a small, self-authored change it's overkill — and this session
discourages spawning subagents unasked. In that case do the review inline: read every hunk
and its enclosing function, then walk the same angles yourself — line-by-line correctness,
what any deleted lines used to guarantee, whether changed signatures break their callers,
reuse/simplification/efficiency, and the CLAUDE.md conventions below. Either way, the bar
and the fixes are the same.

Apply `.github/review-rules.md` as you triage: the report bar is "a defect a reviewer would
ask to be fixed before merge" — wrong logic, an unhandled case, a runtime break, a broken
URLNPC invariant. Style/naming/"could be cleaner" and anything the compiler or test suites
already catch are **not** findings here.

**4b — Check the diff against the issue.** Beyond code-review's normal checks, verify the
implementation actually satisfies the issue. Re-read the issue body and flag:

- a requirement or acceptance criterion the diff does not address;
- implemented behavior that contradicts what the issue described.

**Triage and fix.** For each finding, decide honestly:

- **Fix it** if it's a real defect or a missed requirement that applies to this project.
- **Skip it** if the edge case genuinely doesn't apply here (e.g. a guard against input
  that this call site can't produce, or a concern about a code path the issue rules out).
  Skipping is fine and expected — but note *which* findings you skipped and the one-line
  reason, so the user can veto.

Fixing findings means editing and committing again — then re-run 4a on the new diff if the
fixes were substantial, so you're not opening a PR against un-reviewed code.

If the review surfaces something that makes the issue's approach look wrong, or a finding
you're genuinely unsure whether to fix, **stop and ask the user** rather than pushing on.

## Step 5 — Open the PR

Once the diff is clean (findings fixed or consciously skipped) push the branch and open the
PR:

```bash
git push -u origin <prefix>/<N>-<slug>
gh pr create --title "<title>" --body "<body>"
```

- **Title:** concise, matches the repo's PR style (see `gh pr list --state merged` —
  e.g. `RL: seven movement primitives on EnemyBehavior`).
- **Body:** open with `Closes #<N>.` (CI also reads the linkage from the body; the keyword
  auto-closes the issue on merge). Then a short, plain paragraph or two: what the change
  does and the concrete reason, plus a line on test coverage. Keep it to something the user
  could have written themselves. **No** "Generated with Claude Code" footer, no Claude
  attribution, no thesis framing.

Report back the PR URL and a one-line summary of any review findings you skipped, so the
user knows what to glance at.

## What this skill does not do

- It doesn't merge the PR or touch branch protection — the user and CI's review gate own
  that.
- It doesn't force a `main` sync or discard local changes; if `main` won't fast-forward or
  the tree is dirty, it stops and asks.
- It leaves the automated Unity test suites alone while the editor is open (single-instance
  lock); it says so rather than failing silently.
