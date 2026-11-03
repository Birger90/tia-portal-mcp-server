# Sicherheitsrichtlinie

**Sprache:** [English](SECURITY.md) · Deutsch

> **Dieser Fork** (Birger90/tia-portal-mcp-server): Schwachstellen in Code, der spezifisch für diesen
> Fork ist, über dessen [Security Advisories](https://github.com/Birger90/tia-portal-mcp-server/security/advisories) melden.
> Der Rest dieser Richtlinie stammt aus dem ursprünglichen Projekt und bezieht sich auf dessen Repository und NuGet-Paket.

MCP-Server für Siemens TIA Portal V21, der Operationen der Openness API über eine MCP-Schnittstelle bereitstellt.

## Unterstützte Versionen

Dieses Projekt wird als einzelnes NuGet Global Tool mit fortlaufenden Releases (Rolling Release) gepflegt. Nur die zuletzt veröffentlichte Version erhält Sicherheitsupdates. Es wird keine Matrix unterstützter Versionen gepflegt; Nutzer sollten das Tool über `dotnet tool update -g TiaMcpServer` aktuell halten.

## Melden einer Schwachstelle

Sicherheitslücken nicht als öffentliche GitHub Issues melden. Stattdessen die private Schwachstellenmeldung von GitHub verwenden:

1. Im Repository zum Tab [Security Advisories](https://github.com/Czarnak/tia-portal-mcp/security/advisories) navigieren.
2. Auf „Report a vulnerability“ klicken.
3. Angeben:
   - Beschreibung der Schwachstelle
   - Schritte zur Reproduktion (falls zutreffend)
   - Mögliche Auswirkungen
   - Betroffene Version

Meldungen werden so bald wie möglich bestätigt und eingeordnet. Dieses Projekt pflegt derzeit kein festes SLA; die Antwortzeiten hängen von Schweregrad und Verfügbarkeit ab.

## Sicherheitsaspekte

### Lokaler stdio-Transport

Der MCP-Server kommuniziert ausschließlich über lokales stdio. Er lauscht auf keinem Netzwerkport und ist nicht aus der Ferne erreichbar. Der Zugriff wird durch den aufrufenden Prozess und den Windows-Benutzer gesteuert, der das Tool ausführt.

### Betriebssystemintegration

Das Tool setzt voraus:
- Der aufrufende Benutzer ist Mitglied der Windows-Benutzergruppe `Siemens TIA Openness`.
- TIA Portal V21 läuft mit einem geöffneten Projekt.

Die Zugriffssteuerung wird von den Berechtigungen der TIA-Portal-Installation auf Betriebssystemebene übernommen, statt in diesem Tool separat implementiert zu werden. Autorisierungsfehler auf Betriebssystemebene blockieren das Tool.

### Schreibsicherheit

Jedes Schreibwerkzeug verwendet einen abgesicherten Einzelaufruf unter einer einzigen festgelegten, verifizierten Projektbindung; kein Werkzeug verwendet Sicherheitstokens. `plc_write` und `network_write` zeigen mit `dryRun:true` eine Vorschau, führen standardmäßig aus und fragen nie nach (keine Elicitation); blockierende Guards (Kollisionen, nicht lesbare Belege) verweigern in jedem Modus. Veralteter Inhalt bei einer Inhaltsaktualisierung mit `plc_write` schlägt mit `state_changed` fehl.

Lifecycle-Schreibvorgänge verwenden einen abgesicherten Einzelaufruf: read-write erfordert ein Bestätigungsformular des Clients pro tatsächlichem Aufruf; full läuft unter der Richtlinie ohne Elicitation durch den Server weiter. Blockierende Guards verweigern in jedem Modus, und Dry Runs verändern nie etwas und fragen nie nach. Das Audit v2 für Lifecycle, Network und PLC zeichnet jeden angenommenen Aufruf auf, einschließlich Vorschauen und Ablehnungen, mit Bestätigung durch `user`, `policy` oder `none`. Eine vom Client zurückgegebene Zustimmung kann nicht beweisen, dass ein Mensch eine Abfrage gesehen hat. Details siehe Abschnitt „Schreibsicherheit“ der README.

Schreibaufrufe hängen Audit-Datensätze im JSONL-Format unter `%LOCALAPPDATA%\TiaMcpServer\audit` für forensische Prüfungen an.

### Lieferkette

Siemens-Openness-DLLs (`Siemens.Engineering*.dll`) werden nie in dieses Repository eingecheckt oder in das NuGet-Paket aufgenommen. Sie werden zur Laufzeit aus der lokalen Installation von TIA Portal V21 geladen. Das NuGet-Paket enthält nur den MCP-Host, die ausführbare net48-Worker-Datei und Open-Source-Abhängigkeiten.

### Vertrauensgrenze

Der MCP-Server stellt Lese- und Schreiboperationen auf laufenden TIA-Portal-Projekten bereit. Da das Tool Automatisierungscode und Hardwarekonfiguration einsehen und ändern kann:

- **MCP-Clients oder KI-Agenten nicht ohne ausdrückliche Prüfung auf Produktivprojekte richten.** Für Entwicklung und Tests Wegwerfkopien oder gesicherte Kopien von `.ap21`-Projekten verwenden.
- Die MCP-Client-Konfiguration und jeden KI-Agenten, der den Client steuert, als Teil der eigenen Sicherheits-Vertrauensgrenze behandeln.
- Projektschreibende KI-Prompts vor der Ausführung gegen gemeinsam genutzte oder kritische Projekte prüfen.
