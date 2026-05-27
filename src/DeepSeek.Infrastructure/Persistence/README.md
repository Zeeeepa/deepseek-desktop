# Infrastructure persistence

JSON and file-backed stores for DeepSeek Desktop.

| Store | File / location |
|-------|-----------------|
| `ConfigStore` | `%LocalAppData%/deepseek_desktop/config.json` |
| `DsdSessionConfigStore` | `session-config.json` |
| `DsdContextManagementConfigStore` | `context-management-config.json` |
| `DsdApiSessionStore` | sessions under config directory |

Stores coupled to ApiManagement services remain in `DeepSeek.Core` until ports are extracted.

| Store | Location | Notes |
|-------|----------|-------|
| `DsdApiRequestLogStore` | `Services/` | Uses `ProviderAccountStore` + `DsdApiIpcEventHub` (shell) |
| `DsdAppLogStore` | `Services/` | Publishes `logs:newLog` via IPC event hub |
