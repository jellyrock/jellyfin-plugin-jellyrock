# Development

Developer and maintainer docs for the JellyRock Companion plugin. For install and feature docs, start
at the [top-level README](../../README.md).

## Build

No local .NET SDK needed. Build in the official SDK container:

```bash
docker run --rm -v "$PWD":/src -w /src --user "$(id -u):$(id -g)" \
  -e HOME=/tmp -e DOTNET_CLI_HOME=/tmp \
  mcr.microsoft.com/dotnet/sdk:9.0 \
  dotnet publish Jellyfin.Plugin.JellyRock/Jellyfin.Plugin.JellyRock.csproj -c Release -o /src/publish
# -> ./publish/Jellyfin.Plugin.JellyRock.dll
```

Only the plugin DLL is emitted; the `Jellyfin.*` assemblies and the ASP.NET Core shared framework are
supplied by the server at runtime (`ExcludeAssets=runtime` / `FrameworkReference`). The build targets
`net9.0` against the Jellyfin 10.11.0 API floor, with StyleCop and the analyzer suite enforced as errors.

## Test

Same container, the test project:

```bash
docker run --rm -v "$PWD":/src -w /src --user "$(id -u):$(id -g)" \
  -e HOME=/tmp -e DOTNET_CLI_HOME=/tmp \
  mcr.microsoft.com/dotnet/sdk:9.0 \
  dotnet test Jellyfin.Plugin.JellyRock.Tests/Jellyfin.Plugin.JellyRock.Tests.csproj -c Release
```

## More

- [Manual sideload](sideload.md): drop an unreleased build onto a server by hand.
- [Repo layout](layout.md): the file tree and what each piece does.
- [Releasing](releasing.md): the maintainer release runbook.
