# Contract: Path Normalization & Steam Path Validation

## `IPathNormalizationService.NormalizeRawPath(string rawPath) -> string`
Pure, side-effect-free. Given any of the following raw inputs, returns a
clean Windows path string (no filesystem access performed):

| Input | Output |
|---|---|
| `"  C:\\Games\\Steam  "` | `C:\Games\Steam` |
| `"\"C:\\Games\\Steam\""` | `C:\Games\Steam` |
| `"C:/Games/Steam"` | `C:\Games\Steam` |
| `"C:\\Games\\\\Steam\\\\\\steam.exe"` | `C:\Games\Steam\steam.exe` |
| `"%ProgramFiles(x86)%\\Steam"` | `C:\Program Files (x86)\Steam` (expanded) |
| `"\\\\NAS\\Games\\Steam"` (UNC) | `\\NAS\Games\Steam` (leading `\\` preserved) |

Guarantees:
- Idempotent: `Normalize(Normalize(x)) == Normalize(x)`.
- Never throws on malformed input — returns its best-effort cleaned string.
- Does not resolve `.` / `..` segments or check existence (that's validation).

## `ISteamPathValidator.Validate(string candidatePath) -> SteamPathCheckResult`
Resolution chain (each step feeds the next on success); `wasNormalized`
is `true` when the normalized form differs from the trimmed raw input
(picks `settings.steamPath.normalized` over `settings.steamPath.found` as
the success message for file/folder hits):

1. `NormalizeRawPath(candidatePath)`. Empty/whitespace-only result →
   short-circuits to `SteamPathCheckResult.Empty` (`Status = Empty`,
   `StatusMessageKey = "settings.steamPath.dropHint"`) — no filesystem access.
2. If extension is `.lnk` (case-insensitive): invoke the injected
   `Func<string,string?>` shortcut resolver. `null`/non-existent target →
   `Status = ShortcutUnresolved`, `StatusMessageKey =
   "settings.steamPath.invalid"`. Otherwise recurse into the file-resolution
   branch (step 3) with `successMessageKey = "settings.steamPath.shortcutResolved"`.
3. If the path is an existing **file** (`ResolveExeOrFolder`):
   - name (case-insensitive) is `steam.exe` → `Status = Valid`,
     `NormalizedFolderPath = <parent dir>`, `SteamExePath = <path>`,
     `StatusMessageKey = successMessageKey` (`"settings.steamPath.found"` /
     `"settings.steamPath.normalized"` / `"settings.steamPath.shortcutResolved"`
     depending on the entry path)
   - parent directory missing → `Status = PathNotFound`,
     `StatusMessageKey = "settings.steamPath.notExist"`
   - otherwise (wrong file name) → `Status = WrongExe`,
     `StatusMessageKey = "settings.steamPath.wrongExe"`
4. If the path is an existing **directory** (`ResolveFolder`):
   - contains `steam.exe` → `Status = Valid`,
     `NormalizedFolderPath = <path>`, `SteamExePath = <path>\steam.exe`,
     `StatusMessageKey = successMessageKey`
   - otherwise → `Status = SteamExeNotFound`,
     `NormalizedFolderPath = <path>`,
     `StatusMessageKey = "settings.steamPath.exeNotFound"`
5. Otherwise (nothing exists at the path) → `Status = PathNotFound`,
   `StatusMessageKey = "settings.steamPath.notExist"`

## `ISteamPathValidator.FromInstallation(SteamInstallation installation) -> SteamPathCheckResult`
- `installation.IsValid == false` or empty path → `Status = Empty`,
  `StatusMessageKey = "settings.steamPath.notFoundAuto"`.
- Otherwise → `Status = Valid`, `NormalizedFolderPath = installation.Path`,
  `SteamExePath = installation.SteamExePath`,
  `StatusMessageKey = "settings.steamPath.found"`.

This lets discovered and manually-validated paths render through the same
indicator/status-text bindings.

## Test obligations (see tasks.md §10)
- Each row of the normalization table above as an individual test case.
- Each branch of the resolution chain (folder / `steam.exe` path / wrong exe /
  missing exe / missing path / `.lnk` via a **fake** resolver delegate that
  returns a deterministic target, an existent-but-wrong target, or `null`).
- `FromInstallation` for both the valid and the not-found/empty installation.
