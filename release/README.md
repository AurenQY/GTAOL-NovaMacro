# NovaMacro 2.0.0 — Windows x64

- `NovaMacro.exe`：自带 .NET 运行时的单文件程序，Windows 10/11 x64 双击即可运行（需要管理员权限）。
- 编译自 `master` 提交 `07e8553`。
- SHA-256：`8db6b00a85a74d76f53e84e3f6dc34510ed3e8a85c24b303ad627ffecf9999f4`
- 首次运行会在 exe 同目录生成 `NovaMacro Config\Settings.ini`（默认即作者配置）。
- 程序未签名，SmartScreen 提示时选择“更多信息 → 仍要运行”。

重新生成：

```powershell
dotnet publish src/NovaMacro.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o publish
```
