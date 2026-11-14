# XrplCSharp — C# SDK for XRP Ledger

A pure C# implementation for interacting with the XRP Ledger. Published as the `Xrpl` NuGet package.
Provides native C# methods and models for XRPL transactions, serialization, transaction signing, and the rippled WebSocket API.

Repository: [github.com/StaticBit-io/XrplCSharp](https://github.com/StaticBit-io/XrplCSharp)

## Technology Stack

- **.NET 10 / 11** — multi-target (`net10.0;net11.0` for the main library and both X402 packages); `net8.0` and `net9.0` were dropped in 11.27.0.0 at their end of support. CI installs SDKs 10 and 11, since 11 builds the `net11.0` target and the test projects run on `net10.0`
- **C# latest** — `<LangVersion>latest</LangVersion>` in main Xrpl project
- Base libraries target **netstandard2.0** (AddressCodec, BinaryCodec) and **netstandard2.1** (Keypairs)
- **System.Text.Json** — JSON serialization in client code
- **NBitcoin** 10.0.21 — BIP-39 mnemonics and BIP-32 derivation in `XrplWallet`
- **NBitcoin.Secp256k1** 4.0.4 — all secp256k1 work in `Xrpl.Keypairs` (`K256SecretKey`: signing, verification, key derivation), a libsecp256k1 port whose scalar and nonce are clearable value types. Do not reference its types from the `Xrpl` project: `NBitcoin` compiles the same namespace into its own assembly. The repository dropped BouncyCastle in 11.27.0.0, after checking the port byte for byte against it
- **Chaos.NaCl.Standard** 1.0.0 — Ed25519 operations
- **MSTest** — test framework
- **Flurl.Http** — HTTP client in tests

## Project Structure

Solution file: `XrplCSharp.sln`

```
XrplCSharp/
├── Base/                              # Low-level codec libraries
│   ├── Xrpl.AddressCodec/            # Base58, XRP address encoding/decoding (netstandard2.0)
│   ├── Xrpl.BinaryCodec/            # XRPL binary serialization format (netstandard2.0)
│   └── Xrpl.Keypairs/               # Ed25519/secp256k1 key generation and signing (netstandard2.1)
│
├── Xrpl/                             # Main SDK library (net10.0;net11.0)
│   ├── Client/                       # XrplClient — WebSocket client to rippled
│   │   ├── IXrplClient.cs           # Main interface for consumers
│   │   ├── XrplClient.cs            # WebSocket connection, RPC methods, subscriptions
│   │   └── Json/                    # JSON converters for XRPL types
│   ├── Models/                       # Request/response DTOs for rippled API
│   │   ├── Methods/                 # account_info, account_lines, tx, submit, etc.
│   │   ├── Transactions/           # Payment, TrustSet, OfferCreate, AMMCreate, etc.
│   │   ├── Subscriptions/          # Stream events (ledger, transactions, accounts)
│   │   ├── Ledger/                 # Ledger objects (AccountRoot, RippleState, Offer, etc.)
│   │   ├── Common/                 # Currency, Amount, shared types
│   │   └── Utils/                  # Utility models
│   ├── Sugar/                       # High-level helpers (auto-fill, submit-and-wait, fee estimation)
│   ├── Wallet/                      # XrplWallet — key generation, signing, testnet funding
│   ├── Utils/                       # Hashing, utilities
│   └── Properties/
│
├── Tests/
│   ├── Xrpl.Tests/                  # Unit (TestU) + Integration (TestI) tests for main library
│   ├── Xrpl.AddressCodec.Test/     # AddressCodec unit tests
│   ├── Xrpl.BinaryCodec.Test/     # BinaryCodec unit tests
│   ├── Xrpl.Keypairs.Test/        # Keypairs unit tests
│   └── TestsClients/
│       ├── Test.ClonsoleApp/       # Console demo app
│       └── Blazor-WebAssembly/     # Blazor WASM demo app
│
├── Tools/
│   └── GenerateEnums/              # Code generator for XRPL enums from rippled definitions (net10.0)
│
├── DocFx/                           # Documentation generator config
├── docs/                            # Generated HTML documentation
│
├── .ci-config/
│   ├── docker-compose.ci.yml       # Docker Compose: xrpld standalone + ledger-acceptor (built from Dockerfile.release)
│   ├── rippled.cfg                 # rippled configuration for CI ([features]/[amendments] are generated)
│   ├── generate-amendments.sh      # Regenerates rippled.cfg [features]/[amendments] from a rippled tag's features.macro
│   ├── check-rpc-methods.sh        # rippled RPC methods vs SDK request models (protocol-watch rpc-methods job)
│   ├── rpc-methods-ignore.txt      # RPC methods the SDK deliberately does not implement, with reasons
│   ├── docker-compose.batchv11.yml # Nightly-develop stand for unreleased amendments (BatchV1_1, ...)
│   ├── Dockerfile.release          # xrpld stable image from packages.xrplf.org (version pinned)
│   ├── Dockerfile.nightly          # xrpld develop image from packages.xrplf.org (version pinned)
│   ├── rippled.batchv11.cfg        # Config with [amendments] genesis up-votes for the nightly stand
│   └── validators.txt              # Validator config for CI
│
├── XrplCSharp.sln                   # Solution file
├── test.runsettings                 # MSTest config: parallel at class level
├── CONTRIBUTING.md                  # Development setup and release process
├── CHANGES.md                       # Changelog
└── README.md                        # Usage examples and documentation links
```

## Architecture

### Package Hierarchy

```
Xrpl (main NuGet package)
├── Xrpl.BinaryCodec (binary serialization)
│   └── Xrpl.AddressCodec (Base58, address codec)
└── Xrpl.Keypairs (Ed25519/secp256k1)
    └── Xrpl.AddressCodec
```

### Key Public API

- **`XrplClient`** (`Xrpl.Client`) — main entry point; WebSocket connection to rippled, RPC methods (AccountInfo, Fee, ServerInfo, Subscribe, etc.), event-driven API (`OnConnected`, etc.)
- **`IXrplClient`** — interface for DI and testing
- **`XrplWallet`** (`Xrpl.Wallet`) — wallet generation, seed import, transaction signing
- **`WalletSugar`** — testnet faucet funding
- **`Xrpl.Sugar`** — auto-fill transaction fields, `SubmitAndWait`, fee helpers
- **`Xrpl.Models.*`** — full set of DTOs for rippled API methods, transactions, ledger objects

### Key Namespaces

| Namespace | Purpose |
|-----------|---------|
| `Xrpl.Client` | WebSocket client and JSON-RPC interface |
| `Xrpl.Client.Json` | JSON converters for XRPL types |
| `Xrpl.Models.Methods` | Request/response types for rippled API commands |
| `Xrpl.Models.Transactions` | Transaction type models (Payment, TrustSet, etc.) |
| `Xrpl.Models.Subscriptions` | Subscription/stream event models |
| `Xrpl.Models.Ledger` | Ledger entry types (AccountRoot, RippleState, etc.) |
| `Xrpl.Models.Common` | Currency, Amount, and shared types |
| `Xrpl.Sugar` | High-level transaction helpers |
| `Xrpl.Wallet` | Key management and signing |
| `Xrpl.Utils` | Hashing and utility functions |
| `Xrpl.BinaryCodec` | XRPL binary serialization |
| `Xrpl.BinaryCodec.Enums` | Generated enums from rippled definitions |
| `Xrpl.BinaryCodec.Types` | Serialization field types |
| `Xrpl.AddressCodec` | Base58 and address encoding |
| `Xrpl.Keypairs` | Ed25519 and secp256k1 key operations |

## Build & Test

### Prerequisites

- .NET 11 SDK (builds the `net11.0` target) and the .NET 10 runtime (the test projects run on `net10.0`)
- Docker + Docker Compose (for integration tests)

### Build

```bash
dotnet restore
dotnet build
```

### Unit Tests

```bash
dotnet test --verbosity normal --settings test.runsettings --filter "TestU"
```

### Integration Tests

Integration tests require a standalone `rippled` node. The Docker Compose setup provides one with automatic ledger acceptance:

```bash
# Start rippled in standalone mode
docker compose -f .ci-config/docker-compose.ci.yml up -d

# Run integration tests
dotnet test Tests/Xrpl.Tests/Xrpl.Tests.csproj --verbosity normal --settings test.runsettings --filter "TestI"

# Stop containers
docker compose -f .ci-config/docker-compose.ci.yml down
```

The Docker setup exposes:
- Port 5005 — JSON-RPC
- Port 5006 — WebSocket
- Port 6006 — Admin WebSocket

The `ledger-acceptor` container calls `ledger_accept` every 4 seconds to advance the standalone ledger.

Both stands build their own image from the XRP Ledger Foundation apt repository rather than pulling a prebuilt one. The CI stand used `xrpllabsofficial/xrpld` from Docker Hub until September 2026, when that third-party publisher had still not shipped 3.4.0 five days after the release and the stand sat a release behind with only a workflow notice to show for it. Building from `deb-stable` removes the dependency; `Dockerfile.release` and `Dockerfile.nightly` differ only in the channel and are kept side by side on purpose.

### Integration Tests for Unreleased Amendments (nightly stand)

Some amendments exist only on the rippled `develop` branch and are absent from the release build used by CI. A second stand runs the nightly `xrpld` build with those amendments enabled at genesis:

```bash
# Uses the same ports as the CI stand — stop it first
docker compose -f .ci-config/docker-compose.ci.yml down

docker compose -f .ci-config/docker-compose.batchv11.yml up -d --build
dotnet test Tests/Xrpl.Tests/Xrpl.Tests.csproj --settings test.runsettings --filter "TestIBatch|TestIDelegateSet|TestIClosedEndedVault"
docker compose -f .ci-config/docker-compose.batchv11.yml down
```

The node under test is selected through the environment, not in code: `XRPL_TEST_NODE` (`standalone` default, `devnet`, `testnet`) picks the funding policy, `XRPL_TEST_NODE_URL` overrides the WebSocket URL (a stand on other ports, a private node). See `Tests/Xrpl.Tests/Integration/README.md`.

Amendment-dependent test classes use `Tests/Xrpl.Tests/Integration/AmendmentGuard.cs`: `ClassInitialize` checks the Amendments ledger object and marks tests inconclusive (skipped, exit 0) when the amendment is not active — so these tests are safe on the CI stand and run for real on the nightly stand. To gate a new test class, add the amendment id constant to `AmendmentGuard` and call `Assert.Inconclusive` from `TestInitialize` when inactive.

Nightly stand specifics (see comments in `Dockerfile.nightly` / `rippled.batchv11.cfg`):
- rippled was renamed to **xrpld** on `develop`, and packaging moved from `repos.ripple.com` to the XRP Ledger Foundation at **`packages.xrplf.org`** in August 2026 (`docs/install.md` in rippled names it as the only source). The old host still answers, which is the trap: its nightly channel stopped on 2026-09-15 and never received 3.4.0, and its `stable` channel stopped at 3.3.0, so a pin left there tracks a frozen snapshot while every check reports it current. The channel is `deb-develop` and its suite is `any main`, not a distribution codename. The version must still be **pinned**, now for reproducibility rather than ordering: build numbers are monotonic in the new scheme (`0.0.0~dev-1329.20261023git1f46f75`), so the old 14-to-12-digit timestamp trap died with the old host. The upstream part says nothing on develop: since XRPLF/rippled#8457 (2026-10-01) the version comes from a git tag only, so every untagged develop build is `0.0.0~dev`, and builds before it read `3.5.0~b0-...`. dpkg therefore orders the newer builds below the older ones — an unpinned `apt-get install xrpld` installs the September build — so the script sorts on the build number, not the version. To bump, run `.ci-config/bump-nightly-pin.sh` (`--check` reports without changing anything): it picks the newest `xrpld` build from the `deb-develop` Packages index, rewrites `ARG XRPLD_VERSION` and regenerates the nightly amendment lists from the develop commit encoded in that version string. Entries published without the `.<date>git<sha>` suffix carry no commit and are skipped. The ref must match the pinned build — a newer ref can emit feature names unknown to the binary, which rejects them at startup — which is why the script derives it from the version rather than taking it as an argument. `nightly-pin-watch.yml` runs the same script weekly and opens a PR once the pin is older than its allowance.
- On xrpld (3.2.x and `develop`) the two amendment config sections do different jobs. `[amendments]` (`<hash> <name>`, hash = sha512half of the name) registers genesis up-votes at `--start` — amendments become enabled **on-ledger** (Amendments object, `feature` RPC), which is what `AmendmentGuard`-gated tests check; only Supported::Yes amendments — unsupported ones (e.g. MPTokensV2 on 3.2.0) are skipped at genesis. `[features]` does NOT vote (startup logs say "will be down voted by default") but it DOES feed Rules presets: listed amendments are treated as active during **transaction processing** even when not enabled on-ledger — this is the only way to exercise Supported::No code paths (MPTokensV2 MPT-AMM tests run via this mechanism) and the reason `[features]` may be a superset of `[amendments]`. Without `[amendments]`, introspection reports everything disabled and guard-gated tests skip, even though preset-covered transactors work. Both sections are regenerated by `.ci-config/generate-amendments.sh <tag>` — do not edit the lists by hand.

### Generate Documentation

```bash
dotnet tool install -g docfx
docfx DocFx/docfx.json
```

Output goes to `docs/` directory. Published to GitHub Pages.

## CI/CD

### GitHub Actions (`.github/workflows/`)

| Workflow | Trigger | Purpose |
|----------|---------|---------|
| `dotnet.test.yml` | Push/PR to `dev`, `release`; merge queue | Build + unit tests (`TestU`) + integration tests (`TestI` with Docker rippled) |
| `nuget.release.yml` | Push to `release` | Wait for `.NET CI` on the same commit to pass → Build Release → Pack → Publish to GitHub Packages + NuGet.org |
| `docs.yml` | Push to `release` | DocFx → GitHub Pages |
| `protocol-watch.yml` | Weekly cron (Mon 06:00 UTC); manual | Two watches in a matrix, each with its own tracking issue. `protocol-watch` diffs rippled develop's `*.macro` protocol files and flag/result headers against its baseline. `engine-watch` diffs the rippled sources `Xrpl.Amounts` is ported from (payment steps, offer stream, AMM helpers, `STAmount`/`IOUAmount`/`Number`, `Quality`, `PaymentSandbox`, the `OfferCreate` and `Payment` transactors and their helpers, the path finder and `Rules.cpp`), with its baseline starting at the ported release (`baseline_ref`, now `3.4.1`) so drift since that release is reported. Both comment on their issue when a watched file changes. A third job, `retired-rules`, runs `.ci-config/check-retired-rules.sh` against develop's `features.macro` and is red while an amendment `LedgerRules` reads is retired (`XRPL_RETIRE_*`) or unregistered. A retired amendment is always on in the node, though `feature` still reports the ledger's state, so the port must hardwire it on; an unregistered one is investigated first, since it may have been dropped unenabled or renamed (`Batch` became `BatchV1_1`). A fourth job, `rpc-methods`, runs `.ci-config/check-rpc-methods.sh`, which compares every method name in develop's `src/xrpld/rpc/MethodNames.h` with the SDK's `Command = "..."` values and is red while a method is neither implemented nor listed in `.ci-config/rpc-methods-ignore.txt` (`name  # category: reason`; categories `admin`, `signing`, `cli`, `deprecated`, `internal`, `covered`, `gap`), or while an entry there is stale. The diff watches only report what moves after their baseline, which is how `get_aggregate_price` went unnoticed; this compares the full lists. `MethodNames.h` and `handlers/Handlers.h` are also in the protocol-watch list |
| `release-watch.yml` | Weekly cron (Mon 06:30 UTC); manual | Checks the CI stand against the latest stable rippled release; when the release reaches the `deb-stable` channel at packages.xrplf.org, rewrites `ARG XRPLD_VERSION` in `Dockerfile.release`, regenerates the stand config (`generate-amendments.sh`), smoke-tests it and opens a bump PR (via GitHub App `RELEASE_WATCH_APP_ID`/`RELEASE_WATCH_APP_PRIVATE_KEY` or `RELEASE_WATCH_PAT`; falls back to a `release-watch` issue comment) |
| `definitions-watch.yml` | Weekly cron (Mon 07:00 UTC); manual | Raises the nightly stand and diffs `definitions.json` against its `server_definitions`; red when the node has fields the SDK lacks. Its horizon is the nightly pin, so a stale pin makes it report on the past |
| `devnet-coverage.yml` | Manual only | Runs the coverage-oriented integration classes (`TestIBatchInnerTypes`, `TestISponsoredTypes`, `TestIAMMMpt`, XChain, Sponsorship, Batch) against devnet or testnet with faucet funding; feeds the XRPL Foundation amendment dashboard. Never part of CI |
| `nightly-pin-watch.yml` | Weekly cron (Mon 05:00 UTC); manual | Checks the nightly `xrpld` pin against the nightly apt channel; once it is older than `MAX_PIN_AGE_DAYS` (21), runs `bump-nightly-pin.sh`, starts the stand on the new pin, and opens a bump PR (same credential ladder as release-watch; falls back to a `nightly-pin-watch` issue comment) |

Integration tests do **not** run on PRs into `dev` — `dev` uses a GitHub merge queue, and the `integration` job runs on the `merge_group` event against the merge result before the merge lands ("Merge when ready" button). Promotion PRs into `release` still run the full suite directly. Nor do they run on the push to `dev` that follows a queued merge: that is the commit the queue has just tested. The push to `release` still runs them, and `nuget.release.yml` publishes only once that run is green (its `ci` job waits for it). The suite runs in three `integration-shard` jobs, each on its own runner with its own stand. `.ci-config/integration-shards.txt` assigns classes to shards 1 and 2 by measured run time, and shard 3 takes every other `TestI` class, so a new class is never left out. `.ci-config/integration-shard-filter.sh <n>` prints a shard's `--filter`. The `integration` job, which is the required check, is green when every shard is. MSTest runs one class at a time per worker, so a long class also bounds its shard: split a class whose tests take minutes each, as `TestIPreview*` is split. New pushes to a PR cancel its in-flight CI run (`concurrency`, PR events only). The NuGet packages and the stand image's layers come from the GitHub Actions cache. A run reads only its own ref's cache and dev's, and merge-queue refs are new each time, so the `stand-image` job saves the layers on every push to `dev`, where the queue can read them. The image is still built from `Dockerfile.release` on every run, so a changed pin misses the cache from its line on.

### Release Process

1. Ensure all tests pass on `dev`
2. Bump `<PackageVersion>` **only in the packages that actually changed** — not in all four. The base packages (`Xrpl.AddressCodec`, `Xrpl.BinaryCodec`, `Xrpl.Keypairs`) are consumed via `ProjectReference`, so a `Xrpl` package built at a newer version keeps depending on the already published base packages at their existing version. Leaving an untouched package behind is correct, not an oversight. Check with `git diff --stat origin/release...origin/dev -- Base/` before deciding
3. Choose the bump from the nature of the change: patch for a bugfix with no contract change, minor otherwise
4. Add the entry to `CHANGES.md`. Head it `## <version> <date>` with the full four-component version and the date as `dd/mm/yyyy` (`## 11.6.1.0 22/09/2026`) — step 6 matches on that heading and fails without it. **The entry records what changed, and that is all it is for**: name the types, members, files and tests that moved. Leave out what the old shape cost a consumer, compatibility essays, and lines stating what did *not* change, such as a package that kept its version. A breaking change is the exception worth spelling out — say what breaks and what the migration is
5. Merge `dev` → `release`
6. The push to `release` runs `nuget.release.yml`, which does the rest by itself: it waits for `.NET CI` on the same commit (about 13 minutes) and stops if that run is not green, then packs, pushes to GitHub Packages and to NuGet.org (Trusted Publishing over OIDC), and **creates the tag and the GitHub release**. The tag is `<PackageVersion>` from `Xrpl/Xrpl.csproj` under NuGet's normalisation — `11.6.1.0` is tagged `v11.6.1`, a non-zero fourth component is kept — and the body is the `## <version> <date>` section of `CHANGES.md`, which is why step 4 has to stamp the heading with the full four-component version or the workflow fails there
7. Verify, do not create. `gh release view v<version>` and the [nuget.org flat container](https://api.nuget.org/v3-flatcontainer/xrpl/index.json) — indexing lags the push by a few minutes. Tagging by hand is unnecessary, and a release step that has already run is skipped rather than repeated, so a re-run of the workflow finishes cleanly

### NuGet Packages Published

- `Xrpl` (main package)
- `Xrpl.AddressCodec`
- `Xrpl.BinaryCodec`
- `Xrpl.Keypairs`

## Code Generation

The `Tools/GenerateEnums/` project generates C# enums from rippled's binary codec definitions.
Source definitions are used to keep `Xrpl.BinaryCodec.Enums` in sync with the latest rippled protocol changes.
Generated files have `*.Generated.cs` suffix.

Reference: [xrpl-codec-gen](https://github.com/RichardAH/xrpl-codec-gen)

## Development Notes

- Documentation language: English. Prose, headings, and examples in English; do not mix languages
- Commit messages are English too — subject and body. This repository is public, and its history is part of what a reader sees; 36 of the 562 subjects on `dev` are Russian, and no more are to be added
- No `Directory.Build.props` or `global.json` — versions are managed per `.csproj`
- No centralized package management — each project specifies its own NuGet versions
- `test.runsettings` configures MSTest parallel execution at class level
- `.editorconfig` is minimal (primarily CS8632 suppression)
- The library uses `System.Text.Json` (not Newtonsoft.Json) for serialization
