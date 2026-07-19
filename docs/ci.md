# CI / GitHub Actions Operations

The CI is built security-first: every **required** check runs with no secrets, so it behaves
identically for fork and same-repo PRs. Secret-bearing jobs are gated and never required.

## Workflows

| Workflow | Trigger | Secrets | Required |
|---|---|---|---|
| `ci.yml` (build, unit-test, integration-test) | PR + push master | none | yes |
| `dependency-review.yml` | PR | none | yes (recommended) |
| `secret-scan.yml` (gitleaks) | PR + push | `GITLEAKS_LICENSE` (org) | no (skips fork PRs) |
| `api-canary.yml` (periodicals) | daily + dispatch + PR (paths) | none | no (advisory) |
| `bot-smoke.yml` | dispatch + push master | repo secrets | no |

## Required status checks (branch protection on `master`)

Mark these as required:
- `CI / build`
- `CI / unit-test`
- `CI / integration-test`
- `Dependency Review / review`

All run with no secrets and pass identically for fork and same-repo PRs.

`Secret Scan / gitleaks` must NOT be marked required: it needs the org `GITLEAKS_LICENSE`
secret and skips fork PRs, so a required check would leave fork PRs permanently pending.

## Non-blocking / advisory

- `API Canary / periodicals` - advisory on PRs that touch the API client (a third-party outage must
  not block merge); the daily scheduled run is the alarm for a stale shipped
  `ClientVersion`/`AppVersion`/`AppBuild`.
- `Bot Smoke (gated)` - never required. Triggers only on push-to-master and manual dispatch, so
  the running workflow is always master's reviewed version (no PR-controlled copy ever runs with
  secrets).

## Secrets for the optional bot smoke

`bot-smoke.yml` is the only workflow that reads repository secrets. Add them as **Repository secrets**
(Settings -> Secrets and variables -> Actions):
- `CI_DISCORD_TOKEN` - a **dedicated test bot** token, never the production bot.
- `CI_DB_CONNECTION` - a **non-production** PostgreSQL connection string.

Why this is safe without an Environment:
- The job has no `pull_request` trigger, so a PR can never run a modified copy of it with secrets.
- It only fires on push-to-master (already-merged, reviewed code) and `workflow_dispatch`.
- GitHub withholds repository secrets from fork-triggered runs regardless.

Never put production secrets here. If admin access + a paid plan or a public repo later make
Environments available, move these to a reviewer-gated `ci-live` Environment for an extra approval
step.

## Test categories

Tests are tagged so CI can select them:
- `Unit` - fast, offline (the `EGG9000.Test` suite).
- `Integration` - needs Docker; `EGG9000.Test.Integration` spins up PostgreSQL via
  Testcontainers (DB launch, migration apply, bot DI wiring).
- `Network` - hits the live Egg Inc API; run only in the canary, never in the offline gate.

Run locally:
```
dotnet test EGG9000.Test --filter "TestCategory=Unit"
dotnet test EGG9000.Test.Integration --filter "TestCategory=Integration"   # Docker required
dotnet test EGG9000.Test.Integration --filter "TestCategory=Network"       # network required
```

## CodeQL (removed)

There is intentionally no CodeQL workflow. It was removed while the repo was private (code
scanning required GitHub Advanced Security there). Now that the repo is public, code scanning is
free; re-adding a CodeQL workflow is an open option.

## Action pinning policy

All third-party actions are pinned to a full-length commit SHA with the version in a trailing
comment. To bump an action: resolve the new tag's SHA and update the SHA and the comment together.
```
git ls-remote https://github.com/<owner>/<action> refs/tags/<tag>
```
