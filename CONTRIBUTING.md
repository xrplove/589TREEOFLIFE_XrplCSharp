# Contributing

## Set up your dev environment

### Requirements

We use Visual Studio 2022+ or JetBrains Rider for development.
You must have .NET 10 SDK installed. You can check your `dotnet` version with:

```bash
dotnet --version
```

### Set up

1. Clone the repository
2. `cd` into the repository
3. Restore and build:

```bash
dotnet restore
dotnet build
```

### Unit Tests

```bash
dotnet test --verbosity normal --settings test.runsettings --filter "TestU"
```

### Integration Tests

For integration tests, we use a `rippled` node in standalone mode with an automatic ledger acceptor. To set this up, you need [Docker](https://docs.docker.com/get-docker/) and Docker Compose installed.

```bash
# start rippled + ledger-acceptor via Docker Compose
docker compose -f .ci-config/docker-compose.ci.yml up -d

# wait for rippled to be healthy, then run integration tests
dotnet test Tests/Xrpl.Tests/Xrpl.Tests.csproj --verbosity normal --settings test.runsettings --filter "TestI"

# stop containers when done
docker compose -f .ci-config/docker-compose.ci.yml down
```

For amendment activation details, the nightly-develop standalone environment for unreleased amendments, `temDISABLED` troubleshooting and genesis account funding, see the [Standalone Node Guide](DocFx/StandaloneNode-Guide.md).

## Generate reference docs

You can see the complete reference documentation at [`XrplCSharp` docs](https://staticbit-io.github.io/XrplCSharp/). You can also generate them locally:

```bash
dotnet tool install -g docfx
docfx DocFx/docfx.json
```

This generates documentation into the `docs/` directory.

## Update `DefinitionsJson`

Use [this repo](https://github.com/RichardAH/xrpl-codec-gen) to generate a new `DefinitionsJson` file from the rippled source code. Instructions are available in that README.

## Release process

### Editing the Code

* Your changes should have unit and/or integration tests.
* Your code should pass all the tests on GitHub Actions (unit and integration tests).
* Open a PR against `dev` and ensure that all CI passes.
* Get a code review from one of the maintainers.
* Merge your changes.

### Release

1. Ensure that all tests passed on the last CI that ran on `dev`.
2. Update the version in all `.csproj` files (`Xrpl`, `Xrpl.AddressCodec`, `Xrpl.BinaryCodec`, `Xrpl.Keypairs`), including a package with no change of its own. The release skips a version nuget.org already has, so a package left behind keeps the dependencies it was published with. `TestUPackageVersions` fails when the versions differ. `Xrpl.X402` and `Xrpl.X402.AspNetCore` have versions of their own and no such check: they keep requiring the `Xrpl` they were published with until their version moves.
3. Update `CHANGES.md` with the new version and changelog.
4. Merge `dev` into `release` (the push to `release` publishes the NuGet packages and deploys the documentation).
5. The NuGet publish workflow will start automatically on push to `release`.
6. Create a GitHub release with the appropriate tag.

## Mailing Lists

We have a low-traffic mailing list for announcements of new `XrplCSharp` releases. (About 1 email every couple of weeks)

+ [Subscribe to xrpl-announce](https://groups.google.com/g/xrpl-announce)

If you're using the XRP Ledger in production, you should run a [rippled server](https://github.com/ripple/rippled) and subscribe to the ripple-server mailing list as well.

+ [Subscribe to ripple-server](https://groups.google.com/g/ripple-server)
