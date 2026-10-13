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

## Server compatibility check

One build serves every server line from the `targetAbi` floor up: the plugin is compiled against
10.11.0 and loads unchanged on 12.x. That holds only while each newer server still has every type and
member the plugin uses, so a check proves it against the real server binaries:

```bash
scripts/check-server-abi.sh            # the newest release of every supported line
scripts/check-server-abi.sh 12.2       # one release
scripts/check-server-abi.sh --list     # which releases "supported" means today
```

It downloads each release from repo.jellyfin.org (cached in `~/.cache/jellyfin-plugin-jellyrock`) and
runs `tools/ServerAbiCheck` against the built plugin. That tool reads the plugin with Mono.Cecil and
fails when the server lacks:

- an assembly, type, method or field the plugin references (the server would throw
  `TypeLoadException` or `MissingMethodException`);
- a member the plugin's classes must implement, such as a method newly added to `ISessionController`
  or a new abstract member on `BasePlugin` (the type would fail to load);
- the member a plugin `override` overrides (the plugin loads, but its method is never called).

No local .NET SDK is needed: without `dotnet` on the path, the script runs the tool in the SDK
container.

**Where it runs.** CI runs it on every push and pull request, plus a control: the plugin must *not*
bind to 10.10.7, so a check that stops seeing breaks fails the build. `server-abi-watch.yml` runs it
every Monday, so a new Jellyfin release is checked without a push; a failure opens an issue titled
"Server compatibility check failed", and the next pass closes it. GitHub disables a public
repository's scheduled workflows after 60 days with no repository activity; re-enable it from the
Actions tab.

**When it fails.** A `problems` line names what the server no longer has. The fix is a plugin change
that avoids it, or a decision to raise the floor (`targetAbi` in `build.yaml` and the `Jellyfin.*`
package versions in the csproj together); older servers then stay on the last release that supports
them. An error that the floor line has no release means Jellyfin has moved that line to its archive,
which calls for the same decision.

## More

- [Manual sideload](sideload.md): drop an unreleased build onto a server by hand.
- [Repo layout](layout.md): the file tree and what each piece does.
- [Releasing](releasing.md): the maintainer release runbook.
