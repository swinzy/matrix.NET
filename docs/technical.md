# Technical Documentation

How matrix.NET works, how its parts fit together, the decisions behind the design, known
pitfalls and limitations, and where it is heading.

This is not a guide. There is (not yet) a dedicated usage guide available.

- **Spec baseline:** [Matrix Client-Server API](https://spec.matrix.org/latest/client-server-api/) v1.19
- **Target framework:** .NET 8
- **Roadmap:** [TODO.md](../TODO.md)

## Contents

1. [Project layout](#1-project-layout)
2. [How it works today](#2-how-it-works-today)
3. [Design decisions](#3-design-decisions)
4. [Pitfalls and limitations](#4-pitfalls-and-limitations)
5. [Future plans](#5-future-plans)
6. [Appendix: session design in other SDKs](#appendix-session-design-in-other-sdks)

## 1. Project layout

```
src/matrix.NET/
├── matrix.NET.sln
├── global.json                 # Opts dotnet test into Microsoft.Testing.Platform
├── matrix.NET/                 # The library (namespace TeamBanana.MatrixDotNet)
└── matrix.NET.Tests/           # xUnit v3 unit and integration tests
```

`src/matrix.NET/ConsoleClient/` may exist locally as a scratch client. It is ignored by git
and is not part of the repository.

## 2. How it works today

### Modules

| Type | Role |
|---|---|
| `MatrixServer` | Unauthenticated endpoints of a homeserver: supported login types, login |
| `MatrixClient` | Authenticated endpoints; placeholder until the session design (§3.2) is implemented |
| `LoginRequest` / `LoginResponse` | Body of `POST /login` and its 200 response |
| `LoginFlow` | One entry of `GET /login`'s `flows` |
| `IIdentifier` / `UserIdentifier` | User identifier objects (`m.id.user`) |
| `MatrixException` | A Matrix error response (`errcode`, `error`) or a non-Matrix HTTP failure |

### Request pipeline

`MatrixServer` wraps a single `HttpClient`. Every call follows the same steps:

1. Serialise the request with the shared `JsonSerializerOptions`: `snake_case` property
   names, and `null` properties omitted.
2. Send it to the path under `/_matrix/client/v3/`.
3. `EnsureSuccessAsync` turns any non-2xx response into a `MatrixException`. If the body is
   not a Matrix error (for example an HTML page from a reverse proxy), the error code falls
   back to `M_UNKNOWN`.
4. Deserialise the response. Properties the spec marks as required use C#'s `required`
   modifier, so a missing field throws `JsonException` rather than producing a half-empty
   object.

`MatrixServer` can be constructed from a `Uri`, or from an existing `HttpClient` for tests
and `IHttpClientFactory`.

### Identifier serialisation

`IIdentifier` uses System.Text.Json polymorphism with `type` as the discriminator:

```csharp
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(UserIdentifier), "m.id.user")]
public interface IIdentifier { ... }
```

The C# `Type` property is `[JsonIgnore]`d on implementations; otherwise `type` would be
written twice. Adding `m.id.thirdparty` or `m.id.phone` means adding a class and one more
`[JsonDerivedType]`.

### Tests

- **Unit tests** use `StubHttpMessageHandler`, which returns a canned response and records
  the request. They need no network.
- **Integration tests** run against a real homeserver configured in
  `matrix.NET.Tests/testsettings.json` (git-ignored; see `testsettings.example.json`) or
  `MATRIX_TEST_*` environment variables. They are skipped when not configured.
- The optional `DeviceId` setting makes every test login reuse one device instead of
  creating a new one per run.

## 3. Design decisions

Each decision records its status, what was decided and why.

- **Decided**: agreed by the maintainer.
- **Proposed**: recommended during design discussion, awaiting confirmation.
- **Deferred**: agreed in principle but not scheduled yet.

### 3.1 Foundations

General decisions made while building the first endpoints.

#### D1. Errors are exceptions, not return values (Decided)

Server errors throw `MatrixException` with `StatusCode`, `ErrorCode` and the server's
message.

**Why:** Exceptions are .NET convention. matrix-nio's error-as-value style forces a type
check after every call.

#### D2. `UserIdentifier.User` is `required` (Decided)

The spec does not formally mark `user` as required: it only appears in the example JSON.
Semantically it is mandatory, though, because without it the homeserver cannot know who is
logging in.

#### D3. Test client kept out of the repository (Decided)

`ConsoleClient` is the maintainer's local scratch project, so it is git-ignored and removed
from `matrix.NET.sln`. It keeps its own local `ConsoleClient.sln`.

#### D4. Integration test credentials never committed (Decided)

`testsettings.json` is git-ignored. Only `testsettings.example.json` is committed, and
environment variables override the file.

#### D5. Naming: `MatrixServer` and `MatrixClient` (Decided)

The public entry types are `MatrixServer` and `MatrixClient`, renamed from `Server` and
`Client`.

**Why:** `Server` and `Client` are generic enough to clash with types in consuming apps and
other libraries.

### 3.2 Authenticated session design

Researched in September 2026 by reading the source of matrix-js-sdk, matrix-nio,
mautrix-python and matrix-rust-sdk (see the [appendix](#appendix-session-design-in-other-sdks)).
All four leave credential persistence to the app, and all but rust-sdk leave session state
loosely defined. The goal is to make the common lifecycle of a chat app easy:

1. First launch: log in and save the session.
2. Later launches: restore the saved session without logging in again.
3. Token expiry while running: handled by the library, which tells the app to save new tokens.
4. Session invalidated or logged out: the app is told clearly, so it can return to the login screen.

The intended usage:

```csharp
// Unauthenticated, stateless operations
var server = new MatrixServer(new Uri("https://matrix.example.org/"));
MatrixSession session = await server.LoginAsync(request);

// Authenticated; owns the session lifecycle. Same constructor for login and restore.
await using var client = new MatrixClient(session);
```

#### D6. `MatrixServer` returns a `MatrixSession` (Decided)

`MatrixServer.LoginAsync` returns a `MatrixSession` instead of a client.

**Why:** `MatrixServer` should not care whether a `MatrixClient` exists. The session is the
part the app has to persist, so it should be a first-class value rather than state hidden
inside a client.

#### D7. Separate unauthenticated and authenticated types (Decided)

Unauthenticated endpoints (discovery, login types, login, registration) live on
`MatrixServer`. Authenticated endpoints live on `MatrixClient`, which can only be created
from a session.

**Why:** A `MatrixClient` can never be in a "not logged in" state, so no runtime "not logged
in" checks are needed and misuse is caught at compile time. None of the four SDKs researched
does this. They use empty-string tokens, runtime decorators or `AuthenticationRequired`
errors instead.

#### D8. One way to create a client: its constructor (Decided)

A `MatrixClient` is created only through its constructor, from a session. It makes no
difference whether the session was just returned by `MatrixServer.LoginAsync` or loaded from
storage. There is no `MatrixClient.LoginAsync` wrapper.

**Why:** Login and restore converge on one code path, so there is a single way to create a
client to learn, document and test. The cost is one extra line at login.

This replaces an earlier proposal for a static `Client.LoginAsync(server, request)` factory.

#### D9. `MatrixClient` does not depend on `MatrixServer` (Decided)

`MatrixClient` depends only on its session and an HTTP source; it never sees a
`MatrixServer`. The plumbing both types need moves to an internal transport layer that both
use:

- HTTP sending
- JSON options
- error mapping (currently `MatrixServer.EnsureSuccessAsync`)

```
             internal transport (JSON options, error mapping, auth header)
               ↑                                   ↑
MatrixServer (no credentials)          MatrixClient (session, refresh handling)
```

**Why:** Coupling is about direction, and here there is none between the two public types.
Requiring a server to restore a session would force apps to create one that serves no other
purpose.

#### D10. HTTP source overloads (Decided)

```csharp
new MatrixClient(session);                      // simplest: owns its HttpClient
new MatrixClient(session, httpClientFactory);   // DI and long-running apps
new MatrixClient(session, httpClient);          // caller-owned, e.g. tests with a stub handler
```

Rules:

- The library-owned `HttpClient` uses `SocketsHttpHandler` with `PooledConnectionLifetime`,
  so long-running clients pick up DNS changes without a factory.
- With a factory, a client is taken from it per request, which is how `IHttpClientFactory`
  is meant to be used.
- Whoever creates an `HttpClient` disposes it: `MatrixClient` disposes only the one it
  created.
- Requests use absolute URIs built from the session's homeserver URL and never rely on
  `HttpClient.BaseAddress`, because factory clients may not have one.

**Why:**

- **The factory is optional, not required:** requiring `IHttpClientFactory` would force
  console apps, Unity and non-DI desktop apps to pull in `Microsoft.Extensions.Http` and a
  `ServiceCollection` just to obtain one.
- **A factory is still offered:** a chat client's sync loop can run for days, and one
  long-lived `HttpClient` would keep connecting to stale DNS results.

#### D11. `MatrixSession` contents (Decided)

`MatrixSession` is a serialisable record containing:

- the homeserver URL
- user ID and device ID
- access token, and an optional refresh token
- the token expiry as an absolute `DateTimeOffset`

Its `ToString()` hides the tokens.

**Why:**

- The homeserver URL is included because restoring then needs a single value, and the
  client needs it to build request URIs (D10). Element X's bindings over rust-sdk also store
  the URL.
- The expiry is stored as an absolute time because the spec's `expires_in_ms` is relative:
  persisted as is, it is meaningless after a restart.
- Hiding tokens in `ToString()` keeps them out of logs.

#### D12. Automatic token refresh: on by default, can be turned off (Decided)

Automatic refresh is enabled by default and disabled through
`ClientOptions.AutoRefreshToken`. `ClientOptions` is an optional constructor parameter; an
options object keeps future options from changing constructor signatures.

It is not implemented yet. For now the switch only decides what happens when the homeserver
rejects the token with `M_UNKNOWN_TOKEN`, as shown below.

| Switch | Session has refresh token | Result | Message |
|---|---|---|---|
| On | Yes | `NotImplementedException` | `Automatic token refresh is not implemented yet. Set ClientOptions.AutoRefreshToken to false to disable it.` |
| On | No | `MatrixUnknownTokenException` | `The access token is no longer valid and the session has no refresh token. Log in again.` |
| Off | Yes | `MatrixUnknownTokenException` | `The access token is no longer valid and automatic token refresh is disabled. Refresh the session manually or log in again.` |
| Off | No | `MatrixUnknownTokenException` | Same as On / No |

Rules for these cases:

- The exception is thrown when a refresh would actually be needed, not when a
  `MatrixClient` is created.
- The homeserver's own `error` text is kept alongside the library's message.
- Every `MatrixUnknownTokenException` case also raises the session-invalidated notification
  (D14).

**Why:**

- **Default on:** most apps want refresh, and rust-sdk's opt-in refresh is easy to miss.
- **Configurable:** some apps manage tokens themselves.
- **`NotImplementedException` instead of silently ignoring the switch:** this makes the gap
  visible.
- **Different messages per switch:** they tell the developer exactly which action fixes the
  situation.

When the switch is on, a login should send `refresh_token: true`. Under the spec, this
invites the homeserver to issue expiring tokens. Default configurations will therefore reach
the `NotImplementedException` path once the token expires. That is intended: it makes the
missing feature impossible to overlook.

#### D13. Exception subclasses are added on demand (Decided)

`MatrixException` stays the single error type until a specific error needs more than an
error code. A subclass is added when that happens, not in advance.

The first subclass is `MatrixUnknownTokenException : MatrixException`, which adds a
`SoftLogout` property.

**Why:**

- A large upfront hierarchy, as in mautrix-python, costs maintenance before anyone needs it.
- `M_UNKNOWN_TOKEN` qualifies because the app must read `soft_logout` to decide whether to
  keep local data, so that information cannot live only in the message text.

#### D14. Session change notifications (Decided)

`MatrixClient` raises one event for session changes. Its two cases are:

- **Tokens refreshed:** the app saves the new session.
- **Session invalidated:** carries `SoftLogout`; the app returns to the login screen.

**Why:** Four SDKs out of four leave soft logout entirely to the app. rust-sdk's
session-change stream and save callback are the most usable model, and a single event is
the idiomatic .NET form.

#### D15. Logout invalidates `MatrixClient` but does not dispose it (Decided)

**Rule:** after logout, release what is no longer needed and keep what is, marking the
client invalid.

| After logout | Needed? | Reason |
|---|---|---|
| Access and refresh tokens | No | Invalid |
| HTTP connections, future sync loop | No | Stop and release |
| User ID, device ID | Yes | The app needs to know which account logged out, e.g. to remove it from an account list |
| Local stores (future room cache, crypto keys) | Yes | After an explicit logout the app needs the client to clean them up. After a soft logout they must be kept and reused when logging in again on the same device, as Element does |

**Resulting design:**

- `LogoutAsync` marks the client as logged out, stops background work and releases network
  resources.
- Identity properties stay readable, and every API call throws.
- `MatrixClient` implements `IAsyncDisposable`, and the app disposes it with `await using`,
  like `DbConnection`'s `Close` versus `Dispose`.
- Logout does not dispose automatically; otherwise nothing could clean up or reuse local
  data afterwards.

**Why:** In JS and rust-sdk, `isLoggedIn()` or the auth state still reports "logged in"
after logout. In nio, identity and stores are left in an undefined state. An explicit state
avoids both.

#### D16. Authentication is added by the transport layer, not a `DelegatingHandler` (Decided)

Every authenticated request goes through one internal send method in the transport layer.
It attaches the `Authorization: Bearer` header per request and will also own
refresh-and-retry. Each endpoint declares its authentication requirement (`Required`,
`Optional` or `None`), similar to rust-sdk's per-endpoint metadata.

**Why:** A `DelegatingHandler` was considered and rejected:

- **Incompatible with injected clients (D10):** a client created by `IHttpClientFactory`
  already has its pipeline built, so the library cannot insert a handler. Users would have
  to register it themselves, which is easy to forget.
- **Unsafe for multiple accounts:** `IHttpClientFactory` pools and shares handler instances,
  so a handler holding session state could be shared between accounts.
- **Awkward retries:** a `HttpRequestMessage` cannot be sent twice, and its content may
  already be consumed. The transport method instead takes a request factory and rebuilds the
  request.
- **No endpoint context:** a handler does not know which endpoint it is serving. The
  transport method receives the requirement as a parameter.

Users can still add their own handlers (logging, Polly) to the `HttpClient` or factory they
pass in; only authentication stays out of the handler pipeline. The transport's
`HttpClient` is private, so there is no path around the header logic.

#### D17. Credentials are never persisted by the library (Decided)

The app stores `MatrixSession` wherever it sees fit: keychain, encrypted file, database.

**Why:** Every SDK researched does this. Secure storage is platform-specific: Element Web
uses encrypted localStorage and Element X uses the system keychain.

#### D18. DI registration helper (Deferred)

```csharp
services.AddMatrix();                                  // registers IMatrixClientFactory
var client = matrixClientFactory.Create(session);      // uses IHttpClientFactory internally
```

**Why deferred:** a session is runtime data, so `MatrixClient` cannot be a typed client
resolved directly from DI. A small factory hides `IHttpClientFactory` from DI users, but it
is a convenience that can wait until someone needs it.

## 4. Pitfalls and limitations

### Pitfalls

- **Use the spec source, not summaries.** When checking the spec, read the OpenAPI
  definitions in
  [matrix-org/matrix-spec](https://github.com/matrix-org/matrix-spec/tree/main/data/api/client-server)
  (e.g. `login.yaml`) and `content/client-server-api/_index.md`. A web-page summary used
  during development got several details wrong:
  - it named the `m.id.phone` field `number` instead of `phone`;
  - it gave the wrong status codes for login errors;
  - it said `identifier` was always required.
- **Required vs example-only fields.** Some fields are only shown in examples, not listed as
  required. Judge semantically and record the reasoning.
- **Polymorphic discriminator clash.** A property that serialises to the discriminator name
  (`type`) must be `[JsonIgnore]`d, or System.Text.Json fails.
- **`IHttpClientFactory` shares handlers.** Never put per-account state in a
  `DelegatingHandler` (D16).
- **.NET 10 SDK and xUnit v3.** `dotnet test` refuses to run xUnit v3 through VSTest on the
  .NET 10 SDK. `src/matrix.NET/global.json` opts into Microsoft.Testing.Platform. Filters
  use MTP syntax, e.g. `dotnet test --filter-class <FullClassName>`.
- **Integration tests touch a real account.**
  - Without `DeviceId`, each successful login creates a new device.
  - The wrong-password test counts as a failed login and can trigger rate limiting
    (`M_LIMIT_EXCEEDED`) if run repeatedly.
  - Reusing a device ID may invalidate that device's earlier tokens.
- **Login type depends on the homeserver.** Homeservers that only support OAuth 2.0 answer
  `GET /login` with 404 `M_UNRECOGNIZED`, and password login is impossible there.

### Current limitations

- The session design (§3.2) is decided but not implemented:
  - `MatrixServer.LoginAsync` still returns `LoginResponse`;
  - `MatrixClient` is empty, with no authenticated endpoints, including logout;
  - there is no internal transport layer yet.
- Only `m.id.user` identifiers are supported.
- Rate-limit details (`retry_after_ms`) are not exposed.
- `well_known` in the login response and the deprecated `home_server` field are not
  modelled.
- There is no server discovery (`.well-known`) yet. The homeserver URL must be known.

## 5. Future plans

The feature roadmap lives in [TODO.md](../TODO.md). Near-term technical work:

1. Implement the session design (D6 to D17): internal transport, `MatrixSession`,
   `ClientOptions`, `MatrixUnknownTokenException` and the `MatrixClient` lifecycle.
2. Logout and `whoami`, the first authenticated endpoints.
3. Real automatic token refresh, replacing the `NotImplementedException` path: single-flight
   refresh with one retry, and "logged out" only on a definite rejection.
4. Server discovery through `.well-known/matrix/client`.

Later considerations, not designed yet:

- **DI registration helper:** see D18.
- **Application services (bridges):** mautrix-python's model is worth studying. It shares
  one token and HTTP session across many puppeted users, distinguished by the `user_id`
  query parameter.
- **End-to-end encryption:** it binds the crypto store to a (user ID, device ID) pair.
  Restoring a session with a different device than the store causes mismatches, so session
  and store design must account for this.

## Appendix: session design in other SDKs

Summary of source research from September 2026.

| | matrix-js-sdk | matrix-nio | mautrix-python | matrix-rust-sdk |
|---|---|---|---|---|
| Session object | None; spread over client and HTTP layer | None; mutable fields on the client | None; identity on client, token on HTTP layer | `MatrixSession` (meta + tokens), serialisable |
| Login mutates client | `login()` partly (no device ID); `loginRequest()` no | Yes | Yes by default (`store_access_token=False` opts out) | Yes; auth state can only be set once |
| Restore | Pass tokens to `createClient` | `restore_login()` | Pass token to constructor | `restore_session(session)` |
| Unauthenticated call handling | Caller picks `request` or `authedRequest` | Runtime decorator raises `LocalProtocolError` | Empty token means no header | Per-endpoint metadata; `AuthenticationRequired` |
| Local state after logout | Unchanged; `isLoggedIn()` stays true | Token cleared only | Token and device ID cleared | Unchanged; discard the client |
| Token refresh | Automatic for OAuth sessions only | None | None | Automatic when enabled; save callback and change stream |
| Soft logout | Left to app | Clears token; otherwise left to app | None | Reported via session change stream |
| Errors | Exceptions | Return values | Exception per errcode | `Result` |

Notes:

- **matrix-python-sdk**, the archived official Python SDK, recommends matrix-nio instead.
- **mautrix-python's appservice model:**
  - `AppServiceAPI` impersonates users by adding the `user_id` query parameter to requests
    made with the appservice token.
  - Child APIs share the parent's token, HTTP session and transaction counter, so each extra
    puppet costs almost nothing.
