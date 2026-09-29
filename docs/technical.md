# Technical Documentation

How matrix.NET works, how its parts fit together, the decisions behind the design, known
pitfalls and limitations, and where it is heading.

This is not a guide. There is (not yet) a dedicated usage guide available.

- **Spec baseline:** [Matrix Client-Server API](https://spec.matrix.org/latest/client-server-api/) v1.19
- **Target framework:** .NET 10 (LTS, supported until November 2028)
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
| `MatrixErrorCodes` | Constants for every error code defined by the spec, for exception filters |
| `Transport.MatrixTransport` (internal) | The single path every request goes through (D9, D16) |
| `Transport.AuthRequirement` (internal) | Whether an endpoint needs an access token: `None`, `Optional` or `Required` |

### Request pipeline

Public types never touch `HttpClient` directly. Each endpoint method makes one
`MatrixTransport.SendAsync` call, passing the HTTP method, a relative path such as
`_matrix/client/v3/login`, an optional body, and its `AuthRequirement`. The transport then:

1. Builds an absolute URI from the homeserver URL and the relative path. A trailing slash is
   added to the homeserver URL if missing, so a homeserver under a sub-path
   (`https://example.org/matrix/`) keeps that path.
2. Serialises the body, if any, with the shared `JsonSerializerOptions`: `snake_case`
   property names, and `null` properties omitted.
3. Applies the endpoint's authentication requirement:
   - `None`: never sends a token.
   - `Optional`: sends one if available.
   - `Required`: sends one, or throws `InvalidOperationException` before sending anything.
     This indicates a library bug, because `MatrixClient` always has a session.
4. Takes an `HttpClient` from its client source and sends the request. The client source
   and access token source are both read once per request.
5. Turns any non-2xx response into a `MatrixException`. If the body is not a Matrix error
   (for example an HTML page from a reverse proxy), the error code falls back to `M_UNKNOWN`.
6. Deserialises the response. Properties the spec marks as required use C#'s `required`
   modifier, so a missing field throws `JsonException` rather than producing a half-empty
   object. A literal `null` body also throws `JsonException`.

The request is built from its parts inside the transport rather than passed in, so the
future refresh-and-retry logic can rebuild it (D16).

`MatrixServer` can be constructed from a homeserver `Uri` alone, which uses the shared
client, or together with an `HttpClient` for tests.

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

Decisions are numbered in the order they were made, and a number is never reused or
changed. This keeps references such as "D10" stable across the code, commits and
discussions. Sections group decisions by topic, so numbers within a section may not be
contiguous. A superseded decision keeps its number and says what replaced it.

### 3.1 Foundations

General decisions made while building the first endpoints.

#### D1. Errors are exceptions, not return values (Decided)

Server errors throw `MatrixException` with `StatusCode`, `ErrorCode` and the server's
message. This also covers expected failures such as a wrong password. There is no
`Result` type.

Handling a specific error in place uses an exception filter with the constants in
`MatrixErrorCodes`, which lists every error code defined by the spec:

```csharp
catch (MatrixException e) when (e.ErrorCode == MatrixErrorCodes.Forbidden) { ... }
```

Errors that carry extra data get a subclass on demand (D13).

`ErrorCode` is a plain string, and `MatrixErrorCodes` is only a set of constants for
comparison. Parsing never depends on them. Error codes the library does not know about are
passed through unchanged, whether added by a newer spec or custom to a homeserver (e.g.
`COM.EXAMPLE_FORBIDDEN`). Unknown fields in error and success responses are ignored. Apps
therefore keep working when the homeserver is ahead of the SDK. This is covered by unit
tests.

**Why:**

- **Exceptions are .NET convention.** The base class library, `HttpClient` and EF Core all
  use them.
- **A `Result` type loses its main benefit in C#.** C# 14 (.NET 10) has no discriminated
  unions, so the compiler cannot force callers to handle the error case; `.Value` can be
  read without checking.
- **Two error channels would remain anyway.** Network failures, timeouts and cancellation
  (`OperationCanceledException`) are still exceptions, so a `Result` would only cover Matrix
  errors. matrix-nio has exactly this split, and its error-as-value style forces a type check
  after every call.
- **The `Try` pattern does not fit async code.** Async methods cannot have `out` parameters.
- **Most errors should bubble up** to a common handler in a chat app. Where one is handled in
  place, the exception filter above is short.
- **Exceptions are cheap compared with the network.** Throwing costs microseconds; the HTTP
  request costs tens to hundreds of milliseconds.

A `Try…Async` method returning a result object can still be added later for a specific
high-frequency case. That is purely additive.

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

#### D20. A full SDK that also works as a thin API library (Decided in principle)

matrix.NET aims to be a full SDK, including sync, room state and end-to-end encryption. It
must also serve users who only want typed access to the Client-Server API. Principles:

- **The endpoint layer stands alone.** Calling endpoint methods on `MatrixServer` and
  `MatrixClient` never requires a store, a background task or any local state beyond the
  session.
- **Stateful features are opt-in.** The sync loop, room and state caches, end-to-end
  encryption and their persistence are built on top of the endpoint layer. They are enabled
  explicitly and never required for endpoint calls.
- **Thin-library users pay nothing for the full SDK.** They get no hidden I/O, no background
  work, and ideally no dependencies such as a database or crypto library.

How to layer this is still open. The options are opt-in features on the same types,
separate higher-level types, or separate packages.

**Why:** The SDKs researched are either thin, like the matrix-nio and mautrix-python cores,
or full, like matrix-rust-sdk. A layered design serves both audiences. It also keeps the
endpoint layer easy to test in isolation.

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

Endpoints split into three groups:

- **Only meaningful before login**, such as discovery, login types, login and registration.
  These live on `MatrixServer`.
- **Requiring authentication.** These live on `MatrixClient`, which can only be created from
  a session.
- **Usable with or without a session.** These live on both types; see D19.

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

Rules for the constructor:

- **The constructor never performs I/O.** It only stores its arguments. This is a lasting
  commitment, not a description of the current code.
- **Asynchronous resources are initialised lazily**, on first use. Examples are the future
  crypto store, the sync state and cached server metadata. Initialisation runs once and is
  thread-safe. A failure is thrown from the call that triggered it, and the next call tries
  again.
- **The access token is not validated on creation.** The first real request reveals whether
  it is still valid: a rejected token throws `MatrixUnknownTokenException` and raises the
  session-invalidated notification (D14). Apps that want an early check can call `whoami`
  themselves.
- **Heavy start-up work belongs to an explicit start step.** For example, the future sync loop
  will be started with its own method, as matrix-js-sdk separates `createClient()` from
  `startClient()`.

**Why no I/O on creation:** none of the I/O the library will ever need has to happen at
creation. Keeping the constructor free of I/O means no async factory or initialisation method
is needed. The alternatives were rejected:

- **A static `CreateAsync` factory:** it would be an empty shell today and would break D8
  from the start.
- **A required `InitializeAsync()`:** it is easy to forget and leaves half-initialised
  objects.

If a need for creation-time I/O ever appears, a factory can still be added alongside the
constructor without breaking it.

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
new MatrixClient(session);                      // simplest: uses the library's shared HttpClient
new MatrixClient(session, httpClient);          // caller-owned, e.g. tests with a stub handler
new MatrixClient(session, () => httpClient);    // caller-supplied source, read once per request
```

`MatrixServer` offers the same choices, with a homeserver `Uri` in place of the session.

`IHttpClientFactory` support lives in a separate DI extension package (D18), which passes
`() => factory.CreateClient(...)` to the `Func<HttpClient>` overload.

Rules:

- Without a caller-supplied source, the library uses one static `HttpClient`, shared by every
  `MatrixServer` and `MatrixClient`. It uses `SocketsHttpHandler` with
  `PooledConnectionLifetime`, so long-running clients pick up DNS changes without a factory.
  This follows Microsoft's `HttpClient` guidance for apps without DI.
- The shared client is never disposed, and the library never disposes a caller-supplied
  `HttpClient`, so HTTP clients involve no ownership or disposal.
- The shared client is configured for being shared:
  - **Cookies are disabled** (`UseCookies = false`). One cookie container would otherwise be
    shared by every account and homeserver. The Matrix API does not use cookies, but reverse
    proxies and load balancers may set them.
  - **Timeouts are per request.** The client's own `Timeout` is infinite. The transport
    applies a default of 100 seconds to each request, matching `HttpClient`'s default, and
    individual endpoints can override it, e.g. long-polling `/sync`. A timeout throws
    `TaskCanceledException` with an inner `TimeoutException`, exactly as `HttpClient`
    does, so existing handling keeps working. Cancellation by the caller is not reported as
    a timeout.
  - **Responses are decompressed automatically** (GZip, Deflate and Brotli), because
    `/sync` responses are large JSON. Decompression is transparent to content, so hashes of
    end-to-end encrypted attachments still hold. Two side effects matter for future media
    downloads: `Content-Length` no longer reflects the bytes received, so progress
    reporting must not rely on it; and `Range` requests address compressed bytes if the
    server compresses, so resumable downloads need care.
    Decompression can be turned off for users who need the raw encoded bytes, such as a thin
    forwarding layer: `new MatrixServer(uri, automaticDecompression: false)`, and the same
    option on `ClientOptions`. It is a handler-level setting, so it cannot vary per request.
    Turning it off selects a second shared client, identical except for decompression, which
    is created only when first needed. Without decompression no `Accept-Encoding` header is
    sent, so homeservers answer uncompressed and JSON parsing is unaffected.
- Caller-supplied clients keep their own settings. Their `Timeout` still applies alongside
  the transport's per-request timeout.
- A `Func<HttpClient>` source is called once per request. With a factory behind it, this is
  how `IHttpClientFactory` is meant to be used.
- Requests use absolute URIs built from the session's homeserver URL and never rely on
  `HttpClient.BaseAddress`, because factory clients may not have one.
- Internally the transport only sees a `Func<HttpClient>`. The core library has no
  dependency on `IHttpClientFactory`.

**Why:**

- **One shared client instead of one per instance:** this was changed from "each instance
  owns and disposes its own client" when building the transport. Microsoft recommends one
  long-lived `HttpClient`. A shared one also removes a disposal rule that is easy to get
  wrong.
- **The factory is optional, not required:** requiring `IHttpClientFactory` would force
  console apps, Unity and non-DI desktop apps to pull in `Microsoft.Extensions.Http` and a
  `ServiceCollection` just to obtain one.
- **A factory is still offered:** a chat client's sync loop can run for days, and one
  long-lived `HttpClient` would keep connecting to stale DNS results.
- **The factory is in an extension package, not the core:** `IHttpClientFactory` is defined
  in the `Microsoft.Extensions.Http` package, not the base framework. A factory overload in
  the core would make every user depend on that package and on the DI, logging and options
  abstractions it brings, although not everyone uses DI. Separate DI packages are the usual
  .NET pattern (e.g. `Microsoft.Extensions.Http` itself). Two other options were rejected:
  - accepting the dependency in the core;
  - offering only `Func<HttpClient>`, which would leave DI users to wire the factory up
    themselves.
- **`Func<HttpClient>` is public:** it is what the extension package builds on, so the
  package needs no access to internals. Non-DI apps can also use it for their own client
  management.
- **Misuse of `Func<HttpClient>` is only guarded by documentation:**
  - The risk: `() => new HttpClient()` looks harmless, but opens new connections for every
    request. That is slow, and can exhaust sockets under heavy load.
  - It cannot be detected at runtime, because `IHttpClientFactory` also returns a new
    instance per call.
  - The parameter is named `httpClientSource`, and its XML documentation states the
    once-per-request contract and shows the anti-pattern. The overload is deliberately not
    hidden from IntelliSense, so users see the warning in completion.
  - Two alternatives were rejected. Hiding it with `[EditorBrowsable]` is inconsistent
    across IDEs. Making it internal, with `InternalsVisibleTo` for the extension package,
    trades a low-probability problem for a cross-package code smell.
  - The impact is mostly latency for chat clients, which send few requests. Non-DI users
    normally pick the `HttpClient` overload anyway.

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

Two independent settings control refresh tokens. They live on different types, because
`MatrixServer` does not know about `MatrixClient` (D6, D9).

| Setting | Question it answers | Default |
|---|---|---|
| `LoginRequest.RefreshToken` | Should login ask the homeserver for a refresh token? | `true` |
| `ClientOptions.AutoRefreshToken` | Who refreshes: the library or the app? | `true` (the library) |

`ClientOptions` is an optional `MatrixClient` constructor parameter. An options object keeps
future options from changing constructor signatures.

Every combination is meaningful, so neither type needs to know about the other:

| Refresh token requested | Auto refresh | Meaning |
|---|---|---|
| Yes | On | The library handles refresh entirely. **This is the default.** |
| Yes | Off | The app manages refresh itself, e.g. to coordinate refreshes across processes. |
| No | Either | No refresh token: once the access token is invalid, the user must log in again. |

When the homeserver rejects the access token with `M_UNKNOWN_TOKEN`:

| Auto refresh | Session has refresh token | Result |
|---|---|---|
| On | Yes | Refresh through `POST /refresh`, then retry the request once. Concurrent requests share a single refresh. If the homeserver definitively rejects the refresh, throw `MatrixUnknownTokenException`: `The access token expired and could not be refreshed. Log in again.` |
| On | No | `MatrixUnknownTokenException`: `The access token is no longer valid and the session has no refresh token. Log in again.` |
| Off | Yes | `MatrixUnknownTokenException`: `The access token is no longer valid and automatic token refresh is disabled. Refresh the session manually or log in again.` |
| Off | No | Same as On / No |

Rules:

- A refresh that fails for a transient reason, such as a network error or a 5xx response,
  throws that error without invalidating the session. Only a definite rejection means
  "logged out".
- The homeserver's own `error` text is kept alongside the library's message.
- A successful refresh raises the tokens-refreshed notification (D14). Every
  `MatrixUnknownTokenException` raises the session-invalidated notification.

**Why:**

- **Default on at both ends:** most apps want refresh, and rust-sdk's opt-in refresh is easy
  to miss. With login defaulting to not requesting a refresh token, "auto refresh on by
  default" would have had no effect in default usage. Requesting one also tells the
  homeserver the client supports refresh, as the spec intends.
- **Two settings instead of one:** "whether to have a refresh token" and "who refreshes it"
  are different questions. A single switch derived from the session could not support apps
  that refresh tokens themselves.
- **Different messages per case:** they tell the developer exactly which action fixes the
  situation.

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

#### D18. DI extension package (Decided in principle; Deferred)

All dependency-injection support lives in a separate package, for example
`TeamBanana.MatrixDotNet.Extensions.DependencyInjection`, following the
`Microsoft.Extensions.*` naming pattern. It depends on the core library and
`Microsoft.Extensions.Http`; the core depends on neither.

```csharp
services.AddMatrix();                                  // registers IMatrixClientFactory
var client = matrixClientFactory.Create(session);      // uses IHttpClientFactory internally
```

- `IMatrixClientFactory` creates `MatrixClient` and `MatrixServer` instances through the
  core's public `Func<HttpClient>` overloads (D10), backed by `IHttpClientFactory`.

**Why a separate package:** not every user needs DI. See D10.

**Why a factory interface:** a session is runtime data, so `MatrixClient` cannot be a typed
client resolved directly from DI. A small factory hides `IHttpClientFactory` from DI users.

**Why deferred:** it is a convenience that can wait until someone needs it. The core already
exposes everything the package needs.

#### D19. Endpoints usable with or without a session are declared on both types (Decided)

Some endpoints make sense both before and after login. Examples:

- `GET /versions`
- `GET /profile/{userId}`
- the published room directory

Each such endpoint is declared as a public method on both `MatrixServer` and `MatrixClient`,
and uses `AuthRequirement.Optional`:

- `server.GetProfileAsync(...)` sends no token.
- `client.GetProfileAsync(...)` sends the session's token automatically, including after a
  refresh.

Duplication is kept to one line per endpoint on each side:

- **One implementation:** the request logic lives once in an internal class that takes the
  transport. The public methods on both types forward to it in one line.
- **No signature drift:** both types implement one internal interface listing these
  endpoints, so if one side is changed and the other forgotten, the build fails.
- **One set of docs:** XML documentation is written once and reused with
  `<inheritdoc cref="..."/>`.

Each side remains free to diverge where that helps. For example, `MatrixClient` could default
the profile's user ID to its own, or an endpoint could move to one side only when the spec
changes. The spec has already done this once: since v1.11, downloading media uses
authenticated endpoints, and the unauthenticated ones are deprecated. Media download is
therefore a `MatrixClient`-only feature, not a shared one.

**Why:** Three other options were considered:

- **`client.Server`,** a `MatrixServer` wired to the client's live token source. It exposes
  login and registration on a logged-in client (`client.Server.LoginAsync`). That is harmless
  but semantically odd. Its advantages over the caller passing a token around remain valid:
  the token never leaves the library, and it follows refreshes and logout.
- **A shared public base class** (`MatrixServer` and `MatrixClient` both derive from it).
  This means the least code, but it adds a public type, uses up the single inheritance slot,
  and makes per-side differences awkward (`virtual`/`override`, or moving methods out of the
  base).
- **Exposing the shared endpoints through an interface-typed property**
  (`client.Public.GetProfileAsync()`). This adds an extra step to every call, and a cast
  back to `MatrixServer` would expose login again.

Since the two sides are expected to diverge, explicit declarations on each type are
preferred over inheritance.

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
  - the transport sends each request once. There is no refresh-and-retry and no
    `M_UNKNOWN_TOKEN` handling yet.
- Only `m.id.user` identifiers are supported.
- XML documentation is generated and shipped, so IDEs show it. Warning CS1591, for missing
  documentation, is silenced until all public members are documented.
- Rate-limit details (`retry_after_ms`) are not exposed.
- `well_known` in the login response and the deprecated `home_server` field are not
  modelled.
- There is no server discovery (`.well-known`) yet. The homeserver URL must be known.

## 5. Future plans

The feature roadmap lives in [TODO.md](../TODO.md). Near-term technical work:

1. Implement the session design (D6 to D17, D19): internal transport, `MatrixSession`,
   `ClientOptions`, `MatrixUnknownTokenException` and the `MatrixClient` lifecycle.
2. Logout and `whoami`, the first authenticated endpoints.
3. Automatic token refresh (D12).
4. Server discovery through `.well-known/matrix/client`.

Later considerations, not designed yet:

- **DI extension package:** see D18.
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
