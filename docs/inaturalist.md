# iNaturalist: what lands where

German version: [inaturalist.de.md](inaturalist.de.md)

This page describes what `INaturalistPublisher` does with an entry of the input file: which field becomes which iNaturalist
field, the steps of one run, the outcomes and what is public afterwards. Registration, login and the rate limits are in
[inaturalist-setup.md](inaturalist-setup.md); how the input file is read is in [input-format.md](input-format.md).

Everything here is derived from the code and from iNaturalist's open source and forum, and the flow was checked in a live run against
iNaturalist on 2026-10-15. What a live run has not shown yet is marked as such.

## The steps of one candidate

```mermaid
flowchart TD
    A["Candidate + evidence<br/>(read and checked once)"] --> B{"iNaturalist rules<br/>(date, position, size, ...)"}
    B -->|broken| S1["SkippedInvalidEntry /<br/>SkippedInvalidEvidence"]
    B -->|ok| C["Login (stored token or browser)"]
    C --> D{"Taxon: exactly one<br/>active bat match?"}
    D -->|no| S2["SkippedUnresolvedTaxon"]
    D -->|yes| E{"Published for this entry<br/>before? (found by its tag)"}
    E -->|"yes, complete"| S3["SkippedDuplicate"]
    E -->|"yes, photo or sound missing"| G{"Commit?"}
    E -->|no| H{"Commit?"}
    G -->|no| W1["WouldResume"]
    G -->|yes| K["attach what is missing"]
    H -->|no| W2["WouldCreate"]
    H -->|yes| I["Create the observation"]
    I --> J["Attach spectrogram (photo)"]
    J --> K2["Attach audio (sound)"]
    K --> R["Resumed"]
    K2 --> CR["Created"]
```

Before the first step the orchestrator has read both evidence files and checked them (see
[input-format.md](input-format.md#evidence-files)); a missing file is `SkippedMissingEvidence`. A **dry run** (the default,
`PublishOptions.Commit = false`) goes through every step including the login, the taxon search and the duplicate check, and
stops where the first write would happen. Nothing is created in a dry run.

The steps use these iNaturalist endpoints (every request with its parameters is listed in [inaturalist-api.md](inaturalist-api.md)):

| Step | Request |
|---|---|
| Taxon | `GET /v2/taxa/autocomplete` |
| Duplicate check, resume | `GET /v1/observations` (by the entry's tag, restricted with `user_id`, which comes from `GET /v1/users/me`) |
| Create | `POST /v2/observations` |
| Spectrogram | `POST /v2/observation_photos` (one multipart request that uploads and links) |
| Audio | `POST /v2/observation_sounds` (the same way) |

## From input field to iNaturalist field

| Input | iNaturalist | Notes |
|---|---|---|
| `SpeciesLatin` | `taxon_id` | Resolved live, see [Species and taxon](#species-and-taxon). |
| `SpeciesLatin` | `species_guess` | The name from the file, normalized. For an uncertain call such as `Nyctaloid` this keeps the original value while `taxon_id` is the broader taxon. |
| `Date` | `observed_on_string` | The local date and time as `yyyy-MM-dd HH:mm:ss`, without a zone or offset (see [Time zone](#time-zone)). The time with its zone is also in the description. |
| `Latitude`, `Longitude` | `latitude`, `longitude` | As they are. No positional accuracy is sent. |
| `Latitude`, `Longitude` | `place_guess` | Not sent. The input has no place name; iNaturalist sets one from the coordinates itself, see [Hiding the position](inaturalist-setup.md#hiding-the-position). |
| `Temperature`, `Humidity`, `Comment`, `Date`, `TimeZone` | `description` | German text, see below. |
| `PathToPng` | photo | The spectrogram, uploaded under its file name. |
| `PathToWav` | sound | The recording, uploaded under its file name. |
| `SpeciesLocal` | not sent | |
| (options) | `tag_list` | `INaturalistOptions.TagList`, default `bat,acoustic-monitoring,batinspector`, plus the resume tag of the entry (see below). |
| (options) | `geoprivacy` | `INaturalistOptions.Geoprivacy`, none by default. |
| (options) | `license`, photo `license_code` | `INaturalistOptions.License`, none by default (then the account's default licenses apply). Not for the sound, see [Licenses](inaturalist-setup.md#licenses). |
| `SpeciesLatin`, `Date`, `Latitude`, `Longitude` | `tag_list` | One more tag, `batinspector:` and 12 hex digits: a short hash of the species as in the file, the instant in UTC and the coordinates. It marks the observation as the one this package created for the entry, see [Re-running](#re-running-duplicates-and-resume). |

Not sent: a time zone, a positional accuracy, captive/cultivated flags. The package does not handle
sensitive species; iNaturalist obscures the coordinates of taxa it considers sensitive by itself. The user can hide the
position of every observation with `INaturalistOptions.Geoprivacy`, see [Hiding the position](inaturalist-setup.md#hiding-the-position).

### Time zone

Every observation is sent with coordinates, and iNaturalist then takes the time zone **from the coordinates**: it reads
`observed_on_string` as local time at that place, and ignores a zone or offset in the string and a `time_zone` parameter
(per its source; seen live: the sent local time comes back with the zone of the place, also after midnight and for Lisbon). So the package sends the wall-clock time of the entry as it is and no zone. This is
right as long as `TimeZone` (default Europe/Berlin) is the zone of the place where the recording was made, which is the
meaning of the input field. If it is not (for example a recorder that stayed on German time in Portugal), iNaturalist
shows the time shifted by the difference; the description still names the zone the entry was read in.

### The description

The description is German on purpose (all users of the package are German speaking, and it is public content on the
platform). It is built from these parts, each only when its data is there:

1. `INaturalistOptions.DescriptionPrefix` (default: *Automatisierte passive akustische Erfassung (BatInspector, batdetect2 + manuelle Prüfung).*)
2. The recording time with the zone: `Exemplarische Ruferkennung vom 13.06.2026 04:26:34 Uhr MESZ.` Europe/Berlin is named
   `MEZ` or `MESZ`; any other zone is named explicitly (`(Zeitzone Europe/Lisbon, UTC+01:00)`), never guessed from the offset.
3. For an uncertain call filed under a broader taxon: `Bestimmung in BatInspector: "Nyctaloid" (keine sichere Artbestimmung), hier als Chiroptera eingetragen.`
4. `Temperatur: 20,5 °C.` and `Luftfeuchte: 92,4 %.` (one decimal at most, German decimal comma)
5. `Anmerkung zur Bestimmung: <Comment>`
6. `Beleg: Spektrogramm und Audioaufnahme dieser Ruferkennung.`

For the entry in the [example](input-format.md#example) the description reads:

> Automatisierte passive akustische Erfassung (BatInspector, batdetect2 + manuelle Prüfung). Exemplarische Ruferkennung vom 13.06.2026 04:26:34 Uhr MESZ. Temperatur: 20,5 °C. Luftfeuchte: 92,4 %. Beleg: Spektrogramm und Audioaufnahme dieser Ruferkennung.

`PublishResult.Description` carries the text, also in a dry run, so the host can show what would become public. The
wording may change between versions; a later run recognizes its observations by their tag, not by the text.

## Species and taxon

Taxa are resolved **live for every candidate** with `taxa/autocomplete` and are never cached or shipped as a list. A name
is accepted only when exactly one active taxon has exactly that name and the expected rank (two words: species; one word:
genus), and that taxon is a bat: Chiroptera itself or a taxon below it. Chiroptera is resolved live by name as well. Nothing
is guessed: no "first autocomplete hit", no broader taxon for a typo.

| Situation | Result |
|---|---|
| One active taxon, name and rank match | Used. |
| Several matches | `SkippedUnresolvedTaxon`, ambiguous (the ids are in the message). A match outside the bats does not count, so a homonym in another group does not make a bat name ambiguous. |
| The name exists, but not under Chiroptera (or iNaturalist does not report its ancestry) | `SkippedUnresolvedTaxon`, not a bat. |
| The name exists, but only with another rank | `SkippedUnresolvedTaxon`, wrong rank. |
| The name is a synonym | `SkippedUnresolvedTaxon`, with the current name. Correct the input file. |
| Not found | `SkippedUnresolvedTaxon`, not found. |
| Three or more words | `SkippedUnresolvedTaxon` without a request. |

BatInspector's group and uncertain values are the one exception, a small fixed table that is always on and has no option:

| `SpeciesLatin` | Filed under | `species_guess` |
|---|---|---|
| `Nyctaloid`, `Social`, `?` | Chiroptera (order) | the original value |
| `Mbart` | *Myotis* (genus) | `Mbart` |

`PublishResult.TaxonName` names the taxon an observation is (or would be) filed under. The user can refine the
identification on iNaturalist. `todo` and every other value that is not a taxon name are skipped.

## Re-running: duplicates and resume

Before creating anything the publisher asks iNaturalist whether this package published the entry before. It looks for the entry's
`batinspector:` tag among **the logged-in user's own** observations (`user_id`, the numeric user id). The tag is found whatever the
observation's position is, also when it is obscured or private (by `Geoprivacy`, or by iNaturalist itself for a sensitive taxon).

- **Nothing found:** the observation is created.
- **Found, and it is complete:** `SkippedDuplicate`. The existing observation is named in `ObservationId`. It is never touched.
- **Found, and photo or sound is missing:** an earlier run created it and stopped before its evidence was complete. The missing
  evidence is attached (`Resumed`, in a dry run `WouldResume`). Only an observation that carries the tag is ever completed.

If the search result does not tell whether photos and sounds exist, the observation is deliberately left alone
(`SkippedDuplicate`): a wrong assumption fails safe.

Things to know about this check:

- The tag is a hash of the species as written in the file, the instant and the coordinates. It recognizes the same entry again,
  nothing else: an observation made otherwise (by hand, with an older tool, or with the tag removed) is not recognized and left alone,
  and an entry whose time, position or name was changed after publishing counts as a new entry. Choosing one reference recording per
  species, night and place is the export's job (see [input-format.md](input-format.md#one-reference-recording-per-species-night-and-place));
  the dry run cannot foresee several entries of one file that a commit run would publish, because it creates nothing, so they all say `WouldCreate`.
- The tag is visible on the observation. A user who removes it turns the check off for that observation.
- Observations that appear or disappear on iNaturalist after the check are not foreseen. A new observation can take a moment
  to show up in iNaturalist's search.

## Results

`PublishResult.Status` takes one of these values (the reason is always in `Message`):

| Status | Meaning |
|---|---|
| `Created` | Observation created, spectrogram and audio attached. `ObservationId` is set, `Url` only when iNaturalist returned a numeric id (see below). |
| `WouldCreate` | Dry run: a real run would create it. Nothing was written. |
| `WouldResume` | Dry run: a real run would attach what is missing to an incomplete observation of an earlier run (named in `ObservationId`). Nothing was written. |
| `Resumed` | An incomplete observation of an earlier run was completed (named in `ObservationId`). No new observation. |
| `SkippedDuplicate` | An equivalent observation of the user's already exists. It is named in `ObservationId` when known, and left untouched. |
| `SkippedUnresolvedTaxon` | The name did not resolve to exactly one taxon, see [Species and taxon](#species-and-taxon). |
| `SkippedInvalidEntry` | The entry breaks a rule of iNaturalist ([below](#rules-checked-before-anything-is-written)) or a platform-neutral rule. Nothing was written. |
| `SkippedInvalidEvidence` | A file is unreadable, empty, no PNG / WAV, or larger than `MaxEvidenceBytes`. Nothing was written. |
| `SkippedMissingEvidence` | A file does not exist. Nothing was written. |
| `Failed` | An error; the exception is in `Error`. May be partial: `ObservationId` is set when the observation exists, `SpectrogramAttached` and `AudioAttached` say what is there, `InterruptedStep` names the stage. |
| `Cancelled` | The caller cancelled. The same partial information as `Failed`, without `Error`. |

`ObservationId` is iNaturalist's numeric observation id, and `Url` its public page. If iNaturalist returns only the uuid of an
observation and no numeric id, `ObservationId` is the uuid and `Url` the uuid form of the page address, which iNaturalist redirects
to the observation (this is the usual answer to a created observation; a later run finds the numeric id). It applies to every
status that names an observation.

`Message` is a short English text written by the package, meant for a log or a details view; a host that shows the results
in another language maps the status, not the text. An error from iNaturalist itself is passed through as is, not translated or
reworded: its response body is part of the exception in `Error` and of `Message`, cut to the first 1000 characters
(`INaturalistApiException.ResponseBody` keeps all of it, see [logging.md](logging.md)).

A `Failed` or `Cancelled` result needs no cleanup: run again and the duplicate check and resume complete it. If the stop came
while the observation was being created, its existence is unknown; the next run finds out.

### Rules checked before anything is written

The adapter's own validation of iNaturalist's limits (file size, date, position, `species_guess`, tags) runs before the
login, also in a dry run, so nothing is written for an entry iNaturalist would refuse. A violation is `SkippedInvalidEntry`
(for the evidence files `SkippedInvalidEvidence`), with the problems listed in `Message`. What iNaturalist still rejects
later ends as `Failed`, with its own error text passed through unchanged. The rules, their sources and the open questions are in [inaturalist-setup.md](inaturalist-setup.md#limits-and-rules-checked-before-publishing).

## What is public and irreversible

A real run publishes on the account of the logged-in user:

- the observation with its **exact coordinates** (unless iNaturalist obscures a sensitive taxon or `Geoprivacy` is set), the date, the taxon, the
  tags and the description, which includes the measurements and the comment;
- the spectrogram as a photo and the recording as a sound, under their file names.

The package never deletes or edits an observation. It also does not delete one whose evidence could not be attached (the
failure may be transient, and resume completes it). Changes and deletions are made on iNaturalist.

Therefore a run is a dry run unless `Commit = true` is passed. Before a real run, show `RunReport.Rejected`,
`RunReport.Warnings` and the dry-run results (including `Description`) to the user.
