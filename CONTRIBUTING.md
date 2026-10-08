# 贡献开发指南

感谢你参与 MChub 开发。本指南说明如何获取完整源码、初始化子模块、配置构建环境并运行项目

## 1. 克隆项目

MChub 使用 git 子模块

```bash
git clone  https://github.com/tiouoo/MChub.git
cd MChub/scripts

# Linux / macOS
./update.sh

# Windows
./update.bat
```

## 2. 准备开发环境

构建 MChub 需要：

- .NET SDK `10.0`
- 开发基岩版相关项目时，需要 C++ 工具链

## 3. 配置环境变量

仓库根目录的 `.env.example` 列出了可选变量。它不会被 .NET 自动加载；请复制其中的变量到操作系统环境、IDE 的运行配置。不要将真实密钥提交到仓库。

常用变量如下：

| 变量                         | 用途                    |
| ---------------------------- | ----------------------- |
| `CURSEFORGE_API_KEY`         | CurseForge API 访问     |
| `MICROSOFT_CLIENT_ID`        | Microsoft/Xbox 登录配置 |
| `GRAVITYCONE_UPTIME_API_KEY` | GravityCone 可用性检测  |
| `CNB_UPDATE_TOKEN`           | CNB 更新源访问          |
| `PRE_MC_KEY`                 | 基岩版 Preview          |
| `REL_MC_KEY`                 | 基岩版 Release          |

Linux/macOS 当前 Shell 会话中可以这样设置：

```bash
export MICROSOFT_CLIENT_ID="your-client-id"
export CURSEFORGE_API_KEY="your-key"
```

PowerShell 中可以这样设置：

```powershell
$env:MICROSOFT_CLIENT_ID = "your-client-id"
$env:CURSEFORGE_API_KEY = "your-key"
```

没有这些可选密钥时，MChub 仍可编译；只有依赖对应服务或构建链的功能会不可用。

### 凭据使用规则（Fork / 二次分发必读）

密钥与它所服务的项目身份是绑定的。**Fork 本仓库、二次分发或改名发行时，必须二选一：**

1. 将变量替换为**你自己申请**的 key / client ID；
2. 或将其置为**空字符串 `""`** —— 对应功能会自动关闭，程序仍可正常构建与运行（这是被支持的用法）。

沿用原仓库的凭据，等于以他人身份使用其配额，并使其承担上游条款的责任。

使用这些凭据即表示接受以下条款：

- [CurseForge 3rd Party API Terms and Conditions](https://support.curseforge.com/en/support/solutions/articles/9000207405-curse-forge-3rd-party-api-terms-and-conditions)
- [Microsoft Identity Platform Terms of Use](https://docs.microsoft.com/en-us/legal/microsoft-identity-platform/terms-of-use)

提交前自检（应无输出）：

```bash
git grep -nE '\$2a\$10\$|client_id\s*=\s*"?[0-9a-f]{8}-'
```

## 3.1 配置 NuGet 源（还原基座库）

UI 基座库 `CodeHub-FluentUI` 发布在 GitHub Packages，**不在 nuget.org 上**：

```
https://nuget.pkg.github.com/hub-code-develop/index.json
```

**这个源即使包是公开的也要求认证** —— 匿名请求直接 401（实测：服务索引与包下载均如此），
所以本地首次还原前必须先配一次凭据。需要一个带 `read:packages` 的 Classic PAT：

```bash
dotnet nuget add source "https://nuget.pkg.github.com/hub-code-develop/index.json" \
  --username <你的 GitHub 用户名> \
  --password <带 read:packages 的 PAT> \
  --store-password-in-clear-text \
  --name codehub-gh
```

CI 侧不需要你手动做任何事：各构建作业里已有 `Configure NuGet (GitHub Packages)` 步骤，
用工作流自带的 `GITHUB_TOKEN` 完成同一步配置。

> 若仓库没有该包的读取权限，CI 会以 401/403 失败。此时到
> `https://github.com/orgs/hub-code-develop/packages/nuget/CodeHub-FluentUI/settings`
> 的 **Manage Actions access** 里把本仓库加进去（Read 角色）。

> 完整的凭据与安全策略见 [SECURITY.md](SECURITY.md)，其中包括「客户端二进制内含可提取凭据」
> 这一**已公开披露的取舍**及其后果。

## 4. 构建和运行

在仓库根目录执行：

```bash
dotnet build src/MChub.Desktop/MChub.Desktop.csproj
```

如果只修改了某个子项目，也可以直接构建该项目；提交前建议至少构建对应平台的 `MChub.Desktop` 项目。

## 5. 提交修改前检查

```bash
git status
git diff --check
```

请确认没有提交密钥、个人配置、构建产物或未预期的子模块指针变更。涉及界面修改时，除了编译，还应在目标平台实际运行并检查界面效果。

