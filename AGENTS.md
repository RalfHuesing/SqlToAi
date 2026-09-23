# AGENTS.md — SqlToAi

Projekt-Orientierung für KI-Coding-Assistenten (.NET 10 / C# 14).

## Setup, Build & Test

- **Build:** `dotnet build SqlToAi.slnx`
- **Test:** `dotnet test SqlToAi.slnx` (xUnit v3)
- **Umgebung:** Windows PowerShell (`pwsh`), Git immer mit `--no-pager`.

## C#-Analyse & Qualität: AiNetLinter MCP-First (Verbindlich)

- **Workflow & Tools:** Proaktiv und primär gemäß [.agents/rules/AiNetLinter-McpWorkflow.mdc](.agents/rules/AiNetLinter-McpWorkflow.mdc) (keine Textsuch-Kaskaden für C#-Semantik).
- **Linter-Grenzwerte:** Siehe [.agents/rules/AiNetLinter.mdc](.agents/rules/AiNetLinter.mdc).
- **`targetPath`:** Absoluter Pfad zu `SqlToAi.slnx` (für alle MCP-Abfragen und `verify`).

## Architektur & Richtlinien

- Leitfaden: [.agents/rules/SqlToAiRichtlinien.mdc](.agents/rules/SqlToAiRichtlinien.mdc)
  - Safety-First: Read-Only Guard (Regex + Rollback), granulare Anonymisierung/Tokenisierung (`§§§T1§§§`).
  - Access Levels: `None` (Default) > `SchemaOnly` > `ReadOnlyAnonymized` > `ReadOnly` > `ReadWrite`.
  - Fehlerkatalog: `SQL-AI-0001` bis `SQL-AI-0110`.

## Projektstruktur

- [src/](src/): SqlToAi MCP-Server (Dapper, SqlClient, Guardrails, Anonymisierung).
- [tests/](tests/): Unit- & Integrationstests.
- [docs/](docs/): Dokumentation und MCP-Spezifikation.
- [scripts/](scripts/): PowerShell-Skripte (`create-release.ps1`, `deploy.ps1`).

## Aufgabensteuerung & Workflows

- Dreistufiger Workflow: [.agents/agent-workflow/README.md](.agents/agent-workflow/README.md) (01-Konzept, 02-Roadmap, 03-Orchestrierte Umsetzung).
- Aufgaben-Ordner: `tasks/<name>/` (`Konzept.md`, `roadmap.md`).

## Commit-Konventionen

- **Format:** Conventional Commits (`feat:`, `fix:`, `refactor:`, `chore:`, `build:`, `docs:`, `test:`).
- **Sprache:** Deutsch.
- **Prinzip:** Atomare Slices mit beiliegenden Tests und Doku.
