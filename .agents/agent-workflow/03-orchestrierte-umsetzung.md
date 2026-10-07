# Orchestrated implementation — stage 3

Apply the [shared workflow contracts](README.md). Read concept and roadmap. Orchestrate; do not write production code yourself or invoke another stage. Invoking this stage authorizes atomic commits for its implementation slices.

## Task-specific model and role assignments

Before delegation, read any model/role/effort policy in the task concept and roadmap. Apply explicit user assignments first, then the task-specific assignments, then the defaults in AGENTS.md for roles without an assignment. Pass the exact model and reasoning effort on every delegated assignment, including audits, exploratory reviews, and corrections. Use the smallest sufficient task context and respect separate independent-review contexts; do not inherit the full conversation if that prevents the requested model/effort override or leaks another review's findings.

Distinguish any recommended parent launch setting from mandatory delegated assignments; subagent selection does not change the running parent. A parent recommendation does not block delegation with the required settings. If a required delegated model/effort is unavailable, report the blocker; do not silently substitute another model. Record the actual delegated model/effort with the item's completion evidence.

## Execution

1. Select the first open executable item: a leaf file, otherwise a checkbox with no open children. Do not delegate manual gates or items without an implementation assignment. Honor an item's explicit audit or exploratory-review role instead of assigning it as production implementation.
2. Start exactly one subagent in the selected item's assigned role and wait for completion. Implementation agents are the production-code writers; audit and exploration agents follow their read-only review scope. No concurrent writers in the same worktree.
3. Inspect the diff and sample the claimed checkbox evidence. Return gaps or unsupported completion claims to the same item before advancing.
4. After each milestone (or the last implementation item when there are no milestones), delegate an audit. Use the roadmap's explicit audit item when present rather than duplicating it. Findings permit at most one additional correction item for that milestone; no repeated audit/correction loop. Execute any explicitly required post-audit exploration/content reviews next, with the task's reviewer roles and correction/reverification procedure; a green audit does not complete those reviews.
5. Stop when the roadmap is complete or a real blocker/unresolved decision requires user input. Surface unresolved audit findings; never mark their acceptance complete.

## Implementation assignment

Give the subagent the concept, item file or roadmap excerpt, and project rules. Require:

- Only the assigned scope; no adjacent features or optional additions.
- Inspect current behavior instead of forcing plan assumptions onto code.
- Verify acceptance against the concept, exclusions, invariants, application and required tests.
- Record completion evidence and update only verified checkboxes under the shared contract.
- Commit the slice atomically with its documentation and checkbox changes under the project's commit policy.

## Audit assignment

Read and report findings with locations; do not modify production code. Evidence may go in the roadmap or `audit.md`, with the audit checkbox. With no findings, skip the correction item.
