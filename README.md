# WSL PostgreSQL 管理ツール

.NET 9.0 と Avalonia UI を使用した高性能な PostgreSQL データベース管理ツールです。WSL 内のデータベースを管理・比較するための Windows デスクトップアプリケーションです。

## 機能

### 1. DB設定 (DB設定)
- 複数の PostgreSQL インスタンスの接続文字列を管理
- WSL ディストリビューションの自動検出（`wsl.exe -l -q` を使用）
- 接続設定の追加・削除

### 2. インポート・エクスポート
- **エクスポート**: 
  - 選択したフォルダに `yyyyMMdd_HHmmss` 形式のタイムスタンプサブディレクトリを作成
  - PostgreSQL COPY コマンドを使用した高速 CSV エクスポート
  - 全選択または特定テーブルの選択エクスポート
  
- **インポート**:
  - CSV ファイルを含むフォルダを選択
  - TRUNCATE TABLE でターゲットテーブルをクリア
  - CSV データの全量インポート

### 3. データ比較
- **高性能比較アルゴリズム**:
  - `information_schema.key_column_usage` から主キーを自動取得
  - IDataReader を使用したバッチ読み込み（メモリ効率化）
  - 非主キー列のハッシュ値計算
  - Dictionary<TKey, long> を使用した主キーとハッシュのマッピング
  - .NET 9 の FrozenDictionary を使用した高性能比較

- **比較結果**:
  - **削除 (Deleted)**: 旧データベースにあり、新データベースにない行
  - **追加 (Added)**: 旧データベースになく、新データベースにある行
  - **更新 (Updated)**: 主キーは存在するがハッシュ値が異なる行

- **結果出力**: ClosedXML を使用した Excel レポート生成

## 技術スタック

- **Runtime**: .NET 9.0
- **UI Framework**: Avalonia UI 11.2.0 (MVVM パターン)
- **Database**: Npgsql 9.0.2
- **Excel Processing**: ClosedXML 0.104.1
- **MVVM**: CommunityToolkit.Mvvm 8.3.2

## .NET 9.0 最適化機能

- `FrozenDictionary` を使用した高性能なデータ比較
- `SHA256.HashData` を使用した効率的なハッシュ計算
- バッチ処理によるメモリ効率化（10,000 行単位）
- 非同期ストリーミング処理による UI 応答性の維持

## ビルド方法

```bash
dotnet restore
dotnet build
dotnet run
```

## 使用方法

1. **DB設定タブ**: データベース接続を追加・設定
2. **インポート・エクスポートタブ**: テーブルのエクスポート・インポートを実行
3. **データ比較タブ**: 2つのデータベースを比較し、Excel レポートを生成

## 注意事項

- WSL ディストリビューションの検出には `wsl.exe` が必要です
- 大規模なデータ比較時は、メモリ使用量に注意してください
- PostgreSQL の COPY コマンドを使用するため、適切な権限が必要です

