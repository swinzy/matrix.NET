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
├── matrix.NET.AotSmoke/        # Console app published with Native AOT by AotSmokeTests (D29)
└── matrix.NET.Tests/           # xUnit v3 unit and integration tests
tools/
└── synapse/synapse.sh          # Local Synapse in podman for the integration tests
```

`src/matrix.NET/ConsoleClient/` may exist locally as a scratch client. It is ignored by git
and is not part of the repository.

## 2. How it works today

### Modules

| Type | Role |
|---|---|
| `MatrixServer` | Unauthenticated endpoints of a homeserver: supported login types, login and the shared endpoints |
| `ServerOptions` | Options for `MatrixServer`: automatic decompression (D25) |
| `MatrixClient` | Authenticated endpoints through a `MatrixSession`: `WhoAmIAsync`, `LogoutAsync`, `GetJoinedRoomsAsync`, `LeaveRoomAsync` and the shared endpoints so far; lifecycle `State` and `SessionChanged` (D14, D21); automatic token refresh (D12) |
| `ClientOptions` | Options for `MatrixClient`: automatic token refresh, refresh handler, automatic decompression |
| `ISessionRefreshHandler` / `DiscardingSessionRefreshHandler` | Saves refreshed sessions before their tokens are used; the discarding one saves nothing (D23) |
| `WhoAmIResponse` | Response of `GET /account/whoami` |
| `LoginRequest` | Body of `POST /login` |
| `MatrixSession` | A logged-in session: homeserver, user and device IDs, tokens, expiry (D11) |
| `LoginResponse` (internal) | Wire format of the `POST /login` response, turned into a `MatrixSession` |
| `LoginFlow` | One entry of `GET /login`'s `flows` |
| `VersionsResponse` | Response of `GET /versions`: supported spec versions and unstable features |
| `IIdentifier` / `UserIdentifier` | User identifier objects (`m.id.user`) |
| `MatrixException` | A Matrix error response (`errcode`, `error` as `ServerMessage`) or a non-Matrix HTTP failure |
| `MatrixUnknownTokenException` | `M_UNKNOWN_TOKEN`, with `SoftLogout` (D13, D21) |
| `MatrixUserLockedException` | `M_USER_LOCKED`, with `SoftLogout` (D13, D22) |
| `MatrixClientState` | `Active`, `Locked`, `LoggedOut` or `Invalidated` (D21, D22) |
| `SessionChangedEventArgs` / `SessionChangeKind` | Data of `MatrixClient.SessionChanged` (D14) |
| `MatrixErrorCodes` | Constants for every error code defined by the spec, for exception filters |
| `Transport.MatrixTransport` (internal) | The single path every request goes through (D9, D16) |
| `Transport.AuthRequirement` (internal) | Whether an endpoint needs an access token: `None`, `Optional` or `Required` |
| `Transport.ISharedEndpoints` (internal) | Endpoints usable with or without a session, implemented by both public types (D19) |
| `Transport.SharedEndpoints` (internal) | The single implementation of those endpoints |
| `Transport.MatrixJsonContext` (internal) | Source-generated JSON metadata for every library type (D29) |
| `Compatibility.HomeserverQuirks` (internal) | Every tolerated deviation of a homeserver from the spec (D27) |
| `MatrixVersion` | A comparable spec version; `Minimum` is v1.1 (D24) |
| `TransactionId` | Creates transaction IDs for idempotent requests (D28) |
| `MatrixFeature` | The features added after the minimum version, with the versions that added and removed them (D26) |
| `Transport.Endpoint` / `Transport.Endpoints` (internal) | One declaration per endpoint: method, path, authentication and feature; the transport sends only these (D26) |

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
- **Integration tests run against a local Synapse by default.** `tools/synapse/synapse.sh`
  runs one in podman with every rate limit turned off, registers a test user and writes
  `matrix.NET.Tests/testsettings.local.json` (git-ignored). Without that file, and without
  `MATRIX_TEST_*` environment variables, integration tests are skipped.
  - `tools/synapse/synapse.sh start` starts the latest Synapse on `http://127.0.0.1:8008/`.
  - `tools/synapse/synapse.sh start v1.98.0` starts an older release instead, e.g. to match
    a homeserver that has not been upgraded. Each version keeps its own data in
    `.synapse/<version>/`, since Synapse cannot migrate a database back to an older version.
  - `stop`, `status` and `reset [version]` stop it, show what runs, or start that version
    from empty data.
  - The tests read `testsettings.local.json` where the script writes it, so switching
    versions needs no rebuild.
  - Access tokens issued with a refresh token expire after 2 seconds
    (`refreshable_access_token_lifetime`), so `RefreshIntegrationTests` can wait for a real
    expiry and check the automatic refresh end to end. Logins without a refresh token, such
    as the shared one, never expire. That test only runs against the local Synapse, and is
    skipped before logging in otherwise.
  - The script rewrites its overrides in `homeserver.yaml` on every start, so changes to them
    reach existing data directories.
- **Your own homeserver is the remote target,** selected with `MATRIX_TEST_TARGET=remote`.
  It is configured in `matrix.NET.Tests/testsettings.json` (git-ignored; see
  `testsettings.example.json`), and its rate limits apply, so run it on demand rather than
  routinely. A full run sends three login requests: the shared login, the logout test's own
  login, and the wrong-password test. Check them against the homeserver's `rc_login` limits
  first. In Synapse (`synapse/rest/client/login.py`), with its defaults:
  - `address`: every login request from the IP address counts, failed ones included, so 3
    per run. A burst of 5, then one more about every 5.5 minutes.
  - `account`: only successful logins count, so 2 per run. Same burst and refill.
  - `failed_attempts`: 1 per run. A burst of 3, then one more about every 6 seconds.

  The `address` limit binds first. From a full allowance, a first run leaves 2 of 5, so a
  second run needs to wait about 5.5 minutes for the third. After that, each run needs
  about 17 minutes to earn its 3 back. Logins by other clients of the same account or IP
  address use the same allowance.
- **Tests that leave data behind for good run only against the local Synapse,** e.g. rooms
  they create, which cannot be deleted. They check `TestSettings.IsLocal` and are skipped
  otherwise, before sending anything.
- **A run logs in once for all tests that share a session.** `LoggedInClientFixture` is an
  xUnit assembly fixture that logs in lazily, on the first test asking for a client. A failed
  login is cached, so it is not retried by every test. Tests that test login or logout
  themselves log in separately.
- **Explicit tests** (`[Fact(Explicit = true)]`) are excluded from `dotnet test`. A test is
  explicit only if it affects the machine running it, e.g. by downloading something large,
  or if it costs too much to be worth running every time. Side effects on the test
  homeserver are not a reason, as the default target is the local, disposable one.
  - Run only them: `dotnet test --explicit only`.
  - Run everything: `dotnet test --explicit on`.
- The optional `DeviceId` setting makes the shared login reuse one device instead of
  creating a new one per run.
- **`AotSmokeTests.PublishedApp_PassesItsChecks`** publishes `matrix.NET.AotSmoke` with Native
  AOT and runs it (D29). It is explicit: it needs clang and takes about 10 seconds from a cold
  start. Its homeserver checks run only against the local Synapse.
- **`SpecConformanceTests.Endpoints_MatchTheSpec`** compares every declared endpoint with the
  spec's OpenAPI definition (D26). It downloads the 2.5 MB definition, so it is explicit. Run
  it after adding endpoints and before releases. When the spec baseline changes, update its
  URL too.

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

Stateful features live in separate types from `MatrixClient` (D21), because a type cannot
become disposable only when a feature is enabled. Whether those types also ship as
separate packages is still open.

**Why:** The SDKs researched are either thin, like the matrix-nio and mautrix-python cores,
or full, like matrix-rust-sdk. A layered design serves both audiences. It also keeps the
endpoint layer easy to test in isolation.

#### D29. Trimming and Native AOT are supported; library types never use reflection (Decided)

The library is marked `IsAotCompatible`, which turns on the trimming and AOT analysers, and
their warnings (IL2026, IL3050 and related) are errors, so reflection-based serialisation
cannot creep back in.

- **Every library type is source-generated.** `Transport.MatrixJsonContext` lists each type
  the library serialises, with the snake_case naming policy and null omission as generation
  options. The transport resolves types only through it. A type missing from it fails at
  runtime, which the endpoint's unit tests catch, so adding an endpoint means adding its
  request and response types there too.
- **Content supplied by the app has two overloads,** as `System.Net.Http.Json` does for
  `PostAsJsonAsync`:
  - a convenient one taking `TContent`, serialised by reflection with the library's naming
    policy. It is marked `[RequiresUnreferencedCode]` and `[RequiresDynamicCode]`, so trimmed
    and AOT apps get a warning pointing them to the other;
  - one taking a `JsonTypeInfo<TContent>` from the app's own source-generated context. Its
    naming policy is the app's; it should be snake_case to follow Matrix conventions.
- **Raw JSON content has its own overload taking `JsonObject`,** which needs no context and
  is AOT-safe, for apps that build or forward content as JSON.
- **A Native AOT smoke test checks what the analysers cannot.** The analysers that run while
  building the library see only its own calls. `matrix.NET.AotSmoke` is a console app published
  with Native AOT and the library as a `TrimmerRootAssembly`, so ILLink analyses all of it as
  an app's publish would, and any warning fails the publish. The native binary then runs the
  serialisation paths: session round trips offline, and against the local Synapse login,
  `whoami`, joined rooms, error responses, a real token refresh and logout.
  `AotSmokeTests.PublishedApp_PassesItsChecks` publishes and runs it; it is explicit, as it
  needs clang and compiles to native code. Run it before releases and after changing
  serialisation.

**Why:**

- **Trimming is on by default for Blazor WebAssembly and for MAUI release builds,** and Native
  AOT suits bots and command-line tools. Reflection-based serialisation of library types can
  break in all of them.
- **Now rather than later:** there were five serialisation call sites and about a dozen
  types. Every endpoint added before the switch would have made it bigger.
- Source generation also starts faster and allocates less, which benefits every app.

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
// The handler saves sessions the library refreshes (D23).
var client = new MatrixClient(session, new ClientOptions { SessionRefreshHandler = myHandler });
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
  it is still valid: a rejected token is refreshed if possible (D12). Otherwise it throws
  `MatrixUnknownTokenException` and raises the session-invalidated notification (D14). Apps that want an early check can call `whoami`
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
    forwarding layer: `AutomaticDecompression = false` on `ServerOptions` or `ClientOptions`. It is a handler-level setting, so it cannot vary per request.
    Turning it off selects a second shared client, identical except for decompression, which
    is created only when first needed. Without decompression no `Accept-Encoding` header is
    sent, so homeservers answer uncompressed and JSON parsing is unaffected.
- Caller-supplied clients keep their own settings. Their `Timeout` still applies alongside
  the transport's per-request timeout.
- **The library never changes the state of any `HttpClient`**, shared or caller-supplied:
  not `DefaultRequestHeaders`, `BaseAddress`, `Timeout` or anything else. Changing the shared
  client would affect every account and homeserver in the process, and changing a
  caller-supplied one would affect the caller's other uses of it. Everything
  request-specific, including authentication (D16), is set on the individual
  `HttpRequestMessage`.
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

Apps persist `MatrixSession` for long periods (D17, D23), so **its serialised form is a
compatibility contract**. If a later version changed it incompatibly, every user would be
logged out after upgrading. A contract only holds if the library controls the format, but
apps may serialise the record with their own `JsonSerializerOptions`, e.g. camelCase or
PascalCase names. Three layers protect it:

- **Fixed field names.** Every property has `[JsonPropertyName("user_id")]` and so on.
  Attributes take precedence over naming policies, so any serialiser options produce the
  same names.
- **A format version** (`"format_version": 1`).
  - Fields are only ever added; that does not change the version.
  - An incompatible change would raise it, and the library would keep reading older
    versions.
  - `FromJson` rejects a version newer than the library understands, e.g. after a
    downgrade, with a clear `JsonException` rather than a half-parsed session.
  - A missing version reads as 1.
  - Unknown fields are ignored, so a session written by a newer library with only added
    fields still loads.
- **`ToJson()` and `MatrixSession.FromJson(string)` helpers.** Apps can store a string
  without choosing serialiser options at all. This defines a format, not a storage
  location, so it stays within D17 and D23.

It is a `sealed record`, so a refresh can produce a new session with
`session with { AccessToken = ..., RefreshToken = ... }` without mutating the old one. Its
`ToString()` is overridden, because the generated one would print the tokens.

`MatrixServer.LoginAsync` returns a `MatrixSession` (D6). It builds the session from the
homeserver URL and the login response, converting `expires_in_ms` to an absolute time on
receipt. `LoginResponse` is internal wire format only.

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

- A refresh that fails for a transient reason, such as a network error, a 5xx response or
  rate limiting, throws that error without invalidating the session. Only a definite
  rejection means "logged out".
- **A definite rejection** is what the spec defines, 401 `M_UNKNOWN_TOKEN`, plus any other
  4xx except rate limiting and `M_USER_LOCKED`. The second part is a workaround for Synapse,
  which answers a used or expired refresh token with 403 `M_FORBIDDEN`, and it lives in
  `HomeserverQuirks` (D27). Every definite rejection is reported as
  `MatrixUnknownTokenException`, keeping the original as the inner exception, so apps handle
  every ended session alike.
- The homeserver's own `error` text is kept alongside the library's message.
- **Each request may refresh once itself.** If it is rejected again after its own refresh,
  the session is invalidated with the refresh-failure message. Retrying with a token another
  request refreshed does not use up that chance, so no request loses its refresh because
  another one refreshed first, and the outcome does not depend on whether a request was sent
  before or after another refresh.
  - A request refreshes only when the token it was rejected with is still the current one,
    so it never adds a refresh of its own beyond that one.
  - Every retry without its own refresh follows a real refresh by another request, so the
    loop ends. If the homeserver rejects every new token, the first request to be rejected
    after its own refresh invalidates the session, and every other request stops.
- **When the refresh itself is rejected, `SoftLogout` comes from that response,** not from
  the earlier rejection of the access token. The spec says so explicitly: "If the token
  refresh fails and the error response included a `soft_logout: true` property, then the
  client can treat it as a soft logout … If the error response does not include a
  `soft_logout: true` property, the client should consider the user as being logged out."
  - A rejection without the property, including Synapse's 403, therefore reports
    `SoftLogout = false`.
  - matrix-js-sdk, and so Element Web, reports the access token's rejection instead, which
    usually carries `soft_logout: true`. That keeps local data where the spec says to discard
    it. This library follows the spec; the app decides what to do with local data.
- A refresh answered with `M_USER_LOCKED` locks the client like any other request (D22).
- If the response has no new refresh token, the old one is kept, as the spec allows.
- One lock serialises refreshes. A request rejected for a token that another request has
  already replaced is retried with the new token without refreshing again. The transport
  records the token each rejected request actually sent, so this comparison is exact even
  when a refresh lands just before a request is sent. Each caller's
  cancellation token applies to its own wait. Cancelling the caller that is refreshing
  abandons that refresh, and the next waiting caller refreshes instead.
- `LogoutAsync` never refreshes: an unknown token already counts as logged out (D21).
- After a successful refresh, the new session is saved through `ISessionRefreshHandler` (D23)
  **before** the new tokens are used. Only then does the tokens-refreshed notification
  (D14) fire and the failed request get retried. Every `MatrixUnknownTokenException`
  raises the session-invalidated notification.

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

Subclasses so far:

- `MatrixUnknownTokenException : MatrixException` (`M_UNKNOWN_TOKEN`), which adds a
  `SoftLogout` property.
- `MatrixUserLockedException : MatrixException` (`M_USER_LOCKED`, D22). A locked account
  means something entirely different from an unknown token: the token remains valid and the
  state is temporary. So it gets its own type rather than sharing one.

**Why:**

- A large upfront hierarchy, as in mautrix-python, costs maintenance before anyone needs it.
- `M_UNKNOWN_TOKEN` qualifies because the app must read `soft_logout` to decide whether to
  keep local data, so that information cannot live only in the message text.

#### D14. Session change notifications (Decided)

`MatrixClient` raises one event, `SessionChanged`, whose `SessionChangedEventArgs.Kind`
(`SessionChangeKind`) tells the cases apart:

- **Tokens refreshed** (`TokensRefreshed`): the library refreshed the access token. It is
  raised after `ISessionRefreshHandler` has saved the new session (D23) and before the
  failed request is retried. It is for observers only, such as a debug view.
- **Logged out** (`LoggedOut`): the app called `LogoutAsync`. It is raised even though the
  app initiated it, because several components may care about a session ending, e.g. an
  account list or a notification service. It is a separate kind from invalidation, so a
  component that should not react to a deliberate logout can ignore it.
- **Session invalidated** (`Invalidated`): carries `SoftLogout`; the app returns to the login
  screen (D21).
- **Account locked** (`Locked`) and **account unlocked** (`Unlocked`): the app hides or
  restores its normal UI (D22).

It is a single event with a kind, not one `EventArgs` type per case. Subclasses can be
introduced later if a case needs data of its own.

Rules:

- **Raised once per change of `State`.** For example, ten requests failing while locked
  raise `Locked` once. A refresh does not change `State`; it raises `TokensRefreshed` once
  per refresh, even when several requests were waiting for it.
- **Raised after the state has changed** and before the triggering call returns or throws,
  so handlers see the new `State`.
- **Raised synchronously on the thread that completed the request,** typically a thread pool
  thread. UI apps must marshal to their UI thread.
- **Handlers must catch and handle their own exceptions.** The library swallows anything a
  handler throws. Otherwise a handler's exception would replace the library's own result or
  error, e.g. hiding a `MatrixUnknownTokenException` behind an unrelated
  `NullReferenceException` from the handler. Other handlers still run. An `async` handler is
  compiled as `async void`: anything it throws after its first `await` is beyond the
  library's reach and may crash the process, so it must wrap its whole body in
  `try`/`catch`. This is stated in the event's XML documentation.

The event is for UI and other work that does not affect correctness. **It must not be used
to persist refreshed tokens.** C# events cannot be awaited, so the library could use the new
tokens before an asynchronous save finishes. Persistence goes through `ISessionRefreshHandler`
(D23).

**Why:** Four SDKs out of four leave soft logout entirely to the app. rust-sdk's
session-change stream and save callback are the most usable model, and a single event is
the idiomatic .NET form.

#### D15. Logout invalidates `MatrixClient` but does not dispose it (Superseded by D21)

> Superseded by D21. With the shared `HttpClient` (D10), the endpoint-level `MatrixClient`
> owns nothing that needs disposing, so it no longer implements `IAsyncDisposable`. The
> analysis of what is still needed after logout carries over to D21.

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
The library decides only *when* a session must be saved, through `ISessionRefreshHandler` (D23).
It never decides *where* or *how*.

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

`GET /versions` is the first shared endpoint. The spec itself illustrates why both sides
need it: homeservers may advertise some unstable features only to authenticated users.
`MatrixServer` and `MatrixClient` both implement it through `SharedEndpoints`; only the
client's transport has a token source.

#### D21. `MatrixClient` is not disposable; logout makes it unusable (Decided)

Replaces D15.

**`MatrixClient` does not implement `IDisposable` or `IAsyncDisposable`.** It owns nothing
that needs releasing:

- the `HttpClient` is shared or caller-owned (D10);
- the session is plain data;
- the refresh lock is a `SemaphoreSlim` whose wait handle is never created, so it needs no
  disposal.

**After `LogoutAsync`, or after the session is invalidated, the client is unusable.** The
session counts as invalidated only when the homeserver answers `M_UNKNOWN_TOKEN` and the
token cannot be refreshed (D12). Only then is the token definitely dead. Other
authentication-related errors do not invalidate the session:

- **`M_USER_LOCKED`** puts the client in a locked state instead (D22).
- **`M_USER_SUSPENDED`** is an ordinary `MatrixException`. A suspended account keeps a valid
  token and may still perform many actions, such as receiving messages, leaving rooms and
  verifying devices.

After logout or invalidation:

- `State` becomes `LoggedOut` or `Invalidated`. The `Session` property throws
  `InvalidOperationException`, so the dead tokens cannot be picked up and reused.
  `MatrixSession` requires an access token, so it cannot be emptied instead.
- `UserId` and `DeviceId` stay readable, so the app knows which account ended. For example,
  it can remove the account from a list.
- Every endpoint call throws `InvalidOperationException`. This includes the endpoints shared
  with `MatrixServer` (D19): they do not silently fall back to unauthenticated requests.
  To call them without a session, use `MatrixServer`.
- A new login produces a new session and a new `MatrixClient`.

How `LogoutAsync` treats failures:

- **`M_UNKNOWN_TOKEN` counts as success.** The token is already invalid, which is what
  logging out achieves; Element does the same. This happens when the token died before the
  logout:
  - the device was logged out or deleted elsewhere, e.g. after a password change or by an
    administrator;
  - the app restored a stored session that had ended while it was offline;
  - another login reused the same device ID;
  - another process logged out the same session first;
  - a retried logout whose first attempt succeeded but whose response was lost.

  Returning 401 here is correct for the homeserver. `/logout` authenticates first, and an
  unknown token cannot be told apart from a forged one. Idempotency in HTTP (RFC 9110)
  concerns the effect, not the response: a repeated `DELETE` may answer 404. OAuth token
  revocation (RFC 7009), which the spec's OAuth 2.0 API uses for logout, chose the other way
  and answers 200 for invalid tokens: "the client cannot handle such an error in a
  reasonable way. Moreover, the purpose of the revocation request, invalidating the
  particular token, is already achieved." The client adopts that reasoning, so
  `LogoutAsync` behaves the same whichever login API the session came from. Throwing would
  also leave the user unable to log out at all, as every retry fails the same way.
- **Any other failure,** e.g. a network error or a 5xx response, throws and leaves the client
  `Active`, so the logout can be retried.

Every endpoint call goes through one wrapper in `MatrixClient`. It checks the state before
sending and handles a rejected token (D12). A shared endpoint (D19) therefore invalidates the
session just like any other endpoint.

When the client's own message replaces the homeserver's (D12), the original text stays
available as `MatrixException.ServerMessage` and is appended to the message.

**Stateful components own disposal.** The future sync loop, room and state caches, and the
crypto store are separate types (D20), and they implement `IAsyncDisposable`:

- When the session ends, they stop background work but keep local data.
- Methods to clear local data live on these components. After an explicit logout the app
  can delete the data. After a soft logout it can keep the data and reuse it when logging in
  again on the same device, as Element does.
- Apps dispose only the components they enabled.

**Why:**

- **Implementing `IDisposable` is a contract that asks every user to dispose.** Analysers
  (CA2000) warn when it is not honoured. For thin-library users (D20) this would be pure
  overhead, because their `MatrixClient` holds nothing to release.
- **Resources and disposal belong together:** only what starts background work or opens
  storage needs disposing.
- **One simple rule after logout, "the client cannot be used":** clearer than some endpoints
  working and others throwing. It also avoids the JS and rust-sdk problem of a client that
  still looks logged in.

#### D22. A locked account is a recoverable state, not an invalidated session (Decided)

Administrators can lock an account; it is reversible, unlike deactivation. While the account
is locked, the homeserver answers almost every endpoint with `401 M_USER_LOCKED` and
`soft_logout: true`. Unlike `M_UNKNOWN_TOKEN`, the spec says:

- servers SHOULD NOT invalidate the access token, so sessions survive an unlock;
- clients SHOULD keep session information, including encryption state, and hide the normal
  UI;
- clients SHOULD keep making rate-limited requests, e.g. to `/sync`, to detect when the lock
  is lifted;
- `POST /logout` and `POST /logout/all` keep working.

Design:

- **The client keeps its tokens and stays usable.** It is in a locked state, not the
  unusable state of D21.
- **Locked requests throw `MatrixUserLockedException`** (D13), which carries `SoftLogout`.
- **No token refresh is attempted.** Refresh is triggered by `M_UNKNOWN_TOKEN` only, and the
  spec says a new token cannot be obtained until the account is unlocked.
- **The first `M_USER_LOCKED` response raises the "account locked" notification** (D14).
- **The first successful request to an endpoint that requires authentication raises
  "account unlocked".** Endpoints with optional authentication, such as `/versions`, do not
  count: a homeserver may answer them without checking the token, which would report a
  false unlock. Logout does not count either, since it is allowed while locked. The future
  sync layer provides the rate-limited polling the spec asks for; the client itself does
  not throttle.
- **Logout still works while locked,** as the spec allows.

**Why:** Treating a lock as invalidation would clear a token that is still valid. The app
would then have to recreate the client from its stored session just to detect the unlock,
contradicting the spec's intent that the same session waits for it.

#### D23. Refreshed sessions are saved through an awaited hook (Decided)

```csharp
public interface ISessionRefreshHandler
{
    /// Called after the library refreshed the session, before it uses the new tokens.
    /// Persist the session durably before the returned task completes. If this throws,
    /// the new tokens are discarded and the old refresh token remains usable.
    ValueTask OnSessionRefreshedAsync(MatrixSession session, CancellationToken cancellationToken);
}

var client = new MatrixClient(session, new ClientOptions { SessionRefreshHandler = myHandler });
```

**The problem it solves.** The spec says: "The old refresh token remains valid until the new
access token or refresh token is used, at which point the old refresh token is revoked. This
ensures that if a client fails to receive or persist the new tokens, it will be able to
repeat the refresh operation." Suppose the library used the new tokens before the app had
saved them, and the app then crashed or was killed:

- the stored session would hold an expired access token and a revoked refresh token;
- the user would be logged out on the next launch.

This is common on mobile, where the OS kills background apps.

Rules:

- After a refresh, the library awaits `OnSessionRefreshedAsync` before it uses the new tokens
  (D12).
- **If saving fails, the new tokens are not used,** and the error is thrown to the caller.
  The old refresh token is still valid, so the next attempt can refresh again.
- **The handler is required when it matters.** Constructing a `MatrixClient` throws
  `ArgumentException` when automatic refresh is on, the session has a refresh token and no
  handler is set. The message names both ways out: set a handler, or turn automatic refresh
  off. Without a refresh token nothing is ever refreshed, so no handler is needed.
- **`DiscardingSessionRefreshHandler`** ships in the core for tests and one-off scripts. It
  stores nothing: it lets an app state explicitly that losing refreshed tokens on restart is
  acceptable.
- **Platform secure storage** (DPAPI, Keychain, libsecret, Android Keystore), if ever
  provided, lives in separate packages (D20). It is never in the core.

**Boundary.** The library owns protocol correctness, including the order "save, then use".
The app owns where and how sessions are stored, and what happens to stored sessions:
loading at start-up, deleting after logout, account lists. Hence:

- **Interfaces the app implements contain only methods the library calls.**
  `OnSessionRefreshedAsync` is the only such method. There is no load or delete, because the
  library never loads or deletes sessions.

**Naming.** An earlier draft used `ISessionPersister.SaveAsync`. That read as a
general-purpose "save a session" API: it suggested the library also calls it at login, and
that apps should call it themselves. The chosen names describe when the library calls the
app, not a storage operation:

- **`Handler`, not `Listener`,** because this is not an event. A listener observes something
  that has already happened, and the library neither waits for it nor depends on its result.
  Here the library waits for the handler to finish, and the outcome decides what happens
  next: the new tokens are used only if it succeeds. Events for observers remain the job of
  D14.
- **The `On…Async` prefix** follows the .NET convention for framework callbacks, e.g.
  Kestrel's `ConnectionHandler.OnConnectedAsync`, SignalR's `Hub.OnConnectedAsync` and
  Blazor's `OnInitializedAsync`.
- **`SessionRefreshed`** names the only moment it is called.

An `On…` name can sound like an optional notification. So the obligation to persist before
returning is stated in its XML documentation, and the constructor check above enforces that
a handler exists.

**Why an interface rather than a delegate:** a handler is naturally a class, e.g. one per
platform or registered in DI. It also reads more clearly in `ClientOptions`.

**Why this is not scope creep:** it comes with automatic refresh (D12). Every SDK researched
that refreshes automatically has such a hook: rust-sdk's `save_session_callback`, Element X's
`ClientSessionDelegate`, and matrix-js-sdk's `onTokenRefresh`. Refresh happens inside the
library, so the app cannot enforce the ordering from outside.

**The first login is saved by the app, not through the handler.** Login happens on
`MatrixServer` (D6), which does not know about refresh handlers. The `MatrixClient`
constructor does no I/O (D8), and it is also used to restore sessions, where saving would be
redundant. The recommended pattern, used by Element Web and Element X, is one save function
in the app: call it after login, and call it from the refresh handler.

```csharp
// The app's own save function, used in both places
async ValueTask SaveSessionAsync(MatrixSession s, CancellationToken ct) => await myStore.WriteAsync(s, ct);

var session = await server.LoginAsync(request);
await SaveSessionAsync(session, ct);    // first login: the app saves

var client = new MatrixClient(session, new ClientOptions
{
    SessionRefreshHandler = new MyRefreshHandler(SaveSessionAsync)    // refresh: the library asks
});
```

**Research (September 2026, from source).** Who saves the first session, and is the refresh
hook also used at login?

| Library | First session saved by | Hook at login | Storage interface |
|---|---|---|---|
| matrix-rust-sdk | App (`examples/persist_session` writes `session()` itself) | No. `save_session_callback` fires on refresh only; OAuth `finish_login` skips it deliberately because "it was the source of the session" | Save-only callback |
| Element X (rust-sdk FFI) | App (`sessionStore.addSession` after login) | No | Save-only delegate |
| matrix-js-sdk / Element Web | App (`persistCredentials` after login) | No. `onTokenRefresh` fires on refresh only | Save-only callback |
| matrix-nio | App (`examples/restore_login.py`) | No refresh support | None |
| Trixnity (Kotlin) | Library (repository layer in `MatrixClient.create`) | Yes, one store for both | Full store |
| MSAL.NET, Google.Apis.Auth, Slack Bolt, Supabase, Firebase | Library | Yes, one hook for both | Mostly load, save and delete; MSAL uses a whole-cache blob |

There are two camps:

- **Authentication libraries own the token lifecycle.** The app never handles tokens: it
  asks for "a token for user X", and the library loads, refreshes, saves and deletes. Their
  store interfaces therefore include load and delete, which the library itself calls.
- **Mainstream Matrix SDKs treat the session as app-visible data.** The app manages accounts,
  chooses secure storage and decides what to do after a soft logout. The library only reports
  sessions it refreshed itself.

matrix.NET follows the Matrix camp, consistent with D6 and D8. Trixnity is the one Matrix
SDK found in the other camp. No mainstream Matrix SDK offers a library-managed session layer,
so none is planned.

### 3.3 Homeserver compatibility

Homeservers in use advertise different spec versions. The spec baseline here is v1.19, while
many deployed servers advertise only up to an earlier version. These decisions were informed
by reading the source of matrix-js-sdk and Element Web, matrix-rust-sdk (with ruma),
matrix-nio and mautrix-python in October 2026.

The spec's own rules shape D24 to D26:

- A change to `Y` in `vX.Y` is "backwards compatible or 'managed' backwards compatible".
  Deprecated features may be removed one version later, so a newer version is not
  guaranteed to contain everything an older one did.
- A homeserver must implement everything in each version it advertises, deprecated parts
  included.
- Removals have happened within v1.x in the Client-Server API. Examples:
  - v1.2 moved `prev_content` into `unsigned`;
  - v1.13 removed reply fallbacks;
  - v1.14 removed the `server_name` parameter of `/join` and `/knock` in favour of `via`;
  - v1.18 removed the `score` parameter of reporting.

#### D24. Minimum supported spec version: v1.1 (Decided)

matrix.NET supports homeservers advertising v1.1 or later.

**Why:** v1.1 introduced the global `vX.Y` versioning and the `/_matrix/client/v3/...` paths
that every endpoint here uses. Earlier servers use `r0` paths, which would need a second
path scheme. Every maintained homeserver implementation supports v1.x. matrix-js-sdk and
mautrix-python's bridges use the same floor.

The endpoint layer does not refuse requests to older servers. Instead, `VersionsResponse`
offers a check that apps can run, e.g. on a login screen. It answers whether the homeserver
meets this library's minimum. A future server discovery step may fail early instead, as
Element does before login.

#### D25. `/versions` is fetched lazily and cached with an expiry the app controls (Decided)

- **Fetched on first need,** never in a constructor (D8).
- **Cached per instance.**
  - `MatrixServer` caches the unauthenticated result and `MatrixClient` the authenticated
    one. Since v1.10 the two may differ, and separate instances keep them apart without
    extra logic.
  - Concurrent callers share one request, as token refresh does (D12).
- **Expires after one day by default.** After expiry, the next call that needs the versions
  refreshes them before continuing. There is no background refresh: the endpoint layer
  starts no hidden work (D20).
- **The app can take control:**
  - `RefreshVersionsAsync()` fetches immediately, e.g. at start-up or on the app's own
    schedule.
  - The cache lifetime is configurable. `Timeout.InfiniteTimeSpan` disables automatic
    refresh entirely, so no endpoint call ever waits for `/versions`; the app refreshes
    when it chooses.

The lifetime is set through the options object of each type: `ClientOptions` on
`MatrixClient` and `ServerOptions` on `MatrixServer`.

`ServerOptions` mirrors `ClientOptions`:

```csharp
new MatrixServer(uri);                                  // defaults
new MatrixServer(uri, new ServerOptions { ... });
new MatrixServer(uri, httpClient, new ServerOptions { ... });
new MatrixServer(uri, () => httpClient, new ServerOptions { ... });
```

- Every constructor takes an optional `ServerOptions` as its last parameter, as every
  `MatrixClient` constructor does with `ClientOptions`.
- It is a sealed class with `init`-only properties, exposed as `MatrixServer.Options`.
- It replaced the `automaticDecompression` constructor parameter, which became
  `ServerOptions.AutomaticDecompression`.
- The two options types share no base type. They overlap today (decompression, and the
  `/versions` cache lifetime), but `ClientOptions` will gain session-only options such as
  the refresh handler (D23). A shared base would also make the two types assignable to the
  same parameter, which hides mistakes.

**Why:**

- **An options object on `MatrixServer` too:** adding constructor parameters one by one does
  not scale, and every new optional parameter would change the constructor signatures. This
  had to be settled before the first release, as moving options later is a breaking change.
- **Lazy fetching is what every SDK researched does.**
- **The cache needs an expiry.** matrix-js-sdk caches forever, and its code carries a TODO
  to add an expiry (issue #1020), because server upgrades go unnoticed until restart.
- **Refresh without a background task.** matrix-rust-sdk expires after one day and
  refreshes in the background, but that is background work the endpoint layer avoids.
- **The app decides when to pay the cost.** Letting apps refresh on their own schedule
  means they can keep every endpoint call equally fast.

#### D26. Feature detection by version ranges; the library knows when each feature was added (Decided)

Differences between homeserver versions fall into three kinds:

| Kind | Example | Handling |
|---|---|---|
| Endpoint missing on older servers | Room summaries (v1.15) | Send the request; never refuse it up front. The endpoint declares the version that added it, so a failure gets a clear error and apps can ask in advance (below). No generic fallback. |
| Library must choose between alternatives | Authenticated media (v1.11) vs legacy media; `via` vs `server_name` on `/join` | Detect up front from `/versions` |
| Fields added or removed in responses | New `/sync` fields in v1.16 | Nothing extra: optional fields are nullable, and unknown fields are ignored |

**One feature table covers the first two kinds.** Each entry records the version that added
it, the version that removed it if any, and its MSC's `.stable` flag if there is one.

- Every endpoint added after v1.1 gets an entry when it is implemented. The spec marks each
  with `added-in`, so recording it costs nothing extra at that point. Endpoints from v1.1 or
  earlier need no entry, as v1.1 is the minimum (D24).
- Every choice between alternatives, the second kind, gets an entry.

**The comparison rule:** a feature is supported if any advertised version satisfies
`added ≤ version < removed`, or if the `.stable` flag is `true` in `unstable_features`.
Versions are parsed into comparable numbers, `r0.x` counts as v1.0 as in ruma, and
unparseable entries are ignored.

**Clear errors for missing endpoints.** When an endpoint with an entry is answered with 404 or
405 `M_UNRECOGNIZED`, the library explains the likely cause:

- The exception stays a `MatrixException` with the same `ErrorCode` and `ServerMessage`, so
  existing handling keeps working. Only the message gains the explanation, e.g.
  `GET /rooms/{roomId}/summary requires Matrix v1.15 or later, but the homeserver advertises
  up to v1.12.`
- The explanation uses only the cached `/versions` (D25). The error path never fetches it,
  so a failure causes no extra request. Depending on the cache:
  - **cached, feature not advertised:** the message names the required and the advertised
    versions, as above;
  - **cached, feature advertised:** the message says so and points elsewhere, e.g. a reverse
    proxy that does not forward the path, since the homeserver claims support;
  - **nothing cached:** the message names the required version and says the homeserver may
    not support it.
- The wording says "likely", never "certainly": a 404 can also come from a proxy.
- A dedicated subclass carrying the required version can be added when an app needs the data
  rather than the text (D13).

**Asking in advance.** `MatrixServer` and `MatrixClient` both offer `SupportsAsync(feature)`
(D19), so an app can hide or disable a feature before the user tries it.

- It applies the comparison rule to the instance's own cache. A `MatrixClient` may see more
  than a `MatrixServer`, as homeservers may advertise some features only to logged-in users.
- It fetches `/versions` if the cache is empty or expired, as any other call needing the
  versions does (D25).
- `true` means the homeserver advertises the feature. `false` means it does not, which is not
  proof the endpoint is missing: a server can ship one before advertising it. Apps that must
  be certain call the endpoint and handle the error.
- Feature identifiers are the static members of `MatrixFeature` (below).

The library uses only stable features. Unstable, MSC-prefixed paths are never used, but
`unstable_features` is exposed to apps unchanged.

**How features and endpoints are declared.** The table must stay correct as endpoints are
added, so each endpoint is declared exactly once and checked against the spec automatically.

- **`MatrixFeature`** is a sealed class whose static members are the table, used like
  `HttpMethod.Get`:

  ```csharp
  public static readonly MatrixFeature TokenRefresh = new("Token refresh (POST /refresh)", new MatrixVersion(1, 3));
  ```

  Each member carries `Name`, `AddedIn`, `RemovedIn` and `UnstableFeatureFlag`, so apps can
  read the data, e.g. to show "requires v1.15". The constructor is private, so the set of
  features is exactly the library's table. `MatrixVersion` is a comparable
  `(Major, Minor)` value; `MatrixVersion.Minimum` is v1.1 (D24).
- **Every endpoint is an internal `Endpoint`** in `Transport.Endpoints`, holding its method,
  path, `AuthRequirement` and feature. The transport sends only `Endpoint`s, so there is no
  other way to call one. Paths use the spec's parameter names, e.g. `{roomId}`.
  `Bind(...)` fills them in, in order, percent-encoding each value, so IDs such as
  `!abc:example.org` or `#alias:example.org` cannot change the path. The transport refuses an
  endpoint whose parameters are not bound.
- **Every declaration states whether it needs a feature.** `Endpoint` has no public
  constructor, only two factories:

  ```csharp
  Endpoint.Baseline(HttpMethod.Get, "_matrix/client/v3/account/whoami", AuthRequirement.Required);
  Endpoint.Since(MatrixFeature.TokenRefresh, HttpMethod.Post, "_matrix/client/v3/refresh", AuthRequirement.None);
  ```

  A declaration cannot leave the question out, and a reviewer sees the answer in the line.
- **An explicit test checks every declaration against the spec.** `SpecConformanceTests`
  downloads the spec's merged OpenAPI definition for the baseline version
  (`https://spec.matrix.org/v1.19/client-server-api/api.json`) and, for each field of
  `Endpoints`, reports:
  - an endpoint the spec does not have, e.g. a wrong path or a removed endpoint;
  - a `Baseline` endpoint the spec marks with `x-addedInMatrixVersion` above the minimum,
    i.e. a missing feature;
  - a feature whose `AddedIn` differs from the spec;
  - an `AuthRequirement` that differs from the operation's `security`: none means `None`, an
    empty alternative means `Optional`, a user access token otherwise means `Required`.

  It needs the network, so it is explicit, like the other tests with side effects. A
  non-explicit test checks that no endpoint is declared twice.
- Adding an endpoint therefore costs one `Endpoint` line, plus one `MatrixFeature` line if it
  was added after the minimum. Forgetting the second is caught by the test.

**Why:**

- **Static members rather than an enum:** an enum would need a separate table mapping each
  value to its versions. Two lists that must agree are what goes wrong as endpoints multiply.
  A member is its own table entry, carries its data, and adding one is not a breaking change.
- **Not a capabilities object with a property per feature:** each feature would need a
  property and its own logic, and it could only answer yes or no.
- **Not attributes on endpoint methods:** they would be queried by method name through
  reflection, which is fragile and unfriendly to trimming and AOT. They also cannot describe
  the second kind, such as `via` on `/join`, which is not a method.
- **No interface or inheritance to enforce declarations.** An interface such as
  `IVersionedEndpoint` would only force a member to exist, which the factories already do at
  compile time. One class per endpoint, as in ruma, would be far heavier. Neither checks that
  the value is right; only comparing with the spec does.
- **The library knows the spec; the app decides.** Which version added an endpoint is spec
  knowledge. Without the table, every app that wants to hide an unsupported feature would
  have to look it up in the spec and maintain its own copy. What to do about a missing
  feature, such as hiding a button or offering another way, stays with the app.
- **The table records only what is needed.** ruma declares a full version history for every
  endpoint, generated from code; maintaining that by hand here would cost too much. One
  "added" version, and a "removed" version where there is one, for the endpoints added after
  the minimum, is small and is written once, when the endpoint is.
- **No refusal up front.** A server may implement an endpoint before advertising the version
  that contains it, so refusing based on versions would block features that work. The
  homeserver's own answer is the only certain one.
- **Raw `M_UNRECOGNIZED` is not a clear error.** On its own, a 404 cannot tell an old server
  from a misconfigured proxy or a library bug. The roadmap asks for clear errors for features
  the homeserver does not support.
- **No extra request on the error path.** Fetching `/versions` while handling a failure would
  add hidden I/O and latency to an already failing call (D20). The cache is usually filled by
  then anyway.
- **Version ranges rather than exact matches.**
  - matrix-js-sdk and mautrix-python check whether a version string is present. That misses
    a server advertising only `v1.12` for a v1.11 feature, and says nothing about removals.
    It mostly works because servers such as Synapse list every version they support.
  - Assuming that "newer implies everything older" is also wrong, given the removals above.
  - Ranges with an optional removal version, as ruma uses, follow the spec's rule that a
    server implements everything in each version it advertises.
- **`.stable` flags count** because servers may ship a stable endpoint before advertising
  the spec version that contains it.
- **No generic fallback for missing endpoints.** None of the SDKs researched has one, and
  matrix-js-sdk's per-call-site fallbacks carry Synapse-specific workarounds and expiry
  notes.

#### D27. Deviations from the spec are tolerated in one place, by name (Decided)

The library implements the spec. Where a homeserver deviates from it and the library must
cope, the workaround lives in `Compatibility.HomeserverQuirks`, never inline in the code that
implements the spec. Each entry:

- names the homeserver implementation;
- quotes the spec text it deviates from;
- states where the behaviour was confirmed, e.g. a source file and the date it was checked.

The calling code keeps the spec's rule as written and consults the quirk only as an extra
case, e.g. `catch (MatrixException e) when (HomeserverQuirks.IsFinalRefreshFailure(e))` after
the spec's own `M_UNKNOWN_TOKEN` handling.

An entry may be broader than the one deviation that prompted it, when that is the safer
behaviour, but it still names that deviation as its reason.

Entries so far:

| Entry | Spec | Deviation | Handling |
|---|---|---|---|
| `IsFinalRefreshFailure` | `/refresh` answers an unknown or used refresh token with 401 `M_UNKNOWN_TOKEN`; MSC2918 says "must" | Synapse answers 403 `M_FORBIDDEN` for a used or expired refresh token (`AuthHandler.refresh_token`, `synapse/handlers/auth.py`, checked October 2026) | Any 4xx except rate limiting and `M_USER_LOCKED` ends the session (D12) |

**Why:**

- **The spec stays readable in the code.** Reading the refresh logic shows what the spec
  requires; the workarounds are visibly separate and can be removed when no longer needed.
- **Each workaround can be traced and reviewed.** Naming the implementation and the source
  makes it possible to check later whether the deviation still exists, or to report it
  upstream.
- **Broad where it is safer.** For `/refresh`, matching Synapse's exact error code would
  leave the client refreshing and failing on every request whenever another homeserver
  deviates differently. Ending the session costs a new login; missing a final failure costs
  a client stuck in a loop that survives restarts.

### 3.4 Endpoint conventions

Rules that apply to every endpoint of a kind, so each new endpoint follows them without a
decision of its own.

#### D28. Transaction IDs are optional, UUIDs, and fixed for the whole call (Decided)

Endpoints that the spec makes idempotent with a `{txnId}` in the path, such as sending an
event, take an optional `string? transactionId`:

```csharp
var result = await client.SendMessageAsync(roomId, content);                   // the library creates one
var id = TransactionId.New();
var result = await client.SendMessageAsync(roomId, content, transactionId: id);  // the app's own
```

- **The library creates one when none is given,** with `TransactionId.New()`: a version 4
  UUID as 32 hexadecimal digits, as ruma does. It needs no counter, so instances and
  processes cannot collide.
- **The result reports the ID used,** so the app can match its own event when it comes back
  through sync, by the event's `unsigned.transaction_id`.
- **Apps that may need the ID after a failure create it first.** If a call throws, it returns
  no result, so an ID the library created is lost. To retry safely after, e.g., a timeout, or
  to show the message as pending before the call returns, the app passes its own ID. The XML
  documentation of every such parameter says so.
- **The ID is bound into the path before the call starts,** so the retry after a token refresh
  (D12) sends the same path. This is not what prevents duplicates there, as the rejected
  request was never executed, but it keeps one call to one ID.
- **The parameter is named `transactionId` on every endpoint,** comes after the request's own
  data, and the result carries it as `TransactionId`.

**What the ID does and does not protect against.** The homeserver treats a request as a
retransmission if the transaction ID and the path match an earlier one, and answers it with
the earlier response. The scope of an ID is one device and one endpoint since v1.7; before
v1.7 it was one access token. Synapse scopes by device since 1.90 (experimental from 1.87).
On older servers, a retry with the same ID after a token refresh is treated as a new request,
so a request that succeeded but whose response was lost would be executed twice. The library
cannot prevent that.

**Why:**

- **Optional, as in matrix-js-sdk and matrix-rust-sdk.** Most calls never need the ID, so
  requiring it would add a line to every send. Apps that do need it, for retries or local
  echo, create it first.
- **UUIDs rather than a timestamp and counter,** which the spec also recommends: a counter is
  state that two instances or processes would have to share to avoid collisions.
- **A static `TransactionId` class rather than a method on `MatrixClient`:** creating an ID
  needs nothing from a client, and an app may need one before it has a client, e.g. to queue
  a message while offline.

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
- **Source generation resets missing init-only properties.** With source-generated
  serialisation, a property declared `{ get; init; } = value` that is missing from the JSON is
  set to its type's default, not left at `value`, unlike reflection-based serialisation.
  Avoid initialisers on init-only properties of deserialised types, or handle the missing
  field explicitly, as `MatrixSession.FromJson` does for `format_version`.
- **Polymorphic discriminator clash.** A property that serialises to the discriminator name
  (`type`) must be `[JsonIgnore]`d, or System.Text.Json fails.
- **`IHttpClientFactory` shares handlers.** Never put per-account state in a
  `DelegatingHandler` (D16).
- **.NET 10 SDK and xUnit v3.** `dotnet test` refuses to run xUnit v3 through VSTest on the
  .NET 10 SDK. `src/matrix.NET/global.json` opts into Microsoft.Testing.Platform. Filters
  use MTP syntax, e.g. `dotnet test --filter-class <FullClassName>`.
- **Integration tests against the remote target touch a real account.**
  - Without `DeviceId`, each successful login creates a new device.
  - Logins are rate limited per account. Logging in once per test was enough to hit
    `M_LIMIT_EXCEEDED` after a few runs, so tests share one login, and the remote target is
    run on demand only (see Tests).
  - The wrong-password test counts as a failed login.
  - Reusing a device ID may invalidate that device's earlier tokens.
- **Login type depends on the homeserver.** Homeservers that only support OAuth 2.0 answer
  `GET /login` with 404 `M_UNRECOGNIZED`, and password login is impossible there.

### Current limitations

- The homeserver compatibility design (§3.3) is only partly implemented. Features and
  endpoints are declared (D26), but nothing uses them at runtime yet: `/versions` is not
  cached, and there is no cache lifetime option, no minimum version check, no
  `SupportsAsync` and no explanation of missing endpoints.
- Only `m.id.user` identifiers are supported.
- XML documentation is generated and shipped, so IDEs show it. Warning CS1591, for missing
  documentation, is silenced until all public members are documented.
- Rate-limit details (`retry_after_ms`) are not exposed.
- `well_known` in the login response and the deprecated `home_server` field are not
  modelled.
- There is no server discovery (`.well-known`) yet. The homeserver URL must be known.

## 5. Future plans

The feature roadmap lives in [TODO.md](../TODO.md). Near-term technical work:

1. Homeserver compatibility (D24 to D26): the `/versions` cache and its lifetime option,
   the minimum version check, `SupportsAsync` and clear errors for missing endpoints.
2. Server discovery through `.well-known/matrix/client`.

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
