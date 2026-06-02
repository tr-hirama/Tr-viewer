using System.Globalization;

namespace TrcWatcher;

public sealed class LevelResult
{
    public string Name = "", Bs = "", Fs = "", Tp = "", Remarks = "";
    public int? Gh;
    public int? Ck;
    public bool CkError;
}

/// <summary>
/// レベル(水準)再計算。Tr-viewer(index.html) computeLevels = cad-trcad frmReCalc.LevelReCalc 移植。
/// 高さ(GH) は基準点を ±0 とした相対値 (整数 mm)。
/// </summary>
public static class LevelCalculator
{
    public const int YotenLimit = 20; // mm
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private static bool IsNum(string s)
    {
        s = (s ?? "").Trim();
        return s.Length != 0 && double.TryParse(s, NumberStyles.Float, Inv, out _);
    }

    // JS Math.round / VB CInt 相当 (整数 mm 入力前提)
    private static int RoundInt(string s)
    {
        double.TryParse((s ?? "").Trim(), NumberStyles.Float, Inv, out double d);
        return (int)Math.Round(d, MidpointRounding.AwayFromZero);
    }

    public static List<LevelResult> Compute(IReadOnlyList<TrcLevel> levels)
    {
        var fixedPts = new List<(string Name, int Gh)>();
        int levelBS = 0, levelGH = 0;
        var result = new List<LevelResult>();

        for (int idx = 0; idx < levels.Count; idx++)
        {
            var l = levels[idx];
            string nm = (l.Name ?? "").Trim();
            string bs = (l.Bs ?? "").Trim();
            string fs = (l.Fs ?? "").Trim();
            string tp = (l.Tp ?? "").Trim();
            int? gh = null;
            bool yotenCheck = false;

            if (nm == "")
            {
                // 点名なし → スキップ
            }
            else if ("[" + nm + "]" == bs)
            {
                levelGH = 0; gh = 0; // 自己参照 (基準点)
            }
            else if (IsNum(bs))
            {
                if (idx == 0) { levelBS = RoundInt(bs); levelGH = 0; gh = 0; }
                else
                {
                    int ri = fixedPts.FindIndex(r => r.Name == nm);
                    if (ri >= 0) { levelBS = RoundInt(bs); levelGH = fixedPts[ri].Gh; gh = levelGH; }
                }
            }
            else if (bs.StartsWith("[") && (IsNum(fs) || IsNum(tp)))
            {
                string referNm = bs.Substring(1).Replace("]", "");
                int ri = fixedPts.FindIndex(r => r.Name == referNm);
                if (ri >= 0) { gh = fixedPts[ri].Gh + (IsNum(fs) ? RoundInt(fs) : RoundInt(tp)); yotenCheck = true; }
            }
            else if (IsNum(fs))
            {
                gh = levelBS - RoundInt(fs) + levelGH; yotenCheck = true;
            }
            else if (IsNum(tp))
            {
                gh = levelBS - RoundInt(tp) + levelGH; yotenCheck = true;
            }

            int? ck = null;
            bool ckError = false;
            if (yotenCheck && gh != null)
            {
                int pi = fixedPts.FindIndex(r => r.Name == nm);
                if (pi >= 0) { ck = fixedPts[pi].Gh - gh.Value; ckError = Math.Abs(ck.Value) >= YotenLimit; }
            }

            if (nm != "" && gh != null) fixedPts.Add((nm, gh.Value));

            result.Add(new LevelResult
            {
                Name = l.Name ?? "", Bs = l.Bs ?? "", Fs = l.Fs ?? "", Tp = l.Tp ?? "", Remarks = l.Remarks ?? "",
                Gh = gh, Ck = ck, CkError = ckError
            });
        }
        return result;
    }
}
