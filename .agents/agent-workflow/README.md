# Agent workflow

Opt-in stages: concept, roadmap, implementation. Execute only the stage the user invokes; never start the next automatically. This kit contains workflow instructions; project policy belongs to [AGENTS.md](../../AGENTS.md) and its linked rules.

## Entry points

| Stage | Instruction file | Output |
|---|---|---|
| 1. Concept | [01-konzept-planung.md](01-konzept-planung.md) | Agreed intention and scope |
| 2. Roadmap | [02-roadmap-erstellung.md](02-roadmap-erstellung.md) | Ordered, executable checkboxes |
| 3. Implementation | [03-orchestrierte-umsetzung.md](03-orchestrierte-umsetzung.md) | Delegated slices and milestone audits |

Invoke a file with a task path, e.g. `Execute .agents/agent-workflow/01-konzept-planung.md. Task: tasks/<name>`. A file mention plus task path is equivalent.

For each stage, read this page, project rules, the stage instructions and existing task artifacts. Without a task directory, ask for it before creating artifacts. Store workflow artifacts only there; stage 3 may also edit the implementation files covered by the task.

## Artifact layout

```text
tasks/<name>/
  Konzept.md
  roadmap.md
  konzept/                      # replaces Konzept.md only for large concepts
  roadmap/                      # only when separate milestones/leaves help
    <number>-<short-name>/
      roadmap.md
      tasks/Mx.y-Tz.md
```

Keep the existing German filenames for compatibility; write new content in English. Start with one concept and one roadmap. Add files only when the task needs them.

Each executable item must fit one agent session, including analysis, implementation and validation. Split larger items before execution; do not assume a fixed context capacity.

Resume at the first open executable checkbox. Do not create execution logs, tech-debt inventories, code maps or step files as additional tracking artifacts.

## Concept contract

Minimum: intention (why and what outcome), scope as **Must** / **Out of scope**, and verifiable acceptance criteria. Add constraints only when needed; no optional or nice-to-have scope.

```markdown
---
status: draft
---

# <Title>

## Intention
## Scope
### Must
### Out of scope
## Verification
```

Only explicit user approval with no unresolved decisions permits `status: ready`. While draft, a working-memory section may track open decisions; remove it before ready. Never persist secrets.

## Roadmap contract

Use ordered `- [ ]` items. Each executable item needs intention, scope, exclusions and acceptance criteria. Store detail once and link to it. Parent checkboxes are aggregates: close them only when every child and parent acceptance criterion is verified.

Include an audit at each milestone's end, or once at the end if there are no milestones. Close only checkboxes whose acceptance was actually verified.

For a separate leaf file, use this minimum:

```markdown
# Mx.y-Tz — <Name>

## Intention
## Scope
## Out of scope
## Contracts and invariants
## Acceptance
- [ ] <Verifiable outcome>

## Checklist
- [ ] Current behavior inspected
- [ ] Scope implemented; exclusions preserved
- [ ] Acceptance verified against concept and application
- [ ] Roadmap checkboxes updated
- [ ] Atomic commit

## Completion evidence
```
