using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace TrcWatcher;

/// <summary>
/// タスクトレイ(通知領域)に常駐し、一定間隔で監視フォルダをスキャンする常駐コンテキスト。
/// コンソールウィンドウは出さず、ログはバルーン通知とログ窓で確認する。
/// </summary>
internal sealed class TrayContext : ApplicationContext
{
    private readonly NotifyIcon _icon;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly Dictionary<string, string> _state;
    private readonly string _outPath;
    private LogForm? _logForm;

    public TrayContext(Dictionary<string, string> state, string watchPath, string outPath, int intervalSeconds)
    {
        _state = state;
        _outPath = outPath;

        var menu = new ContextMenuStrip();
        menu.Items.Add("ログを表示(&L)", null, (_, _) => ShowLog());
        menu.Items.Add("出力先を開く(&O)", null, (_, _) => OpenOutput());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("終了(&X)", null, (_, _) => ExitThread());

        var iconPath = Path.Combine(AppContext.BaseDirectory, "appicon.ico");
        var icon = File.Exists(iconPath) ? new Icon(iconPath) : SystemIcons.Application;

        _icon = new NotifyIcon
        {
            Icon = icon,
            Text = Truncate($"TRC 自動変換ウォッチャ\n監視中: {watchPath}", 63),
            Visible = true,
            ContextMenuStrip = menu,
        };
        _icon.DoubleClick += (_, _) => ShowLog();

        // 以降の Log() をトレイ(バルーン/ログ窓)へ流す
        Program.LogSink = OnLog;

        Program.Log("===== TRC 自動変換ウォッチャ (トレイ常駐) =====", ConsoleColor.Cyan);
        Program.Log($"監視フォルダ : {watchPath}", ConsoleColor.Cyan);
        Program.Log($"出力先       : {outPath}", ConsoleColor.Cyan);
        Program.Log("監視を開始しました。", ConsoleColor.Cyan);

        // 初回スキャン
        Program.ScanOnce(_state);

        _timer = new System.Windows.Forms.Timer { Interval = Math.Max(1, intervalSeconds) * 1000 };
        _timer.Tick += (_, _) => Program.ScanOnce(_state);
        _timer.Start();
    }

    private void OnLog(string line, ConsoleColor color)
    {
        _logForm?.Append(line);
        // 重要イベントだけバルーン通知
        if (color == ConsoleColor.Green)
            _icon.ShowBalloonTip(3000, "変換完了", line, ToolTipIcon.Info);
        else if (color == ConsoleColor.Red)
            _icon.ShowBalloonTip(5000, "変換失敗", line, ToolTipIcon.Error);
    }

    private void ShowLog()
    {
        if (_logForm is null || _logForm.IsDisposed)
            _logForm = new LogForm();
        _logForm.Show();
        _logForm.WindowState = FormWindowState.Normal;
        _logForm.BringToFront();
        _logForm.Activate();
    }

    private void OpenOutput()
    {
        try
        {
            if (Directory.Exists(_outPath))
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_outPath}\"") { UseShellExecute = true });
        }
        catch { /* 失敗しても常駐は継続 */ }
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer?.Stop();
            _timer?.Dispose();
            if (_icon is not null)
            {
                _icon.Visible = false; // 残像アイコンを残さない
                _icon.Dispose();
            }
            _logForm?.Dispose();
        }
        base.Dispose(disposing);
    }
}

/// <summary>ログ表示用の簡易ウィンドウ。閉じても破棄せず隠すだけ。</summary>
internal sealed class LogForm : Form
{
    private readonly TextBox _box;

    public LogForm()
    {
        Text = "TRC ウォッチャ ログ";
        Width = 760;
        Height = 420;
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = true;
        Icon = SystemIcons.Application;

        _box = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            Dock = DockStyle.Fill,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Font = new Font("Consolas", 9f),
            BackColor = Color.FromArgb(24, 24, 24),
            ForeColor = Color.Gainsboro,
        };
        Controls.Add(_box);

        // 「終了」はトレイメニューから。ここで閉じても常駐は継続。
        FormClosing += (_, e) =>
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
            }
        };
    }

    public void Append(string line)
    {
        if (InvokeRequired) { BeginInvoke(() => Append(line)); return; }
        _box.AppendText(line + Environment.NewLine);
    }
}
