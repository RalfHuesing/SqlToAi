# Agent-Scaffolding

Wiederverwendbare Workflow-Bausteine für agentische Coding-Sessions —
Markdown-Dateien, die eine KI-Session anweisen, eine bestimmte Rolle
(Orchestrator, Planer, Coder, Auditer, …) zu übernehmen und einen
definierten Prozess abzuarbeiten. Kein Code, keine Abhängigkeiten außer
einem Werkzeug, das Subagenten/Sub-Konversationen mit isoliertem Kontext
starten kann.

**Struktur:** eine Flow-Familie pro Top-Level-Ordner, jede mit eigener
`README.md` (Intention, wann benutzt man was).

## Verfügbare Flow-Familien

- **[`dev-loop/`](dev-loop/README.md)** — Konzept schärfen → planen →
  umsetzen → prüfen, für Software-Änderungen jeder Größe (Audits,
  Refactorings, Feature-Implementierungen).

Weitere Flow-Familien (z. B. für Analyse, Recherche, …) kommen als
eigene Geschwister-Ordner dazu, wenn Bedarf entsteht.

## Einbindung in ein Projekt

Dieses Repo wird nicht kopiert, sondern in Zielprojekte eingebunden
(z. B. als Git-Submodule) — Änderungen an einem Ort, für alle Projekte
verfügbar. Projekt-spezifische Konventionen (Coding-Rules, Architektur-
Leitplanken) bleiben **im jeweiligen Projekt** (typischerweise unter
`.agents/rules/`) und wandern nicht hierher — die Dateien in diesem Repo
sind bewusst projekt-unabhängig.

Wo genau du dieses Repo innerhalb eines Projekts platzierst (`.agents/
dev-loop/`, `tools/agent-scaffolding/`, …), ist egal — alle Verweise
zwischen den Dateien hier sind relativ zueinander formuliert.
