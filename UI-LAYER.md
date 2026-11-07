# UI 2.0 · 纯 UI 层

这个分支（`ui/2.0-only`）只放**新 UI**，不含任何业务底座。
等正式版的 bug 修完，再把最终确定的那套底座整体搬进来。

## 保留了什么

| 目录 | 内容 |
| --- | --- |
| `src/MChub/Workbench/` | IDE 工作台：Shell / Views / Documents / Services 界面层 |
| `src/MChub/Launcher/` | 启动器首页（版本列表 + 横幅） |
| `src/MChub/Styles/`、`Controls/` | 主题、样式、自定义控件 |
| `src/MChub/Assets/` | 字体、图标等 UI 资源 |
| `src/MChub/App.axaml` | 应用级 `ThemeDictionaries`（`Wb*` 色板都在这） |
| `src/MChub.Localization/` | 界面文案（中/英） |
| `src/MChub.Desktop/` | 程序入口 |
| `module/Tio.Avalonia.Standard/` | UI 控件库（不是业务逻辑） |

## 删掉了什么

`src/MChub.Core`、`module/Iridium`、`module/MinecraftLaunch`、`module/LiteSkinViewer`、
全部 `MChub.Bedrock.*`、`MChub.Android`、`MChub.Console`、`MChub.LitematicaViewer`、
`MChub.Web`、`packaging`、`scripts`，以及 `src/MChub` 下的旧 UI
（`Views/`、`ViewModels/`、`Services/`、`Module/`、`Classes/`、`Platform/`）。

## 现在能编译吗

**不能。** UI 层仍然引用 `MinecraftInstance`、`InstanceManager`、`JavaInstanceConfig`
这些底座类型。这是有意为之：底座补回来之前，这里只做界面调整（布局 / 配色 / 文案），
不承诺可运行。

## 怎么找完整的、能跑的那一版

- 分支 `main`：UI 2.0 的完整可运行版本（含底座），随 bug 修复同步更新
- tag `backup/ui2-before-strip-20261107`：拆分前的完整快照
- 离线备份：`~/Backups/MChub-ui2-backup-20261107/`（bundle + tree）

## 底座回来时怎么合

1. 把 `src/MChub.Core`、`module/Iridium`、`module/MinecraftLaunch`、`module/LiteSkinViewer`
   拷回本分支；
2. 恢复 `src/MChub/MChub.csproj` 里的 `MChub.Core` 工程引用；
3. 恢复 `src/MChub.Desktop/MChub.Desktop.csproj` 里的 Bedrock 平台引用；
4. 恢复 `MChub.slnx` 中的工程条目；
5. 补回 `App.axaml.cs` 需要的启动窗口（登录 / OOBE / 迁移 / 整合包导入），
   这些在旧 `Views/` 里，需要按新视觉重做一遍。
