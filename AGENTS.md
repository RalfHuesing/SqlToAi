# AGENTS.md — SqlToAi

Projekt-Orientierung für KI-Coding-Assistenten.

## Setup & Build

- **Build:** `dotnet build` (oder `dotnet build SqlToAi.slnx`)
- **Runtime:** .NET 10 / C# 14

## Tests

- **Test:** `dotnet test` (oder `dotnet test SqlToAi.slnx`)
- **Framework:** xUnit v3

## Code-Style & Konventionen

- Projektspezifische Richtlinien und Linter-Regeln befinden sich unter [.agents/rules/](.agents/rules/):
  - [.agents/rules/SqlToAiRichtlinien.mdc](.agents/rules/SqlToAiRichtlinien.mdc) — Entwicklungs- und Sicherheitsrichtlinien für SqlToAi
  - [.agents/rules/AiNetLinter.mdc](.agents/rules/AiNetLinter.mdc) — C#/.NET Linter-Vorgaben

## Commit- & PR-Konventionen

- **Format:** Conventional Commits (z. B. `feat:`, `fix:`, `refactor:`, `chore:`, `build:`, `docs:`)
- **Sprache:** Deutsch

## Projektstruktur

- [src/](src/): Quellcode des SqlToAi MCP-Servers (Anonymisierung, Read-Only Guard, Schema-Metadaten, Dapper).
- [tests/](tests/): Unit- und Integrationstests.
- [docs/](docs/): Dokumentation und MCP-Spezifikation.
- [scripts/](scripts/): Entwicklungs- und Hilfsskripte.

## Dev-Loop / Aufgabensteuerung

- Für mehrstufige Aufgaben (Audits, Refactorings, Features): siehe [.agents/Agent-Scaffolding/dev-loop/README.md](.agents/Agent-Scaffolding/dev-loop/README.md).
