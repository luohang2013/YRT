using System.IO;
using System.Text.Json;

namespace YRT.Core;

/// <summary>一个 Java 版 resourcepacks 目录及其中的 zip 列表</summary>
public sealed class JavaPackDir
{
    /// <summary>resourcepacks 目录完整路径</summary>
    public string Dir { get; init; } = "";
    /// <summary>游戏根目录（resourcepacks 的上级，options.txt 所在处）</summary>
    public string GameRoot => Path.GetDirectoryName(Dir) ?? "";
    /// <summary>来源标注（官方默认 / 版本隔离 / Prism 实例 / 通用扫描…）</summary>
    public string Source { get; init; } = "";
    /// <summary>其中的 .zip 文件名（已排序）</summary>
    public List<string> Zips { get; init; } = new();

    public override string ToString() => $"{Dir}（{Zips.Count} 个 zip）";
}

/// <summary>一个网易基岩版安装点</summary>
public sealed class BedrockInstall
{
    /// <summary>…\games\com.netease 根目录</summary>
    public string Root { get; init; } = "";
    /// <summary>development_resource_packs（优先）或 resource_packs</summary>
    public string PacksRoot { get; init; } = "";
    /// <summary>编号目录 0,1,2…（网易基础包机制）</summary>
    public List<string> Slots { get; init; } = new();
}

/// <summary>Java 版扫描选项</summary>
public sealed class JavaScanOptions
{
    /// <summary>true = 全盘扫描（所有盘符 + %APPDATA%）；false = 只扫 exe 所在目录及子目录（默认）</summary>
    public bool AllDrives { get; init; } = false;

    /// <summary>通用递归扫描深度上限（1-8）</summary>
    public int MaxDepth { get; init; } = 4;
}

/// <summary>
/// 自动扫描 Minecraft 资源包路径（Java 版 + 网易基岩版）。
/// 移植自 RustPackGen scanner.rs，并按《调研第1期_启动器目录.md》通用规则扩展：
/// 1) 游戏根 = .minecraft 本身 / .minecraft\versions\&lt;名&gt;（须含数据文件）/ Prism 实例 / Modrinth 实例；
/// 2) 每个游戏根固定探测 options.txt 与 resourcepacks/；
/// 3) versions 下只有 .jar+.json 的空壳不算游戏根。
/// </summary>
public static class LauncherScanner
{
    /// <summary>扫描深度限制（相对扫描根的目录层数）</summary>
    private const int MaxDepth = 4;
    /// <summary>每个扫描根允许遍历的目录数上限，防止极端情况下卡死</summary>
    private const int DirBudget = 50_000;
    /// <summary>递归时跳过的无关目录（小写比较）</summary>
    private static readonly string[] SkipDirs =
    {
        "$recycle.bin", "system volume information", "winsxs", "node_modules", ".git",
    };

    /// <summary>枚举本机所有存在的盘符根（A:\ ~ Z:\）</summary>
    public static List<string> DriveRoots()
    {
        var roots = new List<string>();
        for (var c = 'A'; c <= 'Z'; c++)
        {
            var root = $"{c}:\\";
            if (Directory.Exists(root)) roots.Add(root);
        }
        return roots;
    }

    /// <summary>候选扫描根：所有盘符根 + 各盘下的 MC 目录 + %APPDATA%</summary>
    private static List<string> ScanRoots()
    {
        var roots = new List<string>();
        foreach (var drive in DriveRoots())
        {
            roots.Add(drive);
            var mc = Path.Combine(drive, "MC");
            if (Directory.Exists(mc)) roots.Add(mc);
        }
        roots.Add(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
        return roots;
    }

    /// <summary>Java 版扫描入口：返回所有真实存在的 resourcepacks 目录（按路径去重）。
    /// opts.AllDrives=false 时只扫 exe 所在目录及子目录。</summary>
    public static List<JavaPackDir> ScanJava(JavaScanOptions? opts = null)
    {
        opts ??= new JavaScanOptions();
        var found = new Dictionary<string, (string Dir, string Source)>(StringComparer.OrdinalIgnoreCase);

        if (!opts.AllDrives)
        {
            // 程序所在目录模式：只扫 exe 目录树（显式匹配 + 通用递归兜底）
            var exeRoot = AppContext.BaseDirectory;
            ExplicitJavaChecks(exeRoot, found);
            var budget = DirBudget;
            WalkForResourcePacks(exeRoot, 0, found, ref budget, opts.MaxDepth);
            return Finalize(found);
        }

        foreach (var root in ScanRoots())
        {
            // 1) 显式模式匹配（快速，覆盖常见启动器目录，不受深度预算影响）
            ExplicitJavaChecks(root, found);
            // 2) 通用递归扫描（深度 <= MaxDepth，兜底自定义目录结构）
            var budget = DirBudget;
            WalkForResourcePacks(root, 0, found, ref budget, opts.MaxDepth);
        }

        // 3) 各启动器的实例结构（相对盘符根较深，通用扫描够不到，单独显式处理）
        foreach (var drive in DriveRoots())
        {
            // CurseForge：curseforge\minecraft\Instances\<实例>\resourcepacks
            AddChildren(Path.Combine(drive, "curseforge", "minecraft", "Instances"), "CurseForge 实例", found);
            // Prism / MultiMC 便携版：PrismLauncher|MultiMC\instances\<实例>\.minecraft|minecraft
            foreach (var name in new[] { "PrismLauncher", "MultiMC" })
            {
                var instRoot = Path.Combine(drive, name, "instances");
                foreach (var inst in SafeDirs(instRoot))
                {
                    foreach (var sub in new[] { ".minecraft", "minecraft" })
                        AddIfDir(Path.Combine(inst, sub, "resourcepacks"), $"{name} 实例", found);
                }
            }
        }
        // Prism / MultiMC 安装版：%APPDATA%\PrismLauncher\instances\*
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        foreach (var inst in SafeDirs(Path.Combine(appData, "PrismLauncher", "instances")))
            foreach (var sub in new[] { ".minecraft", "minecraft" })
                AddIfDir(Path.Combine(inst, sub, "resourcepacks"), "Prism 实例", found);

        // 4) Modrinth App：profiles\<实例名> 本身就是游戏根
        foreach (var baseDir in new[] { "ModrinthApp", "com.modrinth.theseus" })
            foreach (var profile in SafeDirs(Path.Combine(appData, baseDir, "profiles")))
                AddIfDir(Path.Combine(profile, "resourcepacks"), "Modrinth 实例", found);

        // 5) 官方启动器：launcher_profiles.json 里每个 profile 的 gameDir
        ScanOfficialProfiles(Path.Combine(appData, ".minecraft"), found);

        return Finalize(found);
    }

    /// <summary>扫描结果整理：过滤不存在项、按路径排序、带出 zip 列表</summary>
    private static List<JavaPackDir> Finalize(Dictionary<string, (string Dir, string Source)> found) =>
        found.Values
            .Where(v => Directory.Exists(v.Dir))
            .OrderBy(v => v.Dir, StringComparer.OrdinalIgnoreCase)
            .Select(v => new JavaPackDir { Dir = v.Dir, Source = v.Source, Zips = ListZips(v.Dir) })
            .ToList();

    /// <summary>显式匹配某个扫描根下的常见启动器结构：
    /// &lt;根&gt;\*.minecraft\resourcepacks 与 &lt;根&gt;\*.minecraft\versions\&lt;版本&gt;\resourcepacks</summary>
    private static void ExplicitJavaChecks(string root, Dictionary<string, (string, string)> found)
    {
        foreach (var entry in SafeDirs(root))
        {
            var name = Path.GetFileName(entry);
            // 覆盖 .minecraft 及任意 *.minecraft 目录（PCL2 / HMCL / BakaXL 等启动器）
            if (!name.EndsWith(".minecraft", StringComparison.OrdinalIgnoreCase)) continue;

            AddIfDir(Path.Combine(entry, "resourcepacks"), "启动器目录", found);
            ScanOfficialProfiles(entry, found); // 官方启动器 profile 自定义 gameDir

            // PCL2 / HMCL 版本隔离：versions\<版本>\resourcepacks（空壳版本不算游戏根）
            var versions = Path.Combine(entry, "versions");
            foreach (var v in SafeDirs(versions))
            {
                if (IsGameRootLike(v))
                    AddIfDir(Path.Combine(v, "resourcepacks"), "版本隔离", found);
            }
        }
    }

    /// <summary>解析 launcher_profiles.json，把每个 profile 的 gameDir 也作为游戏根</summary>
    private static void ScanOfficialProfiles(string mcDir, Dictionary<string, (string, string)> found)
    {
        var file = Path.Combine(mcDir, "launcher_profiles.json");
        if (!File.Exists(file)) return;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            if (!doc.RootElement.TryGetProperty("profiles", out var profiles)) return;
            foreach (var p in profiles.EnumerateObject())
            {
                if (p.Value.ValueKind != JsonValueKind.Object) continue;
                if (!p.Value.TryGetProperty("gameDir", out var gd)) continue;
                var dir = gd.GetString();
                if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) continue;
                AddIfDir(Path.Combine(dir.TrimEnd('\\', '/'), "resourcepacks"), "官方启动器 profile", found);
            }
        }
        catch
        {
            // JSON 解析失败不影响其余扫描
        }
    }

    /// <summary>游戏根判定（调研通用规则 2/3）：有 options.txt / saves / resourcepacks 之一才算</summary>
    public static bool IsGameRootLike(string dir) =>
        File.Exists(Path.Combine(dir, "options.txt"))
        || Directory.Exists(Path.Combine(dir, "saves"))
        || Directory.Exists(Path.Combine(dir, "resourcepacks"));

    /// <summary>递归查找名为 resourcepacks 的目录（深度 <= maxDepth）</summary>
    private static void WalkForResourcePacks(
        string dir, int depth, Dictionary<string, (string, string)> found, ref int budget, int maxDepth)
    {
        if (depth > maxDepth || budget <= 0) return;
        foreach (var entry in SafeDirs(dir))
        {
            if (budget <= 0) return;
            budget--;
            var name = Path.GetFileName(entry);
            if (name.Equals("resourcepacks", StringComparison.OrdinalIgnoreCase))
            {
                AddIfDir(entry, "通用扫描", found);
                continue; // 不再深入 resourcepacks 内部
            }
            if (SkipDirs.Contains(name.ToLowerInvariant())) continue;
            WalkForResourcePacks(entry, depth + 1, found, ref budget, maxDepth);
        }
    }

    /// <summary>列出 resourcepacks 目录中的 .zip（按文件名排序）</summary>
    public static List<string> ListZips(string dir)
    {
        var zips = new List<string>();
        if (!Directory.Exists(dir)) return zips;
        foreach (var f in Directory.EnumerateFiles(dir, "*.zip"))
            zips.Add(Path.GetFileName(f));
        zips.Sort(StringComparer.OrdinalIgnoreCase);
        return zips;
    }

    /// <summary>基岩版（网易）扫描：定位 %APPDATA% 下两个网易目录的 games\com.netease</summary>
    public static List<BedrockInstall> ScanBedrock()
    {
        var result = new List<BedrockInstall>();
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        // 网易两个已知安装位置
        foreach (var name in new[] { "MinecraftPC_Netease_PB", "MinecraftPE_Netease" })
        {
            var root = Path.Combine(appData, name, "games", "com.netease");
            if (!Directory.Exists(root)) continue;
            // 验证存在 development_resource_packs 或 resource_packs；根目录优先前者
            var dev = Path.Combine(root, "development_resource_packs");
            var std = Path.Combine(root, "resource_packs");
            var packsRoot = Directory.Exists(dev) ? dev : Directory.Exists(std) ? std : null;
            if (packsRoot is null) continue;
            // 列出编号目录 0,1,2…（网易基础包机制）
            var slots = SafeDirs(packsRoot)
                .Select(d => Path.GetFileName(d))
                .Where(n => n.Length > 0 && n.All(char.IsAsciiDigit))
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();
            result.Add(new BedrockInstall { Root = root, PacksRoot = packsRoot, Slots = slots });
        }
        return result;
    }

    // ---------------- 小工具 ----------------

    private static void AddIfDir(string path, string source, Dictionary<string, (string, string)> found)
    {
        if (Directory.Exists(path)) found[Path.GetFullPath(path)] = (Path.GetFullPath(path), source);
    }

    private static void AddChildren(string parent, string source, Dictionary<string, (string, string)> found)
    {
        foreach (var inst in SafeDirs(parent))
            AddIfDir(Path.Combine(inst, "resourcepacks"), source, found);
    }

    private static List<string> SafeDirs(string dir)
    {
        try
        {
            return Directory.EnumerateDirectories(dir).ToList();
        }
        catch
        {
            return new List<string>();
        }
    }
}
