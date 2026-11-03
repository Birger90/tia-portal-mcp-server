# TIA-Portal-TestSuite-Operationen

**Sprache:** [English](TESTSUITE_OPERATIONS_SUMMARY.md) · Deutsch

## Unterstützungsstatus

Der aktuelle MCP-Vertrag enthält keine TestSuite-spezifischen Operationen.

`compile_check` gibt die Übersetzungsergebnisse für eine PLC oder einen ausgewählten Bausteinbereich zurück. Es ist eine generische Übersetzungsoperation; sie erstellt oder führt keine Application Tests, Style-Guide-Prüfungen oder System Tests aus.

## Aktuelle Grenzen

Die folgenden TestSuite-Bereiche liegen außerhalb der aktuellen Oberfläche:

- Erkennung des `TestSuiteService`.
- Application-Test-Sets, -Gruppen, -Fälle und die Ausführung über `TestCaseExecutor`.
- Style-Guide-Systemgruppen, Regelsätze, Aktualisierungen von Regelsatzdateien und die Ausführung über `RuleSetExecutor`.
- System-Test-Gruppen, -Fälle, OPC-UA-Validierung und die Ausführung über `SystemTestCaseExecutor`.
- TestSuite-Ergebnismeldungen, Ausführungszustände und die Interpretation von Ergebnissen.

Für das generische Übersetzungsverhalten siehe [PROJECT_OPERATIONS_SUMMARY.md](PROJECT_OPERATIONS_SUMMARY.de.md) und [PLC_OPERATIONS_SUMMARY.md](PLC_OPERATIONS_SUMMARY.de.md).
