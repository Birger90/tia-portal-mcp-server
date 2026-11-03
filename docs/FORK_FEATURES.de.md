# Was dieser Fork mehr kann als das Original

**Sprache:** [English](FORK_FEATURES.md) · Deutsch

Vergleich von **Birger90/tia-portal-mcp-server** (Stand `main` 2026-11-02) mit dem Original
**Czarnak/tia-portal-mcp** (`main` 8b41877, 2026-11-02). Die Zahlen sind aus dem Code beider Stände gezählt
(Werkzeugnamen, Operationskataloge), nicht geschätzt. „Live getestet“ heißt: auf TIA Portal V21 und PLCSIM Advanced 8.0 in
den Testprojekten `MCP_Livetest_01`, `MCP_Safety_01`, `MCP_G2_Test` ausgeführt; Belege im
[Fehlerkatalog](superpowers/acceptance/defect-catalog.md) und in den
[Abnahmeberichten](README.de.md). Alles andere ist nur offline geprüft (Unit-Tests, FakeWorker).

## Zahlen

| | Original | Fork |
| --- | --- | --- |
| MCP-Werkzeuge | 19 | 30 |
| `plc_read`-Operationen | 3 | 54 |
| `plc_write`-Operationen | 14 | 202 |
| `network_read` / `network_write` | 4 / 5 | 11 / 25 |
| `hmi_read` / `hmi_write` | 20 / 9 | 24 / 39 |
| `library_read` / `library_write` | – | 14 / 39 |
| `plc_online_read` / `plc_online_write` (S7-1500-Web-API) | – | 18 / 7 |
| `vci_read` / `vci_write` | 3 / 6 | 3 / 6 |

Neue Werkzeuge: `retrieve_project` (Projektarchiv dearchivieren; nicht live getestet), `plc_download`, `plc_tia_online`, `plc_station_upload`, `plcsim_read`, `plcsim_write`, `plcsim_test`,
`plc_online_read`, `plc_online_write`, `library_read`, `library_write`.

## Funktionen, die es im Original nicht gibt

| Bereich | Was der Fork kann | Live getestet |
| --- | --- | --- |
| Laden ins Gerät (`plc_download`) | Laden auf PLCSIM oder echte CPU über jede PC-Schnittstelle, Zertifikatsvertrauen nur per bestätigtem SHA-256-Fingerabdruck | ja, auf PLCSIM mit Standard-CPU; echte Hardware nein. Fehlersichere Programme lehnt TIA über Openness selbst ab (K33): Laden über die TIA-Oberfläche |
| Online über Openness (`plc_tia_online`, `plc_read`) | online gehen, offline gehen, Offline/Online-Vergleich, Beobachtungs- und Forcetabellen, Software-Vergleich, Hardware-Vergleich offline/online (Issue #7) | ja: `go_online`, `go_offline`, `compare_to_online` (identisch nach Download); Tabellen nein |
| Ladbare Dateien (`plc_write generate_loadable`) | ladbare Datei für Bausteine oder Software Units schreiben, für eine PLC oder PLCSIM | nein |
| Know-how-Schutz und Schreibschutz von Bausteinen (`plc_read get_block_protection`, sechs `plc_write`-Operationen) | Know-how-Schutz setzen und entfernen, Schreibschutz definieren, aktivieren, deaktivieren und ändern, mit Passwörtern aus Passwortquellen | nein |
| Projektsprachen und Projekttexte (`plc_read get_project_languages`, `plc_write set_project_languages`, `export_project_texts`, `import_project_texts`) | Projektsprachen verwalten, Projekttexte zum Übersetzen als XLSX exportieren und wieder importieren | nein |
| Weitere CPU-Einstellungen (`plc_read get_cpu_settings`, 20 `plc_write`-Operationen; Issue #15) | Kontosperre, PLC-Passwortrichtlinien, Syslog (CPU- und Projektserver), Systemwebseiten, Standard-Webanwendung, Zugriff auf Beobachtungs-/Forcetabellen für Webserver und Display, Display-Passwort aus einer Passwortquelle, Logo des Frontdisplays, Projektsprache je Zeile, IP-Erreichbarkeit, Web-DBs erzeugen; jede Änderung zurückgelesen | nein |
| Webserver- und OPC-UA-Benutzer (7 `plc_write`-Operationen; Issue #13) | Webserver-Benutzer der CPU mit Rechten, einfache Webserver-Benutzer und Benutzer des OPC-UA-Submoduls anlegen, ändern und löschen; Passwörter nur aus Passwortquellen; lesen mit `get_cpu_settings` | nein |
| UMAC (`plc_read get_umac`, 19 `plc_write`-Operationen; Issue #12) | lesen, ob das Projekt geschützt ist, Rollen, Projekt- und UMC-Benutzer, Engineering- und Geräte-Funktionsrechte, anonymer Benutzer, Passwortrichtlinie; eigene Rollen, Projektbenutzer (Passwörter aus Passwortquellen) und eigene Geräte-Funktionsrechte anlegen/ändern/löschen; Rechte und Rollen zuweisen | nein |
| CPU-Zertifikate (`plc_read get_cpu_certificates`, 11 `plc_write`-Operationen; Issue #14) | Zertifikate der CPU mit SHA-256-Fingerabdrücken lesen (zum Vergleich mit TLS-Abfragen), aus Vorlagen erzeugen, importieren, exportieren, löschen, Webserver/OPC-UA-Server zuordnen, dynamische Zertifikatseinstellungen; nichts wird automatisch vertraut | nein |
| Safety-Ausdruck (`plc_write generate_safety_printout`; Issue #21) | Safety-Ausdruck der F-CPU als PDF-/XPS-Datei, eine Unterlage für die Safety-Abnahme; der MCP ersetzt die Abnahme nie | nein |
| Safety Validation Assistant (`plc_read get_safety_validation`, `check_activation_test`, 14 `plc_write`-Operationen; Issue #25) | Aktivierungstests, Sicherheitsfunktionen und Bedingungen lesen, prüfen und vorbereiten, exportieren und den Testbericht erzeugen; trägt nie ein Testergebnis ein, jede Änderung unter dem Safety-Abnahme-Guard | nein |
| OPC-UA-Server-Schnittstellen (`get_opcua_configuration`, elf `plc_write`-Operationen) | Server- und SIMATIC-Schnittstellen, Referenz-Namensräume, Zugriffsbeschränkungen, Rollen und Berechtigungen | nein |
| Laden in einen Ordner und Master-Secret online (`download_to_folder`, `set_/reset_plc_master_secret_online`; `plc_download rhTarget`) | Ladedaten in einen Windows-Ordner, Master-Secret der Online-PLC, R/H-Download in Primär-/Backup-CPU | nein |
| Station hochladen (`plc_station_upload`) | Station aus dem Gerät ins Projekt laden | nein |
| PLCSIM Advanced (`plcsim_*`) | Instanzen steuern (Stopp/Run), Werte lesen und schreiben, Testabläufe mit Erwartungen und Zeitgrenzen, auch vorzeichenlose Typen | ja, inklusive Sicherheits-Abschalttest 8/8 |
| S7-1500-Web-API (`plc_online_*`) | Beobachten und Steuern echter CPUs ohne TIA | nein (braucht echte Hardware) |
| Fehlersichere Programme | F-Bausteine auflisten, Safety-Administration lesen, F-Ablaufgruppe anlegen und Main-Safety-Baustein zuweisen, Safety-Login und Passwörter | teilweise: Auflisten, Administration lesen, Main-Safety-Baustein per SD bearbeiten, Safety-Baustein kopieren und Safety-Variable verschieben ja; Login und Passwörter nein |
| SIMATIC SD (KOP/FUP als Text) | Netzwerke einfügen, ersetzen und löschen, Baustein aus Dokument anlegen, auch im Main-Safety-Baustein (Verknüpfung der F-Ablaufgruppe bleibt erhalten, K14) | ja |
| Bausteine im Detail | Schnittstellen-Member, DB-Werte, umbenennen, umnummerieren, kopieren, verschieben, Instanz-DB, Aufrufbaum und Analyse, DB-Schnappschuss, Titel und Kommentare | teilweise: DB-Werte, Umnummerieren und Verschieben, Schnittstellen-Member, Kopieren, Schnappschuss, Titel/Kommentare ja |
| Variablen | Kommentare setzen (K22), Variable verschieben, Tabellen als Datei | Kommentare und Verschieben ja |
| Technologieobjekte | anlegen, löschen, Parameter lesen und schreiben, Hardware verbinden, Export und Import | ja (K18–K20, K23) |
| Hardware-Module | Module stecken, verschieben, kopieren, löschen, Typ ändern, Parameter, IO-Adressen, IO-Map-Export, IO-System anlegen, Custom Identities (Schlüssel/Wert) an Geräten und Geräteelementen, PROFINET-Port-Topologie (lesen, verbinden, trennen), Transferbereiche (lesen, anlegen, ändern, löschen), IO-System löschen, IO-Device trennen, CAx-Export und -Import (AutomationML) | teilweise: Stecken, IO-Adressen, IO-Map-Export, Steckplätze, IO-System ja |
| CPU-Schutz | Schutz lesen, Zugriffsstufe setzen, vertrauliche Daten schützen/aufheben | Lesen, Zugriffsstufe, Schutz aufheben ja |
| Übersetzen | Hardware übersetzen (`scope:"hardware"`), Einstellung „Simulation bei Bausteinübersetzung unterstützen“ setzen | ja |
| Änderungserkennung | Prüfsummen von Software und Textlisten einer PLC, Fingerprints eines Bausteins oder PLC-Datentyps (`get_plc_checksums`, `get_fingerprints`) | nein |
| Bibliotheken | Projekt- und globale Bibliotheken, Typen, Versionen, Kopiervorlagen und ihre Ordner, Aufräumen, Harmonisieren, Typen aus Dokumenten, Exporte, Detailvergleiche | nein |
| Meldungen und Textlisten | Textlisten, Meldetexte, Überwachungen, ProDiag, Meldeklassen, Systemdiagnose-Einstellungen als Datei | nein |
| Externe Quellen, Ordner, Software Units | Quellen importieren und generieren, Gruppen und Units verwalten, Dateiexporte | nein |
| WinCC Unified | Bilder, Skripte und Massen-Umbenennungen zusätzlich zu Variablen und Meldungen | nein |
| OPC-UA-Export | OPC-UA-XML der CPU exportieren | nein |
| Passwortverwaltung | Windows-Programm „TiaMcp Passwortverwaltung“: Passwörter in der Windows-Anmeldeinformationsverwaltung, nie im Chat; Zugriffsmodus einstellen | nein |

## Verhalten, das sich vom Original unterscheidet

- **Nie strenger als TIA Portal.** Der Fork verweigert nur, was TIA selbst verweigert, oder ungültige und mehrdeutige
  Eingaben. Wo das Original sperrt, gibt der Fork einen Hinweis und lässt TIA entscheiden. Ausnahme: Ein ausdrücklicher
  SIMATIC-SD-Export von F- und Systembausteinen wird abgelehnt, weil TIA V21 dabei abgestürzt ist.
- **Klare Meldungen statt stillem Warten.** Wartet TIA auf die Openness-Freigabe, kommt nach 25 s ein Hinweis (K37, live).
  TIA-Abfragen, die außerhalb eines Aufrufs beantwortet werden, meldet der nächste Aufruf mit Dialogtext (K42, nicht live).
- **Fehlerkatalog.** 42 live gefundene Fehler, davon 32 live behoben und bestätigt.
- **Nur TIA Portal V21 und WinCC Unified.**

## Grenzen

- Fehlersichere Programme lassen sich über Openness nicht laden (Siemens-Sperre, K33).
- TO-Versionen abfragen (K08) und TOs verschieben (K21): Siemens bietet keine dokumentierte Schnittstelle.
- Alles ohne „ja“ in der Spalte „Live getestet“ ist programmiert und offline getestet, aber auf TIA noch nicht bestätigt.
