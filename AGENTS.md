# AGENTS.md — SqlToAi

Repository map for coding agents. User-facing communication is German.

## Required rules

Read these before working; they own the instructions, this file only routes to them.

| File | Scope |
|---|---|
| [.agents/rules/system-specs.mdc](.agents/rules/system-specs.mdc) | Host, shell, build and test commands |
| [.agents/rules/csharp-navigation.mdc](.agents/rules/csharp-navigation.mdc) | MCP-first C# navigation; apply when inspecting C# |
| [.agents/rules/development.mdc](.agents/rules/development.mdc) | Change constraints, validation, documentation, language and commits |

## Project references

Read the reference relevant to the change, rather than loading all documentation.

| File or directory | Contents |
|---|---|
| [README.md](README.md) | Public introduction and first run |
| [docs/README.md](docs/README.md) | Documentation index |
| [docs/architecture.md](docs/architecture.md) | Components and runtime flow |
| [docs/security.md](docs/security.md) | Access policy, query guards, anonymization and limits |
| [docs/configuration.md](docs/configuration.md) | Settings, credentials, migration, metadata and logging |
| [docs/tools.md](docs/tools.md) | MCP contracts, typed parameters and error catalog |
| [docs/development.md](docs/development.md) | CLI usage, build, tests, publishing and releases |
| [src/SqlToAi/](src/SqlToAi/) | Server implementation and configuration template |
| [tests/SqlToAi.Tests/](tests/SqlToAi.Tests/) | xUnit tests |
| [scripts/](scripts/) | Publishing and release scripts |
| [sql-scripts/](sql-scripts/) | SQL examples |
| [tasks/](tasks/) | Task-specific concepts and roadmaps |

## Explicit planning workflows

Use [.agents/agent-workflow/README.md](.agents/agent-workflow/README.md) when the user invokes concept planning, roadmap creation or orchestrated implementation. These are opt-in stages, not prerequisites for ordinary changes.
