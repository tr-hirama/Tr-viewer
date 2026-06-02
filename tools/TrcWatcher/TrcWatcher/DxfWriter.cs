using System.Globalization;
using System.Text;

namespace TrcWatcher;

/// <summary>
/// DXF (R12 / AutoCAD R12 ASCII) 書き出し。Tr-viewer(index.html) trcToDxf 移植。
/// 座標は (値 - 最小) * 1000 (m→mm)。レイヤ先頭文字 K→KYO-0 / R→KYO / H→TATEMONO / 他→COUI。
/// </summary>
public static class DxfWriter
{
    private const double Scale = 1000.0;
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private static readonly string[] Preamble =
    {
        "0","SECTION","2","HEADER",
        "9","$ACADVER","1","AC1009",
        "9","$INSBASE","10","0.0","20","0.0","30","0.0",
        "9","$PDMODE","70","3",
        "9","$PDSIZE","40","250.0",
        "0","ENDSEC",
        "0","SECTION","2","TABLES",
        "0","TABLE","2","LTYPE","70","3",
        "0","LTYPE","2","CONTINUOUS","70","0","3","Solid line","72","65","73","0","40","0.0",
        "0","LTYPE","2","DASHED","70","0","3","Dashed","72","65","73","2","40","0.75","49","0.5","49","-0.25",
        "0","LTYPE","2","HIDDEN","70","0","3","Hidden","72","65","73","2","40","0.45","49","0.25","49","-0.2",
        "0","ENDTAB",
        "0","TABLE","2","LAYER","70","6",
        "0","LAYER","2","0","70","0","62","7","6","CONTINUOUS",
        "0","LAYER","2","KYO-0","70","0","62","4","6","CONTINUOUS",
        "0","LAYER","2","KYO","70","0","62","4","6","CONTINUOUS",
        "0","LAYER","2","TATEMONO","70","0","62","3","6","CONTINUOUS",
        "0","LAYER","2","COUI","70","0","62","2","6","CONTINUOUS",
        "0","LAYER","2","KYOMOJI","70","0","62","1","6","CONTINUOUS",
        "0","ENDTAB",
        "0","TABLE","2","STYLE","70","1",
        "0","STYLE","2","STANDARD","70","0","40","0.0","41","1.0","50","0.0","71","0","42","2.5","3","txt","4","",
        "0","ENDTAB",
        "0","ENDSEC"
    };

    private static string LayerOf(string layer)
    {
        char c = string.IsNullOrEmpty(layer) ? '\0' : char.ToUpperInvariant(layer[0]);
        return c switch { 'K' => "KYO-0", 'R' => "KYO", 'H' => "TATEMONO", _ => "COUI" };
    }

    private static string F3(double x) => x.ToString("0.000", Inv);

    public static string Build(TrcData trc)
    {
        double minX = double.PositiveInfinity, minY = double.PositiveInfinity;
        void Ext(double x, double y) { if (x < minX) minX = x; if (y < minY) minY = y; }

        foreach (var p in trc.Points) Ext(p.X, p.Y);
        foreach (var c in trc.Circles) Ext(c.X - c.R, c.Y - c.R);
        foreach (var m in trc.Mojis) Ext(m.X, m.Y);
        foreach (var h in trc.Hedges) { Ext(h.X1, h.Y1); Ext(h.X2, h.Y2); }
        foreach (var pl in trc.Plines) foreach (var v in pl.Pts) Ext(v.X, v.Y);
        foreach (var ins in trc.Inserts)
            if (trc.Blocks.TryGetValue(ins.Block, out var blk))
                foreach (var v in blk.Pts) Ext(v.X + ins.X, v.Y + ins.Y);

        if (double.IsInfinity(minX) || double.IsNaN(minX)) { minX = 0; minY = 0; }

        string X(double x) => F3((x - minX) * Scale);
        string Y(double y) => F3((y - minY) * Scale);

        int cnt = 256;
        string H() => (cnt++).ToString("X");

        var body = new List<string>();
        void Add(params string[] toks) => body.AddRange(toks);

        void PushPolyline(IEnumerable<(string x, string y)> pts, string layer, bool closed)
        {
            Add("0", "POLYLINE", "8", layer, "5", H(), "66", "1", "70", closed ? "1" : "0",
                "10", "0.0", "20", "0.0", "30", "0.0");
            foreach (var v in pts)
                Add("0", "VERTEX", "8", layer, "5", H(), "10", v.x, "20", v.y, "30", "0.0");
            Add("0", "SEQEND", "8", layer, "5", H());
        }

        foreach (var p in trc.Points)
        {
            if (p.NotDisp != 0) continue;
            Add("0", "POINT", "8", "0", "5", H(), "10", X(p.X), "20", Y(p.Y), "30", "0.0");
            if (!string.IsNullOrEmpty(p.Name))
            {
                string tx = F3((p.X - minX) * Scale - (150 + 250 * (p.Name.Length - 1)));
                string ty = F3((p.Y - minY) * Scale + 300);
                Add("0", "TEXT", "8", "0", "5", H(), "10", tx, "20", ty, "30", "0.0", "40", "500.0", "1", p.Name, "7", "STANDARD");
            }
        }
        foreach (var ln in trc.Lines)
        {
            if (ln.Del != 0) continue;
            if (!trc.PointMap.TryGetValue(ln.PnA, out var a) || !trc.PointMap.TryGetValue(ln.PnB, out var b)) continue;
            Add("0", "LINE", "8", LayerOf(ln.Layer), "5", H());
            if (ln.Style == 2) Add("6", "DASHED");
            Add("10", X(a.X), "20", Y(a.Y), "30", "0.0", "11", X(b.X), "21", Y(b.Y), "31", "0.0");
        }
        foreach (var c in trc.Circles)
        {
            if (c.Del != 0) continue;
            Add("0", "CIRCLE", "8", LayerOf(c.Layer), "5", H(), "10", X(c.X), "20", Y(c.Y), "30", "0.0", "40", F3(c.R * Scale));
        }
        foreach (var m in trc.Mojis)
        {
            if (m.Del != 0 || string.IsNullOrEmpty(m.Text)) continue;
            Add("0", "TEXT", "8", "0", "5", H(), "10", X(m.X), "20", Y(m.Y), "30", "0.0", "40", "200.0", "1", m.Text, "7", "STANDARD");
            if (m.Angle != 0) Add("50", (360 - m.Angle).ToString(Inv));
        }
        foreach (var h in trc.Hedges)
        {
            Add("0", "LINE", "8", "TATEMONO", "5", H(), "10", X(h.X1), "20", Y(h.Y1), "30", "0.0", "11", X(h.X2), "21", Y(h.Y2), "31", "0.0");
        }
        foreach (var pl in trc.Plines)
        {
            if (pl.Pts.Count < 2) continue;
            PushPolyline(pl.Pts.Select(v => (X(v.X), Y(v.Y))), "COUI", false);
        }
        foreach (var ins in trc.Inserts)
        {
            if (!trc.Blocks.TryGetValue(ins.Block, out var blk) || blk.Pts.Count < 2) continue;
            PushPolyline(blk.Pts.Select(v => (X(v.X + ins.X), Y(v.Y + ins.Y))), "0", false);
        }

        var all = new List<string>(Preamble);
        all.AddRange(new[] { "0", "SECTION", "2", "ENTITIES" });
        all.AddRange(body);
        all.AddRange(new[] { "0", "ENDSEC", "0", "EOF" });
        return string.Join("\r\n", all) + "\r\n";
    }
}
