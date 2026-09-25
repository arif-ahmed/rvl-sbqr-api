---
name: deploy-sbqr
description: Use when the user asks to deploy, redeploy, ship, or update the SBQR.Api build to the AWS EC2 dev server ("deploy to EC2", "server e notun version dao", "update the server", "redeploy the API"). Builds, zips, uploads, runs idempotent DB migrations, restarts the service, verifies health, and auto-rolls back on failure. Dev environment only — no stage/prod exists yet.
---

# Deploy SBQR.Api to EC2 (dev)

One-shot deploy of the current repo state to the dev EC2 box. Follows playbook
§3.2–§3.4 (`docs/aws-deployment-playbook.md`). Everything below is the proven
sequence — do not improvise variants (e.g. do not unzip over the old folder
without `rsync --delete`; stale DLLs cause inexplicable MissingMethod bugs).

## Fixed dev environment facts

| Item | Value |
|---|---|
| Server | `ubuntu@13.212.164.114` |
| SSH key | `D:/Workspace/Sources/RVL/rvl-sbqr-key-dev.pem` (always add `-o BatchMode=yes`) |
| App dir on server | `/var/sbqr/api` |
| Env file | `/etc/sbqr/sbqr.env` — **never touch during deploy** (config changes are NOT deploys) |
| systemd unit | `sbqr-api` (runs as `www-data`) |
| Health | `http://localhost/health/live` and `/health/ready` (server-side) |
| RDS host | `rvl-sbqr-db-dev.cdwe2qugcujc.ap-southeast-1.rds.amazonaws.com`, db `sbqr_app`, user `sbqr_admin` |
| RDS password | ask the user if not already known this session. Never write it into committed files or echo it into chat output. |

All commands run from repo root (`D:\Workspace\Sources\RVL\rvl-secure-bqr-manager`)
via the shell tool (pwsh on this machine).

## Workflow

### 1. Build

```bash
dotnet publish src/Host/SBQR.Api/SBQR.Api.csproj -c Release -o ./publish
```

MUST complete with 0 errors (warnings acceptable). If build fails, stop and fix
the code — do not deploy a partial publish.

### 2. Zip

```powershell
Compress-Archive -Path ./publish/* -DestinationPath sbqr-api.zip -Force
```

Report the zip size afterwards (`(Get-Item sbqr-api.zip).Length`).

### 3. Upload

```bash
scp -i D:/Workspace/Sources/RVL/rvl-sbqr-key-dev.pem -o BatchMode=yes sbqr-api.zip ubuntu@13.212.164.114:~/
scp -i D:/Workspace/Sources/RVL/rvl-sbqr-key-dev.pem -o BatchMode=yes -r db ubuntu@13.212.164.114:~/
```

The `db/` folder always goes up — migrations run every deploy (idempotent).

### 4. Server-side deploy script — ALWAYS use the script-file pattern

**Never inline this over ssh.** pwsh expands `$TS`, `$f`, `$RDS_PASS` locally
before ssh sees them, silently producing garbage. Write the script to
`C:\Users\Arif\AppData\Local\Temp\opencode\deploy-sbqr.sh`, scp it, run it, then
delete it from both ends. Fill `<RDS_MASTER_PASSWORD>` before scp.

```bash
#!/usr/bin/env bash
set -euo pipefail

TS=$(date +%Y%m%d-%H%M)
RDS_HOST=rvl-sbqr-db-dev.cdwe2qugcujc.ap-southeast-1.rds.amazonaws.com
RDS_PASS='<RDS_MASTER_PASSWORD>'

echo "== 1. backup current release"
sudo cp -r /var/sbqr/api /var/sbqr/api.bak-$TS

echo "== 2. replace (temp dir + rsync --delete, then chown)"
sudo rm -rf /var/sbqr/api.new
sudo mkdir -p /var/sbqr/api.new
sudo unzip -q ~/sbqr-api.zip -d /var/sbqr/api.new
sudo rsync -a --delete /var/sbqr/api.new/ /var/sbqr/api/
sudo rm -rf /var/sbqr/api.new
sudo chown -R www-data:www-data /var/sbqr

echo "== 3. migrations (idempotent, libpq conninfo — space-separated lowercase)"
export PGPASSWORD="$RDS_PASS"
for f in ~/db/migrations/*.sql; do
  psql "host=$RDS_HOST user=sbqr_admin dbname=sbqr_app sslmode=require" \
    -v ON_ERROR_STOP=1 -f "$f"
done
unset PGPASSWORD

echo "== 4. restart"
sudo systemctl restart sbqr-api

echo "== 5. health"
sleep 5
systemctl is-active sbqr-api
curl -sf http://localhost/health/live  && echo " LIVE-OK"
curl -sf http://localhost/health/ready && echo " READY-OK"

echo "DEPLOY-OK backup=/var/sbqr/api.bak-$TS"
```

Run it: `scp` the script up, then
`ssh ... "bash ~/deploy-sbqr.sh; rm ~/deploy-sbqr.sh"`.
Delete the local temp copy afterwards too.

### 5. Success criteria

- Script ends with `DEPLOY-OK backup=/var/sbqr/api.bak-<ts>`
- `is-active` → `active`, both `LIVE-OK` and `READY-OK` printed
  (`/health/ready` also proves RDS + S3 vault connectivity from the new build)
- Migrations printed `NOTICE ... skipping` for already-applied objects — that
  is normal idempotency, NOT an error

Also tail the journal for startup noise:

```bash
ssh ... "sudo journalctl -u sbqr-api --since '1 minute ago' --no-pager | tail -20"
```

Known non-fatal noise: `HttpTrustStoreClient` → `Connection refused (localhost:5002)`
(dev trust-store mock not deployed). Ignore it.

### 6. Clean up build artifacts (success path only)

After `DEPLOY-OK`, remove the local build outputs and the server-side zip copy:

```powershell
# local (repo root)
Remove-Item -Recurse -Force ./publish
Remove-Item -Force sbqr-api.zip
```

```bash
# server
ssh -i D:/Workspace/Sources/RVL/rvl-sbqr-key-dev.pem -o BatchMode=yes ubuntu@13.212.164.114 "rm -f ~/sbqr-api.zip"
```

Skip this on a failed/rolled-back deploy until the user decides next steps.

### 7. If health fails — AUTO-ROLLBACK, then report

Run (script-file pattern again):

```bash
#!/usr/bin/env bash
set -euo pipefail
sudo journalctl -u sbqr-api --since '10 minutes ago' --no-pager | tail -40
echo "== rolling back to most recent backup =="
BAK=$(sudo ls -1dt /var/sbqr/api.bak-* | head -1)
echo "restoring $BAK"
sudo systemctl stop sbqr-api
sudo rm -rf /var/sbqr/api
sudo mv "$BAK" /var/sbqr/api
sudo systemctl start sbqr-api
sleep 5
systemctl is-active sbqr-api
curl -sf http://localhost/health/live  && echo " LIVE-OK"
curl -sf http://localhost/health/ready && echo " READY-OK"
echo "ROLLBACK-OK restored=$BAK"
```

After a rollback ALWAYS tell the user:
1. The journal error that caused the failure (first exception in the tail).
2. **Migrations cannot be rolled back** (no down-migrations) — if step 3 of
   the deploy already applied a NEW migration, the restored old code now runs
   against the newer schema. Usually fine (additive, backward-compatible) but
   must be stated explicitly.

## Gotchas (machine + tooling specifics)

- **rtk wrapper**: `rtk ssh` / `rtk scp` work; `rtk curl` prints a misleading
  `FAILED` prefix even on HTTP 200 — check the body/status yourself. rtk cannot
  run PowerShell cmdlets — run `Compress-Archive` etc. directly.
- **psql conninfo is libpq format**: `host=... user=... password=...` with
  spaces, lowercase. Npgsql-style `Host=...;Password=...` fails with
  `invalid connection option "Host"`.
- **`rsync --delete` is mandatory** — plain unzip-over leaves stale assemblies.
- **Env file changes are not deploys** — `/etc/sbqr/sbqr.env` edits need only
  `sudo systemctl restart sbqr-api`, no build/upload.
- **Backup hygiene**: old `/var/sbqr/api.bak-*` dirs accumulate (~130 MB each).
  Occasionally keep the newest 2–3 and `sudo rm -rf` the rest.
- Passwords: RDS password may already exist in session history; never re-echo
  secrets into visible output or committed files. The temp deploy script (which
  contains the password) must be deleted on BOTH ends after every run.

## Final report to the user (Banglish ok)

Include: build status, zip size, backup path created, migration result
(applied/skipped), health results, rollback status if it happened.
