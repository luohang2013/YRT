# YRT — Minecraft 资源包工具箱

基于 .NET 8 / WPF 的 Windows 资源包工具，默认程序名为「YHCT 的资源包工具箱」。

## 功能

- **生成**：材质目录打包或解包，自动处理 `pack.mcmeta` 与 `pack.png`。
- **混搭**：按分类组合多个资源包，保存和恢复混搭方案。
- **编辑 / 分析**：浏览与修改包内容，统计结构、占用并诊断问题。
- **安装**：部署 Java 版与网易基岩版资源包，支持 options.txt 编辑和安装前备份。
- **设置 / 更新**：主题、程序名、操作日志、GitHub Release 更新与公告。

支持拖拽及 `.zip`、`.mcpack`、资源包目录。

## 下载

- [GitHub Releases](https://github.com/luohang2013/YRT/releases)
- [蓝奏云备用](https://wwbrw.lanzouq.com/b00ybjxczg)，提取码：`YRT`

## 从源码构建

需要 .NET 8 SDK，在仓库根目录执行：

```powershell
dotnet publish YRT/YRT.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o dist
```

产物为免安装的 `YRT.exe`。操作日志可从“设置 → 安装 → 数据与日志”打开。

> 程序未签名，杀毒软件可能拦截；如双击无反应，请检查拦截记录并添加信任。

---

仅供学习交流使用，与 Mojang / 网易官方无关。
