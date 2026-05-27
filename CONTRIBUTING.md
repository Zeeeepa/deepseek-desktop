# Contributing to DeepSeek Desktop

Thank you for contributing. This project follows a **layered desktop architecture**; see [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for the full picture.

## Repository layout

| Path | Layer | Allowed |
|------|-------|---------|
| `src/DeepSeek.Domain/` | Domain | Entities, value objects, port interfaces — **no** IO, **no** WPF |
| `src/DeepSeek.Infrastructure/` | Infrastructure | ConfigStore, JSON/SQLite persistence, HTTP clients |
| `src/DeepSeek.Application/` | Application | IPC handlers, use-case orchestration, DTOs |
| `DeepSeekBrowser.csproj` (Desktop shell) | UI | WPF, WebView2 hosts, tray, OAuth windows |
| `Services/` (transition) | Shell + bridge | Web hosts, IPC facade — shrink over time |
| `DeepSeek.Core/` (transition) | Harness + API services | Agent Harness, provider routing — references Domain/Infrastructure |
| `Assets/dsd-api/` | Web UI (built) | Published API console — **do not edit minified `assets/*.js` by hand** |
| `Assets/dsd-api-ui/` | Web UI (overlays) | Desktop-specific inject scripts copied at build |
| `web/dsd-api-renderer/` | Web UI (source) | Chat2API renderer fork — preferred place for console changes |
| `Assets/agent/` | Web UI | Agent panel scripts and styles |
| `Assets/inject/` | Web UI | Scripts injected into chat.deepseek.com |
| `scripts/` | Build | `build.ps1`, UI build, release packaging |
| `DeepSeek.Core.Tests/` | Tests | Unit tests for Core/Domain/Infrastructure |
| `DeepSeek.Application.Tests/` | Tests | IPC handler tests |

## Layer rules

1. **UI (WPF + web bundles)** must not implement business rules (provider deduplication, OAuth validation, load-balancing logic, context-management persistence). Call Application/IPC or Core services instead.
2. **Application** must not reference `System.Windows` or WebView2 types. Use ports (e.g. `IDesktopUiService`) implemented in Desktop for dialogs.
3. **Infrastructure** owns all file paths under `%LocalAppData%` / config directories via `ConfigStore` and `*Store` types.
4. **Preload** ([`Assets/dsd-api/webview-preload.js`](Assets/dsd-api/webview-preload.js)) only exposes whitelisted IPC channels.

## Web / API console changes

**Preferred workflow:**

1. Edit renderer source under `web/dsd-api-renderer/` (or upstream Chat2API renderer with documented patches).
2. Run `scripts/build-dsd-api-ui.ps1` (or full `build.ps1`).
3. Verify `node --check` passes on generated bundles (build script runs this).

**Avoid:**

- Regex editing minified `Assets/dsd-api/assets/*.js` in PowerShell (fragile; caused production SyntaxErrors).
- Duplicating fixes in both `Assets/dsd-api` and `Assets/agent/dsd-api` — sync is automatic via `sync-agent-dsd-api.ps1`.

**Overlay scripts** (`ds-ui-trim.js`, `ds-i18n-zh.js`, …) may adjust presentation only, not authoritative business state.

## C# changes

- Place new persistence in `src/DeepSeek.Infrastructure/Persistence/`.
- Place new IPC handlers in `src/DeepSeek.Application/Ipc/<Domain>/`.
- Keep `RootNamespace` as `DeepSeekBrowser` where needed for compatibility during migration.
- Run `dotnet test` before opening a PR.

## Build and verify

```powershell
.\build.ps1
```

Optional architecture guard (also run in CI-friendly mode):

```powershell
.\scripts\verify-architecture.ps1
```

## Commits and PRs

- One logical change per PR when possible (e.g. IPC handler extraction separate from Harness changes).
- Update `docs/ARCHITECTURE.md` if layer boundaries or IPC channels change.
- Bump `EmbeddedUiBuild` in `AppNavigation.cs` when shipping web asset changes.

## Questions

Open a [GitHub Issue](https://github.com/fanstars2318/deepseek-desktop/issues) for design questions before large refactors.
