# Chromium Tray Updater v2 (.NET 10 WinForms)

A native, low-memory (~15 MB RAM) Windows system-tray application that replaces the legacy `chrupd` scheduled task. It supports **Hibbiki Chromium**, **Ungoogled-Chromium**, and **Thorium Browser** with automatic **Dual-Source Failover** between official GitHub Releases APIs and the rate-limit-free `chromiumbuilds.org` JSON API.

## Included Features

1. **Background Auto-Update (`AutoUpdate`)**: Periodically polls for newer builds and installs them silently.
2. **Windows System Tray Notifications (`EnableNotifications`)**: Optional balloon/toast alerts for available updates, deferred updates, and completed installations.
3. **Minimize-to-Tray on Close (`MinimizeToTrayOnClose`)**: Clicking the window `X` hides the app to the tray instead of terminating the background scheduler.
4. **Launch on Windows Startup (`StartWithWindows`)**: Automatically registers `"HibbikiUpdater.exe" --minimized` in `HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run`.
5. **Manual Controls**: Dedicated **Check Only** and **Force Update Now** buttons (available both in the main UI and the right-click tray menu).
6. **Live Artifact Telemetry**: Displays local installed version, remote artifact version, SHA-256 verification status, artifact publication timestamp, active feed source, and last check time.
7. **Updater safeguards**:
   - **Active Process Guard (`DeferIfChromiumRunning`)**: Detects if `chrome.exe` or `thorium.exe` is currently open. When enabled, background updates are queued and a 60-second watchdog automatically installs the update the moment you close the browser.
   - **Required SHA-256 Checksum Verification**: Blocks installation when a release has no valid SHA-256 digest or the downloaded installer does not match it.
   - **Dual-Source Failover (`ReleaseProviderService`)**: Queries official GitHub Releases APIs first and automatically falls back to `https://chromiumbuilds.org/api/v1/windows.json` and `https://chromiumbuilds.org/api/v1/latest.json` if GitHub rate-limits your IP. Also includes a checkbox to prefer `chromiumbuilds.org` directly.
   - **Local Installed Version Detection (`LocalChromiumService`)**: Reads `FileVersionInfo.ProductVersion` directly from `%LocalAppData%\Chromium\Application\chrome.exe` (or `Thorium\Application\thorium.exe`).
8. **Multi-Build Dropdown (`ChromiumFlavor`)**: Switch seamlessly between **Hibbiki Chromium**, **Ungoogled-Chromium**, and **Thorium Browser (AVX2)**.

## Opening in JetBrains Rider or Visual Studio
1. Extract `HibbikiUpdater_Solution-v2.zip`.
2. Open `HibbikiUpdater.sln` in **JetBrains Rider** or **Visual Studio 2022+**.
3. Run (`F5`) or compile a single-file executable via PowerShell:
   ```powershell
   dotnet publish src/HibbikiUpdater/HibbikiUpdater.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
   ```

## Tests

Run the unit and service integration tests with:

```powershell
dotnet test HibbikiUpdater.sln
```

## GitHub Releases

The source is published at
[github.com/hantersuxx/HibbikiUpdater](https://github.com/hantersuxx/HibbikiUpdater).
To publish a downloadable Windows release, update the project version in
`src/HibbikiUpdater/HibbikiUpdater.csproj`, then create and push a matching tag
from the repository root. For example, for version `2.0.0`:

```powershell
git tag v2.0.0
git push origin v2.0.0
```

The `.github/upgrades` working artifacts are intentionally excluded from source
control. The repository is public, so anyone can download its source and releases.

The GitHub Actions workflow runs the test suite and then publishes a self-contained
Windows x64 ZIP whenever a version tag is pushed. When the workflow succeeds, the
downloadable ZIP and generated release notes appear under the repository's
**Releases** page. For each later release, update the project version, then create
and push a tag with the matching version (for example, `v2.1.0`).

## Optional project support

The app includes a small, optional support row with PayPal and copy-to-clipboard
links for Ethereum and Bitcoin. Supporting is entirely voluntary and has no effect
on app features. The defaults can be customized in
`%LocalAppData%\HibbikiUpdater\config.json`:

```json
{
  "SupportUrl": "https://paypal.me/AliaksandrJoya",
  "EthereumAddress": "0xfdC3A6Dcd29a98d4c1FB0c8EF1Ab643809F5a791",
  "BitcoinAddress": "bc1q9d7qsevsnycdyxt2972n6482hked2m2wnprmlr"
}
```

**Other easy options:** GitHub Sponsors adds a native sponsor button to the repo
(if available for your account); Ko-fi and Buy Me a Coffee provide hosted tip pages;
Open Collective is useful if you want transparent community finances. A hosted
page can consolidate PayPal and card payments, while keeping the crypto addresses
as optional alternatives.

| PayPal | Ethereum | Bitcoin |
|---|---|---|
| [paypal.me/AliaksandrJoya](https://paypal.me/AliaksandrJoya) | `0xfdC3A6Dcd29a98d4c1FB0c8EF1Ab643809F5a791` | `bc1q9d7qsevsnycdyxt2972n6482hked2m2wnprmlr` |
| — | ![Ethereum donation QR](docs/donate/ethereum-qr.png) | ![Bitcoin donation QR](docs/donate/bitcoin-qr.png) |
