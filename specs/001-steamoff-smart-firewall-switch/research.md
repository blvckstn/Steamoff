# Phase 0 Research

## R1. Calling `INetFwPolicy2` from C# without a COM type-library reference
**Decision**: Use late-bound COM via `Type.GetTypeFromProgID("HNetCfg.FwPolicy2")`
+ `Activator.CreateInstance` + `dynamic`, wrapped in a thin
`ComFirewallService` adapter. Each rule is created via
`Type.GetTypeFromProgID("HNetCfg.FWRule")`.
**Rationale**: Avoids adding a NuGet package or generating an interop assembly
(`Interop.NetFwTypeLib.dll`), which keeps the single-file publish simpler and
avoids architecture (x86/x64) mismatches. `dynamic` dispatch over the
`INetFwPolicy2`/`INetFwRules`/`INetFwRule` COM interfaces is a well-established
pattern (same one PowerShell's `New-NetFirewallRule` ultimately wraps via the
`MSFT_NetFirewallRule` CIM provider, but COM is more direct and dependency-free
from .NET).
**Alternatives considered**: `NetFwTypeLib` interop NuGet package (adds a
native-ish dependency and architecture coupling); shelling to
`netsh advfirewall firewall add rule ...` (loses structured read-back of rule
state, requires careful argument escaping — kept only as documented fallback
`NetshFirewallBackend` per ASSUMPTIONS A2).

## R2. Target Framework Moniker given installed SDKs
**Finding**: `dotnet --list-sdks` shows only `10.0.300`; `--list-runtimes`
shows `Microsoft.WindowsDesktop.App` 6.0.16 and 10.0.8 (no 8.0.x desktop
runtime). The .NET 8 SDK workload packs (`Microsoft.WindowsDesktop.App.Ref`
8.0.x, `Microsoft.NETCore.App.Ref` 8.0.x) are NuGet packages that the .NET 10
SDK can still restore and build against for a `net8.0-windows` TFM, and
self-contained publish downloads the matching 8.0 runtime packs for `win-x64`.
**Decision**: Target `net8.0-windows` as specified (LTS). If restore of the
8.0 ref packs fails in this network environment, the documented one-line
mitigation is to change `<TargetFramework>` to `net10.0-windows` in all four
`.csproj` files (see ASSUMPTIONS A1) — no architectural impact.

## R3. Task Scheduler automation without extra dependencies
**Decision**: Shell out to `schtasks.exe /Create /TN Steamoff /SC ONLOGON
/RL HIGHEST /TR "<quoted exe> --tray" /F` using
`ProcessStartInfo.ArgumentList` (never string-concatenated `cmd /c`), and
`schtasks.exe /Query /TN Steamoff /XML` to verify path/privilege/user, and
`schtasks.exe /Delete /TN Steamoff /F` to remove. All arguments are passed as
discrete array elements — no shell interpretation, no injection surface.
**Alternatives considered**: `Microsoft.Win32.TaskScheduler` NuGet (extra
dependency for something `schtasks.exe` already does safely and is present on
every Windows install).

## R4. Tray notifications without WinRT/cloud toast services
**Decision**: `System.Windows.Forms.NotifyIcon.ShowBalloonTip` (WinForms
interop is allowed inside a WPF app via `System.Windows.Forms` reference) for
local balloon notifications — zero network calls, works on Win10/11.
**Alternatives considered**: `Microsoft.Toolkit.Uwp.Notifications` (adds a
package + WinRT activation complexity for marginal visual benefit).

## R5. Generating colored tray icons
**Decision**: Ship four pre-baked `.ico` resources (green/red/orange/gray) plus
one read-only variant, embedded as resources, swapped at runtime by the
`TrayService`. Simpler and more reliable across DPI settings than runtime
`DrawingVisual` rendering.
**Alternatives considered**: Runtime `RenderTargetBitmap` generation — more
code, more failure surface, no real visual benefit for flat-color status dots.

## R6. MVVM toolkit
**Decision**: Hand-roll a minimal `ObservableObject` (`INotifyPropertyChanged`
+ `SetProperty`) and `RelayCommand`/`AsyncRelayCommand` (`ICommand`) in
`Steamoff.Core/Mvvm`. Keeps the dependency graph at zero third-party NuGet
packages for the whole solution (besides xUnit in tests), which simplifies
single-file publish and offline restore.
**Alternatives considered**: `CommunityToolkit.Mvvm` (excellent, but adds a
package dependency the brief doesn't require and that complicates an
offline-leaning build).
