# Fledermaus-Taxonomie in Europa: Wissensquelle

Geschrieben für: den Besitzer des Pakets und alle, die Namen zwischen BatInspector, iNaturalist und naturgucker abgleichen.
Nachschlagewerk, keine Programmgrundlage: das Paket schlägt Arten zur Laufzeit nach und liefert keine Artenliste aus. **Kann veralten**; jeder Eintrag nennt seine Quelle und das Datum.

Stand: 2026-11-01.

## Quellen

- Wikipedia (de), „Liste von Fledermausarten in Deutschland“ (abgerufen 2026-11-01, Seitenstand 2026-10-18): 27 nachgewiesene Arten, davon 2 Irrgäste und 1 ausgestorben; in Europa 45 heimische Arten (Stand 2016, Europäische Umweltagentur). Die Tabelle ließ sich maschinell zu 26 Arten lesen, die 27. fehlt hier und ist nachzutragen.
- Wikipedia (de), „Fledermäuse“: verweist auf die Liste der EUROBATS-Abkommen (55 geschützte, in Europa heimische Arten).
- naturgucker: Handrecherche des Besitzers in der Artsuche und in der Systematik, 2026-11-01 (`docs/naturgucker-feature-requests.md`).
- iNaturalist: eigene Prüfungen aus `docs/batinspector-taxa-check.de.md` und `docs/inaturalist-api.md`.

Wikipedia-Text steht unter CC BY-SA; hier stehen nur Namen als Fakten und die Quellenangabe.

## Arten in Deutschland (Wikipedia de, 26 von 27 gelesen)

| Gattung | Arten |
|---|---|
| Barbastella | barbastellus |
| Eptesicus | nilssonii, serotinus |
| Hypsugo | savii |
| Miniopterus | schreibersii (in Deutschland ausgestorben, gilt als heimisch) |
| Myotis | alcathoe, bechsteinii, brandtii, dasycneme, daubentonii, emarginatus, myotis, mystacinus, nattereri |
| Nyctalus | lasiopterus (Irrgast), leisleri, noctula |
| Pipistrellus | kuhlii, nathusii, pipistrellus, pygmaeus |
| Plecotus | auritus, austriacus |
| Rhinolophus | ferrumequinum, hipposideros |
| Vespertilio | murinus |

Die Liste führt *Eptesicus nilssonii* und *Eptesicus serotinus* unter *Eptesicus*. Weitere europäische Arten (*Myotis blythii* / *oxygnathus*, *Myotis capaccinii*, *Plecotus macrobullaris*, *Tadarida teniotis*, …) stehen hier nicht; sie sind nachzutragen, wenn jemand sie braucht.

## Namen, die sich zwischen den Plattformen unterscheiden

| Name | BatInspector-Export / Wikipedia de | naturgucker | iNaturalist |
|---|---|---|---|
| Nord- und Breitflügelfledermaus | *Eptesicus nilssonii*, *Eptesicus serotinus* | wie Wikipedia; keine Gattung *Cnephaeus* | Synonyme von *Cnephaeus nilssonii* / *Cnephaeus serotinus* (`docs/batinspector-taxa-check.de.md`) |
| Kleines Mausohr | *Myotis blythii* (Wikipedia); der BatInspector-Export schreibt *Myotis oxygnatus* (ohne „h“, vermutlich ein Tippfehler) | „Kleines Mausohr (Myotis blythii)“ ist der genutzte Eintrag; „Myotis oxygnathus (Myotis blythii)“ hat nie eine Sichtung (Besitzer, 2026-11-01) | *Myotis oxygnatus*: kein Treffer. *Myotis oxygnathus* wird als Synonym von *Myotis blythii* gefunden (live geprüft 2026-11-01) |
| Große Bartfledermaus | *Myotis brandtii* | beide Schreibweisen *brandti* und *brandtii* | *brandtii* ist der Name; *brandti* wird ebenfalls darauf aufgelöst (live geprüft 2026-11-01) |

## Forschungsstand (Recherche 2026-11-01)

**Eptesicus / Cnephaeus.** Für die Altwelt-Arten *serotinus* und *nilssonii* ist *Cnephaeus* inzwischen der Gattungsname der großen Datenbanken:

- Mammal Diversity Database (ASM) führt beide unter *Cnephaeus* ([nilssonii](https://www.mammaldiversity.org/taxon/1005526), [serotinus](https://mammaldiversity.org/taxon/1005531)); auch Bat Species of the World ([batnames.org](https://batnames.org/genera/Cnephaeus)).
- Grundlage: Juste et al. 2013 (Phylogenie, *Eptesicus* im weiten Sinn ist polyphyletisch) und eine Revision von 2023, die *Eptesicus* auf die Neuwelt-Arten *E. fuscus* und *E. guadeloupensis* beschränkt und *Cnephaeus* (Kaup, 1829; Typusart *Vespertilio serotinus*) für alle Altwelt-Taxa auf Gattungsrang hebt ([Zoologia 2023](https://scielo.br/j/zool/a/hqy3qD7tNDPLXYxnmqr7xtg)).
- Uneinheitlich bleibt die Praxis: GBIF und Catalogue of Life führen noch *Eptesicus (Cnephaeus) serotinus*, das US Fish and Wildlife Service und ältere deutsche Quellen (Spektrum-Lexikon, die Wikipedia-Artenliste für Deutschland) *Eptesicus*; die Wikipedia-Seite zur Nordfledermaus nennt *Cnephaeus nilssonii*. Dass die Neuordnung allgemein angenommen ist, habe ich nicht geprüft.
- iNaturalist (live geprüft 2026-11-01): *Cnephaeus serotinus* und *Cnephaeus nilssonii* sind die aktiven Arten; *Eptesicus serotinus* und *Eptesicus nilssoni* werden als Synonym gefunden. iNaturalist trennt außerdem *Cnephaeus pachyomus* (Oriental Serotine) von *C. serotinus* (die MDD vermerkt, dass die Verbreitung von *serotinus* noch zu klären ist). Die Gattung *Eptesicus* gibt es dort weiter (*E. fuscus*).

**Myotis blythii / oxygnathus.** *Myotis blythii* (Tomes, 1857) ist der gültige Name; *Myotis oxygnathus* (Monticelli, 1885) ist ein Synonym, bei einigen Autoren eine Unterart (*M. blythii oxygnathus*). Ob *oxygnathus* Artrang verdient, ist umstritten; die Abgrenzung zum Großen Mausohr (*M. myotis*) und zu *M. punicus* ist ebenfalls offen (Furman et al. 2014). Quellen: [Wikipedia](https://en.wikipedia.org/wiki/Lesser_mouse-eared_bat), [MDD](https://www.mammaldiversity.org/taxon/1005376), [GBIF](https://www.gbif.org/species/2432414). naturgucker führt die Art unter dem Namen *oxygnathus*; iNaturalist unter *blythii*.
| Zweifarbfledermaus | *Vespertilio murinus* | Art vorhanden; dazu getrennte Unterart-Einträge | — |

Folge: Das Paket übersetzt nicht still. Welche Übersetzung für welche Plattform gilt, ist eine Entscheidung des Besitzers (`TODO.md`).

## Gruppenwerte

- Gruppen in Rufanalysen: **Nyctaloid** meint üblicherweise *Eptesicus*, *Nyctalus* und *Vespertilio*; **Pipistrelloid** meint *Hypsugo*, *Miniopterus* und *Pipistrellus* (allgemeine Fachpraxis, nicht aus BatInspectors Code belegt).
- naturgucker führt genau diese beiden Dreiergruppen als Sammelgattungen: „Eptesicus/Nyctalus/Vespertilio“ und „Hypsugo/Miniopterus/Pipistrellus“.
- BatInspectors Repo (`BatInspector/dat/BatSpeciesRegions.json`, gelesen 2026-11-01) führt „Nyctaloid“ unter den Gattungen jeder Region, ohne Definition. Was BatInspector damit meint, bestätigt der Besitzer oder die BatInspector-Seite.
- Die Gruppenwerte `Social` und `?` haben bei naturgucker keine erkennbare Entsprechung. Als Ziel kämen „Chiroptera indet.“ und die „… indet.“-Einträge je Gattung in Frage.

## naturgucker: Gattungs- und Ordnungseinträge („indet.“), gefunden 2026-11-01

Rhinolophus, Myotis, Eptesicus, Nyctalus, Vespertilio, Barbastella, Miniopterus, Tadarida, Hypsugo, Pipistrellus, Plecotus, außerdem Chiroptera. Nicht vorhanden: *Cnephaeus*.
