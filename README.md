# HibbikiUpdater

### A Windows tray app that checks for and installs updates to Hibbiki Chromium, Ungoogled Chromium, and Thorium.

[![Build and publish release](https://github.com/hantersuxx/HibbikiUpdater/actions/workflows/release.yml/badge.svg)](https://github.com/hantersuxx/HibbikiUpdater/actions/workflows/release.yml)
[![Latest release](https://img.shields.io/github/v/release/hantersuxx/HibbikiUpdater?display_name=tag)](https://github.com/hantersuxx/HibbikiUpdater/releases/latest)
![Windows](https://img.shields.io/badge/platform-Windows%2010%2F11-0078D4)
![.NET 10](https://img.shields.io/badge/.NET-10-512BD4)

**HibbikiUpdater** checks for new browser builds, verifies installers with SHA-256,
and can install updates automatically or wait until the browser is closed.

<p align="center">
  <a href="https://github.com/hantersuxx/HibbikiUpdater/releases/latest"><strong>Download the latest Windows app</strong></a>
  &nbsp;·&nbsp;
  <a href="#getting-started">Getting started</a>
  &nbsp;·&nbsp;
  <a href="#support-the-project">Support the project</a>
</p>

## At a glance

| Browser builds | Safe updates | Runs quietly |
|---|---|---|
| Hibbiki Chromium, Ungoogled Chromium, Thorium (AVX2) | SHA-256 verification, browser-running guard, GitHub/builds API failover | System tray, optional notifications, configurable schedule |

### What it does

- Checks for updates on a schedule or whenever you ask.
- Automatically installs updates, or defers while Chromium is running.
- Verifies installer checksums before installation.
- Shows installed/latest versions, release date, source, and check status.
- Can start with Windows and minimize to the system tray.
- Includes optional PayPal and crypto links; donations never unlock or restrict features.

## Getting started

1. **[Download the latest single-file Windows executable](https://github.com/hantersuxx/HibbikiUpdater/releases/latest).**
2. Run `HibbikiUpdater-v*-win-x64.exe`. It is self-contained; you don't need to install .NET.
3. On first launch, the app creates its default settings at `%LocalAppData%\HibbikiUpdater\config.json`.
4. Choose a browser build and adjust update behavior in the app.

The app targets **Windows x64**. SmartScreen may show a warning for a new or unsigned
release; only download the executable from this repository's Releases page.

## Build from source

Open `HibbikiUpdater.sln` in Visual Studio 2022+ or JetBrains Rider with the .NET 10
SDK installed. To build a self-contained, single-file Windows executable:

```powershell
dotnet publish src/HibbikiUpdater/HibbikiUpdater.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true
```

Run the unit and service integration tests:

```powershell
dotnet test HibbikiUpdater.sln -c Release
```

## Releases

Tests run automatically for pull requests targeting `main`, pushes to `main`, and
version tags. Pushing a `v*` tag publishes a self-contained Windows x64 `.exe` to the
[GitHub Releases page](https://github.com/hantersuxx/HibbikiUpdater/releases), but only
after the test job passes.

## Support the project

If you find HibbikiUpdater useful, you can support its development. Donations are
optional and never unlock or restrict app features.

- **PayPal:** [paypal.me/AliaksandrJoya](https://paypal.me/AliaksandrJoya)

<p align="center">
  <img src="docs/donate/ethereum-qr.png" width="140" alt="Ethereum donation QR code">
  &nbsp;
  <img src="docs/donate/bitcoin-qr.png" width="140" alt="Bitcoin donation QR code">
</p>

**Ethereum:** `0xfdC3A6Dcd29a98d4c1FB0c8EF1Ab643809F5a791`<br>
**Bitcoin:** `bc1q9d7qsevsnycdyxt2972n6482hked2m2wnprmlr`

The in-app support destinations are fixed by the project owner. They aren't user
settings and have no effect on updates.
