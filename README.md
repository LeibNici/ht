# U8ApiAuthCheck

用于检查当前 U8 环境是否能登录并获取指定 U8 API 单据接口的 Windows x86 控制台程序。

## 本地运行

先在运行机器安装并注册 U8 客户端/API 组件，然后运行：

```powershell
.\U8ApiAuthCheck.exe --server <服务器> --user <账号> --password <密码> --account <账套> --year <年度>
```

可选参数：

```powershell
--module IA --bill PurIn --no-pause
```

也可以使用环境变量：

```powershell
$env:U8_SERVER="<服务器>"
$env:U8_USER="<账号>"
$env:U8_PASSWORD="<密码>"
$env:U8_ACCOUNT="<账套>"
$env:U8_YEAR="<年度>"
.\U8ApiAuthCheck.exe
```

## GitHub Actions 产物

推送到 GitHub 后，`Build Windows EXE` workflow 会在 `windows-latest` 上编译 `Release|x86`，并上传 `U8ApiAuthCheck-win-x86` artifact。下载 artifact 后解压，运行 `U8ApiAuthCheck.exe`。

注意：Actions 只负责生成 exe 和随包文件。实际登录 U8 仍需要在目标 Windows 机器上安装并注册匹配版本的 U8 客户端/API 组件。
