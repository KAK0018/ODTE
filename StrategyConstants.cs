using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using QX.Base.Common;

namespace QX.Blitz.Strategy.ODTE_Sell
{
    public static class IVNames
    {
        public const string IVSymbolName = "IV";
    }

    public static class StrategyInputParameter
    {
        public const string BenchmarkSpread = "TargetSpread";
        public const string OrderQty = "OrderQty";
        public const string ProfitMargin = "ProfitMargin";
        public const string LossMargin = "LossMargin";
        public const string ScalperLogicType = "ScalperLogicType";
        public const string Rounds = "Rounds";
        public const string BidAtTarget = "BidAtTarget";
        public const string OrderReverseFlagActive = "OrderReverseFlagActive";
        public const string VWAPValue = "VWAPValue";
        public const string VolumeRatioValue = "VolumeRatioValue";
        public const string LTPQueueSizeValue = "LTPQueueSizeValue";
        public const string MarketDataDelayInterval = "MarketDataDelayInterval";
        public const string AStop = "AStop";

        public const string ExitBiddingLogic = "ExitBiddingLogic";
        public const string ExitReferenceOrderModifyTick = "ExitReferenceOrderModifyTick";
        public const string ModifyExitOrderTimeInterval = "ModifyExitOrderTimeInterval";
        public const string ModifyExitOrderToMarketAfterTimeInterval = "ModifyExitOrderToMarketAfterTimeInterval";
    }

    public static class StrategyOutputParameter
    {
        public const string MarketSpread = "MarketSpread";
        public const string ExecutedRounds = "ExecutedRounds";
        public const string CurrentEntrySide = "CurrentEntrySide";
        public const string LastExecutedPrice = "LastExecutedPrice";
        public const string LastExecutedSpread = "LastExecutedSpread";
        public const string CurrentEquityRealizedValue = "CurrentEquityRealizedValue";
        public const string CurrentEquityActualValue = "CurrentEquityActualValue";
    }


    //Custome Order Commands class...

    //[Guid("2949002B-CEC6-4F8B-867D-AFCC4D0C81C1")]
    static class ForceExitCommand
    {
        public static Guid CommandStaticID = new Guid("{2949002B-CEC6-4F8B-867D-AFCC4D0C81C1}");
    }

    
    //[Guid("8CD0E7AF-7D50-462A-A8FD-7F1FF5B5A3CE")]
    static class StopAllCommand
    {
        public static Guid CommandStaticID = new Guid("{8CD0E7AF-7D50-462A-A8FD-7F1FF5B5A3CE}");
    }
    static class NewOrderOnStrikePricePairCommand
    {
        public static Guid CommandStaticID = new Guid("{2694A00E-13D4-4B53-9D5B-9177F69A1FAF}");

        public const string Param_StrikePrice = "StrikePrice";
    }

    static class NewOrderOnATMStrikePricePairCommand
    {
        public static Guid CommandStaticID = new Guid("{B2404204-D1F3-4C35-8E1E-620AEF3FD4BF}");
    }


    static class EnterLongCommand
    {
        public static Guid CommandStaticID = new Guid("{55C5F44C-7023-48C5-9400-C6CAE7527B90}");

        public const string Param_LimitPrice = "LimitPrice";
    }


    static class EnterShortCommand
    {
        public static Guid CommandStaticID = new Guid("{38D44866-36F1-42FC-BD50-4557392BD530}");

        public const string Param_LimitPrice = "LimitPrice";
    }

    static class ExitLongCommand
    {
        public static Guid CommandStaticID = new Guid("{84538011-06E8-4861-870F-0218C0FEABA4}");

        public const string Param_LimitPrice = "LimitPrice";
    }

    static class ExitShortCommand
    {
        public static Guid CommandStaticID = new Guid("{0E9C0707-00D6-4C45-968D-43BE828DD0BC}");

        public const string Param_LimitPrice = "LimitPrice";
    }

    class GoFlatOrderCommand
    {
        public static Guid CommandStaticID = new Guid("{DD9FD936-659A-4FC5-A101-01C0EA344154}");
    }

    public static class PrintStatisticsCommand
    {
        public static Guid CommandStaticID = new Guid("{2183FFC6-F6B9-42F7-A6E5-8E2CD97E6932}");
    }

    
    
    
    
    
    
    public static class TaxValue
    {
        public static double Equity = ((double)1900 / 10000000);
        public static double Futures = ((double)1100 / 10000000);
        public static double Options = ((double)6900 / 10000000);

        public static double Get(InstrumentType instrumentType)
        {
            switch (instrumentType)
            {
                case InstrumentType.Futures:
                    return Futures;
                case InstrumentType.Options:
                    return Options;
                case InstrumentType.Spread:
                    return 0;
                case InstrumentType.Equity:
                    return Equity;
                case InstrumentType.Spot:
                    return 0;
                default:
                    return 0;
            }
        }
    }

    enum ScalperEntryLogicType
    {
        None = 0,
        VWAP = 1,
        VolumeRatio = 2,
        LTPQueuing = 3,
        LTPQueuingMid = 4,
        VWAPAndLTPQueuing = 5,
    }

    public enum ExitBiddingLogic
    {
        AtReferencePrice = 0,
        AtBestPrice = 1,
    }

    public enum varKeys
    {
        intialTransection_key,
        curRefTime_key
    }
}
