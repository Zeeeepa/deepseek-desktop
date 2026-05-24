# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased] — template

> Copy this section when preparing the next release.

### Added

- <!-- e.g. New feature or capability -->

### Changed

- <!-- e.g. Behavior or UI adjustment -->

### Fixed

- <!-- e.g. Bug fix with issue link #123 -->

### Removed

- <!-- e.g. Deprecated feature removed -->

### Security

- <!-- e.g. Dependency or auth-related fix -->

### Known issues

- <!-- e.g. Workaround or limitation -->

---

## [1.2.1] - 2026-05-24

### Changed

- README / DISCLAIMER：架构说明改为 **DeepSeek-TUI**，移除 Qwen Code 文档链接
- Skills 路径说明改为 `~/.deepseek/skills`
- 新增 `CHANGELOG.md`

---

## [1.2.0] - 2026-05-24

### Added

- **DeepSeek.Core** shared library with unit tests (`DeepSeek.Core.Tests`, 21 tests)
- **DeepSeek-TUI v0.8.39** integration via git submodule (`third-party/DeepSeek-TUI`)
- Agent panel with embedded **Settings** and **API Management** (Chat2API UI)
- **Chat2ApiIpcBridge** for IPC between embedded UI and desktop config
- **Desktop ↔ TUI stack sync**: `DeepSeekTuiConfigSync`, integration file, `internal://desktop/v1` channel
- **Desktop stack banner** in API Management (`ds-desktop-stack.js`): login state, TUI URL, sync actions
- Chinese-only Chat2API UI (removed About page and language switcher)
- Build and verification scripts: `build-chat2api-ui.ps1`, `verify-integration.ps1`, `smoke-test.ps1`, `agent-tui-smoke.ps1`
- Optional **WinUI 3** shell (`DeepSeek.Desktop`, `build.ps1 -WinUi`)
- Work mode sync between main chat WebView and desktop (overlay / coordinator)
- TUI process cleanup on application exit
- Third-party disclaimer and expanded README

### Changed

- Default shell is **WPF** (`DeepSeekBrowser.csproj`); WinUI is experimental
- API Management opens **inside Agent panel** instead of a standalone popup on startup
- External OpenAI-compatible API is **opt-in** (disabled by default)
- README rewritten for current architecture and build flow

### Fixed

- Settings embed stuck on “检测中…” when iframe bridge was misdetected
- Chat2API About page and locale patches in build script (largest bundle selection)
- Startup API console warmup flash removed

### Removed

- Standalone `Chat2ApiManagementWindow` (replaced by embedded panel)
- Chat2API About page and language settings from shipped UI

### Known issues

- Some Chat2API UI features remain stubbed (logs/stats, multi-provider CRUD, tool calling)
- WinUI 3 shell requires a working Windows App Runtime; WPF is recommended for daily use

[1.2.1]: https://github.com/fanstars2318/deepseek-desktop/compare/v1.2.0...v1.2.1
[1.2.0]: https://github.com/fanstars2318/deepseek-desktop/compare/V1.0.0...v1.2.0
[Unreleased]: https://github.com/fanstars2318/deepseek-desktop/compare/v1.2.1...HEAD
