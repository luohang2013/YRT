using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace YRT.Core;

/// <summary>GitHub Release 信息（更新检测只用到的字段）。</summary>
public sealed class GitHubRelease
{
    [JsonPropertyName("tag_name")]
    public string TagName { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("body")]
    public string Body { get; set; } = "";

    [JsonPropertyName("html_url")]
    public string HtmlUrl { get; set; } = "";

    [JsonPropertyName("published_at")]
    public DateTime? PublishedAt { get; set; }

    [JsonPropertyName("prerelease")]
    public bool Prerelease { get; set; }

    [JsonPropertyName("assets")]
    public List<GitHubAsset> Assets { get; set; } = new();
}

/// <summary>Release 附件（可提供的直接下载链接）。</summary>
public sealed class GitHubAsset
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("browser_download_url")]
    public string BrowserDownloadUrl { get; set; } = "";

    [JsonPropertyName("size")]
    public long Size { get; set; }
}

/// <summary>公告内容。JSON 源支持 title / date / content 字段；纯文本源整体作为 content。</summary>
public sealed class Announcement
{
    public string Title { get; set; } = "";
    public string Date { get; set; } = "";
    public string Content { get; set; } = "";
}

/// <summary>
/// 更新与公告服务：GitHub Releases 版本检测 + 任意公开文本源（GitHub Raw / Gist / 自建）公告拉取。
/// 结果缓存在静态属性上并通过事件广播，设置页 / 主页订阅后刷新界面。
/// </summary>
public static class UpdateService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    /// <summary>
    /// 内置官方更新源（写死在程序内，不提供配置项）：
    /// 防止更新源被篡改后指向恶意仓库，导致更新提示 / 下载页被注入。
    /// </summary>
    public const string OfficialRepo = "luohang2013/YRT";

    static UpdateService()
    {
        // GitHub API 要求携带 User-Agent
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("YRT-UpdateChecker");
        Http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    // ---------------- 当前版本 ----------------

    private static Version LocalVersion =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

    /// <summary>当前程序版本（取自程序集，展示为 v主.次.修订）。</summary>
    public static string CurrentVersionText
    {
        get
        {
            var v = LocalVersion;
            return $"v{v.Major}.{v.Minor}.{v.Build}";
        }
    }

    /// <summary>解析 tag 为版本号：v1.2.3 / 1.2 / 1.2.3-beta 均可；失败返回 false。</summary>
    public static bool TryParseVersion(string? tag, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(tag)) return false;
        var m = Regex.Match(tag, @"\d+(?:\.\d+){1,3}");
        if (!m.Success) return false;
        var nums = m.Value.Split('.')
            .Select(p => int.TryParse(p, out var n) ? Math.Clamp(n, 0, 65535) : 0)
            .ToList();
        while (nums.Count < 3) nums.Add(0);
        try
        {
            version = new Version(nums[0], nums[1], nums[2]);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>远端 tag 是否比本地程序集版本新。</summary>
    public static bool IsNewerThanLocal(string? tag) =>
        TryParseVersion(tag, out var remote) && remote > LocalVersion;

    // ---------------- 更新检测 ----------------

    /// <summary>最近一次检测到的最新 Release（null = 尚未检测或没有结果）。</summary>
    public static GitHubRelease? LatestRelease { get; private set; }

    /// <summary>最近一次检测的错误描述（空 = 无错误）。</summary>
    public static string LastError { get; private set; } = "";

    /// <summary>是否有新版本（LatestRelease 有效且版本号更新时为 true）。</summary>
    public static bool UpdateAvailable => LatestRelease != null && IsNewerThanLocal(LatestRelease.TagName);

    /// <summary>更新检测结果变化时触发（可能在后台线程触发，订阅方需切回 UI 线程）。</summary>
    public static event Action? StateChanged;

    /// <summary>检查更新：repo 形如 owner/repo。所有异常均转为 LastError，不抛出。</summary>
    public static async Task CheckForUpdateAsync(string? repo)
    {
        LatestRelease = null;
        LastError = "";
        if (string.IsNullOrWhiteSpace(repo))
        {
            LastError = "未配置更新源";
        }
        else
        {
            try
            {
                var url = $"https://api.github.com/repos/{repo.Trim().TrimEnd('/')}/releases/latest";
                using var resp = await Http.GetAsync(url);
                if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    LastError = "仓库不存在，或还没有发布任何 Release";
                }
                else
                {
                    resp.EnsureSuccessStatusCode();
                    var json = await resp.Content.ReadAsStringAsync();
                    LatestRelease = JsonSerializer.Deserialize<GitHubRelease>(json);
                    if (LatestRelease == null)
                        LastError = "返回内容无法解析，请检查更新源是否正确";
                }
            }
            catch (Exception ex)
            {
                LastError = FriendlyError(ex);
            }
        }
        StateChanged?.Invoke();
    }

    // ---------------- 公告 ----------------

    /// <summary>最近一次拉取到的公告（null = 尚未拉取或拉取失败）。</summary>
    public static Announcement? Announcement { get; private set; }

    /// <summary>最近一次拉取公告的错误描述（空 = 无错误）。</summary>
    public static string AnnouncementError { get; private set; } = "";

    /// <summary>公告内容变化时触发（可能在后台线程触发，订阅方需切回 UI 线程）。</summary>
    public static event Action? AnnouncementChanged;

    /// <summary>拉取公告：JSON（title/date/content 字段）或纯文本均可。异常均转为 AnnouncementError，不抛出。</summary>
    public static async Task LoadAnnouncementAsync(string? url)
    {
        Announcement = null;
        AnnouncementError = "";
        if (!string.IsNullOrWhiteSpace(url))
        {
            try
            {
                var text = (await Http.GetStringAsync(url.Trim())).Trim();
                if (text.StartsWith("{"))
                {
                    try
                    {
                        Announcement = JsonSerializer.Deserialize<Announcement>(text)
                                       ?? new Announcement { Content = text };
                    }
                    catch
                    {
                        Announcement = new Announcement { Content = text };
                    }
                }
                else
                {
                    Announcement = new Announcement { Content = text };
                }
            }
            catch (Exception ex)
            {
                AnnouncementError = FriendlyError(ex);
            }
        }
        AnnouncementChanged?.Invoke();
    }

    private static string FriendlyError(Exception ex) => ex switch
    {
        HttpRequestException => "网络请求失败，请检查网络连接或源地址",
        TaskCanceledException => "请求超时，请稍后重试",
        _ => "请求失败：" + ex.Message,
    };
}
