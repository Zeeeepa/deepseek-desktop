# 从 deepseek-edge 迁移到 deepseek_desktop

1. **日常开发**在 `deepseek_desktop` 进行；`build.ps1` → `publish/DeepSeek.exe`。
2. **不要**从 `deepseek-edge\bin` 或旧 `DDpublish` 启动。
3. **勿再 edge → desktop 同步**：`scripts/sync-to-deepseek-desktop.ps1` 已停用。该脚本曾用 `robocopy /MIR` 镜像，会把**较新的 desktop 覆盖成较旧的 edge**，并删除 desktop 独有文件。
4. **`deepseek-edge`** 仅作归档（Qt / DdBridge / `third-party`）；新功能只提交本仓。
5. 从 edge 抢救单个文件时：**手动复制** + diff/合并，不要整仓镜像。
6. 远程 Git：在本仓配置 `origin` 并 `push`（见仓库根目录 Git 历史）。
