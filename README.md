# Messanger

[English](README.md) | [Русский](README.ru.md)

## About

Messanger is a browser-based text messaging app that lets users register, find people, create group chats, exchange messages in real time, and access their saved conversation history.

## Features

- Register, sign in, and stay signed in between visits.
- Change your password and sign out.
- Search for people and create group chats.
- Send and receive text messages in real time.
- Browse saved conversations and load older messages.
- See unread message counts and keep read progress synchronized across tabs.

## How to Run

### Run locally (Bash)

Requires .NET 10 SDK, Docker with Compose, and OpenSSL. Run these commands from the project root using Bash.

1. Configure and start PostgreSQL:

   ```bash
   cp .env.example .env
   docker compose up -d --wait
   ```

2. Generate a local signing key:

   ```bash
   mkdir -p .local
   openssl genpkey -algorithm RSA \
     -pkeyopt rsa_keygen_bits:2048 \
     -out .local/jwt-private.pem
   ```

3. Configure the application in your terminal:

   ```bash
   export ConnectionStrings__Postgres="Host=localhost;Port=5432;Database=messanger;Username=messanger;Password=messanger_local_only"
   export Jwt__PrivateKeyPath="$PWD/.local/jwt-private.pem"
   ```

   If you change the database credentials or port in `.env`, update the connection string accordingly.

4. Set up HTTPS, apply migrations, and start the app:

   ```bash
   dotnet dev-certs https --trust
   dotnet run --project Messanger -- --migrate
   dotnet run --project Messanger --launch-profile Local
   ```

   On Linux, certificate trust may require additional setup for your distribution and browser.

Open [https://localhost:7100](https://localhost:7100).

## Security

- Passwords are stored as salted hashes.
- Short-lived access tokens and rotating refresh tokens manage sign-in sessions.
- Changing a password invalidates existing sessions and closes their live connections.
- Membership and role checks control access to chats and messages.
- HTTPS, CSRF protection, and browser-origin restrictions protect connections and session operations.
- Rate limits and connection quotas help prevent abuse.

## Stack

| Technology | Purpose |
|---|---|
| C#, .NET 10, and ASP.NET Core | Backend language, runtime, web server, and HTTP API |
| SignalR | Real-time messages and updates |
| PostgreSQL 18 | Persistent data storage |
| Entity Framework Core and Npgsql | Database access and migrations |
| JWT and ASP.NET Core Identity | Authentication |
| Docker, Docker Compose, and GitHub Actions | CI/CD |
