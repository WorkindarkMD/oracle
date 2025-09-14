using System;
using System.Linq;
using cAlgo.API;
using cAlgo.API.Indicators;
using System.Collections.Generic;
using System.Text;
using System.IO;

namespace cAlgo.Robots
{
    public enum MarketType
    {
        Auto = 0,
        ForexMajor = 1,
        ForexMinor = 2,
        ForexExotic = 3,
        Crypto = 4,
        Metals = 5,
        Oil = 6,
        Indices = 7,
        Stocks = 8
    }

    public enum LiquidityBehavior
    {
        Building,
        Weakening,
        Iceberg,
        Absorption,
        StackedImbalance
    }

    public enum TrendDirection
    {
        Bullish,
        Bearish,
        Sideways,
        Uncertain
    }

    public enum PricePosition
    {
        AboveVA,
        BelowVA,
        InVA,
        AtPOC
    }

    public enum ConflictType
    {
        None,
        BullishDivergence,
        BearishDivergence,
        TimeFrameConflict,
        VolumeConflict
    }

    public enum TeamRole
    {
        Farmer,
        Hunter,
        Scalper,
        Captain
    }

    public enum SpecialistStatus
    {
        Active,
        Waiting,
        Blocked,
        Emergency
    }

    public enum CaptainCommand
    {
        Proceed,
        ReduceSize,
        Block,
        EmergencyExit
    }

    public enum ProfitPreset
    {
        Custom,
        P20,
        P30,
        P40,
        P50,
        P80,
        P100,
        Legacy30
    }

    public class MarketSettings
    {
        public int OrdersCount { get; set; }
        public double StepPips { get; set; }
        public double TakeProfitPips { get; set; }
        public double MaxSpreadPips { get; set; }
        public string MarketName { get; set; }
    }

    public class LiquidityAnchor
    {
        public double Price { get; set; }
        public double Volume { get; set; }
        public TradeType Side { get; set; }
        public double Imbalance { get; set; }
        public bool IsStrong { get; set; }
        public DateTime LastUpdate { get; set; }
        public LiquidityBehavior Behavior { get; set; }
        public int RefreshCount { get; set; }
        public double InitialVolume { get; set; }
    }

    public class ClusterData
    {
        public double Price { get; set; }
        public double BuyVolume { get; set; }
        public double SellVolume { get; set; }
        public double Delta { get; set; }
        public DateTime Timestamp { get; set; }
        public bool IsAggressive { get; set; }
        public LiquidityBehavior Behavior { get; set; }
        public double ImbalanceRatio { get; set; }
    }

    public class TimeFrameAnalysis
    {
        public string Name { get; set; }
        public TrendDirection Trend { get; set; }
        public PricePosition PricePosition { get; set; }
        public double CumulativeDelta { get; set; }
        public double VolumeProfile { get; set; }
        public double POC { get; set; }
        public double VAH { get; set; }
        public double VAL { get; set; }
        public DateTime LastUpdate { get; set; }
        public bool IsValid { get; set; }
    }

    public class VolumeProfile
    {
        public double POC { get; set; }
        public double VAH { get; set; }
        public double VAL { get; set; }
        public Dictionary<double, double> VolumeAtPrice { get; set; }
        public bool IsBalanced { get; set; }
        public string ProfileType { get; set; }

        public VolumeProfile()
        {
            VolumeAtPrice = new Dictionary<double, double>();
        }
    }

    // Специалист команды
    public class TeamSpecialist
    {
        public TeamRole Role { get; set; }
        public SpecialistStatus Status { get; set; }
        public double CurrentExposure { get; set; }
        public double MaxAllowedExposure { get; set; }
        public DateTime LastAction { get; set; }
        public int ActionsToday { get; set; }
        public double ProfitToday { get; set; }
        public string StatusMessage { get; set; }
    }

        // Grant full access for file operations (per user request)
        [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.FullAccess)]
    public class OlympianQuantumTrader : Robot
    {
        // ========== File Logging ==========
        public enum LogLevel { Error = 0, Info = 1, Debug = 2 }

        [Parameter("�������� ��� � ����", DefaultValue = true, Group = "19. Logging")]
        public bool EnableFileLogging { get; set; }

        [Parameter("����� ����� (�����=���������)", DefaultValue = "", Group = "19. Logging")]
        public string LogFolderPath { get; set; }

        [Parameter("������� ����������� (0-2)", DefaultValue = 1, MinValue = 0, MaxValue = 2, Group = "19. Logging")]
        public int LogVerbosity { get; set; }

        private string _logFilePath = string.Empty;
        private object _logLock = new object();
        private StreamWriter _logWriter = null;

        private void LogToFile(LogLevel level, string message)
        {
            try
            {
                if (!EnableFileLogging) return;
                if ((int)level > LogVerbosity) return;
                if (_logWriter == null) return;
                string ts = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                lock (_logLock)
                {
                    _logWriter.WriteLine($"{ts} [{level}] {message}");
                    _logWriter.Flush();
                }
            }
            catch { }
        }

        private void InitLogger()
        {
            try
            {
                if (!EnableFileLogging)
                    return;
                string folder = LogFolderPath;
                if (string.IsNullOrWhiteSpace(folder))
                {
                    string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                    folder = Path.Combine(docs, "cAlgo", "Logs");
                }
                Directory.CreateDirectory(folder);
                string fileName = $"{GetType().Name}_{SymbolName}_{Server.Time:yyyyMMdd_HHmmss}.log";
                _logFilePath = Path.Combine(folder, fileName);
                _logWriter = new StreamWriter(new FileStream(_logFilePath, FileMode.Create, FileAccess.Write, FileShare.Read)) { AutoFlush = true };
                LogToFile(LogLevel.Info, $"Logger initialized: {_logFilePath}");
            }
            catch (Exception ex)
            {
                Print($"[Logging] Init failed: {ex.Message}");
            }
        }

        private void CloseLogger()
        {
            try
            {
                if (_logWriter != null)
                {
                    LogToFile(LogLevel.Info, "Logger closing");
                    _logWriter.Flush();
                    _logWriter.Dispose();
                    _logWriter = null;
                }
            }
            catch { }
        }

        // Converts absolute TP/SL prices to pip distances for order APIs
        private (double? slPips, double? tpPips) ToPipDistances(TradeType tradeType, double entryPrice, double? slPrice, double? tpPrice)
        {
            try
            {
                double? slPips = null;
                double? tpPips = null;
                if (slPrice.HasValue)
                    slPips = Math.Abs((entryPrice - slPrice.Value) / Symbol.PipSize);
                if (tpPrice.HasValue)
                    tpPips = Math.Abs((tpPrice.Value - entryPrice) / Symbol.PipSize);
                return (slPips, tpPips);
            }
            catch { return (null, null); }
        }
        // Dynamic Targets parameters (separate group)
        [Parameter("--- Dynamic Targets ---", Group = "16. Dynamic Targets")]
        public string DynSep { get; set; }

        [Parameter("Включить динамические TP/SL", DefaultValue = true, Group = "16. Dynamic Targets")]
        public bool UseDynamicTargets { get; set; }

        [Parameter("ATR множитель базового TP", DefaultValue = 1.3, MinValue = 0.2, MaxValue = 5.0, Group = "16. Dynamic Targets")]
        public double TpAtrMultiplier { get; set; }

        [Parameter("ATR множитель базового SL", DefaultValue = 1.1, MinValue = 0.2, MaxValue = 5.0, Group = "16. Dynamic Targets")]
        public double SlAtrMultiplier { get; set; }

        [Parameter("Мин. SL как доля ATR", DefaultValue = 1.2, MinValue = 0.2, MaxValue = 5.0, Group = "16. Dynamic Targets")]
        public double MinSlAtrMultiplier { get; set; }

        [Parameter("Буфер за уровнем (доля ATR)", DefaultValue = 0.25, MinValue = 0.0, MaxValue = 1.0, Group = "16. Dynamic Targets")]
        public double LevelBufferAtr { get; set; }

        [Parameter("Свинг lookback (бары)", DefaultValue = 50, MinValue = 10, MaxValue = 500, Group = "16. Dynamic Targets")]
        public int SwingLookback { get; set; }

        [Parameter("Сессии: использовать Server.Time", DefaultValue = true, Group = "16. Dynamic Targets")]
        public bool UseServerTimeForSession { get; set; }

        [Parameter("Смещение сессии (часы)", DefaultValue = 0, MinValue = -12, MaxValue = 14, Group = "16. Dynamic Targets")]
        public int SessionTimezoneOffsetHours { get; set; }

        [Parameter("Fixed StopLoss (pips)", DefaultValue = 20.0, MinValue = 0.0, Group = "16. Dynamic Targets")]
        public double FixedStopLossPips { get; set; }

        [Parameter("Fixed TakeProfit (pips)", DefaultValue = 10.0, MinValue = 0.0, Group = "16. Dynamic Targets")]
        public double FixedTakeProfitPips { get; set; }

        [Parameter("Учитывать комиссию в TP", DefaultValue = true, Group = "16. Dynamic Targets")]
        public bool IncludeCommissionInTargets { get; set; }

        [Parameter("Комиссия за сторону (на 1 лот, валюта счета)", DefaultValue = 0.0, MinValue = 0.0, Group = "16. Dynamic Targets")]
        public double CommissionPerLotPerSide { get; set; }

        // Range-adaptive targets (��� ������ ���������)
        [Parameter("���������� TP � ��������", DefaultValue = true, Group = "16. Dynamic Targets")]
        public bool EnableRangeAdaptiveTargets { get; set; }

        [Parameter("���� �������� (���)", DefaultValue = 10, MinValue = 1, MaxValue = 120, Group = "16. Dynamic Targets")]
        public int RangeCompressionWindowMinutes { get; set; }

        [Parameter("������ ��������� (USD)", DefaultValue = 1.5, MinValue = 0.1, MaxValue = 10.0, Group = "16. Dynamic Targets")]
        public double RangeCompressionWidthUSD { get; set; }

        [Parameter("������ TP (USD)", DefaultValue = 1.0, MinValue = 0.1, MaxValue = 10.0, Group = "16. Dynamic Targets")]
        public double AdaptiveTpUSD { get; set; }

        // --- Auto Close (18) ---
        [Parameter("--- Авто-закрытие прибыли ---", Group = "18. Auto Close")]
        public string AutoCloseSep { get; set; }

        [Parameter("Вкл. автозакрытие при прибыли", DefaultValue = false, Group = "18. Auto Close")]
        public bool AutoCloseOnNetProfit { get; set; }

        [Parameter("Интервал проверки (мин)", DefaultValue = 1, MinValue = 0, MaxValue = 60, Group = "18. Auto Close")]
        public int AutoCloseCheckIntervalMinutes { get; set; }

        [Parameter("Интервал проверки (сек)", DefaultValue = 3, MinValue = 0, MaxValue = 60, Group = "18. Auto Close")]
        public int AutoCloseCheckIntervalSeconds { get; set; }

        [Parameter("Порог прибыли (% от баланса)", DefaultValue = 0.2, MinValue = -50.0, MaxValue = 50.0, Group = "18. Auto Close")]
        public double ProfitThresholdPercentOfBalance { get; set; }

        [Parameter("Закрывать только профитные позиции", DefaultValue = false, Group = "18. Auto Close")]
        public bool AutoCloseOnlyProfitablePositions { get; set; }

        [Parameter("Отменять pending-ордера при автозакрытии", DefaultValue = true, Group = "18. Auto Close")]
        public bool AutoCloseCancelPendingOrders { get; set; }

        [Parameter("Вкл. автозакрытие по сумме (валюта)", DefaultValue = false, Group = "18. Auto Close")]
        public bool AutoCloseOnNetProfitAbs { get; set; }

        [Parameter("Исключить Hunter из автозакрытия", DefaultValue = true, Group = "18. Auto Close")]
        public bool ExcludeHunterFromAutoClose { get; set; }

        [Parameter("Порог суммарной прибыли (валюта)", DefaultValue = 0.0, MinValue = 0.0, Group = "18. Auto Close")]
        public double NetProfitAbsTarget { get; set; }

        [Parameter("Порог суммарного убытка (валюта)", DefaultValue = 0.0, MinValue = 0.0, Group = "18. Auto Close")]
        public double NetLossAbsTarget { get; set; }

        [Parameter("Вкл. персделочное автозакрытие", DefaultValue = false, Group = "18. Auto Close")]
        public bool AutoClosePerPosition { get; set; }

        [Parameter("Порог профита сделки (валюта)", DefaultValue = 0.0, MinValue = 0.0, Group = "18. Auto Close")]
        public double PerPositionProfitTargetCurrency { get; set; }

        [Parameter("Порог убытка сделки (валюта)", DefaultValue = 0.0, MinValue = 0.0, Group = "18. Auto Close")]
        public double PerPositionLossTargetCurrency { get; set; }

        // Net Profit Trailing (lock-in)
        [Parameter("Вкл. тралл общей прибыли", DefaultValue = true, Group = "18. Auto Close")]
        public bool AutoCloseNetProfitTrailing { get; set; }

        [Parameter("Старт тралла Net (валюта)", DefaultValue = 0.0, MinValue = 0.0, Group = "18. Auto Close")]
        public double NetTrailStartCurrency { get; set; }

        [Parameter("Шаг тралла Net (валюта)", DefaultValue = 0.0, MinValue = 0.0, Group = "18. Auto Close")]
        public double NetTrailStepCurrency { get; set; }

        [Parameter("Старт тралла Net (% от баланса)", DefaultValue = 0.3, MinValue = 0.0, MaxValue = 100.0, Group = "18. Auto Close")]
        public double NetTrailStartPercentOfBalance { get; set; }

        [Parameter("Шаг тралла Net (% от баланса)", DefaultValue = 0.15, MinValue = 0.0, MaxValue = 100.0, Group = "18. Auto Close")]
        public double NetTrailStepPercentOfBalance { get; set; }

        [Parameter("Доля частичного на шаге", DefaultValue = 0.5, MinValue = 0.0, MaxValue = 1.0, Group = "18. Auto Close")]
        public double NetTrailLockFraction { get; set; }

        [Parameter("Тралл: только плюсовые позиции", DefaultValue = true, Group = "18. Auto Close")]
        public bool NetTrailOnlyProfitablePositions { get; set; }

        [Parameter("Тралл: макс. позиций за шаг (0=все)", DefaultValue = 0, MinValue = 0, MaxValue = 500, Group = "18. Auto Close")]
        public int NetTrailMaxPositionsPerStep { get; set; }

        [Parameter("Пауза после шага тралла", DefaultValue = false, Group = "18. Auto Close")]
        public bool NetTrailPauseAfterLock { get; set; }

        // Net Loss Trailing (lock-in on drawdown rebound)
        [Parameter("Вкл. тралл убытка (Net)", DefaultValue = true, Group = "18. Auto Close")]
        public bool AutoCloseNetLossTrailing { get; set; }

        [Parameter("Старт тралла убытка (валюта)", DefaultValue = 40.0, MinValue = 0.0, Group = "18. Auto Close")]
        public double LossTrailStartCurrency { get; set; }

        [Parameter("Шаг тралла убытка (валюта)", DefaultValue = 15.0, MinValue = 0.0, Group = "18. Auto Close")]
        public double LossTrailStepCurrency { get; set; }

        [Parameter("Доля частичного на шаге (убыток)", DefaultValue = 0.40, MinValue = 0.0, MaxValue = 1.0, Group = "18. Auto Close")]
        public double LossTrailLockFraction { get; set; }

        [Parameter("Тралл убытка: только убыточные позиции", DefaultValue = true, Group = "18. Auto Close")]
        public bool LossTrailOnlyLosingPositions { get; set; }

        [Parameter("Тралл убытка: макс. позиций за шаг (0=все)", DefaultValue = 4, MinValue = 0, MaxValue = 500, Group = "18. Auto Close")]
        public int LossTrailMaxPositionsPerStep { get; set; }

        [Parameter("Пауза после тралла убытка", DefaultValue = false, Group = "18. Auto Close")]
        public bool LossTrailPauseAfterLock { get; set; }

        // Daily targets
        [Parameter("Вкл. дневные цели", DefaultValue = false, Group = "18. Auto Close")]
        public bool DailyTargetsEnable { get; set; }

        [Parameter("Дневная прибыль (% от баланса)", DefaultValue = 0.8, MinValue = 0.0, MaxValue = 100.0, Group = "18. Auto Close")]
        public double DailyProfitTargetPercentOfBalance { get; set; }

        [Parameter("Дневной лимит убытка (% от баланса)", DefaultValue = 0.8, MinValue = 0.0, MaxValue = 100.0, Group = "18. Auto Close")]
        public double DailyLossLimitPercentOfBalance { get; set; }

        [Parameter("Дневные цели учитывают открытую P/L", DefaultValue = true, Group = "18. Auto Close")]
        public bool DailyUseOpenEquity { get; set; }

        [Parameter("Дневные цели: закрыть позиции при срабатывании", DefaultValue = true, Group = "18. Auto Close")]
        public bool DailyClosePositionsOnHit { get; set; }

        [Parameter("Дневные цели: поставить на паузу", DefaultValue = true, Group = "18. Auto Close")]
        public bool DailyPauseOnHit { get; set; }

        [Parameter("Час сброса дня (лок. сессия)", DefaultValue = 0, MinValue = 0, MaxValue = 23, Group = "18. Auto Close")]
        public int DailyResetHour { get; set; }

        #region Trap Grid

        [Parameter("--- Trap Grid ---", Group = "17. Trap Grid")]
        public string TrapSep { get; set; }

        [Parameter("Включить Trap Grid", DefaultValue = false, Group = "17. Trap Grid")]
        public bool UseTrapGrid { get; set; }

        [Parameter("Mean-Reversion ловушки", DefaultValue = true, Group = "17. Trap Grid")]
        public bool EnableTrapReversion { get; set; }

        [Parameter("Breakout ловушки", DefaultValue = true, Group = "17. Trap Grid")]
        public bool EnableTrapBreakout { get; set; }

        [Parameter("Макс. зон ловушек", DefaultValue = 3, MinValue = 1, MaxValue = 10, Group = "17. Trap Grid")]
        public int MaxTrapZones { get; set; }

        [Parameter("ATR-буфер ловушки", DefaultValue = 0.25, MinValue = 0.05, MaxValue = 1.0, Group = "17. Trap Grid")]
        public double TrapAtrBuffer { get; set; }

        [Parameter("Ширина зоны (пипсы)", DefaultValue = 5.0, MinValue = 0.0, Group = "17. Trap Grid")]
        public double TrapZoneBufferPips { get; set; }

        [Parameter("Перестройка ловушек (сек)", DefaultValue = 60, MinValue = 10, MaxValue = 600, Group = "17. Trap Grid")]
        public int TrapRebuildSeconds { get; set; }

        [Parameter("Trap OCO (отмена соседей)", DefaultValue = true, Group = "17. Trap Grid")]
        public bool TrapUseOCO { get; set; }

        #endregion

        #region Общие параметры

        [Parameter("--- Общие Настройки ---", Group = "1. General")]
        public string Separator1 { get; set; }

        [Parameter("Объем ордера (лоты)", DefaultValue = 0.01, MinValue = 0.01, Group = "1. General")]
        public double OrderVolumeLots { get; set; }

        [Parameter("Автоматическое определение рынка", DefaultValue = true, Group = "1. General")]
        public bool AutoDetectMarket { get; set; }

        [Parameter("Ручной выбор типа рынка", DefaultValue = 0, Group = "1. General")]
        public MarketType ManualMarketType { get; set; }

        [Parameter("Таймфрейм для анализа (0=M1, 1=M3, 2=M5)", DefaultValue = 0, Group = "1. General")]
        public int AnalysisTimeFrameIndex { get; set; }

        [Parameter("Профиль агрессии (%/день)", DefaultValue = ProfitPreset.P20, Group = "1. General")]
        public ProfitPreset AggressionProfile { get; set; }

        [Parameter("Применить пресет на старте", DefaultValue = true, Group = "1. General")]
        public bool ApplyPresetOnStart { get; set; }

        [Parameter("Только GOLD/XAU", DefaultValue = true, Group = "1. General")]
        public bool OnlyGold { get; set; }

        [Parameter("Запретить крипторынок", DefaultValue = true, Group = "1. General")]
        public bool BlockCrypto { get; set; }

        #endregion

        #region Настройки рынков

        [Parameter("--- Настройки Forex Major ---", Group = "2. Forex Major")]
        public string ForexMajorSep { get; set; }

        [Parameter("Кол-во ордеров (каждая сторона)", DefaultValue = 100, Group = "2. Forex Major")]
        public int ForexMajorOrders { get; set; }

        [Parameter("Шаг сетки (пипсы)", DefaultValue = 0.2, Group = "2. Forex Major")]
        public double ForexMajorStep { get; set; }

        [Parameter("Тейк-профит (пипсы)", DefaultValue = 0.3, Group = "2. Forex Major")]
        public double ForexMajorTP { get; set; }

        [Parameter("Макс. спред (пипсы)", DefaultValue = 0.8, Group = "2. Forex Major")]
        public double ForexMajorMaxSpread { get; set; }

        [Parameter("--- Настройки Forex Minor ---", Group = "3. Forex Minor")]
        public string ForexMinorSep { get; set; }

        [Parameter("Кол-во ордеров (каждая сторона)", DefaultValue = 75, Group = "3. Forex Minor")]
        public int ForexMinorOrders { get; set; }

        [Parameter("Шаг сетки (пипсы)", DefaultValue = 0.4, Group = "3. Forex Minor")]
        public double ForexMinorStep { get; set; }

        [Parameter("Тейк-профит (пипсы)", DefaultValue = 0.6, Group = "3. Forex Minor")]
        public double ForexMinorTP { get; set; }

        [Parameter("Макс. спред (пипсы)", DefaultValue = 1.2, Group = "3. Forex Minor")]
        public double ForexMinorMaxSpread { get; set; }

        [Parameter("--- Настройки Forex Exotic ---", Group = "4. Forex Exotic")]
        public string ForexExoticSep { get; set; }

        [Parameter("Кол-во ордеров (каждая сторона)", DefaultValue = 40, Group = "4. Forex Exotic")]
        public int ForexExoticOrders { get; set; }

        [Parameter("Шаг сетки (пипсы)", DefaultValue = 1.5, Group = "4. Forex Exotic")]
        public double ForexExoticStep { get; set; }

        [Parameter("Тейк-профит (пипсы)", DefaultValue = 2.0, Group = "4. Forex Exotic")]
        public double ForexExoticTP { get; set; }

        [Parameter("Макс. спред (пипсы)", DefaultValue = 3.0, Group = "4. Forex Exotic")]
        public double ForexExoticMaxSpread { get; set; }

        [Parameter("--- Настройки Криптовалют ---", Group = "5. Crypto")]
        public string CryptoSep { get; set; }

        [Parameter("Кол-во ордеров (каждая сторона)", DefaultValue = 150, Group = "5. Crypto")]
        public int CryptoOrders { get; set; }

        [Parameter("Шаг сетки (пипсы)", DefaultValue = 0.1, Group = "5. Crypto")]
        public double CryptoStep { get; set; }

        [Parameter("Тейк-профит (пипсы)", DefaultValue = 0.15, Group = "5. Crypto")]
        public double CryptoTP { get; set; }

        [Parameter("Стоп-лосс (пипсы)", DefaultValue = 50, Group = "5. Crypto")]
        public double CryptoSL { get; set; }

        [Parameter("Макс. спред (пипсы)", DefaultValue = 2.0, Group = "5. Crypto")]
        public double CryptoMaxSpread { get; set; }

        [Parameter("--- Настройки Металлов (Gold, Silver) ---", Group = "6. Metals")]
        public string MetalsSep { get; set; }

        [Parameter("Кол-во ордеров (каждая сторона)", DefaultValue = 80, Group = "6. Metals")]
        public int MetalsOrders { get; set; }

        [Parameter("Шаг сетки (пипсы)", DefaultValue = 5.0, Group = "6. Metals")]
        public double MetalsStep { get; set; }

        [Parameter("Тейк-профит (пипсы)", DefaultValue = 8.0, Group = "6. Metals")]
        public double MetalsTP { get; set; }

        [Parameter("Макс. спред (пипсы)", DefaultValue = 15.0, Group = "6. Metals")]
        public double MetalsMaxSpread { get; set; }

        [Parameter("--- Настройки Нефти ---", Group = "7. Oil")]
        public string OilSep { get; set; }

        [Parameter("Кол-во ордеров (каждая сторона)", DefaultValue = 60, Group = "7. Oil")]
        public int OilOrders { get; set; }

        [Parameter("Шаг сетки (пипсы)", DefaultValue = 2.0, Group = "7. Oil")]
        public double OilStep { get; set; }

        [Parameter("Тейк-профит (пипсы)", DefaultValue = 3.0, Group = "7. Oil")]
        public double OilTP { get; set; }

        [Parameter("Макс. спред (пипсы)", DefaultValue = 5.0, Group = "7. Oil")]
        public double OilMaxSpread { get; set; }

        [Parameter("--- Настройки Индексов ---", Group = "8. Indices")]
        public string IndicesSep { get; set; }

        [Parameter("Кол-во ордеров (каждая сторона)", DefaultValue = 50, Group = "8. Indices")]
        public int IndicesOrders { get; set; }

        [Parameter("Шаг сетки (пипсы)", DefaultValue = 1.0, Group = "8. Indices")]
        public double IndicesStep { get; set; }

        [Parameter("Тейк-профит (пипсы)", DefaultValue = 1.5, Group = "8. Indices")]
        public double IndicesTP { get; set; }

        [Parameter("Макс. спред (пипсы)", DefaultValue = 3.0, Group = "8. Indices")]
        public double IndicesMaxSpread { get; set; }

        [Parameter("--- Настройки Акций ---", Group = "9. Stocks")]
        public string StocksSep { get; set; }

        [Parameter("Кол-во ордеров (каждая сторона)", DefaultValue = 10, Group = "9. Stocks")]
        public int StocksOrders { get; set; }

        [Parameter("Шаг сетки (пипсы)", DefaultValue = 0.5, Group = "9. Stocks")]
        public double StocksStep { get; set; }

        [Parameter("Тейк-профит (пипсы)", DefaultValue = 1.0, Group = "9. Stocks")]
        public double StocksTP { get; set; }

        [Parameter("Макс. спред (пипсы)", DefaultValue = 2.0, Group = "9. Stocks")]
        public double StocksMaxSpread { get; set; }

        #endregion

        #region Настройки Команды

        [Parameter("--- Настройки Команды ---", Group = "10. Team Settings")]
        public string TeamSep { get; set; }

        [Parameter("Включить параллельную команду", DefaultValue = true, Group = "10. Team Settings")]
        public bool EnableParallelTeam { get; set; }

        [Parameter("Макс. общая экспозиция (лоты)", DefaultValue = 0.5, Group = "10. Team Settings")]
        public double MaxTotalExposureLots { get; set; }

        [Parameter("Порог дисбаланса L2 для Охотника (%)", DefaultValue = 65, Group = "10. Team Settings")]
        public int HunterImbalanceThreshold { get; set; }

        [Parameter("Множитель ATR для Охотника", DefaultValue = 2.0, Group = "10. Team Settings")]
        public double HunterAtrMultiplier { get; set; }

        [Parameter("Минимальный объем якоря ликвидности", DefaultValue = 50000, Group = "10. Team Settings")]
        public double MinAnchorVolume { get; set; }

        // Hunter dynamic triggering in compression + pseudo-imbalance when L2 is thin/empty
        [Parameter("� ����������: ������� ����� Hunter", DefaultValue = true, Group = "10. Team Settings")]
        public bool EnableHunterDynamicThreshold { get; set; }

        [Parameter("��������� ������ � ���������� (0.1�1.0)", DefaultValue = 0.7, MinValue = 0.1, MaxValue = 1.0, Group = "10. Team Settings")]
        public double HunterThresholdCompressionFactor { get; set; }

        [Parameter("������?�������� ��� ������ L2", DefaultValue = true, Group = "10. Team Settings")]
        public bool EnablePseudoImbalanceWhenL2Empty { get; set; }

        [Parameter("������?��������, % (���� �� ������ LTF)", DefaultValue = 45, MinValue = 0, MaxValue = 100, Group = "10. Team Settings")]
        public int PseudoImbalancePercent { get; set; }

        [Parameter("������?�������� ������ � ����������", DefaultValue = true, Group = "10. Team Settings")]
        public bool PseudoImbalanceOnlyInCompression { get; set; }

        #endregion

        #region Кластерный анализ

        [Parameter("--- Кластерный Анализ ---", Group = "11. Cluster Analysis")]
        public string ClusterSep { get; set; }

        [Parameter("Включить кластерный анализ", DefaultValue = true, Group = "11. Cluster Analysis")]
        public bool EnableClusterAnalysis { get; set; }

        [Parameter("Порог агрессивного объема (%)", DefaultValue = 30, Group = "11. Cluster Analysis")]
        public double AggressiveVolumeThreshold { get; set; }

        [Parameter("Отслеживать айсберг-ордера", DefaultValue = true, Group = "11. Cluster Analysis")]
        public bool TrackIcebergOrders { get; set; }

        #endregion

        #region Мульти-таймфрейм анализ

        [Parameter("--- Мульти-Таймфрейм Анализ ---", Group = "12. Multi-TimeFrame")]
        public string MultiTFSep { get; set; }

        [Parameter("Включить мульти-ТФ анализ", DefaultValue = true, Group = "12. Multi-TimeFrame")]
        public bool EnableMultiTimeFrameAnalysis { get; set; }

        [Parameter("Высший таймфрейм (0=M5, 1=M15, 2=H1)", DefaultValue = 1, Group = "12. Multi-TimeFrame")]
        public int HigherTimeFrameIndex { get; set; }

        [Parameter("Нижний таймфрейм (0=M1, 1=M3, 2=M5)", DefaultValue = 0, Group = "12. Multi-TimeFrame")]
        public int LowerTimeFrameIndex { get; set; }

        #endregion

        #region Скальпинг конфликтов

        [Parameter("--- Скальпинг Конфликтов ---", Group = "13. Conflict Scalping")]
        public string ConflictScalpSep { get; set; }

        [Parameter("Включить скальпинг конфликтов", DefaultValue = true, Group = "13. Conflict Scalping")]
        public bool EnableConflictScalping { get; set; }

        [Parameter("Мин. время удержания ловушки (сек)", DefaultValue = 30, Group = "13. Conflict Scalping")]
        public int MinTrapHoldSeconds { get; set; }

        [Parameter("Макс. время удержания ловушки (сек)", DefaultValue = 300, Group = "13. Conflict Scalping")]
        public int MaxTrapHoldSeconds { get; set; }

        // HTF Reversal Scalper (Variant B: Profile touch/false-break + flow flip)
        [Parameter("�������� HTF Reversal ������", DefaultValue = true, Group = "13. Conflict Scalping")]
        public bool EnableHtfReversalScalper { get; set; }

        [Parameter("HTF ��� ��������� (0=M1, 1=M5)", DefaultValue = 1, MinValue = 0, MaxValue = 1, Group = "13. Conflict Scalping")]
        public int HtfReversalIndex { get; set; }

        [Parameter("HTF ���� (����)", DefaultValue = 10, MinValue = 3, MaxValue = 200, Group = "13. Conflict Scalping")]
        public int HtfReversalLookback { get; set; }

        [Parameter("����� ������ (���� ATR)", DefaultValue = 0.20, MinValue = 0.0, MaxValue = 2.0, Group = "13. Conflict Scalping")]
        public double HtfLevelBufferAtr { get; set; }

        [Parameter("��������� ����� ����� ?", DefaultValue = true, Group = "13. Conflict Scalping")]
        public bool HtfRequireDeltaSignFlip { get; set; }

        [Parameter("��������� ����� ������� L2", DefaultValue = true, Group = "13. Conflict Scalping")]
        public bool HtfRequireImbSlopeFlip { get; set; }

        [Parameter("HTFScalp SL (ATR)", DefaultValue = 1.0, MinValue = 0.1, MaxValue = 5.0, Group = "13. Conflict Scalping")]
        public double HtfScalpSlAtrMult { get; set; }

        [Parameter("HTFScalp TP (ATR)", DefaultValue = 0.6, MinValue = 0.1, MaxValue = 5.0, Group = "13. Conflict Scalping")]
        public double HtfScalpTpAtrMult { get; set; }

        #endregion

        #region Управление рисками

        [Parameter("--- Управление Рисками ---", Group = "14. Risk Management")]
        public string RiskSep { get; set; }

        [Parameter("Макс. чистая позиция (лоты)", DefaultValue = 0.5, Group = "14. Risk Management")]
        public double MaxNetExposureLots { get; set; }

        [Parameter("Аварийный стоп (% от Equity)", DefaultValue = 10.0, Group = "14. Risk Management")]
        public double EmergencyStopLossPercent { get; set; }

        [Parameter("Макс. открытых позиций", DefaultValue = 10, MinValue = 0, Group = "14. Risk Management")]
        public int MaxOpenPositions { get; set; }

        [Parameter("Пакетная постановка ордеров", DefaultValue = true, Group = "14. Risk Management")]
        public bool UseBatchOrderPlacement { get; set; }

        [Parameter("Размер пакета ордеров", DefaultValue = 2, MinValue = 1, MaxValue = 10, Group = "14. Risk Management")]
        public int BatchOrderSize { get; set; }

        [Parameter("Пауза при FreeMargin < %", DefaultValue = 5.0, MinValue = 0.0, MaxValue = 100.0, Group = "14. Risk Management")]
        public double PauseFreeMarginPercent { get; set; }

        [Parameter("Возобновить при FreeMargin > %", DefaultValue = 7.0, MinValue = 0.0, MaxValue = 100.0, Group = "14. Risk Management")]
        public double ResumeFreeMarginPercent { get; set; }

        [Parameter("Митигировать просадку (вместо Stop)", DefaultValue = true, Group = "14. Risk Management")]
        public bool EnableDrawdownMitigation { get; set; }

        [Parameter("Доля частичного закрытия", DefaultValue = 0.5, MinValue = 0.1, MaxValue = 1.0, Group = "14. Risk Management")]
        public double MitigationPartialCloseFraction { get; set; }

        // Team/Hunter-specific risk tuning
        [Parameter("Резерв маржи под Hunter (лоты)", DefaultValue = 0.05, MinValue = 0.0, Group = "10. Team Settings")]
        public double HunterReserveLots { get; set; }

        #endregion

        #region Advanced Drawdown

        [Parameter("--- Advanced Drawdown ---", Group = "14b. Drawdown Advanced")]
        public string AdvDrawdownSep { get; set; }

        [Parameter("Flow окно (сек)", DefaultValue = 45, MinValue = 10, MaxValue = 600, Group = "14b. Drawdown Advanced")]
        public int FlowWindowSeconds { get; set; }

        [Parameter("Delta окно (баров)", DefaultValue = 20, MinValue = 5, MaxValue = 200, Group = "14b. Drawdown Advanced")]
        public int DeltaWindowBars { get; set; }

        [Parameter("Близость якоря (ATR)", DefaultValue = 1.2, MinValue = 0.2, MaxValue = 5.0, Group = "14b. Drawdown Advanced")]
        public double AcceptAnchorAtr { get; set; }

        [Parameter("Жесткий выход ATR", DefaultValue = 1.5, MinValue = 0.5, MaxValue = 5.0, Group = "14b. Drawdown Advanced")]
        public double HardExitAtrMult { get; set; }

        [Parameter("Макс. минут в сделке до decay", DefaultValue = 20, MinValue = 1, MaxValue = 1440, Group = "14b. Drawdown Advanced")]
        public int MaxMinutesInTradeBeforeDecay { get; set; }

        [Parameter("Use Hard Exit Rules", DefaultValue = true, Group = "14b. Drawdown Advanced")]
        public bool UseHardExitRules { get; set; }

        [Parameter("Вес TF", DefaultValue = 0.25, MinValue = 0.0, MaxValue = 1.0, Group = "14b. Drawdown Advanced")]
        public double W_TF { get; set; }

        [Parameter("Вес Flow", DefaultValue = 0.25, MinValue = 0.0, MaxValue = 1.0, Group = "14b. Drawdown Advanced")]
        public double W_Flow { get; set; }

        [Parameter("Вес Profile", DefaultValue = 0.20, MinValue = 0.0, MaxValue = 1.0, Group = "14b. Drawdown Advanced")]
        public double W_Profile { get; set; }

        [Parameter("Вес Anchors", DefaultValue = 0.10, MinValue = 0.0, MaxValue = 1.0, Group = "14b. Drawdown Advanced")]
        public double W_Anchors { get; set; }

        [Parameter("Вес Vol/Time", DefaultValue = 0.10, MinValue = 0.0, MaxValue = 1.0, Group = "14b. Drawdown Advanced")]
        public double W_VolTime { get; set; }

        [Parameter("Вес Expectancy", DefaultValue = 0.10, MinValue = 0.0, MaxValue = 1.0, Group = "14b. Drawdown Advanced")]
        public double W_Expect { get; set; }

        [Parameter("Min доля частичного", DefaultValue = 0.33, MinValue = 0.0, MaxValue = 1.0, Group = "14b. Drawdown Advanced")]
        public double MitigationStepMinFraction { get; set; }

        [Parameter("Max доля частичного", DefaultValue = 0.66, MinValue = 0.0, MaxValue = 1.0, Group = "14b. Drawdown Advanced")]
        public double MitigationStepMaxFraction { get; set; }

        #endregion

        #region Информационная панель

        [Parameter("--- Информационная Панель ---", Group = "15. Dashboard")]
        public string DashboardSep { get; set; }

        [Parameter("Включить панель?", DefaultValue = true, Group = "15. Dashboard")]
        public bool EnableDashboard { get; set; }

        #endregion

        // Приватные переменные для полного функционала
        private MarketDepth _marketDepth;
        private AverageTrueRange _atr;
        private Position _hunterPosition;
        private Bars _analysisBars, _higherTFBars, _lowerTFBars;
        private Bars _htfRevBars;
        private TimeFrame _selectedTimeFrame, _higherTimeFrame, _lowerTimeFrame;
        private Dictionary<double, ClusterData> _trackedClusters;
        private List<LiquidityAnchor> _liquidityAnchors;
        private VolumeProfile _currentVolumeProfile;
        private TimeFrameAnalysis _higherTFAnalysis, _lowerTFAnalysis;
        // Команда специалистов
        private TeamSpecialist _farmer, _hunter, _scalper;
        private double _teamTotalExposure;
        private string _currentStrategy = "Инициализация...";
        private double _orderBookImbalance;
        private MarketType _detectedMarket;
        private MarketSettings _currentSettings;
        private DateTime _lastGridRebuild = DateTime.MinValue;
        private DateTime _lastTrapRebuild = DateTime.MinValue;
        private const string TRAP_LABEL_PREFIX = "Trap";
        private DateTime _lastNetProfitCheck = DateTime.MinValue;
        private double _netProfitPeak = 0.0;
        private double _netLossTrough = 0.0;
        private DateTime _dailyKey = DateTime.MinValue;
        private double _realizedToday = 0.0;
        private bool _pausedByDaily = false;
        private bool _pausedByLossTrail = false;
        // Скальпинг конфликтов
        private bool _scalpingTrapActive = false;
        private DateTime _scalpingTrapTime;
        private double _scalpingTrapCenter;
        private ConflictType _detectedConflict = ConflictType.None;
        // Кластерный анализ
        private double _cumulativeDelta = 0;
        private List<double> _deltaHistory;
        private Dictionary<DateTime, double> _volumeProfile;
        private bool _tradingPaused = false;
        private string _pauseReason = string.Empty;
        private List<Tuple<DateTime, double>> _imbalanceHistory;

        // -------------------- УМНЫЕ ЦЕЛИ TP/SL --------------------
        private (double? tp, double? sl) ComputeDynamicTargets(TradeType tradeType, double entryPrice)
        {
            try
            {
                double atr = Math.Max(_atr?.Result.LastValue ?? 0, Symbol.PipSize * 2);
                double sessionFactor = GetSessionFactor();
                double trendMult = GetTrendMultiplier(tradeType);

                // Базовые дистанции от волатильности и контекста
                double baseTP = atr * (TpAtrMultiplier * sessionFactor) * trendMult;
                double baseSL = atr * (SlAtrMultiplier / Math.Max(0.8, trendMult));

                // Кандидаты уровней из профиля и стакана
                double? vah = _currentVolumeProfile?.VAH > 0 ? _currentVolumeProfile.VAH : (double?)null;
                double? val = _currentVolumeProfile?.VAL > 0 ? _currentVolumeProfile.VAL : (double?)null;
                double? poc = _currentVolumeProfile?.POC > 0 ? _currentVolumeProfile.POC : (double?)null;

                double? anchorAbove = FindNearestAnchorAbove(entryPrice);
                double? anchorBelow = FindNearestAnchorBelow(entryPrice);

                double? swingHigh = GetRecentHighAbove(entryPrice, SwingLookback);
                double? swingLow = GetRecentLowBelow(entryPrice, SwingLookback);

                double? tp = null;
                double? sl = null;

                if (tradeType == TradeType.Buy)
                {
                    var tpCandidates = new List<double>();
                    if (vah.HasValue && vah.Value > entryPrice) tpCandidates.Add(vah.Value);
                    if (poc.HasValue && poc.Value > entryPrice) tpCandidates.Add(poc.Value);
                    if (anchorAbove.HasValue) tpCandidates.Add(anchorAbove.Value);
                    if (swingHigh.HasValue) tpCandidates.Add(swingHigh.Value);
                    tpCandidates.Add(entryPrice + baseTP);

                    tp = tpCandidates.Where(v => v > entryPrice)
                                     .DefaultIfEmpty(entryPrice + baseTP)
                                     .Min();

                    var slCandidates = new List<double>();
                    if (val.HasValue && val.Value < entryPrice) slCandidates.Add(val.Value);
                    if (poc.HasValue && poc.Value < entryPrice) slCandidates.Add(poc.Value);
                    if (anchorBelow.HasValue) slCandidates.Add(anchorBelow.Value);
                    if (swingLow.HasValue) slCandidates.Add(swingLow.Value);
                    slCandidates.Add(entryPrice - baseSL);

                    var slPick = slCandidates.Where(v => v < entryPrice)
                                            .DefaultIfEmpty(entryPrice - baseSL)
                                            .Max();
                    sl = slPick - atr * LevelBufferAtr;
                    // Минимальная дистанция SL по ATR
                    double minSl = entryPrice - Math.Max(atr * MinSlAtrMultiplier, atr * 0.5);
                    sl = Math.Min(sl.Value, minSl);
                }
                else // Sell
                {
                    var tpCandidates = new List<double>();
                    if (val.HasValue && val.Value < entryPrice) tpCandidates.Add(val.Value);
                    if (poc.HasValue && poc.Value < entryPrice) tpCandidates.Add(poc.Value);
                    if (anchorBelow.HasValue) tpCandidates.Add(anchorBelow.Value);
                    if (swingLow.HasValue) tpCandidates.Add(swingLow.Value);
                    tpCandidates.Add(entryPrice - baseTP);

                    tp = tpCandidates.Where(v => v < entryPrice)
                                     .DefaultIfEmpty(entryPrice - baseTP)
                                     .Max();

                    var slCandidates = new List<double>();
                    if (vah.HasValue && vah.Value > entryPrice) slCandidates.Add(vah.Value);
                    if (poc.HasValue && poc.Value > entryPrice) slCandidates.Add(poc.Value);
                    if (anchorAbove.HasValue) slCandidates.Add(anchorAbove.Value);
                    if (swingHigh.HasValue) slCandidates.Add(swingHigh.Value);
                    slCandidates.Add(entryPrice + baseSL);

                    var slPick = slCandidates.Where(v => v > entryPrice)
                                            .DefaultIfEmpty(entryPrice + baseSL)
                                            .Min();
                    sl = slPick + atr * LevelBufferAtr;
                    // Минимальная дистанция SL по ATR
                    double minSl = entryPrice + Math.Max(atr * MinSlAtrMultiplier, atr * 0.5);
                    sl = Math.Max(sl.Value, minSl);
                }

                if (tp.HasValue) tp = NormalizePrice(tp.Value);
                if (sl.HasValue) sl = NormalizePrice(sl.Value);

                if (tradeType == TradeType.Buy)
                {
                    if (!tp.HasValue || tp.Value <= entryPrice) tp = NormalizePrice(entryPrice + baseTP);
                    if (!sl.HasValue || sl.Value >= entryPrice) sl = NormalizePrice(entryPrice - baseSL);
                }
                else
                {
                    if (!tp.HasValue || tp.Value >= entryPrice) tp = NormalizePrice(entryPrice - baseTP);
                    if (!sl.HasValue || sl.Value <= entryPrice) sl = NormalizePrice(entryPrice + baseSL);
                }

                if (tp.HasValue && IncludeCommissionInTargets && CommissionPerLotPerSide > 0 && OrderVolumeLots > 0)
                {
                    double extraPips = CommissionPipsForLots(OrderVolumeLots);
                    double extra = extraPips * Symbol.PipSize;
                    double adjustedTp = tradeType == TradeType.Buy ? tp.Value + extra : tp.Value - extra;
                    if ((tradeType == TradeType.Buy && adjustedTp > entryPrice) || (tradeType == TradeType.Sell && adjustedTp < entryPrice))
                        tp = NormalizePrice(adjustedTp);
                }

                // ������� TP � ������ ��������
                var compressed = MaybeCompressTp(tradeType, entryPrice, (tp, sl));
                return compressed;
            }
            catch
            {
                double atr = Math.Max(_atr?.Result.LastValue ?? 0, Symbol.PipSize * 2);
                if (tradeType == TradeType.Buy)
                    return (NormalizePrice(entryPrice + atr), NormalizePrice(entryPrice - atr));
                else
                    return (NormalizePrice(entryPrice - atr), NormalizePrice(entryPrice + atr));
            }
        }

        // === ���������� TP ��� �������� ===
        private bool IsRangeCompressed()
        {
            try
            {
                if (!EnableRangeAdaptiveTargets) return false;
                var m1 = MarketData.GetBars(TimeFrame.Minute);
                int need = Math.Max(1, RangeCompressionWindowMinutes);
                if (m1 == null || m1.Count < need + 1) return false;
                int start = Math.Max(0, m1.Count - need);
                double maxH = double.MinValue, minL = double.MaxValue;
                for (int i = start; i < m1.Count; i++)
                {
                    var b = m1[i];
                    if (b.High > maxH) maxH = b.High;
                    if (b.Low < minL) minL = b.Low;
                }
                double width = maxH - minL; // � �������� ���� (��� XAUUSD ? USD)
                bool compressed = width <= Math.Max(0.01, RangeCompressionWidthUSD);
                if (compressed) LogToFile(LogLevel.Debug, $"Range compressed: width={width:F2} ? {RangeCompressionWidthUSD:F2} in {need}m");
                return compressed;
            }
            catch { return false; }
        }

        private double? GetAdaptiveTpPips()
        {
            try
            {
                if (!IsRangeCompressed()) return null;
                double pips = AdaptiveTpUSD / Symbol.PipSize; // ����. 1.0$ / 0.01 = 100 ������
                return Math.Max(1.0, pips);
            }
            catch { return null; }
        }

        private (double? tp, double? sl) MaybeCompressTp(TradeType tradeType, double entryPrice, (double? tp, double? sl) t)
        {
            try
            {
                if (!EnableRangeAdaptiveTargets) return t;
                if (!t.tp.HasValue) return t;
                if (!IsRangeCompressed()) return t;
                double delta = Math.Max(0.0, AdaptiveTpUSD);
                double lim = tradeType == TradeType.Buy ? entryPrice + delta : entryPrice - delta;
                double tpNew = t.tp.Value;
                if (tradeType == TradeType.Buy) tpNew = Math.Min(tpNew, lim); else tpNew = Math.Max(tpNew, lim);
                if (Math.Abs(tpNew - t.tp.Value) > Symbol.PipSize)
                    LogToFile(LogLevel.Info, $"Adaptive TP applied: old={t.tp.Value:F2} new={tpNew:F2} (entry={entryPrice:F2})");
                return (NormalizePrice(tpNew), t.sl);
            }
            catch { return t; }
        }

        // ========== Logged trading helpers wrapping base API ==========
        public TradeResult ExecuteMarketOrder(TradeType tradeType, string symbolName, double volumeInUnits, string label = null, double? stopLossPips = null, double? takeProfitPips = null)
        {
            try
            {
                LogToFile(LogLevel.Info, $"ExecuteMarketOrder {tradeType} {symbolName} vol={volumeInUnits:F2} label={label} slPips={stopLossPips?.ToString("F1") ?? "-"} tpPips={takeProfitPips?.ToString("F1") ?? "-"}");
            }
            catch { }
            var res = base.ExecuteMarketOrder(tradeType, symbolName, volumeInUnits, label, stopLossPips, takeProfitPips);
            try
            {
                LogToFile(res.IsSuccessful ? LogLevel.Info : LogLevel.Error,
                    $"Result MarketOrder: success={res.IsSuccessful} err={res.Error?.ToString() ?? "-"} posId={res.Position?.Id.ToString() ?? "-"} entry={res.Position?.EntryPrice.ToString("F5") ?? "-"}");
            }
            catch { }
            return res;
        }

        public TradeResult PlaceLimitOrder(TradeType tradeType, string symbolName, double volumeInUnits, double targetPrice, string label = null, double? stopLossPips = null, double? takeProfitPips = null, DateTime? expiration = null)
        {
            try { LogToFile(LogLevel.Info, $"PlaceLimit {tradeType} {symbolName} vol={volumeInUnits:F2} @ {targetPrice:F5} label={label} slPips={stopLossPips?.ToString("F1") ?? "-"} tpPips={takeProfitPips?.ToString("F1") ?? "-"} exp={(expiration.HasValue ? expiration.Value.ToString("yyyy-MM-dd HH:mm") : "-")}"); } catch { }
            var res = base.PlaceLimitOrder(tradeType, symbolName, volumeInUnits, targetPrice, label, stopLossPips, takeProfitPips, expiration);
            try { LogToFile(res.IsSuccessful ? LogLevel.Debug : LogLevel.Error, $"Result PlaceLimit: success={res.IsSuccessful} err={res.Error?.ToString() ?? "-"} orderId={res.PendingOrder?.Id.ToString() ?? "-"}"); } catch { }
            return res;
        }

        public TradeResult PlaceStopOrder(TradeType tradeType, string symbolName, double volumeInUnits, double targetPrice, string label = null, double? stopLossPips = null, double? takeProfitPips = null, DateTime? expiration = null)
        {
            try { LogToFile(LogLevel.Info, $"PlaceStop {tradeType} {symbolName} vol={volumeInUnits:F2} @ {targetPrice:F5} label={label} slPips={stopLossPips?.ToString("F1") ?? "-"} tpPips={takeProfitPips?.ToString("F1") ?? "-"} exp={(expiration.HasValue ? expiration.Value.ToString("yyyy-MM-dd HH:mm") : "-")}"); } catch { }
            var res = base.PlaceStopOrder(tradeType, symbolName, volumeInUnits, targetPrice, label, stopLossPips, takeProfitPips, expiration);
            try { LogToFile(res.IsSuccessful ? LogLevel.Debug : LogLevel.Error, $"Result PlaceStop: success={res.IsSuccessful} err={res.Error?.ToString() ?? "-"} orderId={res.PendingOrder?.Id.ToString() ?? "-"}"); } catch { }
            return res;
        }

        public TradeResult ModifyPosition(Position position, double? stopLoss, double? takeProfit, ProtectionType protectionType)
        {
            try { LogToFile(LogLevel.Debug, $"ModifyPosition id={position?.Id} sl={(stopLoss.HasValue ? stopLoss.Value.ToString("F5") : "-")} tp={(takeProfit.HasValue ? takeProfit.Value.ToString("F5") : "-")} type={protectionType}"); } catch { }
            var res = base.ModifyPosition(position, stopLoss, takeProfit, protectionType);
            try { LogToFile(res.IsSuccessful ? LogLevel.Debug : LogLevel.Error, $"Result Modify: success={res.IsSuccessful} err={res.Error?.ToString() ?? "-"}"); } catch { }
            return res;
        }

        public TradeResult CancelPendingOrder(PendingOrder order)
        {
            try { LogToFile(LogLevel.Debug, $"CancelPending id={order?.Id} label={order?.Label} @ {order?.TargetPrice:F5}"); } catch { }
            var res = base.CancelPendingOrder(order);
            try { LogToFile(res.IsSuccessful ? LogLevel.Debug : LogLevel.Error, $"Result CancelPending: success={res.IsSuccessful} err={res.Error?.ToString() ?? "-"}"); } catch { }
            return res;
        }

        public TradeResult ClosePosition(Position position)
        {
            try { LogToFile(LogLevel.Info, $"ClosePosition id={position?.Id} label={position?.Label} pnl={position?.NetProfit:C}"); } catch { }
            var res = base.ClosePosition(position);
            try { LogToFile(res.IsSuccessful ? LogLevel.Info : LogLevel.Error, $"Result ClosePosition: success={res.IsSuccessful} err={res.Error?.ToString() ?? "-"}"); } catch { }
            return res;
        }

        public TradeResult ClosePosition(Position position, double volumeInUnits)
        {
            try { LogToFile(LogLevel.Info, $"ClosePositionPartial id={position?.Id} vol={volumeInUnits:F2} label={position?.Label} pnl={position?.NetProfit:C}"); } catch { }
            var res = base.ClosePosition(position, volumeInUnits);
            try { LogToFile(res.IsSuccessful ? LogLevel.Info : LogLevel.Error, $"Result ClosePartial: success={res.IsSuccessful} err={res.Error?.ToString() ?? "-"}"); } catch { }
            return res;
        }

        private double CommissionPipsForLots(double lots)
        {
            try
            {
                if (!IncludeCommissionInTargets || CommissionPerLotPerSide <= 0 || lots <= 0)
                    return 0.0;
                double totalCommission = CommissionPerLotPerSide * 2.0 * lots;
                double pipValuePerLot = Symbol.PipValue;
                if (pipValuePerLot <= 0)
                    return 0.0;
                return totalCommission / (pipValuePerLot * lots);
            }
            catch { return 0.0; }
        }

        private double NormalizePrice(double price)
        {
            return Math.Round(price / Symbol.PipSize) * Symbol.PipSize;
        }

        private double GetSessionFactor()
        {
            var baseTime = UseServerTimeForSession ? Server.Time : DateTime.UtcNow;
            baseTime = baseTime.AddHours(SessionTimezoneOffsetHours);
            int h = baseTime.Hour;
            if (h >= 7 && h <= 16) return 1.2;
            if (h <= 5 || h >= 20) return 0.85;
            return 1.0;
        }

        private double GetTrendMultiplier(TradeType tradeType)
        {
            double mult = 1.0;
            try
            {
                int score = 0;
                if (_higherTFAnalysis != null && _higherTFAnalysis.IsValid)
                {
                    if (_higherTFAnalysis.Trend == TrendDirection.Bullish) score += 1;
                    if (_higherTFAnalysis.Trend == TrendDirection.Bearish) score -= 1;
                }
                if (_lowerTFAnalysis != null && _lowerTFAnalysis.IsValid)
                {
                    if (_lowerTFAnalysis.Trend == TrendDirection.Bullish) score += 1;
                    if (_lowerTFAnalysis.Trend == TrendDirection.Bearish) score -= 1;
                }

                if (tradeType == TradeType.Buy)
                {
                    if (score >= 2) mult = 1.3;
                    else if (score == 1) mult = 1.15;
                    else if (score == -1) mult = 0.95;
                    else if (score <= -2) mult = 0.85;
                }
                else
                {
                    if (score <= -2) mult = 1.3;
                    else if (score == -1) mult = 1.15;
                    else if (score == 1) mult = 0.95;
                    else if (score >= 2) mult = 0.85;
                }
            }
            catch { }
            return mult;
        }

        private double? FindNearestAnchorAbove(double price)
        {
            try
            {
                var above = _liquidityAnchors
                    .Where(a => a.Price > price)
                    .OrderBy(a => a.Price)
                    .FirstOrDefault();
                return above != null ? above.Price : (double?)null;
            }
            catch { return null; }
        }

        private double? FindNearestAnchorBelow(double price)
        {
            try
            {
                var below = _liquidityAnchors
                    .Where(a => a.Price < price)
                    .OrderByDescending(a => a.Price)
                    .FirstOrDefault();
                return below != null ? below.Price : (double?)null;
            }
            catch { return null; }
        }

        private double? GetRecentHighAbove(double price, int lookback)
        {
            try
            {
                var bars = _analysisBars;
                if (bars == null || bars.Count < 5) return null;
                int n = Math.Min(lookback, bars.Count - 1);
                var highs = Enumerable.Range(bars.Count - n, n).Select(i => bars[i].High).Where(h => h > price);
                return highs.Any() ? highs.Min() : (double?)null;
            }
            catch { return null; }
        }

        private double? GetRecentLowBelow(double price, int lookback)
        {
            try
            {
                var bars = _analysisBars;
                if (bars == null || bars.Count < 5) return null;
                int n = Math.Min(lookback, bars.Count - 1);
                var lows = Enumerable.Range(bars.Count - n, n).Select(i => bars[i].Low).Where(l => l < price);
                return lows.Any() ? lows.Max() : (double?)null;
            }
            catch { return null; }
        }

        // -------------------- Trap Grid --------------------
        private void BuildTrapGridIfNeeded()
        {
            try
            {
                if (!UseTrapGrid)
                    return;
                LogToFile(LogLevel.Debug, "TrapGrid check");

                if ((DateTime.Now - _lastTrapRebuild).TotalSeconds < TrapRebuildSeconds)
                {
                    if (TrapUseOCO) EnforceTrapOCO();
                    return;
                }

                // Respect Hunter margin reserve for non-hunter traps
                try
                {
                    double hunterReserve = GetHunterReserveMarginCurrency();
                    if (hunterReserve > 0 && Account.FreeMargin <= hunterReserve)
                    {
                        return;
                    }
                }
                catch { }

                CancelTrapOrders();
                BuildTrapGrid();
                _lastTrapRebuild = DateTime.Now;
                LogToFile(LogLevel.Info, "TrapGrid rebuilt");

                if (TrapUseOCO) EnforceTrapOCO();
            }
            catch (Exception ex)
            {
                Print($"TrapGrid error: {ex.Message}");
            }
        }

        private void CancelTrapOrders()
        {
            foreach (var po in PendingOrders.Where(o => o.SymbolName == SymbolName && o.Label.StartsWith(TRAP_LABEL_PREFIX)))
            {
                LogToFile(LogLevel.Debug, $"CancelTrapOrders: cancel {po.Label} @ {po.TargetPrice:F5}");
                CancelPendingOrder(po);
            }
        }

        private void BuildTrapGrid()
        {
            // Собираем уровни зон: POC/VAH/VAL, ближайшие якоря, ближайшие swing-и
            var levels = new List<(string name, double price)>();
            if (_currentVolumeProfile?.POC > 0) levels.Add(("POC", _currentVolumeProfile.POC));
            if (_currentVolumeProfile?.VAH > 0) levels.Add(("VAH", _currentVolumeProfile.VAH));
            if (_currentVolumeProfile?.VAL > 0) levels.Add(("VAL", _currentVolumeProfile.VAL));

            var aAbove = FindNearestAnchorAbove(Symbol.Bid);
            var aBelow = FindNearestAnchorBelow(Symbol.Bid);
            if (aAbove.HasValue) levels.Add(("Anchor↑", aAbove.Value));
            if (aBelow.HasValue) levels.Add(("Anchor↓", aBelow.Value));

            var swingAbove = GetRecentHighAbove(Symbol.Bid, SwingLookback);
            var swingBelow = GetRecentLowBelow(Symbol.Bid, SwingLookback);
            if (swingAbove.HasValue) levels.Add(("SwingH", swingAbove.Value));
            if (swingBelow.HasValue) levels.Add(("SwingL", swingBelow.Value));

            // Сортируем по близости к цене и берем топ зон
            levels = levels
                .OrderBy(l => Math.Abs(l.price - Symbol.Bid))
                .GroupBy(l => l.name)
                .Select(g => g.First())
                .Take(Math.Max(1, MaxTrapZones))
                .ToList();

            double atr = Math.Max(_atr?.Result.LastValue ?? 0, Symbol.PipSize * 2);
            double zonePips = TrapZoneBufferPips;
            double zoneOffset = (_detectedMarket == MarketType.Crypto ? zonePips : zonePips * Symbol.PipSize);
            double atrOffset = atr * TrapAtrBuffer;
            double offset = Math.Max(Symbol.TickSize, atrOffset + zoneOffset);

            var volume = Symbol.QuantityToVolumeInUnits(OrderVolumeLots);
            int capacity = AvailablePositionSlots();
            if (capacity <= 0)
            {
                Print($"⛔ TrapGrid: пропуск — лимит позиций {MaxOpenPositions} достигнут.");
                return;
            }
            // Respect Hunter margin reserve: avoid placing new traps if free margin at/below reserve
            try
            {
                double hunterReserve = GetHunterReserveMarginCurrency();
                if (hunterReserve > 0 && Account.FreeMargin <= hunterReserve)
                {
                    Print("⏸️ TrapGrid: резерв маржи под Hunter — новые ордера не ставим");
                    return;
                }
            }
            catch { }
            int zoneIdx = 0;

            foreach (var (name, level) in levels)
            {
                string zoneId = $"Z{++zoneIdx}";

                if (EnableTrapReversion)
                {
                    // Mean-reversion: лимиты вокруг уровня
                    double buyPrice = NormalizePrice(level - offset);
                    if (UseDynamicTargets)
                    {
                        var buyTargets = ComputeDynamicTargets(TradeType.Buy, buyPrice);
                        var buyDistances = ToPipDistances(TradeType.Buy, buyPrice, buyTargets.sl, buyTargets.tp);
                        if (capacity-- <= 0) return; PlaceLimitOrder(TradeType.Buy, SymbolName, volume, buyPrice, $"{TRAP_LABEL_PREFIX}:{zoneId}:MR:Buy", buyDistances.slPips, buyDistances.tpPips);
                    }
                    else
                    {
                        double slPips = FixedStopLossPips;
                        double tpPips = FixedTakeProfitPips;
                        if (IncludeCommissionInTargets && CommissionPerLotPerSide > 0 && OrderVolumeLots > 0)
                            tpPips += CommissionPipsForLots(OrderVolumeLots);
                        if (capacity-- <= 0) return; PlaceLimitOrder(TradeType.Buy, SymbolName, volume, buyPrice, $"{TRAP_LABEL_PREFIX}:{zoneId}:MR:Buy", slPips, tpPips);
                    }

                    double sellPrice = NormalizePrice(level + offset);
                    if (UseDynamicTargets)
                    {
                        var sellTargets = ComputeDynamicTargets(TradeType.Sell, sellPrice);
                        var sellDistances = ToPipDistances(TradeType.Sell, sellPrice, sellTargets.sl, sellTargets.tp);
                        if (capacity-- <= 0) return; PlaceLimitOrder(TradeType.Sell, SymbolName, volume, sellPrice, $"{TRAP_LABEL_PREFIX}:{zoneId}:MR:Sell", sellDistances.slPips, sellDistances.tpPips);
                    }
                    else
                    {
                        double slPips = FixedStopLossPips;
                        double tpPips = FixedTakeProfitPips;
                        if (IncludeCommissionInTargets && CommissionPerLotPerSide > 0 && OrderVolumeLots > 0)
                            tpPips += CommissionPipsForLots(OrderVolumeLots);
                        if (capacity-- <= 0) return; PlaceLimitOrder(TradeType.Sell, SymbolName, volume, sellPrice, $"{TRAP_LABEL_PREFIX}:{zoneId}:MR:Sell", slPips, tpPips);
                    }
                }

                if (EnableTrapBreakout)
                {
                    // Breakout: стоп-ордера за границей зоны
                    double buyStop = NormalizePrice(level + offset);
                    if (UseDynamicTargets)
                    {
                        var buyTargets = ComputeDynamicTargets(TradeType.Buy, buyStop);
                        var buyDistances = ToPipDistances(TradeType.Buy, buyStop, buyTargets.sl, buyTargets.tp);
                        if (capacity-- <= 0) return; PlaceStopOrder(TradeType.Buy, SymbolName, volume, buyStop, $"{TRAP_LABEL_PREFIX}:{zoneId}:BO:Buy", buyDistances.slPips, buyDistances.tpPips);
                    }
                    else
                    {
                        double slPips = FixedStopLossPips;
                        double tpPips = FixedTakeProfitPips;
                        if (IncludeCommissionInTargets && CommissionPerLotPerSide > 0 && OrderVolumeLots > 0)
                            tpPips += CommissionPipsForLots(OrderVolumeLots);
                        if (capacity-- <= 0) return; PlaceStopOrder(TradeType.Buy, SymbolName, volume, buyStop, $"{TRAP_LABEL_PREFIX}:{zoneId}:BO:Buy", slPips, tpPips);
                    }

                    double sellStop = NormalizePrice(level - offset);
                    if (UseDynamicTargets)
                    {
                        var sellTargets = ComputeDynamicTargets(TradeType.Sell, sellStop);
                        var sellDistances = ToPipDistances(TradeType.Sell, sellStop, sellTargets.sl, sellTargets.tp);
                        if (capacity-- <= 0) return; PlaceStopOrder(TradeType.Sell, SymbolName, volume, sellStop, $"{TRAP_LABEL_PREFIX}:{zoneId}:BO:Sell", sellDistances.slPips, sellDistances.tpPips);
                    }
                    else
                    {
                        double slPips = FixedStopLossPips;
                        double tpPips = FixedTakeProfitPips;
                        if (IncludeCommissionInTargets && CommissionPerLotPerSide > 0 && OrderVolumeLots > 0)
                            tpPips += CommissionPipsForLots(OrderVolumeLots);
                        if (capacity-- <= 0) return; PlaceStopOrder(TradeType.Sell, SymbolName, volume, sellStop, $"{TRAP_LABEL_PREFIX}:{zoneId}:BO:Sell", slPips, tpPips);
                    }
                }
            }
        }

        private void EnforceTrapOCO()
        {
            try
            {
                // Если по зоне открыта позиция Trap, отменим остальные pending этой зоны
                var trapPositions = Positions.Where(p => p.SymbolName == SymbolName && p.Label.StartsWith(TRAP_LABEL_PREFIX + ":"));
                foreach (var pos in trapPositions)
                {
                    var parts = pos.Label.Split(':');
                    if (parts.Length < 2) continue;
                    var zoneId = parts[1];
                    foreach (var po in PendingOrders.Where(o => o.SymbolName == SymbolName && o.Label.StartsWith($"{TRAP_LABEL_PREFIX}:{zoneId}:")))
                        CancelPendingOrder(po);
                }
            }
            catch { }
        }

        protected override void OnStart()
        {
            InitLogger();
            LogToFile(LogLevel.Info, "OnStart invoked");

            InitializeComponents();
            LogToFile(LogLevel.Debug, "Components initialized");

            InitializeTimeFrames();
            LogToFile(LogLevel.Debug, $"TimeFrames: sel={_selectedTimeFrame}, HTF={_higherTimeFrame}, LTF={_lowerTimeFrame}");

            InitializeAnalysisStructures();
            LogToFile(LogLevel.Debug, "Analysis structures initialized");

            if (EnableParallelTeam)
                InitializeTeam();

            // Определяем тип рынка и настройки
            _detectedMarket = AutoDetectMarket ? DetectMarketType() : ManualMarketType;
            _currentSettings = GetMarketSettings(_detectedMarket);
            LogToFile(LogLevel.Info, $"Market detected: {_detectedMarket}; Settings: orders={_currentSettings.OrdersCount}, step={_currentSettings.StepPips}, tp={_currentSettings.TakeProfitPips}");

            if (ApplyPresetOnStart)
            {
                ApplyAggressionPreset(AggressionProfile);
                _currentSettings = GetMarketSettings(_detectedMarket);
            }

            PrintStartupInfo();
            LogToFile(LogLevel.Info, $"Startup: symbol={SymbolName} market={_currentSettings.MarketName} timeframe={_selectedTimeFrame}");

            Timer.Start(TimeSpan.FromSeconds(1));
            LogToFile(LogLevel.Debug, "Timer started (1s)");

            // Subscribe to position closed event (API without override)
            try { Positions.Closed += OnPositionsClosed; } catch { }
        }

        private void InitializeComponents()
        {
            _marketDepth = MarketData.GetMarketDepth(SymbolName);
            _atr = Indicators.AverageTrueRange(14, MovingAverageType.Simple);
        }

        private void InitializeTimeFrames()
        {
            _selectedTimeFrame = GetTimeFrameFromIndex(AnalysisTimeFrameIndex);
            _higherTimeFrame = GetHigherTimeFrameFromIndex(HigherTimeFrameIndex);
            _lowerTimeFrame = GetLowerTimeFrameFromIndex(LowerTimeFrameIndex);

            _analysisBars = MarketData.GetBars(_selectedTimeFrame);
            _higherTFBars = MarketData.GetBars(_higherTimeFrame);
            _lowerTFBars = MarketData.GetBars(_lowerTimeFrame);
            _htfRevBars = MarketData.GetBars(GetReversalTimeFrameFromIndex(HtfReversalIndex));
        }

        private TimeFrame GetReversalTimeFrameFromIndex(int index)
        {
            switch (index)
            {
                case 0: return TimeFrame.Minute;   // M1
                case 1: return TimeFrame.Minute5;  // M5
                default: return TimeFrame.Minute5;
            }
        }

        private void InitializeAnalysisStructures()
        {
            _trackedClusters = new Dictionary<double, ClusterData>();
            _liquidityAnchors = new List<LiquidityAnchor>();
            _currentVolumeProfile = new VolumeProfile();
            _deltaHistory = new List<double>();
            _volumeProfile = new Dictionary<DateTime, double>();
            _imbalanceHistory = new List<Tuple<DateTime, double>>();

            _higherTFAnalysis = new TimeFrameAnalysis
            {
                Name = $"HTF ({_higherTimeFrame})",
                IsValid = false
            };

            _lowerTFAnalysis = new TimeFrameAnalysis
            {
                Name = $"LTF ({_lowerTimeFrame})",
                IsValid = false
            };
        }

        private void InitializeTeam()
        {
            _farmer = new TeamSpecialist
            {
                Role = TeamRole.Farmer,
                Status = SpecialistStatus.Active,
                MaxAllowedExposure = _currentSettings?.OrdersCount * 2 * OrderVolumeLots ?? 0.2,
                StatusMessage = "Готов к работе"
            };
            _hunter = new TeamSpecialist
            {
                Role = TeamRole.Hunter,
                Status = SpecialistStatus.Waiting,
                MaxAllowedExposure = 0.5,
                StatusMessage = "В засаде"
            };
            _scalper = new TeamSpecialist
            {
                Role = TeamRole.Scalper,
                Status = SpecialistStatus.Waiting,
                MaxAllowedExposure = 0.2,
                StatusMessage = "Сканирование"
            };
            Print("👨‍✈️ Команда специалистов инициализирована!");
        }

        private TimeFrame GetTimeFrameFromIndex(int index)
        {
            switch (index)
            {
                case 0: return TimeFrame.Minute;
                case 1: return TimeFrame.Minute3;
                case 2: return TimeFrame.Minute5;
                default: return TimeFrame.Minute;
            }
        }

        private TimeFrame GetHigherTimeFrameFromIndex(int index)
        {
            switch (index)
            {
                case 0: return TimeFrame.Minute5;
                case 1: return TimeFrame.Minute15;
                case 2: return TimeFrame.Hour;
                default: return TimeFrame.Minute15;
            }
        }

        private TimeFrame GetLowerTimeFrameFromIndex(int index)
        {
            switch (index)
            {
                case 0: return TimeFrame.Minute;
                case 1: return TimeFrame.Minute3;
                case 2: return TimeFrame.Minute5;
                default: return TimeFrame.Minute;
            }
        }

        private void PrintStartupInfo()
        {
            Print($"====== OLYMPIAN QUANTUM TRADER v3.0 ======");
            Print($"🎯 Символ: {SymbolName}");
            Print($"📊 Обнаружен рынок: {_currentSettings.MarketName}");
            Print($"⚙️ Настройки: {_currentSettings.OrdersCount} ордеров, шаг {_currentSettings.StepPips} пипсов");
            Print($"📈 Таймфрейм анализа: {_selectedTimeFrame}");
            Print($"🔥 Кластерный анализ: {(EnableClusterAnalysis ? "ВКЛЮЧЕН" : "ВЫКЛЮЧЕН")}");
            Print($"🧠 Мульти-ТФ анализ: {(EnableMultiTimeFrameAnalysis ? "ВКЛЮЧЕН" : "ВЫКЛЮЧЕН")}");
            Print($"⚡ Скальпинг конфликтов: {(EnableConflictScalping ? "ВКЛЮЧЕН" : "ВЫКЛЮЧЕН")}");
            Print($"👥 Параллельная команда: {(EnableParallelTeam ? "ВКЛЮЧЕНА" : "ВЫКЛЮЧЕНА")}");
            Print("==============================");
        }

        protected override void OnTimer()
        {
            try
            {
                LogToFile(LogLevel.Debug, "OnTimer tick");
                if (EnableParallelTeam)
                {
                    ParallelTeamLoop();
                }
                else
                {
                    MainQuantumLoop();
                }
                // Auto-close check
                CheckNetProfitAndCloseIfNeeded();
            }
            catch (Exception ex)
            {
                Print($"Критическая ошибка в главном цикле: {ex.Message}");
            }
        }

        private void ParallelTeamLoop()
        {
            // 1. Капитан анализирует общую обстановку
            AnalyzeGlobalMarketState();

            // 2. Проверка глобальных рисков
            if (CheckGlobalRiskLimits())
                return;
            // 3. Параллельная работа команды
            ExecuteFarmerLogic();
            ExecuteHunterLogic();
            ExecuteScalperLogic();
            // 4. Обновление дашборда
            if (EnableDashboard)
                DrawTeamDashboard();
        }

        private void AnalyzeGlobalMarketState()
        {
            // Полный кластерный анализ
            if (EnableClusterAnalysis)
                PerformClusterAnalysis();
            // Мульти-таймфрейм анализ
            if (EnableMultiTimeFrameAnalysis)
                PerformMultiTimeFrameAnalysis();
            // Анализ состояния рынка Level 2
            AnalyzeMarketDepthState();
            // Обновляем общую экспозицию команды
            UpdateTeamExposure();
        }

        private void UpdateTeamExposure()
        {
            var allPositions = Positions.Where(p => p.SymbolName == SymbolName);
            var totalBuyLots = allPositions.Where(p => p.TradeType == TradeType.Buy).Sum(p => Symbol.VolumeInUnitsToQuantity(p.VolumeInUnits));
            var totalSellLots = allPositions.Where(p => p.TradeType == TradeType.Sell).Sum(p => Symbol.VolumeInUnitsToQuantity(p.VolumeInUnits));

            _teamTotalExposure = Math.Abs(totalBuyLots - totalSellLots);
            // Обновляем экспозиции специалистов
            if (_farmer != null)
            {
                var farmerPositions = allPositions.Where(p => p.Label == "Farmer");
                _farmer.CurrentExposure = farmerPositions.Sum(p => Symbol.VolumeInUnitsToQuantity(p.VolumeInUnits));
            }
            if (_hunter != null)
            {
                var hunterPositions = allPositions.Where(p => p.Label == "Hunter");
                _hunter.CurrentExposure = hunterPositions.Sum(p => Symbol.VolumeInUnitsToQuantity(p.VolumeInUnits));
            }
            if (_scalper != null)
            {
                var scalperPositions = allPositions.Where(p => p.Label == "Scalper");
                _scalper.CurrentExposure = scalperPositions.Sum(p => Symbol.VolumeInUnitsToQuantity(p.VolumeInUnits));
            }
        }

        private CaptainCommand EvaluateTeamRequest(TeamRole requester, double requestedSize, TradeType tradeType)
        {
            var projectedExposure = _teamTotalExposure + Math.Abs(requestedSize);

            if (projectedExposure > MaxTotalExposureLots)
            {
                if (projectedExposure > MaxTotalExposureLots * 1.2)
                {
                    Print($"👨‍✈️ Капитан: БЛОКИРУЮ {requester} - превышение лимита на {(projectedExposure - MaxTotalExposureLots):F2}");
                    return CaptainCommand.Block;
                }
                else
                {
                    Print($"👨‍✈️ Капитан: {requester} уменьшите размер");
                    return CaptainCommand.ReduceSize;
                }
            }
            return CaptainCommand.Proceed;
        }

        private void ExecuteFarmerLogic()
        {
            if (_farmer.Status == SpecialistStatus.Blocked) return;
            // Keep margin reserve for Hunter: skip farmer actions if free margin at/below reserve
            try
            {
                double hunterReserve = GetHunterReserveMarginCurrency();
                if (hunterReserve > 0 && Account.FreeMargin <= hunterReserve)
                {
                    _farmer.StatusMessage = "Пауза фермера: резерв маржи под Hunter";
                    return;
                }
            }
            catch { }

        if (UseTrapGrid)
        {
            _farmer.StatusMessage = "Trap Grid активен";
            BuildTrapGridIfNeeded();
            return;
        }

        if (UseBatchOrderPlacement)
        {
            MaintainFarmerBatch();
            return;
        }

            var farmerOrders = PendingOrders.Where(o => o.Label == "Farmer" && o.SymbolName == SymbolName);

            if (farmerOrders.Count() != _currentSettings.OrdersCount * 2 ||
                (DateTime.Now - _lastGridRebuild).TotalMinutes > 10)
            {
                var command = EvaluateTeamRequest(TeamRole.Farmer, _currentSettings.OrdersCount * OrderVolumeLots, TradeType.Buy);

                if (command == CaptainCommand.Proceed)
                {
                    RebuildFarmerGrid();
                    _farmer.StatusMessage = "Сетка активна";
                    _farmer.LastAction = DateTime.Now;
                }
                else
                {
                    _farmer.StatusMessage = "Ожидание разрешения капитана";
                }
            }
            else
            {
                _farmer.StatusMessage = $"Сетка работает: {farmerOrders.Count()} ордеров";
            }
        }

        private void ExecuteHunterLogic()
        {
            if (_hunter.Status == SpecialistStatus.Blocked) return;
            _hunterPosition = Positions.FirstOrDefault(p => p.Label == "Hunter" && p.SymbolName == SymbolName);
            if (_hunterPosition != null)
            {
                ManageHunterPosition();
                _hunter.StatusMessage = $"Позиция: {_hunterPosition.NetProfit:C}";
            }
            else
            {
                ScanForHunterTargets();
            }
        }

        private void ScanForHunterTargets()
        {
            if (Math.Abs(_orderBookImbalance) >= GetEffectiveHunterThreshold())
            {
                var tradeType = _orderBookImbalance > 0 ? TradeType.Sell : TradeType.Buy;
                var command = EvaluateTeamRequest(TeamRole.Hunter, OrderVolumeLots, tradeType);

                if (command == CaptainCommand.Proceed)
                {
                    ExecuteHunterShot(tradeType);
                    _hunter.StatusMessage = "Снайперский выстрел!";
                }
                else
                {
                    _hunter.StatusMessage = $"Цель найдена, ожидание разрешения";
                }
            }
            else
            {
                _hunter.StatusMessage = $"В засаде (дисбаланс: {_orderBookImbalance:F1}%)";
            }
        }

        private void ExecuteHunterShot(TradeType tradeType)
        {
            var volume = Symbol.QuantityToVolumeInUnits(OrderVolumeLots);
            if (AbortIfPositionCapReached("HunterShot"))
                return;
            if (UseDynamicTargets)
            {
                double entryApprox = tradeType == TradeType.Buy ? Symbol.Ask : Symbol.Bid;
                var targets = ComputeDynamicTargets(tradeType, entryApprox);
                var distances = ToPipDistances(tradeType, entryApprox, targets.sl, targets.tp);
                var result = ExecuteMarketOrder(tradeType, SymbolName, volume, "Hunter", distances.slPips, distances.tpPips);

                if (result.IsSuccessful)
                {
                    _hunterPosition = result.Position;
                    try
                    {
                        var refined = ComputeDynamicTargets(tradeType, _hunterPosition.EntryPrice);
                        ModifyPosition(_hunterPosition, refined.sl, refined.tp, ProtectionType.Absolute);
                    }
                    catch { }
                    _hunter.ActionsToday++;
                    _hunter.LastAction = DateTime.Now;
                    Print($"🎯 Охотник: Снайперский выстрел {tradeType}! Дисбаланс: {_orderBookImbalance:F1}%");
                }
            }
            else
            {
                var stopLossDistance = _atr.Result.LastValue * HunterAtrMultiplier;
                var stopLossPips = Math.Abs(stopLossDistance / Symbol.PipSize);
                if (AbortIfPositionCapReached("HunterShot"))
                    return;
                var result = ExecuteMarketOrder(tradeType, SymbolName, volume, "Hunter", stopLossPips, null);
                if (result.IsSuccessful)
                {
                    _hunterPosition = result.Position;
                    _hunter.ActionsToday++;
                    _hunter.LastAction = DateTime.Now;
                    Print($"🎯 Охотник: Снайперский выстрел {tradeType}! Дисбаланс: {_orderBookImbalance:F1}%");
                }
            }
        }

        private void ExecuteScalperLogic()
        {
            if (_scalper.Status == SpecialistStatus.Blocked) return;
            if (_scalpingTrapActive)
            {
                ManageScalpingTrap();
            }
            else
            {
                bool acted = false;
                // HTF Reversal priority if enabled
                if (EnableHtfReversalScalper)
                {
                    acted = TryHtfReversalScalp();
                }
                // Fallback to conflict scalper
                if (!acted && EnableConflictScalping)
                {
                    ScanForConflicts();
                }
            }
        }

        private void RebuildFarmerGrid()
        {
            // Отменяем старые ордера фермера
            foreach (var order in PendingOrders.Where(o => o.Label == "Farmer" && o.SymbolName == SymbolName))
            {
                CancelPendingOrder(order);
            }
            var volume = Symbol.QuantityToVolumeInUnits(OrderVolumeLots);
            int capacity = AvailablePositionSlots();
            if (capacity <= 0)
            {
                Print($"⛔ Фермер: пропуск перестройки сетки — лимит позиций {MaxOpenPositions} достигнут.");
                return;
            }
            int placed = 0;
            var stepSize = _detectedMarket == MarketType.Crypto ?
                _currentSettings.StepPips :
                _currentSettings.StepPips * Symbol.PipSize;

            // Динамический TP/SL рассчитываем от цены каждого ордера
            // Размещаем ордера на покупку
            for (int i = 1; i <= _currentSettings.OrdersCount; i++)
            {
                if (placed >= capacity) break;
                double price = Symbol.Bid - i * stepSize;
                if (UseDynamicTargets)
                {
                    var targets = ComputeDynamicTargets(TradeType.Buy, price);
                    var distances = ToPipDistances(TradeType.Buy, price, targets.sl, targets.tp);
                    PlaceLimitOrder(TradeType.Buy, SymbolName, volume, price, "Farmer", distances.slPips, distances.tpPips);
                }
                else
                {
                    double tpPips = FixedTakeProfitPips;
                    var adapt = GetAdaptiveTpPips();
                    if (adapt.HasValue) tpPips = Math.Min(tpPips, adapt.Value);
                    if (IncludeCommissionInTargets && CommissionPerLotPerSide > 0 && OrderVolumeLots > 0)
                        tpPips += CommissionPipsForLots(OrderVolumeLots);
                    double slPips = FixedStopLossPips;
                    PlaceLimitOrder(TradeType.Buy, SymbolName, volume, price, "Farmer", slPips, tpPips);
                }
                placed++;
            }

            // Размещаем ордера на продажу
            for (int i = 1; i <= _currentSettings.OrdersCount; i++)
            {
                if (placed >= capacity) break;
                double price = Symbol.Ask + i * stepSize;
                if (UseDynamicTargets)
                {
                    var targets = ComputeDynamicTargets(TradeType.Sell, price);
                    var distances = ToPipDistances(TradeType.Sell, price, targets.sl, targets.tp);
                    PlaceLimitOrder(TradeType.Sell, SymbolName, volume, price, "Farmer", distances.slPips, distances.tpPips);
                }
                else
                {
                    double tpPips = FixedTakeProfitPips;
                    var adapt = GetAdaptiveTpPips();
                    if (adapt.HasValue) tpPips = Math.Min(tpPips, adapt.Value);
                    if (IncludeCommissionInTargets && CommissionPerLotPerSide > 0 && OrderVolumeLots > 0)
                        tpPips += CommissionPipsForLots(OrderVolumeLots);
                    double slPips = FixedStopLossPips;
                    PlaceLimitOrder(TradeType.Sell, SymbolName, volume, price, "Farmer", slPips, tpPips);
                }
                placed++;
            }
            _lastGridRebuild = DateTime.Now;
            Print($"🌾 Фермер: Сетка перестроена ({_currentSettings.OrdersCount * 2} ордеров)");
        }

        private void MainQuantumLoop()
        {
            // Полный кластерный анализ
            if (EnableClusterAnalysis)
                PerformClusterAnalysis();
            // Мульти-таймфрейм анализ
            if (EnableMultiTimeFrameAnalysis)
                PerformMultiTimeFrameAnalysis();
            // Анализ состояния рынка Level 2
            AnalyzeMarketDepthState();
            // Проверка глобальных рисков
            if (CheckGlobalRiskLimits())
                return;
            // Обнаружение и обработка конфликтов
            if (EnableConflictScalping)
                DetectAndHandleConflicts();
            // Trap Grid (одиночный режим)
            if (UseTrapGrid)
                BuildTrapGridIfNeeded();
            // Управление позицией охотника
            _hunterPosition = Positions.FirstOrDefault(p => p.Label == "Hunter" && p.SymbolName == SymbolName);
            if (_hunterPosition != null)
            {
                ManageHunterPosition();
                return;
            }
            // Принятие решения и выполнение стратегии
            DecideAndExecuteQuantumStrategy();
            // Обновление информационной панели
            if (EnableDashboard)
                DrawQuantumDashboard();
        }

        private void PerformClusterAnalysis()
        {
            try
            {
                var bidEntries = _marketDepth.BidEntries.Take(10).ToList();
                var askEntries = _marketDepth.AskEntries.Take(10).ToList();
                AnalyzeLiquidityAnchors(bidEntries, askEntries);
                DetectClusterPatterns(bidEntries, askEntries);
                UpdateVolumeProfile();
                CalculateCumulativeDelta();
                if (TrackIcebergOrders)
                    DetectIcebergOrders(bidEntries, askEntries);
            }
            catch (Exception ex)
            {
                Print($"Ошибка в кластерном анализе: {ex.Message}");
            }
        }

        private void AnalyzeLiquidityAnchors(List<MarketDepthEntry> bidEntries, List<MarketDepthEntry> askEntries)
        {
            _liquidityAnchors.Clear();
            foreach (var bid in bidEntries.Where(b => b.VolumeInUnits >= MinAnchorVolume))
            {
                var anchor = new LiquidityAnchor
                {
                    Price = bid.Price,
                    Volume = bid.VolumeInUnits,
                    Side = TradeType.Buy,
                    Imbalance = 0,
                    IsStrong = bid.VolumeInUnits >= MinAnchorVolume * 2,
                    LastUpdate = DateTime.Now,
                    Behavior = DetermineLiquidityBehavior(bid.Price, bid.VolumeInUnits, TradeType.Buy),
                    RefreshCount = 1,
                    InitialVolume = bid.VolumeInUnits
                };
                _liquidityAnchors.Add(anchor);
            }
            foreach (var ask in askEntries.Where(a => a.VolumeInUnits >= MinAnchorVolume))
            {
                var anchor = new LiquidityAnchor
                {
                    Price = ask.Price,
                    Volume = ask.VolumeInUnits,
                    Side = TradeType.Sell,
                    Imbalance = 0,
                    IsStrong = ask.VolumeInUnits >= MinAnchorVolume * 2,
                    LastUpdate = DateTime.Now,
                    Behavior = DetermineLiquidityBehavior(ask.Price, ask.VolumeInUnits, TradeType.Sell),
                    RefreshCount = 1,
                    InitialVolume = ask.VolumeInUnits
                };
                _liquidityAnchors.Add(anchor);
            }
            _liquidityAnchors = _liquidityAnchors.OrderByDescending(a => a.Volume).ToList();
        }

        private LiquidityBehavior DetermineLiquidityBehavior(double price, double volume, TradeType side)
        {
            var existingAnchor = _liquidityAnchors.FirstOrDefault(a =>
                Math.Abs(a.Price - price) < Symbol.PipSize && a.Side == side);
            if (existingAnchor != null)
            {
                if (volume > existingAnchor.Volume * 1.2)
                    return LiquidityBehavior.Building;
                else if (volume < existingAnchor.Volume * 0.8)
                    return LiquidityBehavior.Weakening;
                else if (existingAnchor.RefreshCount > 5)
                    return LiquidityBehavior.Iceberg;
            }
            return LiquidityBehavior.Building;
        }

        private void DetectClusterPatterns(List<MarketDepthEntry> bidEntries, List<MarketDepthEntry> askEntries)
        {
            var currentTime = DateTime.Now;

            foreach (var bid in bidEntries)
            {
                var clusterKey = Math.Round(bid.Price / Symbol.PipSize) * Symbol.PipSize;

                if (_trackedClusters.ContainsKey(clusterKey))
                {
                    var cluster = _trackedClusters[clusterKey];
                    cluster.BuyVolume += bid.VolumeInUnits;
                    cluster.Delta = cluster.BuyVolume - cluster.SellVolume;
                    cluster.ImbalanceRatio = cluster.BuyVolume / (cluster.BuyVolume + cluster.SellVolume);
                    cluster.Timestamp = currentTime;
                }
                else
                {
                    _trackedClusters[clusterKey] = new ClusterData
                    {
                        Price = clusterKey,
                        BuyVolume = bid.VolumeInUnits,
                        SellVolume = 0,
                        Delta = bid.VolumeInUnits,
                        Timestamp = currentTime,
                        IsAggressive = bid.VolumeInUnits > bidEntries.Average(b => b.VolumeInUnits) * (1 + AggressiveVolumeThreshold / 100.0),
                        Behavior = LiquidityBehavior.Building,
                        ImbalanceRatio = 1.0
                    };
                }
            }
            foreach (var ask in askEntries)
            {
                var clusterKey = Math.Round(ask.Price / Symbol.PipSize) * Symbol.PipSize;

                if (_trackedClusters.ContainsKey(clusterKey))
                {
                    var cluster = _trackedClusters[clusterKey];
                    cluster.SellVolume += ask.VolumeInUnits;
                    cluster.Delta = cluster.BuyVolume - cluster.SellVolume;
                    cluster.ImbalanceRatio = cluster.BuyVolume / (cluster.BuyVolume + cluster.SellVolume);
                    cluster.Timestamp = currentTime;
                }
                else
                {
                    _trackedClusters[clusterKey] = new ClusterData
                    {
                        Price = clusterKey,
                        BuyVolume = 0,
                        SellVolume = ask.VolumeInUnits,
                        Delta = -ask.VolumeInUnits,
                        Timestamp = currentTime,
                        IsAggressive = ask.VolumeInUnits > askEntries.Average(a => a.VolumeInUnits) * (1 + AggressiveVolumeThreshold / 100.0),
                        Behavior = LiquidityBehavior.Building,
                        ImbalanceRatio = 0.0
                    };
                }
            }
            var expiredClusters = _trackedClusters.Where(kvp =>
                (currentTime - kvp.Value.Timestamp).TotalMinutes > 1).ToList();

            foreach (var expired in expiredClusters)
            {
                _trackedClusters.Remove(expired.Key);
            }
        }

        private void DetectIcebergOrders(List<MarketDepthEntry> bidEntries, List<MarketDepthEntry> askEntries)
        {
            foreach (var anchor in _liquidityAnchors)
            {
                var matchingEntry = anchor.Side == TradeType.Buy ?
                    bidEntries.FirstOrDefault(b => Math.Abs(b.Price - anchor.Price) < Symbol.PipSize) :
                    askEntries.FirstOrDefault(a => Math.Abs(a.Price - anchor.Price) < Symbol.PipSize);
                if (matchingEntry != null)
                {
                    if (Math.Abs(matchingEntry.VolumeInUnits - anchor.InitialVolume) < anchor.InitialVolume * 0.1 &&
                        anchor.RefreshCount > 3)
                    {
                        anchor.Behavior = LiquidityBehavior.Iceberg;
                    }
                    anchor.RefreshCount++;
                }
            }
        }

        private void UpdateVolumeProfile()
        {
            var currentBar = _analysisBars.LastBar;
            var priceLevel = Math.Round(currentBar.Close / Symbol.PipSize) * Symbol.PipSize;
            var volume = currentBar.TickVolume;
            if (_currentVolumeProfile.VolumeAtPrice.ContainsKey(priceLevel))
            {
                _currentVolumeProfile.VolumeAtPrice[priceLevel] += volume;
            }
            else
            {
                _currentVolumeProfile.VolumeAtPrice[priceLevel] = volume;
            }
            if (_currentVolumeProfile.VolumeAtPrice.Any())
            {
                var maxVolumeEntry = _currentVolumeProfile.VolumeAtPrice.OrderByDescending(kvp => kvp.Value).First();
                _currentVolumeProfile.POC = maxVolumeEntry.Key;
                var totalVolume = _currentVolumeProfile.VolumeAtPrice.Sum(kvp => kvp.Value);
                var valueAreaVolume = totalVolume * 0.7;

                var sortedByVolume = _currentVolumeProfile.VolumeAtPrice.OrderByDescending(kvp => kvp.Value).ToList();
                double accumulatedVolume = 0;
                var valueAreaPrices = new List<double>();
                foreach (var entry in sortedByVolume)
                {
                    accumulatedVolume += entry.Value;
                    valueAreaPrices.Add(entry.Key);

                    if (accumulatedVolume >= valueAreaVolume)
                        break;
                }
                if (valueAreaPrices.Any())
                {
                    _currentVolumeProfile.VAH = valueAreaPrices.Max();
                    _currentVolumeProfile.VAL = valueAreaPrices.Min();
                }
            }
        }

        private void CalculateCumulativeDelta()
        {
            var bidVolume = _marketDepth.BidEntries.Take(5).Sum(b => b.VolumeInUnits);
            var askVolume = _marketDepth.AskEntries.Take(5).Sum(a => a.VolumeInUnits);
            var currentDelta = bidVolume - askVolume;

            _cumulativeDelta += currentDelta;
            _deltaHistory.Add(currentDelta);
            if (_deltaHistory.Count > 100)
            {
                _deltaHistory.RemoveAt(0);
            }
        }

        private void PerformMultiTimeFrameAnalysis()
        {
            try
            {
                AnalyzeTimeFrame(_higherTFBars, _higherTFAnalysis);
                AnalyzeTimeFrame(_lowerTFBars, _lowerTFAnalysis);

                _detectedConflict = DetermineTimeFrameConflict();
            }
            catch (Exception ex)
            {
                Print($"Ошибка в мульти-ТФ анализе: {ex.Message}");
            }
        }

        private void AnalyzeTimeFrame(Bars bars, TimeFrameAnalysis analysis)
        {
            if (bars.Count < 20)
            {
                analysis.IsValid = false;
                return;
            }
            var recentBars = bars.TakeLast(20).ToList();
            var closePrices = recentBars.Select(b => b.Close).ToList();
            var smaShort = closePrices.TakeLast(5).Average();
            var smaLong = closePrices.Average();
            if (smaShort > smaLong * 1.001)
                analysis.Trend = TrendDirection.Bullish;
            else if (smaShort < smaLong * 0.999)
                analysis.Trend = TrendDirection.Bearish;
            else
                analysis.Trend = TrendDirection.Sideways;
            var currentPrice = bars.LastBar.Close;
            if (currentPrice > _currentVolumeProfile.VAH)
                analysis.PricePosition = PricePosition.AboveVA;
            else if (currentPrice < _currentVolumeProfile.VAL)
                analysis.PricePosition = PricePosition.BelowVA;
            else if (Math.Abs(currentPrice - _currentVolumeProfile.POC) < Symbol.PipSize * 2)
                analysis.PricePosition = PricePosition.AtPOC;
            else
                analysis.PricePosition = PricePosition.InVA;
            analysis.LastUpdate = DateTime.Now;
            analysis.IsValid = true;
        }

        private ConflictType DetermineTimeFrameConflict()
        {
            if (!_higherTFAnalysis.IsValid || !_lowerTFAnalysis.IsValid)
                return ConflictType.None;
            if (_higherTFAnalysis.Trend == TrendDirection.Bullish && _lowerTFAnalysis.Trend == TrendDirection.Bearish)
                return ConflictType.BearishDivergence;

            if (_higherTFAnalysis.Trend == TrendDirection.Bearish && _lowerTFAnalysis.Trend == TrendDirection.Bullish)
                return ConflictType.BullishDivergence;
            if (_higherTFAnalysis.PricePosition != _lowerTFAnalysis.PricePosition)
                return ConflictType.TimeFrameConflict;
            if (_deltaHistory.Count >= 10)
            {
                var recentDelta = _deltaHistory.TakeLast(5).Average();
                var olderDelta = _deltaHistory.Skip(_deltaHistory.Count - 10).Take(5).Average();

                if (Math.Sign(recentDelta) != Math.Sign(olderDelta) && Math.Abs(recentDelta - olderDelta) > Math.Abs(olderDelta) * 0.5)
                    return ConflictType.VolumeConflict;
            }
            return ConflictType.None;
        }

        private void DetectAndHandleConflicts()
        {
            if (_detectedConflict != ConflictType.None && !_scalpingTrapActive)
            {
                InitializeScalpingTrap();
            }
            else if (_scalpingTrapActive)
            {
                ManageScalpingTrap();
            }
        }

        private void ScanForConflicts()
        {
            _detectedConflict = DetermineTimeFrameConflict();

            if (_detectedConflict != ConflictType.None)
            {
                _scalper.StatusMessage = $"Конфликт обнаружен: {_detectedConflict}";
                InitializeScalpingTrap();
            }
            else
            {
                _scalper.StatusMessage = "Сканирование конфликтов";
            }
        }

        private void InitializeScalpingTrap()
        {
            _scalpingTrapActive = true;
            _scalpingTrapTime = DateTime.Now;
            _scalpingTrapCenter = Symbol.Bid;

            if (_scalper != null)
            {
                _scalper.Status = SpecialistStatus.Active;
                _scalper.StatusMessage = $"Ловушка активирована: {_detectedConflict}";
            }

            Print($"⚡ Активирована ловушка скальпинга! Конфликт: {_detectedConflict}");
        }

        private void ManageScalpingTrap()
        {
            var trapDuration = (DateTime.Now - _scalpingTrapTime).TotalSeconds;

            if (trapDuration > MaxTrapHoldSeconds)
            {
                DeactivateScalpingTrap();
                return;
            }
            if (trapDuration >= MinTrapHoldSeconds)
            {
                CheckForScalpEntry();
            }
            if (_scalper != null)
            {
                _scalper.StatusMessage = $"Ловушка активна: {trapDuration:F0}с";
            }
        }

        private void CheckForScalpEntry()
        {
            var priceDeviation = Math.Abs(Symbol.Bid - _scalpingTrapCenter);
            var atrValue = _atr.Result.LastValue;
            if (priceDeviation > atrValue * 0.5 && priceDeviation < atrValue * 1.5)
            {
                ExecuteConflictScalping();
            }
        }

        private void ExecuteConflictScalping()
        {
            var tradeType = Symbol.Bid > _scalpingTrapCenter ? TradeType.Sell : TradeType.Buy;

            if (EnableParallelTeam)
            {
                var command = EvaluateTeamRequest(TeamRole.Scalper, OrderVolumeLots * 0.5, tradeType);
                if (command != CaptainCommand.Proceed)
                {
                    if (_scalper != null)
                        _scalper.StatusMessage = "Ожидание разрешения капитана";
                    return;
                }
            }

            var volume = Symbol.QuantityToVolumeInUnits(OrderVolumeLots * 0.5);
            // Ensure we don't eat into Hunter's reserved margin
            try
            {
                double est = EstimatedMarginForLots(tradeType, OrderVolumeLots * 0.5);
                if (!HasMarginAfterReserve(est))
                {
                    if (_scalper != null)
                        _scalper.StatusMessage = "Пауза скальпера: резерв маржи под Hunter";
                    return;
                }
            }
            catch { }

            if (AbortIfPositionCapReached("Scalper"))
                return;
            double entryApprox = tradeType == TradeType.Buy ? Symbol.Ask : Symbol.Bid;
            var stopLoss = tradeType == TradeType.Buy ?
                entryApprox - _atr.Result.LastValue :
                entryApprox + _atr.Result.LastValue;
            var takeProfit = tradeType == TradeType.Buy ?
                entryApprox + _atr.Result.LastValue * 0.5 :
                entryApprox - _atr.Result.LastValue * 0.5;
            // ������ TP ��� ��������
            try
            {
                if (EnableRangeAdaptiveTargets && IsRangeCompressed())
                {
                    double delta = Math.Max(0.0, AdaptiveTpUSD);
                    var lim = tradeType == TradeType.Buy ? entryApprox + delta : entryApprox - delta;
                    if (tradeType == TradeType.Buy) takeProfit = Math.Min(takeProfit, lim); else takeProfit = Math.Max(takeProfit, lim);
                }
            }
            catch { }

            var distances = ToPipDistances(tradeType, entryApprox, stopLoss, takeProfit);
            var result = ExecuteMarketOrder(tradeType, SymbolName, volume, "Scalper", distances.slPips, distances.tpPips);

            if (result.IsSuccessful)
            {
                DeactivateScalpingTrap();

                if (_scalper != null)
                {
                    _scalper.ActionsToday++;
                    _scalper.LastAction = DateTime.Now;
                    _scalper.StatusMessage = $"Скальп выполнен: {tradeType}";
                }

                Print($"⚡ Скальп-сделка открыта: {tradeType} при конфликте {_detectedConflict}");
            }
        }

        private void DeactivateScalpingTrap()
        {
            _scalpingTrapActive = false;

            if (_scalper != null)
            {
                _scalper.Status = SpecialistStatus.Waiting;
                _scalper.StatusMessage = "Ловушка деактивирована";
            }

            Print("⚡ Ловушка скальпинга деактивирована");
        }

        private void AnalyzeMarketDepthState()
        {
            try
            {
                if (_marketDepth == null)
                    return;
                var bidEntries = _marketDepth.BidEntries.Take(5).ToList();
                var askEntries = _marketDepth.AskEntries.Take(5).ToList();
                if (bidEntries.Count < 3 || askEntries.Count < 3)
                {
                    var pseudo = ComputePseudoImbalance();
                    if (pseudo.HasValue)
                    {
                        _orderBookImbalance = pseudo.Value;
                        PushImbalanceSample(DateTime.Now, _orderBookImbalance);
                    }
                    else
                    {
                        _orderBookImbalance = 0;
                    }
                    return;
                }
                double totalBidVolume = bidEntries.Sum(b => b.VolumeInUnits);
                double totalAskVolume = askEntries.Sum(a => a.VolumeInUnits);
                double totalVolume = totalBidVolume + totalAskVolume;
                if (totalVolume == 0)
                {
                    var pseudo = ComputePseudoImbalance();
                    if (pseudo.HasValue)
                    {
                        _orderBookImbalance = pseudo.Value;
                        PushImbalanceSample(DateTime.Now, _orderBookImbalance);
                    }
                    else
                    {
                        _orderBookImbalance = 0;
                    }
                    return;
                }
                _orderBookImbalance = (totalBidVolume - totalAskVolume) / totalVolume * 100.0;
                // track imbalance history for flow analysis
                PushImbalanceSample(DateTime.Now, _orderBookImbalance);
            }
            catch (Exception ex)
            {
                Print($"Ошибка в анализе стакана: {ex.Message}");
                _orderBookImbalance = 0;
            }
        }

        private MarketType DetectMarketType()
        {
            string symbol = SymbolName.ToUpper();

            if (symbol.Contains("BTC") || symbol.Contains("ETH") || symbol.Contains("LTC") ||
                symbol.Contains("XRP") || symbol.Contains("ADA") || symbol.Contains("DOT") ||
                symbol.Contains("USDT") || symbol.Contains("USDC") || symbol.Contains("BNB"))
                return MarketType.Crypto;
            if (symbol.Contains("XAU") || symbol.Contains("GOLD") || symbol.Contains("XAG") ||
                symbol.Contains("SILVER") || symbol.Contains("PLATINUM") || symbol.Contains("PALLADIUM"))
                return MarketType.Metals;
            if (symbol.Contains("OIL") || symbol.Contains("WTI") || symbol.Contains("BRENT") ||
                symbol.Contains("CRUDE") || symbol.Contains("CL") || symbol.Contains("QM"))
                return MarketType.Oil;
            if (symbol.Contains("SPX") || symbol.Contains("NAS") || symbol.Contains("DOW") ||
                symbol.Contains("DAX") || symbol.Contains("FTSE") || symbol.Contains("NIKKEI") ||
                symbol.Contains("US30") || symbol.Contains("US500") || symbol.Contains("NAS100"))
                return MarketType.Indices;
            if (symbol.Contains(".") || symbol.Length <= 5 && !IsForexPair(symbol))
                return MarketType.Stocks;
            if (IsForexPair(symbol))
            {
                string[] majorPairs = { "EURUSD", "GBPUSD", "USDJPY", "USDCHF", "AUDUSD", "USDCAD", "NZDUSD" };
                string[] minorPairs = { "EURGBP", "EURJPY", "EURCHF", "EURAUD", "EURCAD", "EURCZK", "EURNOK", "EURSEK",
                                      "GBPJPY", "GBPCHF", "GBPAUD", "GBPCAD", "GBPNZD", "AUDJPY", "AUDCHF", "AUDCAD",
                                      "AUDNZD", "CADJPY", "CADCHF", "CHFJPY", "NZDJPY", "NZDCHF", "NZDCAD" };
                if (majorPairs.Contains(symbol))
                    return MarketType.ForexMajor;
                else if (minorPairs.Contains(symbol))
                    return MarketType.ForexMinor;
                else
                    return MarketType.ForexExotic;
            }
            return MarketType.ForexMajor;
        }

        private bool IsForexPair(string symbol)
        {
            return symbol.Length == 6 &&
                   char.IsLetter(symbol[0]) && char.IsLetter(symbol[1]) && char.IsLetter(symbol[2]) &&
                   char.IsLetter(symbol[3]) && char.IsLetter(symbol[4]) && char.IsLetter(symbol[5]);
        }

        private MarketSettings GetMarketSettings(MarketType marketType)
        {
            switch (marketType)
            {
                case MarketType.ForexMajor:
                    return new MarketSettings
                    {
                        OrdersCount = ForexMajorOrders,
                        StepPips = ForexMajorStep,
                        TakeProfitPips = ForexMajorTP,
                        MaxSpreadPips = ForexMajorMaxSpread,
                        MarketName = "Forex Major"
                    };
                case MarketType.ForexMinor:
                    return new MarketSettings
                    {
                        OrdersCount = ForexMinorOrders,
                        StepPips = ForexMinorStep,
                        TakeProfitPips = ForexMinorTP,
                        MaxSpreadPips = ForexMinorMaxSpread,
                        MarketName = "Forex Minor"
                    };
                case MarketType.ForexExotic:
                    return new MarketSettings
                    {
                        OrdersCount = ForexExoticOrders,
                        StepPips = ForexExoticStep,
                        TakeProfitPips = ForexExoticTP,
                        MaxSpreadPips = ForexExoticMaxSpread,
                        MarketName = "Forex Exotic"
                    };
                case MarketType.Crypto:
                    return new MarketSettings
                    {
                        OrdersCount = CryptoOrders,
                        StepPips = CryptoStep,
                        TakeProfitPips = CryptoTP,
                        MaxSpreadPips = CryptoMaxSpread,
                        MarketName = "Cryptocurrency"
                    };
                case MarketType.Metals:
                    return new MarketSettings
                    {
                        OrdersCount = MetalsOrders,
                        StepPips = MetalsStep,
                        TakeProfitPips = MetalsTP,
                        MaxSpreadPips = MetalsMaxSpread,
                        MarketName = "Precious Metals"
                    };
                case MarketType.Oil:
                    return new MarketSettings
                    {
                        OrdersCount = OilOrders,
                        StepPips = OilStep,
                        TakeProfitPips = OilTP,
                        MaxSpreadPips = OilMaxSpread,
                        MarketName = "Oil & Energy"
                    };
                case MarketType.Indices:
                    return new MarketSettings
                    {
                        OrdersCount = IndicesOrders,
                        StepPips = IndicesStep,
                        TakeProfitPips = IndicesTP,
                        MaxSpreadPips = IndicesMaxSpread,
                        MarketName = "Stock Indices"
                    };
                case MarketType.Stocks:
                    return new MarketSettings
                    {
                        OrdersCount = StocksOrders,
                        StepPips = StocksStep,
                        TakeProfitPips = StocksTP,
                        MaxSpreadPips = StocksMaxSpread,
                        MarketName = "Individual Stocks"
                    };
                default:
                    return new MarketSettings
                    {
                        OrdersCount = 10,
                        StepPips = 2.0,
                        TakeProfitPips = 3.0,
                        MaxSpreadPips = 5.0,
                        MarketName = "Default Market"
                    };
            }
        }

        private void DecideAndExecuteQuantumStrategy()
        {
            double currentSpread = _detectedMarket == MarketType.Crypto ? Symbol.Spread : Symbol.Spread / Symbol.PipSize;

            if (currentSpread > _currentSettings.MaxSpreadPips)
            {
                if (PendingOrders.Any(o => o.Label == "Farmer"))
                    CancelAllFarmerOrders();
                _currentStrategy = $"Спред слишком большой ({currentSpread:F2} > {_currentSettings.MaxSpreadPips:F2})";
                return;
            }
            if (Math.Abs(_orderBookImbalance) >= GetEffectiveHunterThreshold())
            {
                if (_hunterPosition == null)
                {
                    _currentStrategy = "Охотник: Атака дисбаланса!";
                    ExecuteHunterMode();
                }
            }
            else
            {
                _currentStrategy = $"Фермер: {_currentSettings.MarketName}";
                ExecuteFarmerMode();
            }
        }

        private void ExecuteFarmerMode()
        {
            // Keep margin reserve for Hunter: skip farmer grid if free margin at/below reserve
            try
            {
                double hunterReserve = GetHunterReserveMarginCurrency();
                if (hunterReserve > 0 && Account.FreeMargin <= hunterReserve)
                {
                    _currentStrategy = "PAUSE: резерв маржи под Hunter";
                    return;
                }
            }
            catch { }

            if (UseBatchOrderPlacement)
            {
                MaintainFarmerBatch();
                return;
            }
            var farmerOrders = PendingOrders.Where(o => o.Label == "Farmer" && o.SymbolName == SymbolName);

            if (farmerOrders.Count() != _currentSettings.OrdersCount * 2 ||
                (DateTime.Now - _lastGridRebuild).TotalMinutes > 10)
            {
                RebuildGrid(_currentSettings.OrdersCount, _currentSettings.OrdersCount);
            }
        }

        private void ExecuteHunterMode()
        {
            if (_hunterPosition != null)
                return;
            CancelAllFarmerOrders();
            var tradeType = _orderBookImbalance > 0 ? TradeType.Sell : TradeType.Buy;
            double volume = Symbol.QuantityToVolumeInUnits(OrderVolumeLots);
            if (AbortIfPositionCapReached("HunterMode"))
                return;
            if (UseDynamicTargets)
            {
                double entryApprox = tradeType == TradeType.Buy ? Symbol.Ask : Symbol.Bid;
                var targets = ComputeDynamicTargets(tradeType, entryApprox);
                var distances = ToPipDistances(tradeType, entryApprox, targets.sl, targets.tp);
                var result = ExecuteMarketOrder(tradeType, SymbolName, volume, "Hunter", distances.slPips, distances.tpPips);
                if (result.IsSuccessful)
                {
                    _hunterPosition = result.Position;
                    try
                    {
                        var refined = ComputeDynamicTargets(tradeType, _hunterPosition.EntryPrice);
                        ModifyPosition(_hunterPosition, refined.sl, refined.tp, ProtectionType.Absolute);
                    }
                    catch { }
                    Print($"🎯 Охотник открыл {tradeType} позицию! Дисбаланс: {_orderBookImbalance:F1}%");
                }
                else
                {
                    var stopLossDistance = _atr.Result.LastValue * HunterAtrMultiplier;
                    var stopLossPips = Math.Abs(stopLossDistance / Symbol.PipSize);
                    if (AbortIfPositionCapReached("HunterMode"))
                        return;
                    var fallbackResult = ExecuteMarketOrder(tradeType, SymbolName, volume, "Hunter", stopLossPips, null);
                    if (fallbackResult.IsSuccessful)
                    {
                        _hunterPosition = fallbackResult.Position;
                        Print($"🎯 Охотник открыл {tradeType} позицию! Дисбаланс: {_orderBookImbalance:F1}%");
                    }
                }
            }
        }

        private void RebuildGrid(int buys, int sells)
        {
            CancelAllFarmerOrders();
            double volume = Symbol.QuantityToVolumeInUnits(OrderVolumeLots);
            // Respect max open positions by limiting how many pending orders we place
            int capacity = AvailablePositionSlots();
            if (capacity <= 0)
            {
                Print($"⛔ Сетка не перестроена: лимит позиций {MaxOpenPositions} уже достигнут.");
                return;
            }
            // Respect Hunter margin reserve: avoid placing new grid if free margin at/below reserve
            try
            {
                double hunterReserve = GetHunterReserveMarginCurrency();
                if (hunterReserve > 0 && Account.FreeMargin <= hunterReserve)
                {
                    Print("⏸️ Сетка не ставится: резерв маржи под Hunter");
                    return;
                }
            }
            catch { }
            int placed = 0;
            double stepSize = _detectedMarket == MarketType.Crypto ?
                _currentSettings.StepPips :
                _currentSettings.StepPips * Symbol.PipSize;

            // Динамический TP/SL рассчитываем от цены каждого ордера

            for (int i = 1; i <= buys; i++)
            {
                if (placed >= capacity) break;

                double price = Symbol.Bid - i * stepSize;

                if (UseDynamicTargets)
                {
                    var targets = ComputeDynamicTargets(TradeType.Buy, price);
                    var distances = ToPipDistances(TradeType.Buy, price, targets.sl, targets.tp);
                    PlaceLimitOrder(TradeType.Buy, SymbolName, volume, price, "Farmer", distances.slPips, distances.tpPips);
                }
                else
                {
                    double slPips = FixedStopLossPips;
                    double tpPips = FixedTakeProfitPips;
                    var adapt = GetAdaptiveTpPips();
                    if (adapt.HasValue) tpPips = Math.Min(tpPips, adapt.Value);
                    PlaceLimitOrder(TradeType.Buy, SymbolName, volume, price, "Farmer", slPips, tpPips);
                }
                placed++;
            }

            for (int i = 1; i <= sells; i++)
            {
                if (placed >= capacity) break;
                double price = Symbol.Ask + i * stepSize;
                if (UseDynamicTargets)
                {
                    var targets = ComputeDynamicTargets(TradeType.Sell, price);
                    var distances = ToPipDistances(TradeType.Sell, price, targets.sl, targets.tp);
                    PlaceLimitOrder(TradeType.Sell, SymbolName, volume, price, "Farmer", distances.slPips, distances.tpPips);
                }
                else
                {
                    double slPips = FixedStopLossPips;
                    double tpPips = FixedTakeProfitPips;
                    var adapt = GetAdaptiveTpPips();
                    if (adapt.HasValue) tpPips = Math.Min(tpPips, adapt.Value);
                    PlaceLimitOrder(TradeType.Sell, SymbolName, volume, price, "Farmer", slPips, tpPips);
                }
                placed++;
            }
            _lastGridRebuild = DateTime.Now;
            Print($"🌾 Сетка перестроена: размещено {placed} ордеров (лимит позиций {MaxOpenPositions})");
        }

        private void ManageHunterPosition()
        {
            if (_hunterPosition == null || _hunterPosition.Pips <= 0)
                return;
            try
            {
                double trailingStopDistance = _atr.Result.LastValue * HunterAtrMultiplier;

                if (_hunterPosition.TradeType == TradeType.Buy)
                {
                    double newStopLoss = Symbol.Bid - trailingStopDistance;
                    if (_hunterPosition.StopLoss == null || newStopLoss > _hunterPosition.StopLoss)
                    {
                        ModifyPosition(_hunterPosition, newStopLoss, _hunterPosition.TakeProfit, ProtectionType.Absolute);
                    }
                }
                else
                {
                    double newStopLoss = Symbol.Ask + trailingStopDistance;
                    if (_hunterPosition.StopLoss == null || newStopLoss < _hunterPosition.StopLoss)
                    {
                        ModifyPosition(_hunterPosition, newStopLoss, _hunterPosition.TakeProfit, ProtectionType.Absolute);
                    }
                }
            }
            catch (Exception ex)
            {
                Print($"Ошибка в ManageHunterPosition: {ex.Message}");
            }
        }

        private void CheckEmergencyStop()
        {
            // This is the "hard stop" check. It runs with high frequency (e.g., OnTick)
            // and immediately liquidates the portfolio for the symbol if a critical threshold is breached.
            if (_tradingPaused) return;

            bool emergencyTriggered = false;
            string triggerReason = string.Empty;

            // 1. Check Equity Drawdown Percentage
            if (EnableDrawdownMitigation && EmergencyStopLossPercent > 0 && EmergencyStopLossPercent < 100)
            {
                double drawdownPercent = Account.Balance > 0 ? ((Account.Balance - Account.Equity) / Account.Balance) * 100 : 0;
                if (drawdownPercent >= EmergencyStopLossPercent)
                {
                    emergencyTriggered = true;
                    triggerReason = $"Просадка эквити {drawdownPercent:F1}% >= лимита {EmergencyStopLossPercent}%";
                }
            }

            // 2. Check Absolute Net Loss Target (if not already triggered)
            if (!emergencyTriggered && AutoCloseOnNetProfitAbs && NetLossAbsTarget > 0)
            {
                var positions = Positions.Where(p => p.SymbolName == SymbolName);
                if (positions.Any())
                {
                    double totalPnL = positions.Sum(p => p.NetProfit);
                    if (totalPnL <= -NetLossAbsTarget)
                    {
                        emergencyTriggered = true;
                        triggerReason = $"Абсолютный убыток {totalPnL:C} <= лимита -{NetLossAbsTarget:C}";
                    }
                }
            }

            // 3. If triggered, execute emergency actions
            if (emergencyTriggered)
            {
                Print($"🚨 ЭКСТРЕННЫЙ СТОП: {triggerReason}");
                LogToFile(LogLevel.Error, $"EMERGENCY STOP: {triggerReason}. Balance={Account.Balance:C}, Equity={Account.Equity:C}");

                CloseAllPositionsAndStop(triggerReason);
            }
        }

        private bool CheckGlobalRiskLimits()
        {
            // Daily pause guard
            if (_pausedByDaily || _pausedByLossTrail)
            {
                _currentStrategy = _pausedByDaily ? "PAUSE: дневная цель достигнута" : "PAUSE: после тралла убытка";
                return true;
            }
            // Margin pause logic
            try
            {
                if (!PassesMarketPolicy())
                    return true;
                double freeMarginPct = Account.Balance > 0 ? (Account.FreeMargin / Account.Balance) * 100.0 : 0.0;
                if (freeMarginPct < PauseFreeMarginPercent)
                {
                    if (!_tradingPaused)
                    {
                        _pauseReason = $"Нехватка маржи: {freeMarginPct:F1}% < {PauseFreeMarginPercent}%";
                        _tradingPaused = true;
                        CancelAllPendingOrdersForSymbol();
                        _currentStrategy = $"PAUSE: {_pauseReason}";
                        LogToFile(LogLevel.Info, $"Trading paused (margin): {_pauseReason}");
                    }
                    return true;
                }
                if (_tradingPaused && freeMarginPct >= ResumeFreeMarginPercent)
                {
                    _tradingPaused = false;
                    _pauseReason = string.Empty;
                    LogToFile(LogLevel.Info, "Trading resumed (margin)");
                    _currentStrategy = "Возобновление после паузы (маржа)";
                }
            }
            catch { }

            // Equity drawdown mitigation instead of full stop
            if (EnableDrawdownMitigation && Account.Equity < Account.Balance * (1 - EmergencyStopLossPercent / 100.0))
            {
                MitigateDrawdown();
                return true;
            }

            var symbolPositions = Positions.Where(p => p.SymbolName == SymbolName);
            double buyLots = symbolPositions.Where(p => p.TradeType == TradeType.Buy).Sum(p => Symbol.VolumeInUnitsToQuantity(p.VolumeInUnits));
            double sellLots = symbolPositions.Where(p => p.TradeType == TradeType.Sell).Sum(p => Symbol.VolumeInUnitsToQuantity(p.VolumeInUnits));

            if (Math.Abs(buyLots - sellLots) > MaxNetExposureLots)
            {
                CloseAllPositionsAndStop("АВАРИЙНЫЙ СТОП: Превышен лимит чистого объема.");
                return true;
            }
            return false;
        }

        private bool IsGoldSymbol()
        {
            try
            {
                var s = (SymbolName ?? string.Empty).ToUpperInvariant();
                return s.Contains("XAU") || s.Contains("GOLD");
            }
            catch { return false; }
        }

        // ---- Position cap helpers ----
        private int OpenPositionsForSymbol()
        {
            try { return Positions.Count(p => p.SymbolName == SymbolName); }
            catch { return 0; }
        }

        private int AvailablePositionSlots()
        {
            try
            {
                if (MaxOpenPositions <= 0) return int.MaxValue;
                var openUsed = OpenPositionsForSymbol();
                var pendingUsed = PendingOrders.Count(o => o.SymbolName == SymbolName);
                var used = openUsed + pendingUsed;
                var left = MaxOpenPositions - used;
                return left < 0 ? 0 : left;
            }
            catch { return 0; }
        }

        private int GetEffectiveHunterThreshold()
        {
            try
            {
                int thr = HunterImbalanceThreshold;
                if (EnableHunterDynamicThreshold && IsRangeCompressed())
                {
                    double f = HunterThresholdCompressionFactor;
                    if (double.IsNaN(f) || f <= 0) f = 1.0;
                    thr = (int)Math.Max(1, Math.Round(thr * f));
                }
                return thr;
            }
            catch { return HunterImbalanceThreshold; }
        }

        private double? ComputePseudoImbalance()
        {
            try
            {
                if (!EnablePseudoImbalanceWhenL2Empty) return null;
                if (PseudoImbalanceOnlyInCompression && !IsRangeCompressed()) return null;
                int sign = 0;
                if (_lowerTFAnalysis != null && _lowerTFAnalysis.IsValid)
                {
                    // Map trend to the model�s sign convention: positive imbalance currently triggers Sell in logic,
                    // so Bullish trend should yield negative value to trigger Buy; Bearish -> positive for Sell.
                    sign = _lowerTFAnalysis.Trend == TrendDirection.Bullish ? -1 :
                           _lowerTFAnalysis.Trend == TrendDirection.Bearish ? 1 :
                           0;
                }
                if (sign == 0) return null;
                double val = Clamp(PseudoImbalancePercent, 0, 100);
                return sign * val;
            }
            catch { return null; }
        }

        // Maintains Farmer orders in small batches (e.g., 2 at a time)
        private void MaintainFarmerBatch()
        {
            try
            {
                // Respect Hunter margin reserve: don't place new pendings if free margin is at/below reserve
                double hunterReserve = GetHunterReserveMarginCurrency();
                if (hunterReserve > 0 && Account.FreeMargin <= hunterReserve)
                {
                    _farmer.StatusMessage = "Резерв маржи под Hunter сохранён; новые ордера не ставим";
                    return;
                }
                // If open positions already at max, cancel Farmer pendings and wait
                if (MaxOpenPositions > 0 && OpenPositionsForSymbol() >= MaxOpenPositions)
                {
                    foreach (var order in PendingOrders.Where(o => o.Label == "Farmer" && o.SymbolName == SymbolName).ToList())
                        CancelPendingOrder(order);
                    _farmer.StatusMessage = $"Ждем: достигнут лимит позиций ({MaxOpenPositions})";
                    return;
                }

                // Compute free slots considering open + pending
                int freeSlots = AvailablePositionSlots();
                if (freeSlots <= 0)
                {
                    _farmer.StatusMessage = "Ждем срабатывания ордеров";
                    return;
                }

                // Current Farmer pendings by side
                var farmerPendings = PendingOrders.Where(o => o.SymbolName == SymbolName && o.Label == "Farmer").ToList();
                int pendingBuy = farmerPendings.Count(o => o.TradeType == TradeType.Buy);
                int pendingSell = farmerPendings.Count(o => o.TradeType == TradeType.Sell);

                // Plan to place at most BatchOrderSize new orders, limited by freeSlots
                int toPlace = Math.Min(Math.Max(0, freeSlots), Math.Max(1, BatchOrderSize));
                if (toPlace <= 0)
                {
                    _farmer.StatusMessage = "Ждем срабатывания ордеров";
                    return;
                }

                double volume = Symbol.QuantityToVolumeInUnits(OrderVolumeLots);
                double stepSize = _detectedMarket == MarketType.Crypto ? _currentSettings.StepPips : _currentSettings.StepPips * Symbol.PipSize;

                int placed = 0;
                while (placed < toPlace)
                {
                    // Alternate sides to keep symmetry
                    bool placeBuy = pendingBuy <= pendingSell;
                    if (placeBuy)
                    {
                        int idx = pendingBuy + 1;
                        double price = Symbol.Bid - idx * stepSize;
                        if (UseDynamicTargets)
                        {
                            var targets = ComputeDynamicTargets(TradeType.Buy, price);
                            var distances = ToPipDistances(TradeType.Buy, price, targets.sl, targets.tp);
                            PlaceLimitOrder(TradeType.Buy, SymbolName, volume, price, "Farmer", distances.slPips, distances.tpPips);
                        }
                        else
                        {
                            double tpPips = FixedTakeProfitPips;
                            if (IncludeCommissionInTargets && CommissionPerLotPerSide > 0 && OrderVolumeLots > 0)
                                tpPips += CommissionPipsForLots(OrderVolumeLots);
                            double slPips = FixedStopLossPips;
                            PlaceLimitOrder(TradeType.Buy, SymbolName, volume, price, "Farmer", slPips, tpPips);
                        }
                        pendingBuy++;
                    }
                    else
                    {
                        int idx = pendingSell + 1;
                        double price = Symbol.Ask + idx * stepSize;
                        if (UseDynamicTargets)
                        {
                            var targets = ComputeDynamicTargets(TradeType.Sell, price);
                            var distances = ToPipDistances(TradeType.Sell, price, targets.sl, targets.tp);
                            PlaceLimitOrder(TradeType.Sell, SymbolName, volume, price, "Farmer", distances.slPips, distances.tpPips);
                        }
                        else
                        {
                            double tpPips = FixedTakeProfitPips;
                            if (IncludeCommissionInTargets && CommissionPerLotPerSide > 0 && OrderVolumeLots > 0)
                                tpPips += CommissionPipsForLots(OrderVolumeLots);
                            double slPips = FixedStopLossPips;
                            PlaceLimitOrder(TradeType.Sell, SymbolName, volume, price, "Farmer", slPips, tpPips);
                        }
                        pendingSell++;
                    }
                    placed++;
                }
                _lastGridRebuild = DateTime.Now;
                _farmer.StatusMessage = $"Поставлено {placed} ордера (batch)";
            }
            catch (Exception ex)
            {
                Print($"Ошибка MaintainFarmerBatch: {ex.Message}");
            }
        }

        private bool CanOpenAnotherPosition()
        {
            return AvailablePositionSlots() > 0;
        }

        // ---- Hunter margin reserve helpers ----
        private double GetHunterReserveMarginCurrency()
        {
            try
            {
                double lots = Math.Max(0.0, HunterReserveLots);
                if (lots <= 0) return 0.0;
                double vol = Symbol.QuantityToVolumeInUnits(lots);
                double mBuy = 0.0, mSell = 0.0;
                try { mBuy = Symbol.GetEstimatedMargin(TradeType.Buy, vol); } catch { }
                try { mSell = Symbol.GetEstimatedMargin(TradeType.Sell, vol); } catch { }
                return Math.Max(mBuy, mSell);
            }
            catch { return 0.0; }
        }

        private double EstimatedMarginForLots(TradeType tradeType, double lots)
        {
            try
            {
                if (lots <= 0) return 0.0;
                double vol = Symbol.QuantityToVolumeInUnits(lots);
                return Symbol.GetEstimatedMargin(tradeType, vol);
            }
            catch { return 0.0; }
        }

        private bool HasMarginAfterReserve(double extraNeeded)
        {
            try
            {
                double reserve = GetHunterReserveMarginCurrency();
                return (Account.FreeMargin - Math.Max(0.0, extraNeeded)) > reserve;
            }
            catch { return true; }
        }

        private bool AbortIfPositionCapReached(string context)
        {
            if (!CanOpenAnotherPosition())
            {
                _currentStrategy = $"PAUSE: достигнут лимит позиций ({MaxOpenPositions}) [{context}]";
                Print($"⛔ Лимит открытых позиций достигнут: {MaxOpenPositions}. Блокируем вход [{context}].");
                return true;
            }
            return false;
        }

        private bool PassesMarketPolicy()
        {
            try
            {
                if (OnlyGold && !IsGoldSymbol())
                {
                    CancelAllPendingOrdersForSymbol();
                    _currentStrategy = "PAUSE: только GOLD/XAU";
                    return false;
                }
                // Жестко блокируем Crypto независимо от параметров
                if (_detectedMarket == MarketType.Crypto)
                {
                    CancelAllPendingOrdersForSymbol();
                    _currentStrategy = "PAUSE: крипто рынок заблокирован";
                    return false;
                }
            }
            catch { }
            return true;
        }

        private void ApplyAggressionPreset(ProfitPreset preset)
        {
            if (preset == ProfitPreset.Custom)
                return;

            int metalsOrders = MetalsOrders;
            double metalsStep = MetalsStep;
            double metalsTP = MetalsTP;
            bool useTrap = UseTrapGrid;
            int hunterThr = HunterImbalanceThreshold;
            double totalExpo = MaxTotalExposureLots;
            double netExpo = MaxNetExposureLots;
            double trailStartPct = NetTrailStartPercentOfBalance;
            double trailStepPct = NetTrailStepPercentOfBalance;
            double lockFrac = NetTrailLockFraction;

            switch (preset)
            {
                case ProfitPreset.P20:
                    metalsOrders = 60; metalsStep = 6.0; metalsTP = 10.0; useTrap = false;
                    hunterThr = 70; totalExpo = 0.5; netExpo = 0.5; trailStartPct = 1.0; trailStepPct = 0.5; lockFrac = 0.33;
                    break;
                case ProfitPreset.P30:
                    metalsOrders = 80; metalsStep = 5.0; metalsTP = 8.0; useTrap = false;
                    hunterThr = 65; totalExpo = 0.8; netExpo = 0.7; trailStartPct = 1.5; trailStepPct = 0.75; lockFrac = 0.40;
                    break;
                case ProfitPreset.P40:
                    metalsOrders = 100; metalsStep = 4.0; metalsTP = 7.0; useTrap = true;
                    hunterThr = 60; totalExpo = 1.0; netExpo = 0.9; trailStartPct = 2.0; trailStepPct = 1.0; lockFrac = 0.50;
                    break;
                case ProfitPreset.P50:
                    metalsOrders = 120; metalsStep = 3.0; metalsTP = 6.0; useTrap = true;
                    hunterThr = 55; totalExpo = 1.5; netExpo = 1.2; trailStartPct = 2.5; trailStepPct = 1.25; lockFrac = 0.60;
                    break;
                case ProfitPreset.P80:
                    metalsOrders = 150; metalsStep = 2.0; metalsTP = 5.0; useTrap = true;
                    hunterThr = 50; totalExpo = 2.0; netExpo = 1.5; trailStartPct = 4.0; trailStepPct = 2.0; lockFrac = 0.70;
                    break;
                case ProfitPreset.P100:
                    metalsOrders = 200; metalsStep = 1.5; metalsTP = 4.0; useTrap = true;
                    hunterThr = 45; totalExpo = 3.0; netExpo = 2.0; trailStartPct = 5.0; trailStepPct = 2.5; lockFrac = 0.80;
                    break;
                case ProfitPreset.Legacy30:
                    // Exact mapping of user legacy JSON preset geared for XAUUSD TrapGrid semi-auto
                    useTrap = true;
                    metalsOrders = 5; metalsStep = 5.0; metalsTP = 300.0;
                    MaxTrapZones = 1; TrapUseOCO = true; TrapAtrBuffer = 0.25; TrapZoneBufferPips = 5.0; TrapRebuildSeconds = 60;
                    EnableTrapReversion = true; EnableTrapBreakout = true;

                    // Risk and sizing
                    OrderVolumeLots = 0.03; totalExpo = 0.5; netExpo = 0.5;
                    EmergencyStopLossPercent = 100.0; // effectively disabled

                    // Hunter tuned high threshold to rarely intervene
                    hunterThr = 79; HunterAtrMultiplier = 1.9;

                    // Targets: fixed MR/BO, with commission included
                    UseDynamicTargets = false; FixedTakeProfitPips = 500.0; FixedStopLossPips = 3000.0;
                    IncludeCommissionInTargets = true; CommissionPerLotPerSide = 30.2;
                    TpAtrMultiplier = 1.2; SlAtrMultiplier = 1.3; MinSlAtrMultiplier = 1.0; LevelBufferAtr = 0.2;

                    // Timeframes
                    AnalysisTimeFrameIndex = 2; // M5
                    HigherTimeFrameIndex = 1;   // M15
                    LowerTimeFrameIndex = 0;    // M1

                    // Info/cluster
                    EnableClusterAnalysis = true; TrackIcebergOrders = true; AggressiveVolumeThreshold = 30; SwingLookback = 50;
                    EnableMultiTimeFrameAnalysis = true; EnableConflictScalping = true; EnableParallelTeam = true; EnableDashboard = true;

                    // Session
                    UseServerTimeForSession = true; SessionTimezoneOffsetHours = 2;

                    // Auto-close: percent sum, profits only, tiny threshold, cancel pendings
                    AutoCloseOnNetProfit = true; ProfitThresholdPercentOfBalance = 0.05; AutoCloseOnlyProfitablePositions = true; AutoCloseCancelPendingOrders = true; AutoCloseCheckIntervalMinutes = 1;
                    // No net profit/loss trailing in legacy
                    AutoCloseNetProfitTrailing = false; AutoCloseNetLossTrailing = false;

                    // Make sure we're on Metals; autodetect is fine with XAUUSD
                    AutoDetectMarket = true; ManualMarketType = MarketType.Metals;

                    // Leave daily targets off
                    DailyTargetsEnable = false;

                    // Keep current trailing net defaults (unused when disabled)
                    trailStartPct = NetTrailStartPercentOfBalance; trailStepPct = NetTrailStepPercentOfBalance; lockFrac = NetTrailLockFraction;
                    break;
            }

            // Apply common controls
            HunterImbalanceThreshold = hunterThr;
            MaxTotalExposureLots = totalExpo;
            MaxNetExposureLots = netExpo;
            UseTrapGrid = useTrap;
            EnableTrapReversion = useTrap;
            EnableTrapBreakout = useTrap;
            // If legacy disabled trailing above, respect it; otherwise set trailing params
            if (AutoCloseNetProfitTrailing)
            {
                NetTrailStartPercentOfBalance = trailStartPct;
                NetTrailStepPercentOfBalance = trailStepPct;
                NetTrailLockFraction = lockFrac;
            }

            // Metals-specific tuning
            MetalsOrders = metalsOrders;
            MetalsStep = metalsStep;
            MetalsTP = metalsTP;
        }

        private void OnPositionsClosed(PositionClosedEventArgs args)
        {
            try
            {
                if (args.Position.SymbolName != SymbolName) return;
                // accumulate realized P/L for daily accounting
                _realizedToday += args.Position.NetProfit;
            }
            catch { }
        }

        private void PushImbalanceSample(DateTime time, double value)
        {
            try
            {
                if (_imbalanceHistory == null) _imbalanceHistory = new List<Tuple<DateTime, double>>();
                _imbalanceHistory.Add(new Tuple<DateTime, double>(time, value));
                // prune by FlowWindowSeconds
                var cutoff = time.AddSeconds(-Math.Max(10, FlowWindowSeconds));
                // remove old samples
                int idx = 0;
                while (idx < _imbalanceHistory.Count && _imbalanceHistory[idx].Item1 < cutoff)
                    idx++;
                if (idx > 0)
                    _imbalanceHistory.RemoveRange(0, Math.Min(idx, _imbalanceHistory.Count));
                // cap size to avoid growth
                if (_imbalanceHistory.Count > 600)
                    _imbalanceHistory.RemoveRange(0, _imbalanceHistory.Count - 600);
            }
            catch { }
        }

        private (double mean, double slope) ComputeImbalanceStats()
        {
            try
            {
                if (_imbalanceHistory == null || _imbalanceHistory.Count < 2) return (0.0, 0.0);
                var values = _imbalanceHistory.Select(t => t.Item2).ToList();
                double mean = values.Average();
                // simple slope: last - first
                double slope = values.Last() - values.First();
                return (mean, slope);
            }
            catch { return (0.0, 0.0); }
        }

        private double ComputeDeltaSlope(int window)
        {
            try
            {
                if (_deltaHistory == null || _deltaHistory.Count < 2) return 0.0;
                int n = Math.Max(2, Math.Min(window, _deltaHistory.Count));
                var slice = _deltaHistory.Skip(_deltaHistory.Count - n).Take(n).ToList();
                return slice.Last() - slice.First();
            }
            catch { return 0.0; }
        }

        private double Clamp(double v, double min, double max)
        {
            if (double.IsNaN(v)) return min;
            return v < min ? min : (v > max ? max : v);
        }

        private bool TryHtfReversalScalp()
        {
            try
            {
                if (_htfRevBars == null || _htfRevBars.Count < Math.Max(5, HtfReversalLookback))
                    return false;

                TradeType signal;
                if (!DetectHtfReversal(out signal))
                    return false;

                // Risk checks and captain approval
                double lotsReq = OrderVolumeLots * 0.5;
                var cmd = EnableParallelTeam ? EvaluateTeamRequest(TeamRole.Scalper, lotsReq, signal) : CaptainCommand.Proceed;
                if (cmd != CaptainCommand.Proceed)
                {
                    if (_scalper != null) _scalper.StatusMessage = "�������� �������� (HTF)";
                    return false;
                }
                // Margin reserve for Hunter
                try
                {
                    double est = EstimatedMarginForLots(signal, lotsReq);
                    if (!HasMarginAfterReserve(est))
                    {
                        if (_scalper != null) _scalper.StatusMessage = "����� �������� (������ ��� Hunter)";
                        return false;
                    }
                }
                catch { }

                if (AbortIfPositionCapReached("ScalperHTF"))
                    return false;

                double volume = Symbol.QuantityToVolumeInUnits(lotsReq);
                double entry = signal == TradeType.Buy ? Symbol.Ask : Symbol.Bid;
                double atr = Math.Max(_atr?.Result.LastValue ?? 0, Symbol.PipSize * 2);
                double slAbs = HtfScalpSlAtrMult * atr;
                double tpAbs = HtfScalpTpAtrMult * atr;

                double sl = signal == TradeType.Buy ? entry - slAbs : entry + slAbs;
                double tp = signal == TradeType.Buy ? entry + tpAbs : entry - tpAbs;

                // Compress TP in range
                try
                {
                    if (EnableRangeAdaptiveTargets && IsRangeCompressed())
                    {
                        double delta = Math.Max(0.0, AdaptiveTpUSD);
                        double lim = signal == TradeType.Buy ? entry + delta : entry - delta;
                        if (signal == TradeType.Buy) tp = Math.Min(tp, lim); else tp = Math.Max(tp, lim);
                    }
                }
                catch { }

                var d = ToPipDistances(signal, entry, sl, tp);
                var res = ExecuteMarketOrder(signal, SymbolName, volume, "ScalperHTF", d.slPips, d.tpPips);
                if (res.IsSuccessful)
                {
                    if (_scalper != null)
                    {
                        _scalper.ActionsToday++;
                        _scalper.LastAction = DateTime.Now;
                        _scalper.StatusMessage = $"HTF-������: {signal}";
                    }
                    Print($"? HTF Reversal scalp ������: {signal}");
                    return true;
                }
            }
            catch (Exception ex)
            {
                Print($"������ HTF-�������: {ex.Message}");
            }
            return false;
        }

        private bool DetectHtfReversal(out TradeType tradeType)
        {
            tradeType = TradeType.Buy;
            try
            {
                var hbars = _htfRevBars;
                var last = hbars.LastBar;
                if (last == null) return false;

                double atr = Math.Max(_atr?.Result.LastValue ?? 0, Symbol.PipSize * 2);
                double buf = atr * HtfLevelBufferAtr;
                double price = (Symbol.Bid + Symbol.Ask) * 0.5;

                double vah = _currentVolumeProfile?.VAH > 0 ? _currentVolumeProfile.VAH : double.NaN;
                double val = _currentVolumeProfile?.VAL > 0 ? _currentVolumeProfile.VAL : double.NaN;

                // Flow confirmations
                var flow = ComputeImbalanceStats();
                double deltaSlope = ComputeDeltaSlope(Math.Max(5, DeltaWindowBars));
                Func<bool, bool> flowOk = isLong =>
                {
                    bool ok = true;
                    if (HtfRequireImbSlopeFlip) ok &= isLong ? (flow.slope > 0) : (flow.slope < 0);
                    if (HtfRequireDeltaSignFlip) ok &= isLong ? (deltaSlope > 0) : (deltaSlope < 0);
                    return ok;
                };

                // Variant B: Value Area boundaries false break
                // Long: false break below VAL and back above VAL
                if (!double.IsNaN(val))
                {
                    bool touchedBelow = last.Low < (val - buf);
                    bool backAbove = price >= val;
                    if (touchedBelow && backAbove && flowOk(true))
                    {
                        tradeType = TradeType.Buy;
                        return true;
                    }
                }
                // Short: false break above VAH and back below VAH
                if (!double.IsNaN(vah))
                {
                    bool touchedAbove = last.High > (vah + buf);
                    bool backBelow = price <= vah;
                    if (touchedAbove && backBelow && flowOk(false))
                    {
                        tradeType = TradeType.Sell;
                        return true;
                    }
                }

                // Strong anchor touch/false break
                if (_liquidityAnchors != null && _liquidityAnchors.Count > 0)
                {
                    var strongBelow = _liquidityAnchors
                        .Where(a => a.IsStrong && a.Price <= price)
                        .OrderByDescending(a => a.Price)
                        .FirstOrDefault();
                    var strongAbove = _liquidityAnchors
                        .Where(a => a.IsStrong && a.Price >= price)
                        .OrderBy(a => a.Price)
                        .FirstOrDefault();

                    if (strongBelow != null)
                    {
                        bool touched = last.Low < (strongBelow.Price - buf);
                        bool back = price >= strongBelow.Price;
                        if (touched && back && flowOk(true))
                        {
                            tradeType = TradeType.Buy;
                            return true;
                        }
                    }

                    if (strongAbove != null)
                    {
                        bool touched = last.High > (strongAbove.Price + buf);
                        bool back = price <= strongAbove.Price;
                        if (touched && back && flowOk(false))
                        {
                            tradeType = TradeType.Sell;
                            return true;
                        }
                    }
                }
            }
            catch { }
            return false;
        }

        private bool ShouldHardExit(Position pos, out string reason)
        {
            reason = string.Empty;
            try
            {
                if (!UseHardExitRules) return false;
                double atr = Math.Max(_atr?.Result.LastValue ?? 0, Symbol.PipSize * 2);
                double price = (Symbol.Bid + Symbol.Ask) * 0.5;
                var vah = _currentVolumeProfile?.VAH > 0 ? _currentVolumeProfile.VAH : double.NaN;
                var val = _currentVolumeProfile?.VAL > 0 ? _currentVolumeProfile.VAL : double.NaN;
                var flow = ComputeImbalanceStats();
                double deltaSlope = ComputeDeltaSlope(Math.Max(5, DeltaWindowBars));

                if (pos.TradeType == TradeType.Buy)
                {
                    bool belowVAL = !double.IsNaN(val) && price < (val - atr * HardExitAtrMult);
                    bool flowAgainst = (flow.mean < -5 && flow.slope < 0) || deltaSlope < 0;
                    if (belowVAL && flowAgainst)
                    {
                        reason = $"HardExit LONG: price<{(val - atr * HardExitAtrMult):F5}, flow {flow.mean:F1}/{flow.slope:F1}, dSlope {deltaSlope:F1}";
                        return true;
                    }
                }
                else
                {
                    bool aboveVAH = !double.IsNaN(vah) && price > (vah + atr * HardExitAtrMult);
                    bool flowAgainst = (flow.mean > 5 && flow.slope > 0) || deltaSlope > 0;
                    if (aboveVAH && flowAgainst)
                    {
                        reason = $"HardExit SHORT: price>{(vah + atr * HardExitAtrMult):F5}, flow {flow.mean:F1}/{flow.slope:F1}, dSlope {deltaSlope:F1}";
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }

        private double ScoreRecoveryAdvanced(Position pos)
        {
            try
            {
                double price = (Symbol.Bid + Symbol.Ask) * 0.5;
                double atr = Math.Max(_atr?.Result.LastValue ?? 0, Symbol.PipSize * 2);
                int sign = pos.TradeType == TradeType.Buy ? 1 : -1;

                // 1) TF score
                double tfRaw = 0.0;
                Func<TrendDirection, int> dirToSign = d => d == TrendDirection.Bullish ? 1 : (d == TrendDirection.Bearish ? -1 : 0);
                double tfDen = 0.0;
                if (_higherTFAnalysis != null && _higherTFAnalysis.IsValid) { tfRaw += 0.8 * sign * dirToSign(_higherTFAnalysis.Trend); tfDen += 0.8; }
                if (_lowerTFAnalysis != null && _lowerTFAnalysis.IsValid) { tfRaw += 0.6 * sign * dirToSign(_lowerTFAnalysis.Trend); tfDen += 0.6; }
                double tfScore = tfDen > 0 ? 0.5 + Clamp(tfRaw / tfDen, -1, 1) * 0.5 : 0.5;

                // 2) Flow score (L2 + delta)
                var (imbMean, imbSlope) = ComputeImbalanceStats();
                double deltaSlope = ComputeDeltaSlope(Math.Max(5, DeltaWindowBars));
                double flowSupport = 0.0;
                flowSupport += sign * (imbMean / 100.0);               // [-1..1]
                flowSupport += 0.5 * sign * (imbSlope / 50.0);         // rough normalization
                flowSupport += 0.5 * sign * Math.Sign(deltaSlope);     // direction of delta
                flowSupport = Clamp(flowSupport, -1, 1);
                double flowScore = 0.5 + flowSupport * 0.5;

                // 3) Profile score (location vs VA/POC)
                double vah = _currentVolumeProfile?.VAH > 0 ? _currentVolumeProfile.VAH : double.NaN;
                double val = _currentVolumeProfile?.VAL > 0 ? _currentVolumeProfile.VAL : double.NaN;
                double poc = _currentVolumeProfile?.POC > 0 ? _currentVolumeProfile.POC : double.NaN;
                double profScore = 0.5;
                if (pos.TradeType == TradeType.Buy)
                {
                    if (!double.IsNaN(val) && price < val) profScore = 0.1;
                    else if (!double.IsNaN(poc) && price < poc) profScore = 0.4;
                    else if (!double.IsNaN(vah) && price < vah) profScore = 0.7;
                    else profScore = 0.5;
                }
                else
                {
                    if (!double.IsNaN(vah) && price > vah) profScore = 0.1;
                    else if (!double.IsNaN(poc) && price > poc) profScore = 0.4;
                    else if (!double.IsNaN(val) && price > val) profScore = 0.7;
                    else profScore = 0.5;
                }

                // 4) Anchors score
                var below = FindNearestAnchorBelow(price);
                var above = FindNearestAnchorAbove(price);
                double aThr = AcceptAnchorAtr * atr;
                double ancScore = 0.5;
                if (pos.TradeType == TradeType.Buy)
                {
                    bool sup = below.HasValue && Math.Abs(price - below.Value) <= aThr;
                    bool res = above.HasValue && Math.Abs(above.Value - price) <= aThr;
                    if (sup && !res) ancScore = 0.9; else if (!sup && res) ancScore = 0.2; else ancScore = 0.5;
                }
                else
                {
                    bool good = above.HasValue && Math.Abs(above.Value - price) <= aThr;   // resistance above helps shorts
                    bool bad = below.HasValue && Math.Abs(price - below.Value) <= aThr;     // support below hurts shorts
                    if (good && !bad) ancScore = 0.9; else if (!good && bad) ancScore = 0.2; else ancScore = 0.5;
                }

                // 5) Vol/Time score
                double minutesInTrade = Math.Max(0.0, (DateTime.Now - pos.EntryTime).TotalMinutes);
                double decay = Math.Min(1.0, MaxMinutesInTradeBeforeDecay > 0 ? (minutesInTrade / MaxMinutesInTradeBeforeDecay) : 0.0);
                double volTimeScore = 1.0 - 0.7 * decay; // 1..0.3
                volTimeScore = Clamp(volTimeScore, 0.3, 1.0);

                // 6) Expectancy score (distance to targets vs invalidation)
                double eUp, eDn;
                if (pos.TradeType == TradeType.Buy)
                {
                    double up1 = (!double.IsNaN(vah) && vah > price) ? (vah - price) : double.PositiveInfinity;
                    double up2 = above.HasValue ? (above.Value - price) : double.PositiveInfinity;
                    double up3 = GetRecentHighAbove(price, SwingLookback) ?? double.PositiveInfinity;
                    if (!double.IsInfinity(up3)) up3 = up3 - price; else { }
                    eUp = new List<double> { up1, up2, up3, 2 * atr }.Where(x => !double.IsInfinity(x) && x > 0).DefaultIfEmpty(atr).Min();
                    double dn1 = (!double.IsNaN(val) && val < price) ? (price - val) : double.PositiveInfinity;
                    double dn2 = below.HasValue ? (price - below.Value) : double.PositiveInfinity;
                    double dn3 = GetRecentLowBelow(price, SwingLookback) ?? double.PositiveInfinity;
                    if (!double.IsInfinity(dn3)) dn3 = price - dn3;
                    eDn = new List<double> { dn1, dn2, dn3, 2 * atr }.Where(x => !double.IsInfinity(x) && x > 0).DefaultIfEmpty(atr).Min();
                }
                else
                {
                    double up1 = (!double.IsNaN(val) && val < price) ? (price - val) : double.PositiveInfinity; // unfavorable up for shorts
                    double up2 = below.HasValue ? (price - below.Value) : double.PositiveInfinity;
                    double up3v = GetRecentLowBelow(price, SwingLookback) ?? double.PositiveInfinity;
                    if (!double.IsInfinity(up3v)) up3v = price - up3v;
                    eDn = new List<double> { (!double.IsNaN(vah) && vah > price) ? (vah - price) : double.PositiveInfinity,
                                              above.HasValue ? (above.Value - price) : double.PositiveInfinity,
                                              (GetRecentHighAbove(price, SwingLookback) ?? double.PositiveInfinity) - price,
                                              2 * atr }
                                              .Where(x => !double.IsInfinity(x) && x > 0).DefaultIfEmpty(atr).Min();
                    eUp = new List<double> { up1, up2, up3v, 2 * atr }.Where(x => !double.IsInfinity(x) && x > 0).DefaultIfEmpty(atr).Min();
                }
                double expect = (eUp + eDn) > 0 ? Clamp(eUp / (eUp + eDn), 0.0, 1.0) : 0.5;

                // Combine with weights (normalize weights sum)
                double wSum = W_TF + W_Flow + W_Profile + W_Anchors + W_VolTime + W_Expect;
                if (wSum <= 0) wSum = 1.0;
                double prob = (W_TF * tfScore + W_Flow * flowScore + W_Profile * profScore + W_Anchors * ancScore + W_VolTime * volTimeScore + W_Expect * expect) / wSum;
                if (double.IsNaN(prob)) prob = 0.5;
                return Clamp(prob, 0.0, 1.0);
            }
            catch { return 0.5; }
        }

        private void MitigateDrawdown()
        {
            try
            {
                LogToFile(LogLevel.Info, "=== НАЧАЛО МИТИГАЦИИ ПРОСАДКИ ===");
                Print($"🚨 ЗАПУСК МИТИГАЦИИ: Balance={Account.Balance:C}, Equity={Account.Equity:C}");
                var losing = Positions.Where(p => p.SymbolName == SymbolName && p.NetProfit < 0).ToList();
                if (!losing.Any())
                {
                    LogToFile(LogLevel.Info, "Нет убыточных позиций для митигации");
                    return;
                }
                LogToFile(LogLevel.Info, $"Найдено убыточных позиций: {losing.Count}, общий убыток: {losing.Sum(p => p.NetProfit):C}");
                Print($"📊 Митигация: {losing.Count} убыточных позиций, убыток: {losing.Sum(p => p.NetProfit):C}");

                int fully = 0, partially = 0, kept = 0, hard = 0;
                foreach (var pos in losing)
                {
                    // Hard exit check
                    if (ShouldHardExit(pos, out var hardReason))
                    {
                        ClosePosition(pos);
                        fully++;
                        hard++;
                        Print($"🚨 HardExit: {hardReason}");
                        continue;
                    }

                    double prob = ScoreRecoveryAdvanced(pos);
                    if (prob >= 0.75)
                    {
                        kept++;
                        continue; // держим
                    }

                    if (prob >= 0.55)
                    {
                        // micro/optional partial if trade is very old
                        double minutesInTrade = Math.Max(0.0, (DateTime.Now - pos.EntryTime).TotalMinutes);
                        if (minutesInTrade >= MaxMinutesInTradeBeforeDecay)
                        {
                            double n = Clamp((0.55 - prob) / 0.20, 0.0, 1.0);
                            double fraction = MitigationStepMinFraction + n * (MitigationStepMaxFraction - MitigationStepMinFraction);
                            fraction = Clamp(fraction, 0.0, 1.0);
                            if (fraction > 0)
                            {
                                double volumeToClose = pos.VolumeInUnits * fraction;
                                ClosePosition(pos, volumeToClose);
                                partially++;
                            }
                        }
                        else kept++;
                        continue;
                    }

                    if (prob >= 0.35)
                    {
                        double n = Clamp((0.55 - prob) / 0.20, 0.0, 1.0); // 0..1 as prob goes 0.55->0.35
                        double fraction = MitigationStepMinFraction + n * (MitigationStepMaxFraction - MitigationStepMinFraction);
                        fraction = Clamp(fraction, 0.1, 1.0);
                        double volumeToClose = pos.VolumeInUnits * fraction;
                        ClosePosition(pos, volumeToClose);
                        partially++;
                    }
                    else
                    {
                        ClosePosition(pos);
                        fully++;
                    }
                }
                _currentStrategy = $"Митигируем просадку: полных={fully}, частичных={partially}, удержано={kept}{(hard>0 ? ", hardExit=" + hard : "")}";
            }
            catch (Exception ex)
            {
                Print($"Ошибка в MitigateDrawdown: {ex.Message}");
            }
        }

        private double ScoreLossRecoveryProbability(Position pos)
        {
            try
            {
                double score = 0.0;

                // Trend support (HTF + LTF)
                Func<TrendDirection, int> dirToSign = d => d == TrendDirection.Bullish ? 1 : (d == TrendDirection.Bearish ? -1 : 0);
                int sign = pos.TradeType == TradeType.Buy ? 1 : -1;
                if (_higherTFAnalysis != null && _higherTFAnalysis.IsValid)
                    score += 0.8 * sign * dirToSign(_higherTFAnalysis.Trend);
                if (_lowerTFAnalysis != null && _lowerTFAnalysis.IsValid)
                    score += 0.6 * sign * dirToSign(_lowerTFAnalysis.Trend);

                // Volume profile location
                double price = (Symbol.Bid + Symbol.Ask) * 0.5;
                if (_currentVolumeProfile != null && _currentVolumeProfile.VAL > 0 && _currentVolumeProfile.VAH > 0)
                {
                    if (pos.TradeType == TradeType.Buy)
                    {
                        if (price < _currentVolumeProfile.VAL) score -= 0.7; // ниже value area
                        else if (price > _currentVolumeProfile.POC) score += 0.3;
                    }
                    else
                    {
                        if (price > _currentVolumeProfile.VAH) score -= 0.7;
                        else if (price < _currentVolumeProfile.POC) score += 0.3;
                    }
                }

                // Order book imbalance
                if (_orderBookImbalance != 0)
                {
                    double signImb = _orderBookImbalance > 0 ? -1 : 1; // our metric: >0 means sell pressure earlier? adjust to behavior
                    // In our earlier definition: positive -> bids > asks -> скорее поддержка покупок
                    signImb = _orderBookImbalance > 0 ? 1 : -1;
                    score += 0.4 * sign * signImb;
                }

                // Nearby supportive anchor within ATR
                double atr = Math.Max(_atr?.Result.LastValue ?? 0, Symbol.PipSize * 2);
                var below = FindNearestAnchorBelow(price);
                var above = FindNearestAnchorAbove(price);
                if (pos.TradeType == TradeType.Buy && below.HasValue && Math.Abs(price - below.Value) <= atr)
                    score += 0.3;
                if (pos.TradeType == TradeType.Sell && above.HasValue && Math.Abs(above.Value - price) <= atr)
                    score += 0.3;

                // normalize: map roughly from [-3, +3] -> [0,1]
                double prob = 0.5 + Math.Max(-3.0, Math.Min(3.0, score)) / 6.0;
                if (double.IsNaN(prob)) prob = 0.5;
                return Math.Max(0.0, Math.Min(1.0, prob));
            }
            catch { return 0.5; }
        }

        private void CloseAllPositionsAndStop(string reason)
        {
            Print($"🚨 {reason}");
            CancelAllFarmerOrders();

            foreach (var pos in Positions.Where(p => p.SymbolName == SymbolName))
            {
                ClosePosition(pos);
            }

            // Do not stop the cBot; enter a safe pause instead
            _tradingPaused = true;
            _pauseReason = reason;
            _currentStrategy = $"PAUSE: {reason}";
        }

        private void CancelAllFarmerOrders()
        {
            foreach (var order in PendingOrders.Where(o => o.Label == "Farmer" && o.SymbolName == SymbolName))
            {
                CancelPendingOrder(order);
            }
        }

        private void DrawTeamDashboard()
        {
            var text = new StringBuilder();
            text.AppendLine($"=== 🚀 OLYMPIAN QUANTUM TEAM v3.0 ===");
            text.AppendLine($"Рынок: {_currentSettings.MarketName}");
            text.AppendLine($"Общая экспозиция: {_teamTotalExposure:F2}/{MaxTotalExposureLots:F2}");
            text.AppendLine($"TrapGrid: {(UseTrapGrid ? $"ON (Reversion={(EnableTrapReversion ? "Y" : "N")}, Breakout={(EnableTrapBreakout ? "Y" : "N")})" : "OFF")}");
            text.AppendLine($"Targets: {(UseDynamicTargets ? "Dynamic" : $"Fixed TP={FixedTakeProfitPips}, SL={FixedStopLossPips}")}");
            // Дисбаланс
            text.AppendLine($"=== ⚖️ ДИСБАЛАНС L2 ===");
            text.AppendLine($"Значение: {_orderBookImbalance:F1}%");
            text.AppendLine($"Порог охотника: ±{GetEffectiveHunterThreshold()}%");
            // Дисбаланс
            text.AppendLine($"=== ⚖️ ДИСБАЛАНС L2 ===");
            text.AppendLine($"Значение: {_orderBookImbalance:F1}%");
            text.AppendLine($"Порог охотника: ±{GetEffectiveHunterThreshold()}%");
            // Дисбаланс
            text.AppendLine($"=== ⚖️ ДИСБАЛАНС L2 ===");
            text.AppendLine($"Значение: {_orderBookImbalance:F1}%");
            text.AppendLine($"Порог охотника: ±{GetEffectiveHunterThreshold()}%");
            text.AppendLine($"");

            // Статус команды
            text.AppendLine($"=== 👥 СТАТУС КОМАНДЫ ===");
            if (_farmer != null)
                text.AppendLine($"🌾 Фермер: {_farmer.Status} - {_farmer.StatusMessage}");
            if (_hunter != null)
                text.AppendLine($"🎯 Охотник: {_hunter.Status} - {_hunter.StatusMessage}");
            if (_scalper != null)
                text.AppendLine($"⚡ Скальпер: {_scalper.Status} - {_scalper.StatusMessage}");
            text.AppendLine($"");

            // Мульти-таймфрейм анализ
            if (EnableMultiTimeFrameAnalysis)
            {
                text.AppendLine($"=== 🧠 МУЛЬТИ-ТФ АНАЛИЗ ===");
                text.AppendLine($"{_higherTFAnalysis.Name}: {_higherTFAnalysis.Trend} ({_higherTFAnalysis.PricePosition})");
                text.AppendLine($"{_lowerTFAnalysis.Name}: {_lowerTFAnalysis.Trend} ({_lowerTFAnalysis.PricePosition})");

                var conflict = DetermineTimeFrameConflict();
                text.AppendLine($"Конфликт: {conflict}");
                text.AppendLine($"");
            }

            // L2 анализ
            text.AppendLine($"=== 🕵️ ДЕТЕКТИВ ЛИКВИДНОСТИ ===");
            text.AppendLine($"Кластеров отслеживается: {_trackedClusters.Count}");
            if (_trackedClusters.Any())
            {
                var building = _trackedClusters.Values.Count(c => c.Behavior == LiquidityBehavior.Building);
                var weakening = _trackedClusters.Values.Count(c => c.Behavior == LiquidityBehavior.Weakening);
                var icebergs = _trackedClusters.Values.Count(c => c.Behavior == LiquidityBehavior.Iceberg);

                text.AppendLine($"Наращивание: {building} | Ослабление: {weakening}");
                text.AppendLine($"Айсбергов: {icebergs}");
            }
            text.AppendLine($"");

            // Дисбаланс
            text.AppendLine($"=== ⚖️ ДИСБАЛАНС L2 ===");
            text.AppendLine($"Значение: {_orderBookImbalance:F1}%");
            text.AppendLine($"Порог охотника: ±{GetEffectiveHunterThreshold()}%");
            text.AppendLine($"");

            // Позиции и статистика
            text.AppendLine($"=== 💼 ПОЗИЦИИ ===");
            var symbolPositions = Positions.Where(p => p.SymbolName == SymbolName);
            text.AppendLine($"Открыто: {symbolPositions.Count()}");

            if (symbolPositions.Any())
            {
                text.AppendLine($"P/L: {symbolPositions.Sum(p => p.NetProfit):C}");

                var farmerPnL = symbolPositions.Where(p => p.Label == "Farmer").Sum(p => p.NetProfit);
                var scalpPnL = symbolPositions.Where(p => p.Label == "Scalper").Sum(p => p.NetProfit);
                var hunterPnL = symbolPositions.Where(p => p.Label == "Hunter").Sum(p => p.NetProfit);

                if (farmerPnL != 0) text.AppendLine($"🌾 Фермер: {farmerPnL:C}");
                if (scalpPnL != 0) text.AppendLine($"⚡ Скальпер: {scalpPnL:C}");
                if (hunterPnL != 0) text.AppendLine($"🎯 Охотник: {hunterPnL:C}");
            }

            text.AppendLine($"Pending: {PendingOrders.Count(o => o.SymbolName == SymbolName)}");

            Chart.DrawText("TeamDashboard", text.ToString(), Chart.LastVisibleBarIndex, Chart.TopY, Color.Gold);
        }

        private void DrawQuantumDashboard()
        {
            var text = new StringBuilder();
            text.AppendLine($"=== 🚀 OLYMPIAN QUANTUM TRADER v3.0 ===");
            text.AppendLine($"Рынок: {_currentSettings.MarketName}");
            text.AppendLine($"Режим: {_currentStrategy}");
            text.AppendLine($"Targets: {(UseDynamicTargets ? "Dynamic" : $"Fixed TP={FixedTakeProfitPips}, SL={FixedStopLossPips}")}");
            text.AppendLine($"TrapGrid: {(UseTrapGrid ? $"ON (Reversion={(EnableTrapReversion ? "Y" : "N")}, Breakout={(EnableTrapBreakout ? "Y" : "N")})" : "OFF")}");
            text.AppendLine($"");
            text.AppendLine($"=== 🎯 КЛЮЧЕВЫЕ УРОВНИ ===");
            var poc = _currentVolumeProfile?.POC > 0 ? _currentVolumeProfile.POC.ToString("F5") : "n/a";
            var vah = _currentVolumeProfile?.VAH > 0 ? _currentVolumeProfile.VAH.ToString("F5") : "n/a";
            var val = _currentVolumeProfile?.VAL > 0 ? _currentVolumeProfile.VAL.ToString("F5") : "n/a";
            var aAbove = FindNearestAnchorAbove(Symbol.Bid);
            var aBelow = FindNearestAnchorBelow(Symbol.Bid);
            text.AppendLine($"POC: {poc} | VAH: {vah} | VAL: {val}");
            text.AppendLine($"Anchor↑: {(aAbove.HasValue ? aAbove.Value.ToString("F5") : "n/a")} | Anchor↓: {(aBelow.HasValue ? aBelow.Value.ToString("F5") : "n/a")}");

            // Мульти-таймфрейм статус
            if (EnableMultiTimeFrameAnalysis)
            {
                text.AppendLine($"=== 🧠 МУЛЬТИ-ТФ АНАЛИЗ ===");
                text.AppendLine($"{_higherTFAnalysis.Name}: {_higherTFAnalysis.Trend} ({_higherTFAnalysis.PricePosition})");
                text.AppendLine($"{_lowerTFAnalysis.Name}: {_lowerTFAnalysis.Trend} ({_lowerTFAnalysis.PricePosition})");

                var conflict = DetermineTimeFrameConflict();
                text.AppendLine($"Конфликт: {conflict}");
                text.AppendLine($"");
            }

            // L2 анализ
            text.AppendLine($"=== 🕵️ ДЕТЕКТИВ ЛИКВИДНОСТИ ===");
            text.AppendLine($"Кластеров отслеживается: {_trackedClusters.Count}");
            if (_trackedClusters.Any())
            {
                var building = _trackedClusters.Values.Count(c => c.Behavior == LiquidityBehavior.Building);
                var weakening = _trackedClusters.Values.Count(c => c.Behavior == LiquidityBehavior.Weakening);
                var icebergs = _trackedClusters.Values.Count(c => c.Behavior == LiquidityBehavior.Iceberg);

                text.AppendLine($"Наращивание: {building} | Ослабление: {weakening}");
                text.AppendLine($"Айсбергов: {icebergs}");
            }
            text.AppendLine($"");

            // Скальпинг статус
            if (EnableConflictScalping)
            {
                text.AppendLine($"=== ⚡ СКАЛЬПИНГ КОНФЛИКТОВ ===");
                text.AppendLine($"Ловушка активна: {(_scalpingTrapActive ? "ДА" : "НЕТ")}");
                if (_scalpingTrapActive)
                {
                    var timeActive = (DateTime.Now - _scalpingTrapTime).TotalMinutes;
                    text.AppendLine($"Время активности: {timeActive:F1} мин");
                    text.AppendLine($"Центр ловушки: {_scalpingTrapCenter:F5}");
                }
                text.AppendLine($"");
            }

            // Основная сетка
            text.AppendLine($"=== 🌾 ОСНОВНАЯ СЕТКА ===");
            text.AppendLine($"Ордеров: {_currentSettings.OrdersCount * 2}");
            text.AppendLine($"Шаг: {_currentSettings.StepPips}");
            text.AppendLine($"TP/SL: Dynamic (Profile/L2/ATR/Trend)");

            if (_detectedMarket == MarketType.Crypto)
            {
                text.AppendLine($"Спред: ${Symbol.Spread:F2} (лимит: ${_currentSettings.MaxSpreadPips:F2})");
            }
            else
            {
                text.AppendLine($"Спред: {Symbol.Spread / Symbol.PipSize:F2} пипсов");
            }
            text.AppendLine($"");
            // Дисбаланс
            text.AppendLine($"=== ⚖️ ДИСБАЛАНС L2 ===");
            text.AppendLine($"Значение: {_orderBookImbalance:F1}%");
            text.AppendLine($"Порог охотника: ±{GetEffectiveHunterThreshold()}%");
            text.AppendLine($"");

            // Позиции и статистика
            text.AppendLine($"=== 💼 ПОЗИЦИИ ===");
            var symbolPositions = Positions.Where(p => p.SymbolName == SymbolName);
            text.AppendLine($"Открыто: {symbolPositions.Count()}");

            if (symbolPositions.Any())
            {
                text.AppendLine($"P/L: {symbolPositions.Sum(p => p.NetProfit):C}");

                var farmerPnL = symbolPositions.Where(p => p.Label == "Farmer").Sum(p => p.NetProfit);
                var scalpPnL = symbolPositions.Where(p => p.Label == "Scalper").Sum(p => p.NetProfit);
                var hunterPnL = symbolPositions.Where(p => p.Label == "Hunter").Sum(p => p.NetProfit);

                if (farmerPnL != 0) text.AppendLine($"🌾 Фермер: {farmerPnL:C}");
                if (scalpPnL != 0) text.AppendLine($"⚡ Скальп: {scalpPnL:C}");
                if (hunterPnL != 0) text.AppendLine($"🎯 Охотник: {hunterPnL:C}");
            }

            text.AppendLine($"Pending: {PendingOrders.Count(o => o.SymbolName == SymbolName)}");

            Chart.DrawText("QuantumDashboard", text.ToString(), Chart.LastVisibleBarIndex, Chart.TopY, Color.Cyan);
        }

        private void CheckNetProfitAndCloseIfNeeded()
        {
            try
            {
                // Throttle by combined interval (minutes + seconds). Either can be 0.
                var now = DateTime.Now;
                int mins = Math.Max(0, AutoCloseCheckIntervalMinutes);
                int secs = Math.Max(0, AutoCloseCheckIntervalSeconds);
                int totalSeconds = mins * 60 + secs;
                if (totalSeconds < 1) totalSeconds = 1; // ensure at least 1s
                if ((now - _lastNetProfitCheck).TotalSeconds < totalSeconds)
                    return;
                _lastNetProfitCheck = now;

                // Reset daily counters if needed
                ResetDailyIfNeeded();

                var openPositions = Positions.Where(p => p.SymbolName == SymbolName).ToList();
                if (!openPositions.Any())
                    return;
                double netProfit = openPositions.Sum(p => p.NetProfit);
                // Track peak for trailing
                if (netProfit > _netProfitPeak)
                    _netProfitPeak = netProfit;
                // Track trough for loss-trailing
                if (netProfit < _netLossTrough)
                    _netLossTrough = netProfit;

                // 1) Percent-based sum
                if (AutoCloseOnNetProfit)
                {
                    double thresholdPct = Account.Balance * (ProfitThresholdPercentOfBalance / 100.0);
                    if (thresholdPct > 0 && netProfit > thresholdPct)
                    {
                        CloseByAggregate(openPositions, netProfit, $"> {thresholdPct:C}");
                        return;
                    }
                }

                // 2) Absolute sum in account currency (both sides)
                if (AutoCloseOnNetProfitAbs)
                {
                    bool hitProfit = NetProfitAbsTarget > 0 && netProfit >= NetProfitAbsTarget;
                    bool hitLoss = NetLossAbsTarget > 0 && netProfit < 0 && Math.Abs(netProfit) >= NetLossAbsTarget;
                    LogToFile(LogLevel.Debug, $"Проверка абс. порогов: netProfit={netProfit:F2}, hitProfit={hitProfit}, hitLoss={hitLoss}");
                    if (hitProfit || hitLoss)
                    {
                        string side = hitProfit ? $">= {NetProfitAbsTarget:C}" : $"убыток >= {NetLossAbsTarget:C}";
                        CloseByAggregate(openPositions, netProfit, side);
                        return;
                    }
                }

                // 3) Per-position absolute thresholds
                if (AutoClosePerPosition)
                {
                    int closed = 0;
                    foreach (var pos in openPositions)
                    {
                        if (PerPositionProfitTargetCurrency > 0 && pos.NetProfit >= PerPositionProfitTargetCurrency)
                        {
                            ClosePosition(pos); closed++;
                            continue;
                        }
                        if (PerPositionLossTargetCurrency > 0 && pos.NetProfit <= -PerPositionLossTargetCurrency)
                        {
                            ClosePosition(pos); closed++;
                        }
                    }
                    if (closed > 0)
                        Print($"✅ Персделочное автозакрытие: закрыто {closed} позиций по порогам {PerPositionProfitTargetCurrency:C} / -{PerPositionLossTargetCurrency:C}");
                }

                // 4) Daily targets
                if (DailyTargetsEnable)
                {
                    double baseProfit = DailyUseOpenEquity ? (_realizedToday + netProfit) : _realizedToday;
                    double dailyProfitCcy = Account.Balance * (DailyProfitTargetPercentOfBalance / 100.0);
                    double dailyLossCcy = Account.Balance * (DailyLossLimitPercentOfBalance / 100.0);

                    bool hitProfit = DailyProfitTargetPercentOfBalance > 0 && baseProfit >= dailyProfitCcy;
                    bool hitLoss = DailyLossLimitPercentOfBalance > 0 && baseProfit <= -dailyLossCcy;
                    if (hitProfit || hitLoss)
                    {
                        if (DailyClosePositionsOnHit)
                        {
                            CloseByAggregate(openPositions, netProfit, hitProfit ? $"Дневная прибыль >= {dailyProfitCcy:C}" : $"Дневной убыток <= -{dailyLossCcy:C}");
                        }
                        if (DailyPauseOnHit)
                        {
                            _pausedByDaily = true;
                            CancelAllPendingOrdersForSymbol();
                            _currentStrategy = hitProfit ? "PAUSE: дневная цель по прибыли" : "PAUSE: дневной лимит убытка";
                        }
                        return;
                    }
                }

                // 5) Net Profit Trailing lock
                // Effective thresholds: prefer currency, fallback to % of balance
                double effNetTrailStart = NetTrailStartCurrency > 0 ? NetTrailStartCurrency : (Account.Balance * (NetTrailStartPercentOfBalance / 100.0));
                double effNetTrailStep = NetTrailStepCurrency > 0 ? NetTrailStepCurrency : (Account.Balance * (NetTrailStepPercentOfBalance / 100.0));
                if (AutoCloseNetProfitTrailing && effNetTrailStep > 0 && NetTrailLockFraction > 0)
                {
                    if (netProfit >= effNetTrailStart && (_netProfitPeak - netProfit) >= effNetTrailStep)
                    {
                        var baseList = NetTrailOnlyProfitablePositions ?
                            openPositions.Where(p => p.NetProfit > 0) :
                            openPositions;
                        if (ExcludeHunterFromAutoClose)
                            baseList = baseList.Where(p => p.Label != "Hunter");
                        var actList = baseList.OrderByDescending(p => p.NetProfit).ToList();
                        if (NetTrailMaxPositionsPerStep > 0)
                            actList = actList.Take(NetTrailMaxPositionsPerStep).ToList();

                        int closed = 0; double frac = Math.Max(0.0, Math.Min(1.0, NetTrailLockFraction));
                        foreach (var pos in actList)
                        {
                            double volumeToClose = pos.VolumeInUnits * frac;
                            if (volumeToClose <= 0) continue;
                            ClosePosition(pos, volumeToClose);
                            closed++;
                        }
                        Print($"🔒 Тралл Net-прибыли: peak={_netProfitPeak:C} → cur={netProfit:C}. Закрыто частично позиций: {closed} (frac={frac:P0})");
                        _netProfitPeak = netProfit; // reset after lock
                        if (NetTrailPauseAfterLock)
                        {
                            _pausedByDaily = true;
                            CancelAllPendingOrdersForSymbol();
                            _currentStrategy = "PAUSE: после тралла прибыли";
                            return;
                        }
                    }
                }

                // 6) Net Loss Trailing lock (on rebound from worst)
                if (AutoCloseNetLossTrailing && LossTrailStepCurrency > 0 && LossTrailLockFraction > 0)
                {
                    // Activate only if we had at least LossTrailStartCurrency of loss at trough
                    if (_netLossTrough <= -Math.Max(0.0, LossTrailStartCurrency))
                    {
                        double improvement = netProfit - _netLossTrough; // improvement from worst (negative → less negative)
                        if (improvement >= LossTrailStepCurrency && netProfit <= 0)
                        {
                            var baseList = LossTrailOnlyLosingPositions ?
                                openPositions.Where(p => p.NetProfit < 0) :
                                openPositions;
                            if (ExcludeHunterFromAutoClose)
                                baseList = baseList.Where(p => p.Label != "Hunter");
                            var actList = baseList.OrderBy(p => p.NetProfit).ToList();
                            if (LossTrailMaxPositionsPerStep > 0)
                                actList = actList.Take(LossTrailMaxPositionsPerStep).ToList();

                            int closed = 0; double frac = Math.Max(0.0, Math.Min(1.0, LossTrailLockFraction));
                            foreach (var pos in actList)
                            {
                                double volumeToClose = pos.VolumeInUnits * frac;
                                if (volumeToClose <= 0) continue;
                                ClosePosition(pos, volumeToClose);
                                closed++;
                            }
                            Print($"🛡️ Тралл Net-убытка: trough={_netLossTrough:C} → cur={netProfit:C}. Частично закрыто: {closed} (frac={frac:P0})");
                            _netLossTrough = netProfit; // reset after lock to current
                            if (LossTrailPauseAfterLock)
                            {
                                _pausedByLossTrail = true;
                                CancelAllPendingOrdersForSymbol();
                                _currentStrategy = "PAUSE: после тралла убытка";
                                return;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Print($"Ошибка в авто-закрытии прибыли: {ex.Message}");
            }
        }

        private void CloseByAggregate(List<Position> openPositions, double netProfit, string reasonSide)
        {
            var toClose = AutoCloseOnlyProfitablePositions
                ? openPositions.Where(p => p.NetProfit > 0).ToList()
                : openPositions;

            // Exclude Hunter positions from aggregate auto-close if enabled
            if (ExcludeHunterFromAutoClose)
                toClose = toClose.Where(p => p.Label != "Hunter").ToList();

            int closed = 0;
            foreach (var pos in toClose)
            {
                LogToFile(LogLevel.Info, $"AutoClose aggregate: closing posId={pos.Id} label={pos.Label} pnl={pos.NetProfit:C}");
                ClosePosition(pos);
                closed++;
            }

            int canceled = 0;
            if (AutoCloseCancelPendingOrders)
            {
                canceled = CancelAllPendingOrdersForSymbol();
            }

            Print($"✅ Автозакрытие по сумме: Net={netProfit:C} {reasonSide}. Закрыто позиций: {closed}. {(AutoCloseCancelPendingOrders ? ($"Отменено pending: {canceled}.") : "")}");
            LogToFile(LogLevel.Info, $"AutoClose aggregate done: net={netProfit:F2} {reasonSide}, closed={closed}, canceledPend={canceled}");
        }

        private DateTime GetLocalSessionNow()
        {
            var baseTime = UseServerTimeForSession ? Server.Time : DateTime.UtcNow;
            return baseTime.AddHours(SessionTimezoneOffsetHours);
        }

        private void ResetDailyIfNeeded()
        {
            try
            {
                var now = GetLocalSessionNow();
                var dayStart = new DateTime(now.Year, now.Month, now.Day, DailyResetHour, 0, 0);
                if (now < dayStart)
                    dayStart = dayStart.AddDays(-1);

                if (_dailyKey == DateTime.MinValue)
                {
                    _dailyKey = dayStart;
                    return;
                }

                if (dayStart > _dailyKey)
                {
                    // new day
                    _dailyKey = dayStart;
                    _realizedToday = 0.0;
                    _netProfitPeak = 0.0;
                    _netLossTrough = 0.0;
                    _pausedByDaily = false;
                    _pausedByLossTrail = false;
                    Print($"🔄 Сброс дневных счетчиков ({_dailyKey:yyyy-MM-dd HH:mm})");
                }
            }
            catch { }
        }

        private int CancelAllPendingOrdersForSymbol()
        {
            int count = 0;
            foreach (var order in PendingOrders.Where(o => o.SymbolName == SymbolName).ToList())
            {
                CancelPendingOrder(order);
                count++;
            }
            return count;
        }

        protected override void OnStop()
        {
            try { Positions.Closed -= OnPositionsClosed; } catch { }
            LogToFile(LogLevel.Info, "OnStop invoked");

            Print("====== QUANTUM TRADER ОСТАНОВЛЕН ======");
            Print($"[STOP] 🎯 Символ: {SymbolName}");
            Print($"[STOP] 📊 Рынок: {_currentSettings?.MarketName ?? "Неизвестен"}");
            Print($"[STOP] ⏰ Время работы: до {DateTime.Now:HH:mm:ss}");

            var symbolPositions = Positions.Where(p => p.SymbolName == SymbolName);
            var symbolOrders = PendingOrders.Where(o => o.SymbolName == SymbolName);

            Print($"[STOP] 💼 Открытых позиций: {symbolPositions.Count()}");
            Print($"[STOP] 📋 Pending ордеров: {symbolOrders.Count()}");
            Print($"[STOP] 🕵️ Отслеживаемых кластеров: {_trackedClusters.Count}");

            if (EnableParallelTeam)
            {
                Print($"[STOP] 👥 Команда:");
                if (_farmer != null) Print($"[STOP] 🌾 Фермер: {_farmer.ActionsToday} действий");
                if (_hunter != null) Print($"[STOP] 🎯 Охотник: {_hunter.ActionsToday} выстрелов");
                if (_scalper != null) Print($"[STOP] ⚡ Скальпер: {_scalper.ActionsToday} атак");
            }

            if (symbolPositions.Any())
            {
                double totalPnL = symbolPositions.Sum(p => p.NetProfit);
                Print($"[STOP] 💰 Общий P/L: {totalPnL:C}");

                var farmerPositions = symbolPositions.Where(p => p.Label == "Farmer");
                var scalpPositions = symbolPositions.Where(p => p.Label == "Scalper");
                var hunterPositions = symbolPositions.Where(p => p.Label == "Hunter");

                if (farmerPositions.Any())
                    Print($"[STOP] 🌾 Фермер P/L: {farmerPositions.Sum(p => p.NetProfit):C}");
                if (scalpPositions.Any())
                    Print($"[STOP] ⚡ Скальпинг P/L: {scalpPositions.Sum(p => p.NetProfit):C}");
                if (hunterPositions.Any())
                    Print($"[STOP] 🎯 Охотник P/L: {hunterPositions.Sum(p => p.NetProfit):C}");
            }

            Print("==============================");
            CloseLogger();
        }
    }
}
