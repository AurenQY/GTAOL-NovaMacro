# NovaMacro 2.0.0 — Windows x64

- `NovaMacro.exe`：自带 .NET 运行时的单文件程序，Windows 10/11 x64 双击即可运行（需要管理员权限）。
- 编译自提交 `15c540b`（一屏紧凑界面、修复游戏卡顿与嘟嘟声、深色主题、默认配置无个人数据）。
- SHA-256：`3ac3124168b4f57baf14e803db0d975f8c5de3859c859a7a3f561a2ab469d32d`
- 首次运行会在 exe 同目录生成 `NovaMacro Config\Settings.ini`（默认参数，不含任何个人数据；你的数据只保存在本地）。
- 程序未签名，SmartScreen 提示时选择“更多信息 → 仍要运行”。

重新生成：

```powershell
dotnet publish src/NovaMacro.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o publish
```
