# NovaMacro 2.0.0 — Windows x64

- `NovaMacro.exe`：自带 .NET 运行时的单文件程序，Windows 10/11 x64 双击即可运行（需要管理员权限）。
- 编译自分支最新提交（新图标、“关闭后保留在托盘”选项；整机卡顿修复、ReadyToRun 预编译）。
- SHA-256：`34bcb4285b7cbf50083864c1d865bb2546d5e52a8892cfca9f2e8f36dfda6ee2`
- 首次运行会在 exe 同目录生成 `NovaMacro Config\Settings.ini`（默认参数，不含任何个人数据；你的数据只保存在本地）。
- 程序未签名，SmartScreen 提示时选择“更多信息 → 仍要运行”。

重新生成：

```powershell
dotnet publish src/NovaMacro.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o publish
```
