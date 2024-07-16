using CsvHelper;
using CsvHelper.Configuration;
using GaBatch.Commons;
using GaBatch.Commons.Constants;
using GaBatch.Models;
using GaCommon.Common.Constants;
using GaCommon.Commons;
using GaCommon.Commons.BatchParameters;
using GaCommon.Commons.Constants;
using GaCommon.Commons.DefinitionObjects.GACH;
using GaCommon.Commons.Services;
using GaCommon.Commons.Services.DefinitionServices;
using GaCommon.Daos;
using GaCommon.Resources;
using Microsoft.Extensions.Configuration;
using NLog;
using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using static GaCommon.Commons.MetaDataModels.MetaDataObjects.MetaDataObjects;
using Path = System.IO.Path;

namespace GaBatch.Services
{
    /// <summary>
    /// チェックタスク実行
    /// </summary>
    internal class GACH001510BatchService : IBatchService<BatchParameter>
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
        /// 定数
        /// </summary>
        ///---------------------------------------------------------------------
        /// <summary>
        /// メッセージ 「位置」
        /// </summary>
        private const string MSG_POSITION = "位置";

        /// <summary>
        /// メッセージ 「バイト長」
        /// </summary>
        private const string MSG_BYTES = "バイト長";

        /// <summary>
        /// メッセージ 「対象データ」
        /// </summary>
        private const string MSG_TARGET_DATA = "対象データ";


        /// <summary>
        /// メッセージ 「参照データ」
        /// </summary>
        private const string MSG_REFERENCE_DATA = "参照データ";


        /// <summary>
        /// 「△」
        /// </summary>
        private const string TRIANGLE = "△";

        /// <summary>
        /// 「" "」(半角スペース)
        /// </summary>
        private const string SPACE_SINGLE_BYTE = " ";


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
        private UserInfo _userInfo = new();

        /// <summary>
        /// 定義情報オブジェクト DefinitionInfo型
        /// </summary>
        private DefinitionInfo _definitionInfo = new();

        /// <summary>
        /// 対象データ情報オブジェクト DataFileInfo型
        /// </summary>
        private DataFileInfo _dataFileInfo = new();

        /// <summary>
        /// データ領域オブジェクト DataField型
        /// </summary>
        private DataField _dataField = new();

        /// <summary>
        /// エラーリストオブジェクト DataFileInfo型
        /// </summary>
        private DataFileInfo _errorListFileInfo = new();

        /// <summary>
        /// 参照データ取得結果フラグ
        /// </summary>
        private bool _flagContinue;

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
        /// チェックツールサービス
        /// </summary>
        ///---------------------------------------------------------------------

        private readonly GACHDefinitionService _gACHDefinitionService;

        ///---------------------------------------------------------------------
        /// <summary>
        /// 定数
        /// </summary>
        ///---------------------------------------------------------------------        
        /// <summary>
        /// 参照オフコード
        /// </summary>
        private readonly string _referenceOffCode;

        /// <summary>
        ///","固定
        /// </summary>
        private readonly string _commaCode;

        /// <summary>
        ///"-"固定
        /// </summary>
        private readonly string _hyphenCode;

        /// <summary>
        ///extension
        /// </summary>
        private readonly string _defaultFileExtension;

        /// <summary>
        ///delimiter
        /// </summary>
        private readonly string _defaultDelimiter;


 
        ///---------------------------------------------------------------------
        /// <summary>
        /// 変数
        /// </summary>
        ///---------------------------------------------------------------------
        /// <summary>
        /// バッチ用チェック定義情報
        /// </summary>
        private readonly List<GACH001510CheckDefinitionInfo> _updatedDefinitionInfos = new();

        /// <summary>
        /// チェック定義情報取得結果
        /// </summary>
        private GACHDefinition _definitionInfos = new();

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
        /// 対象データ調査票メタ
        /// </summary>
        private StudyMetaDataset _targetMeta = new();

        /// <summary>
        /// 参照ファイルデータリスト
        /// </summary>
        private readonly List<GACH001510ReferenceFileData> _referenceFileDatas = new();

        /// <summary>
        /// エラーリスト
        /// </summary>
        private readonly List<GACH001510ErrorList> _errorLists = new();

        /// <summary>
        /// 対象データ（CSVの場合）
        /// </summary>
        private readonly List<string[]> _targetDataForCSV = new();

        /// <summary>
        /// 対象データ（固定長の場合）
        /// </summary>
        private List<string> _targetDataForFixed = new();

        /// <summary>
        /// 対象データ処理行数
        /// </summary>
        private int _targetDataRowCount = 1;

        /// <summary>
        /// <see cref="GACH001510BatchService"/>のコンストラクタ
        /// </summary>
        /// <param name="logger">ログ</param>
        /// <param name="dbContext">DB-Context.</param>
        public GACH001510BatchService(NLog.Logger logger, GaDbContext dbContext)
            : this(logger, dbContext, null)
        {
        }

        /// <summary>
        /// <see cref="GACH001510BatchService"/>のコンストラクタ
        /// </summary>
        /// <param name="logger">ログ</param>
        /// <param name="dbContext">DB-Context.</param>
        /// <param name="configurationSection">設定値情報</param>
        public GACH001510BatchService(Logger logger, GaDbContext dbContext, IConfigurationSection? configurationSection)
        {
            this._logger = logger;
            this._dbContext = dbContext;

            // 支援機能関連サービス
            _getMetaDatasetService = new GAMD001510(_dbContext);
            _startExecutionResultsService = new GAES002510Service(logger, _dbContext);
            _endExecutionResultsService = new GAES002520Service(logger, _dbContext);
            _gAIO001590GetVoteDataService = new GAIO001590GetVoteDataService(logger, dbContext);
            _encodingService = new GAIO002530GetEncodingService(logger);

            // チェックツールサービス
            _gACHDefinitionService = new GACHDefinitionService(logger, dbContext);

            // configから値を取得
            _referenceOffCode = configurationSection?["ReferenceOffCode"] ?? string.Empty;
            _hyphenCode = configurationSection?["HyphenCode"] ?? string.Empty;
            _commaCode = configurationSection?["SignCommaCode"] ?? string.Empty;
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
            _logger.Debug(MessageResource.MBECH042);
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            ExecutionStatus executionStatus = ExecutionStatus.Completed;
            bool flagErr = false;
#if DEBUG
            var stopwatch = new Stopwatch();
            stopwatch.Start();
#endif

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
                     batchParam.ToolInfo.CheckInfo.ErrorListFileInfo is null)
                {
                    throw new Exception(MessageResource.MBECH026);
                }

                // パラメータ取得
                _userInfo = batchParam.UserInfo;
                _definitionInfo = batchParam.DefinitionInfo;
                _calculationInfo = batchParam.CalculationInfo;
                _dataFileInfo = batchParam.DataFileInfo;
                _dataField = batchParam.DataField;
                _errorListFileInfo = batchParam.ToolInfo.CheckInfo.ErrorListFileInfo;

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
                _logger.Debug("データ取得処理開始");

                // チェック定義情報取得
                var (resultType, definitionInfo) = _gACHDefinitionService.GetDefinition(_definitionInfo.DefinitionId);
                if (resultType is ResultType.ERR)
                {
                    throw new Exception(MessageResource.MBECH028);
                }

                _definitionInfos = ReplaceTriangleToSpace(definitionInfo);

                // 個票データ取得
                _voteData = _gAIO001590GetVoteDataService
                    .GetVoteData(_calculationInfo.StatCode, _dataFileInfo.DataFileId, _calculationInfo.AccessControl);
                if (_voteData.resultType == ResultType.ERR)
                {
                    throw new Exception(MessageResource.MBECH029);
                }

                // 対象データの調査票メタチェック
                if (_voteData.idDataSet is null )
                {
                    _flagContinue = false;
                    this.UnContinueErrorListForBatchErrMessage(MessageResource.MBECH030);
                }
                else
                {
                    _flagContinue = true;
                }

                if (_flagContinue)
                {
                    // 調査票メタ取得
                    var _metaData = _getMetaDatasetService.GetStudyMetaDataset(_definitionInfos.StudyMetaID, _userInfo);

                    if (_metaData != null)
                    {
                        _targetMeta = _metaData;
                    }
                    else
                    {
                        string msg = string.Format(MessageResource.MBECH036, _definitionInfos.StudyMetaID.ToString());
                        this.UnContinueErrorListForBatchErrMessage(msg);

                        _flagContinue = false;
                    }
                }

                if (_flagContinue)
                {
                    // 対象データ読み込み処理（固定長の場合）
                    if (_dataFileInfo.DataFormat is (int)DataFormats.Fixed)
                    {
                        _targetDataForFixed = this.ReadFixedDataList(
                                _dataFileInfo.SaveDir, _dataFileInfo.DataName, _dataFileInfo.CharacterCode, out flagErr);

                        if (flagErr)
                        {
                            _flagContinue = false;
                        }
                    }
                }

#if DEBUG
                    stopwatch.Stop();
                    _logger.Debug($"データ取得{stopwatch.Elapsed}");
                    stopwatch.Restart();
#endif

                    _logger.Debug("データ取得処理終了");

                    #endregion データ取得処理

                if (_flagContinue)
                {
                        // _flagContinue true：正常  false：異常
                        _flagContinue = this.UpdateDefInfoProcess(this._definitionInfos);
                }

                if (_flagContinue)
                {
                    // 定義情報編集処理
                    _logger.Debug("定義情報編集処理開始");
#if DEBUG
                    stopwatch.Stop();
                    _logger.Debug($"定義情報編集処理{stopwatch.Elapsed}");
                    stopwatch.Restart();
#endif

                    _logger.Debug("定義情報編集処理終了");

                    // チェック処理
                    _logger.Debug("チェック処理開始");
                    switch (_dataFileInfo.DataFormat)
                    {
                        case (int)DataFormats.Csv:
                            this.CheckProcessForCSV();
                            break;
                        case (int)DataFormats.Fixed:
                            this.CheckProcessForFixed();
                            break;
                        default:
                            throw new ArgumentException(string.Format(MessageResource.MBECH040, MSG_TARGET_DATA));
                    }
#if DEBUG
                    stopwatch.Stop();
                    _logger.Debug($"チェック処理{stopwatch.Elapsed}");
                    stopwatch.Restart();
#endif
                    _logger.Debug("チェック処理終了");
                }

                // ファイル出力処理
                _logger.Debug("ファイル出力処理開始");
                this.ExportDefInfoFile();

# if DEBUG
                stopwatch.Stop();
                _logger.Debug($"ファイル出力処理{stopwatch.Elapsed}");
                stopwatch.Restart();

# endif

                _logger.Debug("ファイル出力処理終了");

                // 正常終了（エラー検知あり）チェック
                if (_errorLists != null && _errorLists.Count > 0)
                {
                    int errCount = _errorLists.Where(w => w.ErrFlgData != null && !w.ErrFlgData.Equals("")).Count();
                    if (errCount > 0 || !_flagContinue)
                    {
                        executionStatus = ExecutionStatus.CompletedWithErrorDetection;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error("エラー情報:" + ex.Message);
                _logger.Error(ex.StackTrace);
                executionStatus = ExecutionStatus.Error;
            }
            finally
            {
                // --------------------------------------------------------------------
                // 実行結果（終了）を更新
                // --------------------------------------------------------------------
                _logger.Debug(MessageResource.MBECH049);

                string outputParamSaveDir = string.Empty;
                string outputParamDataName = string.Empty;
                if (this._dataField.OutputFileInfo != null)
                {
                    outputParamSaveDir = _dataField.OutputFileInfo.SaveDir;
                    outputParamDataName = _dataField.OutputFileInfo.DataName;
                }

                int? outputParamDataDataType = null;
                string? outputParamDataCharacterCode = string.Empty;
                int? outputParamDataDataFormat = null;
                if (this._dataFileInfo != null)
                {
                    outputParamDataDataType = _dataFileInfo.DataType;
                    outputParamDataCharacterCode = _dataFileInfo.CharacterCode;
                    outputParamDataDataFormat = _dataFileInfo.DataFormat;
                }



                var dataFiles = new List<DataFileInfo>()
                {
                    new()
                    {
                        SaveDir = outputParamSaveDir,
                        DataName = outputParamDataName,
                        DataType = outputParamDataDataType,
                        CharacterCode = outputParamDataCharacterCode,
                        DataFormat = outputParamDataDataFormat
                    }
                };

                var result = _endExecutionResultsService.UpdateExecutionResultsEnd(executionStatus, _calculationInfo, _userInfo, dataFiles);
                if (result == ResultType.ERR)
                {
                    _logger.Debug(MessageResource.MBECH050);
                }
                else
                {
                    _logger.Debug(MessageResource.MBECH051);
                }

                _logger.Debug(MessageResource.MBECH017);

            }
        }

        /// <summary>
        /// データ取得処理（CSV用）
        /// </summary>
        /// <param name="saveDir">データ格納先パス</param>
        /// <param name="dataName">データファイル名</param>
        /// <param name="characterCode">文字コード</param>
        /// <param name="metaData">調査票メタ</param>
        /// <exception cref="NullReferenceException">データが取得できなかった場合</exception>
        /// <exception cref="FormatException">データ形式が該当しなかった場合</exception>
        /// <exception cref="IOException">データ取得処理中に例外が発生した場合</exception>
        /// <exception cref="Exception">データ取得処理中に例外が発生した場合</exception>
        private List<string[]> ReadCsvDataList(string saveDir, string dataName, string? characterCode, out bool flagErr)
        {
            // 返却用CSVデータ
            var csvDatas = new List<string[]>();
            // エンコーディング
            var encoding = this.GetEncoding(characterCode);
            // ファイルパス取得
            var dataInfoFilePath = this.GetFullFilePath(saveDir, dataName, DataFormats.Csv);

            string msg;
            int rowNum;
            flagErr = false;
            bool flagBatData = false;


            if (!File.Exists(dataInfoFilePath))
            {
                throw new FileNotFoundException(string.Format(MessageResource.MBECH069, dataInfoFilePath));
            }

            var configCSV = GAESCommon.CSVTokenSettinng(_targetMeta.QuoteMark);
            configCSV.HasHeaderRecord   = false;    // ヘッダ行無し(1行からレコード行)
            configCSV.IgnoreBlankLines  = false;    // true:空行無視する　false:空行無視しない
            configCSV.BadDataFound = x => { flagBatData = true; }; // フォーマット不正の場合


            using (var streamReader = new StreamReader(new BufferedStream(File.OpenRead(dataInfoFilePath)), encoding))
            using (var csvReader = new CsvReader(streamReader, configCSV))
            {
                rowNum = 0;
                while (csvReader.Read())
                {
                    rowNum++;

                    if (csvReader.Parser.Record!.Length == 1 && string.IsNullOrEmpty(csvReader.Parser.Record![0]))
                    {
                        msg = string.Format(MessageResource.MBECH056, rowNum.ToString());
                        this.UnContinueErrorListForBatchErrMessage(msg);
                        flagErr = true;

                        return csvDatas;
                    }

                    if (flagBatData)
                    {
                        msg = string.Format(MessageResource.MBECH056, rowNum.ToString());
                        this.UnContinueErrorListForBatchErrMessage(msg);
                        flagErr = true;

                        return csvDatas;
                    }

                    csvDatas.Add(csvReader.Parser.Record!);
                }
            }

            return csvDatas;
        }

        /// <summary>
        /// データ取得処理（固定長）
        /// </summary>
        /// <param name="saveDir">データ格納先パス</param>
        /// <param name="dataName">データファイル名</param>
        /// <param name="characterCode">文字コード</param>
        /// <exception cref="NullReferenceException">データが取得できなかった場合</exception>
        /// <exception cref="IOException">データ取得処理中に例外が発生した場合</exception>
        /// <exception cref="Exception">データ取得処理中に例外が発生した場合</exception>
        private List<string> ReadFixedDataList(string saveDir, string dataName, string? characterCode,out bool flagErr)
        {
            // 返却用固定長データ
            var fixedDatas = new List<string>();

            string msg;
            flagErr = false;
            int rowNum;

            // ファイルパス取得
            var dataInfoFilePath = this.GetFullFilePath(saveDir, dataName, DataFormats.Fixed); 
            if(!File.Exists(dataInfoFilePath))
            {
                throw new FileNotFoundException(string.Format(MessageResource.MBECH069, dataInfoFilePath));
            }

            using var reader = new StreamReader(dataInfoFilePath, this.GetEncoding(characterCode));
            string? row = null;
            rowNum = 0;

            while ((row = reader.ReadLine()) != null)
            {
                rowNum++;
                // 空行だった場合
                if (string.IsNullOrEmpty(row))
                {
                    msg = string.Format(MessageResource.MBECH056, rowNum.ToString());
                    this.UnContinueErrorListForBatchErrMessage(msg);
                    flagErr = true;

                    return fixedDatas;
                }

                fixedDatas.Add(row);
            }

            return fixedDatas;
        }

        /// <summary>
        /// 定義情報編集処理
        /// </summary>
        /// <param name="definitionInfos">チェック定義取得結果</param>
        /// <returns>true：正常終了 false：異常終了(後続処理でエラーリスト出力)</returns>
        /// <exception cref="NullReferenceException">定義情報編集処理中に必須情報が取得できなかった場合</exception>
        /// <exception cref="Exception">定義情報編集処理中に参照ファイルデータ取得失敗した場合</exception>
        /// <exception cref="ArgumentException">定義情報編集処理中に参照ファイルデータ形式がCSVと固定長以外の場合</exception>
        private bool UpdateDefInfoProcess(GACHDefinition definitionInfos)
        {
            #region チェック定義情報リストのループ
            if (definitionInfos.CheckDefinitionInfoList == null || definitionInfos.CheckDefinitionInfoList.Count == 0)
            {
                throw new NullReferenceException(MessageResource.MBECH034);
            }

            foreach (var checkDefinitionInfo in definitionInfos.CheckDefinitionInfoList)
            {
                // 参照ファイルデータ取得済チェック
                if(checkDefinitionInfo.CheckType is (int)CheckType.ReferenceCheck
                    && !_referenceFileDatas.Any(r => r.StudyMeta.ID == checkDefinitionInfo.ReferenceFileID))
                {
                    if (checkDefinitionInfo.ReferenceFileID is null)
                    {
                        throw new NullReferenceException(MessageResource.MBECH004);
                    }

                    // 調査票メタ取得支援機能呼出
                    var metaDataSet = _getMetaDatasetService.GetStudyMetaDataset((int)checkDefinitionInfo.ReferenceFileID, _userInfo);
                    if (metaDataSet == null)
                    {
                        string msg = string.Format(MessageResource.MBECH019, checkDefinitionInfo.ReferenceFileID.ToString());
                        this.UnContinueErrorListForBatchErrMessage(msg);
                        _flagContinue = false;
                    }
                    else
                    {// 参照ファイルメタリスト追加処理
                        _referenceFileDatas.Add(
                            new GACH001510ReferenceFileData
                            {
                                StudyMeta = metaDataSet
                            });
                    }

                }

                var wkDefinitionInfo = this.CreateDefInfoForBatch(checkDefinitionInfo,out bool flagErr);

                if (flagErr)
                {
                    return false;
                }
                else
                {
                    _updatedDefinitionInfos.Add(wkDefinitionInfo);
                }
            }

            #endregion

            // 通常チェックのみの場合は、参照ファイル対象データの取得処理を飛ばす
            if (!definitionInfos.CheckDefinitionInfoList.Any(c => c.CheckType == (int)CheckType.ReferenceCheck))
            {
                return true;
            }

            #region 参照ファイル対象データの取得
            // 使用参照データの取得
            var useRefDatas = new TUseRefDataExecutedDao(this._dbContext).SearchUseRefDataExecuted(_calculationInfo.WorkFlowExecId, (short)_calculationInfo.ExecutionOrder);

            // 使用参照データ取得結果チェック
            if (useRefDatas == null || useRefDatas.Count == 0)
            {
                this.UnContinueErrorListForBatchErrMessage(MessageResource.MBECH058);
                return false;
            }

            // 使用参照データ取得結果のループ
            for (var i = 0; i < useRefDatas.Count; i++)
            {
                if (useRefDatas[i].ReferenceDataId is null)
                {
                    throw new NullReferenceException(MessageResource.MBECH032);
                }

                // 個票データ取得
                var (resultType, voteDataName, saveDir,
                    division1, division2, division3, division4, division5,
                    characterCode, remarks, dataFormat, idDataSet) = _gAIO001590GetVoteDataService
                    .GetVoteData(_calculationInfo.StatCode, (int)useRefDatas[i].ReferenceDataId!, _calculationInfo.AccessControl);

                if (resultType is ResultType.ERR)
                {
                    throw new NullReferenceException(MessageResource.MBECH029);
                }

                var referenceFileData = _referenceFileDatas.Where(w => w.StudyMeta.ID == idDataSet).FirstOrDefault();

                if (referenceFileData == null)
                {
                    this.UnContinueErrorListForBatchErrMessage(String.Format(MessageResource.MBECH059, idDataSet.ToString()));
                    return false;
                }

                if (dataFormat is null)
                {
                    throw new NullReferenceException(MessageResource.MBECH010);
                }

                referenceFileData.DataFormat = (DataFormats)dataFormat;

                switch ((DataFormats)dataFormat)
                {
                    // 対象データ読み込み処理（CSVの場合）
                    case DataFormats.Csv:
                        if (referenceFileData.LoadDataForCSV.Any())
                        {
                            break;
                        }

                        List<string[]> _csvData = this.ReadCsvDataList(saveDir, voteDataName, characterCode, out bool flagErr);

                        if (flagErr)
                        {
                            return false;
                        }
                        
                        // 参照ファイルなし
                        if (_csvData == null || _csvData.Count == 0)
                        {
                            return false;
                        }

                        referenceFileData.LoadDataForCSV.AddRange(_csvData);

                        break;
                    // 対象データ読み込み処理（固定長の場合）
                    case DataFormats.Fixed:
                        if (referenceFileData.LoadDataForFixed.Any())
                        {
                            break;
                        }

                        List<string> _fixedData = this.ReadFixedDataList(saveDir, voteDataName, characterCode, out flagErr);

                        if (flagErr)
                        {
                            return false;
                        }

                        // 参照ファイルなし
                        if (_fixedData == null || _fixedData.Count == 0)
                        {
                            return false;
                        }

                        referenceFileData.LoadDataForFixed.AddRange(_fixedData);
                        break;
                    default:
                        throw new ArgumentException(string.Format(MessageResource.MBECH040, MSG_REFERENCE_DATA));
                }
            }

            // 使用参照データチェック
            foreach (var referenceFileData in _referenceFileDatas)
            {
                switch (referenceFileData.DataFormat)
                {
                    case DataFormats.Csv:
                        if (!referenceFileData.LoadDataForCSV.Any())
                        {
                            this.UnContinueErrorListForBatchErrMessage(MessageResource.MBECH060);
                            return false;
                        }

                        break;
                    case DataFormats.Fixed:
                        if (!referenceFileData.LoadDataForFixed.Any())
                        {
                            this.UnContinueErrorListForBatchErrMessage(MessageResource.MBECH060);
                            return false;
                        }

                        break;
                    default:
                        throw new Exception(MessageResource.MBECH037);
                }
            }
            #endregion

            return true;
        }

        /// <summary>
        /// ファイル出力処理
        /// </summary>
        /// <exception cref="NullReferenceException">ファイル出力処理中に必須情報が取得できなかった場合</exception>
        /// <exception cref="IOException">ファイル出力処理にデータ形式がNULLの場合</exception>
        /// <exception cref="ArgumentException">ファイル出力処理にデータ形式がCSVと固定長以外の場合</exception>
        private void ExportDefInfoFile()
        {
            if (_flagContinue)
            {
                int wkDataFormat = 0;
                string wkCharacterCode = string.Empty;

                if (this._dataFileInfo.DataFormat != null)
                {
                    wkDataFormat = (int)_dataFileInfo.DataFormat;
                }

                if (this._dataFileInfo.CharacterCode != null)
                {
                    wkCharacterCode = _dataFileInfo.CharacterCode.ToString();
                }

                if (wkDataFormat == (int)DataFormats.Csv)    //エラーチェック済データ(csv)出力
                {
                    // ファイルパス取得
                    var csvDataInfoFilePath = this.GetFullFilePath
                        (this._dataField.OutputFileInfo?.SaveDir, this._dataField.OutputFileInfo?.DataName, DataFormats.Csv);
                    var configCsv = this.GetCsvConfiguration(false, _targetMeta.QuoteMark);

                    using var writer = new StreamWriter(csvDataInfoFilePath, false, this.GetEncoding(_dataFileInfo.CharacterCode));
                    using var csv = new CsvWriter(writer, configCsv);
                    for (var i = 0; i < _targetDataForCSV.Count; i++)
                    {
                        for (var j = 0; j < _targetDataForCSV[i].Length; j++)
                        {
                            csv.WriteField(_targetDataForCSV[i][j]);
                            writer.Flush();
                        }

                        csv.NextRecord();
                    }
                }
                else   //エラーチェック済データ(固定長)出力
                {
                    // エンコード
                    Encoding encoding = Encoding.GetEncoding(wkCharacterCode);

                    // ファイルパス取得
                    var fixedDataInfoFilePath = this.GetFullFilePath
                            (this._dataField.OutputFileInfo?.SaveDir, _dataField.OutputFileInfo?.DataName, DataFormats.Fixed);

                    using var writer = new StreamWriter(fixedDataInfoFilePath, false, encoding);
                    for (var i = 0; i < _targetDataForFixed.Count; i++)
                    {
                        writer.WriteLine(_targetDataForFixed[i]);
                        writer.Flush();
                    }
                }
            }

            var configCSV = this.GetCsvConfiguration(true);

            // エラーリスト出力
            // パス取得
            var errorFilePath = this.GetFullFilePath(_errorListFileInfo.SaveDir, _errorListFileInfo.DataName, DataFormats.Csv);

            // エラーリスト出力処理
            using (var writer = new StreamWriter(errorFilePath, false, this.GetEncoding(CharCodeName.SHIFT_JIS)))
            using (var csv = new CsvWriter(writer, configCSV))
            {
                csv.WriteRecords(_errorLists);
                writer.Flush();
            }
        }

        /// <summary>
        /// ファイルパス取得
        /// </summary>
        /// <param name="saveDir">データ格納先パス</param>
        /// <param name="dataName">データファイル名</param>
        /// <returns>ファイル絶対パス</returns>
        private string GetFullFilePath(string? saveDir, string? dataName, DataFormats dataType)
        {
            if (string.IsNullOrEmpty(saveDir) || string.IsNullOrEmpty(dataName))
            {
                throw new NullReferenceException(MessageResource.MBECH039);
            }

            string extensionPart = Path.GetExtension(dataName);
            string finalPath = Path.Combine(saveDir, dataName);
            if (dataType is DataFormats.Csv)
            {
                extensionPart = extensionPart.Equals("") ? _defaultFileExtension : "";
                finalPath += extensionPart;
            }

            return finalPath;
        }

        /// <summary>
        /// CSVの設定を取得する
        /// </summary>
        /// <param name="encoding">エンコーディング</param>
        /// <param name="dataFormats">データ形式</param>
        /// <param name="hasHeader">ヘッダー出力有無</param>
        /// <returns></returns>
        private CsvConfiguration GetCsvConfiguration(bool hasHeader, string? quate = null)
        {
            bool boolShouldQuote = false;
            if (!string.IsNullOrEmpty(quate))
            {
                boolShouldQuote = true;
            }

            return new CsvConfiguration(CultureInfo.CurrentCulture)
            {
                Delimiter = _defaultDelimiter,                // 区切り文字(default:#)
                HasHeaderRecord = hasHeader,                  // ヘッダーの出力有無
                IgnoreBlankLines = false,                      // true:空行無視する　false:空行無視しない
                ShouldQuote = (context) => boolShouldQuote
            };
        }

        /// <summary>
        /// エンコーディング取得処理
        /// </summary>
        /// <param name="characterCode">文字コード</param>
        /// <returns>エンコーディング</returns>
        /// <exception cref="NullReferenceException">文字コードがNULLの場合</exception>
        private Encoding GetEncoding(string? characterCode)
        {
            if (string.IsNullOrEmpty(characterCode))
            {
                throw new NullReferenceException(MessageResource.MBECH054);
            }

            var (resultType, encoding) = _encodingService.GetEncoding(characterCode);
            if (resultType == ResultType.ERR || encoding is null)
            {
                throw new NullReferenceException(MessageResource.MBECH054);
            }

            return encoding;
        }

        /// <summary>
        /// バッチ用定義情報作成
        /// </summary>
        /// <param name="gACHCheckDefinitionInfo">チェック定義取得結果</param>
        /// <returns>チェック定義情報</returns>
        /// <exception cref="NullReferenceException">定義情報詳細リスト処理中に必須情報が取得できなかった場合</exception>
        private GACH001510CheckDefinitionInfo CreateDefInfoForBatch(GACHCheckDefinitionInfo gACHCheckDefinitionInfo,out bool flagErr)
        {
            flagErr = false;
            string msg = string.Empty;
            GACH001510CheckDefinitionInfo ret = new();


            //バッチ用定義情報
            var batchCheckInfo = new GACH001510CheckDefinitionInfo
            {
                CheckNum = gACHCheckDefinitionInfo.CheckNum,
                CheckSummary = gACHCheckDefinitionInfo.CheckSummary,
                CheckType = gACHCheckDefinitionInfo.CheckType,
                ReferenceFileID = gACHCheckDefinitionInfo.ReferenceFileID,
            };

            //定義情報詳細リストのループ
            if (gACHCheckDefinitionInfo.CheckInfoDetailList == null
                || !gACHCheckDefinitionInfo.CheckInfoDetailList.Any())
            {
                throw new NullReferenceException(MessageResource.MBECH043);
            }

            // キー情報リストの編集処理
            if (gACHCheckDefinitionInfo.KeyInfoList is not null
                    && gACHCheckDefinitionInfo.KeyInfoList.Any())
            {
                var refMeta = _referenceFileDatas.FirstOrDefault(r => r.StudyMeta.ID == gACHCheckDefinitionInfo.ReferenceFileID) ??
                    throw new NullReferenceException(MessageResource.MBECH002);

                foreach (var keyInfo in gACHCheckDefinitionInfo.KeyInfoList)
                {
                    var checkTargetMatter = GetStMatter(keyInfo.CheckTargetItemID, _targetMeta);
                    var refFileTargetMatter = GetStMatter(keyInfo.RefFileTargetItemID, refMeta.StudyMeta);
                    var keyInfoForAdd = new GACH001510KeyInfo
                    {
                        CheckTargetItemID = keyInfo.CheckTargetItemID,
                        RefFileTargetItemID = keyInfo.RefFileTargetItemID,
                    };

                    // 調査票メタデータ事項チェック
                    if (checkTargetMatter is null
                            || string.IsNullOrEmpty(checkTargetMatter.Position))
                    {
                        // ここから後続の処理をskipして支援機能 終了処理へ飛ぶ
                        msg = string.Format(MessageResource.MBECH033, _targetMeta.ID.ToString(), keyInfo.CheckTargetItemID.ToString());
                        this.UnContinueErrorListForBatchErrMessage(msg);
                        flagErr = true;

                        return ret;
                    }

                    if (!int.TryParse(checkTargetMatter.Position, out int convertedCheckPosition))
                    {
                        msg = string.Format(MessageResource.MBECH048,_targetMeta.ID.ToString(),keyInfo.CheckTargetItemID.ToString(),MSG_POSITION);
                        this.UnContinueErrorListForBatchErrMessage(msg);
                        flagErr = true;

                        return ret;
                    }

                    keyInfoForAdd.CheckTargetItemPosition = convertedCheckPosition;

                    if (!string.IsNullOrEmpty(checkTargetMatter.Bytes))
                    {
                        if (!int.TryParse(checkTargetMatter.Bytes, out int convertedCheckBytes))
                        {
                            msg = string.Format(MessageResource.MBECH048,_targetMeta.ID.ToString(),checkTargetMatter.ID.ToString(),MSG_BYTES);
                            this.UnContinueErrorListForBatchErrMessage(msg);
                            flagErr = true;

                            return ret;
                        }

                        keyInfoForAdd.CheckTargetItemBytes = convertedCheckBytes;
                    }

                    if (refFileTargetMatter is null
                        || string.IsNullOrEmpty(refFileTargetMatter.Position))
                    {
                        msg = string.Format(MessageResource.MBECH033,refMeta.StudyMeta.ID.ToString(),keyInfo.RefFileTargetItemID.ToString());
                        this.UnContinueErrorListForBatchErrMessage(msg);
                        flagErr = true;

                        return ret;

                    }

                    if (!int.TryParse(refFileTargetMatter.Position, out int convertedRefPosition))
                    {
                        msg = string.Format(MessageResource.MBECH048,refMeta.StudyMeta.ID.ToString(), keyInfo.RefFileTargetItemID.ToString(),MSG_POSITION);
                        this.UnContinueErrorListForBatchErrMessage(msg);
                        flagErr = true;

                        return ret;
                    }

                    keyInfoForAdd.RefFileTargetItemPosition = convertedRefPosition;


                    if (!string.IsNullOrEmpty(refFileTargetMatter.Bytes))
                    {
                        if (!int.TryParse(refFileTargetMatter.Bytes, out int convertedRefBytes))
                        {
                            msg = string.Format(MessageResource.MBECH048,refMeta.StudyMeta.ID.ToString(),keyInfo.RefFileTargetItemID.ToString(), MSG_BYTES);
                            this.UnContinueErrorListForBatchErrMessage(msg);
                            flagErr = true;

                            return ret;

                        }

                        keyInfoForAdd.RefFileTargetItemBytes = convertedRefBytes;
                    }

                    batchCheckInfo.KeyInfos.Add(keyInfoForAdd);
                }
            }

            foreach (var checkInfoDetail in gACHCheckDefinitionInfo.CheckInfoDetailList)
            {
                // バッチ用定義情報詳細
                var detailForBatch = new GACH001510CheckInfoDetail(checkInfoDetail);

                if (checkInfoDetail.CheckTargetConditionItemList is null)
                {
                    throw new NullReferenceException(MessageResource.MBECH044);
                }

                // 定義情報詳細.チェック対象条件リストの件数分、以下の処理を繰り返す
                foreach (var checkTargetConditionItem in checkInfoDetail.CheckTargetConditionItemList)
                {
                    if(checkTargetConditionItem is null)
                    {
                        continue;
                    }

                    int wkConditionItemID = gACHCheckDefinitionInfo.CheckTargetConditionItemList.FirstOrDefault
                            (c => c.ConditionNum == checkTargetConditionItem.ConditionNum)?.ConditionItemID ??
                            throw new NullReferenceException(MessageResource.MBECH044);

                    var matter = GetStMatter(wkConditionItemID, _targetMeta);

                    // チェック対象条件の事項取得
                    if (matter is null || string.IsNullOrEmpty(matter.Position))
                    {
                        msg = string.Format(MessageResource.MBECH033,_targetMeta.ID.ToString(),wkConditionItemID.ToString());
                        this.UnContinueErrorListForBatchErrMessage(msg);
                        flagErr = true;

                        return ret;
                    }

                    if (!int.TryParse(matter.Position, out int convertedPosition))
                    {
                        msg = string.Format(MessageResource.MBECH048,_targetMeta.ID.ToString(),wkConditionItemID.ToString(),MSG_POSITION);
                        this.UnContinueErrorListForBatchErrMessage(msg);
                        flagErr = true;

                        return ret;
                    }
                    
                    int wkMatterId = 0;

                    if (matter.ID != null)
                    {
                        wkMatterId = (int)matter.ID;
                    }

                    var checkTargetConditionItemForAdd = new GACH001510TargetCondition
                    {
                        // チェック対象条件番号
                        ConditionNum = checkTargetConditionItem.ConditionNum,
                        // 事項ID
                        MatterId = wkMatterId,
                        // 事項位置
                        MatterPositon = convertedPosition,
                        // 比較条件
                        ComparisonCondition = checkTargetConditionItem.ComparisonCondition,
                        // 符号リスト
                        Signs = this.GetSignList(checkTargetConditionItem.Sign)
                    };

                    if(matter.Bytes is not null
                        && int.TryParse(matter.Bytes, out int convertedBytes))
                    {
                        checkTargetConditionItemForAdd.MatterBytes = convertedBytes;
                    }

                    // チェック対象条件リスト追加
                    detailForBatch.CheckTargetConditionItemList.Add(checkTargetConditionItemForAdd);
                }

                // チェック種別チェック
                switch (gACHCheckDefinitionInfo.CheckType)
                {
                    case (int)CheckType.NormalCheck:
                        if (checkInfoDetail.ItemCheckTargetConditionList == null)
                        {
                            throw new NullReferenceException(MessageResource.MBECH028);
                        }

                        // 項目チェック対象条件リストのループ
                        foreach (var itemCheckTargetCondition in checkInfoDetail.ItemCheckTargetConditionList)
                        {
                            if(itemCheckTargetCondition is null)
                            {
                                continue;
                            }

                            int wkConditionItemID = gACHCheckDefinitionInfo.MultipleCheckConditionItemList.FirstOrDefault(
                                    m => m.ConditionNum == itemCheckTargetCondition.ConditionNum)?.ConditionItemID ??
                                    throw new NullReferenceException(MessageResource.MBECH028);

                            // 項目チェック対象条件事項取得
                            var matter = GetStMatter(wkConditionItemID, _targetMeta);

                            if (matter is null || string.IsNullOrEmpty(matter.Position))
                            {
                                msg = string.Format(MessageResource.MBECH033,_targetMeta.ID.ToString(),wkConditionItemID.ToString());
                                this.UnContinueErrorListForBatchErrMessage(msg);
                                flagErr = true;

                                return ret;
                            }

                            if (!int.TryParse(matter.Position, out int convertedPosition))
                            {
                                msg = string.Format(MessageResource.MBECH048,_targetMeta.ID.ToString(), wkConditionItemID.ToString(),MSG_POSITION);
                                this.UnContinueErrorListForBatchErrMessage(msg);
                                flagErr = true;

                                return ret;
                            }

                            var multipleCheckConditionForAdd = new GACH001510TargetCondition
                            {
                                // チェック対象条件番号
                                ConditionNum = itemCheckTargetCondition.ConditionNum,
                                // 事項位置
                                MatterPositon = convertedPosition,
                                // 比較条件
                                ComparisonCondition = itemCheckTargetCondition.ComparisonCondition,
                                // 符号リスト
                                Signs = this.GetSignList(itemCheckTargetCondition.Sign)
                            };

                            if(matter.Bytes is not null
                                && int.TryParse(matter.Bytes, out int convertedBytes))
                            {
                                multipleCheckConditionForAdd.MatterBytes = convertedBytes;
                            }

                            // 項目チェック対象条件リスト追加処理
                            detailForBatch.ItemCheckTargetConditionList.Add(multipleCheckConditionForAdd);
                        }

                        break;
                    case (int)CheckType.ReferenceCheck:
                        if (checkInfoDetail.RefFileTargetConditionList == null)
                        {
                            throw new NullReferenceException(MessageResource.MBECH068);
                        }

                        // 参照ファイル対象条件の調査票メタ取得
                        var referenceFileData = _referenceFileDatas.FirstOrDefault(r => r.StudyMeta.ID == gACHCheckDefinitionInfo.ReferenceFileID);

                        if (referenceFileData?.StudyMeta is null)
                        {
                            msg = string.Format(MessageResource.MBECH019, gACHCheckDefinitionInfo.ReferenceFileID.ToString());
                            this.UnContinueErrorListForBatchErrMessage(msg);
                            flagErr = true;

                            return ret;
                        }

                        // 参照ファイル対象条件リストのループ
                        foreach (var refFileTargetCondition in checkInfoDetail.RefFileTargetConditionList)
                        {
                            if (refFileTargetCondition is null)
                            {
                                continue;
                            }

                            int wkConditionItemID = gACHCheckDefinitionInfo.RefTargetConditionItemList
                                .FirstOrDefault(r => r.ConditionNum == refFileTargetCondition.ConditionNum)?.ConditionItemID ??
                                throw new NullReferenceException(MessageResource.MBECH009);

                            // 参照ファイル対象条件の事項取得
                            var matter = GetStMatter(wkConditionItemID, referenceFileData.StudyMeta);

                            if (matter is null || string.IsNullOrEmpty(matter.Position))
                            {
                                msg = string.Format(MessageResource.MBECH033, referenceFileData.StudyMeta.ID.ToString(),wkConditionItemID.ToString());
                                this.UnContinueErrorListForBatchErrMessage(msg);
                                flagErr = true;

                                return ret;
                            }

                            if (!int.TryParse(matter.Position, out int convertedPosition))
                            {
                                msg = string.Format(MessageResource.MBECH048,referenceFileData.StudyMeta.ID.ToString(),wkConditionItemID.ToString(),MSG_POSITION);
                                this.UnContinueErrorListForBatchErrMessage(msg);
                                flagErr = true;

                                return ret;
                            }

                            var refTargetConditionItemForAdd = new GACH001510TargetCondition
                            {
                                //チェック対象条件番号
                                ConditionNum = refFileTargetCondition.ConditionNum,
                                // 事項位置
                                MatterPositon = convertedPosition,
                                //比較条件
                                ComparisonCondition = refFileTargetCondition.ComparisonCondition,
                                //符号リスト
                                Signs = this.GetSignList(refFileTargetCondition.Sign)
                            };

                            if (matter.Bytes is not null
                                && int.TryParse(matter.Bytes, out int convertedBytes))
                            {
                                refTargetConditionItemForAdd.MatterBytes = convertedBytes;
                            }

                            // 参照ファイル対象条件リスト追加処理
                            detailForBatch.RefFileTargetConditionList.Add(refTargetConditionItemForAdd);
                        }

                        break;
                }

                // 定義情報詳細.変換文字列リストのループ処理
                foreach (var replaceString in checkInfoDetail.ReplaceStringList)
                {
                    int wkConditionItemID = gACHCheckDefinitionInfo.ReplaceTargetItemList.FirstOrDefault(
                                            r => r.ReplaceTargetNum == replaceString.ReplaceTargetNum)?.ReplaceTargetItemID ??
                                            throw new NullReferenceException(MessageResource.MBECH016);

                    var matter = GetStMatter(wkConditionItemID, _targetMeta);

                    if (matter is null )
                    {
                        msg = string.Format(MessageResource.MBECH033,_targetMeta.ID.ToString(),wkConditionItemID.ToString());
                        this.UnContinueErrorListForBatchErrMessage(msg);
                        flagErr = true;

                        return ret;
                    }

                    detailForBatch.ReplaceStringList.Add(new GACH001510ReplaceString
                    {
                        ReplaceTargetNum = replaceString.ReplaceTargetNum,
                        StMatter = matter,
                        Type = replaceString.Type,
                        ReplaceString = replaceString.ReplaceString
                    });
                }

                batchCheckInfo.CheckInfoDetails.Add(detailForBatch);
            }

            if(gACHCheckDefinitionInfo.CheckType is not (int)CheckType.NormalCheck)
            {
                return batchCheckInfo;
            }

            return batchCheckInfo;
        }

        /// <summary>
        /// 符号リスト作成処理
        /// </summary>
        /// <param name="signStr">符号</param>
        /// <returns>符号リスト</returns>
        /// <exception cref="NullReferenceException">符号がNULLの場合</exception>
        private List<GACH001510Sign> GetSignList(string? signStr)
        {
            if (signStr is null)
            {
                throw new NullReferenceException(MessageResource.MBECH065);
            }

            // 符号配列
            var splitedSignStr = signStr.Split(_commaCode);
            if(splitedSignStr is null || splitedSignStr.Length == 0)
            {
                throw new NullReferenceException(MessageResource.MBECH025);
            }

            // 返却用符号リスト
            var rtnSigns = new List<GACH001510Sign>();
            for(var i = 0; i < splitedSignStr.Length; i++)
            {
                rtnSigns.Add(this.CreateCheckSign(splitedSignStr[i]));
            }

            return rtnSigns;
        }

        /// <summary>
        ///  処理詳細_チェック処理
        /// </summary>
        /// <param name="sign">符号</param>
        /// <returns>返却符号</returns>
        /// <exception cref="NullReferenceException">処理詳細_チェック処理中に必須情報が取得できなかった場合</exception>
        /// <exception cref="FormatException"></exception>
        /// <exception cref="Exception">int.Parse処理失敗した場合、</exception>
        private GACH001510Sign CreateCheckSign(string sign)
        {
            // 返却用符号リスト
            string[] splitedArr;

            // 符号に"-"を含む場合
            if (sign.Contains(_hyphenCode))
            {
                //符号分割処理 (符号配列)
                splitedArr = sign.Split(_hyphenCode);
                if (splitedArr.Length != 2)
                {
                    return CreateOffCodeCheck(sign);
                }

                //符号配列[0]または符号配列[1]が数値型に変換できる場合
                if (decimal.TryParse(splitedArr[0], out _) && decimal.TryParse(splitedArr[1], out _))
                {
                    return new GACH001510Sign
                    {
                        CheckMethodType = CheckMethodType.RangeCheck,
                        Sign1 = splitedArr[0],
                        Sign2 = splitedArr[1]
                    };
                }
                else
                {
                    return CreateOffCodeCheck(sign);
                }
            }
            // 符号に"-"を含まない場合
            else
            {
                return CreateOffCodeCheck(sign);
            }
        }

        /// <summary>
        /// オフコードチェック作成
        /// </summary>
        /// <param name="sign">符号</param>
        /// <returns>符号情報</returns>
        private static GACH001510Sign CreateOffCodeCheck(string sign)
        {
            return new GACH001510Sign
            {
                CheckMethodType = CheckMethodType.OffCodeCheck,
                Sign1 = sign
            };
        }

        /// <summary>
        /// チェック処理（CSV用）
        /// </summary>
        /// <exception cref="Exception">チェック処理中に例外が発生した場合</exception>
        private void CheckProcessForCSV()
        {
            // Exception発生時の出力メッセージ向け
            int rowNum = 0;
            string msg;

            // エンコーディング
            var encoding = this.GetEncoding(_dataFileInfo.CharacterCode);
            // ファイルパス
            var dataInfoFilePath = this.GetFullFilePath(_dataFileInfo.SaveDir, _dataFileInfo.DataName, DataFormats.Csv);
            if (!File.Exists(dataInfoFilePath))
            {
                throw new Exception(string.Format(MessageResource.MBECH069, dataInfoFilePath));
            }

            var configCSV = GAESCommon.CSVTokenSettinng(_targetMeta.QuoteMark);
            configCSV.HasHeaderRecord  = false;    // false：ヘッダ行無し(1行からレコード行)
            configCSV.IgnoreBlankLines = false;    // false：空行無視しない
            configCSV.BadDataFound = x =>{_flagContinue = false;}; // BadDataFoundの場合の処理

            using var streamReader = new StreamReader(new BufferedStream(File.OpenRead(dataInfoFilePath)), encoding);
            using var csvReader = new CsvReader(streamReader, configCSV);


            try
            {
                while (csvReader.Read())
                {
                    rowNum++;

                    // 条件合致参照ファイルレコード(CSV用）
                    string[]? matchedReferenceRecoedForCSV = null;
                    // 条件合致参照ファイルレコード（固定長用）
                    string? matchedReferenceRecoedForFixed = null;

                    // 一行あたりのデータを取得
                    var rowData = csvReader.GetRecord<dynamic>();
                    if (!_flagContinue)
                    {
                        // ここから後続の処理をskipして支援機能 終了処理へ飛ぶ
                        msg = string.Format(MessageResource.MBECH056, rowNum.ToString());
                        this.UnContinueErrorListForBatchErrMessage(msg);

                        return;
                    }

                    // ループ中の対象データ行
                    string[]? targetDataRow = ((IDictionary<string, object>)rowData!).Values.Cast<string>().ToArray();

                    if (targetDataRow.Length == 1 && string.IsNullOrEmpty(targetDataRow[0]))
                    {
                        // ここから後続の処理をskipして支援機能 終了処理へ飛ぶ
                        msg = string.Format(MessageResource.MBECH056, rowNum.ToString());
                        this.UnContinueErrorListForBatchErrMessage(msg);
                        _flagContinue = false;

                        return ;
                    }

                    // バッチ用チェック定義情報リストのループ
                    foreach (var checkDefinition in _updatedDefinitionInfos)
                    {
                        // チェック種別チェック
                        targetDataRow = checkDefinition.CheckType switch
                        {
                            (int)CheckType.NormalCheck => this.NormalCheckProcessForCSV(checkDefinition, targetDataRow),
                            (int)CheckType.ReferenceCheck => this.ReferenceCheckProcessForCSV(checkDefinition, targetDataRow,
                            ref matchedReferenceRecoedForCSV, ref matchedReferenceRecoedForFixed),
                            _ => throw new ArgumentException(MessageResource.MBECH006),
                        };

                        if (!_flagContinue)
                        {
                            return;
                        }
                    }

                    _targetDataForCSV.Add(targetDataRow);
                    _targetDataRowCount++;
                }
            }
            catch 
            {
                throw;
            }
        }

        /// <summary>
        /// チェック処理（固定長）
        /// </summary>
        /// <exception cref="Exception">チェック処理中に例外が発生した場合</exception>
        private void CheckProcessForFixed()
        {
            // 条件合致参照ファイルレコード(CSV用）
            string[]? matchedReferenceRecoedForCSV = null;
            // 条件合致参照ファイルレコード（固定長用）
            string? matchedReferenceRecoedForFixed = null;

            // Exception発生時の出力メッセージ向け
            int rowNum = 0;
            
            try
            {
                // 対象データのループ
                for (int i = 0; i < _targetDataForFixed.Count; i++)
                {
                    rowNum++;
                    matchedReferenceRecoedForCSV = null;
                    matchedReferenceRecoedForFixed = null;

                    // バッチ用チェック定義情報リストのループ
                    foreach (var checkDefinition in _updatedDefinitionInfos)
                    {
                        // チェック種別チェック
                        _targetDataForFixed[i] = checkDefinition.CheckType switch
                        {
                            (int)CheckType.NormalCheck => this.NormalCheckProcessForFixed(checkDefinition, _targetDataForFixed[i]),
                            (int)CheckType.ReferenceCheck => this.ReferenceCheckProcessForFixed(checkDefinition, _targetDataForFixed[i],
                                                                                    ref matchedReferenceRecoedForCSV, ref matchedReferenceRecoedForFixed),
                            _ => throw new ArgumentException(MessageResource.MBECH006),
                        };

                        if (!_flagContinue)
                        {
                            return;
                        }
                    }

                    _targetDataRowCount++;
                }

            }
            catch
            {
                throw;
            }

        }

        /// <summary>
        /// 通常チェック処理（CSV用）
        /// </summary>
        /// <param name="checkDefinitionInfo">チェック定義情報</param>
        /// <param name="targetData">対象データ行</param>
        /// <returns>エラーチェック済データ行（CSV)</returns>
        /// <exception cref="NullReferenceException">必須情報が取得できなかった場合</exception>
        /// <exception cref="FormatException">データ形式に該当しなかった場合</exception>
        private string[] NormalCheckProcessForCSV(
            GACH001510CheckDefinitionInfo checkDefinitionInfo, string[] targetData)
        {
            // 項目チェック対象条件データ
            var itemCheckTargetConditionData = string.Empty;
            // 条件合致フラグ
            var isMatch = false;
            //ループ条件
            bool isContinue = false;
            // 文字列変換
            GACH001510ReplaceString? replaceString;
            // 文字列変換データ
            var replaceInputData = string.Empty;

            string msg = string.Empty;
            string[] ret = Array.Empty<string>();

            // チェック対象条件位置
            var checkTargetConditionPosition = checkDefinitionInfo.CheckInfoDetails.FirstOrDefault()?
                .CheckTargetConditionItemList.FirstOrDefault()?.MatterPositon ??
                throw new NullReferenceException(MessageResource.MBECH012);

            // チェック対象条件データ
            string checkTargetConditionData = GetMatterDataForCSV(targetData, checkTargetConditionPosition) ??
                throw new NullReferenceException(MessageResource.MBECH013);

            // バッチ用定義情報詳細リストのループ
            foreach (var detail in checkDefinitionInfo.CheckInfoDetails)
            {
                var checkTargetCondition = detail.CheckTargetConditionItemList.FirstOrDefault(c => c.ConditionNum == 1);
                if(checkTargetCondition?.Signs is null)
                {
                    throw new NullReferenceException(MessageResource.MBECH044);
                }

                // 符号リストループ
                foreach (var sign in checkTargetCondition?.Signs!)
                {
                    // オフコードチェックとレンジチェックの処理
                    isMatch = CheckOffCodeOrRange(checkTargetConditionData, sign);
                    if (isMatch)
                    {
                        break;
                    }
                }

                // 符号チェック
                if (!CheckSignForNormalCheck(isMatch,
                    detail.CheckTargetConditionItemList.FirstOrDefault(c => c.ConditionNum == 1)?.ComparisonCondition))
                {
                    continue;
                }

                // 項目チェック対象条件リストのループ
                foreach (var itemCheckTargetCondition in detail.ItemCheckTargetConditionList)
                {
                    if (itemCheckTargetCondition.ComparisonCondition == null)
                    {
                        continue;
                    }

                    itemCheckTargetConditionData = GetMatterDataForCSV(targetData, itemCheckTargetCondition.MatterPositon) ??
                                                        throw new NullReferenceException(MessageResource.MBECH013);

                    // 符号リストループ
                    foreach (var itemListSign in itemCheckTargetCondition.Signs)
                    {
                        // オフコードチェックとレンジチェックの処理
                        isMatch = CheckOffCodeOrRange(itemCheckTargetConditionData, itemListSign);
                        if (isMatch)
                        {
                            break;
                        }
                    }

                    // 符号チェック
                    if (CheckSignForNormalCheck(isMatch, itemCheckTargetCondition.ComparisonCondition))
                    {
                        continue;
                    }
                    else
                    {
                        isContinue = true;
                        break;
                    }
                }

                if (isContinue)
                {
                    continue;
                }

                // 変換対象データ取得
                replaceString = detail.ReplaceStringList.FirstOrDefault(r => r.ReplaceTargetNum == 1);

                // 文字列変換処理
                if(replaceString is not null
                    && !string.IsNullOrEmpty(replaceString.ReplaceString))
                {
                    if (string.IsNullOrEmpty(replaceString.StMatter.Position))
                    {
                        msg = string.Format(MessageResource.MBECH033,
                                                _targetMeta.ID.ToString(), // 調査票メタID
                                                replaceString.StMatter.ID.ToString());
                        this.UnContinueErrorListForBatchErrMessage(msg);
                        _flagContinue = false;

                        return ret;
                    }

                    if(!int.TryParse(replaceString.StMatter.Position, out int convertedReplaceTargetPosition))
                    {
                        msg = string.Format(MessageResource.MBECH048,_targetMeta.ID.ToString(),replaceString.StMatter.ID.ToString(),MSG_POSITION);
                        this.UnContinueErrorListForBatchErrMessage(msg);
                        _flagContinue = false;

                        return ret;
                    }

                    // 文字列変換取得
                    replaceInputData = GetMatterDataForCSV(targetData, convertedReplaceTargetPosition) ??
                        throw new NullReferenceException(MessageResource.MBECH020);

                    // エラーリスト作成処理
                    var errorList = new GACH001510ErrorList
                    {
                        CheckNum = checkDefinitionInfo.CheckNum.ToString(),
                        CheckSummary = checkDefinitionInfo.CheckSummary,
                        ErrorRow = _targetDataRowCount.ToString(),
                        ReplacePlace = replaceString.StMatter!.Position ?? string.Empty,
                        ReplaceItemName = replaceString.StMatter!.Name ?? string.Empty,
                        InputData = replaceInputData,
                        ReplaceData = replaceString.ReplaceString,
                        ErrFlgData = detail.ErrFlg ?? string.Empty,
                        BatchErrMessage = string.Empty
                    };

                    // 文字列変換処理
                    targetData = this.ReplaceStringForCSV(replaceString.StMatter!, errorList, targetData);
                }
                else if(!string.IsNullOrEmpty(detail.ErrFlg))
                {
                    // 文字列変換行わない場合のエラーリスト追加処理
                    this.AddErrFlgErrorList(checkDefinitionInfo, detail);
                }
            }

            return targetData;
        }

        /// <summary>
        /// 参照ファイルチェック処理（CSV用）
        /// </summary>
        /// <param name="checkDefinitionInfo">チェック定義情報</param>
        /// <param name="targetData">対象データ行</param>
        /// <param name="matchedReferenceRecoedForCSV">条件合致参照ファイルレコード（CSV用）</param>
        /// <param name="matchedReferenceRecoedForFixed">条件合致参照ファイルレコード（固定長用）</param>
        /// <returns>エラーチェック済データ行（CSV)</returns>
        /// <exception cref="NullReferenceException">データが取得できなかった場合</exception>
        /// <exception cref="FormatException">データ形式が該当しなかった場合</exception>
        /// <exception cref="ArgumentException">コードが該当しなかった場合</exception>
        private string[] ReferenceCheckProcessForCSV(
            GACH001510CheckDefinitionInfo checkDefinitionInfo, string[] targetData,
            ref string[]? matchedReferenceRecoedForCSV, ref string? matchedReferenceRecoedForFixed)
        {
            #region 変数
            // 対象参照ファイルデータ
            var referenceFileData = _referenceFileDatas.FirstOrDefault(r => r.StudyMeta.ID == checkDefinitionInfo.ReferenceFileID);
            // バッチ用定義情報詳細リストループ中断フラグ
            var isCheckInfoDetailBreak = false;
            // バッチ用定義情報詳細リストループスキップフラグ
            var isCheckInfoDetailContinue = false;
            // 正常チェック定義フラグ
            var isNormalCheck = true;
            // チェック対象条件リストループ中断フラグ
            var isCheckTargetConditionItemBreak = false;
            // 条件合致参照ファイルレコード取得スキップフラグ
            var isMatchReferenceContinue = false;
            // 条件合致フラグ
            var isMatch = false;
            // 参照ファイルチェックスキップフラグ
            var isCheckReferenceContinue = false;
            // チェック対象条件データ
            string? checkTargetConditionData;
            // キー情報
            GACH001510KeyInfo? keyInfo = new();
            // 文字列変換対象データ
            string? targetReplaceStringData;
            // 文字列変換バイト数
            int replaceBytes = 0;
            // 文字列変換事項
            StMatter? replaceStringMatter;
            // 文字列変換データ
            string? replaceStringData;
            // 合致項目位置
            int? matchedItemPosition = -1;

            string msg;
            string[] ret = Array.Empty<string>();

            #endregion 変数

            // チェック定義チェック
            // Key情報リスト
            if (checkDefinitionInfo.KeyInfos is null)
            {
                throw new NullReferenceException(MessageResource.MBECH008);
            }

            // 参照ファイルデータ
            if (referenceFileData is null)
            {
                throw new NullReferenceException(MessageResource.MBECH005);
            }

            // バッチ用定義情報詳細リストのループ
            foreach (var checkInfoDetail in checkDefinitionInfo.CheckInfoDetails)
            {
                isCheckInfoDetailContinue = false;

                #region チェック対象条件チェック処理
                foreach (var checkTargetConditionItem in checkInfoDetail.CheckTargetConditionItemList)
                {
                    if (checkTargetConditionItem.ComparisonCondition is null)
                    {
                        continue;
                    }

                    // チェック対象条件データ
                    checkTargetConditionData = GetMatterDataForCSV(targetData, checkTargetConditionItem.MatterPositon);

                    if (checkTargetConditionData is null)
                    {
                        throw new NullReferenceException(MessageResource.MBECH013);
                    }

                    foreach (var sign in checkTargetConditionItem.Signs)
                    {
                        // 参照オフコードチェック
                        // 初回のループ時のみチェックを行う
                        if (this.CheckReferenceOffCode(checkTargetConditionItem, sign))
                        {
                            // 参照オフコードチェックで使用するキー情報
                            keyInfo = checkDefinitionInfo.KeyInfos?.FirstOrDefault(
                                k => k.CheckTargetItemID == checkTargetConditionItem.MatterId) ??
                                throw new NullReferenceException(MessageResource.MBECH063);

                            matchedItemPosition = this.GetReferenceOffCodeIndex(referenceFileData, checkTargetConditionData, keyInfo,
                                                                            out bool flagErr);

                            if (flagErr)
                            {
                                _flagContinue = false;
                                return ret;
                            }

                            if (matchedItemPosition != -1)
                            {
                                isCheckInfoDetailContinue = true;
                            }
                            else
                            {
                                isMatchReferenceContinue = true;
                                isCheckReferenceContinue = true;
                            }

                            isCheckTargetConditionItemBreak = true;
                            break;
                        }

                        // 参照オフコードでない場合
                        // チェック処理
                        isMatch = CheckOffCodeOrRange(checkTargetConditionData, sign);

                        if (isMatch)
                        {
                            break;
                        }
                    }

                    // 符号チェック
                    if (CheckSignForReferenceCheck(isMatch, checkTargetConditionItem.ComparisonCondition))
                    {
                        isCheckInfoDetailContinue = true;
                        break;
                    }

                    if (isCheckTargetConditionItemBreak)
                    {
                        break;
                    }
                }
                #endregion チェック対象条件チェック処理

                if (isCheckInfoDetailContinue)
                {
                    continue;
                }

                #region 条件合致参照ファイルレコード取得済チェック
                if (!isMatchReferenceContinue &&
                    IsGetmatchedReferenceRecoed(
                    referenceFileData.DataFormat, matchedReferenceRecoedForCSV, matchedReferenceRecoedForFixed))
                {
                    for(var i = 0; i < checkDefinitionInfo.KeyInfos.Count; i++)
                    {
                        checkDefinitionInfo.KeyInfos[i].CheckTargetItemData = 
                            GetMatterDataForCSV(targetData, checkDefinitionInfo.KeyInfos[i].CheckTargetItemPosition) ??
                            throw new NullReferenceException(MessageResource.MBECH013);
                    }

                    isCheckInfoDetailBreak = this.SetMatchedReferenceRecord(
                        referenceFileData, checkDefinitionInfo, ref matchedReferenceRecoedForCSV, ref matchedReferenceRecoedForFixed);

                }

                if (isCheckInfoDetailBreak)
                {
                    break;
                }

                #endregion 条件合致参照ファイルレコード取得済チェック

                // 参照ファイル対象条件チェック処理
                if (!isCheckReferenceContinue)
                {
                    isCheckInfoDetailContinue = ChecktReferenceFile(
                        checkInfoDetail, referenceFileData, matchedReferenceRecoedForCSV, matchedReferenceRecoedForFixed);
                }

                if (isCheckInfoDetailContinue)
                {
                    continue;
                }

                #region 文字列変換処理
                // バッチ用定義情報詳細.変換文字列リストのループ
                foreach (var replaceString in checkInfoDetail.ReplaceStringList)
                {
                    // 追加用エラーリストデータ
                    GACH001510ErrorList errorList = new();

                    if (replaceString.Type is null)
                    {
                        continue;
                    }

                    if (replaceString.ReplaceString is null)
                    {
                        throw new NullReferenceException(MessageResource.MBECH016);
                    }

                    if (string.IsNullOrEmpty(replaceString.StMatter.Position))
                    {
                        throw new NullReferenceException(MessageResource.MBECH014);
                    }

                    if (!int.TryParse(replaceString.StMatter.Position, out int convertedPosition))
                    {
                        msg = string.Format(MessageResource.MBECH048,_targetMeta.ID.ToString(),replaceString.StMatter.ID.ToString(),MSG_POSITION);
                        this.UnContinueErrorListForBatchErrMessage(msg);
                        _flagContinue = false;

                        return ret;
                    }

                    targetReplaceStringData = GetMatterDataForCSV(targetData, convertedPosition);
                    if (targetReplaceStringData is null)
                    {
                        throw new NullReferenceException(MessageResource.MBECH013);
                    }

                    // 種類チェック
                    switch (replaceString.Type)
                    {
                        case (int)ReplaceStringType.ConstValue:
                            // エラーリスト追加処理
                            errorList = new GACH001510ErrorList
                            {
                                CheckNum = checkDefinitionInfo.CheckNum.ToString(),
                                CheckSummary = checkDefinitionInfo.CheckSummary,
                                ErrorRow = _targetDataRowCount.ToString(),
                                ReplacePlace = replaceString.StMatter.Position,
                                ReplaceItemName = replaceString.StMatter.Name,
                                InputData = targetReplaceStringData,
                                ReplaceData = replaceString.ReplaceString,
                                ErrFlgData = checkInfoDetail.ErrFlg ?? string.Empty,
                                BatchErrMessage = string.Empty
                            };
                            break;
                        case (int)ReplaceStringType.ReferenceFile:
                            // 該当参照ファイル存在チェック
                            if (IsGetmatchedReferenceRecoed(
                                referenceFileData.DataFormat, matchedReferenceRecoedForCSV, matchedReferenceRecoedForFixed))
                            {
                                this.AddErrorListForBatchErrMessage(MessageResource.MBECH001, checkDefinitionInfo);
                                isNormalCheck = false;
                                break;
                            }

                            if (!int.TryParse(replaceString.ReplaceString, out int convertedReplaceItemId))
                            {
                                msg = MessageResource.MBECH041;
                                this.UnContinueErrorListForBatchErrMessage(msg);
                                _flagContinue = false;

                                return ret;
                            }


                            replaceStringMatter = GetStMatter(convertedReplaceItemId, referenceFileData.StudyMeta);
                            if (replaceStringMatter is null)
                            {
                                this.AddErrorListForBatchErrMessage(MessageResource.MBECH001, checkDefinitionInfo);
                                isNormalCheck = false;
                                break;
                            }

                            if (string.IsNullOrEmpty(replaceStringMatter.Position))
                            {
                                msg = string.Format(MessageResource.MBECH033,
                                                                _targetMeta.ID.ToString(), // 調査票メタID
                                                                replaceStringMatter.ID.ToString());
                                this.UnContinueErrorListForBatchErrMessage(msg);
                                _flagContinue = false;

                                return ret;
                            }

                            if (!int.TryParse(replaceStringMatter.Position, out int convertedReplacePosition))
                            {
                                msg = string.Format(MessageResource.MBECH048,_targetMeta.ID.ToString(),replaceStringMatter.ID.ToString(),MSG_POSITION);
                                this.UnContinueErrorListForBatchErrMessage(msg);
                                _flagContinue = false;

                                return ret;
                            }


                            if (!string.IsNullOrEmpty(replaceStringMatter.Bytes))
                            {
                                if (!int.TryParse(replaceStringMatter.Bytes, out replaceBytes))
                                {
                                    msg = string.Format(MessageResource.MBECH048,_targetMeta.ID.ToString(),replaceStringMatter.ID.ToString(), MSG_BYTES);
                                    this.UnContinueErrorListForBatchErrMessage(msg);
                                    _flagContinue = false;

                                    return ret;
                                }
                            }

                            // 条件合致参照ファイルレコードが存在する場合
                            replaceStringData = GetMatterData(
                               referenceFileData.DataFormat, matchedReferenceRecoedForCSV, matchedReferenceRecoedForFixed, convertedReplacePosition, replaceBytes);
                            if (replaceStringData is null)
                            {
                                throw new NullReferenceException(MessageResource.MBECH020);
                            }

                            errorList = new GACH001510ErrorList
                            {
                                CheckNum = checkDefinitionInfo.CheckNum.ToString(),
                                CheckSummary = checkDefinitionInfo.CheckSummary,
                                ErrorRow = _targetDataRowCount.ToString(),
                                ReplacePlace = replaceString.StMatter.Position,
                                ReplaceItemName = replaceString.StMatter.Name,
                                InputData = targetReplaceStringData,
                                ReplaceData = replaceStringData,
                                ErrFlgData = checkInfoDetail.ErrFlg ?? string.Empty,
                                BatchErrMessage = string.Empty
                            };
                            break;
                        case (int)ReplaceStringType.TargetFile:
                            if (!int.TryParse(replaceString.ReplaceString, out int convertedReplaceStringId))
                            {
                                msg = MessageResource.MBECH041;
                                this.UnContinueErrorListForBatchErrMessage(msg);
                                _flagContinue = false;

                                return ret;
                            }

                            replaceStringMatter = GetStMatter(convertedReplaceStringId, _targetMeta);
                            if (replaceStringMatter is null)
                            {
                                this.AddErrorListForBatchErrMessage(MessageResource.MBECH001, checkDefinitionInfo);
                                isNormalCheck = false;
                                break;
                            }

                            if (string.IsNullOrEmpty(replaceStringMatter.Position))
                            {
                                msg = string.Format(MessageResource.MBECH033,
                                                                _targetMeta.ID.ToString(), // 調査票メタID
                                                                replaceStringMatter.ID.ToString());
                                this.UnContinueErrorListForBatchErrMessage(msg);
                                _flagContinue = false;

                                return ret;
                            }

                            if (!int.TryParse(replaceStringMatter.Position, out int convertedReplaceTargetFilePosition))
                            {
                                msg = string.Format(MessageResource.MBECH048,_targetMeta.ID.ToString(),replaceStringMatter.ID.ToString(),MSG_POSITION);
                                this.UnContinueErrorListForBatchErrMessage(msg);
                                _flagContinue = false;

                                return ret;
                            }

                            replaceStringData = GetMatterDataForCSV(targetData, convertedReplaceTargetFilePosition);
                            if (replaceStringData is null)
                            {
                                throw new NullReferenceException(MessageResource.MBECH070);
                            }

                            // エラーリスト追加処理
                            errorList = new GACH001510ErrorList
                            {
                                CheckNum = checkDefinitionInfo.CheckNum.ToString(),
                                CheckSummary = checkDefinitionInfo.CheckSummary,
                                ErrorRow = _targetDataRowCount.ToString(),
                                ReplacePlace = replaceString.StMatter.Position,
                                ReplaceItemName = replaceString.StMatter.Name,
                                InputData = targetReplaceStringData,
                                ReplaceData = replaceStringData,
                                ErrFlgData = checkInfoDetail.ErrFlg ?? string.Empty,
                                BatchErrMessage = string.Empty
                            };
                            break;
                        default:
                            throw new ArgumentException(MessageResource.MBECH021);
                    }


                    isNormalCheck = false;

                    // 文字列変換処理
                    targetData = this.ReplaceStringForCSV(replaceString.StMatter, errorList, targetData);
                }

                #endregion 文字列変換処理

                // 正常定義チェック
                if (CheckNormal(isNormalCheck, checkInfoDetail.ErrFlg))
                {
                    break;
                }

                // エラーリスト追加処理
                if (isNormalCheck)
                {
                    this.AddErrFlgErrorList(checkDefinitionInfo, checkInfoDetail);
                }

                break;
            }

            return targetData;
        }

        /// <summary>
        /// 通常チェック処理（固定長用）
        /// </summary>
        /// <param name="checkDefinitionInfo">バッチ用チェック定義情報</param>
        /// <param name="targetData">対象データ行</param>
        /// <returns>エラーチェック済データ（固定長）</returns>
        /// <exception cref="NullReferenceException"データが取得できなかった場合></exception>
        /// <exception cref="FormatException">データ形式が該当しなかった場合</exception>
        private string NormalCheckProcessForFixed(
            GACH001510CheckDefinitionInfo checkDefinitionInfo, string targetData)
        {


            // 項目チェック対象条件データ
            var itemCheckTargetConditionData = string.Empty;
            
            
            // 条件合致フラグ
            var isMatch = false;
            // ループ条件
            bool isContinue = false;
            // 文字列変換
            GACH001510ReplaceString? replaceString;
            // 変換文字列データ
            var replaceInputData = string.Empty;

            string msg;
            string ret = string.Empty;

            // チェック対象条件
            var checkTargetCondition = checkDefinitionInfo.CheckInfoDetails.FirstOrDefault()?
                .CheckTargetConditionItemList.FirstOrDefault(c => c.ConditionNum == 1) ??
                throw new NullReferenceException(MessageResource.MBECH064);

            if (checkTargetCondition.MatterBytes == null )
            {
                msg = string.Format(MessageResource.MBECH033,_definitionInfos.StudyMetaID.ToString(),checkTargetCondition.MatterId.ToString());
                this.UnContinueErrorListForBatchErrMessage(msg);
                _flagContinue = false;

                return ret;
            }

            // チェック対象条件データ
            string checkTargetConditionData = GetMatterDataForFixed(targetData, checkTargetCondition.MatterPositon, checkTargetCondition.MatterBytes)??
                throw new NullReferenceException(MessageResource.MBECH061);

            // バッチ用定義情報詳細リストのループ
            foreach (var detail in checkDefinitionInfo.CheckInfoDetails)
            {
                var signs = detail.CheckTargetConditionItemList.FirstOrDefault(c => c.ConditionNum == 1)?.Signs ??
                    throw new NullReferenceException(MessageResource.MBECH065);

                // 符号リストループ
                foreach (var sign in signs)
                {
                    // オフコードチェックとレンジチェックの処理
                    isMatch = CheckOffCodeOrRange(checkTargetConditionData, sign);
                    if (isMatch)
                    {
                        break;
                    }
                }

                // 符号チェック
                if(!CheckSignForNormalCheck(isMatch,
                    detail.CheckTargetConditionItemList[0].ComparisonCondition))
                {
                    continue;
                }

                // 項目チェック対象条件リストのループ
                foreach (var itemCheckTargetCondition in detail.ItemCheckTargetConditionList)
                {
                    if (itemCheckTargetCondition.ComparisonCondition is null)
                    {
                        continue;
                    }

                    // 項目チェック対象条件データ取得
                    itemCheckTargetConditionData = GetMatterDataForFixed(targetData, 
                        itemCheckTargetCondition.MatterPositon, itemCheckTargetCondition.MatterBytes)??
                                throw new NullReferenceException(MessageResource.MBECH061);

                    // 符号リストループ
                    foreach (var sign in itemCheckTargetCondition.Signs)
                    {
                        // オフコードチェックとレンジチェックの処理
                        isMatch = CheckOffCodeOrRange(itemCheckTargetConditionData, sign);
                        if (isMatch)
                        {
                            break;
                        }
                    }

                    // 符号チェック
                    if (CheckSignForNormalCheck(isMatch, itemCheckTargetCondition.ComparisonCondition))
                    {
                        continue;
                    }
                    else
                    {
                        isContinue = true;
                        break;
                    }
                }

                if (isContinue)
                {
                    continue;
                }

                // 変換対象データ取得
                replaceString = detail.ReplaceStringList.FirstOrDefault(r => r.ReplaceTargetNum == 1);

                // 文字列変換有無チェック
                if(replaceString is not null && !string.IsNullOrEmpty(replaceString.ReplaceString))
                {
                    if (string.IsNullOrEmpty(replaceString.StMatter.Position))
                    {
                        msg = string.Format(MessageResource.MBECH033,
                                            _targetMeta.ID.ToString(), // 調査票メタID
                                            replaceString.StMatter.ID.ToString());
                        this.UnContinueErrorListForBatchErrMessage(msg);
                        _flagContinue = false;

                        return ret;
                    }

                    if (!int.TryParse(replaceString.StMatter.Position, out int convertedPosition))
                    {
                        msg = string.Format(MessageResource.MBECH048,_targetMeta.ID.ToString(),replaceString.StMatter.ID.ToString(),MSG_POSITION);
                        this.UnContinueErrorListForBatchErrMessage(msg);
                        _flagContinue = false;

                        return ret;
                    }

                    if (string.IsNullOrEmpty(replaceString.StMatter.Bytes))
                    {
                        msg = string.Format(MessageResource.MBECH033,
                                                    _targetMeta.ID.ToString(), // 調査票メタID
                                                    replaceString.StMatter.ID.ToString());
                        this.UnContinueErrorListForBatchErrMessage(msg);
                        _flagContinue = false;

                        return ret;
                    }

                    if (!int.TryParse(replaceString.StMatter.Bytes, out int convertedBytes))
                    {
                        msg = string.Format(MessageResource.MBECH048,_targetMeta.ID.ToString(),replaceString.StMatter.ID.ToString(), MSG_BYTES);
                        this.UnContinueErrorListForBatchErrMessage(msg);
                        _flagContinue = false;

                        return ret;
                    }

                    // 文字列変換取得
                    replaceInputData = GetMatterDataForFixed(targetData, convertedPosition, convertedBytes) ??
                        throw new NullReferenceException(MessageResource.MBECH020);


                    // エラーリスト作成処理
                    var errorList = new GACH001510ErrorList
                    {
                        CheckNum = checkDefinitionInfo.CheckNum.ToString(),
                        CheckSummary = checkDefinitionInfo.CheckSummary,
                        ErrorRow = _targetDataRowCount.ToString(),
                        ReplacePlace = replaceString.StMatter.Position ?? string.Empty,
                        ReplaceItemName = replaceString.StMatter.Name ?? string.Empty,
                        InputData = replaceInputData,
                        ReplaceData = replaceString.ReplaceString,
                        ErrFlgData = detail.ErrFlg ?? string.Empty,
                        BatchErrMessage = string.Empty
                    };
                    
                    // 文字列変換処理
                    this.ReplaceStringForFixed(replaceString.StMatter, errorList, ref targetData);
                }
                else if(!string.IsNullOrEmpty(detail.ErrFlg))
                {
                    // 文字列変換行わない場合のエラーリスト追加処理
                    this.AddErrFlgErrorList(checkDefinitionInfo, detail);
                }
            }

            return targetData;
        }

        /// <summary>
        /// 参照ファイルチェック処理（固定長用）
        /// </summary>
        /// <param name="checkDefinitionInfo">バッチ用チェック定義情報</param>
        /// <param name="targetData">対象データ行</param>
        /// <param name="matchedReferenceRecoedForCSV">条件合致参照ファイルレコード（CSV用）</param>
        /// <param name="matchedReferenceRecoedForFixed">条件合致参照ファイルレコード（固定長用）</param>
        /// <returns>エラーチェック済データ行（固定長)</returns>
        /// <exception cref="NullReferenceException">データが取得できなかった場合</exception>
        /// <exception cref="FormatException">データ形式が該当しなかった場合</exception>
        /// <exception cref="ArgumentException">コードが該当しなかった場合</exception>
        private string ReferenceCheckProcessForFixed(
            GACH001510CheckDefinitionInfo checkDefinitionInfo, string targetData,
            ref string[]? matchedReferenceRecoedForCSV, ref string? matchedReferenceRecoedForFixed)
        {
            #region 変数
            // 対象参照ファイルデータ
            var referenceFileData = _referenceFileDatas.FirstOrDefault(r => r.StudyMeta.ID == checkDefinitionInfo.ReferenceFileID);
            // バッチ用定義情報詳細リストループ中断フラグ
            var isCheckInfoDetailBreak = false;
            // バッチ用定義情報詳細リストループスキップフラグ
            var isCheckInfoDetailContinue = false;
            // 正常チェック定義フラグ
            var isNormalCheck = true;
            // チェック対象条件リストループ中断フラグ
            var isCheckTargetConditionItemBreak = false;
            // 条件合致参照ファイルレコード取得スキップフラグ
            var isMatchReferenceContinue = false;
            // 条件合致フラグ
            var isMatch = false;
            // 参照ファイルチェックスキップフラグ
            var isCheckReferenceContinue = false;
            // 追加用エラーリスト
            GACH001510ErrorList? errorList = new();
            // チェック対象条件データ
            string? checkTargetConditionData;
            // キー情報
            GACH001510KeyInfo keyInfo = new();
            //文字列変換バイト数
            int convertedBytes = 0;
            // 文字列変換対象データ
            string? targetReplaceStringData;
            // 文字列変換事項
            StMatter? replaceStringMatter;
            // 文字列変換データ
            string? replaceStringData;
            // 合致項目位置
            var matchedItemPosition = -1;
            string msg;
            string ret = string.Empty;

            #endregion 変数

            // チェック定義チェック
            // Key情報リスト
            if (checkDefinitionInfo.KeyInfos is null)
            {
                throw new NullReferenceException(MessageResource.MBECH008);
            }

            // 参照ファイルデータ
            if (referenceFileData is null)
            {
                throw new NullReferenceException(MessageResource.MBECH005);
            }

            // バッチ用定義情報詳細リストのループ
            foreach (var checkInfoDetail in checkDefinitionInfo.CheckInfoDetails)
            {
                isCheckInfoDetailContinue = false;

                #region チェック対象条件チェック処理
                foreach (var checkTargetConditionItem in checkInfoDetail.CheckTargetConditionItemList)
                {
                    if (checkTargetConditionItem.ComparisonCondition is null)
                    {
                        continue;
                    }

                    // チェック対象条件データ
                    checkTargetConditionData = GetMatterDataForFixed(
                        targetData, checkTargetConditionItem.MatterPositon, checkTargetConditionItem.MatterBytes)??
                                                        throw new NullReferenceException(MessageResource.MBECH013);

                    foreach (var sign in checkTargetConditionItem.Signs)
                    {
                        // 参照オフコードチェック
                        // 初回のループ時のみチェックを行う
                        if (this.CheckReferenceOffCode(checkTargetConditionItem, sign))
                        {
                            // 参照オフコードチェックで使用するキー情報
                            keyInfo = checkDefinitionInfo.KeyInfos?.FirstOrDefault(
                                k => k.CheckTargetItemID == checkTargetConditionItem.MatterId) ??
                                throw new NullReferenceException(MessageResource.MBECH063);

                            matchedItemPosition = this.GetReferenceOffCodeIndex(referenceFileData, checkTargetConditionData, keyInfo,
                                                                            out bool flagErr);

                            if (flagErr)
                            {
                                _flagContinue = false;
                                return ret;
                            }

                            if (matchedItemPosition != -1)
                            {
                                isCheckInfoDetailContinue = true;
                            }
                            else
                            {
                                isMatchReferenceContinue = true;
                                isCheckReferenceContinue = true;
                            }

                            isCheckTargetConditionItemBreak = true;
                            break;
                        }

                        // 参照オフコードでない場合
                        // チェック処理
                        isMatch = CheckOffCodeOrRange(checkTargetConditionData, sign);

                        if (isMatch)
                        {
                            break;
                        }
                    }

                    // 符号チェック
                    if (CheckSignForReferenceCheck(isMatch, checkTargetConditionItem.ComparisonCondition))
                    {
                        isCheckInfoDetailContinue = true;
                        break;
                    }

                    if (isCheckTargetConditionItemBreak)
                    {
                        break;
                    }
                }
                #endregion チェック対象条件チェック処理

                if (isCheckInfoDetailContinue)
                {
                    continue;
                }

                #region 条件合致参照ファイルレコード取得済チェック
                if (!isMatchReferenceContinue &&
                    IsGetmatchedReferenceRecoed(
                    referenceFileData.DataFormat, matchedReferenceRecoedForCSV, matchedReferenceRecoedForFixed))
                {

                    for (var i = 0; i < checkDefinitionInfo.KeyInfos.Count; i++)
                    {
                        checkDefinitionInfo.KeyInfos[i].CheckTargetItemData =
                            GetMatterDataForFixed(targetData, checkDefinitionInfo.KeyInfos[i].CheckTargetItemPosition,
                            checkDefinitionInfo.KeyInfos[i].CheckTargetItemBytes) ??
                            throw new NullReferenceException(MessageResource.MBECH013);
                    }

                    isCheckInfoDetailBreak = this.SetMatchedReferenceRecord(
                        referenceFileData, checkDefinitionInfo, ref matchedReferenceRecoedForCSV, ref matchedReferenceRecoedForFixed);

                    if (!_flagContinue)
                    {
                        return ret;
                    }
                }
                #endregion 条件合致参照ファイルレコード取得済チェック

                if (isCheckInfoDetailBreak)
                {
                    break;
                }

                // 参照ファイル対象条件チェック処理
                if (!isCheckReferenceContinue)
                {
                    isCheckInfoDetailContinue = ChecktReferenceFile(
                        checkInfoDetail, referenceFileData, matchedReferenceRecoedForCSV, matchedReferenceRecoedForFixed);
                }

                #region 文字列変換処理
                // バッチ用定義情報詳細.変換文字列リストのループ
                foreach (var replaceString in checkInfoDetail.ReplaceStringList)
                {
                    if (replaceString.Type is null)
                    {
                        continue;
                    }

                    if (replaceString.ReplaceString is null)
                    {
                        throw new NullReferenceException(MessageResource.MBECH016);
                    }

                    if (string.IsNullOrEmpty(replaceString.StMatter.Position) || string.IsNullOrEmpty(replaceString.StMatter.Bytes))
                    {
                        msg = string.Format(MessageResource.MBECH033,
                                                _targetMeta.ID.ToString(), // 調査票メタID
                                                replaceString.StMatter.ID.ToString());
                        this.UnContinueErrorListForBatchErrMessage(msg);
                        _flagContinue = false;

                        return ret;
                    }

                    if (!int.TryParse(replaceString.StMatter.Position, out int convertedReplacePosition))
                    {
                        msg = string.Format(MessageResource.MBECH048,_targetMeta.ID.ToString(),replaceString.StMatter.ID.ToString(),MSG_POSITION);
                        this.UnContinueErrorListForBatchErrMessage(msg);
                        _flagContinue = false;

                        return ret;
                    }

                    if (!int.TryParse(replaceString.StMatter.Bytes, out int convertedReplaceBytes))
                    {
                        msg = string.Format(MessageResource.MBECH048,_targetMeta.ID.ToString(),replaceString.StMatter.ID.ToString(),MSG_BYTES);
                        this.UnContinueErrorListForBatchErrMessage(msg);
                        _flagContinue = false;

                        return ret;
                    }

                    targetReplaceStringData = GetMatterDataForFixed(targetData, convertedReplacePosition, convertedReplaceBytes);

                    if (targetReplaceStringData is null)
                    {
                        throw new NullReferenceException(MessageResource.MBECH066);
                    }

                    // 種類チェック
                    switch (replaceString.Type)
                    {
                        case (int)ReplaceStringType.ConstValue:
                            errorList = new GACH001510ErrorList
                            {
                                CheckNum = checkDefinitionInfo.CheckNum.ToString(),
                                CheckSummary = checkDefinitionInfo.CheckSummary,
                                ErrorRow = _targetDataRowCount.ToString(),
                                ReplacePlace = replaceString.StMatter.Position,
                                ReplaceItemName = replaceString.StMatter.Name,
                                InputData = targetReplaceStringData,
                                ReplaceData = replaceString.ReplaceString,
                                ErrFlgData = checkInfoDetail.ErrFlg ?? string.Empty,
                                BatchErrMessage = string.Empty
                            };
                            break;
                        case (int)ReplaceStringType.ReferenceFile:
                            // 該当参照ファイル存在チェック
                            if (IsGetmatchedReferenceRecoed(
                                referenceFileData.DataFormat, matchedReferenceRecoedForCSV, matchedReferenceRecoedForFixed))
                            {
                                this.AddErrorListForBatchErrMessage(MessageResource.MBECH001, checkDefinitionInfo);
                                isNormalCheck = false;
                                break;
                            }

                            if (!int.TryParse(replaceString.ReplaceString, out int convertedReplaceItemId))
                            {
                                msg = MessageResource.MBECH041;
                                this.UnContinueErrorListForBatchErrMessage(msg);
                                _flagContinue = false;

                                return ret;
                            }

                            replaceStringMatter = GetStMatter(convertedReplaceItemId, referenceFileData.StudyMeta);
                            if (replaceStringMatter is null)
                            {
                                this.AddErrorListForBatchErrMessage(MessageResource.MBECH001, checkDefinitionInfo);
                                isNormalCheck = false;
                                break;
                            }


                            if (string.IsNullOrEmpty(replaceStringMatter.Position) || string.IsNullOrEmpty(replaceStringMatter.Bytes))
                            {
                                msg = string.Format(MessageResource.MBECH033,
                                                        referenceFileData.StudyMeta.ID.ToString(), // 調査票メタID
                                                        replaceStringMatter.ID.ToString());
                                this.UnContinueErrorListForBatchErrMessage(msg);
                                _flagContinue = false;

                                return ret;
                            }

                            if (!int.TryParse(replaceStringMatter.Position, out int convertedRefPosition))
                            {
                                msg = string.Format(MessageResource.MBECH048,referenceFileData.StudyMeta.ID.ToString(),replaceStringMatter.ID.ToString(),MSG_POSITION);
                                this.UnContinueErrorListForBatchErrMessage(msg);
                                _flagContinue = false;

                                return ret;
                            }

                            if (!int.TryParse(replaceStringMatter.Bytes, out convertedBytes))
                            {
                                msg = string.Format(MessageResource.MBECH048,referenceFileData.StudyMeta.ID.ToString(),replaceStringMatter.ID.ToString(), MSG_BYTES);
                                this.UnContinueErrorListForBatchErrMessage(msg);
                                _flagContinue = false;

                                return ret;

                            }

                            // 条件合致参照ファイルレコードが存在する場合
                            replaceStringData = GetMatterData(referenceFileData.DataFormat, matchedReferenceRecoedForCSV,
                                matchedReferenceRecoedForFixed, convertedRefPosition, convertedBytes);
                            if (replaceStringData is null)
                            {
                                throw new NullReferenceException(MessageResource.MBECH067);
                            }

                            errorList = new GACH001510ErrorList
                            {
                                CheckNum = checkDefinitionInfo.CheckNum.ToString(),
                                CheckSummary = checkDefinitionInfo.CheckSummary,
                                ErrorRow = _targetDataRowCount.ToString(),
                                ReplacePlace = replaceString.StMatter.Position,
                                ReplaceItemName = replaceString.StMatter.Name,
                                InputData = targetReplaceStringData,
                                ReplaceData = replaceStringData,
                                ErrFlgData = checkInfoDetail.ErrFlg ?? string.Empty,
                                BatchErrMessage = string.Empty
                            };
                            break;
                        case (int)ReplaceStringType.TargetFile:
                            if (!int.TryParse(replaceString.ReplaceString, out int convertedReplaceStringId))
                            {
                                msg = MessageResource.MBECH041;
                                this.UnContinueErrorListForBatchErrMessage(msg);
                                _flagContinue = false;

                                return ret;
                            }

                            replaceStringMatter = GetStMatter(convertedReplaceStringId, _targetMeta);
                            if (replaceStringMatter is null)
                            {
                                this.AddErrorListForBatchErrMessage(MessageResource.MBECH001, checkDefinitionInfo);
                                isNormalCheck = false;
                                break;
                            }

                            if(string.IsNullOrEmpty(replaceStringMatter.Position) || string.IsNullOrEmpty(replaceStringMatter.Bytes) )
                            {
                                msg = string.Format(MessageResource.MBECH033,
                                                        referenceFileData.StudyMeta.ID.ToString(), // 調査票メタID
                                                        replaceStringMatter.ID.ToString());
                                this.UnContinueErrorListForBatchErrMessage(msg);
                                _flagContinue = false;

                                return ret;
                            }

                            if (!int.TryParse(replaceStringMatter.Position, out int convertedPosition))
                            {
                                msg = string.Format(MessageResource.MBECH048,_targetMeta.ID.ToString(),replaceStringMatter.ID.ToString(),MSG_BYTES);
                                this.UnContinueErrorListForBatchErrMessage(msg);
                                _flagContinue = false;

                                return ret;

                            }

                            if (!int.TryParse(replaceStringMatter.Bytes, out convertedBytes))
                            {
                                msg = string.Format(MessageResource.MBECH048,_targetMeta.ID.ToString(),replaceStringMatter.ID.ToString(), MSG_BYTES);
                                this.UnContinueErrorListForBatchErrMessage(msg);
                                _flagContinue = false;

                                return ret;
                            }

                            replaceStringData = GetMatterDataForFixed(targetData, convertedPosition, convertedBytes);
                            if (replaceStringData is null)
                            {
                                throw new NullReferenceException(MessageResource.MBECH070);
                            }

                            // エラーリスト追加処理
                            errorList = new GACH001510ErrorList
                            {
                                CheckNum = checkDefinitionInfo.CheckNum.ToString(),
                                CheckSummary = checkDefinitionInfo.CheckSummary,
                                ErrorRow = _targetDataRowCount.ToString(),
                                ReplacePlace = replaceString.StMatter.Position,
                                ReplaceItemName = replaceString.StMatter.Name,
                                InputData = targetReplaceStringData,
                                ReplaceData = replaceStringData,
                                ErrFlgData = checkInfoDetail.ErrFlg ?? string.Empty,
                                BatchErrMessage = string.Empty
                            };
                            break;
                        default:
                            throw new ArgumentException(MessageResource.MBECH021);
                    }

                    isNormalCheck = false;

                    // 文字列変換処理
                    this.ReplaceStringForFixed(replaceString.StMatter, errorList, ref targetData);
                }
                #endregion 文字列変換処理

                // 正常定義チェック
                if (CheckNormal(isNormalCheck, checkInfoDetail.ErrFlg))
                {
                    break;
                }

                // エラーリスト追加処理
                if (isNormalCheck)
                {
                    this.AddErrFlgErrorList(checkDefinitionInfo, checkInfoDetail);
                }

                break;
            }

            return targetData;
        }

        /// <summary>
        /// オフコードチェック処理
        /// </summary>
        /// <param name="compareTarget">比較対象</param>
        /// <param name="sign">符号1</param>
        /// <returns></returns>
        private static bool CheckOffCode(string compareTarget, string sign)
        {
            return compareTarget == sign;
        }

        /// <summary>
        /// レンジチェック処理
        /// </summary>
        /// <param name="compareTarget">比較対象</param>
        /// <param name="sign1">符号1</param>
        /// <param name="sign2">符号2</param>
        /// <returns></returns>
        private static bool CheckRange(string compareTarget, string sign1, string? sign2)
        {
            if (sign2 is null)
            {
                throw new Exception(MessageResource.MBECH022);
            }

            // 型チェック
            if (!sign1.Contains('.') && !sign2.Contains('.')
                && sign1.Length == sign2.Length
                && sign1.Length != compareTarget.Length)
            {
                return false;
            }

            //数値型チェック
            if (!GaCommon.Commons.TypeCheckService.NumberTypeCheck(compareTarget)
                || !decimal.TryParse(compareTarget, out decimal convertedtarget))
            {
                return false;
            }

            if (!GaCommon.Commons.TypeCheckService.NumberTypeCheck(sign1)
                || !decimal.TryParse(sign1, out decimal convertedSign1))
            {
                return false;
            }

            if (!GaCommon.Commons.TypeCheckService.NumberTypeCheck(sign2)
                || !decimal.TryParse(sign2, out decimal convertedSign2))
            {
                return false;
            }

            if (convertedtarget >= convertedSign1 && convertedtarget <= convertedSign2)
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// 参照ファイルチェック
        /// </summary>
        /// <param name="checkInfoDetail">定義情報詳細</param>
        /// <param name="referenceFileData">参照ファイルデータ</param>
        /// <param name="matchedReferenceRecoedForCSV">条件合致参照ファイルレコード（CSV用）</param>
        /// <param name="matchedReferenceRecoedForFixed">条件合致参照ファイルレコード（固定長用）</param>
        /// <returns>参照ファイルチェック結果</returns>
        /// <exception cref="NullReferenceException">参照ファイルが取得できなかった場合</exception>
        private static bool ChecktReferenceFile(GACH001510CheckInfoDetail checkInfoDetail, GACH001510ReferenceFileData referenceFileData,
            string[]? matchedReferenceRecoedForCSV, string? matchedReferenceRecoedForFixed)
        {
            // チェック参照ファイルデータ
            string? referenceFileDataString;
            // 条件合致フラグ
            var isMatch = false;

            foreach (var refFileTargetCondition in checkInfoDetail.RefFileTargetConditionList)
            {
                // 比較条件チェック
                if (refFileTargetCondition.ComparisonCondition is null)
                {
                    continue;
                }

                // チェック参照ファイルデータ
                referenceFileDataString = GetMatterData(
                    referenceFileData.DataFormat, matchedReferenceRecoedForCSV, matchedReferenceRecoedForFixed, 
                    refFileTargetCondition.MatterPositon, refFileTargetCondition.MatterBytes);

                if (referenceFileDataString is null)
                {
                    throw new NullReferenceException(MessageResource.MBECH018);
                }

                // 符号リストのループ
                foreach (var sign in refFileTargetCondition.Signs)
                {
                    isMatch = CheckOffCodeOrRange(referenceFileDataString, sign);

                    if (isMatch)
                    {
                        break;
                    }
                }

                // 符号チェック
                if (CheckSignForReferenceCheck(isMatch, refFileTargetCondition.ComparisonCondition))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 正常定義チェック
        /// </summary>
        /// <param name="isNormal">正常フラグ</param>
        /// <param name="errorFlg">エラーフラグ</param>
        /// <returns>正常定義かどうか</returns>
        private static bool CheckNormal(bool isNormal, string? errorFlg)
        {
            return isNormal && string.IsNullOrEmpty(errorFlg);
        }

        /// <summary>
        /// 参照オフコードチェック
        /// </summary>
        /// <param name="targetCondition">対象条件</param>
        /// <param name="sign">符号情報</param>
        /// <returns>参照オフコードチェックが必要かどうか</returns>
        private bool CheckReferenceOffCode(GACH001510TargetCondition targetCondition, GACH001510Sign sign)
        {
            return targetCondition.ConditionNum == 1
                            && sign.Sign1 == _referenceOffCode;
        }

        /// <summary>
        /// オフコードチェックまたは参照ファイルチェック処理
        /// </summary>
        /// <param name="targetData">対象データ</param>
        /// <param name="sign">符号情報</param>
        /// <returns>チェック結果</returns>
        /// <exception cref="ArgumentException">チェックできない符号内容が設定されている場合</exception>
        private static bool CheckOffCodeOrRange(string targetData, GACH001510Sign sign)
        {
            return sign.CheckMethodType switch
            {
                CheckMethodType.OffCodeCheck => CheckOffCode(targetData, sign.Sign1),
                CheckMethodType.RangeCheck => CheckRange(targetData, sign.Sign1, sign.Sign2),
                _ => throw new ArgumentException(MessageResource.MBECH022)
            };
        }

        /// <summary>
        /// 参照ファイル用符号チェック
        /// </summary>
        /// <param name="isMatch">符号が条件と合致しているかどうか</param>
        /// <param name="comparisonCondition">条件種別</param>
        /// <returns>True:条件合致/False:条件不合致</returns>
        private static bool CheckSignForReferenceCheck(bool isMatch, int? comparisonCondition)
        {
            return (!isMatch && comparisonCondition is (int)ComparisonCondition.Equal)
                        || (isMatch && comparisonCondition is (int)ComparisonCondition.Other);
        }

        /// <summary>
        /// 通常用符号チェック
        /// </summary>
        /// <param name="isMatch"></param>
        /// <param name="comparisonCondition"></param>
        /// <returns></returns>
        private static bool CheckSignForNormalCheck(bool isMatch, int? comparisonCondition)
        {
            return (isMatch && comparisonCondition is (int)ComparisonCondition.Equal)
                        || (!isMatch && comparisonCondition is (int)ComparisonCondition.Other);
        }

        /// <summary>
        /// 条件合致参照ファイルレコード取得チェック
        /// </summary>
        /// <param name="dataFormats">データ形式</param>
        /// <param name="matchedReferenceRecoedForCSV">条件合致参照ファイルレコード（CSV用）</param>
        /// <param name="matchedReferenceRecoedForFixed">条件合致参照ファイルレコード（固定長用）</param>
        /// <returns>条件合致参照ファイルレコード取得済かどうか</returns>
        private static bool IsGetmatchedReferenceRecoed(
            DataFormats dataFormats, string[]? matchedReferenceRecoedForCSV, string? matchedReferenceRecoedForFixed)
        {
            return (dataFormats is DataFormats.Csv && matchedReferenceRecoedForCSV is null) ||
                                (dataFormats is DataFormats.Fixed && matchedReferenceRecoedForFixed is null);
        }

        /// <summary>
        /// 事項データ取得処理
        /// </summary>
        /// <param name="dataFormats">データ形式</param>
        /// <param name="datasForCSV">対象データ行（CSV用）</param>
        /// <param name="datasForFixed">対象データ行（固定長用）</param>
        /// <param name="matter">事項</param>
        /// <returns>事項に対応するデータ情報</returns>
        /// <exception cref="ArgumentException">使用できないデータ形式が設定されている場合</exception>
        private static string? GetMatterData(
            DataFormats dataFormats, string[]? datasForCSV, string? datasForFixed, int position, int? bytes)
        {
            return dataFormats switch
            {
                DataFormats.Csv => GetMatterDataForCSV(datasForCSV, position),
                DataFormats.Fixed => GetMatterDataForFixed(datasForFixed, position, bytes),
                _ => throw new ArgumentException(string.Format(MessageResource.MBECH040, MSG_REFERENCE_DATA))
            };
        }

        /// <summary>
        /// データ取得処理（CSV用）
        /// </summary>
        /// <param name="targetDatas">対象データの対象行データ</param>
        /// <param name="position">位置</param>
        /// <returns>事項に対応する対象データ情報</returns>
        private static string? GetMatterDataForCSV(string[]? targetDatas, int? position)
        {
            if(targetDatas is null)
            {
                return null;
            }

            if (position is null)
            {
                throw new NullReferenceException(MessageResource.MBECH014);
            }

            if (targetDatas.Length >= (int)position)
            {
                return targetDatas[(int)position - 1];
            }
 
            return null;
        }

        /// <summary>
        /// データ取得処理（固定長用）
        /// </summary>
        /// <param name="targetDataLine">対象データの対象行データ</param>
        /// <param name="position">位置</param>
        /// <param name="bytes">バイト数</param>
        /// <returns>事項に対応する対象データ情報</returns>
        private static string? GetMatterDataForFixed(string? targetDataLine, int position, int? bytes)
        {
            if (targetDataLine is null)
            {
                return null;
            }

            if(bytes is null)
            {
                throw new NullReferenceException(MessageResource.MBECH014);
            }

            if (targetDataLine.Length >= (position - 1 + bytes))
            {
                return targetDataLine.Substring(position - 1, (int)bytes);
            }

            return null;
        }

        /// <summary>
        /// 対象事項データ取得
        /// </summary>
        /// <param name="id">事項ID</param>
        /// <param name="meta">調査票メタ</param>
        /// <returns>事項</returns>
        private static StMatter? GetStMatter(int? id, StudyMetaDataset meta)
        {
            return meta.Matters?.FirstOrDefault(m => m.ID == id);
        }

        /// <summary>
        /// 参照オフコードIndex取得
        /// </summary>
        /// <param name="referenceFileData">参照ファイルデータ</param>
        /// <param name="targetConditionData">比較対象データ</param>
        /// <param name="keyInfo">キー情報</param>
        /// <returns>比較対象データが存在するIndex</returns>
        /// <exception cref="NullReferenceException">参照ファイルデータが設定されていない場合</exception>
        /// <exception cref="FormatException">参照ファイルのデータ長が位置より小さい場合</exception>
        /// <exception cref="ArgumentException">参照ファイルがCSVまたは固定長以外の場合</exception>
        private  int GetReferenceOffCodeIndex(
            GACH001510ReferenceFileData referenceFileData, string targetConditionData, GACH001510KeyInfo keyInfo, out bool flagErr)
        {
            // 参照ファイルデータキーリスト設定(CSV用）
            List<string>? targetReferenceFileDataForCSV;
            // 参照ファイルデータキーリスト設定(固定長用）
            List<string?>? targetReferenceFileDataForFixed;

            string msg = string.Empty;
            flagErr = false;

            switch (referenceFileData.DataFormat)
            {
                case DataFormats.Csv:
                    if (referenceFileData.LoadDataForCSV is null)
                    {
                        throw new NullReferenceException(MessageResource.MBECH005);
                    }

                    // 参照ファイル(csv) 対象カラム項目存在チェック
                    if (!(referenceFileData.LoadDataForCSV[0].Length >= keyInfo.RefFileTargetItemPosition))
                    {
                        msg = string.Format(MessageResource.MBECH062, referenceFileData.StudyMeta.ID.ToString());
                        this.UnContinueErrorListForBatchErrMessage(msg);
                        flagErr = true;

                        return 0;
                    }

                    // 参照ファイルデータキーリスト設定
                    targetReferenceFileDataForCSV
                        = referenceFileData.LoadDataForCSV?.Select(data => data[keyInfo.RefFileTargetItemPosition - 1]).ToList();
                    if (targetReferenceFileDataForCSV is null)
                    {
                        throw new NullReferenceException(MessageResource.MBECH005);
                    }

                    return targetReferenceFileDataForCSV.FindIndex(t => t == targetConditionData);
                case DataFormats.Fixed:
                    if (keyInfo.RefFileTargetItemBytes is null)
                    {
                        throw new FormatException(MessageResource.MBECH011);
                    }

                    // 参照ファイルデータキーリスト設定
                    // 位置からバイト数分を切り取ってリスト化
                    targetReferenceFileDataForFixed = referenceFileData.LoadDataForFixed?
                        .Select(data => data.Length >= keyInfo.RefFileTargetItemPosition - 1  + keyInfo.RefFileTargetItemBytes
                        ? data.Substring(keyInfo.RefFileTargetItemPosition - 1, (int)keyInfo.RefFileTargetItemBytes) : null).ToList();

                    if (targetReferenceFileDataForFixed is null)
                    {
                        throw new NullReferenceException(MessageResource.MBECH005);
                    }

                    return targetReferenceFileDataForFixed.FindIndex(t => t == targetConditionData);
                default:
                    throw new ArgumentException(string.Format(MessageResource.MBECH040, MSG_REFERENCE_DATA));
            }
        }

        /// <summary>
        /// 条件合致参照ファイルレコード設定
        /// </summary>
        /// <param name="referenceFileData">参照ファイルデータ</param>
        /// <param name="checkDefinitionInfo">チェック定義情報</param>
        /// <param name="matchedReferenceRecoedForCSV">条件合致参照ファイルレコード（CSV）</param>
        /// <param name="matchedReferenceRecoedForFixed">条件合致参照ファイルレコード（固定長）</param>
        /// <param name="errorLists">エラーリスト</param>
        /// <returns>定義情報詳細リストのループ中断するかどうか</returns>
        /// <exception cref="NullReferenceException">対象データがなかった場合</exception>
        /// <exception cref="ArgumentException">参照データの形式がCSV,固定長以外の場合</exception>
        private bool SetMatchedReferenceRecord(
            GACH001510ReferenceFileData referenceFileData, GACH001510CheckDefinitionInfo checkDefinitionInfo,
            ref string[]? matchedReferenceRecoedForCSV, ref string? matchedReferenceRecoedForFixed)
        {
            // CSV用条件
            var conditionsForCSV = new List<Func<string[], bool>>();
            // 固定長用条件
            var conditionsForFixed = new List<Func<string, bool>>();

            string msg;

            switch (referenceFileData.DataFormat)
            {
                case DataFormats.Csv:
                    if (referenceFileData.LoadDataForCSV is null)
                    {
                        throw new NullReferenceException(MessageResource.MBECH005);
                    }

                    // 条件式を動的に構築
                    conditionsForCSV.Add(arr => arr.Length >= checkDefinitionInfo.KeyInfos.Max(r => r.RefFileTargetItemPosition));

                    foreach (var keyInfo in checkDefinitionInfo.KeyInfos)
                    {
                        conditionsForCSV.Add(arr => arr[keyInfo.RefFileTargetItemPosition - 1] == keyInfo.CheckTargetItemData);
                    }

                    // 動的に構築した条件式で条件合致参照ファイルレコードを検索
                    matchedReferenceRecoedForCSV
                        = referenceFileData.LoadDataForCSV.Where(arr => conditionsForCSV.All(condition => condition(arr))).FirstOrDefault();

                    if (matchedReferenceRecoedForCSV is null)
                    {
                        this.AddErrorListForBatchErrMessage(MessageResource.MBECH002, checkDefinitionInfo);
                        return true;
                    }

                    break;
                case DataFormats.Fixed:
                    if (referenceFileData.LoadDataForFixed is null)
                    {
                        throw new NullReferenceException(MessageResource.MBECH005);
                    }

                    foreach (var keyInfo in checkDefinitionInfo.KeyInfos)
                    {
                        if(keyInfo.RefFileTargetItemBytes is null)
                        {
                            msg = string.Format(MessageResource.MBECH033, checkDefinitionInfo.ReferenceFileID,keyInfo.CheckTargetItemID.ToString());
                            this.UnContinueErrorListForBatchErrMessage(msg);
                            _flagContinue = false;

                            return false;
                        }

                        conditionsForFixed.Add(
                            str => str.Substring(keyInfo.RefFileTargetItemPosition - 1, (int)keyInfo.RefFileTargetItemBytes)
                            == keyInfo.CheckTargetItemData);
                    }

                    matchedReferenceRecoedForFixed = referenceFileData.LoadDataForFixed
                        .Where(str => conditionsForFixed.All(condition => condition(str))).FirstOrDefault();

                    if (matchedReferenceRecoedForFixed is null)
                    {
                        this.AddErrorListForBatchErrMessage(MessageResource.MBECH002, checkDefinitionInfo);
                        return true;
                    }

                    break;
                default:
                    throw new ArgumentException(string.Format(MessageResource.MBECH040, MSG_REFERENCE_DATA));
            }

            return false;
        }

        /// <summary>
        /// 文字列変換処理（CSV用）
        /// </summary>
        /// <param name="matter">事項</param>
        /// <param name="errorList">エラーリストデータ</param>
        /// <param name="targetDatas">対象データの対象行データ</param>
        /// <returns>文字列変換後エラーチェック済データ（CSV)</returns>
        private string[] ReplaceStringForCSV(StMatter matter, GACH001510ErrorList errorList, string[] targetDatas)
        {
            if (int.TryParse(matter.Position, out var position)
                && targetDatas.Length >= position - 1)
            {
                // エラーリスト追加処理
                _errorLists.Add(errorList);

                // 文字列変換処理
                targetDatas[position - 1] = errorList.ReplaceData;
            }
            else
            {
                // 位置情報が取得できなかった場合は
                // バッチ処理エラーメッセージを表示する
                errorList.ReplacePlace = string.Empty;
                errorList.ReplaceItemName = string.Empty;
                errorList.InputData = string.Empty;
                errorList.ReplaceData = string.Empty;
                errorList.ErrFlgData = string.Empty;
                errorList.BatchErrMessage = MessageResource.MBECH014;

                // エラーリスト追加処理
                _errorLists.Add(errorList);
            }

            return targetDatas;
        }

        /// <summary>
        /// 文字列変換処理（固定長用）
        /// </summary>
        /// <param name="matter">事項情報</param>
        /// <param name="errorList">対象エラーリスト</param>
        /// <param name="targetDataLine">対象データ行</param>
        private void ReplaceStringForFixed(
            StMatter matter, GACH001510ErrorList errorList, ref string targetDataLine)
        {
            if (matter.Bytes is null)
            {
                throw new NullReferenceException(MessageResource.MBECH014);
            }

            if (int.TryParse(matter.Position, out int position) &&
                int.TryParse(matter.Bytes, out int bytes) && targetDataLine.Length >= bytes &&
                bytes == errorList.ReplaceData.Length)
            {
                // エラーリスト追加処理
                _errorLists.Add(errorList);

                // 文字列変換処理
                var sb = new StringBuilder(targetDataLine);
                sb.Remove(position - 1, bytes);
                sb.Insert(position - 1, errorList.ReplaceData);
                targetDataLine = sb.ToString();
                return;
            }

            // バイト数が帳票メタと異なる場合や位置、バイト数がint変換できない場合は
            // バッチ処理エラーメッセージを表示する
            errorList.ReplacePlace = string.Empty;
            errorList.ReplaceItemName = string.Empty;
            errorList.InputData = string.Empty;
            errorList.ReplaceData = string.Empty;
            errorList.ErrFlgData = string.Empty;
            errorList.BatchErrMessage = MessageResource.MBECH003;

            // エラーリスト追加処理
            _errorLists.Add(errorList);
        }

        /// <summary>
        /// エラーリスト追加処理（エラーフラグのみ設定用）
        /// </summary>
        /// <param name="checkDefinitionInfo">チェック定義情報</param>
        /// <param name="checkInfoDetail">定義情報詳細</param>
        /// <exception cref="NullReferenceException">エラーフラグが設定されていない場合</exception>
        private void AddErrFlgErrorList(
            GACH001510CheckDefinitionInfo checkDefinitionInfo, GACH001510CheckInfoDetail checkInfoDetail)
        {
            if (string.IsNullOrEmpty(checkInfoDetail.ErrFlg))
            {
                throw new NullReferenceException(MessageResource.MBECH024);
            }

            _errorLists.Add(new GACH001510ErrorList
            {
                CheckNum = checkDefinitionInfo.CheckNum.ToString(),
                CheckSummary = checkDefinitionInfo.CheckSummary,
                ErrorRow = _targetDataRowCount.ToString(),
                ReplacePlace = string.Empty,
                ReplaceItemName = string.Empty,
                InputData = string.Empty,
                ReplaceData = string.Empty,
                ErrFlgData = checkInfoDetail.ErrFlg,
                BatchErrMessage = string.Empty
            });
        }

        /// <summary>
        /// エラーリスト追加処理（バッチエラーメッセージ用）
        /// </summary>
        /// <param name="batchErrMessage">バッチ用エラーメッセージ</param>
        /// <param name="checkDefinitionInfo">チェック定義情報</param>
        private void AddErrorListForBatchErrMessage(string batchErrMessage, GACH001510CheckDefinitionInfo checkDefinitionInfo)
        {
            _errorLists.Add(new GACH001510ErrorList
            {
                CheckNum = checkDefinitionInfo.CheckNum.ToString(),
                CheckSummary = checkDefinitionInfo.CheckSummary,
                ErrorRow = _targetDataRowCount.ToString(),
                ReplacePlace = string.Empty,
                ReplaceItemName = string.Empty,
                InputData = string.Empty,
                ReplaceData = string.Empty,
                ErrFlgData = string.Empty,
                BatchErrMessage = batchErrMessage
            });
        }

        /// <summary>
        /// 処理続行不可エラーリスト追加処理（バッチエラーメッセージ用）
        /// </summary>
        /// <param name="batchErrMessage">バッチ用エラーメッセージ</param>
        private void UnContinueErrorListForBatchErrMessage(string batchErrMessage)
        {
            _errorLists.Clear();
            _errorLists.Add(new GACH001510ErrorList
            {
                CheckNum            = string.Empty,
                CheckSummary        = string.Empty,
                ErrorRow            = string.Empty,
                ReplacePlace        = string.Empty,
                ReplaceItemName     = string.Empty,
                InputData           = string.Empty,
                ReplaceData         = string.Empty,
                ErrFlgData          = string.Empty,
                BatchErrMessage = batchErrMessage
            });
        }

        /// <summary>
        /// 「△」→半角スペースに置換
        ///チェック対象条件リスト.符号
        ///項目チェック対象条件リスト.符号
        ///参照ファイル対象条件リスト.符号
        ////変換文字列
        /// </summary>
        /// <param name="paramDefinitionInfo">定義情報</param>
        static private GACHDefinition ReplaceTriangleToSpace(GACHDefinition paramDefinitionInfo)
        {
            GACHDefinition retDefinitionInfo = paramDefinitionInfo;


            // △→半角スペース変換
            foreach (var wkCheckDefinitionInfo in retDefinitionInfo.CheckDefinitionInfoList)
            {
                foreach (var wkCheckInfoDetail in wkCheckDefinitionInfo.CheckInfoDetailList)
                {
                    // チェック対象条件リスト.符号
                    foreach (var wkCheckTargetConditionItem in wkCheckInfoDetail.CheckTargetConditionItemList)
                    {
                        if (!string.IsNullOrEmpty(wkCheckTargetConditionItem.Sign) && wkCheckTargetConditionItem.Sign.Contains(TRIANGLE))
                        {
                            wkCheckTargetConditionItem.Sign = wkCheckTargetConditionItem.Sign.Replace(TRIANGLE, SPACE_SINGLE_BYTE);
                        }
                    }

                    // 項目チェック対象条件リスト.符号
                    foreach (var wkItemCheckTargetCondition in wkCheckInfoDetail.ItemCheckTargetConditionList)
                    {
                        if (!string.IsNullOrEmpty(wkItemCheckTargetCondition.Sign) && wkItemCheckTargetCondition.Sign.Contains(TRIANGLE))
                        {
                            wkItemCheckTargetCondition.Sign = wkItemCheckTargetCondition.Sign.Replace(TRIANGLE, SPACE_SINGLE_BYTE);
                        }
                    }


                    // 参照ファイル対象条件リスト.符号
                    foreach (var wkRefFileTargetCondition in wkCheckInfoDetail.RefFileTargetConditionList)
                    {
                        if (!string.IsNullOrEmpty(wkRefFileTargetCondition.Sign) && wkRefFileTargetCondition.Sign.Contains(TRIANGLE))
                        {
                            wkRefFileTargetCondition.Sign = wkRefFileTargetCondition.Sign.Replace(TRIANGLE, SPACE_SINGLE_BYTE);
                        }
                    }


                    // 変換文字列
                    foreach (var wkReplaceString in wkCheckInfoDetail.ReplaceStringList)
                    {
                        if (!string.IsNullOrEmpty(wkReplaceString.ReplaceString) && wkReplaceString.ReplaceString.Contains(TRIANGLE))
                        {
                            wkReplaceString.ReplaceString = wkReplaceString.ReplaceString.Replace(TRIANGLE, SPACE_SINGLE_BYTE);
                        }
                    }
                }
            }

            return retDefinitionInfo;
        }
    }
}