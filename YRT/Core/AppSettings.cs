using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using YRT.FirstRun;

namespace YRT.Core;

/// <summary>
/// 应用设置（持久化为 JSON，位于 ResourceDeployer.DataDir\settings.json）。
/// 单例：AppSettings.Instance；修改属性后调用 Save() 落盘。
/// </summary>
public sealed class AppSettings
{
    // ---------------- 安装默认行为 ----------------

    /// <summary>基岩一键安装前结束 MC 进程（默认开）</summary>
    public bool KillMc { get; set; } = true;

    /// <summary>安装完成后自动打开目标目录</summary>
    public bool OpenDirAfterInstall { get; set; } = false;

    // ---------------- 生成页默认值 ----------------

    /// <summary>默认输出目录（空 = exe 同级 output\）</summary>
    public string DefaultOutDir { get; set; } = "";

    /// <summary>包名模板（{name} 会被替换为源目录名）</summary>
    public string PackNameTemplate { get; set; } = "{name}";

    /// <summary>打包时目录缺 pack.mcmeta 则自动在 zip 内补齐（默认开，不改动源目录）</summary>
    public bool AutoMcmeta { get; set; } = true;

    // ---------------- 扫描设置 ----------------

    /// <summary>Java 扫描范围：ProgramDir = exe 所在目录（默认）；AllDrives = 全盘</summary>
    public string ScanScope { get; set; } = "ProgramDir";

    /// <summary>通用递归扫描深度（1-8，默认 4）</summary>
    public int ScanDepth { get; set; } = 4;

    // ---------------- 外观 ----------------

    /// <summary>主题色：Green / Blue / Purple / Amber / Custom</summary>
    public string Theme { get; set; } = "Green";

    /// <summary>自定义主题色 #RRGGBB（Theme = Custom 时生效）</summary>
    public string CustomThemeColor { get; set; } = "";

    /// <summary>界面模式：Light / Dark</summary>
    public string ThemeMode { get; set; } = "Light";

    /// <summary>程序名称（外观-程序 里自定义；空 = 默认名）</summary>
    public string AppName { get; set; } = "";

    // ---------------- 更新与公告 ----------------

    // 更新源已内置为 Core.UpdateService.OfficialRepo（不可配置，防篡改注入）

    /// <summary>公告源：文本或 JSON 的 URL（GitHub Raw / Gist / 自建均可）；空 = 不拉取公告</summary>
    public string AnnouncementUrl { get; set; } = "";

    /// <summary>启动时自动检查更新（默认开；未配置更新源时无操作）</summary>
    public bool CheckUpdateOnStartup { get; set; } = true;

    /// <summary>蓝奏云下载页（文件夹外链或文件链接）；空 = 界面隐藏蓝奏云入口</summary>
    public string LanzouUrl { get; set; } = "https://wwbrw.lanzouq.com/b00ybjxczg";

    /// <summary>蓝奏云提取码（点击下载按钮时自动复制到剪贴板）；空 = 无密码</summary>
    public string LanzouPassword { get; set; } = "YRT";

    // ---------------- 资源包库 ----------------

    /// <summary>资源包页来源包库（启动时自动恢复；移除/清空后同步保存）</summary>
    public List<string> SavedPackPaths { get; set; } = new();

    // ---------------- 单例与持久化 ----------------

    public static AppSettings Instance { get; private set; } = Load();

    [JsonIgnore]
    public static string FilePath =>
        Path.Combine(ResourceDeployer.DataDir, "settings.json");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
    };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOpts);
                if (s is not null) return s;
            }
        }
        catch
        {
            // 配置损坏时回退默认值
        }
        return new AppSettings();
    }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Instance, JsonOpts));
        }
        catch
        {
            // 写盘失败不影响使用（下次启动回退默认）
        }
    }

    /// <summary>恢复默认值（不落盘，需再调 Save）</summary>
    public void ResetToDefaults()
    {
        var d = new AppSettings();
        KillMc = d.KillMc;
        OpenDirAfterInstall = d.OpenDirAfterInstall;
        DefaultOutDir = d.DefaultOutDir;
        PackNameTemplate = d.PackNameTemplate;
        AutoMcmeta = d.AutoMcmeta;
        ScanScope = d.ScanScope;
        ScanDepth = d.ScanDepth;
        Theme = d.Theme;
        CustomThemeColor = d.CustomThemeColor;
        ThemeMode = d.ThemeMode;
        AppName = d.AppName;
        AnnouncementUrl = d.AnnouncementUrl;
        CheckUpdateOnStartup = d.CheckUpdateOnStartup;
        LanzouUrl = d.LanzouUrl;
        LanzouPassword = d.LanzouPassword;
    }
}
