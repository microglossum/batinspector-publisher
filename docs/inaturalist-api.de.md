# iNaturalist: die Anfragen des Pakets

English version: [inaturalist-api.md](inaturalist-api.md)

Jede HTTP-Anfrage, die `BatInspectorPublisher` an iNaturalist stellt, mit ihren Parametern, dem Zweck der Antwort und der
Angabe, ob das Verhalten schon live gesehen wurde. Das Paket kontaktiert keinen anderen Host. Was jeder Schritt für einen Eintrag
bedeutet, steht in [inaturalist.de.md](inaturalist.de.md); Registrierung, Anmeldung und Ratenbegrenzung stehen in
[inaturalist-setup.de.md](inaturalist-setup.de.md).

Die Basis-URLs sind fest und keine Option: Client Secret und Tokens gehen an den eingestellten Host, eine Host-Anwendung kann
sie also nicht umleiten. Nur die Tests des Pakets ändern sie.

**Live bestätigt** heißt: gegen den echten Dienst gesehen. **Noch nicht live** heißt: aus dem Open-Source-Code, der
API-Dokumentation oder dem Forum von iNaturalist abgeleitet und noch nicht im ersten Live-Test geprüft.

## Übersicht

| Nr. | Anfrage | Host | Zweck | Wann | Stand |
|---|---|---|---|---|---|
| 1 | `GET /oauth/authorize` | www | Anmeldung im Browser | Einmal, ohne gespeichertes Token | Live bestätigt |
| 2 | `POST /oauth/token` | www | Code gegen OAuth-Token | Einmal je Anmeldung | Live bestätigt |
| 3 | `GET /users/api_token` | www | OAuth-Token gegen API-JWT | Anmeldung und etwa einmal täglich | Live bestätigt |
| 4 | `GET /v1/users/me` | api | Login-Name, numerische Nutzer-ID | Anmeldung; einmal je JWT in einem Lauf | Live bestätigt |
| 5 | `GET /v2/taxa/autocomplete` | api | Wissenschaftlichen Namen auflösen, dann Chiroptera | Jeder Kandidat, zweimal, wenn der Name einen Treffer hat | Live bestätigt |
| 6 | `GET /v1/observations` | api | Duplikatprüfung und Fortsetzen | Jeder Kandidat | Live bestätigt |
| 7 | `POST /v2/observations` | api | Beobachtung anlegen | Nur im Commit-Lauf | Live bestätigt |
| 8 | `POST /v2/observation_photos` | api | Spektrogramm | Nur im Commit-Lauf | Live bestätigt |
| 9 | `POST /v2/observation_sounds` | api | Audio | Nur im Commit-Lauf | Live bestätigt |

`www` ist `https://www.inaturalist.org`, `api` ist `https://api.inaturalist.org`. Ein Probelauf sendet die Anfragen 4 bis 6,
davor die Anmeldung (1 bis 3), wenn kein gültiges Token gespeichert ist, und nichts sonst.
Die Anfragen 4 bis 9 tragen das JWT als `Authorization: Bearer`. Das JWT wird nie protokolliert.

Je Kandidat sendet ein Commit-Lauf eine Taxonsuche, eine Duplikatprüfung, eine Anlage, eine Foto- und eine Sound-Anfrage: fünf
Anfragen, dazu eine Profilanfrage für den ersten Kandidaten eines JWT. Die Anfragen werden um `MinRequestInterval` (Standard eine
Sekunde) versetzt; Wiederholungen sind aus (`MaxAttempts = 1`), siehe
[inaturalist-setup.de.md](inaturalist-setup.de.md#anfragerate-und-wiederholungen).

## Anmeldung

**1. `GET /oauth/authorize`**: wird im Browser des Nutzers geöffnet, das Paket sendet sie nicht. Parameter: `client_id`,
`redirect_uri` (Loopback-Adresse des lokalen Listeners), `response_type=code`, `state`, `code_challenge`,
`code_challenge_method=S256` (PKCE). Die Antwort ist eine Weiterleitung an den Loopback-Listener mit `code` und `state`.

**2. `POST /oauth/token`**: Formular mit `grant_type=authorization_code`, `code`, `redirect_uri`, `code_verifier`, `client_id` und,
falls konfiguriert, `client_secret`. Die Antwort enthält das OAuth-`access_token`. Das gespeicherte Token läuft nach dem
Quellcode nicht ab, und es wird kein Refresh-Token ausgegeben; ein `refresh_token`-Grant ist im Code für den Fall vorgesehen, dass doch eines kommt.

**3. `GET /users/api_token`** (Host `www`, nicht `api`): das OAuth-Token als Bearer. Die Antwort ist `{"api_token": "<JWT>"}`. Die
REST-API lehnt ein rohes OAuth-Token mit 401 ab, deshalb ist dieser Tausch nötig. Das JWT gilt etwa 24 Stunden und wird still aus
dem gespeicherten OAuth-Token erneuert, auch früher, wenn eine API-Anfrage (4 bis 9) mit 401 antwortet; diese Anfrage wird dann einmal
mit dem neuen JWT wiederholt.

**4. `GET /v1/users/me`**: JWT als Bearer. Aus `results[0]` wird verwendet:

- `login`: wird mit dem Token gespeichert und von der Anmeldung angezeigt (SmokeTest `whoami`);
- `id`: die numerische Nutzer-ID, die die Duplikatprüfung braucht (in der echten Antwort vorhanden; dieselbe Zahl steht im Claim
  `user_id` des JWT). Sie wird einmal je JWT erfragt und für die Lebensdauer des Publishers im Speicher
  gehalten, nirgends geschrieben. Fehlt `id` in der Antwort, endet der Kandidat als `Failed`, bevor etwas gesucht oder geschrieben wird.

## Taxon

**5. `GET /v2/taxa/autocomplete`**, Parameter `q=<wissenschaftlicher Name>` und `fields=id,name,rank,is_active,matched_term,ancestor_ids`.
Angenommen wird nur ein exakter Treffer von `name` (Groß-/Kleinschreibung egal), der aktiv ist und den erwarteten Rang hat (Art bei
zwei Wörtern, Gattung bei einem, die feste Tabelle für die Gruppenwerte von BatInspector), und nur, wenn er eine Fledermaus ist: Hat
der Name einen Treffer, löst eine zweite Anfrage mit `q=Chiroptera` die Ordnung nach denselben Regeln auf, und ein Treffer zählt nur,
wenn er Chiroptera ist oder die ID von Chiroptera in `ancestor_ids` trägt. Ein Synonym wird mit dem aktuellen
Namen gemeldet (`matched_term`), nie verwendet. Es wird nichts zwischengespeichert: Jeder Kandidat fragt neu. Inaktive Taxa liefert
dieser Endpunkt nicht (live geprüft, 2026-10-14).

## Duplikatprüfung und Fortsetzen

**6. `GET /v1/observations`** fragt, ob dieses Paket den Eintrag schon veröffentlicht hat, mit diesen Parametern:

| Parameter | Wert | Warum |
|---|---|---|
| `q` | das `batinspector:`-Tag des Eintrags (URL-kodiert) | Das ganze Tag wird genau getroffen, kein Präfix und kein Teilbegriff. |
| `search_on` | `tags` | Sucht nur in den Tags. |
| `user_id` | die ID aus Anfrage 4 | Beschränkt die Suche auf die eigenen Beobachtungen. |

Die Suche findet eine Beobachtung, egal wo ihre Position liegt: Live geprüft (2026-10-31) fand sie eine `open`-, eine `obscured`- und
eine `private`-Beobachtung, jeweils mit Foto- und Ton-Liste, und eine unvollständige `private` wurde vervollständigt. Eine Suche nach
Position (`lat`, `lng`, `radius`) tut das nicht: Eine verschleierte Beobachtung liegt bis zu etwa 15 km von ihrer echten Position
entfernt und eine private hat keine. Das Paket vergleicht die Tags jedes Ergebnisses noch einmal mit dem Tag des Eintrags, bevor es
einem Treffer glaubt.

Gelesen wird nur die erste Ergebnisseite (Standardgröße). Aus jedem Ergebnis nutzt das Paket `id`, `uuid`, `tags`, `photos` und `sounds`
(alle in der Antwort vorhanden, live geprüft). `tags` kommt als Liste von Zeichenketten zurück, die das mit Anfrage 7 gesendete
`batinspector:`-Tag unverändert enthält, und eine halb angelegte Beobachtung (ein fehlgeschlagener Upload) wird sofort aufgelistet
(live gesehen, 2026-10-15). Ein `tags`-Wert anderer Form würde ignoriert, die Beobachtung dann nicht erkannt.

**`mine` und `mine_only` wirken nicht.** Die v1-API ignoriert beide, auch mit gültiger Anmeldung: Eine Suche nach Chiroptera lieferte mit
und ohne sie dieselben 291317 Ergebnisse, mit `user_id` 6 (live geprüft, 2026-10-14). Sie hätten Beobachtungen anderer Nutzer getroffen,
deshalb beschränkt das Paket mit `user_id`.

## Schreiben

Alle drei Schreibanfragen gibt es nur im Commit-Lauf, nur wenn die Duplikatprüfung nichts zum Fortsetzen fand, und sie werden nach einem
Serverfehler, einem Netzwerkfehler oder einem Timeout nicht wiederholt (eine Wiederholung könnte eine zweite Beobachtung oder einen zweiten Upload anlegen).

**7. `POST /v2/observations`**, JSON-Body `{"observation": {...}}` mit `taxon_id`, `observed_on_string` (`yyyy-MM-dd HH:mm:ss`, Ortszeit), `latitude`,
`longitude`, `species_guess` (der Name aus der Datei), `description` (deutscher Text, siehe
[inaturalist.de.md](inaturalist.de.md#die-beschreibung)), `tag_list` (`INaturalistOptions.TagList` und das
`batinspector:`-Tag des Eintrags) und, wenn gesetzt, `license` (`INaturalistOptions.License`) und `geoprivacy` (`INaturalistOptions.Geoprivacy`). Aus der Antwort wird `results[0].uuid` zum Anhängen der Belege gebraucht;
die numerische `id` fehlt manchmal, dann trägt das Ergebnis die uuid und die URL mit der uuid (sie leitet auf die Beobachtung um, live gesehen).

**8. `POST /v2/observation_photos`** und **9. `POST /v2/observation_sounds`**: je eine Multipart-Anfrage, die die Datei hochlädt
(`file`) und im selben Schritt verknüpft (`observation_photo[observation_id]` bzw. `observation_sound[observation_id]`, gesetzt auf
die uuid der Beobachtung). Der Zweischritt-Ablauf (hochladen, dann verknüpfen) wurde von der echten API abgelehnt; die kombinierte
Anfrage folgt der OpenAPI-Spezifikation von v2. Ist `INaturalistOptions.License` gesetzt, trägt die Foto-Anfrage zusätzlich
`observation_photo[photo][license_code]` (live bestätigt). Die Sound-Anfrage trägt keine Lizenz: Die API hat dafür keinen Parameter.

## Was nie gesendet wird

Keine Anfrage löscht, ändert oder versteckt etwas, und das Paket kontaktiert keinen anderen Host als `www.inaturalist.org` und `api.inaturalist.org`.
