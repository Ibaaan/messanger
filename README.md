# Messanger

PostgreSQL persistence foundation for .NET 10, organized into separate projects with one type per file:

| Project                            | Responsibility                                    | References          |
|------------------------------------|---------------------------------------------------|---------------------|
| `Messanger`                        | Console host and dependency injection composition | Infrastructure      |
| `src/Messanger.Domain`             | Entities and domain rules                         | None                |
| `src/Messanger.Application`        | Repository contracts and future use cases         | Domain              |
| `src/Messanger.Infrastructure`     | EF Core, PostgreSQL repositories, migrations      | Application, Domain |
| `tests/Messanger.IntegrationTests` | Executable database integration checks            | Infrastructure      |

Project references enforce dependency direction at compile time. Domain and Application do not reference EF Core or
Npgsql, so business logic can be tested independently of PostgreSQL. The host wires interfaces to Infrastructure
implementations through dependency injection. These projects still form one deployed application; the tests run
separately.

This is a small Clean Architecture-style layout. Compared with folders alone, projects provide stronger boundaries and
keep persistence dependencies out of the core. The tradeoff is more project files and reference management.

For similar separation,
see [Microsoft's architecture guide](https://learn.microsoft.com/en-us/dotnet/architecture/modern-web-apps-azure/common-web-application-architectures), [Ardalis CleanArchitecture](https://github.com/ardalis/CleanArchitecture),
and [Jason Taylor's CleanArchitecture](https://github.com/jasontaylordev/CleanArchitecture).

## Local database

```bash
cp .env.example .env
docker compose up -d --wait
export ConnectionStrings__Postgres='Host=localhost;Port=5432;Database=messanger;Username=messanger;Password=messanger_local_only;Maximum Pool Size=20;Timeout=10;Command Timeout=30'
dotnet tool restore
dotnet ef database update --project src/Messanger.Infrastructure
dotnet run --project Messanger
```

If you change `.env`, update the connection string to match. Compose reads `.env`; .NET reads the exported environment
variable. The database binds only to localhost and stores data in a named volume. PostgreSQL 18 uses the
`/var/lib/postgresql` volume path. `docker compose down` stops the database and preserves its data.

The console entry point checks connectivity only. Schema changes are applied explicitly with EF migrations, never on
application startup. Use deployment-managed credentials and TLS for production.

## Repository usage

Register `services.AddPostgresRepositories(connectionString)`, then resolve repositories and `IUnitOfWork` from a DI
scope. All repositories share that scope's `MessangerDbContext` and its pooled singleton `NpgsqlDataSource`. Do not use
a scope/context concurrently.

Add methods and tracked entity mutations are persisted with `await unitOfWork.SaveChangesAsync(cancellationToken)`. A
save is atomic; multi-save operations need an explicit database transaction. Queries accept cancellation tokens, history
uses descending message-ID pagination with a maximum of 100 rows, and membership queries exclude departed users. User
insertion also inserts a profile and normalizes the username with invariant uppercase.

Authorization and domain rules (active sender membership, same-chat replies/read markers, validated usernames, and
race-safe refresh rotation) belong in the future application use cases. The persistence foundation does not yet
implement those use cases, media cleanup/outbox processing, or HTTP endpoints. Refresh revocation methods stage changes
until `SaveChangesAsync`.

## Validation

Against a migrated disposable PostgreSQL database:

```bash
dotnet build Messanger.sln
dotnet run --project tests/Messanger.IntegrationTests
```

The integration executable tests user/profile insertion, normalized username uniqueness, active membership filtering,
message pagination, and refresh-family revocation. Its data changes run inside a rolled-back transaction.

Add a schema migration with:

```bash
dotnet ef migrations add MigrationName --project src/Messanger.Infrastructure
```
