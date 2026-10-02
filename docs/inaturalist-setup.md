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
catches that single request. `AuthorizationTimeout` (default 5 minutes) bounds the wait.

`EnsureAuthenticatedAsync` (called by the publisher) returns the stored API token and renews it silently when it has expired:
iNaturalist OAuth tokens never expire, so the browser login is needed **once** (until the user revokes access). A new login only happens if iNaturalist rejects the stored token. A network error never starts a login.

### Port 45679

iNaturalist requires the redirect URI of each request to match a registered value exactly and is
not confirmed to accept an arbitrary loopback port, so the port is fixed. 45679 avoids common
dev-server ports. If it is taken on a machine, the login fails with an `HttpListenerException`. Pick another
port, set `INaturalistOptions.RedirectUri` and register the identical URI at iNaturalist.

The host must be `127.0.0.1` in both places: `HttpListener` matches the request's `Host` header
text verbatim, so `localhost` would get a 404 from a listener bound to `127.0.0.1`.

### Why there is an extra JWT exchange

iNaturalist's REST API does not accept a raw OAuth access token as Bearer token (401). The token is
first exchanged for a JWT with `GET https://www.inaturalist.org/users/api_token` (note the `www`
host), and that JWT, valid about 24 h, is used for all API calls and stored locally. This is
undocumented in the official API reference.

## Troubleshooting

| Symptom | Cause / fix |
|---|---|
| `ArgumentException` about `ClientId` | `INaturalistOptions.ClientId` is empty. |
| `HttpListenerException` on login | Port in use, see [Port 45679](#port-45679). |
| iNaturalist error page after clicking Authorize | The registered redirect URI differs from `INaturalistOptions.RedirectUri`. |
| `TimeoutException` on login | Nobody completed the browser login within `AuthorizationTimeout`. |
| `PlatformNotSupportedException` when saving the token | Not Windows. Pass `allowPlaintextOnNonWindows: true` or supply your own `INaturalistTokenStore`. |
| 401/403 during the JWT exchange | See [Why there is an extra JWT exchange](#why-there-is-an-extra-jwt-exchange); check the application registration at <https://www.inaturalist.org/oauth/applications>. |
