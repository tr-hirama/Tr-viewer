using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace TrcWatcher;

internal static class Program
{
    private static string _watchPath = "";
    private static string _outPath = "";
    private static int _interval = 2;
    private static bool _once = false;

    /// <summary>トレイ常駐時にログを通知領域/ログ窓へ流すためのフック。</summary>
    internal static Action<string, ConsoleColor>? LogSink;

    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int dwProcessId);
    private const int ATTACH_PARENT_PROCESS = -1;

    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        AttachConsole(ATTACH_PARENT_PROCESS); // コンソールから起動された場合のみ親コンソールへ出力
        try { Console.OutputEncoding = new UTF8Encoding(false); } catch { /* WinExe: コンソール無し */ }
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        ParseArgs(args);

        // 監視フォルダ
        if (string.IsNullOrEmpty(_watchPath))
        {
            _watchPath = PickFolder("監視するフォルダを選択してください (.trc がここに置かれたら自動変換)",
                                    Directory.Exists(@"D:\claude\trc") ? @"D:\claude\trc" : "") ?? "";
            if (string.IsNullOrEmpty(_watchPath)) { Log("監視フォルダが選択されませんでした。終了します。", ConsoleColor.Yellow); return 1; }
        }
        if (!Directory.Exists(_watchPath)) { Log($"監視フォルダが存在しません: {_watchPath}", ConsoleColor.Red); return 1; }

        // 出力先
        if (string.IsNullOrEmpty(_outPath))
        {
            _outPath = _once ? _watchPath
                             : (PickFolder("出力先フォルダを選択してください (キャンセルで監視フォルダと同じ場所)", _watchPath) ?? _watchPath);
        }
        Directory.CreateDirectory(_outPath);

        Log("===== TRC 自動変換ウォッチャ (C#) =====", ConsoleColor.Cyan);
        Log($"監視フォルダ : {_watchPath}", ConsoleColor.Cyan);
        Log($"出力先       : {_outPath}", ConsoleColor.Cyan);
        Log("レベル形式   : .xlsx / DXF: R12(SJIS)", ConsoleColor.Cyan);

        // state: フルパス -> "ticks:size"
        var state = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (_once)
        {
            ScanOnce(state);
            Log("完了 (--once)。", ConsoleColor.Cyan);
            return 0;
        }

        // 常駐モード: タスクトレイ(通知領域)に常駐して監視を続ける
        Application.Run(new TrayContext(state, _watchPath, _outPath, _interval));
        return 0;
    }

    private static void ParseArgs(string[] args)
    {
        var positional = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--watch": case "-w": if (i + 1 < args.Length) _watchPath = args[++i]; break;
                case "--out":   case "-o": if (i + 1 < args.Length) _outPath = args[++i]; break;
                case "--interval": if (i + 1 < args.Length && int.TryParse(args[++i], out int n)) _interval = Math.Max(1, n); break;
                case "--once": _once = true; break;
                default: positional.Add(args[i]); break;
            }
        }
        if (string.IsNullOrEmpty(_watchPath) && positional.Count > 0) _watchPath = positional[0];
        if (string.IsNullOrEmpty(_outPath) && positional.Count > 1) _outPath = positional[1];
    }

    internal static void ScanOnce(Dictionary<string, string> state)
    {
        string[] files;
        try { files = Directory.GetFiles(_watchPath, "*.trc", SearchOption.TopDirectoryOnly); }
        catch { return; }

        foreach (string path in files)
        {
            FileInfo fi;
            try { fi = new FileInfo(path); } catch { continue; }
            string sig = $"{fi.LastWriteTimeUtc.Ticks}:{fi.Length}";
            if (state.TryGetValue(path, out string? prev) && prev == sig) continue;
            if (!IsFileReady(path)) continue; // コピー中 / ロック中はスキップ (次回再試行)
            try
            {
                ConvertOne(path, _outPath);
                state[path] = sig;
            }
            catch (Exception ex)
            {
                Log($"変換失敗: {Path.GetFileName(path)} : {ex.Message}", ConsoleColor.Red);
                state[path] = sig; // 同一内容での無限リトライ防止
            }
        }
    }

    private static void ConvertOne(string trcPath, string outDir)
    {
        string baseName = Path.GetFileNameWithoutExtension(trcPath);
        string dxfPath = Path.Combine(outDir, baseName + ".dxf");
        string xlsxPath = Path.Combine(outDir, baseName + "_level.xlsx");

        string text = TrcReader.ReadText(trcPath);
        TrcData trc = TrcReader.Parse(text);

        // --- DXF (R12 / Shift-JIS) ---
        string dxf = DxfWriter.Build(trc);
        File.WriteAllText(dxfPath, dxf, TrcReader.Sjis);

        // --- レベル xlsx ---
        var headers = new[] { "測点", "BS(後視)", "FS(前視)", "TP(中間)", "高さ(GH)", "CK(較差)", "備考" };
        var rows = new List<object?[]>();
        int ckErrors = 0;
        if (trc.Levels.Count > 0)
        {
            foreach (var r in LevelCalculator.Compute(trc.Levels))
            {
                if (r.CkError) ckErrors++;
                rows.Add(new object?[] { r.Name, r.Bs, r.Fs, r.Tp, r.Gh, r.Ck, r.Remarks });
            }
        }
        XlsxWriter.Write(xlsxPath, headers, rows);

        string warn = ckErrors > 0 ? $" (⚠ 較差不一致 {ckErrors} 件)" : "";
        Log($"変換完了: {Path.GetFileName(trcPath)}  → DXF({trc.ShapeCount}図形) / レベル({trc.Levels.Count}点){warn}", ConsoleColor.Green);
        Log($"           {dxfPath}", ConsoleColor.DarkGray);
        Log($"           {xlsxPath}", ConsoleColor.DarkGray);
    }

    private static bool IsFileReady(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
            return true;
        }
        catch { return false; }
    }

    private static string? PickFolder(string description, string defaultPath)
    {
        using var dlg = new FolderBrowserDialog
        {
            Description = description,
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true
        };
        if (!string.IsNullOrEmpty(defaultPath) && Directory.Exists(defaultPath))
            dlg.SelectedPath = defaultPath;
        return dlg.ShowDialog() == DialogResult.OK ? dlg.SelectedPath : null;
    }

    private static readonly object _logLock = new();
    internal static void Log(string msg, ConsoleColor color = ConsoleColor.Gray)
    {
        lock (_logLock)
        {
            string line = $"[{DateTime.Now:HH:mm:ss}] {msg}";
            try
            {
                var prev = Console.ForegroundColor;
                Console.ForegroundColor = color;
                Console.WriteLine(line);
                Console.ForegroundColor = prev;
            }
            catch { /* WinExe: コンソール無し */ }
            LogSink?.Invoke(line, color);
        }
    }
}
