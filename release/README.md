# NovaMacro 2.0.0 — Windows x64

- `NovaMacro.exe`：自带 .NET 运行时的单文件程序，Windows 10/11 x64 双击即可运行（需要管理员权限）。
- 编译自提交 `72f1965`（修复整机卡顿：键盘钩子无锁化、鼠标改用 Raw Input、去除周期性进程枚举；预编译 ReadyToRun）。
- SHA-256：`dcb94ca4b6c04cf69c051ac0be27ef24e2c90a37490a15b0f82f1d45e782ca7c`
- 首次运行会在 exe 同目录生成 `NovaMacro Config\Settings.ini`（默认参数，不含任何个人数据；你的数据只保存在本地）。
- 程序未签名，SmartScreen 提示时选择“更多信息 → 仍要运行”。

重新生成：

```powershell
dotnet publish src/NovaMacro.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o publish
```
