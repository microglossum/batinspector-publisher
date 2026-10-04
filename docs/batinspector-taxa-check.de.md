# Abgleich der BatInspector-Artenliste mit iNaturalist (Stand 2026-10-04)

Geprüft wurde die [Artenliste in BatInfo.cs](https://github.com/chrmue44/BatInspector/blob/master/BatInspector/ViewModel/BatInfo.cs) (Branch `master`). Jeder Name wurde live bei iNaturalist gesucht ([API v2 `taxa/autocomplete`](https://api.inaturalist.org/v2/taxa/autocomplete?q=Myotis&fields=id,name,rank,is_active,matched_term)), ohne Anmeldung. Das Paket gleicht einen Namen nur exakt ab (aktives Taxon, passender Rang) und rät nie.

## Positiv: 24 von 26 Arten werden eindeutig gefunden

| Art | iNaturalist |
|---|---|
| *Barbastella barbastellus* | [40581](https://www.inaturalist.org/taxa/40581) |
| *Hypsugo savii* | [765528](https://www.inaturalist.org/taxa/765528) |
| *Miniopterus schreibersii* | [57534](https://www.inaturalist.org/taxa/57534) |
| *Myotis alcathoe* | [74692](https://www.inaturalist.org/taxa/74692) |
| *Myotis bechsteinii* | [74697](https://www.inaturalist.org/taxa/74697) |
| *Myotis brandtii* | [74699](https://www.inaturalist.org/taxa/74699) |
| *Myotis dasycneme* | [40308](https://www.inaturalist.org/taxa/40308) |
| *Myotis daubentonii* | [424254](https://www.inaturalist.org/taxa/424254) |
| *Myotis emarginatus* | [40281](https://www.inaturalist.org/taxa/40281) |
| *Myotis myotis* | [40291](https://www.inaturalist.org/taxa/40291) |
| *Myotis mystacinus* | [40314](https://www.inaturalist.org/taxa/40314) |
| *Myotis nattereri* | [210400](https://www.inaturalist.org/taxa/210400) |
| *Nyctalus lasiopterus* | [40576](https://www.inaturalist.org/taxa/40576) |
| *Nyctalus leisleri* | [40573](https://www.inaturalist.org/taxa/40573) |
| *Nyctalus noctula* | [40572](https://www.inaturalist.org/taxa/40572) |
| *Pipistrellus kuhlii* | [40383](https://www.inaturalist.org/taxa/40383) |
| *Pipistrellus nathusii* | [40391](https://www.inaturalist.org/taxa/40391) |
| *Pipistrellus pipistrellus* | [40364](https://www.inaturalist.org/taxa/40364) |
| *Pipistrellus pygmaeus* | [74908](https://www.inaturalist.org/taxa/74908) |
| *Plecotus auritus* | [40416](https://www.inaturalist.org/taxa/40416) |
| *Plecotus austriacus* | [40415](https://www.inaturalist.org/taxa/40415) |
| *Rhinolophus ferrumequinum* | [40645](https://www.inaturalist.org/taxa/40645) |
| *Rhinolophus hipposideros* | [40695](https://www.inaturalist.org/taxa/40695) |
| *Vespertilio murinus* | [40446](https://www.inaturalist.org/taxa/40446) |

- Alle Gattungen der Liste werden eindeutig gefunden (*Myotis*, *Plecotus*, *Pipistrellus*, *Eptesicus*, *Vespertilio*, *Nyctalus*, *Rhinolophus*, *Barbastella*, *Hypsugo*, *Miniopterus*). *Myotis* gibt es auch als Untergattung, der Rangabgleich löst das auf.
- Die Gruppenwerte funktionieren: `Nyctaloid`, `Social` und `?` werden unter [Chiroptera](https://www.inaturalist.org/taxa/40268) eingeordnet, `Mbart` unter der Gattung [*Myotis*](https://www.inaturalist.org/taxa/40270). Der Originalwert bleibt als `species_guess` erhalten.

## Negativ: 2 Namen werden nicht gefunden (diese Einträge werden übersprungen)

1. **[*Eptesicus nilssonii*](https://github.com/chrmue44/BatInspector/blob/master/BatInspector/ViewModel/BatInfo.cs#L800)** (Kürzel `ENIL`, Nordfledermaus)
   - iNaturalist führt die Art als [*Cnephaeus nilssonii*](https://www.inaturalist.org/taxa/1596132). Der alte Name ist dort nur noch ein Synonym.
   - Das Paket überspringt den Eintrag mit dem Hinweis auf den aktuellen Namen, es ersetzt ihn nicht selbst.
2. **[*Myotis oxygnatus*](https://github.com/chrmue44/BatInspector/blob/master/BatInspector/ViewModel/BatInfo.cs#L812)** (Kürzel `MOXY`, Kleines Mausohr)
   - Der Name wird nicht gefunden. Die übliche Schreibweise ist *oxygnathus* (mit „h“).
   - Auch *Myotis oxygnathus* gilt bei iNaturalist nur als Synonym von [*Myotis blythii*](https://www.inaturalist.org/taxa/40317). Welcher Name richtig ist, muss BatInspector entscheiden.

## Hinweise

- Die Taxonomie ändert sich laufend (Arten werden umbenannt, geteilt, zusammengelegt). Das Paket hält deshalb keine Namensliste vor, sondern prüft jeden Namen bei jedem Lauf live. Änderungen bei iNaturalist wirken also sofort, ohne dass das Paket aktualisiert werden muss.
- Gewünscht ist daher, dass BatInspector die wissenschaftlichen Namen aktuell hält oder regelmäßig gegen iNaturalist abgleicht. Das Paket kann nur sauber melden, was nicht passt.
