# LoadoutBuffs

[![CI](https://github.com/algo7/LoadoutBuffs/actions/workflows/ci.yml/badge.svg)](https://github.com/algo7/LoadoutBuffs/actions/workflows/ci.yml)

A client-side [BepInEx](https://github.com/BepInEx/BepInEx) mod for Valheim: **buffs** of extra "while worn" effects
and stats per equipment slot, picked in an in-game window (built with [Jötunn](https://github.com/Valheim-Modding/Jotunn)).
What it does for players, the buffs file format and the console commands are in [package/README.md](package/README.md),
which is also the mod's Thunderstore page. Changes: [CHANGELOG.md](CHANGELOG.md).

## Building

Needs the .NET SDK 8 (tests) and 10 (the ILRepack tool), and Valheim's game DLLs for compile-time references: by default
from a local Steam install (`~/.local/share/Steam/steamapps/common/Valheim`). BepInEx, HarmonyX and Jötunn come from NuGet
(`nuget.config` adds the BepInEx feed).

```sh
make test       # unit tests (no game needed at runtime, only its DLLs as references)
make package    # → Algo7-LoadoutBuffs-<version>.zip (manifest.json is generated from LoadoutBuffs.csproj)
make test MANAGED_DIR=/path/to/Valheim/valheim_Data/Managed    # game DLLs from elsewhere
```

Install the zip with r2modman ("Import local mod"), or copy `LoadoutBuffs.dll` into `BepInEx/plugins/`.
The Makefile looks for the SDK in `~/.dotnet`; pass `DOTNET=dotnet` if it's on your PATH.

## CI / CD

- **CI** (`.github/workflows/ci.yml`, every push and pull request): build, unit tests and the zip as an artifact. The
  game DLLs come from Valheim's free dedicated server (Steam app 896660, anonymous login): only its `Managed` DLLs, fetched
  with [DepotDownloader](https://github.com/SteamRE/DepotDownloader) (pinned, checksum-verified) in a few seconds;
  nothing from the game is committed.
- **Release** (`.github/workflows/release.yml`): pushing a tag `vX.Y.Z` that matches the project version (and a released
  `## X.Y.Z` section in CHANGELOG.md) builds and tests again, then, after approval in the `thunderstore` environment,
  creates the GitHub Release and publishes the same zip to Thunderstore (`tcli`, secret `TCLI_AUTH_TOKEN`).
- **Dependabot**: NuGet packages and GitHub Actions, weekly.

## Layout

```
LoadoutBuffs.csproj        net48 plugin; ILRepack merges YamlDotNet; Package target (zip + generated manifest)
src/                       plugin: entry, game glue, commands (lb_stats report), parry help
src/Bundles/               buffs: file, rules, stats, runtime, Harmony hooks, buff, window
example/                   first-run buffs file (embedded in the DLL)
package/                   Thunderstore README and icon
tests/                     unit tests (net8.0)
thunderstore.toml          Thunderstore publishing settings (tcli)
.github/                   workflows, Dependabot, the CI helper that fetches the game DLLs
```

## License

[MIT](LICENSE)
