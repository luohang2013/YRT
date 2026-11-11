using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace YRT.Core;

/// <summary>
/// 部署模块（移植自 RustPackGen deployer.rs，原逻辑移植自网易基岩版"全局材质包安装器"）：
/// - 基础包：按顺序整体替换 game 编号目录 0,1,2…
/// - 材质包：合并进编号目录，跳过清单文件（保留游戏原 manifest）
/// - 应用前结束 Minecraft 进程（文件句柄占用会导致复制不完整）
/// - Java 版：zip 直接复制进 resourcepacks 目录；并支持改写 options.txt 启用/关闭
/// </summary>
public static class PackDeployer
{
    /// <summary>合并时跳过的文件名（保留游戏/网易原始清单与图标）</summary>
    public static readonly string[] MergeSkip =
    {
        "manifest.json",
        "pack_icon.png",
        "contents.json",
        "netease_config.json",
        "pack_manifest.json",
    };

    private static readonly string[] McProcessNames = { "MinecraftPC", "MinecraftPE", "Minecraft", "javaw" };

    /// <summary>检测 Minecraft 相关进程是否在运行</summary>
    public static bool McRunning()
    {
        try
        {
            var psi = new ProcessStartInfo("tasklist", "/FO CSV /NH")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi);
            if (p is null) return false;
            var s = p.ReadToEndOutput().ToLowerInvariant();
            p.WaitForExit();
            return McProcessNames.Any(n => s.Contains(n.ToLowerInvariant() + ".exe"));
        }
        catch
        {
            return false;
        }
    }

    /// <summary>结束 Minecraft 相关进程，返回成功结束的进程名列表</summary>
    public static List<string> KillMinecraftProcesses()
    {
        var killed = new List<string>();
        foreach (var name in McProcessNames)
        {
            try
            {
                var psi = new ProcessStartInfo("taskkill", $"/IM {name}.exe /F")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using var p = Process.Start(psi);
                if (p is null) continue;
                p.WaitForExit();
                if (p.ExitCode == 0) killed.Add(name);
            }
            catch
            {
                // 单个进程名结束失败不影响其余
            }
        }
        return killed;
    }

    /// <summary>递归复制目录，返回复制的文件数</summary>
    private static int CopyRecursive(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        var count = 0;
        foreach (var entry in Directory.EnumerateFileSystemEntries(src))
        {
            var name = Path.GetFileName(entry);
            var dest = Path.Combine(dst, name);
            if (Directory.Exists(entry))
            {
                count += CopyRecursive(entry, dest);
            }
            else
            {
                File.Copy(entry, dest, overwrite: true);
                count++;
            }
        }
        return count;
    }

    /// <summary>基础包整体替换：先删除 dst，再完整复制 src → dst，返回复制的文件数</summary>
    public static int ReplaceDir(string src, string dst)
    {
        if (Directory.Exists(dst)) Directory.Delete(dst, recursive: true);
        return CopyRecursive(src, dst);
    }

    /// <summary>
    /// 材质包合并：把 src 内容合并进 dst（覆盖同名文件），
    /// 跳过 MergeSkip 清单与 disabled 目录，返回复制的文件数。
    /// </summary>
    public static int MergePack(string src, string dst, ISet<string> disabled)
    {
        Directory.CreateDirectory(dst);
        var count = 0;
        foreach (var entry in Directory.EnumerateFileSystemEntries(src))
        {
            var name = Path.GetFileName(entry);
            if (MergeSkip.Contains(name, StringComparer.Ordinal)) continue;
            var dest = Path.Combine(dst, name);
            if (Directory.Exists(entry))
            {
                if (disabled.Contains(name)) continue;
                count += MergePack(entry, dest, disabled);
            }
            else
            {
                File.Copy(entry, dest, overwrite: true);
                count++;
            }
        }
        return count;
    }

    /// <summary>
    /// 应用到游戏（网易基岩）：对每个槽位 slot 先整体替换基础包，再把每个材质包合并进去。
    /// 返回（写入文件总数, 应用的槽位列表）。
    /// </summary>
    public static (int Total, List<string> Used) ApplyBedrock(
        string packsRoot, IEnumerable<int> slots,
        IReadOnlyList<string> basePacks, IReadOnlyList<string> commonPacks,
        ISet<string> disabled)
    {
        var total = 0;
        var used = new List<string>();
        foreach (var s in slots)
        {
            var gameSlot = Path.Combine(packsRoot, s.ToString());
            // 每个槽位先放基础包（按 basePacks 顺序取第一个存在的）
            var baseDir = basePacks.FirstOrDefault(Directory.Exists)
                ?? throw new DirectoryNotFoundException("没有可用的基础包目录");
            var n = ReplaceDir(baseDir, gameSlot);
            total += n;
            // 合并所有材质包
            foreach (var c in commonPacks)
            {
                if (!Directory.Exists(c)) continue;
                total += MergePack(c, gameSlot, disabled);
            }
            used.Add(s.ToString());
        }
        return (total, used);
    }

    /// <summary>
    /// Java 版部署：把 zip 复制进 resourcepacks 目录，返回目标完整路径。
    /// conflict = "Rename" 时同名文件自动加序号；否则覆盖。
    /// </summary>
    public static string DeployJava(string zip, string resourcepacksDir, string conflict = "Overwrite")
    {
        Directory.CreateDirectory(resourcepacksDir);
        var name = Path.GetFileName(zip);
        var dst = Path.Combine(resourcepacksDir, name);
        if (!string.Equals(conflict, "Rename", StringComparison.Ordinal)) 
        {
            File.Copy(zip, dst, overwrite: true);
            return dst;
        }
        var stem = Path.GetFileNameWithoutExtension(name);
        var ext = Path.GetExtension(name);
        for (var i = 2; File.Exists(dst); i++)
            dst = Path.Combine(resourcepacksDir, $"{stem} ({i}){ext}");
        File.Copy(zip, dst, overwrite: false);
        return dst;
    }

    /// <summary>
    /// 安装前备份：把指定槽位目录打包为 zip 存入 backupDir，返回备份文件列表。
    /// </summary>
    public static List<string> BackupSlots(string packsRoot, IEnumerable<int> slots, string backupDir)
    {
        var made = new List<string>();
        Directory.CreateDirectory(backupDir);
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        foreach (var s in slots)
        {
            var dir = Path.Combine(packsRoot, s.ToString());
            if (!Directory.Exists(dir)) continue;
            var zipPath = Path.Combine(backupDir, $"槽位{s}_{stamp}.zip");
            PackPacker.PackDir(dir, zipPath);
            made.Add(zipPath);
        }
        return made;
    }

    /// <summary>
    /// 把 zip/mcpack 压缩档物化为目录（基岩版安装用，mcpack 本质是 zip）：
    /// 解压到 %TEMP%\YRT_archives\&lt;名称_大小&gt; 缓存目录，已存在则直接复用，返回包根目录。
    /// 目录来源原样返回。
    /// </summary>
    public static string MaterializeArchive(string path)
    {
        if (Directory.Exists(path)) return path;
        if (!File.Exists(path) || !PackPacker.IsPackArchive(path))
            throw new FileNotFoundException("路径不存在或不是 zip/mcpack：" + path);

        var fi = new FileInfo(path);
        var dir = Path.Combine(
            Path.GetTempPath(), "YRT_archives",
            $"{Path.GetFileNameWithoutExtension(path)}_{fi.Length:X8}");
        if (Directory.Exists(dir) && Directory.EnumerateFileSystemEntries(dir).Any())
            return EnsureManifestRoot(dir);

        Directory.CreateDirectory(dir);
        ZipFile.ExtractToDirectory(path, dir, overwriteFiles: true);
        return EnsureManifestRoot(dir);
    }

    /// <summary>
    /// 压缩档整体多包一层目录时下探：根目录没有清单而唯一子目录有，则取子目录。
    /// </summary>
    private static string EnsureManifestRoot(string dir)
    {
        bool HasManifest(string d) =>
            File.Exists(Path.Combine(d, "manifest.json")) ||
            File.Exists(Path.Combine(d, "pack_manifest.json")) ||
            File.Exists(Path.Combine(d, "pack.mcmeta"));
        if (HasManifest(dir)) return dir;
        var subs = Directory.GetDirectories(dir);
        return subs.Length == 1 && HasManifest(subs[0]) ? subs[0] : dir;
    }

    // ---------------- options.txt 启用/关闭（Java 版） ----------------

    /// <summary>
    /// 改写游戏根目录下 options.txt 的 resourcePacks 列表，启用/关闭指定 zip。
    /// 调用方必须先确认游戏已关闭（否则改动会被游戏退出时覆盖）。
    /// 返回操作结果描述。
    /// </summary>
    public static string ToggleResourcePack(string gameRoot, string zipName, bool enable)
    {
        var path = Path.Combine(gameRoot, "options.txt");
        if (!File.Exists(path))
            throw new FileNotFoundException("该游戏根目录下未找到 options.txt", path);

        var lines = File.ReadAllLines(path);
        var idx = Array.FindIndex(lines, l => l.StartsWith("resourcePacks:", StringComparison.Ordinal));
        var entries = idx >= 0 ? ParseList(lines[idx]["resourcePacks:".Length..]) : new List<string>();

        var entry = "file/" + zipName;
        bool changed;
        if (enable)
        {
            changed = !entries.Contains(entry);
            if (changed) entries.Add(entry);
        }
        else
        {
            changed = entries.RemoveAll(e => e == entry) > 0;
        }
        if (!changed) return enable ? "已处于启用状态" : "未启用，无需关闭";

        var newLine = "resourcePacks:" + FormatList(entries);
        if (idx >= 0) lines[idx] = newLine;
        else lines = lines.Append(newLine).ToArray();

        // options.txt 为 UTF-8 无 BOM，保持原样写回
        File.WriteAllLines(path, lines, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return enable ? "已启用（进入游戏后在资源包设置中置顶即可生效）" : "已关闭";
    }

    /// <summary>解析 options.txt 里形如 ["vanilla","file/a.zip"] 的列表</summary>
    private static List<string> ParseList(string s)
    {
        var result = new List<string>();
        s = s.Trim();
        var a = s.IndexOf('[');
        var b = s.LastIndexOf(']');
        if (a < 0 || b <= a) return result;
        var inner = s[(a + 1)..b];
        foreach (var part in inner.Split(','))
        {
            var t = part.Trim().Trim('"');
            if (t.Length > 0) result.Add(t);
        }
        return result;
    }

    private static string FormatList(List<string> entries) =>
        "[" + string.Join(",", entries.Select(e => $"\"{e}\"")) + "]";

    /// <summary>options.txt 里形如 key:value 的行（value 可能含冒号，取首个冒号切分）</summary>
    public static Dictionary<string, string> ReadOptions(string gameRoot)
    {
        var path = Path.Combine(gameRoot, "options.txt");
        var dict = new Dictionary<string, string>();
        foreach (var line in File.ReadAllLines(path))
        {
            var c = line.IndexOf(':');
            if (c <= 0) continue;
            var k = line[..c].Trim();
            if (k.Length > 0 && !dict.ContainsKey(k))
                dict[k] = line[(c + 1)..].Trim();
        }
        return dict;
    }

    /// <summary>
    /// 把若干键值写回 options.txt（保留其余行与顺序，末尾追加新键）。
    /// 写入前先备份 options.txt.bak；UTF-8 无 BOM 保持原样。
    /// 返回更新的键个数。
    /// </summary>
    public static int WriteOptions(string gameRoot, IReadOnlyDictionary<string, string> values)
    {
        var path = Path.Combine(gameRoot, "options.txt");
        if (!File.Exists(path))
            throw new FileNotFoundException("该游戏根目录下未找到 options.txt", path);

        File.Copy(path, path + ".bak", overwrite: true);
        var lines = File.ReadAllLines(path).ToList();
        var pending = new Dictionary<string, string>(values);
        var changed = 0;
        for (var i = 0; i < lines.Count && pending.Count > 0; i++)
        {
            var c = lines[i].IndexOf(':');
            if (c <= 0) continue;
            var k = lines[i][..c].Trim();
            if (pending.TryGetValue(k, out var v))
            {
                var newLine = k + ":" + v;
                if (newLine != lines[i]) { lines[i] = newLine; changed++; }
                pending.Remove(k);
            }
        }
        foreach (var (k, v) in pending)
        {
            lines.Add(k + ":" + v);
            changed++;
        }
        File.WriteAllLines(path, lines, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return changed;
    }
}

/// <summary>Process 扩展：读完整标准输出</summary>
internal static class ProcessExtensions
{
    public static string ReadToEndOutput(this Process p) => p.StandardOutput.ReadToEnd();
}
