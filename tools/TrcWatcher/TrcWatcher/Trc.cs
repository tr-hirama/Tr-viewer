using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace TrcWatcher;

// ---- TRC データモデル ----
public sealed class TrcPoint { public string Name = ""; public double X, Y; public int NotDisp, Sketch, Del; }
public sealed class TrcLine  { public string Name = "", Layer = "", PnA = "", PnB = ""; public int Del, Sketch, Style = 1; }
public sealed class TrcCircle{ public string Name = "", Layer = ""; public double X, Y, R; public int Fill, Del, Sketch, Photo; }
public sealed class TrcMoji  { public string Name = "", Layer = "", Text = ""; public double X, Y, Height, Angle; public int Del, Sketch; }
public sealed class TrcHedge { public string Name = ""; public double X1, Y1, X2, Y2; public int Del; }
public sealed class TrcVertex{ public double X, Y; }
public sealed class TrcPline { public string Name = ""; public int Del, Sketch; public List<TrcVertex> Pts = new(); }
public sealed class TrcBlock { public string Name = ""; public List<TrcVertex> Pts = new(); }
public sealed class TrcInsert{ public string Name = "", Layer = "", Block = ""; public double X, Y, Angle; public int Del; }
public sealed class TrcLevel { public string Name = "", Bs = "", Fs = "", Remarks = "", Tp = ""; }

public sealed class TrcData
{
    public List<TrcPoint>  Points  = new();
    public List<TrcLine>   Lines   = new();
    public List<TrcCircle> Circles = new();
    public List<TrcMoji>   Mojis   = new();
    public List<TrcHedge>  Hedges  = new();
    public List<TrcPline>  Plines  = new();
    public Dictionary<string, TrcBlock> Blocks = new();
    public List<TrcInsert> Inserts = new();
    public List<TrcLevel>  Levels  = new();
    public Dictionary<string, TrcPoint> PointMap = new();

    public int ShapeCount =>
        Points.Count + Lines.Count + Circles.Count + Mojis.Count + Hedges.Count + Plines.Count + Inserts.Count;
}

/// <summary>
/// TRC の読み込み・パース。Tr-viewer(index.html) の readTrcText / parseTrc を移植。
/// </summary>
public static class TrcReader
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static Encoding Sjis
    {
        get
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(932);
        }
    }

    // ZIP 内 TrDATA.trc / 平文 両対応 (Shift-JIS)
    public static string ReadText(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        if (bytes.Length >= 4 && bytes[0] == 0x50 && bytes[1] == 0x4B && bytes[2] == 0x03 && bytes[3] == 0x04)
        {
            using var ms = new MemoryStream(bytes);
            using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
            ZipArchiveEntry? entry = zip.Entries.FirstOrDefault(e => e.Name == "TrDATA.trc")
                                     ?? zip.Entries.FirstOrDefault();
            if (entry == null) throw new InvalidDataException("ZIP 内にエントリがありません");
            using var es = entry.Open();
            using var outMs = new MemoryStream();
            es.CopyTo(outMs);
            return Sjis.GetString(outMs.ToArray());
        }
        return Sjis.GetString(bytes);
    }

    private static double ToNum(string v)
        => double.TryParse((v ?? "").Trim(), NumberStyles.Float, Inv, out double f) ? f : 0.0;

    private static int ToInt0(string v)
        => int.TryParse((v ?? "").Trim(), out int n) ? n : 0;

    private static string At(List<string> f, int i) => i < f.Count ? f[i] : "";

    // 1 行を CSV 分解 (ダブルクォート対応, 各フィールド trim)
    public static List<string> SplitCsv(string line)
    {
        var outp = new List<string>();
        var sb = new StringBuilder();
        bool inQ = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (inQ)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                    else inQ = false;
                }
                else sb.Append(c);
            }
            else
            {
                if (c == '"') inQ = true;
                else if (c == ',') { outp.Add(sb.ToString().Trim()); sb.Clear(); }
                else sb.Append(c);
            }
        }
        outp.Add(sb.ToString().Trim());
        return outp;
    }

    public static TrcData Parse(string text)
    {
        var trc = new TrcData();
        string? section = null;
        bool inBlockPts = false, inPlinePts = false, inDataSec = false;
        TrcBlock? curBlock = null;
        TrcPline? curPline = null;

        foreach (string raw in text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None))
        {
            string line = raw.Trim();
            if (line.Length == 0) continue;

            if (line[0] == '*' && (line.EndsWith("-Section*") || line.EndsWith("-End*")))
            {
                if (line == "*BlockPoint-Section*") inBlockPts = true;
                else if (line == "*BlockPoint-End*") inBlockPts = false;
                else if (line == "*PlinePoint-Section*") inPlinePts = true;
                else if (line == "*PlinePoint-End*") inPlinePts = false;
                else if (line == "*Section-End*") { section = null; curBlock = null; curPline = null; inDataSec = false; }
                else section = line;
                continue;
            }

            // *SurveyData-Section* (野帳) — 図形/レベル変換には未使用だがセクション制御のため読み飛ばす
            if (section == "*SurveyData-Section*")
            {
                if (line == "*DATA -SECTION") { inDataSec = true; continue; }
                if (line == "*END-SECTION") { inDataSec = false; continue; }
                if (line[0] == '*' || line[0] == '#') continue;
                if (!inDataSec) continue;
                continue;
            }

            List<string> f = SplitCsv(line);
            switch (section)
            {
                case "*Point-Section*":
                    trc.Points.Add(new TrcPoint
                    {
                        Name = At(f, 0), X = ToNum(At(f, 1)), Y = ToNum(At(f, 2)),
                        NotDisp = ToInt0(At(f, 3)), Sketch = ToInt0(At(f, 4)), Del = ToInt0(At(f, 5))
                    });
                    break;
                case "*Line-Section*":
                {
                    int st = ToInt0(At(f, 6)); if (st == 0) st = 1;
                    trc.Lines.Add(new TrcLine
                    {
                        Name = At(f, 0), Layer = At(f, 1), PnA = At(f, 2), PnB = At(f, 3),
                        Del = ToInt0(At(f, 4)), Sketch = ToInt0(At(f, 5)), Style = st
                    });
                    break;
                }
                case "*Circle-Section*":
                    trc.Circles.Add(new TrcCircle
                    {
                        Name = At(f, 0), Layer = At(f, 1), X = ToNum(At(f, 2)), Y = ToNum(At(f, 3)), R = ToNum(At(f, 4)),
                        Fill = ToInt0(At(f, 5)), Del = ToInt0(At(f, 6)), Sketch = ToInt0(At(f, 7)), Photo = ToInt0(At(f, 9))
                    });
                    break;
                case "*Moji-Section*":
                    trc.Mojis.Add(new TrcMoji
                    {
                        Name = At(f, 0), Layer = At(f, 1), Text = At(f, 2), X = ToNum(At(f, 3)), Y = ToNum(At(f, 4)),
                        Height = ToNum(At(f, 5)), Del = ToInt0(At(f, 6)), Sketch = ToInt0(At(f, 7)), Angle = ToNum(At(f, 8))
                    });
                    break;
                case "*Hedge-Section*":
                    trc.Hedges.Add(new TrcHedge
                    {
                        Name = At(f, 0), X1 = ToNum(At(f, 1)), Y1 = ToNum(At(f, 2)),
                        X2 = ToNum(At(f, 3)), Y2 = ToNum(At(f, 4)), Del = ToInt0(At(f, 5))
                    });
                    break;
                case "*Insert-Section*":
                    trc.Inserts.Add(new TrcInsert
                    {
                        Name = At(f, 0), Layer = At(f, 1), Block = At(f, 2), X = ToNum(At(f, 3)), Y = ToNum(At(f, 4)),
                        Angle = ToNum(At(f, 5)), Del = ToInt0(At(f, 6))
                    });
                    break;
                case "*Level-Section*":
                    trc.Levels.Add(new TrcLevel
                    {
                        Name = At(f, 0), Bs = At(f, 1), Fs = At(f, 2), Remarks = At(f, 3),
                        Tp = f.Count > 4 ? At(f, 4) : ""
                    });
                    break;
                case "*Pline-Section*":
                    if (inPlinePts)
                    {
                        curPline?.Pts.Add(new TrcVertex { X = ToNum(At(f, 0)), Y = ToNum(At(f, 1)) });
                    }
                    else
                    {
                        curPline = new TrcPline { Name = At(f, 0), Del = ToInt0(At(f, 1)), Sketch = ToInt0(At(f, 2)) };
                        trc.Plines.Add(curPline);
                    }
                    break;
                case "*Block-Section*":
                    if (inBlockPts)
                    {
                        curBlock?.Pts.Add(new TrcVertex { X = ToNum(At(f, 0)), Y = ToNum(At(f, 1)) });
                    }
                    else
                    {
                        curBlock = new TrcBlock { Name = At(f, 0) };
                        trc.Blocks[At(f, 0)] = curBlock;
                    }
                    break;
            }
        }

        foreach (var p in trc.Points) trc.PointMap[p.Name] = p;
        return trc;
    }
}
