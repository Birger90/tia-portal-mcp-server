# KOP und FUP als SIMATIC-SD-Text schreiben (TIA Portal V21)

**Sprache:** [English](lad-fbd-simatic-sd.md) · Deutsch

Dieser Leitfaden richtet sich an einen Agenten (oder eine Person), der KOP- (LAD) und FUP-Netzwerke (FBD) über diesen
Server schreibt oder ändert. Er sammelt die Syntax von SIMATIC SD (`.s7dcl` / `.s7res`) mit dem Beleg für jedes Konstrukt, damit
nichts auf Vermutung hin geschrieben wird. Die mit **L** gekennzeichneten Zeilen wurden von diesem Fork am 2026-11-02 auf TIA Portal V21 live verifiziert
(Testprojekt `MCP_Livetest_01`, [Live-Bericht](../superpowers/acceptance/reports/2026-11-02-simatic-sd-and-safety-live.md)): Ein FB mit 15 KOP-/FUP-/SCL-Netzwerken wurde
mit `update_block_logic` Format `sd` (Override) mit 0 Übersetzungsfehlern importiert, und sein erneuter Export entspricht dem
eingereichten Code für jedes mit **L** gekennzeichnete Konstrukt; nur Leerraum wich ab. Jede andere Zeile ist weiterhin unverifiziert.

## Warum SIMATIC SD und nicht SimaticML

Siemens' V21-Openness-Skill `simatic-sd` (tia-portal-ai-extensions, MIT, Siemens AG, Commit `f34f3e2`) nennt den
SD-Round-Trip (Export → Textänderung → Import) „den richtigen Weg, über Openness KOP-/FUP-/SCL-Netzwerke zu
bestehenden Bausteinen hinzuzufügen“. Ein KOP-Netzwerk ist in SimaticML ein `FlgNet`-Graph aus `Part`-, `Access`- und `Wire`-Elementen mit UIds;
dasselbe Netzwerk ist in SD ein paar Zeilen Text. SimaticML bleibt verfügbar (`get_block_content` `xml`,
`update_block_logic`) für das, was SD nicht ausdrücken kann (siehe V21-Fallstrick 1: AT-Überlagerungen, einige Konstrukte, die der SD-Import
Berichten zufolge verändert).

## Legende der Belege

| Kennzeichen | Quelle |
| --- | --- |
| **S** | Siemens-Skill `simatic-sd` (V21), Primärquelle |
| **E** | Beispiel eines Dritten in Importform, das sein Autor als in V21 importiert und mit 0 Fehlern übersetzt meldet, mit der passenden SimaticML daneben: `1748477582/tia-portal-openness-mcp` `tools/tiaportal-mcp/skill/lad-cookbook` (MIT, Commit `8e7ca42`, `MCPVerify_FC_LAD`, `MCPVerify_FB_LAD_v3`) |
| **D** | Beispiel eines Dritten unbelegter Herkunft: BetterTIA/editor `tests/samples/Demo_Line` (MIT, Commit `3d115b3`), asckye/TIA_Portal_Openness_MCP `reference/tool-examples/languages` (MIT, Commit `2767520`) |
| **R** | Berichtetes Verhalten, kein Beispiel: BetterTIA/writer README/TODO (MIT, Commit `fc8179c`) |
| **L** | Von diesem Fork auf TIA Portal V21 live verifiziert, 2026-11-02 (Import mit `update_block_logic` Format `sd`, 0 Übersetzungsfehler, erneuter Export bis auf Leerraum gleich dem eingereichten Code; [Live-Bericht](../superpowers/acceptance/reports/2026-11-02-simatic-sd-and-safety-live.md)) |

Ein nur mit **D** gekennzeichnetes Konstrukt kann falsch sein. Schreibe es, importiere es und lass den Import von TIA Portal und `compile_check`
entscheiden; behandle eine D-Zeile nie als Tatsache.

## Dokumentstruktur

```
{
    S7_Optimized := "TRUE";
    S7_PreferredLanguage := "LAD";
    S7_Version := "0.1"
}
FUNCTION_BLOCK "MyFB"
    VAR_INPUT
        Start : Bool;
    END_VAR
    VAR_OUTPUT
        Run : Bool;
    END_VAR

    { S7_Language := "LAD" }
    NETWORK
        RUNG wire#powerrail
            Contact( #Start )
            Coil( #Run )
        END_RUNG
    END_NETWORK
END_FUNCTION_BLOCK
```

| Konstrukt | Hinweise | Beleg |
| --- | --- | --- |
| Header-Pragma-Block mit `S7_Optimized`, `S7_PreferredLanguage`, `S7_Version` | Rückfallsprache auf Bausteinebene | S |
| Header-Schlüssel `S7_BlockNumber`, `S7_BlockTitle`, `S7_BlockComment` (IDs in die `.s7res`) | | E |
| Header-Schlüssel `S7_Safety := "TRUE"` | kennzeichnet einen fehlersicheren Baustein | D |
| `FUNCTION_BLOCK` / `FUNCTION "Name" : RetType` / `ORGANIZATION_BLOCK` / `DATA_BLOCK` und ihre `END_`-Schlüsselwörter | | S |
| `FUNCTION "Name" : Void` | | D |
| `VAR_INPUT`, `VAR_OUTPUT`, `VAR` (statisch), `VAR_TEMP` … `END_VAR` | | S (VAR_INPUT), E (die anderen) |
| Netzwerk-Pragma in einer Zeile `{ S7_Language := "LAD" }` unmittelbar vor `NETWORK` | darf nie von seinem `NETWORK` getrennt werden | S |
| Netzwerk-Pragma als mehrzeiliger Block `{ S7_Language := "LAD"; S7_NetworkComment := "MLC_4zs"; S7_NetworkTitle := "MLC_463" }` | Schlüssel durch `;` getrennt, der letzte ohne | E |
| Die Werte von `S7_NetworkTitle` / `S7_NetworkComment` sind IDs von `.s7res`-Einträgen | | E |
| `.s7res`: `MultiLingualTexts:`, dann `- id: MLC_x` und je Sprache eine Zeile `<culture>: text` | TIA vergibt eigene IDs für Texte, die es anlegt (`MLC_4SY`, `MLC_3VL` live gesehen); vom Aufrufer gewählte IDs (`MLC_T1`, `MLC_K1`) wurden von `create_block_from_document` akzeptiert | E, L (vom Aufrufer gewählte IDs, de-DE-Texte) |
| Jede Kultur in der `.s7res` muss eine Sprache des Projekts sein | `create_block_from_document` mit `en-US`- und `de-DE`-Texten schlug live fehl: "Cannot import multilingual text with culture 'en-US' in File …s7res: Document option "Override" is not valid for Multilingual text."; dasselbe nur mit `de-DE`-Texten gelang (die Sprachen des Projekts sind vermutlich nur de-DE, nicht verifiziert) | L |
| Ein KOP-Bausteinimport braucht en-US-Texte für jedes `.s7res`-Element, sonst kann er fehlschlagen | berichtet von BetterTIA (TODO, Stand 2025-09-02) und 1748477582; **live widerlegt am 2026-11-02 in einem de-DE-Projekt**: Eine Kultur, die das Projekt nicht hat, lässt den Import fehlschlagen | R, widerlegt |
| Überhaupt keine `.s7res` | Der eigene Export von TIA Portal V21 eines Bausteins ohne Texte besteht nur aus der `.s7dcl` (UTF-8-BOM, CRLF); `create_block_from_document` akzeptiert seit 2026-11-02 ein Bündel ohne `.s7res` | L (Export) |
| Dateien dürfen mit einem UTF-8-BOM beginnen | beibehalten | E |
| SCL-Netzwerk in einem KOP-Baustein: `{ S7_Language := "SCL" }`, dann `NETWORK`, SCL-Anweisungen, `END_NETWORK` | | S, L |

## KOP (LAD)

Ein Netzwerk enthält ein oder mehrere `RUNG … END_RUNG`. `RUNG wire#powerrail` beginnt an der linken Stromschiene. Elemente in einem
Rung liegen in Reihe, oben nach unten = links nach rechts.

| Element | Syntax | Beleg |
| --- | --- | --- |
| Schließer | `Contact( #Tag )` / `Contact( "GlobalTag" )` | S, L |
| Öffner | `I_Contact( #Tag )` | R, D, L |
| Spule | `Coil( #Out )` | S, L |
| Setz- / Rücksetzspule | `S_Coil( #Out )`, `R_Coil( #Out )` | E, L |
| Kontakt für positive / negative Flanke | `P_Trig( #EdgeMemory )`, `N_Trig( #EdgeMemory )` | E, R (P_Trig), D (N_Trig), L (beide) |
| Verknüpfungsergebnis invertieren | `Not()` | E, L |
| Vergleichskontakt | `{ S7_Templates := "SrcType := Int" }`, dann `LT_Contact( in1 := #A, in2 := #B )`; außerdem `GT_Contact`, `EQ_Contact`, `NE_Contact` | E (LT, GT), R (EQ, NE), L (GT, EQ mit der `SrcType`-Vorlage) |
| Move-Box | `Move( in := 0, out1 => #Target )` | E, R, L (TIA schreibt in seinem Export eine Leerzeile innerhalb des RUNG vor `Move(`) |
| Sub / Mul / Div | wie Add | R |
| Operandennamen, die Anführungszeichen brauchen | `Contact( #"A" )`, deklariert als `"A" : Bool;` | E |
| Add-Box | `{ S7_Templates := "SrcType := Int" }`, dann `Add( in1 := #A, in2 := 1, out => #A )` | E, L (TIA exportiert die Vorlage als mehrzeiliges Pragma) |
| IEC-Zeitglied, Instanz in einer Multiinstanz | `{ S7_Templates := "time_type := Time" }`, dann `#tonInst.TON( pt := T#500ms, et =>  )` mit `tonInst : TON_TIME` in `VAR`; `TOF` mit `TOF_TIME` entsprechend | S (TON mit Vorlage), E (Multiinstanz), L (TON- und TOF-Multiinstanzen mit der `time_type`-Vorlage) |
| IEC-Zeitglied, Instanz-DB | `"DB_T_Rundown".TOF( pt := T#500ms, et =>  )` | S (TON auf einem DB), D (TOF) |
| Ein nicht verwendeter Ausgang | `et =>  ` (leer nach `=>`) | S |

**Parallelzweige** verwenden benannte Wires: `wire#wN` innerhalb eines Rungs greift einen Knotenpunkt ab, `RUNG wire#wN` beginnt dort einen Zweig, und `… END_RUNG wire#wN` führt ihn wieder darin zusammen (ODER zweier Kontakte von der Stromschiene: E, L; verschachtelte Zweige unten: D, L). Ein Rung darf auf einem Wire enden, und ein anderer Rung darf auf ihm beginnen:

```
RUNG wire#powerrail
    Contact( "Sensor_A" )
    wire#w1
    Contact( "Sensor_B" )
    wire#w2
    Coil( "Output_1" )
END_RUNG
RUNG wire#w1
    Contact( "Sensor_C" )
END_RUNG wire#w2
```

liest sich als `Sensor_A AND (Sensor_B OR Sensor_C) → Output_1`. Zwei Zweige von der Stromschiene, die sich bei `w1` vereinen:

```
RUNG wire#powerrail
    Contact( "Button_1" )
    I_Contact( "Button_2" )
END_RUNG wire#w1
```

Mehrere Spulen oder Boxen nach einem Wire (zweimal `RUNG wire#w1`) sind parallele Ausgänge.

`{ S7_Templates := "…" }` steht in der Zeile vor der Anweisung, die es typisiert, und muss beibehalten werden (S).

## FUP (FBD)

`RUNG <first input>` beginnt eine Kette; jedes folgende Element nimmt das vorherige Ergebnis als seinen ersten Eingang
(Beleg D, sofern nicht anders angegeben; **L**, wo gekennzeichnet).

| Element | Syntax |
| --- | --- |
| UND- / ODER- / XOR-Box | `A( in2 := #B )`, `O( in2 := #B, in3 := #C )`, `X( in2 := #B )` (L) |
| Negierter Eingang | `in2 := NOT #Fault` (L) |
| Zuweisung / negierte Zuweisung | `Coil( #Out )`, `NOT Coil( #Out )` |
| Setzen / Rücksetzen | `S_Coil( #M )`, `R_Coil( #M )` |
| SR-Box | `S_SR( r1 := #Clear, operand := #Mem )` |
| Flanke | `P_Trig( #EdgeMem )`, `N_Trig( #EdgeMem )` |
| Konstanter Start | `RUNG true` / `RUNG TRUE` |
| Box-Ausgang speist den Eingang einer anderen Box | der Box-Rung endet mit `END_RUNG wire#w1`, der Verwender schreibt `in2 := wire#w1` |
| Mehrere Ausgänge einer Kette | `wire#w2` in der Kette, dann `RUNG wire#w2` |
| Rung, der an einer Box beginnt (kein erster Eingang) | `RUNG` allein, dann der Aufruf |

## Fehlersichere Programme (F-KOP / F-FUP)

- Fehlersichere FBs und FCs werden nur in FUP oder KOP programmiert (Siemens „SIMATIC Safety – Projektieren und
  Programmieren“, V21). SCL ist für F-Bausteine nicht verfügbar.
- Ein fehlersicherer SD-Baustein trägt `S7_Safety := "TRUE"` in seinem Header (D).
- Sicherheitsanweisungen werden wie FBs aufgerufen, mit einer Multiinstanz in `VAR`, z. B. `EStop : ESTOP1;` und
  `#EStop.ESTOP1( E_STOP := "Emergency_Stop", ACK_NEC := NOT #Ack_Required, ACK := wire#w1, TIME_DEL :=  ,
  Q => #EStop_OK, Q_DELAY =>  , ACK_REQ => #Ack_Request, DIAG => )`, mit einem vorangestellten inneren Pragma
  `{ f_user_card := "1"; f_image_card := "0" }` (D; die Bedeutung der beiden Schlüssel ist nicht dokumentiert — kopiere sie aus einem
  Export, erfinde sie nie). `SFDOOR` folgt derselben Form (D).
- In der fehlersicheren Programmierung können EN/ENO nicht verschaltet werden (Siemens Distributed Safety Getting Started).
- Jeder Schreibvorgang eines fehlersicheren Bausteins verlangt den Abnahmehinweis (`plc_safety_acceptance_required`). Validierung,
  Abnahme und der Ausdruck bleiben bei einer qualifizierten Person.
- **Der Main-Safety-Baustein einer F-Ablaufgruppe wird nie per SD importiert (L, 2026-11-02).** Ein SIMATIC-SD-Import dieses Bausteins
  lässt TIA Portal V21 den `MainSafetyBlockName` der Gruppe verwerfen (das Übersetzen der PLC schlägt dann mit "does not have a valid main
  safety block" fehl), und Openness kann ihn nicht neu zuweisen; der SimaticML-Import behält ihn. Daher läuft jeder SD-Schreibvorgang auf einen solchen Baustein
  (`update_block_logic` sd, `insert_network`, `replace_network`, `delete_network`, `create_block_from_document` mit
  `overwrite`) innerhalb eines Elements so ab: das Dokument als temporärer F-Baustein `zzMcpTmp_<block>_<8 hex>` in der
  Gruppe des Bausteins (ohne `S7_BlockNumber`, mit `S7_Safety := "TRUE"`), `compile_check` davon, seine SimaticML mit `Name`,
  `Number` und `AutoNumber` auf die Werte des Main-Bausteins gesetzt, `update_block_logic` xml auf den Main-Baustein, dann wird der temporäre
  Baustein gelöscht. Ein Übersetzungsfehler deines Dokuments stoppt, bevor sich der Main-Baustein ändert; das Element schlägt mit den Meldungen
  des Compilers fehl. Das `mainSafetyBlockRoute` des Ergebnisses listet die Schritte auf. Schreibe das Dokument genau wie für einen direkten Import (der
  deklarierte Name bleibt der des Main-Bausteins). Andere F-Bausteine werden direkt importiert.
- Ein gerade aus einem SD-Dokument angelegter Baustein (zum Beispiel mit `create_block_from_document`) exportiert keine SimaticML, bis
  er übersetzt ist (L, BetterTIA-Fallstrick B4): `get_block_content xml` gibt dann nur die SD-Dokumente und eine Warnung zurück;
  führe `compile_check` für den Baustein aus und lies erneut.

## Importform und Exportform unterscheiden sich (berichtet; auf V21 live nicht reproduziert)

Der Skill von 1748477582 (`tools/tiaportal-mcp/skill/SKILL.md`, Abschnitt 9a, laut seinem Autor „verifiziert 2026-06-11“)
berichtet, dass V21 einige Anweisungen in einer Form **exportiert**, die `ImportFromDocuments` mit einem allgemeinen
"Failed importing" **ablehnt**. **Das wurde am 2026-11-02 auf V21 live nicht reproduziert** für `GT_Contact`/`EQ_Contact`, `Move`,
`P_Trig`/`N_Trig`, `TON`/`TOF` und `Add`: Ihr erneuter Export entspricht der unten stehenden Importform, abgesehen von Leerraum
(eine Leerzeile innerhalb des RUNG vor `Move(`, die `Add`-Vorlage als mehrzeiliges Pragma). Andere Anweisungen wurden
nicht getestet, daher bleibt der Bericht für sie bestehen; schreibe die Importform (R):

| Bedarf | Schreiben (Importform) | Exportform, nicht schreiben |
| --- | --- | --- |
| Vergleich | `{ S7_Templates := "SrcType := DInt" }` + `GT_Contact( in1 := …, in2 := … )`; `LT_`, `EQ_`, `NE_Contact` | `Gt{ SrcType := DInt }( IN1, IN2 )` und `Lt`/`Eq`/`Ne` |
| Move | `Move( in := …, out1 => … )` | `Move{ Card := 1; DisableENO := TRUE }( IN, OUT1 )` |
| Steigende Flanke | `P_Trig( #memBit )` | `PBox( … )` |
| IEC-Zeitglied (Inline-Vorlage, wird Berichten zufolge ebenfalls importiert) | `#tonInst.TON{ time_type := Time }( PT := T#500ms, ET => )`; `TOF`, `TP` entsprechend | gleich |
| FC-Aufruf / FB-Aufruf | `"FCName"()` / `"InstanceDB"( p := …, o => … )` | — |
| Symbolischer globaler Operand | `Contact( "DB".path.member )` | — |

Folge (berichtet): Ein von TIA exportierter und unverändert erneut importierter Baustein kann fehlschlagen, wenn er reine Exportformen enthält.
Wenn eine Änderung über SD an Netzwerken fehlschlägt, die du nicht angefasst hast, schreibe diese Netzwerke entweder in der Importform neu oder verwende
für diesen Baustein den SimaticML-Weg.

Weitere Berichte aus derselben Quelle (R): Inline-Literaltitel (`S7_NetworkTitle := "text"`) schlagen fehl — Titel müssen
`MLC_*`-IDs sein, die in der `.s7res` vorhanden sind; die `.s7res` muss auch ohne Titel existieren (ein Eintrag mit `zh-CN` und
`en-US`) — **live widerlegt am 2026-11-02**: Der eigene Export von TIA Portal eines Bausteins ohne Texte hat keine `.s7res`, und eine
Kultur, die das Projekt nicht hat (`en-US` in diesem de-DE-Projekt), ließ den Import fehlschlagen; beide Dateien UTF-8 **mit**
BOM (der eigene Export von TIA hat das BOM, L); ein Baustein, dessen bevorzugte Sprache SCL ist, kann keine KOP-Netzwerke enthalten, während ein
KOP-Baustein SCL-Netzwerke enthalten kann. `Neg` wurde dort nicht per Import verifiziert.

## Regeln, die fehlgeschlagene Importe vermeiden (Siemens-Skill, sofern nicht anders gekennzeichnet)

1. Halte jedes Netzwerk-Pragma direkt vor seinem `NETWORK`.
2. Entferne aus eingefügtem Code nur das `S7_Language`-Pragma, nie innere `{ S7_Templates … }`- oder `{ f_… }`-Pragmas.
3. Ein `NETWORK … END_NETWORK` pro eingefügtem Code.
4. Leerzeilen innerhalb eines `RUNG`-Rumpfs schlagen Berichten zufolge fehl ("File Content has some discrepancies"); TIA Portal V21
   schreibt selbst in seinem Export eine Leerzeile innerhalb eines RUNG vor `Move(`, und dieser Export ließ sich live erneut importieren
   (2026-11-02), daher ist diese Regel ein Bericht, keine Tatsache. Vermeide sie in Code, den du schreibst.
5. Importiere mit `ImportFromDocuments(…, ImportDocumentOptions.Override)`, nie über externe Quellen.
6. Wenn der Baustein Texte hat, liegt die `.s7res` für den Import neben der `.s7dcl`; ein Baustein ohne Texte braucht keine
   (der eigene Export von TIA hat keine, L).
7. Jede Kultur in der `.s7res` muss eine Projektsprache sein (L: Ein `en-US`-Text ließ den Import in einem
   de-DE-Projekt fehlschlagen). Die von BetterTIA/1748477582 berichtete Regel „en-US-Texte behalten“ wurde am 2026-11-02 in
   einem de-DE-Projekt live widerlegt.
8. Ein erneuter Import kann Instanz-DBs und Aufrufer inkonsistent hinterlassen (R): danach übersetzen.
9. Ein SIMATIC-SD-Import des Main-Safety-Bausteins einer F-Ablaufgruppe verwirft den `MainSafetyBlockName` der Gruppe
   (L, 2026-11-02, reproduziert; der SimaticML-Weg behält ihn, und Openness kann ihn nicht neu zuweisen). Der Server leitet
   SD-Schreibvorgänge auf einen Main-Safety-Baustein daher über einen temporären F-Baustein und importiert SimaticML (siehe
   [fehlersichere Programme](#fehlersichere-programme-f-kop--f-fup)); `get_safety_administration` und `compile_check` bestätigen anschließend
   die Referenz.

## Arbeitsablauf mit diesem Server

1. `plc_read list_networks` (oder `get_block_content` Format `sd`) — den Baustein als Text lesen. Jedes Netzwerk listet die
   `exportOnlyForms` auf, die es enthält (siehe oben).
2. Das Netzwerk in der obigen Importform schreiben; jede ungewöhnliche Form (Vorlagen, F-Pragmas, Instanzaufrufe) aus
   einem Export desselben Projekts kopieren, umgewandelt in die Importform, wo dieser Leitfaden eine nennt.
3. `plc_write insert_network` / `replace_network` / `delete_network` (optional `expectedContentHash` aus dem
   sd-Lesevorgang), `update_block_logic` mit Format `sd` oder `create_block_from_document` (`targetGroupPath`, ein sd-Bündel mit
   der `.s7dcl` und, wenn der Baustein Texte hat, der `.s7res`) für einen neuen Baustein. Der Info-Guard
   `plc_sd_reimports_export_form` nennt nicht angefasste Netzwerke in einer berichteten Exportform (für die obigen
   Anweisungen live nicht reproduziert). Die Verifikation meldet `codeMatchesExport`; eine Abweichung ist eine Information, kein Fehler
   (TIA formatiert Leerraum um).
4. `compile_check`, dann `plcsim_test` für das Verhalten.
5. Die Netzwerke einmal im TIA-Editor ansehen: Der Import kann sie anders anordnen, und nur der Editor zeigt,
   was das Instandhaltungspersonal sehen wird.

## Testbaustein für die Live-Abnahme

[`samples/MCP_SD_Matrix.s7dcl`](samples/MCP_SD_Matrix.s7dcl) mit [`samples/MCP_SD_Matrix.s7res`](samples/MCP_SD_Matrix.s7res)
(UTF-8 mit BOM, en-US- und de-DE-Texte) ist ein FB mit 15 Netzwerken: eines pro Konstruktzeile dieses Leitfadens (Reihen- und
Öffnerkontakt, paralleler und verschachtelter Zweig, Setzen/Rücksetzen, beide Flanken, `Not()`, `GT_Contact`/`EQ_Contact`, `Move`/`Add`,
`TON`/`TOF`-Multiinstanzen (`TON_TIME`, `TOF_TIME`), FUP UND/ODER/XOR, ein SCL-Netzwerk). Er wurde für diesen
Fork aus den obigen Zeilen geschrieben.

Ergebnisse des Live-Laufs am 2026-11-02 ([Live-Bericht](../superpowers/acceptance/reports/2026-11-02-simatic-sd-and-safety-live.md), E1-E7):

- Ein FB mit 15 KOP-/FUP-/SCL-Netzwerken mit diesen Konstrukten wurde mit `update_block_logic` Format `sd`
  (Override) importiert, 0 Übersetzungsfehler; der erneute Export entspricht dem eingereichten Code für jedes Konstrukt (mit **L** gekennzeichnete Zeilen);
  nur Leerraum weicht ab (E2).
- `create_block_from_document` mit einer `.s7res`, die `en-US`- und `de-DE`-Texte enthielt, schlug in `MCP_Livetest_01` fehl
  ("Cannot import multilingual text with culture 'en-US' …"); nur mit `de-DE`-Texten und vom Aufrufer gewählten IDs
  gelang es (E4). Entferne in einem Projekt ohne Englisch vor dem Import die `en-US`-Zeilen aus `MCP_SD_Matrix.s7res`.
- Ein Bündel ohne `.s7res` wurde von der eigenen Validierung dieses Servers abgelehnt, strenger als TIA Portal (E5); behoben:
  Die `.s7res` ist optional.
- `get_block_content` Format `sd` eines Bausteins ohne Texte gibt nur die `.s7dcl` zurück (UTF-8-BOM, CRLF); ein Baustein
  mit Netzwerktitel und -kommentar exportiert eine `.s7res` mit von TIA erzeugten IDs (`MLC_4SY`, `MLC_3VL`) (E1).
- `insert_network`, `replace_network` und `delete_network` verkettet in einem Aufruf gelangen mit 0 Übersetzungsfehlern und
  korrekter Verifikation; `replace_network` auf einem Baustein mit Netzwerktitel und -kommentar behielt die Texte (E3).
- `replace_network` auf dem Main-Safety-Baustein einer F-Ablaufgruppe gelang und ließ sich übersetzen, aber TIA Portal verwarf
  den `MainSafetyBlockName` der Gruppe (E7, reproduziert mit `b79b2df`, L1); die Umgehung über einen temporären F-Baustein behielt ihn
  (L4); siehe die [Safety-Referenz](../SupportedOperations/PLC_SAFETY_SUMMARY.md#main-safety-block-references-across-imports).
- `create_block_from_document` ohne `.s7res` legte die FC `MCP_SD_OhneTexte` an (E5 behoben).

## Offene Fragen für die Live-Abnahme

- Beantwortet am 2026-11-02: Die D-Konstrukte `I_Contact`, `N_Trig`, verschachtelte Zweige und die FUP-Boxen `A`/`O`/`X` mit einem
  negierten Eingang lassen sich unverändert importieren (E2). Noch offen: `S_SR`, `RUNG true`, Box-zu-Box-Wires, `SFDOOR`/`ESTOP1`, die
  F-Pragmas `f_user_card`/`f_image_card`, `NE_Contact`, `Sub`/`Mul`/`Div`, Operandennamen in Anführungszeichen und `TOF` auf einem
  Instanz-DB.
- Teilweise beantwortet: Der SD-Import eines Netzwerks eines F-Bausteins (`Main_Safety_RTG1` in `MCP_Safety_01`) gelang und
  ließ sich übersetzen (E7), verwarf aber die Referenz der Ablaufgruppe (L1); ein temporärer F-FB, angelegt aus einem SD-Dokument mit
  `S7_Safety := "TRUE"`, wurde ein F_FBD-FB (L4). Noch offen: ob `S7_Safety := "TRUE"` für einen
  F-Baustein exportiert wird, ob F-FUP und F-KOP sich gleich verhalten, und der automatische Weg als ein Aufruf.
- Beantwortet: TIA vergibt IDs wie `MLC_4SY`/`MLC_3VL` für Texte, die es anlegt (E1), und ein Import akzeptiert
  vom Aufrufer gewählte IDs (`MLC_T1`, `MLC_K1`), wenn ihre Kulturen Projektsprachen sind (E4).
- Offen: wie der SD-Import ein Netzwerk behandelt, das er nicht zeichnen kann (TIA-Fehler oder stille Änderung).
- Offen: welche anderen Anweisungen, falls überhaupt, V21 in einer Form exportiert, die es nicht erneut importiert.
