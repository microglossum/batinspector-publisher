# iNaturalist setup

Deutsche Version: [inaturalist-setup.de.md](inaturalist-setup.de.md)

BatInspectorPublisher ships no iNaturalist credentials. The application that uses the package
registers its own OAuth application once and passes the values in via `INaturalistOptions`. **Registering an application is restricted by iNaturalist, see the first section.**

## Prerequisite: you must be approved as an "App Owner"

**Creating an iNaturalist application is not self-service.** iNaturalist does not let everybody register one.
You first have to be approved as an *App Owner*, and the form in step 1 only works after that.

1. Apply on the App Owner application page: <https://www.inaturalist.org/oauth/app_owner_application>.
2. The page states two conditions: your account must be **at least 2 months old**, and you must have made **at least 10 improving
   identifications in the last month**.
3. "Improving" is a specific term, defined in the
   [iNaturalist help article](https://help.inaturalist.org/en/support/solutions/articles/151000170241):
   * It must be an identification on **someone else's** observation. Your own observations do not count.
   * It must move the observation forward, for example adding a first identification to an *unknown* observation that later gets confirmed,
     or refining a genus to a species. Plain "I agree" identifications are *confirming*, not improving.
   * Check your own count at `https://www.inaturalist.org/identifications?user_id=<your login>&category=improving&for=others`.
4. After you submit, iNaturalist staff process the application manually. There is no automatic confirmation and no stated processing time;
   reports in the forum range from days to weeks. Plan for the wait.

What this means in practice:

* **One application per host application, not one per user.** The application that distributes this package (BatInspector) registers a single
  iNaturalist application and ships its client ID. Its users do **not** need an application of their own, they only log in with their own
  iNaturalist account (step 3). Only the developer has to be an App Owner.
* If you cannot qualify (yet), you cannot publish with your own application. Reading public iNaturalist data needs no application, but this
  package writes observations, which requires an authorized (OAuth) application.

Sources: iNaturalist community forum, [API Application Request (May to Sept 2026)](https://forum.inaturalist.org/t/api-application-request/78965)
and [Can't register as App owner (Aug 2025)](https://forum.inaturalist.org/t/cant-register-as-app-owner-api-authentication/68639),
quoting the application page and iNaturalist staff. The conditions are iNaturalist's to change; check the application page for the current text.

## 1. Register the OAuth application

1. Log in at <https://www.inaturalist.org/> with the account that should own the application.
2. Open <https://www.inaturalist.org/oauth/applications/new>.
3. Fill in:

   | Field | Value |
   |---|---|
   | Name | A descriptive name, shown to users on the authorization screen. |
   | Redirect URI | `http://127.0.0.1:45679/callback`. Must match exactly, including the port. Use `127.0.0.1`, not `localhost`. |
   | Confidential | See [Confidential or public client](#confidential-or-public-client). |

4. Copy the **Client ID** (and the **Client Secret** if one was issued).

### Confidential or public client

* **Confidential** (default): you get a Client ID and a Client Secret. Pass both in `INaturalistOptions`.
* **Public**: a native desktop application cannot keep a secret safe, so this is the technically correct choice.
  You only get a Client ID; leave `ClientSecret` empty. The code sends the secret only if one is set.

> A secret embedded in a distributed application is not secret. Prefer a public client (PKCE protects the flow)
> when the application is distributed to other people.

## 2. Pass the credentials to the package

```csharp
var options = new INaturalistOptions
{
    ClientId = settings.INaturalistClientId,
    ClientSecret = settings.INaturalistClientSecret, // null or empty for a public client
};
```

The package reads no files or environment variables. Where the host keeps the values is up to the host.
Never commit them to a repository.

## 3. Log in

```csharp
var auth = new INaturalistAuthenticator(options, httpClient);
await auth.LoginAsync(); // e.g. from a "connect to iNaturalist" button
```

By default the system browser opens. To show the URL in your own UI, pass an `AuthorizationPrompt`.
The login completes when iNaturalist redirects to the redirect URI; a temporary local listener
catches that request; other requests to it are ignored. `AuthorizationTimeout` (default 5 minutes) bounds the wait.

`EnsureAuthenticatedAsync` (called by the publisher) returns the stored API token and renews it silently when it has expired:
iNaturalist OAuth tokens never expire, so the browser login is needed **once** (until the user revokes access). A new login only happens if iNaturalist rejects the stored token. A network error never starts a login.

### Port 45679

The login listens on the port of the registered redirect URI. 45679 avoids common dev-server ports.
If it is taken on a machine, the login retries on up to three free ports chosen by the operating system
(logged as a warning) and sends the matching redirect URI to iNaturalist. For a loopback URI
(`127.0.0.1`) iNaturalist ignores the port when it matches the redirect URI, so the one registered URI is
enough. Should the browser show iNaturalist's redirect URI error anyway, free the port, or pick another
port, set `INaturalistOptions.RedirectUri` and register the identical URI at iNaturalist. If no port can
be opened at all, the login fails with an `INaturalistLoginException` (`Reason` is `ListenerUnavailable`).

While it waits, the listener ignores every request that does not belong to this login (a port scan, a browser
prefetch, another local program) and answers it with 404; only the redirect carrying this login's `state`
ends the wait.

The host must be `127.0.0.1` in both places: `HttpListener` matches the request's `Host` header
text verbatim, so `localhost` would get a 404 from a listener bound to `127.0.0.1`.

### Where the loopback login does not work

The login needs two things on the **same machine**: the app can open a local port, and the browser that
completes the login can reach it. These setups break one of them (none was tested yet; the cause follows
from how the login works):

| Setup | What happens | What to do |
|---|---|---|
| Browser on another machine than the app (remote desktop, VDI, SSH session) | iNaturalist redirects the browser to `127.0.0.1` of *its* machine, where nothing listens. The login ends with `TimedOut`. | Forward the port to the browser's machine (for SSH: `ssh -L 45679:127.0.0.1:45679 <host>`) and make sure the fixed port is free, because a fallback port cannot be forwarded in advance. Or log in on the machine that has the browser. |
| Locked-down machine (firewall or security software blocks local listeners) | No port can be opened: `ListenerUnavailable`. Or the browser cannot reach it: `TimedOut`. | Allow the app to listen on the loopback address. If that is not possible, the loopback login is not usable there. |
| Several users on one machine (terminal server) | The fixed port is taken by another session's login; the fallback ports take over. | Nothing, unless `ListenerUnavailable` appears: then retry when the other login has finished. |
| Sandboxed or packaged apps with loopback isolation (for example MSIX) | The app may be unable to reach its own loopback listener from the browser. | Not supported: the login needs a loopback listener. |

A failed login stores nothing. Cancel and try again; the stray requests of other programs do not end the wait.

### Why there is an extra JWT exchange

iNaturalist's REST API does not accept a raw OAuth access token as Bearer token (401). The token is
first exchanged for a JWT with `GET https://www.inaturalist.org/users/api_token` (note the `www`
host), and that JWT, valid about 24 h, is used for all API calls and stored locally. This is
undocumented in the official API reference.

## Limits and rules checked before publishing

Before the login and before anything is written, the publisher checks what iNaturalist would reject, and reports it
as `PublishStatus.SkippedInvalidEntry` (the entry) or `SkippedInvalidEvidence` (the files) with every problem in
`PublishResult.Message`. This also applies to a dry run. Without it a rejected upload would only fail after the
observation was created, leaving a public observation without its photo or sound. The rules are taken from
iNaturalist's open source and forum, not from an official reference, and are not verified against the live service yet.

| Rule | Limit |
|---|---|
| File size (spectrogram and audio, each) | 20 MB (`INaturalistOptions.MaxEvidenceBytes`, default 20,000,000 bytes) |
| Observation date | not in the future (checked with the tolerance of every time zone), not older than 130 years |
| Latitude | greater than -90 and less than 90 |
| Longitude | -180 to 180 |
| Position | not exactly 0, 0 |
| Species name (`species_guess`) | at most 255 characters |
| `INaturalistOptions.TagList` | at most 750 characters in total, at most 255 per tag |

Accepted as they are: PNG photos (iNaturalist scales them to 2048 pixels on the longest side) and WAV sounds
(stored unchanged; MP3 and M4A are accepted by iNaturalist too, but the input format only knows `.wav`).
iNaturalist documents no duration or sample-rate limit. For orientation: a mono 16 bit recording at 384 kHz takes
768 kB per second, so about 26 seconds fill 20 MB. If a file is too large, shorten the recording or lower its sample
rate and run again; nothing was created for that entry.

Error messages that iNaturalist itself sends back are passed through unchanged in `PublishResult.Message`.

## Troubleshooting

| Symptom | Cause / fix |
|---|---|
| `ArgumentException` about `ClientId` | `INaturalistOptions.ClientId` is empty. |
| `INaturalistLoginException`, `Reason` = `ListenerUnavailable`, on login | Port in use and no fallback port could be opened, see [Port 45679](#port-45679). |
| iNaturalist error page after clicking Authorize | The registered redirect URI differs from `INaturalistOptions.RedirectUri`. |
| `INaturalistLoginException`, `Reason` = `TimedOut`, on login | Nobody completed the browser login within `AuthorizationTimeout`. |
| `PlatformNotSupportedException` when logging in or saving the token | Not Windows. The check runs before the browser opens. Pass `allowPlaintextOnNonWindows: true` (file mode `0600`, protects against other local users only) or supply your own `INaturalistTokenStore`. |
| Login asked again although you logged in before | The token file could not be read (corrupt, or encrypted by another Windows user); a warning is logged and the file is replaced by the next login. |
| 401/403 during the JWT exchange | See [Why there is an extra JWT exchange](#why-there-is-an-extra-jwt-exchange); check the application registration at <https://www.inaturalist.org/oauth/applications>. |
