# DSD API Renderer (source)

This folder documents the **canonical build path** for the embedded API management console (`Assets/dsd-api`).

## Source of truth

| Path | Role |
|------|------|
| [`../Chat2API-main/Chat2API-main`](../Chat2API-main/Chat2API-main) (sibling repo) | Upstream Electron renderer (electron-vite) |
| [`../Assets/dsd-api-ui/`](../Assets/dsd-api-ui/) | Desktop overlay scripts (preload companions, i18n, trim) |
| [`../Assets/dsd-api/`](../Assets/dsd-api/) | **Published** bundle copied into the WPF app |

When the sibling Chat2API tree is present, build via:

```powershell
.\scripts\build-dsd-api-ui.ps1 -ForceRebuild
```

Without upstream source, the script reuses the committed bundle under `Assets/dsd-api` and only refreshes overlay scripts + `index.html`.

## Desktop-specific UI rules (implement in renderer source, not regex)

- `providersStore.ensureLoaded()` on Models page mount
- Settings **负载均衡** tab with `LoadBalanceMount` component (no bundle string surgery)
- Route guards for embedded mode: hide `/about`, `/proxy`, `/api-keys`
- Feature flag: `VITE_DSD_EMBEDDED=true` (recommended for fork)

## Do not

- Hand-edit minified files under `Assets/dsd-api/assets/*.js`
- Duplicate fixes in `Assets/agent/dsd-api` — run `scripts/sync-agent-dsd-api.ps1`

See [docs/ARCHITECTURE.md](../docs/ARCHITECTURE.md) and [CONTRIBUTING.md](../CONTRIBUTING.md).
