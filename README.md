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

Create an empty GitHub repository named `HibbikiUpdater` (do not initialize it
with another README or `.gitignore`), then connect this local repository and push
the initial branch:

```powershell
git add .
git commit -m "Initial HibbikiUpdater source"
git remote add origin https://github.com/YOUR-OWNER/HibbikiUpdater.git
git push -u origin main
```

The `.github/upgrades` working artifacts are intentionally excluded from source
control. For downloadable releases, keep the repository public or grant download
access to the intended audience.

The GitHub Actions workflow runs the test suite and then publishes a self-contained
Windows x64 ZIP whenever a version tag is pushed. Create a version tag and push it:

```powershell
git tag v2.1.0
git push origin v2.1.0
```

When the workflow succeeds, the downloadable ZIP and generated release notes appear
under the repository's **Releases** page. To release a new version, update the
project version, create a new tag, and push that tag.

## Optional project support

The app's **Support this project** link is hidden by default. To show it, add a
`SupportUrl` property containing your HTTPS donation or sponsorship page to
`%LocalAppData%\HibbikiUpdater\config.json`. For example:

```json
{
  "SupportUrl": "https://github.com/sponsors/YOUR-USERNAME"
}
```

The link is optional and does not unlock features or interrupt app use.
