---
status: draft
type: konzept
project_kind: brownfield
estimated_scope: small
rules_dir: .agents/rules
last_updated: 2026-07-28T11:31:00+02:00
open_questions:
  - Wann/wie soll die Synchronisierung ausgelöst werden (z. B. automatisch beim Start, per CLI-Flag)?
  - Woher stammt die Referenz-Struktur für neue/entfernte Optionen (embedded Resource vs. appsettings.default.json)?
  - Wie genau soll die Backup-Datei benannt und abgelegt werden (Timestamp vs. statische .bak)?
---

# Konzept: appsettings.json automatisch aktuell halten

## Ziel (Was)

Beim Entwickeln verändern sich Konfigurationsoptionen in `appsettings.json` (neue Optionen kommen hinzu, alte entfallen). Wenn eine neue Version der Anwendung (`.exe`) auf ein System mit bestehender `appsettings.json` ausgeliefert wird, soll die `appsettings.json` automatisch synchronisiert werden:
- Veraltete (entfernte) Optionen werden gelöscht.
- Neue Optionen werden mit ihren Standardwerten hinzugefügt.
- Bestehende, weiterhin gültige Konfigurationswerte bleiben unverändert erhalten.
- Vor der Anpassung wird ein Backup der bestehenden `appsettings.json` angelegt.

## Warum / Kontext

Bei Updates einer bereits installierten/genutzten Software fehlen manuell gepflegten `appsettings.json` oft neue Pflicht- oder Utility-Felder, oder sie enthalten alte, ungenutzte Leichen. Der Anwender/Admin sollte die Konfigurationsdatei nicht manuell mit Versionshinweisen vergleichen müssen.

## Scope

### Muss-Haben

- Automatische Schema-Synchronisierung (Merge) bestehender `appsettings.json` mit dem neuen Ziel-Schema.
- Erhalt bestehender Benutzer-Werte für alle weiterhin existierenden Schlüssel.
- Hinzufügen neuer Schlüssel mit Standardwerten aus der Referenz-Konfiguration.
- Entfernen von Schlüsseln, die in der neuen Version nicht mehr existieren.
- Erstellung einer Backup-Datei vor dem Schreiben der Änderungen.

### Nice-to-Have (optional, spätere Iteration)

- Erstellung des Backups nur dann, wenn sich tatsächlich Änderungen ergeben haben.
- Detailliertes Logging / Trail über geänderte, hinzugefügte oder entfernte Konfigurationsschlüssel beim Startup.

### Non-Goals (bewusst NICHT Teil davon)

- Manuelle GUI- oder Interaktionsdialoge während des Updates (soll nahtlos/automatisch ablaufen).
- Migration von komplexen Wert-Transformationen (z. B. Formatänderungen von Werten), sofern Schlüsselname identisch bleibt.

## Zielplattformen / Technischer Rahmen

- .NET 10 / C# 14
- JSON Configuration Handling (System.Text.Json / JsonNode oder IConfiguration integration)

## Verworfene Alternativen

- *Manuelle Anleitung/Documentation für Nutzer zur Migration:* Zu fehleranfällig und unbequem bei automatisiertem Einsatz als MCP-Server.

## Wo im Projekt

- [appsettings.json](file:///c:/Daten/Entwicklung/Ralf/SqlToAi/src/SqlToAi/appsettings.json) (Haupt-Konfigurationsdatei)
- [Program.cs](file:///c:/Daten/Entwicklung/Ralf/SqlToAi/src/SqlToAi/Program.cs) (Einstiegspunkt für Startup-Logik)
- `src/SqlToAi/Configuration/` (Ordner für Konfigurations-Services/Migration)

## Wie (grober Ansatz)

1. Beim Anwendungsstart (oder per getriggertem Mechanismus) wird die lokale `appsettings.json` eingelesen.
2. Eine Referenz-Struktur (die "Soll"-Konfiguration aus dem aktuellen Build) wird geladen.
3. Strukturvergleich (JSON-Tree-Walk):
   - Fehlende Knoten im Ziel werden aus der Referenz mit Vorgabewerten ergänzt.
   - Knoten im Ziel, die in der Referenz fehlen, werden entfernt.
4. Falls Abweichungen festgestellt wurden:
   - Sicherungskopie anlegen (Backup).
   - Aktualisierte `appsettings.json` formatiert (indented) speichern.

## Definition of Done / Erfolgskriterien

- Ein automatisierter Test deckt folgende Szenarien ab:
  1. Eine veraltete `appsettings.json` mit fehlenden Schlüsseln erhält die neuen Schlüssel mit Standardwerten.
  2. Eine `appsettings.json` mit entfernten Schlüsseln verliert diese Schlüssel nach dem Sync.
  3. Geänderte Benutzerwerte bleiben unangetastet.
  4. Eine Backup-Datei wird erfolgreich angelegt.
- Die Anwendung führt beim Start oder Aufruf den Sync ohne Beschädigung valider Einstellungen aus.

## Offene Punkte

- Details zu Auslösezeitpunkt, Referenzquelle und Backup-Formatierung (siehe Fragen an Nutzer).