# Behaviour Differences from Other SDKs

Where matrix.NET behaves differently from other Matrix SDKs, because it reads the spec in a
specific way, or makes its own choice where the spec leaves the behaviour open. It is meant
for app developers who compare matrix.NET with another SDK or with Element and notice a
difference.

It is not an assessment of the other SDKs. The spec leaves room for interpretation, some of
its wording was clarified after those SDKs were written, and their choices often reflect
sensible priorities, such as compatibility with deployed servers or never losing a user's
data. Each entry therefore also says what the other approach does well.

Each entry gives:

- the spec text, quoted, and whether it settles the question or leaves it open;
- what matrix.NET does, with the design decision in [technical.md](technical.md);
- what the other SDKs checked do, and where in their source;
- the trade-off, and what an app can do if it prefers the other behaviour.

The SDKs are matrix-rust-sdk, matrix-js-sdk (with Element Web where it decides), matrix-nio,
mautrix-python and Trixnity, read from source in October 2026. An entry only describes what
was checked: an SDK missing from an entry was not checked for that point, or does not have
the feature.

- [Server discovery looks up the hostname without the port](#server-discovery-looks-up-the-hostname-without-the-port)
- [A failed token refresh takes `soft_logout` from the refresh response](#a-failed-token-refresh-takes-soft_logout-from-the-refresh-response)
- [Feature detection uses version ranges](#feature-detection-uses-version-ranges)
- [Rate-limited requests are retried a few times, for short waits only](#rate-limited-requests-are-retried-a-few-times-for-short-waits-only)
- [Waits longer than three seconds are reported to the app](#waits-longer-than-three-seconds-are-reported-to-the-app)

## Server discovery looks up the hostname without the port

Differs from all SDKs checked that discover a homeserver from a server name.

**Spec** (Client-Server API, Server Discovery; v1.19):

> 2. Extract the hostname from the server name as described by the
>    [grammar](https://spec.matrix.org/v1.19/appendices/#server-name).
> 3. Make a GET request to `https://hostname/.well-known/matrix/client`.

The grammar is `server_name = hostname [ ":" port ]`.

- "Extract the hostname from the server name" has been in the spec since r0.4.0 (2018).
- v1.11 added "as described by the grammar" and the link to it (matrix-spec #1862, 2024,
  marked as a clarification).

**matrix.NET** reads the hostname as excluding the port:
`MatrixServer.DiscoverAsync("@alice:example.org:8448")` requests
`https://example.org/.well-known/matrix/client` (D36).

**Other SDKs** keep the port in the lookup:

| SDK | Where |
|---|---|
| matrix-rust-sdk | `client/builder/homeserver_config.rs` builds `https://{server_name}` |
| matrix-js-sdk / Element Web | `autodiscovery.ts` takes a domain; Element passes everything after the user ID's first colon |
| mautrix-python | `client/api/base.py` passes the whole server name as the URL's host |
| Trixnity | `serverDiscovery.kt` keeps the port of the URL built from the server name |

matrix-nio does not discover a homeserver from a server name. matrix-js-sdk's discovery
code dates from 2018, before the spec linked the step to the grammar.

**Trade-off:**

- Keeping the port works for deployments that serve the discovery file on the port in the
  server name.
- Dropping it works for deployments whose server name carries a federation port, such as
  8448, while the client discovery file is served over HTTPS on the default port.
- With matrix.NET, if nothing answers on port 443, the request fails and discovery reports
  `FailPrompt`. The app can then ask the user for the homeserver URL, or try
  `https://example.org:8448` itself.

## A failed token refresh takes `soft_logout` from the refresh response

Differs from some of the SDKs checked.

**Spec** (Client-Server API, Soft logout):

> If the token refresh fails and the error response included a `soft_logout: true`
> property, then the client can treat it as a soft logout … If the error response does not
> include a `soft_logout: true` property, the client should consider the user as being
> logged out.

**matrix.NET:** when `/refresh` itself is rejected, `MatrixUnknownTokenException.SoftLogout`
and `SessionChangedEventArgs.SoftLogout` come from that rejection (D12). Synapse answers a
used or expired refresh token with 403 `M_FORBIDDEN` without `soft_logout`, so the result is
`false`: the session has ended and local data may be discarded.

**Other SDKs:**

| SDK | Behaviour | Where |
|---|---|---|
| matrix-rust-sdk | Reports the earlier rejection of the access token, which usually has `soft_logout: true` | `client/futures.rs`, `broadcast_unknown_token` after a rejected refresh |
| matrix-js-sdk / Element Web | The same; js-sdk refreshes OAuth 2.0 sessions | `http-api/fetch.ts` emits `SessionLoggedOut` with the original error, whose `soft_logout` Element's `MatrixChat.tsx` reads |
| Trixnity | Reads it from the refresh response, as matrix.NET does, and also treats Synapse's 403 as final | `ClassicMatrixClientAuthProvider.kt` |

matrix-nio and mautrix-python do not refresh tokens.

**Trade-off:**

- Reporting a soft logout keeps local data, such as encryption keys, and lets the user sign
  in again on the same device. It never loses data, at the cost of keeping some the spec says
  may be discarded.
- Following the refresh response tells the app exactly what the homeserver said.
- An app that prefers to keep local data in this case can do so: the library only reports
  `SoftLogout`, and what to do with local data is always the app's decision.

## Feature detection uses version ranges

Differs from some of the SDKs checked.

**Spec** (Client-Server API, API Versions): a homeserver must implement everything in each
version it advertises, and deprecated features may be removed in a later version. So a
server is not required to list every older version, and a newer version may lack something
an older one had.

**matrix.NET:** a feature counts as supported if any advertised version satisfies
`added ≤ version < removed`, or its `.stable` flag is set (D26).

**Other SDKs:**

| SDK | Behaviour |
|---|---|
| matrix-js-sdk, mautrix-python | Check whether a specific version string is listed |
| matrix-rust-sdk (ruma) | Uses ranges, as matrix.NET does |

**Trade-off:**

- Checking for a version string is simple, and works with servers such as Synapse that list
  every version they support.
- Ranges also cover a server that lists only some versions, and represent removals, at the
  cost of recording when each feature was added and removed.
- With Synapse, both give the same answers.

## Rate-limited requests are retried a few times, for short waits only

Differs from all SDKs checked, in the combination of limits; each part on its own is shared
with some of them.

**Spec** (Client-Server API, Standard error response, `M_LIMIT_EXCEEDED`):

> Too many requests have been sent in a short period of time. Wait a while then try again.

and (Rate limiting) a rate-limited request is one that "is refused due to rate limiting".
The spec says to wait, and how long through `Retry-After`, but leaves whether a library or
an app does the waiting open.

**matrix.NET:** every request refused with 429 or `M_LIMIT_EXCEEDED` is retried up to 3
times, waiting as the homeserver asks, or 1, 2 and 4 seconds. If it asks for longer than one
minute, the error is thrown at once with `RetryAfter`. Other errors are not retried. Both
limits are options, and retrying can be turned off (D37).

**Other SDKs:**

| SDK | Behaviour | Where |
|---|---|---|
| matrix-rust-sdk | Retries every request by default, without a count limit but for at most 15 minutes in total; also retries several 5xx statuses | `http_client/native.rs`, `error.rs` (`RetryKind`), `config/request.rs` |
| matrix-js-sdk | No retry in the HTTP layer; the event-sending scheduler and some other callers retry up to 4 times | `http-api/utils.ts` (`calculateRetryBackoff`), `scheduler.ts` |
| matrix-nio | Retries every request by default, without limit, waiting `retry_after_ms` or 5 seconds | `client/async_client.py` (`_send`, `max_limit_exceeded`) |
| mautrix-python | Does not retry rate-limited requests | `api.py` (`request`) |
| Trixnity | Retries every request up to 3 times, waiting at least as long as `Retry-After` asks | `MatrixClientServerApiBaseClient.kt` (Ktor `HttpRequestRetry`) |

**Trade-off:**

- Retrying until the homeserver accepts, as matrix-rust-sdk and matrix-nio do, means a call
  succeeds whenever it eventually can, and the app never handles a rate limit itself.
- Not retrying in the HTTP layer, as matrix-js-sdk and mautrix-python do, keeps every call's
  duration predictable and leaves all timing to the app.
- matrix.NET takes short waits itself and leaves long ones to the app: a call waits for at
  most about three minutes with the defaults, and a five-minute login limit, as Synapse sets
  after repeated failed logins, reaches the app at once. Any wait that takes a call past
  three seconds is reported to the app as it starts (see the next entry).
- An app that prefers either of the other behaviours can set `MaxRateLimitRetries` to 0 and
  retry itself, or raise `MaxRateLimitRetries` and `MaxRateLimitWait`.

## Waits longer than three seconds are reported to the app

**Spec:** says nothing about how a client shows that it is waiting. It only asks clients to
wait when rate-limited, so this is a library's own choice.

**matrix.NET:** every method that sends requests, except `SyncAsync`, takes an optional
`IProgress<RequestWait>`. Once a call has taken, or a rate-limit wait would take it, longer
than three seconds, the library reports the reason, how long the call has taken, how long the
wait will last when known, and whether cancelling now would leave the outcome unknown. The
threshold is an option (D38).

**Other SDKs:**

| SDK | What the app sees while the SDK waits out a rate limit | Where |
|---|---|---|
| matrix-rust-sdk | A log entry (`tracing` warning); the send queue shows a send as pending | `http_client/native.rs`, `send_queue/mod.rs` |
| matrix-js-sdk | A debug log entry from the event scheduler; the event stays in `SENDING`, where it cannot be cancelled | `scheduler.ts`, `client.ts` (`cancelPendingEvent`) |
| matrix-nio | The `429` response, passed to the app's response callbacks before the SDK waits | `client/async_client.py` (`_send`) |
| mautrix-python | No wait: rate-limited requests are not retried | `api.py` (`request`) |
| Trixnity | A log entry (warning) | `MatrixClientServerApiBaseClient.kt` |

**Trade-off:**

- Logging, as most SDKs do, keeps every method's signature unchanged, and fits SDKs whose
  send queues already show the state of each message elsewhere.
- matrix.NET reports per call, so an app can tie each wait to the action the user took and
  say how long it will be. The cost is one more optional parameter on each method, before
  the `CancellationToken`, so a token passed by position must be named.
- An app that does not want reports passes no `IProgress`, or sets `WaitReportThreshold` to
  `Timeout.InfiniteTimeSpan`.
