using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace TrcWatcher;

/// <summary>
/// 依存ライブラリ無しで最小 OpenXML (.xlsx) を生成する。
/// セル値が int / double なら数値セル、string なら inlineStr、null は空セル(省略)。
/// </summary>
public static class XlsxWriter
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static readonly string[] Cols = { "A","B","C","D","E","F","G","H","I","J","K","L" };

    private static string XmlEsc(string s) =>
        (s ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    private static string Cell(string colRow, object? val)
    {
        if (val == null) return "";
        if (val is int or long or double or decimal)
        {
            double d = Convert.ToDouble(val, Inv);
            return $"<c r=\"{colRow}\"><v>{d.ToString(Inv)}</v></c>";
        }
        string t = XmlEsc(val.ToString() ?? "");
        return $"<c r=\"{colRow}\" t=\"inlineStr\"><is><t xml:space=\"preserve\">{t}</t></is></c>";
    }

    public static void Write(string path, IReadOnlyList<string> headers, IReadOnlyList<object?[]> rows, string sheetName = "Level")
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        sb.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");

        sb.Append("<row r=\"1\">");
        for (int c = 0; c < headers.Count; c++) sb.Append(Cell($"{Cols[c]}1", headers[c]));
        sb.Append("</row>");

        int r = 1;
        foreach (var row in rows)
        {
            r++;
            sb.Append($"<row r=\"{r}\">");
            for (int c = 0; c < row.Length; c++) sb.Append(Cell($"{Cols[c]}{r}", row[c]));
            sb.Append("</row>");
        }
        sb.Append("</sheetData></worksheet>");
        string sheetXml = sb.ToString();

        const string contentTypes =
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
            "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
            "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
            "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
            "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
            "</Types>";
        const string rootRels =
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
            "</Relationships>";
        string workbook =
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
            $"<sheets><sheet name=\"{XmlEsc(sheetName)}\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>";
        const string workbookRels =
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>" +
            "</Relationships>";

        if (File.Exists(path)) File.Delete(path);
        var enc = new UTF8Encoding(false);
        using var fs = new FileStream(path, FileMode.CreateNew);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);

        void AddEntry(string name, string content)
        {
            var e = zip.CreateEntry(name, CompressionLevel.Optimal);
            using var s = e.Open();
            using var w = new StreamWriter(s, enc);
            w.Write(content);
        }

        AddEntry("[Content_Types].xml", contentTypes);
        AddEntry("_rels/.rels", rootRels);
        AddEntry("xl/workbook.xml", workbook);
        AddEntry("xl/_rels/workbook.xml.rels", workbookRels);
        AddEntry("xl/worksheets/sheet1.xml", sheetXml);
    }
}
