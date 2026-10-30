# Chromium Build Updater

**A small desktop app for checking and installing selected Chromium builds on Windows, macOS, and Linux.**

[![Build, test, and publish](https://github.com/hantersuxx/ChromiumBuildUpdater/actions/workflows/release.yml/badge.svg)](https://github.com/hantersuxx/ChromiumBuildUpdater/actions/workflows/release.yml)
[![Latest release](https://img.shields.io/github/v/release/hantersuxx/ChromiumBuildUpdater?display_name=tag)](https://github.com/hantersuxx/ChromiumBuildUpdater/releases/latest)
![Windows, macOS, Linux](https://img.shields.io/badge/platform-Windows%20%7C%20macOS%20%7C%20Linux-526A8A)
![.NET 10](https://img.shields.io/badge/.NET-10-512BD4)

The app checks upstream release feeds, shows the version and source, and installs an update
only after validating its SHA-256 checksum. You can check manually, enable automatic updates,
or wait for the browser to close before an install.

## Supported builds

Support depends on whether a build has a verifiable upstream release for that operating system:

| Operating system | Available builds | Install format |
|---|---|---|
| Windows x64 | Hibbiki Chromium, Ungoogled Chromium, Thorium | Upstream `.exe` installer |
| macOS x64 / Apple silicon | Ungoogled Chromium | Signed and notarized `.dmg`; Gatekeeper checks before install |
| Linux x64 / ARM64 | Thorium | Per-user `.AppImage` |

Hibbiki does not publish macOS or Linux builds in the checked feeds. The available Ungoogled
Linux binaries are contributor-submitted, so the app does not install them. Thorium Linux
releases are beta builds published by a community contributor; review the upstream release
before choosing them. Other flavor/platform combinations are intentionally unavailable rather
than silently substituting a different browser.

## What it does

- Check manually or on a schedule.
- Optionally install new versions automatically.
- Defer an install while the selected browser is running.
- Verify the exact selected asset with SHA-256 before installation.
- Install Linux AppImages per user, without root or package-manager changes.
- On macOS, validate the app signature and Gatekeeper assessment before replacing the
  per-user application.
- Optionally launch at sign-in using the native startup mechanism for the current OS.
- Keep support links optional and fixed by the project owner.

## Download and run

Download the archive or executable for your platform from
[GitHub Releases](https://github.com/hantersuxx/ChromiumBuildUpdater/releases/latest).

- **Windows:** run `ChromiumBuildUpdater-v*-win-x64.exe`.
- **Linux:** extract the matching `linux-x64` or `linux-arm64` `.tar.gz` and run
  `ChromiumBuildUpdater`.
- **macOS:** extract the matching `osx-x64` or `osx-arm64` `.tar.gz` and run
  `ChromiumBuildUpdater`. The app itself is currently distributed unsigned, so macOS may
  require the usual Gatekeeper approval for software downloaded directly from GitHub.

The app creates default settings on first launch. On Windows, existing settings are migrated
from `%LocalAppData%\HibbikiUpdater\config.json`; the new file is saved under
`%LocalAppData%\ChromiumBuildUpdater\config.json`. macOS and Linux use their standard
per-user configuration directories.

## Build and test

Install the .NET 10 SDK, then run:

```powershell
dotnet build ChromiumBuildUpdater.sln --configuration Release
dotnet test ChromiumBuildUpdater.sln --configuration Release
```

Publish a self-contained single-file app for a runtime identifier:

```powershell
dotnet publish src/ChromiumBuildUpdater/ChromiumBuildUpdater.csproj `
  --configuration Release --runtime win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

Supported release runtime identifiers are `win-x64`, `linux-x64`, `linux-arm64`, `osx-x64`,
and `osx-arm64`.

## Releases and checks

Pull requests and pushes to `main` run the test suite on Windows, macOS, and Linux and build the
desktop app. Pushing a `v*` tag publishes self-contained single-file packages for all five
supported runtime identifiers, but only after every test/build job succeeds.

## Support the project

Support is optional; the app is free to use and donations never unlock or restrict features.

- **PayPal:** [paypal.me/AliaksandrJoya](https://paypal.me/AliaksandrJoya)
- **Ethereum:** `0xfdC3A6Dcd29a98d4c1FB0c8EF1Ab643809F5a791`
- **Bitcoin:** `bc1q9d7qsevsnycdyxt2972n6482hked2m2wnprmlr`

The in-app destinations are fixed and are not user settings.
