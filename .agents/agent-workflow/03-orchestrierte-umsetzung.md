# Orchestrated implementation — stage 3

Apply the [shared workflow contracts](README.md). Read concept and roadmap. Orchestrate; do not write production code yourself or invoke another stage. Invoking this stage authorizes atomic commits for its implementation slices.

## Execution

1. Select the first open executable item: a leaf file, otherwise a checkbox with no open children. Do not delegate manual gates or items without an implementation assignment.
2. Start exactly one implementation subagent and wait for completion. No concurrent writers in the same worktree.
3. Inspect the diff and sample the claimed checkbox evidence. Return gaps or unsupported completion claims to the same item before advancing.
4. After each milestone (or the last implementation item when there are no milestones), delegate an audit. Findings permit at most one additional correction item for that milestone; no repeated audit/correction loop.
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
