using CsvHelper;
using CsvHelper.Configuration;
using GaBatch.Commons;
using GaBatch.Commons.Constants;
using GaBatch.Models;
using GaCommon.Commons;
using GaCommon.Commons.BatchParameters;
using GaCommon.Commons.Constants;
using GaCommon.Commons.Services;
using GaCommon.Daos;
using GaCommon.Resources;
using Microsoft.Extensions.Configuration;
using NLog;
using System.Globalization;
using System.Text;
using static GaCommon.Commons.MetaDataModels.MetaDataObjects.MetaDataObjects;

namespace GaBatch.Services
{
    /// <summary>
    /// チェック項目別件数リスト出力
    /// </summary>
    internal class GACH001520BatchService : IBatchService<BatchParameter>
    {
        /// <summary>
        /// ログ
        /// </summary>
        private readonly Logger _logger;

        /// <summary>
        /// DB-Context.
        /// </summary>
        private readonly GaDbContext _dbContext;

        ///---------------------------------------------------------------------
        /// <summary>
        /// バッチパラメータ
        /// </summary>
        ///---------------------------------------------------------------------
        /// <summary>
        /// 演算管理情報オブジェクト CalculationInfo型
        /// </summary>
        private CalculationInfo _calculationInfo = new();

        /// <summary>
        /// 実行者情報オブジェクト UserInfo型
        /// </summary>
        private GaCommon.Commons.UserInfo _userInfo = new();

        /// <summary>
        /// 対象データ情報オブジェクト DataFileInfo型
        /// </summary>
        private DataFileInfo _dataFileInfo = new();

        /// <summary>
        /// 項目別件数リストオブジェクト DataFileInfo型
        /// </summary>
        private DataFileInfo _itemListFileInfo = new();

        ///---------------------------------------------------------------------
        /// <summary>
        /// 支援機能関連サービス
        /// </summary>
        ///---------------------------------------------------------------------
        /// <summary>
        /// メタデータ取得
        /// </summary>
        private readonly GAMD001510 _getMetaDatasetService;

        /// <summary>
        /// 個票データ取得サービス
        /// </summary>
        private readonly GAIO001590GetVoteDataService _gAIO001590GetVoteDataService;

        /// <summary>
        /// 実行結果更新（開始）サービス
        /// </summary>
        private readonly GAES002510Service _startExecutionResultsService;

        /// <summary>
        /// 実行結果更新（終了）サービス
        /// </summary>
        private readonly GAES002520Service _endExecutionResultsService;

        /// <summary>
        /// エンコーディング取得サービス
        /// </summary>
        private readonly GAIO002530GetEncodingService _encodingService;

        ///---------------------------------------------------------------------
        /// <summary>
        /// 定数
        /// </summary>
        ///---------------------------------------------------------------------
        /// <summary>
        /// 範囲文字
        /// </summary>
        private readonly string _rangeChar;

        /// <summary>
        /// 符号設定文字（範囲外）
        /// </summary>
        private readonly string _otherSign;

        /// <summary>
        /// 符号名称設定文字（範囲外）
        /// </summary>
        private readonly string _otherSignName;

        /// <summary>
        ///extension
        /// </summary>
        private readonly string _defaultFileExtension;

        /// <summary>
        ///delimiter
        /// </summary>
        private readonly string _defaultDelimiter;

        /// <summary>
        /// 「△」
        /// </summary>
        private const string TRIANGLE = "△";

        /// <summary>
        /// 「" "」(半角スペース)
        /// </summary>
        private const string SPACE_SINGLE_BYTE = " ";

        private string _msgErr = string.Empty;


        ///---------------------------------------------------------------------
        /// <summary>
        /// 変数
        /// </summary>
        ///---------------------------------------------------------------------
        /// <summary>
        /// 対象データ調査票メタ
        /// </summary>
        private StudyMetaDataset _targetMeta = new();

        /// <summary>
        /// 項目別件数リスト
        /// </summary>
        private readonly List<GACH001520NumberOfItem> _numberOfItems = new();

        /// <summary>
        /// 対象データ（CSV用）
        /// </summary>
        private readonly List<string[]> _targetDataForCSV = new();

        /// <summary>
        /// 対象データ（固定長用）
        /// </summary>
        private readonly List<string> _targetDataForFixed = new();

        /// <summary>
        /// 個票データ取得結果
        /// </summary>
        private (ResultType resultType,
                string voteDataName,
                string saveDir,
                string division1,
                string division2,
                string division3,
                string division4,
                string division5,
                string characterCode,
                string remarks,
                int? dataFormat,
                int? idDataSet) _voteData = new();

        /// <summary>
        /// <see cref="GACH001520BatchService"/>のコンストラクタ
        /// </summary>
        /// <param name="logger">ログ</param>
        /// <param name="dbContext">DB-Context.</param>
        public GACH001520BatchService(NLog.Logger logger, GaDbContext dbContext)
            : this(logger, dbContext, null)
        {
        }

        /// <summary>
        /// <see cref="GACH001520BatchService"/>のコンストラクタ
        /// </summary>
        /// <param name="logger">ログ</param>
        /// <param name="dbContext">DB-Context.</param>
        /// <param name="configurationSection">設定値情報</param>
        public GACH001520BatchService(Logger logger, GaDbContext dbContext, IConfigurationSection? configurationSection)
        {
            this._logger = logger;
            this._dbContext = dbContext;

            // 支援機能関連サービス
            _getMetaDatasetService = new GAMD001510(_dbContext);
            _startExecutionResultsService = new GAES002510Service(logger, _dbContext);
            _endExecutionResultsService = new GAES002520Service(logger, _dbContext);
            _gAIO001590GetVoteDataService = new GAIO001590GetVoteDataService(logger, dbContext);
            _encodingService = new GAIO002530GetEncodingService(logger);

            // configの値を設定
            _rangeChar = configurationSection?["RangeChar"] ?? string.Empty;
            _otherSign = configurationSection?["OtherSign"] ?? string.Empty;
            _otherSignName = configurationSection?["OtherSignName"] ?? string.Empty;
            _defaultFileExtension = configurationSection?["DefaultCSVFileExtension"] ?? string.Empty;
            _defaultDelimiter = configurationSection?["DefaultDelimiter"] ?? string.Empty;
        }

        /// <summary>
        /// Main処理
        /// </summary>
        /// <param name="batchParam">バッチパラメータ</param>
        /// <exception cref="Exception">バッチ処理中に例外が発生した場合</exception>
        public void Execute(BatchParameter? batchParam)
        {
            _logger.Debug(MessageResource.MBECH052);
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            ExecutionStatus executionStatus = ExecutionStatus.Completed;
            bool flagErr = false;

            try
            {
                #region パラメータ取得

                if (batchParam is null)
                {
                    throw new Exception(MessageResource.MBECH026);
                }

                if (batchParam.UserInfo is null ||
                     batchParam.DefinitionInfo is null ||
                     batchParam.CalculationInfo is null ||
                     batchParam.DataFileInfo is null ||
                     batchParam.DataField is null ||
                     batchParam.ToolInfo is null ||
                     batchParam.ToolInfo.CheckInfo is null ||
                     batchParam.ToolInfo.CheckInfo.ItemListFileInfo is null)
                {
                    throw new Exception(MessageResource.MBECH026);
                }

                // パラメータ取得
                _userInfo = batchParam.UserInfo;
                _calculationInfo = batchParam.CalculationInfo;
                _dataFileInfo = batchParam.DataFileInfo;
                _itemListFileInfo = batchParam.ToolInfo.CheckInfo.ItemListFileInfo;

                #endregion パラメータ取得

                #region データ取得処理
                // --------------------------------------------------------------------
                // 実行結果（開始）を更新
                // --------------------------------------------------------------------
                _logger.Debug(MessageResource.MBECH046);
                var start = _startExecutionResultsService.UpdateExecutionResultsStart(_calculationInfo, _userInfo);
                if (start == ResultType.ERR)
                {
                    throw new Exception(MessageResource.MBECH027);
                }

                _logger.Debug(MessageResource.MBECH045);

                // 個票データ取得
                _voteData = _gAIO001590GetVoteDataService
                    .GetVoteData(_calculationInfo.StatCode, _dataFileInfo.DataFileId, _calculationInfo.AccessControl);
                if (_voteData.resultType == ResultType.ERR)
                {
                    throw new Exception(MessageResource.MBECH029);
                }

                // 調査票メタ取得
                int idDataSet = _voteData.idDataSet != null ?
                    (int)_voteData.idDataSet : throw new NullReferenceException(MessageResource.MBECH038);
                
                var _metaData = _getMetaDatasetService.GetStudyMetaDataset(idDataSet, _userInfo) 
                    ?? throw new NullReferenceException(MessageResource.MOECH014);

                // 「△」→半角スペースリプレース 調査メタ.符号
                StudyMetaDataset? wkMetaData = ReplaceTriangleToSpace(_metaData);
                _targetMeta = wkMetaData;

                #endregion データ取得処理

                #region 項目別件数リストデータ編集
                if (_targetMeta.Matters is null || !_targetMeta.Matters.Any())
                {
                    throw new NullReferenceException(MessageResource.MBECH014);
                }

                foreach (var matter in _targetMeta.Matters)
                {
                    // 初期値設定
                    var numberOfItem = new GACH001520NumberOfItem(matter);

                    // バイト数NULLチェック
                    if (_dataFileInfo.DataFormat == (int)DataFormats.Fixed)
                    {
                        if (int.TryParse(matter.Bytes, out int convertedBytes))
                        {
                            numberOfItem.Bytes = convertedBytes;
                        }
                        else
                        {
                            throw new FormatException(MessageResource.MBECH015);
                        }
                    }

                    switch (numberOfItem.DataType)
                    {
                        case MatterDataType.DataType:
                            if (string.IsNullOrEmpty(matter.MinVal) || string.IsNullOrEmpty(matter.MaxVal))
                            {
                                continue;
                            }

                            if (!decimal.TryParse(matter.MinVal, out decimal convertedMinVal)
                                || !decimal.TryParse(matter.MaxVal, out decimal convertedMaxVal))
                            {
                                throw new FormatException(MessageResource.MBECH015);
                            }

                            var detailMinOrMax = new GACH001520NumberOfItemDetail
                            {
                                Sign = matter.MinVal + _rangeChar + matter.MaxVal,
                                Min = convertedMinVal,
                                Max = convertedMaxVal
                            };
                            var detailOther = new GACH001520NumberOfItemDetail
                            {
                                Sign = _otherSign
                            };

                            numberOfItem.NumberOfItemDetails.Add(detailMinOrMax);
                            numberOfItem.NumberOfItemDetails.Add(detailOther);
                            break;
                        case MatterDataType.CodeList:
                            if (matter.CodeList is null || !matter.CodeList.Any())
                            {
                                _msgErr = string.Format(MessageResource.MBECH035, _targetMeta.ID.ToString(), matter.ID.ToString());
                                flagErr = true;
                                break;
                            }

                            foreach (var codeList in matter.CodeList)
                            {
                                numberOfItem.NumberOfItemDetails.Add(new GACH001520NumberOfItemDetail
                                {
                                    Sign = codeList.Value,
                                    SignName = codeList.Content
                                });
                            }

                            break;
                        default:
                            break;
                    }

                    if (flagErr)
                    {
                        break;
                    }
                    else
                    { 
                    _numberOfItems.Add(numberOfItem);
                    }
                }

                #endregion 項目別件数リストデータ編集

                #region 対象データ読み込み処理
                if (!flagErr)
                {
                    switch (this._dataFileInfo.DataFormat)
                    {
                        case (int)DataFormats.Csv:
                            // 対象データ読み込み処理（CSVの場合）
                            List<string[]> _csvData = this.ReadCsvDataList(
                                this._dataFileInfo.SaveDir, this._dataFileInfo.DataName, _dataFileInfo.CharacterCode, out bool flagReadErr);
                            if (flagReadErr)
                            {
                                flagErr = true;
                                break;
                            }

                            _targetDataForCSV.AddRange(_csvData);

                            // フォーマットチェック
                            for (int i = 0; i < _targetDataForCSV.Count; i++)
                            {
                                if (_targetDataForCSV[i].Length < _numberOfItems.Count)
                                {
                                    throw new ArgumentException(string.Format(MessageResource.MBECH057, (i + 1).ToString()));
                                }
                            }

                            break;
                        case (int)DataFormats.Fixed:
                            // 対象データ読み込み処理（固定長の場合）
                            List<string> _fixedData = this.ReadFixedDataList(
                                this._dataFileInfo.SaveDir, this._dataFileInfo.DataName, this._dataFileInfo.CharacterCode);
                            _targetDataForFixed.AddRange(_fixedData);

                            // フォーマットチェック
                            foreach (var numberOfItem in _numberOfItems)
                            {
                                for (int i = 0; i < _targetDataForFixed.Count; i++)
                                {
                                    if (_targetDataForFixed[i].Length < (numberOfItem.Position - 1 + numberOfItem.Bytes))
                                    {
                                        throw new ArgumentException(string.Format(MessageResource.MBECH057, (i + 1).ToString()));
                                    }
                                }
                            }

                            break;
                        default:
                            throw new ArgumentException(MessageResource.MBECH023);
                    }
                    #endregion 対象データ読み込み処理
                }

                if (!flagErr)
                {

                    #region 項目別件数リストループ処理
                    foreach (var numberOfItem in _numberOfItems)
                    {
                        // 対象項目データリスト
                        var targetItemDatas = _dataFileInfo.DataFormat switch
                        {
                            (int)DataFormats.Csv
                                => _targetDataForCSV.Where(row => row.Length >= numberOfItem.Position - 1)
                                .Select(row => row[numberOfItem.Position - 1]).ToList(),
                            (int)DataFormats.Fixed
                                => _targetDataForFixed.Where(row => row.Length >= (numberOfItem.Position - 1 + numberOfItem.Bytes))
                                .Select(row => row.Substring(numberOfItem.Position - 1, numberOfItem.Bytes)).ToList(),
                            _ => throw new ArgumentException(MessageResource.MBECH007)
                        } ?? throw new NullReferenceException(MessageResource.MBECH015);

                        switch (numberOfItem.DataType)
                        {
                            case MatterDataType.CodeList:

                                foreach (var detail in numberOfItem.NumberOfItemDetails)
                                {
                                    detail.Count = targetItemDatas.Count(t => t == detail.Sign);
                                }

                                // 規定外の符号件数カウント処理
                                var signs = numberOfItem.NumberOfItemDetails.Select(detail => detail.Sign).ToList();
                                // 項目別件数詳細リスト.符号に合致したいデータ
                                var unMatchedDatas = targetItemDatas.Where(t => !signs.Contains(t)).Distinct().ToList();

                                if (unMatchedDatas.Any())
                                {
                                    foreach (var unMatchedData in unMatchedDatas)
                                    {
                                        numberOfItem.NumberOfItemDetails.Add(new GACH001520NumberOfItemDetail
                                        {
                                            Sign = unMatchedData,
                                            SignName = _otherSignName,
                                            IsOutside = true,
                                            Count = targetItemDatas.Count(t => t == unMatchedData)
                                        });
                                    }
                                }

                                break;
                            case MatterDataType.DataType:
                                // 数値チェック 数値型以外をはじく
                                List<string>? wkRangeDatas = new();
                                foreach (var wkTargetItem in targetItemDatas)
                                {
                                    if (wkTargetItem != null)
                                    { 
                                        if (GaCommon.Commons.TypeCheckService.NumberTypeCheck(wkTargetItem))
                                        {
                                            wkRangeDatas.Add(wkTargetItem);
                                        }
                                    }
                                }

                                // 範囲チェック
                                var matchedRangeDatas = wkRangeDatas.Where(t => decimal.TryParse(t, out decimal convertedVal)
                                    && numberOfItem.NumberOfItemDetails[0].Min <= convertedVal
                                    && convertedVal <= numberOfItem.NumberOfItemDetails[0].Max).ToList();

                                // 型チェック
                                var minOrMax = numberOfItem.NumberOfItemDetails[0].Sign.Split(_rangeChar);
                                if (!minOrMax[0].ToString().Contains('.')
                                    && !minOrMax[1].ToString().Contains('.')
                                    && minOrMax[0].Length == minOrMax[1].Length)
                                {
                                    matchedRangeDatas
                                        = matchedRangeDatas.Where(m => m.Length == minOrMax[0].Length && m.Length == minOrMax[1].Length).ToList();
                                }

                                if (matchedRangeDatas.Count > 0)
                                {
                                    numberOfItem.NumberOfItemDetails[0].Min = decimal.Parse(matchedRangeDatas.Min()!);
                                    numberOfItem.NumberOfItemDetails[0].Max = decimal.Parse(matchedRangeDatas.Max()!);
                                    numberOfItem.NumberOfItemDetails[0].Count = matchedRangeDatas.Count;
                                    numberOfItem.NumberOfItemDetails[1].Count = targetItemDatas.Count - matchedRangeDatas.Count;
                                }
                                else
                                {
                                    numberOfItem.NumberOfItemDetails[0].Min = 0;
                                    numberOfItem.NumberOfItemDetails[0].Max = 0;
                                    numberOfItem.NumberOfItemDetails[0].Count = 0;
                                    numberOfItem.NumberOfItemDetails[1].Count = targetItemDatas.Count;
                                }

                                break;
                        }
                    }
                }
                #endregion 項目別件数リストループ処理

                if (flagErr)
                {
                    executionStatus = ExecutionStatus.Error;
                }

                // ファイル出力
                this.ExportFile(flagErr, _msgErr);
            }
            catch (Exception ex)
            {
                executionStatus = ExecutionStatus.Error;
                _logger.Error("エラー情報:" + ex.Message);
                _logger.Error(ex.StackTrace);
            }
            finally
            {
                // --------------------------------------------------------------------
                // 実行結果（終了）を更新
                // --------------------------------------------------------------------
                _logger.Debug(MessageResource.MBECH049);

                var result = _endExecutionResultsService.UpdateExecutionResultsEnd(executionStatus, _calculationInfo, _userInfo);
                if (result == ResultType.ERR)
                {
                    _logger.Debug(MessageResource.MBECH050);
                }
                else
                {
                    _logger.Debug(MessageResource.MBECH051);
                }

                _logger.Debug(MessageResource.MBECH053);
            }
        }

        /// <summary>
        /// 対象データ読み込み処理（CSVの場合）
        /// </summary>
        /// <param name="saveDir">データ格納先パス</param>
        /// <param name="dataName">データファイル名</param>
        /// <param name="characterCode">文字コード</param>
        /// <exception cref="NullReferenceException">データが取得できなかった場合</exception>
        /// <exception cref="FormatException">データ形式が該当しなかった場合</exception>
        /// <exception cref="IOException">対象データ読み込み処理中に例外が発生した場合</exception>
        /// <exception cref="Exception">対象データ読み込み処理中に例外が発生した場合</exception>
        private List<string[]> ReadCsvDataList(string saveDir, string dataName, string? characterCode,out bool flagErr)
        {
            int rowNum = 0;
            bool flagBatData = false;
            flagErr = false;

            // 返却用CSVデータ
            var csvDatas = new List<string[]>();
            // CSVのファイルを読み込みする
            var configCSV = GAESCommon.CSVTokenSettinng(_targetMeta.QuoteMark);
            configCSV.BadDataFound = x =>{flagBatData = true;};

            // ファイルパス取得
            var dataInfoFilePath = this.GetFullFilePath(saveDir, dataName, DataFormats.Csv);

            if (!File.Exists(dataInfoFilePath))
            {
                throw new Exception(MessageResource.MBECH032);
            }

            using var streamReader = new StreamReader(dataInfoFilePath, this.GetEncoding(characterCode));
            using var csvReader = new CsvReader(streamReader, configCSV);

            while (csvReader.Read())
            {
                rowNum++;

                var recordData = csvReader.Parser.Record;
                if (flagBatData)
                {
                    _msgErr = string.Format(MessageResource.MBECH056, rowNum.ToString());
                    flagErr = true;
                    return csvDatas;
                }

                if (recordData is not null)
                {
                    csvDatas.Add(recordData);
                }
            }

            return csvDatas;
        }

        /// <summary>
        /// 対象データ読み込み処理（固定長の場合）
        /// </summary>
        /// <param name="saveDir">ファイルディレクトリ</param>
        /// <param name="dataName">ファイル名</param>
        /// <param name="characterCode">文字コード</param>
        /// <exception cref="NullReferenceException">データが取得できなかった場合</exception>
        /// <exception cref="IOException">対象データ読み込み処理中に例外が発生した場合</exception>
        /// <exception cref="Exception">対象データ読み込み処理中に例外が発生した場合</exception>
        private List<string> ReadFixedDataList(string saveDir, string dataName, string? characterCode)
        {
            var dataFixedList = new List<string>();

            // ファイルパス取得
            var dataInfoFilePath = this.GetFullFilePath(saveDir, dataName, DataFormats.Fixed);
            if (!File.Exists(dataInfoFilePath))
            {
                throw new Exception(MessageResource.MBECH032);
            }

            using var reader = new StreamReader(dataInfoFilePath, this.GetEncoding(characterCode));
            string? row = null;
            row = reader.ReadLine();
            if (row is null)
            {
                throw new NullReferenceException(MessageResource.MBECH055);
            }

            dataFixedList.Add(row);

            while ((row = reader.ReadLine()) != null)
            {
                dataFixedList.Add(row);
            }

            return dataFixedList;
        }

        /// <summary>
        /// データ格納先パス + データファイル名取得
        /// </summary>
        /// <param name="saveDir">ファイルディレクトリ</param>
        /// <param name="dataName">ファイル名</param>
        private string GetFullFilePath(string saveDir, string dataName, DataFormats dataType)
        {
            if (string.IsNullOrEmpty(saveDir) || string.IsNullOrEmpty(dataName))
            {
                throw new NullReferenceException(MessageResource.MBECH039);
            }

            string extensionPart = Path.GetExtension(dataName);
            string finalPath = Path.Combine(saveDir,dataName);
            if (dataType == DataFormats.Csv)
            {
                extensionPart = extensionPart.Equals("") ? _defaultFileExtension : "";
                finalPath += extensionPart;
            }

            return finalPath;
        }

        /// <summary>
        /// エンコーディング取得
        /// </summary>
        /// <param name="characterCode">文字コード</param>
        /// <returns>エンコーディング</returns>
        /// <exception cref="NullReferenceException">文字コードが設定されていなかった場合</exception>
        private Encoding GetEncoding(string? characterCode)
        {
            if (string.IsNullOrEmpty(characterCode))
            {
                throw new NullReferenceException(MessageResource.MBECH054);
            }

            var (resultType, encoding) = _encodingService.GetEncoding(characterCode);
            if (resultType is ResultType.ERR || encoding is null)
            {
                throw new NullReferenceException(MessageResource.MBECH054);
            }

            return encoding;
        }

        /// <summary>
        /// CSV設定情報取得
        /// </summary>
        /// <param name="characterCode">文字コード</param>
        /// <returns>CSV設定</returns>
        private CsvConfiguration GetConfig()
        {
            return new CsvConfiguration(CultureInfo.CurrentCulture)
            {
                Delimiter = _defaultDelimiter                // 区切り文字(default:#)
            };
        }

        /// <summary>
        /// ファイル出力
        /// </summary>
        /// <param name="flagErr">エラーフラグ</param>
        /// <param name="msg">エラーメッセージ</param>
        /// <exception cref="NullReferenceException">データが取得できなかった場合</exception>
        /// <exception cref="FormatException">データ形式が該当しなかった場合</exception>
        /// <exception cref="IOException">ファイル出力中に例外が発生した場合</exception>
        /// <exception cref="Exception">ファイル出力中に例外が発生した場合</exception>
        private void ExportFile(bool flagErr,string msg)
        {
            // 格納先パス取得
            var dataInfoFilePath = this.GetFullFilePath(_itemListFileInfo.SaveDir, _itemListFileInfo.DataName, DataFormats.Csv);
            // 行カウント
            var rowCount = 1;
            // CSV設定
            var configCsv = this.GetConfig();

            // ヘッダー
            var headers = new string[] { "No", "事項ID", "事項番号", "事項名（大分類）", "事項名（中分類）", "事項名", "符号", "符号内容", "件数" };

            using var writer = new StreamWriter(dataInfoFilePath,false, this.GetEncoding(CharCodeName.SHIFT_JIS));
            using var csv = new CsvWriter(writer, configCsv);
            foreach (var header in headers)
            {
                csv.WriteField(header);
                writer.Flush();
            }

            csv.NextRecord();

            // エラー
            if (flagErr)
            {
                csv.WriteField(msg);
                writer.Flush();

                return;
            }


            // 項目別件数詳細リストループ処理
            foreach (var numberOfItem in _numberOfItems)
            {
                if (numberOfItem.NumberOfItemDetails is null || !numberOfItem.NumberOfItemDetails.Any())
                {
                    continue;
                }

                foreach (var detail in numberOfItem.NumberOfItemDetails.Where(n => !n.IsOutside))
                {
                    // 項目別件数詳細リスト書き込み処理
                    csv.WriteField(rowCount);
                    csv.WriteField(numberOfItem.ItemID);
                    csv.WriteField(numberOfItem.MatterNum);
                    csv.WriteField(numberOfItem.MatterMajorClassificationsName);
                    csv.WriteField(numberOfItem.MatterMediumClassificationsName);
                    csv.WriteField(numberOfItem.MatterName);

                    csv.WriteField(detail.Sign);
                    csv.WriteField(detail.SignName);
                    csv.WriteField(detail.Count);
                    writer.Flush();

                    csv.NextRecord();
                    rowCount++;
                }

                // データタイプ種別チェック
                switch (numberOfItem.DataType)
                {
                    case MatterDataType.DataType:
                        // 最小値行
                        if (numberOfItem.MinValue.Equals(null))
                        {
                            throw new NullReferenceException(MessageResource.MBECH031);
                        }

                        csv.WriteField(rowCount);
                        csv.WriteField(numberOfItem.ItemID);
                        csv.WriteField(numberOfItem.MatterNum);
                        csv.WriteField(numberOfItem.MatterMajorClassificationsName);
                        csv.WriteField(numberOfItem.MatterMediumClassificationsName);
                        csv.WriteField(numberOfItem.MatterName);
                        csv.WriteField(numberOfItem.NumberOfItemDetails?[0].Min);
                        csv.WriteField("最小値");
                        csv.WriteField(null);
                        writer.Flush();

                        csv.NextRecord();
                        rowCount++;

                        // 最大値行
                        if (numberOfItem.MaxValue.Equals(null))
                        {
                            throw new NullReferenceException(MessageResource.MBECH031);
                        }

                        csv.WriteField(rowCount);
                        csv.WriteField(numberOfItem.ItemID);
                        csv.WriteField(numberOfItem.MatterNum);
                        csv.WriteField(numberOfItem.MatterMajorClassificationsName);
                        csv.WriteField(numberOfItem.MatterMediumClassificationsName);
                        csv.WriteField(numberOfItem.MatterName);
                        csv.WriteField(numberOfItem.NumberOfItemDetails?[0].Max);
                        csv.WriteField("最大値");
                        csv.WriteField(null);
                        writer.Flush();

                        csv.NextRecord();

                        rowCount++;

                        break;
                    case MatterDataType.CodeList:
                        var coutsides = numberOfItem.NumberOfItemDetails!.Where(n => n.IsOutside).OrderBy(n => n.Sign).ToList();

                        if (!coutsides.Any())
                        {
                            break;
                        }

                        for (var i = 0; i < coutsides.Count; i++)
                        {
                            csv.WriteField(rowCount);
                            csv.WriteField(numberOfItem.ItemID);
                            csv.WriteField(numberOfItem.MatterNum);
                            csv.WriteField(numberOfItem.MatterMajorClassificationsName);
                            csv.WriteField(numberOfItem.MatterMediumClassificationsName);
                            csv.WriteField(numberOfItem.MatterName);

                            csv.WriteField(coutsides[i].Sign);
                            csv.WriteField(coutsides[i].SignName);
                            csv.WriteField(coutsides[i].Count);
                            writer.Flush();

                            csv.NextRecord();
                            rowCount++;
                        }

                        break;
                    default:
                        throw new Exception(MessageResource.MBECH031);
                }
            }
        }

        /// <summary>
        /// 「符号」「変換文字列」
        /// 「△」→半角スペースに置換
        /// </summary>
        /// <param name="paramMeta">調査票メタデータセット</param>
        static private StudyMetaDataset ReplaceTriangleToSpace(StudyMetaDataset paramMeta)
        {
            StudyMetaDataset retMeta = paramMeta;


            // #86：△→半角スペース変換
            // 調査メタ.符号

            if (retMeta.Matters is not null)
            {
                foreach (var wkMatters in retMeta.Matters)
                {
                    if (wkMatters.CodeList is null)
                    {
                        continue;
                    }

                    foreach (Sign wkCodelist in wkMatters.CodeList)
                    {
                        if (!string.IsNullOrEmpty(wkCodelist.Value) && wkCodelist.Value.Contains(TRIANGLE))
                        {
                            wkCodelist.Value = wkCodelist.Value.Replace(TRIANGLE, SPACE_SINGLE_BYTE);
                        }
                    }
                }
            }

            return retMeta;
        }
    }
}
