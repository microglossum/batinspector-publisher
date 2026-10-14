# iNaturalist: the requests the package sends

German version: [inaturalist-api.de.md](inaturalist-api.de.md)

Every HTTP request `BatInspectorPublisher` makes to iNaturalist, with its parameters, what the answer is used for and
whether the behavior has been seen live. The package contacts no other host. What each step means for an entry is in
[inaturalist.md](inaturalist.md); registration, login and rate limits are in [inaturalist-setup.md](inaturalist-setup.md).

The base URLs are options (`INaturalistOptions.ApiBaseUrlV1`, `ApiBaseUrlV2`, `OAuthAuthorizeUrl`, `OAuthTokenUrl`,
`ApiTokenExchangeUrl`); the defaults are used below.

**Verified live** means seen against the real service. **Not yet live** means written from iNaturalist's open source,
the API documentation or the forum and waiting for the first live test.

## Overview

| # | Request | Host | Purpose | When | Status |
|---|---|---|---|---|---|
| 1 | `GET /oauth/authorize` | www | Browser login | Once, without a stored token | Verified live |
| 2 | `POST /oauth/token` | www | Code for OAuth token | Once per login | Verified live |
| 3 | `GET /users/api_token` | www | OAuth token for API JWT | Login and about once a day | Verified live |
| 4 | `GET /v1/users/me` | api | Login name, numeric user id | Login; once per JWT in a run | `login` live, `id` not yet |
| 5 | `GET /v2/taxa/autocomplete` | api | Resolve a scientific name | Every candidate | Verified live |
| 6 | `GET /v1/observations` | api | Duplicate check and resume | Every candidate | Partly live |
| 7 | `POST /v2/observations` | api | Create the observation | Commit run only | Not yet live |
| 8 | `POST /v2/observation_photos` | api | Spectrogram | Commit run only | Not yet live |
| 9 | `POST /v2/observation_sounds` | api | Audio | Commit run only | Not yet live |

`www` is `https://www.inaturalist.org`, `api` is `https://api.inaturalist.org`. A dry run sends requests 4 to 6, preceded by the login (1 to 3) when no valid token is stored, and nothing else.
Requests 4 to 9 carry the JWT as `Authorization: Bearer`. The JWT is never logged.

Per candidate a commit run sends one taxon search, one duplicate check, one create, one photo and one sound request: five requests,
plus one profile request for the first candidate of a JWT. Requests are spaced by `MinRequestInterval` (default one second);
retries are off by default (`MaxAttempts = 1`), see [inaturalist-setup.md](inaturalist-setup.md#request-rate-and-retries).

## Login

**1. `GET /oauth/authorize`**: opened in the user's browser, not sent by the package. Parameters: `client_id`, `redirect_uri`
(loopback address of the local listener), `response_type=code`, `state`, `code_challenge`, `code_challenge_method=S256` (PKCE).
The answer is a redirect to the loopback listener with `code` and `state`.

**2. `POST /oauth/token`**: form body with `grant_type=authorization_code`, `code`, `redirect_uri`, `code_verifier`, `client_id`
and, if configured, `client_secret`. The answer holds the OAuth `access_token`. The stored token does not expire as far as the
source shows, and no refresh token is issued; a `refresh_token` grant exists in the code for the case that one is.

**3. `GET /users/api_token`** (host `www`, not `api`): the OAuth token as Bearer. The answer is `{"api_token": "<JWT>"}`. The REST API
rejects a raw OAuth token with 401, so this exchange is required. The JWT is valid for about 24 hours and is renewed silently
from the stored OAuth token.

**4. `GET /v1/users/me`**: JWT as Bearer. Used from `results[0]`:

- `login`: stored with the token and shown by the login (the SmokeTest `whoami`);
- `id`: the numeric user id, needed by the duplicate check (that this field is present is **not yet live**; the same number is the `user_id` claim of the JWT). It is asked once per JWT and kept in memory for the
  publisher's lifetime, not written anywhere. A response without an `id` ends the candidate as `Failed` before anything is searched or written.

## Taxon

**5. `GET /v2/taxa/autocomplete`**, parameters `q=<scientific name>` and `fields=id,name,rank,is_active,matched_term`.
Only an exact, case-insensitive match of `name` that is active and has the expected rank (species for two words, genus for one,
the fixed table for BatInspector's group values) is accepted. A synonym is reported with its current name (`matched_term`), never used.
Nothing is cached: every candidate asks again. Inactive taxa are not returned by this endpoint (checked live, 2026-10-14).

## Duplicate check and resume

**6. `GET /v1/observations`** with these parameters:

| Parameter | Value | Why |
|---|---|---|
| `taxon_id` | the resolved taxon | Includes descendant taxa (checked live), see the note below. |
| `d1`, `d2` | the local calendar day, both the same | One day. |
| `lat`, `lng` | the entry's position | |
| `radius` | `DuplicateCheckRadiusKm` (default 0.1) | Kilometres. |
| `user_id` | the id from request 4 | Restricts the search to the user's own observations. |

Only the first page of results is read (the default page size); `total_results` decides whether something exists, the first
results tell whether it is complete. From each result the package uses `id`, `uuid`, `description`, `photos` and `sounds`
(all present in the answer, checked live). Whether `description` comes back exactly as sent, and whether an upload that is not
processed yet is already listed, is **not yet live**.

**`mine` and `mine_only` do not work.** The v1 API ignores both, even with a valid login: a Chiroptera search returned the
same 291317 results with and without them, and 6 with `user_id` (checked live, 2026-10-14). They would have matched other users'
observations as duplicates, so the package restricts by `user_id`.

**Descendants.** A search for a broader taxon includes its descendants: an entry filed under Chiroptera (`Nyctaloid`) or
*Myotis* (`Mbart`) matches your own species-level observation of that day and place and is skipped as a duplicate, while a
species entry does not match an observation identified only as a family or order (checked live).

## Writing

All three writes happen only in a commit run, only after the duplicate check found nothing to complete, and are not repeated after a
server error, a network error or a timeout (a repeat could create a second observation or upload).

**7. `POST /v2/observations`**, JSON body `{"observation": {...}}` with `taxon_id`, `observed_on_string` (`yyyy-MM-dd`),
`latitude`, `longitude`, `place_guess` (the coordinates as text), `species_guess` (the name from the file), `description`
(German text, see [inaturalist.md](inaturalist.md#the-description)) and `tag_list`. The answer's `results[0].uuid` is needed to
attach the evidence; the numeric `id` is sometimes missing, then the result carries the uuid and no URL.

**8. `POST /v2/observation_photos`** and **9. `POST /v2/observation_sounds`**: one multipart request each that uploads the file
(`file`) and links it in the same step (`observation_photo[observation_id]` or `observation_sound[observation_id]`, set to the
observation's uuid). The two-step flow (upload, then link) was rejected by the real API; the combined request follows the v2
OpenAPI specification.

## What is never sent

No request deletes, edits or hides anything, and the package contacts no host other than `www.inaturalist.org` and `api.inaturalist.org`.
