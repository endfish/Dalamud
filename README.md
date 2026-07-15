# Dalamud Standalone CN

这是面向国服客户端的个人实验性 Dalamud 分支，也是
[DalamudStandaloneCN](https://github.com/endfish/DalamudStandaloneCN) 独立注入壳所使用的核心载荷。
项目以 [ottercorp/Dalamud](https://github.com/ottercorp/Dalamud) 为主要同步上游，并保留
[goatcorp/Dalamud](https://github.com/goatcorp/Dalamud) 的原始成果与许可。

## 项目边界

- 只支持向已经运行的 `ffxiv_dx11.exe` 附加注入，不负责账号登录或启动游戏。
- 不使用 XIVLauncherCN 的数据目录，默认数据根目录为 `%APPDATA%\DalamudStandaloneCN`。
- 不连接国服官方 Dalamud 发布、分支、反馈或插件库服务。
- 插件来源仅包括用户配置的自定义仓库、已安装到本地的插件以及开发插件。
- 仓库不可用或插件从仓库下架时，已安装副本仍作为本地非托管插件保留；只有来源 URL 与已配置仓库精确匹配时才会提供更新。
- 本仓库只公开源码，不提供编译产物、发行版或面向第三方用户的技术支持。

这不是 XIVLauncherCN、OtterCorp 或 Dalamud 上游团队的官方发行版。补丁日绕过兼容性保护可能导致游戏崩溃、数据损坏或插件异常，使用者需自行阅读代码、编译并承担风险。

## 分支与同步

- `otter-sync`：跟踪国服上游，不放 Standalone 专用改动。
- `standalone-cn`：独立注入、目录隔离、自定义插件源及补丁日研究改动。
- `lib/FFXIVClientStructs` 指向 [endfish/FFXIVClientStructs](https://github.com/endfish/FFXIVClientStructs)，便于在国服更新后独立维护解析结构。

同步上游时应先更新 `otter-sync`，再将确认过的改动合入 `standalone-cn`，不要把 Standalone 差异反向提交给上游。

## 本地构建

```powershell
git submodule update --init --recursive
dotnet restore Dalamud/Dalamud.csproj
dotnet build Dalamud/Dalamud.csproj -c Release --no-restore
dotnet build Dalamud.Injector/Dalamud.Injector.csproj -c Release --no-restore
```

完整注入还需要构建原生的 `Dalamud.Boot`、准备匹配的 .NET Runtime 与 Dalamud Assets，并在独立壳配置中指定载荷目录。独立壳不会下载官方载荷，也不会替用户判断补丁日兼容性。

## 许可与致谢

Dalamud 依据仓库中的 AGPL-3.0-or-later 许可发布。感谢 Dalamud、goatcorp、ottercorp、FFXIVClientStructs、Lumina 及所有相关上游贡献者。

Final Fantasy XIV © SQUARE ENIX CO., LTD. 本项目与 SQUARE ENIX CO., LTD. 无关联。
