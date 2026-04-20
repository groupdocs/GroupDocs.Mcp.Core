# Release Process — GroupDocs.MCP.Core

End-to-end checklist for releasing a new version of the 4 Core NuGet packages.
All 4 packages are released together under one CalVer version.

## Versioning — CalVer `YY.MM.N`

- `YY` — 2-digit year (e.g. `26` = 2026)
- `MM` — month without leading zero (e.g. `4` = April)
- `N` — patch increment starting at `0`; increment for hotfixes within the same month

Example: `26.4.0`, `26.4.1`, `26.5.0`.

---

## Day-to-day work (no release)

Just push to `main`:

```bash
git add <files>
git commit -m "…"
git push
```

`build_packages.yml` and `run_tests.yml` run on every push — `publish_prod.yml` does **not**. Commits never create a tag, a NuGet release, or a GitHub Release. Changelog updates, code edits, and version-prop bumps are all free actions — you can commit them whenever without triggering a release.

---

## Releasing a new version

### 1. Prepare the release commit

All four changes go in **one commit on `main`**:

1. **Bump all four version properties** in [build/dependencies.props](build/dependencies.props). They must match — the four packages release in lockstep.

   ```xml
   <GroupDocsMcpCore>{NEW_VERSION}</GroupDocsMcpCore>
   <GroupDocsMcpLocalStorage>{NEW_VERSION}</GroupDocsMcpLocalStorage>
   <GroupDocsMcpAwsS3Storage>{NEW_VERSION}</GroupDocsMcpAwsS3Storage>
   <GroupDocsMcpAzureBlobStorage>{NEW_VERSION}</GroupDocsMcpAzureBlobStorage>
   ```

2. **Add a changelog entry** — new file `changelog/NNN-short-slug.md` with `version: {NEW_VERSION}` in the frontmatter. Format in [changelog/README.md](changelog/README.md).

3. *(Optional, rare)* Bump external dependency versions in the same props file — `MicrosoftExtensions*`, `AzureStorageBlobs`, `AwsSdkS3`, `MicrosoftSourceLinkGithub`.

4. Commit + push:

   ```bash
   git add build/dependencies.props changelog/NNN-*.md
   git commit -m "Release {NEW_VERSION}"
   git push
   ```

### 2. Verify locally (optional but recommended)

```powershell
./build.ps1                                            # packs all 4 under build_out\
dotnet test src/GroupDocs.Mcp.Core.sln -c Release      # runs tests
```

### 3. Wait for CI green on `main`

`build_packages.yml` + `run_tests.yml` must be green before releasing.

### 4. Trigger the release

Two ways to release — **pick one, not both**.

#### Option A — UI dispatch (no git-CLI required)

1. GitHub → **Actions** → **Publish Prod** → **Run workflow** button.
2. Leave the branch dropdown on `main`.
3. Type the version (e.g. `26.4.0`) in the `version` input.
4. Click **Run workflow**.

The workflow validates the input matches `dependencies.props`, runs the full pipeline, and **creates the `26.4.0` tag + GitHub Release at the very end** — only if every prior step succeeded. If anything fails (bad version, signing rejected, publish error), no tag and no release are created.

#### Option B — tag push (no Actions UI required)

```bash
git tag 26.4.0
git push origin 26.4.0
```

The tag push fires the same workflow with `github.ref_name = 26.4.0`. Validation still runs (the tag name must match `dependencies.props`), rest of the pipeline is identical.

> Tag must be `YY.MM.N` — no `v` prefix, no suffix. The workflow rejects anything else.

### 5. CI takes over (either trigger)

`publish_prod.yml` runs these steps:

1. **Verify required secrets** (precheck — fails in seconds if any are missing).
2. **Resolve + validate version** — confirms input/tag matches the four props.
3. Build with `BUILD_TYPE=PROD` (stable version, no `-prod-xxx` suffix).
4. Pack 4 × `.nupkg` + 4 × `.snupkg`.
5. Sign `.nupkg` with SSL.com eSigner. Signing failures are detected via exit code **and** a file-count check.
6. Push to NuGet.org using `NUGET_API_KEY_PROD` (`--skip-duplicate` for idempotent re-runs).
7. **Only now** — create the GitHub Release (+ tag if it doesn't yet exist) with the changelog body + nupkg assets attached.

### 6. Post-release verification

- [ ] All four packages appear on nuget.org at the new version
- [ ] NuGet listing shows the signed-package badge
- [ ] GitHub Release is created and links to the changelog entry
- [ ] Symbol packages (`.snupkg`) are present on nuget.org symbols

### 7. Re-running a failed release

If something upstream failed and you need to re-run:

- **Via UI**: Actions → pick the failed run → **Re-run all jobs**. (Or start a fresh Run workflow with the same version input — `dotnet nuget push --skip-duplicate` makes this safe.)
- **Via tag**: delete and re-push the tag only if the tag was never created:

  ```bash
  git push origin :refs/tags/{VERSION}    # delete from remote if it exists
  git tag -d {VERSION}                    # delete locally
  git tag {VERSION}                       # re-tag HEAD
  git push origin {VERSION}               # fires the workflow again
  ```

  **Do not re-push a tag that already has a successful release pointing at it** — that would rewrite history for downstream consumers.

---

## Required GitHub secrets & variables

**Secrets** (`Settings → Secrets and variables → Actions → Secrets`):

| Secret | Purpose |
|---|---|
| `NUGET_API_KEY_PROD` | NuGet.org API key scoped to the 4 Core package IDs |
| `ES_USERNAME` | SSL.com eSigner username |
| `ES_PASSWORD` | SSL.com eSigner password |
| `ES_TOTP_SECRET` | SSL.com eSigner TOTP 2FA secret |
| `CODE_SIGN_CLIENT_ID` | SSL.com eSigner OAuth CLIENT_ID |

**Variables** (`Settings → Secrets and variables → Actions → Variables`):

| Variable | Default | Purpose |
|---|---|---|
| `CODE_SIGN_TOOL_VERSION` | `1.3.0` | CodeSignTool release tag from github.com/SSLcom/CodeSignTool |

The first step of `publish_prod.yml` fails fast if any secret is missing — no CI minutes burned before the blocker is surfaced.

---

## Yanking a bad release

Never re-upload the same version — nuget.org rejects replays.

1. On nuget.org, **unlist** (don't delete) the bad packages.
2. Bump `N` in [build/dependencies.props](build/dependencies.props) (e.g. `26.4.0` → `26.4.1`).
3. Add a `type: fix` changelog entry describing the issue.
4. Commit + push + release the patch using the normal flow above.
