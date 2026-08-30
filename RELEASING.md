# Releasing Updates

How to ship a new version to all customers. The app auto-updates: each client
checks `version.json` on the `main` branch at startup, and if a newer version is
listed, prompts the customer to install it. The installer overwrites the app in
place and relaunches it — **customers never reinstall manually**, and their saved
settings/mappings/history (stored under `%LocalAppData%\DtfOrderAutomation`) are
preserved.

## How it fits together

| Piece | Role |
|---|---|
| `AppVersion.Current` (`AppVersion.cs`) | Version baked into the build; what each client compares against |
| `version.json` (repo root, on `main`) | The "release feed" every client polls |
| `installer/installer.iss` | Inno Setup script that builds `DTF.Setup.exe` |
| GitHub Release asset | Where `DTF.Setup.exe` is hosted for download |

## One-time prerequisites (on a Windows machine)

- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- [Inno Setup 6](https://jrsoftware.org/isdl.php) (provides `ISCC.exe`)

## Release steps

For a release going from, say, `1.0.0` → `1.0.1`:

### 1. Bump the version in the app
Edit `DTF Win/DtfOrderAutomation/AppVersion.cs`:
```csharp
public const string Current = "1.0.1";
```
Use a 3-part `major.minor.patch` version. This must be **higher** than the
previous release or clients won't offer the update.

### 2. Publish the app
From the repo root, on Windows:
```bat
dotnet publish "DTF Win\DtfOrderAutomation\DtfOrderAutomation.csproj" ^
  -c Release -r win-x64 --self-contained true ^
  -p:WindowsAppSDKSelfContained=true ^
  -o installer\publish
```
- `--self-contained true` bundles the **.NET runtime**.
- `-p:WindowsAppSDKSelfContained=true` bundles the **Windows App SDK runtime**.

Both are required so a customer can run the app with nothing else installed —
this app is unpackaged (`WindowsPackageType=None`), so without these the customer
would have to install those runtimes by hand.

### 3. Build the installer
```bat
ISCC.exe installer\installer.iss /DMyAppVersion=1.0.1
```
This produces `installer\Output\DTF.Setup.exe`.

> Test it once locally: run `DTF.Setup.exe`, confirm the app installs to
> `%LocalAppData%\Programs\DtfOrderAutomation`, launches, and that your existing
> settings are still there.

### 4. Create a GitHub Release
- Tag: `v1.0.1`
- Upload `DTF.Setup.exe` as a release asset.
- Copy the asset's download URL, which looks like:
  `https://github.com/ryanvanbelkum/dtf-order-automation/releases/download/v1.0.1/DTF.Setup.exe`

### 5. Update `version.json` on `main`
```json
{
  "version": "1.0.1",
  "download_url": "https://github.com/ryanvanbelkum/dtf-order-automation/releases/download/v1.0.1/DTF.Setup.exe",
  "release_notes": "What changed in this version."
}
```
Commit and push to `main`. **This is the switch that releases the update** — the
moment it's on `main`, every customer is offered `1.0.1` on their next launch.

### 6. Verify
Open an already-installed copy of the app (an older version). Within a few
seconds of launch you should get the "Update Available" prompt. Accept it and
confirm it updates in place and reopens.

## Notes

- **Order matters:** publish → build installer → upload to Release → *then*
  update `version.json`. If you push `version.json` before the asset exists,
  clients will try to download a URL that 404s.
- **The three versions should agree:** `AppVersion.Current`, the
  `/DMyAppVersion` you pass to ISCC, and the `version` in `version.json`.
- **SmartScreen warning:** because the `.exe` isn't code-signed, Windows
  SmartScreen may show a one-time "Windows protected your PC → More info → Run
  anyway" notice on a customer's first run of a downloaded installer. To remove
  it, buy a code-signing certificate and sign `DTF.Setup.exe` (add a
  `SignTool` directive to the `.iss`) — no architecture change needed.
- **Rollback:** to pull a bad release, point `version.json` back at the previous
  version/URL. (Clients won't downgrade, but new installs and un-updated clients
  get the good one.)
