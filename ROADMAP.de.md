# Roadmap

**Sprache:** [English](ROADMAP.md) · Deutsch

> Richtungsweisende Roadmap, kein Release-Zeitplan. Prioritäten können sich ändern, sobald Fähigkeiten, Lizenzierung und Validierung an realen Projekten von Siemens TIA Portal V21 bestätigt sind.

- **Eine zuverlässige und sichere Grundlage erhalten** — Lifecycle-Operationen, Diagnose, Fehlerbehandlung, Paketierung, Dokumentation und Live-Abnahmebelege mit TIA Portal V21 verlässlich halten.
- **Live-Validierung von Openness abschließen** — verbleibende hardwareabhängige API-Fragen klären und abgesicherte Transaktions-, Exklusivzugriffs-, Ereignis- und Diagnosefähigkeiten ergänzen, wo die Unterstützung in V21 verifiziert ist.
- **Den Austausch von PLC-Quellen weiterentwickeln** — praxistaugliche Quellformate für UDTs, Datenbausteine und SCL einführen und anschließend die grafische Bearbeitung von KOP/FUP anhand realer Round Trips bewerten; XML bleibt der Kompatibilitäts-Fallback.
- **Den Austausch von Engineering-Daten verbreitern** — Workflows für generisches SimaticML, Projektgrafiken, CAx AutomationML und Import/Export von Projekttexten ergänzen.
- **Hardware- und Netzwerk-Engineering ausbauen** — Topologie, Kommunikationsverbindungen und ausgewählte Workflows zur erweiterten Geräte- und Modulkonfiguration unterstützen.
- **Die Unterstützung des HMI-Engineerings erweitern** — Unified-Lesevorgänge (Contract 1.1) und neun kontrollierte Inhaltsschreibvorgänge (Contract 2.0) sind implementiert, einschließlich mehrsprachiger Texte und typisierter Ergebnisse bei nativen Ablehnungen. Die [Qualifizierung der nativen Ablehnungen](docs/superpowers/acceptance/reports/2026-10-30-hmi-write-native-rejections-live-verification.md) ist im dokumentierten Umfang R1/R2/R3 abgeschlossen; als Nächstes die Abdeckung unterstützter Importe/Exporte und Objekte erweitern. Die Matrix mit 35 Fällen verwendete die installierte .112; das fokussierte R1, die UI-/Local-Session-Prüfungen und das Timing-after verwendeten .113, statt eines vollständigen erneuten Durchlaufs der Matrix.
- **Wiederverwendbare Engineering-Assets ermöglichen** — Workflows für globale Bibliotheken, Bibliothekstypen und Kopiervorlagen mit expliziten Versions- und Zugriffskontrollen ergänzen.
- **Kollaboratives Engineering unterstützen** — Sichtbarkeit von Multiuser/VCI und Projektserver, Session-Lifecycle, Sperren und Commit-Workflows ergänzen.
- **Kontrollierte Online- und Sicherheitsoperationen stärken** — abgesicherte PLC-Online-Workflows pflegen und Fähigkeiten für Benutzer, Rollen, Funktionsrechte und UMAC nach dem Prinzip der minimalen Rechte ergänzen.
- **Erweiterungen für Safety und automatisiertes Testen evaluieren** — Automatisierung von Safety und TestSuite nur dort untersuchen, wo Voraussetzungen, Isolation, Freigabe und Nachvollziehbarkeit hergestellt werden können.

Alle projektverändernden Fähigkeiten behalten eine explizite Zielvalidierung, eine Vorschau der Konsequenzen, Guards gegen bekannt gefährliche Änderungen, Nachvollziehbarkeit (Audit) sowie angemessene automatisierte und Live-Verifikation mit TIA Portal. Die Freigabe eines einzelnen Schreibvorgangs ist die Entscheidung des MCP-Clients; der Server macht die Konsequenz sichtbar und fragt, wo der Client es unterstützt, den Benutzer direkt (siehe [ADR-0004 (Neugestaltung der Schreibsicherheit)](docs/adr/0004-guarded-single-call-write-pipeline.md)).
