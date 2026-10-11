# Behaviour Differences from Other SDKs

Where matrix.NET behaves differently from other Matrix SDKs because it follows a specific
reading of the spec. It is meant for app developers who compare matrix.NET with another SDK
or with Element and notice a difference.

It is not an assessment of the other SDKs. The spec leaves room for interpretation, some of
its wording was clarified after those SDKs were written, and their choices often reflect
sensible priorities, such as compatibility with deployed servers or never losing a user's
data. Each entry therefore also says what the other approach does well.

Each entry gives:

- the spec text, quoted;
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
