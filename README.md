# Messanger

A minimal text messenger: ASP.NET Core 10, PostgreSQL, SignalR, and a plain HTML/JavaScript frontend, organized into separate projects with
one type per file:

| Project                            | Responsibility                                                            | References           |
|------------------------------------|---------------------------------------------------------------------------|----------------------|
| `Messanger`                        | HTTP host, authentication middleware, endpoints, and dependency injection | Infrastructure       |
| `src/Messanger.Domain`             | Entities and domain rules                                                 | None                 |
| `src/Messanger.Application`        | Repository contracts and authentication use cases                         | Domain               |
| `src/Messanger.Infrastructure`     | EF Core, PostgreSQL repositories, migrations                              | Application, Domain  |
| `tests/Messanger.IntegrationTests` | Executable repository and HTTP integration checks                         | Host, Infrastructure |

Project references enforce dependency direction at compile time. Domain and Application do not reference EF Core or
Npgsql, so business logic can be tested independently of PostgreSQL. The host wires interfaces to Infrastructure
implementations through dependency injection. These projects still form one deployed application; the tests run
separately.

This is a small Clean Architecture-style layout. Compared with folders alone, projects provide stronger boundaries and
keep persistence dependencies out of the core. The tradeoff is more project files and reference management.

For similar separation,
see [Microsoft's architecture guide](https://learn.microsoft.com/en-us/dotnet/architecture/modern-web-apps-azure/common-web-application-architectures), [Ardalis CleanArchitecture](https://github.com/ardalis/CleanArchitecture),
and [Jason Taylor's CleanArchitecture](https://github.com/jasontaylordev/CleanArchitecture).

## Code organization

The project references still enforce Clean Architecture dependency direction. Related files are grouped only where there
are enough responsibilities to make the grouping useful:

```text
Messanger/
  Authentication/    HTTP endpoints, request model, Bearer validation, account access policy
  Accounts/          Administrator status changes and self-deletion endpoints
  Chats/             User search, group creation, details, and history endpoints
  Realtime/          Authorized SignalR hub and delivery
  Http/              Error responses, exception handling, transport/origin policies
  wwwroot/           Plain HTML/JavaScript and the vendored SignalR browser client
  Program.cs         Host composition
src/
  Messanger.Domain/   Entities kept flat: this small project has no competing categories
  Messanger.Application/
    Authentication/  Registration/login use cases, token/password ports, auth results
    Accounts/        Account status/deletion workflows and safe account DTOs
    Chats/           Group workflows, membership access, and chat DTOs
    Messages/        Text send/history use cases and DTOs
    Persistence/     Repository interfaces and unit-of-work contract
    UserResponse.cs  Shared safe user DTO
    UseCaseException.cs
  Messanger.Infrastructure/
    Authentication/  Identity password hashing, JWT signing/key/options, adapter registration
    Persistence/
      Repositories/  PostgreSQL repository implementations
      Configurations/ EF entity mappings
      Migrations/    Versioned schema and model snapshot
      MessangerDbContext.cs
      DesignTimeDbContextFactory.cs
      ModelBuilderExtensions.cs
    DependencyInjection.cs
tests/Messanger.IntegrationTests/
   Api/              HTTP authentication and security scenarios
   Repositories/     PostgreSQL repository scenarios
   Support/          Shared hosts, database setup, assertions, and token fixtures
   Program.cs        Test runner
```

Application depends on abstractions; Infrastructure implements them. The HTTP host owns HTTP contracts, validation
middleware, and access policies, and composes Infrastructure adapters through DI. Shared DTO/error types and composition
entry points stay at their project roots. No new projects or empty feature folders are introduced. Existing Domain
entity identities and migration history are preserved.

## Local database

```bash
cp .env.example .env
docker compose up -d --wait
export ConnectionStrings__Postgres='Host=localhost;Port=5432;Database=messanger;Username=messanger;Password=messanger_local_only;Maximum Pool Size=20;Timeout=10;Command Timeout=30'
dotnet tool restore
dotnet ef database update --project src/Messanger.Infrastructure
```

If you change `.env`, update the connection string to match. Compose reads `.env`; .NET reads the exported environment
variable. The Compose project is explicitly named `messanger-local` to avoid collisions with older stacks in a folder named Messanger. Its PostgreSQL 18 data is stored in the separate `messanger-local_postgres_data` volume. The database binds only to localhost and stores data in a named volume. PostgreSQL 18 uses the
`/var/lib/postgresql` volume path. `docker compose down` stops the database and preserves its data.

Schema changes are applied explicitly with EF migrations, never on application startup. Use deployment-managed
credentials and database TLS for production.

## Local configuration across terminals and IDEs

Docker Compose loads `.env`; .NET does not. An exported `ConnectionStrings__Postgres` only exists in that terminal and its child processes. For persistent local development configuration, use .NET user secrets:

```bash
dotnet user-secrets set "ConnectionStrings:Postgres" 'Host=localhost;Port=5432;Database=messanger;Username=messanger;Password=messanger_local_only;Maximum Pool Size=20;Timeout=10;Command Timeout=30' --project Messanger
mkdir -p .local
# Generate this key once, keeping an existing key on later runs.
(umask 077; test -f .local/jwt-signing.pem || openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 -out .local/jwt-signing.pem)
dotnet user-secrets set "Jwt:PrivateKeyPath" "$(pwd)/.local/jwt-signing.pem" --project Messanger
dotnet dev-certs https --trust
dotnet run --project Messanger --launch-profile Local
```

Match the connection string to your `.env` database, username, password, and mapped port. Start PostgreSQL with `docker compose up -d --wait` and apply the explicit EF migration command above first. Select the **Local** launch profile when running from an IDE; it sets Development mode, HTTPS port 7100, and the matching browser origin. Open `https://localhost:7100/`.

User secrets live outside Git and load only in Development; `.local/` is ignored and contains only the local signing key. Existing environment variables override user secrets, so remove stale overrides if you change configuration. Production still requires explicit connection/signing configuration and HTTPS. Persistent sign-in requires HTTPS in Development too; HTTP authentication requests are rejected.

## Run the app

Generate a local signing key outside the repository, and configure the development HTTPS endpoint:

```bash
export Jwt__PrivateKeyPath="$(mktemp /tmp/messanger-signing-XXXXXX.pem)"
openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 -out "$Jwt__PrivateKeyPath"
chmod 600 "$Jwt__PrivateKeyPath"
dotnet dev-certs https
export ASPNETCORE_ENVIRONMENT=Development
export ASPNETCORE_URLS=https://localhost:7100
export HttpSecurity__AllowedOrigins__0=https://localhost:7100
dotnet run --project Messanger --no-launch-profile
```

Keep the exported `ConnectionStrings__Postgres` from the database setup. .NET does not automatically load `.env`.
Production requires a managed private-key file, HTTPS, exact allowed origins, and `AllowedHosts` set to the deployed
host name. `Jwt__Issuer` and `Jwt__Audience` override the development identifiers in `appsettings.json`. Missing/invalid
signing configuration fails startup. Do not generate a new production key on every restart.

`HttpSecurity__TrustedProxies__0` can add an explicit trusted reverse-proxy IP for forwarded scheme/address headers.
Built-in loopback proxy trust remains; arbitrary remote forwarders are not trusted. Empty `AllowedOrigins` accepts
requests without an Origin header, but denies browser requests carrying one. CORS allows GET/POST/PATCH/DELETE and the authorization/content/SignalR client headers, without cookies. Production rejects unencrypted requests; Development permits local HTTP for
tooling. HTTP bodies and SignalR messages are limited to 16 KiB. Tokens in query strings are accepted only for browser WebSocket/SSE requests to `/hubs/chat`; keep proxy logs free of token-bearing query strings.

| Endpoint             | Access                         | Result                                                                                                                                                       |
|----------------------|--------------------------------|--------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `POST /registration` | Public                         | Validates username/password and atomically creates an active user plus profile; returns 201, or 409 for a duplicate username.                                |
| `POST /auth/login` | HTTPS + CSRF proof | Returns an access token and safe user fields; sets the persistent refresh cookie. Invalid credentials/inactive accounts return generic 401. |
| `GET /api/users/me`  | Active account + access Bearer | Returns the signed-in user's ID, username, role, and language.                                                                                               |

Registration/login bodies are `{ "username": "alice", "password": "a long passphrase" }`. Usernames contain 3–20 ASCII
letters/digits/underscores; uniqueness/login lookup ignore case. Passwords contain 10–256 characters, allow spaces, and
cannot be all whitespace. Registration creates the default Russian-language profile/account settings. Passwords use
ASP.NET Identity hashes; hashes are never returned to clients.

For a self-signed local development certificate only, `curl -k` can exercise the endpoints:

```bash
curl -k https://localhost:7100/registration -H 'Content-Type: application/json' -d '{"username":"alice","password":"a long passphrase"}'
# Keep this private cookie jar outside the repository.
umask 077
CSRF_TOKEN=$(curl -ks -c /tmp/messanger.cookies https://localhost:7100/auth/csrf | python3 -c 'import json,sys; print(json.load(sys.stdin)["token"])')
curl -k -b /tmp/messanger.cookies -c /tmp/messanger.cookies https://localhost:7100/auth/login -H "X-CSRF-Token: $CSRF_TOKEN" -H 'Content-Type: application/json' -d '{"username":"alice","password":"a long passphrase"}'
# Copy accessToken from the login response into ACCESS_TOKEN in the same terminal.
curl -k https://localhost:7100/api/users/me -H "Authorization: Bearer $ACCESS_TOKEN"
```

Access tokens expire after 15 minutes, with a 60-second validation clock skew. Validation checks RS256, signature,
issuer, audience, expiry/not-before, `at+jwt` header type, `token_use=access`, and required identity claims. Protected
requests also check the current account status in PostgreSQL. Responses use `Cache-Control: no-store`; errors use
`{ "error": { "code", "message", "requestId" } }` with actual JSON values.

## Use the plain frontend

Open `https://localhost:7100/` after starting the app above. There is no CSS, frontend framework, build step, or runtime CDN dependency. The official SignalR JavaScript client and its license are checked into `wwwroot/lib/signalr`.

1. Open two separate browser profiles, or a normal and a private window. Register/log in as a different user in each. Tabs within one browser profile share the same persistent session.
2. In the first window, search for the second user's username, select them, enter a group name, and create the group.
3. The second user sees the group appear live. Open it in both tabs and send text messages.
4. The latest 50 messages load in chronological order. Use **Load older messages** for earlier pages.
5. Temporary disconnects trigger reconnect attempts and reload the latest history. Reloading or reopening the browser restores the session from the refresh cookie until logout, revocation, or the original 30-day deadline.

The UI renders all user-provided text with `textContent`. It merges live messages and history by message ID, so the sender's acknowledgement and broadcast do not create duplicates. Access tokens renew automatically before expiry and remain in memory. **Log out** revokes this browser's refresh family, clears its cookie, and closes its live connections across tabs. Unsent text is kept in memory with an explanation if the session expires.

Use the HTTPS Local launch profile and trust the development certificate in your browser. All cookie session endpoints require HTTPS, including in Development. The configured allowed origin must exactly match the URL used by the browser, including its port.

## Chat API and live messages

All chat/search endpoints and hub connections require an active account and an access token.

| Endpoint | Behavior |
| --- | --- |
| `GET /api/users/search?query=alice` | Up to 10 case-insensitive username matches, excluding caller and inactive accounts. |
| `POST /api/chats/create` | Body `{ "chatName": "Group", "description": null, "userIds": [2] }`; atomically inserts a group, the caller as OWNER, and each unique selected participant as MEMBER. Requires 1–50 other active users. |
| `GET /api/chats` | Caller’s active groups, members, and latest message summaries. |
| `GET /api/chats/{chatId}` | Membership-protected group details plus the latest 50 messages. |
| `GET /api/chats/{chatId}/messages?beforeMessageId=100&limit=50` | Membership-protected chronological history; maximum page size 50, exclusive ID cursor. |
| `/hubs/chat` | SignalR `JoinChat(chatId)`, `LeaveChat(chatId)`, `SendMessage(chatId,text)`, `SendReply(chatId,text,replyToMessageId)`, and `MarkRead(chatId,messageId)`. |

Text messages contain 1–4000 characters. The sender ID comes from the access token; clients cannot choose it. Message writes check/lock the active sender and participant rows, validate that replies belong to the same chat, and commit before notification. A failed broadcast does not delete the saved message. Clients receive `ReceiveMessage` for joined chats, `ChatUpdated` for their active chats, and `ChatCreated` when added to a new group. Delivery rechecks active membership/account status, so a stale subscription does not grant access after leaving or suspension.

Connection subscription bookkeeping currently supports a single application instance. Multiple instances need shared routing/backplane coordination. Attachments, public-release rate limits, and deployment hardening remain separate tasks. The host does not mutate the schema on startup.

## Group membership contracts

Development exposes the generated OpenAPI document at `/openapi/v1.json`; Production does not expose it.
The document describes request/response DTOs, bearer authentication, and the shared error envelope.
JSON serialization has a bounded depth of 32 so nested OpenAPI schemas can be generated; HTTP bodies remain limited to 16 KiB.

All routes below require an active account. Group IDs and user IDs must be positive integers.

| Route | JSON body | Permission and result |
| --- | --- | --- |
| `POST /api/chats/{chatId}/members` | `{ "userId": 2 }` | OWNER adds an active account; 200 chat summary. |
| `DELETE /api/chats/{chatId}/members/{userId}` | None | OWNER removes others; MODERATOR removes ordinary MEMBERs only; 204. |
| `POST /api/chats/{chatId}/leave` | None | Non-owner leaves; repeated leave succeeds with 204. OWNER must transfer first. |
| `POST /api/chats/{chatId}/ownership` | `{ "userId": 2 }` | OWNER transfers to another active participant; 200 summary. Previous owner becomes MEMBER. |
| `PATCH /api/chats/{chatId}/members/{userId}/role` | `{ "role": "MODERATOR" }` | OWNER assigns MEMBER or MODERATOR; 200 summary. OWNER changes use the transfer route. |
| `PATCH /api/chats/{chatId}` | `{ "chatName": "Friends", "description": null }` | OWNER replaces group name/description; 200 summary. Name trims to 1–120 characters; description at most 2000. |

Groups permit at most 51 active participants including the owner. Adding an existing active member returns 409.
Only the owner can re-add a departed user; the unique membership row is reused and the role resets to MEMBER.
Removing an already departed participant is idempotent when the caller still has permission. Use the leave route for yourself.
Ownership, roles, and the capacity limit are checked within serialized PostgreSQL transactions.

Malformed JSON/type binding returns 400, missing/invalid business values 422, insufficient access 403,
missing participant 404, and state conflicts 409. Examples of conflict codes are `owner_transfer_required`,
`member_already_active`, and `membership_conflict`. Errors retain the same JSON shape:

```json
{ "error": { "code": "owner_transfer_required", "message": "Transfer ownership before leaving the chat.", "requestId": "request identifier" } }
```

SignalR business failures use `HubException` messages in the form `code: message`, including `validation_failed` and `forbidden`.
After committed membership/detail changes, current members receive `ChatMembershipChanged(chatId)` and should reload chat details/list.
Removed/departed users receive `ChatAccessRevoked(chatId)` on each joined connection and are unsubscribed immediately.
The browser clears the revoked conversation. Re-added users must invoke `JoinChat` again; rejoining a group never silently
restores an old live subscription. The membership version fence rejects joins racing with removal.

Events are best effort after commit; reload the chat list/history after reconnecting. Subscription state and revocation
coordination currently support one application instance. The API supports group administration; the plain frontend
reacts to changes but does not yet provide membership/role management controls.

## Account lifecycle

Apply the new migration before running this version:

```bash
dotnet ef database update --project src/Messanger.Infrastructure
```

The migration adds `users.session_version` with default zero. Access JWTs now require a matching `session_version` claim;
previously issued access tokens require a new login. Status changes increment this version and revoke every unrevoked
refresh record in the same transaction. Reactivation permits a fresh login and never revives earlier tokens.
Persistent refresh-cookie sign-in is implemented as described below.

| Route | Body | Behavior |
| --- | --- | --- |
| `PATCH /api/admin/users/{userId}/status` | `{ "accountStatus": "SUSPENDED" }` or `{ "accountStatus": "ACTIVE" }` | Current active database ADMIN required; 200 safe account DTO. Cannot target yourself or restore a DELETED account. Repeating the current status is idempotent. |
| `DELETE /api/users/me` | None | 204 soft deletion. Requires transferring owned groups first (409 `owner_transfer_required`). The last active administrator cannot delete their account (409 `account_conflict`). |

Registration cannot assign ADMIN. Provision the initial administrator through a controlled database maintenance step;
there is no public administrator bootstrap or role-assignment endpoint. Authorization checks the current stored role,
so an old ADMIN token cannot authorize status changes after demotion. Manual role changes must also increment
`session_version`, revoke that user's refresh records, and restart/close affected live connections; the public API does
not currently expose role changes.

Suspension/deletion closes affected SignalR connections, including idle connections and multiple tabs. The browser
checks its session after closure and clears revoked credentials. Each hub invocation also checks the current account
status and session version. Version-aware closure preserves newer connections if an older status-change notification
arrives late. Live closure coordination supports one application instance; multiple instances need coordinated revocation.

Deletion marks memberships departed, frees group capacity, and preserves messages for other participants. History
returns `deleted_account` / `Deleted account` instead of the former username/display name. Identity/profile records
remain stored for referential integrity, and the username remains reserved. This is soft deletion, not a data-erasure workflow.
A suspended owner retains ownership and can resume managing the group after an administrator reactivates their account.
The plain frontend has a password-change form; administrator status and account deletion still have no UI; these routes are available through the API.

## Persistent browser sessions

The frontend and session endpoints run on the same HTTPS origin. Cross-origin credentialed sessions are deliberately
unsupported; CORS for bearer APIs stays without credentials. The Local profile uses `https://localhost:7100/`.
Run `dotnet dev-certs https --trust` and complete your browser's local certificate setup yourself if prompted.

| Endpoint | Contract |
| --- | --- |
| `GET /auth/csrf` | Anonymous HTTPS bootstrap. Sets the HttpOnly/Secure `__Host-MessangerCsrf` cookie and returns `{ "token": "..." }`; no-store. |
| `POST /auth/login` | Credentials JSON plus `X-CSRF-Token`; returns access/user JSON and sets the refresh cookie. A successful login replaces/revokes the browser's previous family. |
| `POST /auth/refresh` | Refresh cookie plus `X-CSRF-Token`; atomically consumes the old token, sets its successor cookie, and returns access/user JSON. |
| `POST /auth/logout` | Refresh cookie plus `X-CSRF-Token`; idempotently revokes that family, clears its cookie, and closes that family's live connections. Other browser/device families remain active. |

Bootstrap and session requests omit Authorization headers. Supply the CSRF cookie and returned request token together.
Session requests reject cross-site fetches and Origin headers differing from the exact HTTPS request origin, in addition
to the application's origin allowlist. Missing/invalid CSRF proof returns 403 `csrf_failed`; invalid/expired/revoked refresh
credentials return 401 `session_expired`. CSRF failures do not rotate or clear a valid session. Raw refresh tokens are
never accepted in JSON/Bearer inputs and never appear in JSON responses or browser storage.

The host-only `__Secure-MessangerRefresh` cookie has `HttpOnly; Secure; SameSite=Strict; Path=/auth` and persistent
Expires/Max-Age capped at 30 days from login. Refresh JWTs use RS256, `rt+jwt`, `token_use=refresh`, and the access
audience suffixed with `:refresh`. Only SHA256 hashes of random token identifiers are stored. All successors retain the
original deadline, and access expiry is capped at that deadline too. No additional schema migration is needed for rotation.
Access tokens carry `sid`; HTTP authorization and each hub invocation check that the family is still active in PostgreSQL.
Earlier access tokens without `sid` require a fresh login after this update.

Rotation/logout serialize on the user row, using the same lock order as account status changes. A consumed token replay
revokes its whole family, including a successor already returned to another caller. There is no replay grace window:
concurrent raw HTTP refresh requests using the same cookie fail closed (one rotation, followed by family revocation).
The frontend prevents normal tab collisions with a same-origin Web Lock covering CSRF bootstrap and cookie mutation,
plus one in-flight renewal per tab. It requires a browser with Web Locks support. BroadcastChannel propagates explicit
logout/account changes without storing tokens; each tab refreshes its own in-memory access token from the shared cookie.
A lost rotation response requires a fresh login if the old cookie is subsequently replayed.

Five JavaScript tests cover restoration/renewal, draft retention, cross-tab logout, account-change races, and tab locking.
The real-browser reload/new-tab/logout flow was manually confirmed by the user over HTTPS. Server tests model reopening
with a fresh client sharing only cookies. Password changes revoke every session as described below; expired refresh-record retention is handled by the worker described below.
Production must persist/protect ASP.NET Core Data Protection keys for CSRF cookies across restarts/instances and
coordinate live revocation across application instances; live connection bookkeeping still supports one instance.

## Read markers and unread counts

`GET /api/chats` and `GET /api/chats/{chatId}` include `unreadCount` and `lastReadMessageId` for the caller.
Fetching a list, details, or history does not mark anything read. The existing read-marker table is reused; no migration is needed.

Invoke SignalR `MarkRead(chatId,messageId)` after displaying messages. It returns:

```json
{ "chatId": 7, "lastReadMessageId": 123, "unreadCount": 2 }
```

The authenticated user must be active and currently belong to the chat. Both IDs must be positive, and the message must
exist in that chat. Invalid/missing/cross-chat message IDs yield `validation_failed`; insufficient access yields `forbidden`.
A read marker advances only to a greater message ID. Repeated/older acknowledgements preserve both its value and update
time. PostgreSQL locks serialize competing acknowledgements and membership changes, with one marker per participant.
A user can acknowledge their own membership without joining the live-delivery group first.

Unread count includes **all** messages whose ID is greater than the participant's marker, including their own messages.
Without a marker, every message counts as unread. Rejoining preserves the original participant row and marker, so
messages sent while away count by the same rule. One user's read action never changes another participant's marker.

`ChatUpdated` now carries a **personalized summary**, replacing its earlier raw-message payload:

```json
{
  "chatId": 7,
  "lastMessage": { "id": 125, "chatId": 7, "text": "Latest message" },
  "messageType": "TEXT",
  "unreadCount": 2,
  "lastReadMessageId": 123
}
```

`lastMessage` is the existing full message DTO (abbreviated above), or null for an empty chat; `messageType` is currently
TEXT, or null. New messages notify every active participant's connections with their own count. A read acknowledgement
notifies only that reader's connections, including other tabs; it does not publish read receipts to other participants.
`ReceiveMessage` retains its existing message DTO. Clients should reload the chat list/history on reconnect: notifications
are best effort, and their arrival order is not a state version.

The plain UI adds an unread count to chat buttons and acknowledges the greatest message ID rendered in the selected,
visible, focused conversation. Background tabs/windows do not mark new messages read; visibility/focus changes retry.
Older pages cannot move the marker backward, and only one acknowledgement is in flight per tab. Chat switching or
logout invalidates stale UI responses. The UI reloads authoritative counts instead of applying possibly stale events.
Eight additional JavaScript tests exercise these read/visibility/race behaviors (13 JavaScript tests in total).

## Change password

Use **Change password** in the signed-in UI, entering the current password, a new password, and its confirmation.
Every attempt clears those form fields. A successful change signs out all existing sessions, preserves unsent message
text in the current tab, and asks for a fresh login with the new password. A wrong current password leaves the existing
session active. Tabs sharing this browser receive a logout notification.

The API is `POST /api/users/me/password` over HTTPS with the caller's explicit access Bearer token:

```json
{ "currentPassword": "existing passphrase", "newPassword": "replacement passphrase" }
```

The server derives the user from the token; request bodies cannot select another account. Password policy is shared
with registration: 10–256 characters, spaces allowed, and not all whitespace. Values are never trimmed. A valid current
password is required even when an access token is valid. Passwords and hashes are never returned.

Success returns **204**, atomically replacing the salted Identity hash, incrementing `session_version`, and revoking all
existing refresh families. Existing access tokens stop authorizing requests and the user's live connections close;
a fresh login starts a new family. The response expires the browser's refresh cookie with its original scope.
Other browsers/devices may retain an unusable cookie until their next refresh, which clears it.

This route authenticates with explicit Bearer credentials; refresh cookies alone never authorize it. The frontend uses
`credentials: same-origin` to accept the expired-cookie response and shares the refresh/logout Web Lock to serialize
cookie mutations. Auth-cookie lifecycle endpoints keep their existing CSRF protection.

A wrong current password returns 401 `invalid_credentials`; missing authentication returns 401 `unauthorized`.
Inactive/revoked sessions return 403; invalid password input returns 422; a verified account snapshot that changes before
commit returns 409 `account_conflict`. Failed validation/confirmation does not revoke families or clear a valid cookie.
The conditional database update prevents two requests verified against the same old hash/version from both winning.
Login/refresh use the same user-row locking order, so an old password/session version cannot survive a concurrent change.

No migration is needed. Six integration scenarios cover hashing, all-session revocation, cookie/live closure, targeting,
validation/access, concurrency, and inactive accounts. Six JavaScript tests cover the form, lock/Bearer flow, failed
confirmation, stale-account responses, and late renewal errors (19 JavaScript tests in total).

## Expired refresh-token cleanup

A background worker runs once at startup and then waits 24 hours after each pass. It purges refresh-token records
whose expiry is **at least 90 days old**, using an inclusive UTC cutoff (`expires_at <= now - 90 days`). Retention is
fixed in application code. A family with any record newer than that cutoff is preserved in full, including older
ancestors, so active/recent families retain their replay evidence. Revocation alone never makes a record eligible.

Apply the additive index migration before starting the updated app:

```bash
dotnet ef database update --project src/Messanger.Infrastructure
```

`RefreshTokenRetentionIndexes` adds indexes on `(expires_at,id)` and `(family_id,expires_at)` for the bounded candidate
scan and family check. It does not delete data or change token/session lifetimes. Migrations remain an explicit step.

The defaults in `appsettings.json` are:

```json
"RefreshTokenCleanup": {
  "Enabled": true,
  "Interval": "1.00:00:00",
  "BatchSize": 1000,
  "MaximumBatchesPerRun": 10
}
```

The interval supports one minute through seven days; batch size supports 1–10000 rows and a pass supports 1–100 batches.
Invalid settings fail startup. Environment overrides use names such as `RefreshTokenCleanup__BatchSize`.
`Enabled=false` disables automatic passes. The interval is measured after the preceding pass, rather than at a fixed
wall-clock time.

Each batch is one atomic PostgreSQL delete, committed directly without loading entities. `FOR UPDATE SKIP LOCKED`
allows independent cleaners to process disjoint batches and skip rows another operation has locked. Runs stop at the
batch budget; remaining/skipped records wait for a later pass. A full final batch reports `BatchLimitReached`, which
is a scheduling hint, not an exact backlog count. Every pass uses and disposes a fresh DI/database scope.

Database failures are logged and retried at the next configured interval. Shutdown cancels an in-flight command or
delay; completed batches remain committed and a later pass resumes the backlog. Logs contain deletion counts/cutoff
and failures, never raw token values. The worker deletes only refresh records; users, chats, messages, and active
sessions are retained. There is no public cleanup endpoint.

Seven database scenarios cover retention boundaries, family evidence, bounded resumption, concurrent cleaners,
locked-row skipping, active-session/replay behavior, and cancellation. Three controlled worker tests cover scheduled
recovery, fresh scope disposal, batch budgets, and shutdown without waiting for real daily timers. Normal API tests
disable automatic passes and invoke retention explicitly in isolated schemas.

## Repository usage

Register `services.AddPostgresRepositories(connectionString)`, then resolve repositories and `IUnitOfWork` from a DI
scope. All repositories share that scope's `MessangerDbContext` and its pooled singleton `NpgsqlDataSource`. Do not use
a scope/context concurrently.

Add methods and tracked entity mutations are persisted with `await unitOfWork.SaveChangesAsync(cancellationToken)`. A
save is atomic; multi-save operations need an explicit database transaction. Queries accept cancellation tokens, history
uses descending message-ID pagination with a maximum of 100 rows, and membership queries exclude departed users. User
insertion also inserts a profile and normalizes the username with invariant uppercase. `IChatRepository.CreateAsync` and `IMessageRepository.SaveTextAsync` own explicit transactions and commit their complete operations internally; they are not staged Add methods. The application history contract caps pages at 50 and reverses the repository page into chronological order.

Authentication use cases live in `Application/Authentication`; password hashing and JWT issuance are implemented in
`Infrastructure/Authentication`. The HTTP host configures token validation and wires the adapters through dependency
injection. Chat use cases enforce active membership for reads and writes. Media cleanup/outbox processing remains to be implemented. Refresh repository revocation methods stage changes until `SaveChangesAsync`.

## Validation

Against a migrated disposable PostgreSQL database:

```bash
dotnet build Messanger.sln
dotnet run --project tests/Messanger.IntegrationTests
node tests/browser/session.test.cjs
node tests/browser/read.test.cjs
node tests/browser/password.test.cjs
```

The executable runs 82 named checks (7 repository, 3 worker, and 72 HTTP/database/live scenarios): the original seven repository scenarios plus registration, password
hashing, concurrent username uniqueness, login, active-account authorization, JWT rejection cases, CORS, production
HTTPS enforcement, malformed/oversized bodies, user discovery, atomic group creation, history paging, cross-chat reply rejection, membership loss, SignalR delivery, browser WebSocket authentication, new-chat notification, reconnect recovery, member add/remove/rejoin, moderator restrictions, concurrent ownership/capacity changes, multi-tab revocation, stale tracked authorization, hub validation, Development/Production OpenAPI availability, account suspension/reactivation, soft deletion, refresh-family revocation, stale administrator claims, last-administrator concurrency, and closure of joined/idle connections, secure refresh cookies, reload restoration, rotation/replay/concurrency, family-scoped logout, CSRF, absolute session expiry, monotonic read markers, unread counts, read authorization/concurrency, rejoin progress, private per-user summary delivery, password-change validation, session invalidation, and concurrent password/login/refresh requests. Repository scenarios roll back their own transactions. HTTP scenarios
use a generated schema in the supplied test database and a temporary RSA key; both are removed after the suite. Use a
disposable database whose role can create/drop schemas. The test host applies migrations only to its generated schema.

`Program.cs` lists the checks; `Api/` and `Repositories/` contain focused scenarios and `Support/` contains shared test
infrastructure. The runner prints every PASS/FAIL result and exits with a nonzero code if any fail. Tests use ASP.NET
Core's in-process test server and the real PostgreSQL provider; HTTPS certificate negotiation itself is not exercised by
that test server. Browser QA additionally verified two-account live messaging, escaped text, duplicate suppression, reload/login recovery, and loading 57 messages across two history pages.

Add a schema migration with:

```bash
dotnet ef migrations add MigrationName --project src/Messanger.Infrastructure
```

## Production operations

See [deployment and recovery](deploy/README.md) for the HTTPS app/PostgreSQL stack, separate migration credentials, persistent encrypted session keys, health probes, administrator metrics, limits, key rotation, CI, and encrypted backup/restore drills. Production setup requires explicit secrets and certificates. Rate limits and connection quotas are enabled by default; see `Messanger/Security/SecurityLimitsOptions.cs` for configurable budgets.
