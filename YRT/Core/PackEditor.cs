using System.IO;
using System.IO.Compression;

namespace YRT.Core;

/// <summary>编辑页文件条目。</summary>
public sealed record EditFileInfo(string RelPath, string Name, long Size)
{
    public string SizeText => Size switch
    {
        >= 1 << 20 => $"{Size / 1024.0 / 1024.0:0.0} MB",
        >= 1 << 10 => $"{Size / 1024.0:0.0} KB",
        _ => $"{Size} B",
    };

    /// <summary>按路径与扩展名归类（编辑页分类筛选用）。</summary>
    public string Category
    {
        get
        {
            var p = RelPath.ToLowerInvariant();
            var ext = Path.GetExtension(p);
            if (p == "pack.mcmeta" || p == "pack.png" || p == "credits.json") return "信息";
            if (p.Contains("textures/") || ext is ".png" or ".jpg" or ".jpeg" or ".tga" or ".webp") return "贴图";
            if (p.Contains("sounds/") || p.Contains("sound/") || ext is ".ogg" or ".wav" or ".mp3" or ".fsb") return "音效";
            if (p.Contains("models/") || p.Contains("blockstates/") || p.Contains("attachables/")) return "模型";
            if (p.Contains("animations/") || p.Contains("animation_controllers")) return "动画";
            if (p.Contains("font/") || ext is ".ttf" or ".otf") return "字体";
            if (p.Contains("lang/") || ext == ".lang") return "语言";
            return "其他";
        }
    }
}

/// <summary>
/// 资源包编辑器：把 zip / 目录导入数据目录 EditWork\ 工作副本，
/// 支持重命名 / 删除 / 替换 / 添加文件与整包导出；pack.mcmeta 用分析页同款编辑器写回。
/// </summary>
public static class PackEditor
{
    /// <summary>工作副本根目录（数据目录 YRT\EditWork\）。</summary>
    public static string WorkRoot => Path.Combine(FirstRun.ResourceDeployer.DataDir, "EditWork");

    /// <summary>
    /// 导入资源包（zip/mcpack/目录）到工作副本，返回（工作根目录, 包根目录）。
    /// 同一来源重复导入会覆盖旧工作副本。
    /// </summary>
    public static (string WorkDir, string PackRoot) Import(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
            throw new FileNotFoundException("路径不存在：" + path);

        Directory.CreateDirectory(WorkRoot);
        var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var stamp = $"{name}_{DateTime.Now:yyyyMMddHHmmss}";
        var work = Path.Combine(WorkRoot, stamp);

        if (Directory.Exists(path))
        {
            CopyDir(path, work);
        }
        else if (PackPacker.IsPackArchive(path))
        {
            Directory.CreateDirectory(work);
            ZipFile.ExtractToDirectory(path, work, overwriteFiles: true);
        }
        else
        {
            throw new InvalidOperationException("仅支持 zip / mcpack / 目录");
        }

        // 来源标记（便于展示与清理）
        File.WriteAllText(Path.Combine(work, ".yrt_source.txt"), path);
        return (work, ResolvePackRoot(work));
    }

    /// <summary>包根定位：根有 pack.mcmeta 用根；否则唯一子目录时下探。</summary>
    public static string ResolvePackRoot(string workDir)
    {
        if (File.Exists(Path.Combine(workDir, "pack.mcmeta"))) return workDir;
        var subs = Directory.GetDirectories(workDir);
        return subs.Length == 1 && !Path.GetFileName(subs[0]).StartsWith(".") ? subs[0] : workDir;
    }

    /// <summary>递归列出包内文件（相对包根，'/' 分隔展示）。</summary>
    public static List<EditFileInfo> ListFiles(string packRoot)
    {
        var list = new List<EditFileInfo>();
        foreach (var f in Directory.EnumerateFiles(packRoot, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(packRoot, f).Replace('\\', '/');
            list.Add(new EditFileInfo(rel, Path.GetFileName(f), new FileInfo(f).Length));
        }
        list.Sort((a, b) => string.Compare(a.RelPath, b.RelPath, StringComparison.OrdinalIgnoreCase));
        return list;
    }

    /// <summary>重命名包内文件（同名冲突抛异常）。</summary>
    public static void RenameFile(string packRoot, string relPath, string newName)
    {
        newName = newName.Trim().Replace('\\', '/');
        if (newName.Length == 0 || newName.EndsWith("/"))
            throw new InvalidOperationException("新名称不能为空");
        var src = SafeJoin(packRoot, relPath);
        var targetRel = relPath.Contains('/')
            ? relPath[..relPath.LastIndexOf('/')] + "/" + newName
            : newName;
        var dst = SafeJoin(packRoot, targetRel);
        if (File.Exists(dst)) throw new IOException("目标已存在：" + targetRel);
        Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
        File.Move(src, dst);
    }

    /// <summary>重命名包根下的一级目录（assets / textures …）。</summary>
    public static void RenameTopDir(string packRoot, string dirName, string newName)
    {
        newName = newName.Trim().Replace('\\', '/');
        var src = SafeJoin(packRoot, dirName);
        if (!Directory.Exists(src)) throw new DirectoryNotFoundException(dirName);
        var dst = SafeJoin(packRoot, newName);
        if (Directory.Exists(dst) || File.Exists(dst)) throw new IOException("目标已存在：" + newName);
        Directory.Move(src, dst);
    }

    /// <summary>删除包内文件。</summary>
    public static void DeleteFile(string packRoot, string relPath) =>
        File.Delete(SafeJoin(packRoot, relPath));

    /// <summary>用外部文件替换包内文件。</summary>
    public static void ReplaceFile(string packRoot, string relPath, string srcFile)
    {
        var dst = SafeJoin(packRoot, relPath);
        File.Copy(srcFile, dst, overwrite: true);
    }

    /// <summary>添加外部文件到包内指定目录（"" = 包根）。</summary>
    public static void AddFile(string packRoot, string srcFile, string subDir = "")
    {
        var dst = SafeJoin(packRoot, string.IsNullOrEmpty(subDir)
            ? Path.GetFileName(srcFile)
            : subDir + "/" + Path.GetFileName(srcFile));
        if (File.Exists(dst)) throw new IOException("包内已存在同名文件：" + Path.GetFileName(dst));
        Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
        File.Copy(srcFile, dst, overwrite: false);
    }

    /// <summary>整包导出为 zip（自动补 pack.mcmeta 可关）。</summary>
    public static void Export(string packRoot, string outZip, bool autoMcmeta = false, string packName = "")
    {
        if (!File.Exists(Path.Combine(packRoot, "pack.mcmeta")))
            autoMcmeta = true; // 包里没有清单时导出前自动补，保证游戏可识别
        PackPacker.PackDir(packRoot, outZip, autoMcmeta, packName);
    }

    /// <summary>清理超过 keepDays 天的旧工作副本。</summary>
    public static void CleanStale(int keepDays = 14)
    {
        try
        {
            if (!Directory.Exists(WorkRoot)) return;
            foreach (var dir in Directory.GetDirectories(WorkRoot))
                if (Directory.GetLastWriteTime(dir) < DateTime.Now.AddDays(-keepDays))
                    Directory.Delete(dir, recursive: true);
        }
        catch
        {
            // 清理失败不影响使用
        }
    }

    /// <summary>相对路径 → 工作目录内绝对路径（防越界）。</summary>
    private static string SafeJoin(string root, string rel)
    {
        rel = rel.Replace('\\', '/').TrimStart('/');
        var full = Path.GetFullPath(Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar)));
        var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
            throw new IOException("非法路径：" + rel);
        return full;
    }

    private static void CopyDir(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var f in Directory.EnumerateFiles(src))
            File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), overwrite: true);
        foreach (var d in Directory.EnumerateDirectories(src))
            CopyDir(d, Path.Combine(dst, Path.GetFileName(d)));
    }
}
