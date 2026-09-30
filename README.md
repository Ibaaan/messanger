# Messanger

ASP.NET Core 10 API with PostgreSQL persistence and access-token authentication, organized into separate projects with
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
  Http/              Error responses, exception handling, transport/origin policies
  Program.cs         Host composition
src/
  Messanger.Domain/   Entities kept flat: this small project has no competing categories
  Messanger.Application/
    Authentication/  Registration/login use cases, token/password ports, auth results
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
variable. The database binds only to localhost and stores data in a named volume. PostgreSQL 18 uses the
`/var/lib/postgresql` volume path. `docker compose down` stops the database and preserves its data.

Schema changes are applied explicitly with EF migrations, never on application startup. Use deployment-managed
credentials and database TLS for production.

## Run the authentication API

Generate a local signing key outside the repository, and configure the development HTTPS endpoint:

```bash
export Jwt__PrivateKeyPath="$(mktemp /tmp/messanger-signing-XXXXXX.pem)"
openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 -out "$Jwt__PrivateKeyPath"
chmod 600 "$Jwt__PrivateKeyPath"
dotnet dev-certs https
export ASPNETCORE_ENVIRONMENT=Development
export ASPNETCORE_URLS=https://localhost:7100
export HttpSecurity__AllowedOrigins__0=https://localhost:7100
dotnet run --project Messanger
```

Keep the exported `ConnectionStrings__Postgres` from the database setup. .NET does not automatically load `.env`.
Production requires a managed private-key file, HTTPS, exact allowed origins, and `AllowedHosts` set to the deployed
host name. `Jwt__Issuer` and `Jwt__Audience` override the development identifiers in `appsettings.json`. Missing/invalid
signing configuration fails startup. Do not generate a new production key on every restart.

`HttpSecurity__TrustedProxies__0` can add an explicit trusted reverse-proxy IP for forwarded scheme/address headers.
Built-in loopback proxy trust remains; arbitrary remote forwarders are not trusted. Empty `AllowedOrigins` accepts
requests without an Origin header, but denies browser requests carrying one. CORS allows only GET/POST and
Authorization/Content-Type, without cookies. Production rejects unencrypted requests; Development permits local HTTP for
tooling. The current auth API limits request bodies to 16 KiB.

| Endpoint             | Access                         | Result                                                                                                                                                       |
|----------------------|--------------------------------|--------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `POST /registration` | Public                         | Validates username/password and atomically creates an active user plus profile; returns 201, or 409 for a duplicate username.                                |
| `POST /auth/login`   | Public                         | Returns an RS256 access token, Bearer token type, expiry, and safe user fields. Invalid credentials and inactive accounts return the same generic 401 error. |
| `GET /api/users/me`  | Active account + access Bearer | Returns the signed-in user's ID, username, role, and language.                                                                                               |

Registration/login bodies are `{ "username": "alice", "password": "a long passphrase" }`. Usernames contain 3–20 ASCII
letters/digits/underscores; uniqueness/login lookup ignore case. Passwords contain 10–256 characters, allow spaces, and
cannot be all whitespace. Registration creates the default Russian-language profile/account settings. Passwords use
ASP.NET Identity hashes; hashes are never returned to clients.

For a self-signed local development certificate only, `curl -k` can exercise the endpoints:

```bash
curl -k https://localhost:7100/registration -H 'Content-Type: application/json' -d '{"username":"alice","password":"a long passphrase"}'
curl -k https://localhost:7100/auth/login -H 'Content-Type: application/json' -d '{"username":"alice","password":"a long passphrase"}'
# Copy accessToken from the login response into ACCESS_TOKEN in the same terminal.
curl -k https://localhost:7100/api/users/me -H "Authorization: Bearer $ACCESS_TOKEN"
```

Access tokens expire after 15 minutes, with a 60-second validation clock skew. Validation checks RS256, signature,
issuer, audience, expiry/not-before, `at+jwt` header type, `token_use=access`, and required identity claims. Protected
requests also check the current account status in PostgreSQL. Responses use `Cache-Control: no-store`; errors use
`{ "error": { "code", "message", "requestId" } }` with actual JSON values.

This is the first P0 milestone. Refresh/logout rotation remains P1 and no refresh token is issued yet; log in again when
the access token expires. Chat/search/message endpoints, SignalR, the messaging UI, public-release rate limits, and
production readiness remain on the status table. The host does not mutate the schema on startup.

## Repository usage

Register `services.AddPostgresRepositories(connectionString)`, then resolve repositories and `IUnitOfWork` from a DI
scope. All repositories share that scope's `MessangerDbContext` and its pooled singleton `NpgsqlDataSource`. Do not use
a scope/context concurrently.

Add methods and tracked entity mutations are persisted with `await unitOfWork.SaveChangesAsync(cancellationToken)`. A
save is atomic; multi-save operations need an explicit database transaction. Queries accept cancellation tokens, history
uses descending message-ID pagination with a maximum of 100 rows, and membership queries exclude departed users. User
insertion also inserts a profile and normalizes the username with invariant uppercase.

Authentication use cases live in `Application/Authentication`; password hashing and JWT issuance are implemented in
`Infrastructure/Authentication`. The HTTP host configures token validation and wires the adapters through dependency
injection. Chat rules (active sender membership and same-chat replies/read markers), race-safe refresh rotation, and
media cleanup/outbox processing remain to be implemented. Refresh repository revocation methods stage changes until
`SaveChangesAsync`.

## Validation

Against a migrated disposable PostgreSQL database:

```bash
dotnet build Messanger.sln
dotnet run --project tests/Messanger.IntegrationTests
```

The integration executable runs 19 named checks: the original seven repository scenarios plus registration, password
hashing, concurrent username uniqueness, login, active-account authorization, JWT rejection cases, CORS, production
HTTPS enforcement, and malformed/oversized bodies. Repository scenarios roll back their own transactions. HTTP scenarios
use a generated schema in the supplied test database and a temporary RSA key; both are removed after the suite. Use a
disposable database whose role can create/drop schemas. The test host applies migrations only to its generated schema.

`Program.cs` lists the checks; `Api/` and `Repositories/` contain focused scenarios and `Support/` contains shared test
infrastructure. The runner prints every PASS/FAIL result and exits with a nonzero code if any fail. Tests use ASP.NET
Core's in-process test server and the real PostgreSQL provider; HTTPS certificate negotiation itself is not exercised by
that test server.

Add a schema migration with:

```bash
dotnet ef migrations add MigrationName --project src/Messanger.Infrastructure
```
