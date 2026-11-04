# NovaMacro 2.0.0 — Windows x64

- `NovaMacro.exe`：自带 .NET 运行时的单文件程序，Windows 10/11 x64 双击即可运行（需要管理员权限）。
- 编译自最新提交（自检修复：退出时恢复卡单的游戏、按住类功能不再漏键、开机启动/音量/缩放等设置生效、与原版对齐的小工具热键与载具启动逻辑）。
- SHA-256：`9b2d69225309f3e6b2178e45f40a80a3dfd2a65e8a821029bd337ee9d22c8e5c`
- 首次运行会在 exe 同目录生成 `NovaMacro Config\Settings.ini`（默认参数，不含任何个人数据；你的数据只保存在本地）。
- 程序未签名，SmartScreen 提示时选择“更多信息 → 仍要运行”。

重新生成：

```powershell
dotnet publish src/NovaMacro.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o publish
```
