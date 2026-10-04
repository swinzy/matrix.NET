> [!NOTE] 
> This document is AI generated. It is for developmental reference only and is not an official roadmap

# TODO

A rough roadmap for matrix.NET, based on the
[Matrix Client-Server API](https://spec.matrix.org/latest/client-server-api/) (v1.19).

Priorities loosely follow the spec's
[feature profiles](https://spec.matrix.org/latest/client-server-api/#feature-profiles):
MVP plus Must Have covers the modules the spec requires of a CLI client, and Should Have
adds the rest of what it requires of Web, Desktop and Mobile clients.

- **MVP**: just enough to log in, and send and receive plain text messages
- **Must Have**: a usable, spec-conforming client library
- **Should Have**: features most real-world clients expect
- **Good to Have**: common but non-essential modules
- **Optional**: niche, administrative or rarely used features

## MVP

- [x] Standard error responses (`errcode` / `error`) mapped to `MatrixException`
- [x] Discover supported login types (`GET /login`)
- [x] Password login (`POST /login`, `m.id.user`)
- [x] Authenticated requests (`Authorization: Bearer` access token)
- [x] Current account information (`GET /account/whoami`)
- [x] Logout (`POST /logout`)
- [x] Refreshing access tokens (`POST /refresh`), automatic by default
- [x] Transaction identifiers for idempotent sends
- [ ] Sync: initial and incremental (`GET /sync` with `since` and `timeout`)
- [x] List joined rooms (`GET /joined_rooms`)
- [ ] Join rooms (`POST /join/{roomIdOrAlias}`)
- [x] Leave rooms (`POST /rooms/{roomId}/leave`)
- [x] Send text messages (`PUT /rooms/{roomId}/send/m.room.message/{txnId}`, `m.text`)
- [ ] Read timeline events from sync (`m.room.message`)

## Must Have

### Core API

- [ ] Server discovery (`/.well-known/matrix/client`)
- [x] Supported spec versions (`GET /versions`)
- [ ] Compatibility with older homeservers: a minimum supported spec version, feature
      detection through `/versions`, `unstable_features` and `/capabilities`, and clear errors
      for features the homeserver does not support
- [ ] Rate limit handling (`M_LIMIT_EXCEEDED`, `retry_after_ms`, `Retry-After`)
- [ ] Capabilities negotiation (`GET /capabilities`)
- [ ] Filtering (`POST /user/{userId}/filter`, lazy-loading room members)

### Authentication

- [ ] OAuth 2.0 API (`GET /auth_metadata`, authorisation code flow); servers that only
      support OAuth 2.0 reject legacy login with `M_UNRECOGNIZED`
- [ ] Token login (`m.login.token`)
- [ ] Soft logout (`M_UNKNOWN_TOKEN` with `soft_logout`)
- [ ] Logout all devices (`POST /logout/all`)
- [ ] Third-party (`m.id.thirdparty`) and phone (`m.id.phone`) identifiers
- [x] Locked and suspended account errors (`M_USER_LOCKED`, `M_USER_SUSPENDED`)

### Events and rooms

- [ ] Room event, state event and stripped state formats
- [ ] Room history pagination (`GET /rooms/{roomId}/messages`)
- [ ] Get single events and room state (`GET /rooms/{roomId}/event/{eventId}`, `/state`)
- [ ] Send state events (`PUT /rooms/{roomId}/state/{eventType}/{stateKey}`)
- [ ] Redactions (`PUT /rooms/{roomId}/redact/{eventId}/{txnId}`)
- [ ] Room creation (`POST /createRoom`)
- [ ] Room aliases (`/directory/room/{roomAlias}`)
- [ ] Room membership: invite, kick, ban, unban, knock, forget
- [ ] Room members (`GET /rooms/{roomId}/members`, `/joined_members`)
- [ ] Power levels and permissions (`m.room.power_levels`)

### Modules required by the CLI profile

- [ ] Instant Messaging: all `msgtype`s, room name, topic and avatar
- [ ] Direct Messaging (`m.direct`)
- [ ] Presence
- [ ] Receipts
- [ ] Room History Visibility
- [ ] Room Upgrades
- [ ] Typing Notifications

## Should Have

### Modules required by the Web, Desktop and Mobile profiles

- [ ] Content Repository: upload, download, thumbnails, authenticated media
- [ ] Ignoring Users
- [ ] User and Room Mentions
- [ ] Push Notifications: pushers and push rules (required by the Mobile profile)
- [ ] Third-party Invites (required by the Mobile profile)
- [ ] Voice over IP: call signalling events, TURN server credentials

### Other modules

- [ ] Account registration (`POST /register`, User-Interactive Authentication)
- [ ] Account management: change password, deactivate account, third-party identifiers
- [ ] Profiles: display name and avatar (`/profile/{userId}`)
- [ ] Client Config (account data)
- [ ] Device Management
- [ ] End-to-End Encryption (Olm / Megolm, key management and verification)
- [ ] Send-to-Device Messaging (required for End-to-End Encryption)
- [ ] Secrets: storage and sharing (required for cross-signing and key backup)
- [ ] Event Replacements (edits)
- [ ] Event Annotations and reactions
- [ ] Rich replies
- [ ] Threading
- [ ] Read and Unread Markers
- [ ] SSO Client Login/Authentication

## Good to Have

- [ ] Spaces
- [ ] Room Tagging
- [ ] User Directory search
- [ ] Published room directory
- [ ] Room Summaries
- [ ] Server Side Search
- [ ] Event Context
- [ ] Reference Relations
- [ ] Room Previews
- [ ] Sticker Messages
- [ ] Image Packs
- [ ] Reporting Content
- [ ] Guest Access

## Optional

- [ ] Moderation Policy Lists
- [ ] Policy Servers
- [ ] Server Access Control Lists (ACLs)
- [ ] Server Notices
- [ ] Server Administration
- [ ] OpenID
- [ ] Third-party Networks
- [ ] Invite permission
- [ ] Mutual Rooms
- [ ] Recently used emoji
- [ ] Deprecated legacy login fields (`user`, `medium`, `address`)
