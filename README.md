# YRT — Minecraft 资源包工具箱

> 程序名支持在设置页自定义（默认「YHCT 的资源包工具箱」）

把 Minecraft 资源包的 **生成 · 混搭 · 分析 · 安装** 做成一站式流程的 Windows 桌面工具（WPF，.NET 8，win-x64 单文件，免安装解压即用）。

## 功能一览

| 模块 | 说明 |
|---|---|
| 生成 | 材质目录打包为 zip（自动 pack.mcmeta / pack.png），或解包现有 zip 编辑 |
| 资源包 | 多包混搭：贴图取 A 包、音效取 B 包，按分类聚合导出；支持保存/载入混搭方案（.yrtplan），包库启动自动恢复 |
| 分析 | 解析资源包结构、分类占用统计与诊断 |
| 安装 | Java 版 / 网易基岩版一键部署与启用；Java 版附带 options.txt 可视化编辑器（亮度、渲染距离、全屏等，保存前自动备份） |
| 设置 | 4 组主题色 + 自定义取色、浅色/深色模式、程序名自定义 |
| 更新 | GitHub Release 检测更新 + 公告展示（主页公告卡） |

全站支持拖拽操作，`.mcpack`（网易基岩版压缩档）同样全链路可用。

## 下载

- **GitHub Releases**（推荐）：https://github.com/luohang2013/YRT/releases
- **蓝奏云备用**：（上传后把链接贴在这里）

软件内也会自动提示新版本：设置 → 更新 → 检查更新。

## 从源码构建

```powershell
dotnet publish src/YRT/YRT.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o dist
```

要求 .NET 8 SDK，产物为单个 `YRT.exe`。

## 发布新版（维护者）

1. 更新 `src/YRT/YRT.csproj` 中的 `<Version>`（如 `0.5.0`）
2. 构建 → 打包 zip
3. `gh release create v0.5.0 YRT-v0.5.0.zip --title "..." --notes-file notes.md`
4. tag 必须带版本号（`v0.5.0` / `0.5.0` 均可），用户端会比较 tag 与本地版本

## 公告

公告源是一个 Gist / 任意公开文本链接，支持纯文本或 JSON：

```json
{"title": "标题", "date": "2026-11-11", "content": "公告内容"}
```

改完保存，用户端主页公告卡刷新即生效。

---
*仅供学习交流使用，与 Mojang / 网易官方无关。*
