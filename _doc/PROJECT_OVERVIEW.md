# Messanger — project overview

Messanger is a browser-based text messenger written in C#. Users can register, find people, create group chats, exchange messages live, and return to their saved conversation history. The interface deliberately uses plain HTML and JavaScript with no CSS framework.

The application runs as one ASP.NET Core service backed by PostgreSQL. Separate .NET projects organize its business rules, use cases, and database implementation; they are deployed together, rather than as separate microservices.

This document describes implemented behavior as of October 30, 2026. Some capabilities are available through the API without corresponding controls in the minimal browser interface.

## Supported capabilities

### Accounts and sign-in

- Register with a unique, case-insensitive username and a password stored as a salted hash.
- Log in and retrieve the current user's account details.
- Stay signed in across page refreshes and browser restarts, subject to a 30-day absolute session deadline, logout, or revocation.
- Renew short-lived access tokens through a rotating refresh session.
- Log out of the current browser session, with logout coordinated across its tabs.
- Change a password after confirming the current password. A successful change invalidates all existing sessions and closes their live connections.
- Soft-delete the current account through the API.
- Allow administrators to suspend or reactivate accounts through the API. Authorization checks current account state rather than relying only on old token claims.

### Group chats and membership

- Search for active users and create a named group with selected participants.
- List joined chats and retrieve chat details, members, and message history.
- Enforce owner, moderator, and ordinary-member permissions.
- Add, remove, and rejoin participants through the membership API.
- Change member roles, update group information, transfer ownership, and leave a group through the API.
- Prevent former members and outsiders from reading history or sending messages.

### Messages and live updates

- Send text messages of up to 4,000 characters through SignalR.
- Persist messages before broadcasting them to authorized subscribers.
- Reply to an existing message in the same chat through the hub API.
- Retrieve older messages with message-ID pagination; the application API returns at most 50 messages per page.
- Receive live messages, chat-summary updates, and membership-change notifications.
- Recover saved history and current chat state after reconnecting. Live notifications are best effort; PostgreSQL holds the authoritative state.
- Maintain a monotonic read marker for each participant and calculate personalized unread counts.
- Synchronize read progress across the reader's tabs. These updates are private to that reader, rather than read receipts shown to other members.

Unread counts include every message after the participant's read marker, including their own messages. The browser acknowledges messages when the selected conversation is visible and focused.

### Minimal browser interface

The current page supports registration, login/logout, password changes, user search, group creation, chat selection, sending and receiving text, unread counts, and loading older messages. Access tokens stay in memory; persistent refresh credentials use a Secure, HttpOnly cookie. Web Locks coordinate refresh operations between tabs.

Membership administration, account administration, account deletion, and replies have backend contracts but do not yet have dedicated controls in this page.

## Security and operation

The implemented protections include HTTPS enforcement outside Development, exact browser-origin policies, CSRF protection for cookie-based session operations, active-session checks, refresh replay detection, and membership checks for chat access. HTTPS is also required for browser sign-in during development.

Configurable rate limits cover authentication, session operations, password changes, messages, and hub activity. Credential concurrency and global/per-user connection quotas limit resource use. Security logs sample repeated denials and omit credentials, tokens, and message bodies.

Operational support includes:

- Separate liveness and readiness endpoints, including database and migration checks.
- Structured JSON request logs with generated request IDs.
- Administrator-only metrics for requests, security events, hub connections, and database activity.
- Persistent, certificate-encrypted ASP.NET Core Data Protection keys and JWT signing-key rollover support.
- A bounded background worker that removes sufficiently old refresh-token records while preserving replay evidence for recent families.
- A Docker deployment with a non-root, read-only application container, private PostgreSQL networking, database TLS, file-based secrets, and separate migration/runtime credentials.
- Coordinated encrypted backups of the database, protection keys, and secrets, with guarded restore tooling. Restore invalidates old sessions to prevent historical backups from reviving revoked credentials.

The deployment package has been verified locally, including real HTTPS/WebSocket messaging, process restarts, database failures, backup integrity, and restore. A remote production deployment has not been performed.

## Technology stack

| Layer | Technology | Purpose |
|---|---|---|
| Language and runtime | C#, .NET 10 | Backend implementation and execution |
| Web server and API | ASP.NET Core 10, Kestrel, Minimal APIs | Serve the frontend and HTTP endpoints; compose middleware and dependencies |
| Real-time transport | ASP.NET Core SignalR and its JavaScript client | Authorized live chat connections and notifications; WebSockets are verified in the deployment drill |
| Browser client | HTML, vanilla JavaScript, Fetch, Web Locks | Minimal UI, API calls, session coordination, and reconnect behavior |
| Database | PostgreSQL 18 | Durable accounts, chats, memberships, messages, read markers, and refresh-session records |
| Persistence | Entity Framework Core 10, Npgsql EF Core provider 10, Npgsql | Entity mappings, PostgreSQL queries, pooled connections, transactions, and migrations |
| Authentication | JWT Bearer authentication, RSA signing, rotating refresh JWTs | Short-lived API access and persistent browser sessions |
| Passwords | ASP.NET Core Identity `PasswordHasher<User>` | Salted password hashing and verification; the app does not use the full Identity UI |
| Cookie/key protection | ASP.NET Core antiforgery and Data Protection, X.509 certificates | CSRF checks and persistent encrypted key storage |
| API description | ASP.NET Core OpenAPI | Development-only API metadata, including authentication and error contracts |
| Observability | .NET logging and metrics, Npgsql instrumentation | Structured logs, request correlation, bounded metrics, and database measurements |
| Packaging | Docker, Docker Compose, official .NET and PostgreSQL images | Application/database deployment and isolated verification stacks |
| Recovery tooling | Python, OpenSSL, PostgreSQL dump/restore tools | Coordinated backup, authenticated encryption, guarded restore, and failure drills |
| Verification and CI | Custom C# integration runner, ASP.NET Core test host, real PostgreSQL, Node.js tests, GitHub Actions workflow | Check repositories, HTTP/hub behavior, browser logic, and deployment/recovery |

## Code architecture

The solution follows a small Clean Architecture-style structure, with related files grouped by functionality inside each layer.

| Project or directory | Responsibility |
|---|---|
| `Messanger/` | ASP.NET Core host, HTTP contracts/endpoints, authentication policies, SignalR, security controls, operations, and `wwwroot` frontend |
| `src/Messanger.Domain/` | Core entities and domain rules, without database or web-framework dependencies |
| `src/Messanger.Application/` | Account, session, chat, membership, read, and message use cases; repository and adapter contracts |
| `src/Messanger.Infrastructure/` | PostgreSQL repository implementations, EF mappings/migrations, password hashing, and JWT adapters |
| `tests/Messanger.IntegrationTests/` | Executable integration scenarios and shared test support |
| `tests/browser/` | JavaScript session, read-state, and password-flow tests |
| `deploy/` | Deployment configuration, secret provisioning, backup/restore tools, and release smoke checks |

Application depends on Domain. Infrastructure implements Application contracts and references Domain. The host composes these layers through dependency injection. Database contexts are scoped; the pooled PostgreSQL data source is shared. Transactions protect operations that must succeed or fail together, such as creating a group or rotating a refresh token.

## Current scope and limitations

The completed scope is authenticated group text messaging and its P1 session, security, and operational support. The latest local verification passed 100 integration checks and 19 JavaScript checks, plus the deployment/recovery drill. The GitHub Actions workflow is configured; local verification does not establish that it has run remotely.

The application currently targets one running application instance. Connection bookkeeping, live revocation, and rate budgets are local to that process. Multiple instances require shared coordination and SignalR distribution.

Media attachments, photo/video uploads and downloads, avatars, profile/settings editing, Russian/English localization, and media cleanup/outbox workers remain P2 work. SeaweedFS/S3 storage appears in the design but is not part of the running stack. Existing media/profile entity fields do not make those user-facing features implemented. End-to-end message encryption is not implemented; HTTPS and database TLS protect transport.

For setup and endpoint details, see [README](../README.md). For deployment, key rotation, monitoring, and recovery, see the [deployment runbook](../deploy/README.md). For completed and remaining work, see the [implementation table](../_docs/IMPLEMENTATION_STATUS.md).
