using System.Globalization;

namespace TrcWatcher;

/// <summary>
/// DXF (R12 / ASCII) 書き出し。Tr-viewer(index.html) trcToDxf を移植し、出力を一致させる。
/// 座標は (値 - (最小 - 3)) * 1000 (m→mm、図面を原点付近へ移動)。
/// レイヤー / 除外規則 / 境界点の塗り● / 図枠 INSERT / コメント MTEXT までビューワーと同一。
/// </summary>
public static class DxfWriter
{
    private const double Scale = 1000.0;
    private const double BoundaryRadius = 0.025; // _BoundaryDiameter(50) / 2000
    private const char SketchSymbol = '_';       // _Project.SketchSymbol 既定
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // trcad の画層を網羅 (index.html DXF_LAYER_TABLE と同一)
    private static readonly (string Name, string Color)[] LayerTable =
    {
        ("0","7"), ("DENCHU","6"), ("KYO-0","4"), ("KYO","4"), ("TATEMONO","3"),
        ("TATEMONO2","3"), ("SETUBI","5"), ("SETUBI2","5"), ("COUI","2"), ("COUZOU","2"),
        ("KYOMOJI","1"), ("RED","1"), ("REBERU","1"), ("UEKOMI","8"), ("TEXT","7"),
    };

    private static readonly (string Name, string Desc, double[] Pat)[] LtypeTable =
    {
        ("CONTINUOUS", "Solid line", Array.Empty<double>()),
        ("HIDDEN-2", "Hidden", new[] { 0.45, -0.2 }),
        ("DASHED-2", "Dashed", new[] { 0.5, -0.25 }),
        ("DASHED-4", "Dashed long", new[] { 1.0, -0.5 }),
    };

    // レイヤーコード(先頭1文字) → DXF画層 (index.html dxf*Layer と同一)
    private static char C0(string? layer) => string.IsNullOrEmpty(layer) ? '\0' : layer[0];

    private static string DxfLineLayer(string? layer) => C0(layer) switch
    {
        'D' => "DENCHU", 'R' => "KYO", 'H' => "TATEMONO", 'T' => "SETUBI", 'U' => "SETUBI2",
        'I' => "TATEMONO2", 'G' => "COUI", 'Q' => "COUZOU", 'Y' => "KYO", _ => "COUI",
    };
    private static string DxfCircleLayer(string? layer) => C0(layer) switch
    {
        'D' => "DENCHU", 'T' or 'B' => "SETUBI", 'U' => "SETUBI2", _ => "COUI",
    };
    private static string DxfMojiLayer(string? layer) => C0(layer) switch
    {
        'D' => "DENCHU", 'K' => "KYO-0", 'R' => "KYO", 'H' => "TATEMONO", 'Y' => "KYO",
        'B' => "SETUBI", 'Z' => "COUZOU", 'U' => "COUI", 'T' => "TATEMONO", 'M' => "TATEMONO2",
        'S' => "RED", 'L' => "REBERU", _ => "COUI",
    };
    private static string DxfInsertLayer(string? layer) => C0(layer) switch
    {
        'D' => "DENCHU", 'K' => "KYO-0", 'R' => "KYO", 'H' => "TATEMONO",
        'T' => "SETUBI", 'U' => "SETUBI2", _ => "COUI",
    };

    private static string F3(double x) => x.ToString("0.000", Inv);

    // JS String(number) 相当 (整数なら小数点なし)
    private static string Js(double v) => v.ToString("R", Inv);

    private static List<string> HeaderTables()
    {
        var a = new List<string>
        {
            "0","SECTION","2","HEADER",
            "9","$ACADVER","1","AC1009",
            "9","$INSBASE","10","0.0","20","0.0","30","0.0",
            "9","$PDMODE","70","3",
            "9","$PDSIZE","40","250.0",
            "0","ENDSEC",
            "0","SECTION","2","TABLES",
            "0","TABLE","2","LTYPE","70",LtypeTable.Length.ToString(Inv),
        };
        foreach (var (nm, desc, pat) in LtypeTable)
        {
            a.AddRange(new[] { "0","LTYPE","2",nm,"70","0","3",desc,"72","65","73",pat.Length.ToString(Inv),
                "40",F3(pat.Sum(Math.Abs)) });
            foreach (double v in pat) { a.Add("49"); a.Add(Js(v)); }
        }
        a.AddRange(new[] { "0","ENDTAB","0","TABLE","2","LAYER","70",LayerTable.Length.ToString(Inv) });
        foreach (var (nm, col) in LayerTable)
            a.AddRange(new[] { "0","LAYER","2",nm,"70","0","62",col,"6","CONTINUOUS" });
        a.AddRange(new[]
        {
            "0","ENDTAB",
            "0","TABLE","2","STYLE","70","2",
            "0","STYLE","2","STANDARD","70","0","40","0.0","41","1.0","50","0.0","71","0","42","2.5","3","txt","4","",
            "0","STYLE","2","MSPGothic","70","0","40","0.0","41","1.0","50","0.0","71","0","42","2.5","3","msgothic.ttc","4","",
            "0","ENDTAB","0","ENDSEC",
        });
        return a;
    }

    public static string Build(TrcData trc)
    {
        // 座標基準: 表示点 + 非削除線の端点 から最小値 - 3 (cad-trcad と同じ)
        double minX = double.PositiveInfinity, minY = double.PositiveInfinity;
        void Ext(double x, double y) { if (x < minX) minX = x; if (y < minY) minY = y; }
        foreach (var p in trc.Points) if (p.NotDisp == 0) Ext(p.X, p.Y);
        foreach (var ln in trc.Lines)
        {
            if (ln.Del != 0) continue;
            if (trc.PointMap.TryGetValue(ln.PnA, out var a)) Ext(a.X, a.Y);
            if (trc.PointMap.TryGetValue(ln.PnB, out var b)) Ext(b.X, b.Y);
        }
        if (double.IsInfinity(minX)) { minX = 3; minY = 3; }
        minX -= 3; minY -= 3;

        string X(double x) => F3((x - minX) * Scale);
        string Y(double y) => F3((y - minY) * Scale);

        var ent = new List<string>();
        void Add(params string[] toks) => ent.AddRange(toks);
        var blocks = new List<(string Name, List<string[]> Lines)>(); // BLOCKS セクション用

        // 点 (＋点名 TEXT)
        foreach (var p in trc.Points)
        {
            if ((p.NotDisp != 0 || p.Sketch != 0) && p.Yoten == 0) continue;
            if (string.IsNullOrEmpty(p.Name)) continue;
            if (char.ToUpperInvariant(p.Name[0]) == SketchSymbol) continue;
            Add("0","POINT","8","0","10",X(p.X),"20",Y(p.Y),"30","0.0");
            Add("0","TEXT","8","0",
                "10",F3((p.X - minX) * Scale - (150 + 250 * (p.Name.Length - 1))),
                "20",F3((p.Y - minY) * Scale + 300),"30","0.0",
                "40","500.0","1",p.Name,"7","Standard");
        }

        // 線 (境界K / スケッチS / レベルL は出力しない)
        foreach (var ln in trc.Lines)
        {
            if (ln.Del != 0 || ln.Sketch != 0) continue;
            char c0 = C0(ln.Layer);
            if (c0 is 'S' or 'K' or 'L') continue;
            if (!trc.PointMap.TryGetValue(ln.PnA, out var a) || !trc.PointMap.TryGetValue(ln.PnB, out var b)) continue;
            Add("0","LINE","8",DxfLineLayer(ln.Layer));
            if (ln.Style == 1) Add("6","HIDDEN-2");
            else if (ln.Style == 2) Add("6","DASHED-2");
            else if (ln.Style == 3) Add("6","DASHED-4");
            Add("10",X(a.X),"20",Y(a.Y),"30","0.0","11",X(b.X),"21",Y(b.Y),"31","0.0");
        }

        // 円 (境界K・スケッチ除外。境界点の塗り●は LWPOLYLINE)
        foreach (var c in trc.Circles)
        {
            if (c.Del != 0 || c.Sketch != 0) continue;
            char c0 = C0(c.Layer);
            if (c0 is 'S' or 'K') continue;
            if (Math.Abs(c.R - BoundaryRadius) < 1e-6 && c.Fill == 1)
            {
                double cx = (c.X - minX) * Scale, cy = (c.Y - minY) * Scale;
                string lay = c.PointName.Length > 0 && char.ToUpperInvariant(c.PointName[0]) == 'K' ? "KYOMOJI" : "KYO";
                Add("0","LWPOLYLINE","8",lay,"90","2","70","1","43","75.0",
                    "10",F3(cx - 37.5),"20",F3(cy),"42","1.0",
                    "10",F3(cx + 37.5),"20",F3(cy),"42","1.0");
            }
            else
            {
                Add("0","CIRCLE","8",DxfCircleLayer(c.Layer),
                    "10",X(c.X),"20",Y(c.Y),"30","0.0","40",F3(c.R * Scale));
            }
        }

        // 文字 (スケッチS・レベルL 除外。空文字も trcad は出力する)
        foreach (var m in trc.Mojis)
        {
            if (m.Del != 0 || m.Sketch != 0) continue;
            char c0 = C0(m.Layer);
            if (c0 is 'S' or 'L') continue;
            string lay = DxfMojiLayer(m.Layer);
            string[] rows = (m.Text ?? "").Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            if (rows.Length > 1)
            {
                // 複数行: 1行ずつ TEXT (座標は近似)
                const double LS = 265;
                for (int i = 0; i < rows.Length; i++)
                {
                    Add("0","TEXT","8",lay,
                        "10",X(m.X),"20",F3((m.Y - minY) * Scale + rows.Length * LS / 2 - i * LS - 185),
                        "30","0.0","40","200.0","1",rows[i].Trim(),"72","0","7","MSPGothic");
                }
            }
            else
            {
                Add("0","TEXT","8",lay,"10",X(m.X),"20",Y(m.Y),"30","0.0","40","200.0","1",m.Text ?? "");
                if (m.Angle != 0) Add("50",Js(360 - m.Angle));
                Add("72","4","11",X(m.X),"21",Y(m.Y),"7","MSPGothic");
            }
        }

        // インサート (図枠などのブロック参照。参照ブロックは BLOCKS にも定義する)
        var seenInsBlk = new HashSet<string>();
        foreach (var ins in trc.Inserts)
        {
            if (ins.Del != 0) continue;
            if (!trc.Blocks.TryGetValue(ins.Block, out var blk) || blk.Pts.Count == 0) continue;
            string bname = ins.Block == "Uekomi" ? "KABU1" : ins.Block;
            if (seenInsBlk.Add(bname))
            {
                // ブロック点はインサート位置からの相対座標 (×1000 のみ、min オフセット不要)
                var lines = new List<string[]>();
                for (int i = 1; i < blk.Pts.Count; i++)
                {
                    lines.Add(new[]
                    {
                        F3(blk.Pts[i - 1].X * Scale), F3(blk.Pts[i - 1].Y * Scale),
                        F3(blk.Pts[i].X * Scale), F3(blk.Pts[i].Y * Scale),
                    });
                }
                blocks.Add((bname, lines));
            }
            Add("0","INSERT","8",DxfInsertLayer(ins.Layer),"2",bname,
                "10",X(ins.X),"20",Y(ins.Y),"30","0.0");
            if (ins.Angle != 0) Add("50",Js(-ins.Angle));
        }

        // ポリライン → ブロック定義＋INSERT (cad-trcad と同じ構造)
        foreach (var pl in trc.Plines)
        {
            if (pl.Del != 0 || pl.Pts.Count < 2) continue;
            if (pl.Name.StartsWith("PMADO", StringComparison.Ordinal)) continue;
            var lines = new List<string[]>();
            for (int i = 1; i < pl.Pts.Count; i++)
            {
                lines.Add(new[]
                {
                    F3((pl.Pts[i - 1].X - minX) * Scale), F3((pl.Pts[i - 1].Y - minY) * Scale),
                    F3((pl.Pts[i].X - minX) * Scale), F3((pl.Pts[i].Y - minY) * Scale),
                });
            }
            blocks.Add((pl.Name, lines));
            Add("0","INSERT","8","0","2",pl.Name,"10","0.0","20","0.0","30","0.0");
        }

        // コメント → MTEXT (Check=True を SortNum 順。位置は近似)
        var comments = trc.Comments.Where(c => c.Check).OrderBy(c => c.Sort).ToList();
        if (comments.Count > 0)
        {
            double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
            foreach (var p in trc.Points)
                if (p.NotDisp == 0) { if (p.X > maxX) maxX = p.X; if (p.Y > maxY) maxY = p.Y; }
            if (double.IsInfinity(maxX)) { maxX = minX; maxY = minY; }
            double posY = (maxY - minY) * Scale;
            double posX = (maxX - minX) * Scale + 3000;
            foreach (var c in comments)
            {
                Add("0","MTEXT","8","TEXT","10",F3(posX),"20",F3(posY),"30","0.0",
                    "40","250.0","1",c.Text.Replace("|", "\\P"));
                posY -= c.Text.Split('|').Length * 250 + 800;
            }
        }

        // 組み立て: HEADER/TABLES + BLOCKS + ENTITIES + EOF
        var all = HeaderTables();
        all.AddRange(new[] { "0","SECTION","2","BLOCKS" });
        foreach (var (name, lines) in blocks)
        {
            all.AddRange(new[] { "0","BLOCK","8","0","2",name,"70","0","10","0.0","20","0.0","30","0.0","3",name });
            foreach (var l in lines)
                all.AddRange(new[] { "0","LINE","8","0","10",l[0],"20",l[1],"30","0.0","11",l[2],"21",l[3],"31","0.0" });
            all.AddRange(new[] { "0","ENDBLK","8","0" });
        }
        all.AddRange(new[] { "0","ENDSEC","0","SECTION","2","ENTITIES" });
        all.AddRange(ent);
        all.AddRange(new[] { "0","ENDSEC","0","EOF" });
        return string.Join("\r\n", all) + "\r\n";
    }
}
