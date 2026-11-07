# TIA Portal HMI-Operationen

**Sprache:** [English](HMI_OPERATIONS_SUMMARY.md) · Deutsch

`hmi_read` ist das nur lesende HMI-Tool für WinCC Unified. Es ist in jedem Zugriffsmodus registriert (Zählungen
13/32/32), schreibt nie, übersetzt nie und öffnet, speichert oder schließt nie ein Projekt. Es führt bis zu
50 Elemente pro Aufruf auf dem strukturierten JSON-Vertrag aus (`contractVersion:"1.1"`); Elemente laufen
unabhängig voneinander, sodass ein fehlschlagendes Element spätere nicht stoppt. Der Design-Hintergrund steht in der
[Design-Spezifikation von hmi_read](https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/specs/2026-10-07-hmi-read-design.md), dem
[Spike-Bericht](../superpowers/acceptance/reports/2026-10-07-hmi-read-spike.md), dem
[Bericht zur Live-Abnahme](../superpowers/acceptance/reports/2026-10-08-hmi-read-live-acceptance.md),
der [Vervollständigungs-Spezifikation](https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/specs/2026-10-11-hmi-read-completion-design.md) und dem
[Bericht zur Live-Abnahme der Vervollständigung](../superpowers/acceptance/reports/2026-10-12-hmi-read-completion-live-acceptance.md).

`hmi_write` stellt separat neun Schreibvorgänge für Variablentabellen, Variablen und Meldungen in schreibfähigen Modi für
Einzelplatzprojekte und gebundene lokale Sitzungen bereit. Der [Bericht zur Schreib-Abnahme](../superpowers/acceptance/reports/2026-10-14-hmi-write-live-acceptance.md)
qualifiziert genau die Produktionsquelle bzw. das Produktionspaket, nebeneinander gestartet ohne globale Installation.

## Operationen

Jede Operation außer `list_hmi_devices` und `list_project_languages` nimmt ein optionales `hmiName`
(Software- oder Gerätename); es nur weglassen, wenn das Projekt genau ein Unified HMI hat. Jedes Element
trägt eine eindeutige `operationId` (höchstens 256 Zeichen) und eine `operation`. Felder außerhalb der Liste einer
Operation werden vor jedem Worker-Aufruf abgelehnt.

| Operation | Erforderlich | Optional | Seitenweise | Liefert |
|---|---|---|---|---|
| `list_hmi_devices` | - | - | nein | Unified- und Classic-HMI-Geräte im Projekt |
| `list_tag_tables` | - | `hmiName`, `groupPath` | nein | Baum aus Variablentabellen und Gruppen |
| `list_tags` | - | `hmiName`, `tableName`, `language` | ja | Variablenzeilen (Kernfelder) |
| `get_tag` | `tagName` | `hmiName`, `language` | nein | Eine Variable im Detail: Member, Grenzwerte, Bereiche, Ersatzwerte |
| `list_system_tags` | - | `hmiName` | ja | Systemvariablen |
| `list_connections` | - | `hmiName` | nein | Verbindungen, Treibereigenschaften, Anfangsadressen |
| `list_alarms` | - | `hmiName`, `alarmKind`, `language` | ja | Zeilen von Bit- oder Analogmeldungen |
| `get_alarm` | `alarmName`, `alarmKind` | `hmiName`, `language` | nein | Eine Meldung im Detail |
| `list_alarm_classes` | - | `hmiName` | nein | Meldeklassen, Audit-Klassen, OPC-UA-Meldungstypen |
| `list_logs` | - | `hmiName` | nein | Daten-Logs und Meldungs-Logs mit Einstellungen, Segmenten, Sicherung |
| `list_logging_tags` | - | `hmiName`, `dataLogName`, `tagName` | ja | Logging-Variablen |
| `list_screens` | - | `hmiName`, `groupPath`, `language` | nein | Baum der Bildgruppen mit Bildern |
| `list_screen_items` | `screenName` | `hmiName` | ja | Objekte auf einem Bild |
| `list_faceplate_instances` | - | `hmiName`, `screenName` | ja | Faceplate-Instanzen und ihre Bindungen |
| `get_screen_navigation` | - | `hmiName` | nein | Navigationskanten zwischen Bildern |
| `get_runtime_settings` | - | `hmiName` | nein | Abschnitte der Runtime-Einstellungen |
| `list_script_modules` | - | `hmiName` | nein | JavaScript-Skriptmodule |
| `list_text_and_graphic_lists` | - | `hmiName` | nein | Textlisten und Grafiklisten |
| `list_project_languages` | - | - | nein | Projektsprachen |
| `validate` | `category` | `hmiName`, `name` | ja | Befunde der Openness-Validierung |

`alarmKind` ist `discrete` oder `analog`. `validate` nimmt `category` `tags`, `alarms`, `screens`,
`connections`, `logs`, `alarmClasses` oder `all`; `name` schränkt auf ein Objekt ein und ist mit
`all` nicht gültig. Die namensbezogenen Kategorien `memberTags`, `loggingTags` und `screenItems` erfordern `name` (siehe
[validate](#hinweise-zu-den-operationen)) und sind nicht Teil von `all`. `language` ist ein Kulturname wie `en-US`; `list_connections` und `list_alarm_classes`
haben kein `language`, weil Verbindungskommentare einfache Strings sind und Meldeklassen keine Texte tragen.

## Ergebniskonventionen

- **Variablen- und Meldungszeilen.** `list_tags`, `get_tag` und rekursive Member-Zeilen enthalten
  `displayName`-Textarrays und `plcName`. `list_alarms` und `get_alarm` enthalten `triggerBitAddress` für
  Bitmeldungen und `triggerAddress` für Analogmeldungen. Nicht lesbare/nicht anwendbare Member sind explizite Nullwerte;
  diese Ergänzungen halten den Lesevertrag bei `1.1`.
- **Paging.** Seitenweise Operationen nehmen `offset` (0 oder größer, Standard 0) und `limit` (1-2000, Standard
  100) und liefern `page { offset, limit, total, nextOffset, nextCursor }`. Die Reihenfolge ist ordinal ohne Beachtung der Groß-/Kleinschreibung,
  mit einem Gleichstandsentscheid unter Beachtung der Groß-/Kleinschreibung. Ein Aufruf ohne `cursor` durchsucht das Projekt erneut; siehe
  [Cursor-Paging](#cursor-paging) zum Fortsetzen eines Durchlaufs.
- **Vollständigkeit.** Jedes Ergebnis trägt `isComplete` und `messages`. Eine Eigenschaft, die Openness nicht lesen kann,
  wird zu `null` mit einer Meldung und `isComplete:false`. Eine nicht lesbare Komposition lässt das Element mit
  `worker_operation_failed` fehlschlagen. Die Wiederherstellung pro Eigenschaft gilt nur für
  `EngineeringTargetInvocationException` und `EngineeringNotSupportedException`; ein nicht mehr laufendes Portal lässt
  das Element fehlschlagen.
- **Werte.** `HmiVariant.value` ist ein echter JSON-Skalar, nie JSON in einem String. Texte sind
  `HmiText`-Arrays aus `{culture, text}` und werden roh geliefert: Ereignistexte von Meldungen behalten ihr
  `<body><p>...</p></body>`-Markup. Ein `language`-Filter, der eine Kultur nennt, die keine
  Projektsprache ist, lässt das Element mit `target_not_found` fehlschlagen und nennt die Projektsprachen.
- **Budgets.** Eine vollständige Antwort ist auf 180.000 Zeichen begrenzt und ein Wert auf 60.000; größere
  Werte werden als Ganzes ausgelassen, mit Hinweisen zum Eingrenzen (niedrigeres `limit`, ein eingrenzendes Feld hinzufügen oder
  die `operationId` allein erneut ausführen).
- **Fehler.** Classic-WinCC-Geräte (`Siemens.Engineering.Hmi.HmiTarget`) werden von
  `list_hmi_devices` aufgelistet, aber jede andere Operation lässt das Element mit `target_kind_unsupported` fehlschlagen ("... is a
  Classic (WinCC) HMI; only WinCC Unified HMIs are supported.", gefolgt von den Namen der klassischen Operationen). Das gilt auch, wenn `hmiName`
  weggelassen wird und das Projekt kein Unified HMI, aber mindestens ein Classic HMI hat. Classic-HMIs haben eigene
  Operationen (`list_classic_objects` und fünf `hmi_write`-Operationen, Issue #28); siehe [Klassisches WinCC und Unified-Erweiterungen](HMI_CLASSIC_UNIFIED_EXTENSIONS_SUMMARY.de.md). Selektor-Fehltreffer sind
  `target_not_found` oder `target_ambiguous`.

## Cursor-Paging

Die sieben seitenweisen Operationen (`list_tags`, `list_system_tags`, `list_alarms`, `list_logging_tags`,
`list_screen_items`, `list_faceplate_instances`, `validate`) liefern `page.nextCursor`, wann immer
`nextOffset` gesetzt ist, und null auf der letzten Seite. Ihn als `cursor` statt `offset` zurückgeben, mit derselben
Operation und denselben Filtern, um denselben Durchlauf fortzusetzen. `limit` darf sich zwischen den Seiten unterscheiden. Offset-Paging
bleibt voll unterstützt; ein Aufruf ohne `cursor` verhält sich wie bisher und durchsucht erneut.

- **Aufwand.** Der Worker hält die nach Namen geordneten Objekte des ersten Durchlaufs, sodass eine Fortsetzung nur
  ihre eigenen Zeilen liest. Live: 1.286 Faceplate-Instanzen auf einem Unified PC brauchten 21 s für die erste Seite und
  0,5-0,8 s pro Fortsetzung; eine reine Offset-Seite brauchte 21 s.
- **Veralten.** Die Objektliste wird eingefroren, wenn die erste Seite gelesen wird; jede Zeile wird live gelesen. Ein
  seitdem gelöschtes Objekt lässt seine eigene Zeile mit einer Meldung und `isComplete:false` fehlschlagen; die anderen Zeilen
  sind nicht betroffen. Seitdem hinzugefügte Objekte sind nicht enthalten; ein Aufruf ohne `cursor` sieht sie.
- **Lebensdauer.** Der Worker hält höchstens 4 Snapshots, der am längsten nicht verwendete wird zuerst verdrängt, jeder wird
  nach 10 Minuten ohne Fortsetzung verworfen. Jeder Durchlauf mit mehr Zeilen als seine Seite belegt einen Platz,
  auch reine Offset-Aufrufe, sodass ein Batch mit mehr als 4 mehrseitigen Elementen oder andere seitenweise Aufrufe
  dazwischen einen Cursor verdrängen können, der noch in Gebrauch ist.
- **Fehlschläge.** `cursor` zusammen mit `offset` oder `cursor` bei einer nicht seitenweisen Operation ist ein
  `validation_error`, der den ganzen Aufruf ablehnt, wie jeder andere Feldfehler. Jeder andere
  Cursor-Fehlschlag lässt nur sein Element fehlschlagen. Prüfungen vor jedem Worker-Aufruf:

  | Bedingung | Kategorie |
  |---|---|
  | leerer, fehlerhaft gebildeter oder manipulierter Cursor oder einer, der von einem früheren Serverprozess ausgegeben wurde | `invalid_cursor` |
  | andere Operation oder ein geänderter Filter (`hmiName`, `tableName`, `language`, `alarmKind`, `dataLogName`, `tagName`, `screenName`, `category`, `name`), auch wenn sich nur die Groß-/Kleinschreibung ändert | `cursor_filter_mismatch` |
  | Projektbindung hat sich geändert, seit der Cursor ausgegeben wurde | `cursor_binding_mismatch` |

  Im Worker ist ein abgelaufener oder verdrängter Snapshot `snapshot_unavailable`, und ein Snapshot aus
  einem anderen Projekt oder einer anderen Operation ist `cursor_snapshot_mismatch`. In jedem Fall ohne Cursor neu beginnen.
- **Worker-Neustart.** Ein Worker-Neustart verliert jeden Snapshot, macht aber auch die
  Projektbindung ungültig, sodass die nächste Fortsetzung mit `binding_conflict` fehlschlägt, nicht mit `snapshot_unavailable`. Nach
  `bind_project` schlägt der alte Cursor mit `cursor_binding_mismatch` fehl; ein Aufruf ohne Cursor stellt den Betrieb wieder her.
- **Lokale Sitzungen.** Cursor-Paging funktioniert auf einer kalt übernommenen lokalen `.als21`-Sitzung (live, 2026-10-12).

## Hinweise zu den Operationen

- **`get_tag`-Member.** Member werden rekursiv bis Tiefe 8 und insgesamt höchstens 100 Member-Zeilen gelesen
  (alle Ebenen zusammen). Das Erreichen einer der beiden Obergrenzen setzt `membersTruncated:true`; die Zeilenobergrenze fügt außerdem
  eine Meldung hinzu. Keine der beiden Obergrenzen macht das Ergebnis unvollständig.
- **`validate`.** Blättert über *durchsuchte* Objekte, sodass der Aufwand eines Aufrufs begrenzt ist; `total` ist die
  Anzahl der Objekte in der Kategorie. Saubere Objekte werden gezählt, nicht aufgelistet; jeder Befund trägt
  `objectKind` (den Openness-Typnamen, etwa `HmiIOField`), `name` und `results[]` aus
  `{propertyName, errors[], warnings[]}`. Objekte ohne `Validate()` werden einmal pro Art in
  `notValidatable` aufgelistet. Systemvariablen werden nicht durchsucht oder gezählt (`tags` und `all`). Ein nicht lesbares
  Validierungsergebnis wird nie als sauber gezählt. Drei namensbezogene Kategorien erreichen Objekte, die die anderen
  überspringen; `name` ist erforderlich, und sie sind nicht Teil von `all`:

  | Kategorie | `name` | Validiert | Befund-`name` |
  |---|---|---|---|
  | `memberTags` | Variable der obersten Ebene | jedes Member, rekursiv bis Tiefe 8 | `Motor_1.Speed` |
  | `loggingTags` | Variable der obersten Ebene | die Logging-Variablen der Variable | `Temp_1/Log_1` |
  | `screenItems` | Bild | jedes Objekt auf diesem Bild | `Main/Button_3` |

  Live-Beispiel: Ein EA-Feld, das an eine gelöschte Variable gebunden ist, wird unter `screenItems` gemeldet als
  `objectKind:"HmiIOField"`, `propertyName:"Dynamization"`, mit dem Fehler "The tag '...' for
  dynamization of the property 'Process value' does not exist."
- **`list_logging_tags` mit `dataLogName`.** Eine Logging-Variable, deren `DataLog` nicht gelesen werden kann, wird
  mit einer Meldung und `isComplete:false` aus dem Durchlauf ausgelassen. Die Meldung stammt vom durchsuchenden Aufruf;
  Cursor-Fortsetzungen durchsuchen nicht erneut und wiederholen sie nicht.
- **`get_runtime_settings`.** Einstellungswerte sind Strings. Der Abschnitt OpcUaServer trägt eine Teilmenge
  seiner Einstellungen. `GMPEnabled`, `GeneralESIGCommentsStrategy` und `TagOptimizationActive` stehen auf einer
  Positivliste: Wenn Openness eine davon auf dem Gerät als nicht unterstützt meldet, ist der Wert null mit einem Hinweis,
  und `isComplete` bleibt true. Jede andere nicht lesbare Einstellung setzt weiterhin `isComplete:false`. Ob
  sich die GMP- und ESIG-Einstellungen lesen lassen, hängt vom Gerät ab; manche Comfort Panels liefern sie nie.
- **Bildobjekte.** Die Geometrie stammt aus `IHmiBoxFeature`; Objekte ohne Box-Geometrie (zum Beispiel
  elliptische Bögen) melden eine Null-Geometrie ohne Meldung. Ein Bildfenster ohne Zielbild
  ergibt keine Navigationskante und eine Meldung (`isComplete` bleibt true).
- **Verbindungen.** Geheimnisse verlassen den Worker nie. Eine Treibereigenschaft, deren Name ein
  zugangsdatenähnliches Wort enthält (`password`, `passwd`, `passphrase`, `pwd`, `secret`, `token`, `credential`,
  `privatekey`, beliebige Groß-/Kleinschreibung), wird verworfen, bevor ihr Wert gelesen wird, und bei einer Eigenschaft, deren Name nicht
  gelesen werden kann, bleibt der Wert ungelesen. Dieselbe Prüfung verwirft passende `initialAddress.parsed`-Schlüssel und setzt
  `initialAddress.raw` auf null, wenn die Adresse einen nennt. Doppelte (Name, Wert)-Zeilen, die Openness
  meldet, werden zusammengefasst.
- **Nie gelesen.** `HmiTag.ConfirmationType` (es brachte TIA Portal im Spike zum Absturz) und
  `GmpRelevant` / `MandatoryCommenting`. Diese bleiben dauerhaft ungelesen; es gibt keine Probe.

## Schreibvorgänge (`hmi_write`)

Neun weitere Operationen für Bilder, Bildobjekte, Eigenschaften, Variablen-Dynamisierungen, Skripte und Batch-Umbenennungen von Bildern
und HMI-Variablen wurden von BetterTIA/writer übernommen; sie sind, mit der Herkunft und Bindung
jedes Siemens-Members, in der [Referenz der BetterTIA-Operationen](BETTERTIA_ADOPTED_OPERATIONS_SUMMARY.md#wincc-unified-screens-scripts-and-batch-renames-hmi_write) beschrieben.
Zwei weitere, `export_text_lists` und `import_text_lists` (Benutzer-Textlisten als SimaticML-Dateien; die Member sind von
Siemens für Unified nicht dokumentiert und werden zur Laufzeit gebunden), sind in der
[selben Referenz](BETTERTIA_ADOPTED_OPERATIONS_SUMMARY.md#tag-tables-and-wincc-unified-text-lists-as-files-owner-additions) beschrieben; insgesamt 20.
Der Rest dieses Abschnitts beschreibt die neun Schreibvorgänge für Variablentabellen, Variablen und Meldungen.

`hmi_write(operations, dryRun=false)` führt standardmäßig gegen ein verifiziertes, bereits geöffnetes Projekt aus.
Für eine Vorschau `dryRun:true` setzen. Jedes von 1–50 vom Aufrufer geordneten Elementen trägt eine eindeutige `operationId`
(höchstens 256 Zeichen), `operation` und ein optionales `hmiName`. Die HMI-Auswahl folgt
den Leseregeln; alle Elemente zielen auf das gebundene Projekt. Unbekannte Felder (einschließlich eines ausgemusterten Element-`projectPath`) schlagen
bei der Validierung vor jeder Worker-Aktivität fehl. Der Modus read-only registriert das Tool weder, noch erlaubt er seine neun
Worker-Methoden.

| Operation | Erforderlich | Optional |
| --- | --- | --- |
| `create_tag_table` | `tableName` | `groupPath` |
| `update_tag_table` | `tableName`, `newName` | – |
| `delete_tag_table` | `tableName` | – |
| `create_tag` | `tableName`, `tagName` und entweder `plcTag` oder `dataType` | `connection` (mit `plcTag`), `comment`, `displayName`, `acquisitionCycle` |
| `update_tag` | `tagName` | `newName`, `plcTag`, `connection` (mit `plcTag`), `dataType`, `comment`, `displayName`, `acquisitionCycle` |
| `delete_tag` | `tagName` | – |
| `create_alarm` | `alarmKind`, `alarmName`, `triggerTag`; analog zusätzlich `condition`, `conditionValue` | `alarmClass` (Standard `Alarm`), `priority`, `eventText`, `infoText`; nur discrete `bitNumber` (Standard 0), `triggerMode` |
| `update_alarm` | `alarmKind`, `alarmName` | `newName` und die für diese Art gültigen Create-Felder |
| `delete_alarm` | `alarmKind`, `alarmName` | – |

### Felder und Abhängigkeiten

- **Bindung.** Die Anfrageform verlangt `plcTag`, wenn `connection` angegeben ist; ein Create braucht
  `plcTag` oder `dataType`. Der Host prüft die Existenz der Verbindung nicht vorab, verlangt keine gebundene Variable
  für `plcTag` und lehnt `dataType` bei einer gebundenen Variable nicht ab. Siemens entscheidet diese semantischen Fälle während der
  Mutation; die Bindung legt keine Verbindungsobjekte an. Gequotete (`"DB".Member`) und nicht gequotete
  PLC-Pfade werden auf die nicht gequotete, punktgetrennte Form von Siemens normalisiert. Eine Vorschau kann weder die native Akzeptanz noch die
  Existenz der PLC-Quelle beweisen.
- **Namen.** Variablen gelten HMI-weit, Tabellen umfassen alle Gruppen, Meldungen sind nach Art abgegrenzt. `tableName` platziert
  eine neue Variable; bestehende Variablen verwenden `tagName` allein. Neue Namen dürfen nicht leer oder aufgefüllt sein und keine Steuerzeichen,
  `.` oder `"` enthalten. Bestehende Selektoren passen ohne Beachtung der Groß-/Kleinschreibung. Kollisionen bei Create/Umbenennen gehen an Siemens;
  mit einem öffentlichen Lesevorgang die resultierende Schreibweise und Objektidentität prüfen.
- **Werte.** `acquisitionCycle` ist `T100ms`, `T250ms`, `T500ms`, `T1s`, `T2s`, `T5s` oder `T10s`.
  Die Priorität ist 0–255. Das diskrete `bitNumber` ist mindestens null, mit seiner nativen typspezifischen Obergrenze;
  `triggerMode` ist `OnRisingEdge` oder `OnFallingEdge`. Die analoge `condition` ist `LowerLimit`, `UpperLimit`,
  `Equal`, `NotEqual`, `LowerLimitOrEqual` oder `UpperLimitOrEqual`. Ein endliches JSON-`conditionValue` verwendet
  Int32-Setter-Eingaben für ganzzahlige Werte im Int32-Bereich (einschließlich `50.0`), sonst Double.
- **Member und Referenzen.** `triggerTag` akzeptiert eine Variable oder einen rekursiven `Tag.Member`-Pfad. Der Host
  beweist vor der Mutation nicht die Existenz von Member, Trigger-Variable oder Meldeklasse. Es gibt keine
  Einschränkung Neubinden/Lesen/zweiter Aufruf für ein neu eingeführtes Member. Siemens kann eine ungültige
  Referenz akzeptieren; die betroffenen Objekte lesen, das öffentliche `validate` ausführen und das HMI-Gerät in der
  Oberfläche von TIA Portal übersetzen. Diese Prüfungen sind von der Schreibverifikation getrennt.
- **Texte.** `comment`, `displayName`, `eventText` und `infoText` sind nicht leere Arrays aus `{culture,text}`;
  nur angegebene Kulturen ändern sich. Doppelte Kulturen sind `validation_error`; eine nicht konfigurierte Sprache
  ist `target_not_found`. Reinen Text senden: Eingaben, die mit `<body` beginnen, werden abgelehnt; Zeilenumbrüche in EventText
  werden abgelehnt. Der Server maskiert kaufmännische Und-Zeichen/spitze Klammern im XML-Text, erhält literale
  Anführungszeichen und umschließt den Ereignistext als `<body><p>…</p></body>`. Literale Entitäten bleiben literaler Text.
  Andere Textfelder werden so geschrieben, wie sie angegeben sind. Effekte verwenden skalare `field[culture]`-Werte; rohes Markup der Rücklesung
  ist keine Schreibeingabe.

### Guards und Ergebnisse

| Guard | Stufe | Wird ausgelöst, wenn |
| --- | --- | --- |
| `hmi_state_unverifiable` | info | Erforderliche Nachweise unvollständig sind oder die anfängliche Löschplanung mehr als 200 Querverweisabfragen braucht; das Element läuft und TIA Portal entscheidet (Block bis 2026-11-01). |
| `hmi_deletes_table_contents` | info | Das Löschen einer Tabelle auch ihre inventarisierten Variablen löscht. |
| `hmi_deletes_referenced_tag` | info | Gelöschte Variablen inventarisierte Verwender in Meldungen/Bildern/Skripten haben; Referenzen können ins Leere zeigen. |

Die Planung sammelt pro aufgelöstem HMI einen anfänglichen Nachweissatz aus öffentlichen Lesevorgängen für Update-/Delete-Ziele
und Löschfolgen über sequenzielle, nicht atomare Lesevorgänge und simuliert dann die Effekte in der Reihenfolge des
Aufrufers. Die Obergrenze von 200 Abfragen umfasst ihre
Querverweis-Lesevorgänge für vorher bestehende Variablen der obersten Ebene,
die der Aufruf entfernt, einschließlich Tabelleninhalten. Es gibt keine Neuplanung pro Element und keine Rücklesung
durch den Worker nach dem Schreiben. Löscheffekte verwenden die anfänglichen Nachweise plus die Simulation in Aufruferreihenfolge; keine
nativen Folgelesevorgänge aktualisieren sie nach der Mutation. Löschungen auf mehrere Aufrufe verteilen, wenn die anfänglichen
Nachweise die Obergrenze überschreiten würden.

Fehlschläge des Bindungs-Gates behalten ihre ursprüngliche Kategorie, einschließlich `worker_timeout`, `worker_crashed`,
`portal_lost` und `protocol_error`, im Fehler auf oberster Ebene und im Audit. Eine Ablehnung durch das Gate stoppt vor der Planung
oder Mutation; eine fehlende, nicht passende oder geänderte Bindung ist `binding_conflict`.

Die gemeinsame geschützte Pipeline hält die Bindung über Planung, Mutation, Verifikation und Audit fest.
Es gibt keine Elicitation, kein Bestätigungsargument, keine Bestätigungseingabe und kein Safety-Token. Die Ausführung stoppt
beim ersten fehlschlagenden Setter/Element; es gibt kein Batch-Rollback und keine automatische Wiederholung. Typisierte Ergebnisse
behalten geordnete `appliedProperties`, `failedProperty` und beim Create `cleanup` (`notNeeded`, `deleted`,
`failed`, mit Diagnose). `appliedProperties` listet abgeschlossene Stufen, keinen atomaren Änderungssatz:
Eine fehlschlagende mehrsprachige `Comment`-Stufe kann bereits eine frühere Kultur geändert haben, obwohl
`Comment` nicht aufgeführt ist. Ein fehlgeschlagenes Create versucht ein identitätsgeprüftes Löschen des neuen Objekts;
die Bereinigung kann fehlschlagen und es zurücklassen. Ein fehlgeschlagenes Update behält frühere Änderungen. Den tatsächlichen Zustand
vor einem erneuten Versuch mit öffentlichen Lesevorgängen prüfen.

Eine `EngineeringTargetInvocationException` während einer Mutationsmethode ist `engineering_rejected`;
andere Siemens-`EngineeringException`-Fehler sind `worker_operation_failed`, und `portal_lost`
hat Vorrang. Das bedingte `engineeringError` des Elementfehlers enthält `type`, die wörtliche
`message` und `inner[]`, begrenzt auf 4.000 Meldungszeichen und acht innere Ausnahmen. HMI-Wrapper
erhalten diesen Nachweis, außer wenn `portal_lost` Vorrang hat. Ein Teilergebnis ist nur
nach der Writer-Zuteilung vorhanden; die HMI-Auflösung kann vor diesem Punkt fehlschlagen.
Worker-Ablehnungen behalten ihre Kategorie, etwa `target_not_found` für eine fehlende Kultur; diese
Kategorie allein belegt nicht, dass sich nichts geändert hat. Zum Beispiel lautet die erfasste Meldung eines Creates einer
doppelten Variable:

```json
"Error when calling method 'Create' of type 'Siemens.Engineering.HmiUnified.HmiTags.HmiTagComposition'.\r\n\r\n-The value must be unique.\r\n"
```

Die gemeinsame Verifikation erfasst abgeschlossene Elemente als `trusted` oder `superseded`, ohne Rücklesung durch den Worker
oder Validate-Aufruf. Fehlgeschlagene/übersprungene Elemente haben keinen Verifikationseintrag. Ein später versuchtes
Element kann einen früheren Erfolg überschreiben, selbst wenn es nach einer möglichen Mutation fehlschlägt, einschließlich
eines ergebnistragenden `target_not_found`; seine Verifikationsmeldung benennt die Unsicherheit.
Daher kann `verification.success:true` neben dem Wurzel-`success:false` bestehen. Mutationen machen
die Lese-Snapshots dieses HMI ungültig; anfängliche Planungslesevorgänge können ebenfalls Snapshot-Plätze belegen oder verdrängen.

Schreibantworten verwenden `contractVersion:"2.0"` (Lesevorgänge bleiben `1.1`), die Phasen `preview`, `applied`, `blocked`, `error`, typisierte
Effekte/Ergebnisse/Verifikation, explizite Nullwerte und standardmäßige Zeichenbudgets von 180.000 pro Dokument/60.000 pro Wert,
beim Start erhöhbar.
Ablehnungen haben einen Fehler auf oberster Ebene; ein versuchter Mutations-/Verifikationsfehlschlag hat `success:false`,
`error:null`, MCP `isError:false` und kann Änderungen behalten. Auslassungen ganzer Werte können die Auslieferung fehlschlagen lassen,
während wahre Ausführungszusammenfassungen erhalten bleiben. Jeder eingetretene Aufruf, einschließlich Vorschau/Ablehnung, erzeugt
einen Audit-v2-Datensatz mit dem exakt ausgelieferten kanonischen Text/Hash. Die tatsächliche Bestätigung ist `none` in
read-write und `policy` in full.

Schreibvorgänge in Einzelplatzprojekten und lokalen Sitzungen verwenden die aktive Engineering-Wurzel; sie speichern nicht implizit,
übersetzen nicht, aktualisieren nicht vom Project Server und checken nicht dort ein. `save_project` explizit verwenden; ein lokales Speichern
ruft nur `LocalSession.Save()` auf.

### Antwortdaten beim Schreiben

Die Wurzel ist `{tool, contractVersion, phase, success, error, warnings, guards, effects, batch,
verification, omission}`. Text und strukturierter Inhalt sind dasselbe kanonische Dokument.

| Wert | Felder |
| --- | --- |
| `effects[]` | `operationId`, `effect`, `omission` |
| `effect` | `operation`, `target`, `changes`, `removes`, nullbares `dependsOn` (Darstellungsmetadaten, keine Anforderung einer Neuplanung) |
| `target` | `kind`, `hmiName`, `name`, `alarmKind`, `tableName`, `groupPath` |
| `changes[]` | `field`, `current`, `requested`, `derived` |
| `removes` | `tags[]`, `references[]`; jede Referenz hat `tag`, `name`, `typeName`, `path` |
| Element-`result` | `objectName`, geordnete `appliedProperties[]`, `failedProperty`, `cleanup`, `cleanupMessage` |
| `verification` | `success`, `operations[]`, `omission`; jeder Eintrag hat `operationId`, `success`, `check`, `expected`, `actual`, `message` |

Ein versuchtes, fehlgeschlagenes Element kann sein typisiertes `result` neben `failure` behalten; es ist weder ein
Erfolgsergebnis noch ein Beweis für einen unveränderten Zustand. Eine Vorschau hat kein Mutationsergebnis und keine Garantie einer Rücklesung.

### Aktuelle Qualifizierung

Der [Qualifizierungsnachweis zu nativen Ablehnungen](../superpowers/acceptance/reports/2026-10-30-hmi-write-native-rejections-live-verification.md)
dokumentiert eine abgeschlossene Qualifizierung innerhalb des erfassten Umfangs R1/R2/R3. Die frische Matrix mit 35 Fällen lief auf dem installierten
`4.2.0-local.112.g96a28b7`: 31 P-Varianten ergaben 21 native O1-Ablehnungen, drei als O2 akzeptierte
ungültige Referenzen, fünf akzeptierte Kontrollfälle und zwei Worker-Ablehnungen, dazu L1–L4. Die O1-Fehlertypen
und -Meldungen stimmen mit dem nativen Checkpoint überein. O2-Referenzen blieben in öffentlichen Lesevorgängen sichtbar und
wurden vom öffentlichen Validate gemeldet; ein Schreiberfolg bescheinigt nicht ihre Gültigkeit.

| Erfasster nativer Fall | Tool-Ergebnis / beobachteter Zustand |
| --- | --- |
| Doppelte Tabelle/Variable/Meldung oder kollidierende Umbenennung; Umbenennung einer Variable nur in der Groß-/Kleinschreibung; Löschen der Standardtabelle | O1 `engineering_rejected`; ursprüngliche Objekte blieben erhalten |
| `dataType` bei gebundener Variable, `plcTag` ohne Verbindung oder fehlende Verbindung gefolgt von `PlcTag` | O1-Setter-Ablehnung, abgeschlossene Stufen bleiben erhalten; fehlgeschlagene neue Variablen wurden bereinigt |
| Variable in fehlender Tabelle anlegen | O1 native `Create`-Ablehnung; keine Ersatzplatzierung und kein automatisches Anlegen der Tabelle |
| Fehlende Meldeklasse, Trigger-Variable oder fehlendes Struktur-Member | O2 akzeptierte Referenz; das öffentliche Validate meldet das fehlende Objekt; das frühere Übersetzen in der UI am Checkpoint diagnostizierte diese Fälle ebenfalls |
| Wiederverwendung eines Meldungsnamens über Arten hinweg, Umbenennung einer Tabelle/Meldung nur in der Groß-/Kleinschreibung, Löschen-dann-Anlegen einer Schreibvariante | Akzeptierte Kontrollfälle; beabsichtigte Objekte/Lesezustand und das öffentliche Validate stimmten überein |
| Fehlende Tabellengruppe oder Textkultur | Worker `target_not_found`; die Gruppenablehnung geht dem Create voraus, während die Kulturablehnung der Mutation und Bereinigung folgen kann |

Die [Tabelle der 31 Varianten und kanonischen Eingaben](https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/acceptance/reports/inputs/2026-10-30-hmi-write-native-rejections-live-cases.md)
bewahrt exakte Ergebnisse, unabhängige Lesevorgänge, die öffentliche Validierung und die Wiederherstellung. Die Beobachtungen in der UI
am Checkpoint sind keine frischen Übersetzungen jeder Kandidatenvariante.

Die fokussierte Live-Prüfung der R1-Korrektur bestand auf dem installierten `.113` / `31d1a44` und stellte die gespeicherte
Baseline wieder her. Sie zeigte ein konservatives Überschreiben nach einem späteren Teilfehlschlag. Die vollständige
Offline-Suite bestand 7.644 Tests mit 95,05 % bereichsbezogener Abdeckung; das ist von der
nativen/UI-Qualifizierung getrennt. Die manuellen Nachweise bzw. die Nachweise aus lokalen Sitzungen auf `.113` sind akzeptiert und vollständig: Der Maintainer
meldete fehlerfreie Übersetzungen für P10, P12-discrete, L1, L2 und L5; L4 ergab die erwarteten zwei
Fehler wegen fehlender Variablen und null Warnungen. Sechs R3-Ausnahmen verwenden den früheren Checkpoint mit frischen
Kandidaten-Lesevorgängen/Validate wieder, statt frischer Übersetzungen der Kandidaten.

Die beiden Bereinigungselemente von L5 gelangen und ihr vollständiges Inventar stimmte mit der lokalen Baseline überein: sieben
Tabellen, 72 Variablen und 78 Meldungen. `isModified` wechselte trotz wiederhergestelltem Inventar von false auf true;
die Probe hat weder gespeichert noch eingecheckt. Der Maintainer meldete dann die manuelle Bereinigung und das Öffnen des
Einzelplatzprojekts. Die Zeitmessung danach auf `.113` ist vollständig: Die Wurzel-Mediane aus drei gemessenen Stichproben
sind H1 12.117 ms, H2 128.725 ms, H3 33.934 ms, H4 34.542 ms und H5 17.824 ms. Der Vergleich
verwendet das installierte `75c0568` als Baseline, eine erfasste Abweichung vom geplanten `0b2640a`.
Weder eine vollständige erneute Ausführung der `.113`-Matrix noch ein globaler Beweis der Abwesenheit von O3 noch eine Qualifizierung induzierter Races/Fehler wird beansprucht.

## Bekannte Grenzen

- Durchläufe über das ganze Projekt dauern auf einer großen PC-Station unabhängig von `limit` etwa 21-23 s pro Aufruf;
  mit `screenName` oder `tableName` eingrenzen und mit dem Cursor fortsetzen.
- Jeder Locator-Aufruf, einschließlich Treffern im behaltenen Cache, durchläuft frisch, um die aktuelle
  Projektzugehörigkeit und die Eindeutigkeit des Selektors zu prüfen. Behaltene Einträge werden bei der nächsten Verwendung des Locators nach einem
  Wechsel des Engineering-Eigentümers geleert; ein weggelassener Selektor füllt einen schlüsselbasierten Eintrag nicht wieder. Der Maintainer behielt
  den Cache und ließ O1 offen: Die geplante Einsparung beim Durchlaufen wird nicht erbracht. Die früheren etwa
  6 s pro `validate screenItems`-Element sind historische Baseline-Nachweise.
- Eine direkte Umbenennung einer Variable nur in der Groß-/Kleinschreibung kann am Siemens-Setter fehlschlagen, wobei die ursprüngliche Variable erhalten bleibt.
  Ein Erfolg bei Tabellen/Meldungen nur in der Groß-/Kleinschreibung qualifiziert keine Variablen.
  Zwei explizite, vom Aufrufer verantwortete Umbenennungen über einen eindeutigen Zwischennamen funktionierten bei einem früheren
  Kandidaten; es gibt keine verborgene Umbenennung durch den Server. Den Teilzustand vor einem erneuten Versuch prüfen.
- Das Löschen der Standard-Variablentabelle kann nativ fehlschlagen; keine öffentliche Eigenschaft identifiziert sie zuverlässig, daher wird
  kein namensbasierter Guard verwendet. Eine Bindung an eine einfache Struktur kann angewendet werden, aber die Validierung nicht bestehen, was eine explizite
  Bereinigung der angelegten Variable erfordert.
- `get_tag` mit einem punktgetrennten UDT-Membernamen schlägt mit `target_not_found` fehl, ohne den Hinweis auf den Member-Pfad, den
  `read_cross_references` gibt.
- Gleitkommawerte werden explizit geschrieben (`G15`, oder `G17`, wenn 15 Stellen nicht verlustfrei zurückwandeln;
  `G7`/`G9` für float), sodass net10 und net48 denselben Text erzeugen. Der nicht ganzzahlige Anfangswert einer Variable
  kommt von Openness als String (`System.String`), daher ist die Kodierung nicht ganzzahliger `Double`-Werte weiterhin nicht
  live beobachtet.
- Lücken bei Live-Nachweisen: nicht standardmäßige Verbindungstreiber (keine OPC-UA-Verbindung war verfügbar),
  Live-Validierungswarnungen, nicht standardmäßige Object-Werte und eine native Zuweisung nur der Verbindung ohne
  einen nachfolgenden `PlcTag`-Setter. Die öffentliche Anfrageform verlangt weiterhin `plcTag` mit `connection`;
  die spätere `PlcTag`-Ablehnung der Probe mit fehlender Verbindung qualifiziert diesen isolierten Setter-Pfad nicht.

Siehe das [Verbesserungsprotokoll](../IMPROVEMENT_LOG.md#open-hmi_read-follow-ups-2026-10-12) für Folgearbeiten.
Der [frühere Bericht zur Schreib-Abnahme](../superpowers/acceptance/reports/2026-10-14-hmi-write-live-acceptance.md)
qualifiziert seine historische funktionale Verifikation in beiden Modi auf der Quelle `48f2edd`. Die Darstellung im Editor bleibt eine
Maintainer-Prüfung an einem früheren Kandidaten. Die Performance verwendet eine archivierte Baseline ohne Schreib-Tool:
Abgeglichene Vergleiche zwischen Baseline und Schreiben und das Gate mit drei Wiederholungen für den Eigentümerwechsel bleiben
unqualifiziert; die einzelne Beobachtung eines Eigentümerwechsels verfehlte die vorgeschlagene Bedingung einer Speicherrückgabe von 10 %.

## V21-Fallstricke

Von rickgaiser/TiaMcp gemeldet und hier nicht auf V21 verifiziert; siehe [V21-Fallstricke](V21_PITFALLS.de.md) und dessen
Live-Abnahmeliste.

- Ein geöffneter HMI-Variableneditor in TIA Portal (zum Beispiel "Show all tags") verlangsamt Berichten zufolge jeden
  Openness-Aufruf auf diesem HMI, ein Lesen oder Schreiben seiner Variablentabellen um etwa das Achtfache. Ihn schließen, während `hmi_read` oder
  `hmi_write` an diesem HMI arbeiten; der Server kann einen geöffneten Editor nicht erkennen.
- Das Exportieren einer Grafikliste und das Lesen von `HmiSystemDiagnosisControl.ScriptDiagnosisOverviewText` bringen
  Berichten zufolge TIA Portal V21 zum Absturz. Keine `hmi_read`-Operation tut eines davon: `list_text_and_graphic_lists`
  liest nur die Namen von Grafiklisten, und `list_screen_items` liest nur typisierte Geometrie, `Visible` und
  `Enabled`. Ob `validate` mit `screenItems` auf einem Systemdiagnose-Control diese Eigenschaft
  innerhalb von TIA Portal erreicht, ist unbekannt (Live-Fall L9).
- Ein Classic-Bild mit doppelter `<Number>` beendet Berichten zufolge TIA Portal; `import_classic_objects` prüft die
  Bildnummern, bevor es Bilder importiert, und eine klassische Grafikliste wird nie exportiert ([Klassisches WinCC und Unified-Erweiterungen](HMI_CLASSIC_UNIFIED_EXTENSIONS_SUMMARY.de.md)).

## Querverweise von HMI-Variablen

`read_cross_references` nimmt ein vorläufiges Ziel `{ hmi: { hmiName, tagName } }` für eine Unified-HMI-Variable
der obersten Ebene; siehe
[Abdeckung der Querverweise](PLC_OPERATIONS_SUMMARY.md#cross-reference-coverage-read_cross_references).

## Außerhalb des Umfangs

SiVArc, YAML- oder JavaScript-Exportinhalte, ein generischer
Attribut-Dump, HMI-Übersetzen und Verbindungen auf Hardware-Ebene. Classic-WinCC-Objekte, Unified-Ereignisse und -Dynamisierungen (über `set_dynamization_tag` hinaus), Audit-Klassen, das Schreiben von Runtime-Einstellungen und Anlagensichten decken nur die Operationen in
[Klassisches WinCC und Unified-Erweiterungen](HMI_CLASSIC_UNIFIED_EXTENSIONS_SUMMARY.de.md) ab.
Schreibvorgänge schließen außerdem aus: Anlegen/Bearbeiten von Verbindungen, Änderungen an Variablentabellen-Gruppen, Verschieben/Entbinden von Variablen,
`accessMode`, `acquisitionMode`, Quittiervariablen, `EventText1`–`EventText9`, Parameterfelder von Meldungstexten,
Logging- und Bildänderungen. `compile_check` behält seinen nur auf PLC bezogenen Vertrag.

Verwandt: [README.md](README.de.md) für den Operationsindex und
[DEVICES_OPERATIONS_SUMMARY.md](DEVICES_OPERATIONS_SUMMARY.de.md) für Hardware-Informationen.
