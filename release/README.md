# NovaMacro 2.0.0 — Windows x64

- `NovaMacro.exe`：自带 .NET 运行时的单文件程序，Windows 10/11 x64 双击即可运行（需要管理员权限）。
- 编译自提交 `ba70e88`（深色主题、紧凑界面、默认配置已去个人化）。
- SHA-256：`237ce8743f916257ad06f1649da1444ba5392b498130bb10f57a330ddfd796f9`
- 首次运行会在 exe 同目录生成 `NovaMacro Config\Settings.ini`（默认参数，不含任何个人数据；你的数据只保存在本地）。
- 程序未签名，SmartScreen 提示时选择“更多信息 → 仍要运行”。

重新生成：

```powershell
dotnet publish src/NovaMacro.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o publish
```
