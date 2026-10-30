# Deployment and recovery

This package targets one application instance and PostgreSQL 18. Media/object storage is a P2 feature and is not deployed yet. Requirements: Docker Compose v2, OpenSSL, Python 3.12+, and a host with persistent storage. Run commands from the repository root.

## Prepare and run

```sh
docker build -t messanger:local .
python3 deploy/init-secrets.py /secure/messanger-secrets --origin https://localhost:7443
export MESSANGER_SECRETS_DIR=/secure/messanger-secrets
export COMPOSE_PROJECT_NAME=messanger-production
docker compose -f deploy/compose.yaml up -d --wait app
```

The generator creates a private CA for development or a managed internal deployment. Trust its `ca.crt` explicitly on clients. For public use, replace the application TLS certificate with one trusted by clients, configure the real HTTPS origin, allowed host, JWT issuer, and health-check hostname in `runtime.json`, and arrange certificate renewal. Do not disable certificate validation. The default port binds only to loopback; expose it through a correctly configured HTTPS proxy or deliberately change the binding. Configure trusted proxies only for actual proxy addresses. Preserve WebSocket upgrades, HTTPS forwarding, and the exact browser origin. Never publish the database port.

Secrets are regular files inside a host directory with mode 0700. Mounted files are readable by the non-root application user; restrict access to the host and Docker socket. Keep generated CA private keys offline after provisioning. Production refuses ephemeral, unencrypted Data Protection storage. The key volume, protection certificate/private key, JWT signing key, and database are all required for continuity.

Compose runs migrations as a separate job using the schema-owner role; runtime credentials have DML privileges without schema creation or migration-history writes. Deploy a versioned image tag/digest instead of `local` for releases. Before upgrading, take a backup, review migrations, test them against a restored copy, then run the migration job and app. An older binary is safe to roll back only while it remains compatible with the resulting schema; database rollback requires a deliberate restore and loses writes after that snapshot.

## Health, logging, and limits

`GET /health/live` reports process liveness. `GET /health/ready` checks database connectivity, applied migrations, and model compatibility with a short timeout and cached results. Use readiness to remove unhealthy instances from traffic. Storage checks will be added with P2 media storage.

JSON request logs carry a generated `X-Request-ID`, endpoint name, status, and duration. Security denials are sampled. Raw paths, query tokens, credentials, and message bodies are not logged by these components. `GET /ops/metrics` requires an active administrator and reports bounded request counts/latencies, security events, active hub connections, and Npgsql command/pool measurements. The `Messanger` meter can also be collected with .NET diagnostic tooling. Alert on failed readiness, increased 5xx/429 responses, sustained latency, exhausted connections, and failed backups. Protect and rotate logs; choose retention and alert thresholds for the actual workload.

`SecurityLimits` configures fixed-window HTTP/IP/server/account/message/hub budgets, credential concurrency, and connection quotas. Defaults are in `SecurityLimitsOptions.cs`. Limits are per process; deploy one instance until shared quotas and SignalR distribution are implemented. Identity partitions hash into 4096 buckets to bound memory; collisions conservatively share allowance. Untrusted forwarding headers cannot change the client IP. Tune quotas using measured legitimate traffic before public rollout.

## Coordinated encrypted backups

Create an RSA backup recipient certificate and private key outside the application secrets directory. Keep the private key offline or in a recovery vault; the backup host needs only the public certificate.

```sh
openssl req -x509 -newkey rsa:3072 -nodes -keyout /offline/backup.key -out /secure/backup.crt -days 365 -subj /CN=MessangerBackup
python3 deploy/ops.py --project messanger-production --secrets /secure/messanger-secrets backup /backups/snapshot.cms --recipient /secure/backup.crt
```

Backup briefly stops the app to coordinate the database, Data Protection volume, and secret files. It resumes a previously running app even after a backup failure. The archive uses authenticated AES-256-GCM encryption with RSA-OAEP key wrapping and includes sensitive private keys. Temporary plaintext needs a trusted local filesystem. Copy encrypted backups off-host, restrict access, define a retention schedule, and verify successful uploads. Never put the recovery private key in the archive or lose it.

Choose backup frequency from your recovery point objective: a daily backup can lose up to a day of messages. Measure recovery time with representative database sizes; the small smoke fixture is not an RTO guarantee. Schedule backups with your deployment scheduler and alert on failures or stale snapshots.

## Restore and failure drills

Restore overwrites the explicitly named target database, keys, and secrets. Prefer a separate recovery project first. The archive is authenticated and paths validated before the target is stopped. The exact project name must be supplied twice to prevent accidental targeting.

```sh
python3 deploy/ops.py --project messanger-recovery --secrets /secure/recovery-secrets --port 7444 restore /backups/snapshot.cms --recipient /secure/backup.crt --identity /offline/backup.key --confirm-project messanger-recovery
```

Restore recreates credentials, restores data and protection keys, applies current migrations, and waits for health. Every account's session version is advanced and all refresh families revoked: users sign in again so old backups cannot revive sessions revoked after the snapshot. Verify message history, account access, HTTPS, and WebSockets before redirecting traffic. Resolve the desired hostname/port and certificates in the restored configuration before opening it to clients.

`python3 deploy/smoke.py` builds disposable project names and tests real HTTPS/WSS messaging, least-privilege database TLS, session continuity across app restart, pending migrations, database outage/recovery, encrypted backup integrity, restore, history preservation, and post-restore session invalidation. It removes only its own containers and volumes. CI runs the same drill after the integration and browser-unit suites. CI is configured; a successful remote workflow run is established by your Git hosting service, not by local verification.

## Key rotation

Rotate the JWT private key and keep old public PEM files in `Jwt:PreviousPublicKeyPaths` through the longest outstanding refresh lifetime. New tokens use the current key; existing tokens can validate with retained public keys. Remove compromised keys immediately and revoke affected sessions.

Rotate the Data Protection certificate using `DataProtection:CertificatePath`, retaining previous PFX files in `PreviousCertificatePaths`. Those files currently use the configured `CertificatePassword`. Keep previous certificates for as long as encrypted key files need them. Back up keys and certificates together before rotation and rehearse restart/recovery. Preserve the `Messanger` application name. Renew TLS separately, retaining correct DNS names and trust chains.
