# TrcWatcher (C# / .NET 10 / VS2026)

指定フォルダを監視し、`.trc` ファイルが置かれたら自動で次を生成する常駐コンソールアプリ。

- `<名前>.dxf` … DXF (R12 / AutoCAD R12 ASCII, Shift-JIS)
- `<名前>_level.xlsx` … レベル(水準)再計算結果の Excel ファイル

変換ロジックは Tr-viewer (`dxf-viewer/index.html`) の `parseTrc` / `trcToDxf` / `computeLevels`
を C# へ移植したもの。`.trc` は平文・ZIP版(Tr-CAD保存形式, 内部 `TrDATA.trc`)の両対応。

## 必要環境
- Visual Studio 2026 (または .NET 10 SDK)
- Windows (WinForms のフォルダ選択ダイアログを使用)

## ビルド
```
dotnet build -c Release
```
または `TrcWatcher.slnx` を Visual Studio 2026 で開いて F5。

## 実行
引数なしで起動するとフォルダ選択ダイアログが出ます（①監視フォルダ ②出力先）。

```
TrcWatcher.exe
```

引数指定（ダイアログを出さない）:

| 引数 | 意味 |
|---|---|
| `--watch <パス>` / `-w` | 監視フォルダ |
| `--out <パス>` / `-o`   | 出力先（省略時は監視フォルダと同じ） |
| `--interval <秒>`       | チェック間隔（既定 2 秒） |
| `--once`                | 常駐せず、今ある `.trc` を1回だけ変換して終了 |

例:
```
TrcWatcher.exe --watch D:\claude\trc --out D:\claude\trc\out
TrcWatcher.exe -w D:\claude\trc --once
```

`Ctrl+C` で終了。

## 構成
| ファイル | 役割 |
|---|---|
| `Program.cs`         | エントリ・フォルダ選択・監視ループ |
| `Trc.cs`             | TRC データモデル + 読み込み/パース |
| `LevelCalculator.cs` | レベル(水準)再計算 |
| `DxfWriter.cs`       | DXF (R12) 書き出し |
| `XlsxWriter.cs`      | 依存なし OpenXML (.xlsx) 生成 |
