- 开发中所有文档统一放在 files/ 目录。
- 模块开发完成后必须同步更新开发文档。
- 前端设计实现需遵循 skills/apple-design/DESIGN.md 中的设计主题。
- 所有模块的开发要遵循：高内聚、低耦合的思想。
- 所有方案文档只保留单一版本，文件名不带版本号，后续在原文档直接更新。

- 2026-06-02: 已移除 `MainView.axaml` 中文件树的展开按钮与文件的打开按钮，并移除左侧“打开标签”区域以精简交互（更依赖顶部标签栏）。
- 2026-06-02: 新增单元测试 `FileTree_ExpandedState_ShouldBePreservedAcrossWorkspaceRefresh`，验证 `MainViewModel` 在 Workspace 刷新时保持文件树展开状态。
- 2026-06-02: 已在本地运行 `dotnet build` 与 `dotnet test`，所有测试通过（64/64）。
