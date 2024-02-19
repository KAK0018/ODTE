using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


using QX.Base.Common;
using QX.Base.Common.InstrumentInfo;
using QX.Base.Common.Message;
using QX.Base.Core;
using QX.Base.Data;
using QX.Blitz.Core;
using QX.Blitz.Core.Series;
using QX.Common.Helper;
using QX.Common.Lib;

namespace QX.Blitz.Strategy.OptionsMLX1
{
    public class LegDefination
    {
        public LegDefination(IOrderCommand legOptionsOrderCommand, OrderSide orderSide, OptionType optionType, int lotSize)
        {
            OrderCommand = legOptionsOrderCommand;
            LegOrderSide = orderSide;
            LotSize = lotSize;
            OptionType = optionType;


        }

        public IOrderCommand OrderCommand
        {
            get;
            set;
        }

        public OptionType OptionType
        {
            get;
            set;
        }

        public OrderSide LegOrderSide
        {
            get;
            set;
        }

        public int LotSize
        {
            get;
            set;
        }

        public short spreadNature
        {
            get;
            set;
        }


        public override string ToString()
        {
            return string.Format("LegDefination. Options: {0}, OrderSide: {1}, LotSize: {2}",
                OrderCommand.IVInfo.InstrumentName, LegOrderSide.ToString(), LotSize);
        }
    }

    public class SmartMLegExecutorX2
    {
        private List<LegDefination> _legDefinationList;
        private int MaxLot;
        public bool FirstSellFlag = false;

        private bool _isOrderResettable = false;
        private string _errorText = string.Empty;

        private Action<LogType, string> _logger = null;
        private List<double> _executionSpreadList = new List<double>();

        public SmartMLegExecutorX2(List<LegDefination> legDefinationList, int MaxLot, Action<LogType, string> logger = null)
        {
            _legDefinationList = legDefinationList;
            this.MaxLot = MaxLot;
          
            //_logger = logger;

            foreach (LegDefination legDefination in legDefinationList)
            {
                legDefination.OrderCommand.OnOrderTraded += OnSmartOrderTraded;
                legDefination.OrderCommand.OnOrderRejected += OnSmartOrderRejected;
                legDefination.OrderCommand.OnOrderSendingFailed += OnSmartOrderSendingFailed;
           
            }
            
        }

        //for deregistering the Event and cleaning the current Instance..
        //must called after the execution is done..
        public void Clean()
        { 
            //Basically Cancel First leg Quoting, if Order is Still not closed..
            if (!_legDefinationList[0].OrderCommand.IsCurrentOrderClosed() && !IsOrderResettable)
            {
                //_legDefinationList[0].OrderCommand.CurrentOrder.CanCancel();
                _legDefinationList[0].OrderCommand.Set(false, OrderSide.None, OrderType.Unknown, 0, 0, 0);

            }

            foreach (LegDefination legDefination in _legDefinationList)
            {
                legDefination.OrderCommand.OnOrderTraded -= OnSmartOrderTraded;
                legDefination.OrderCommand.OnOrderRejected -= OnSmartOrderRejected;
                legDefination.OrderCommand.OnOrderSendingFailed -= OnSmartOrderSendingFailed;

                legDefination.OrderCommand.Reset(out _errorText);
            }
        }

        private void LogInfo(LogType logType, string logText)
        {
            if (_logger != null)
                _logger(logType, logText);
           
        }

        public bool IsOrderResettable
        {
            get { return _isOrderResettable; }
        }

        public int CurrentLot
        {
            get { return (_legDefinationList[0].OrderCommand.IVInfo.Statistics.NetPosition / _legDefinationList[0].OrderCommand.IVInfo.LotSize); }
        }

        public short spreadNature
        {
            get;        //1 -for the Buy, //1 -for the Sell,,
            set;
        }
        public int CurrentRound
        {
            get;
            internal set;
        } = 0;

        public double userSpread
        {
            get;
            internal set;
        } = 0;

        public double maxSpreadBound
        {
            get;
            internal set;
        }

        public int getMaxLot()
        {
            return this.MaxLot;
        }

    


        //For Avg. execution spread iof entire qty..
        //Too much of list count ,ccan also turn for the direct averaging..(as qty is unit)
        public double getAvgExecutionSpread()
        {
            
            if (_executionSpreadList.Count == 0)
                return 0;

            //Return Only positive value for every spread..
            //assuming single single round Execution..
            return Math.Abs(Math.Round(_executionSpreadList.Sum()/_executionSpreadList.Count,2));   
            
        }
        
        //for getting Shift+A kind of logger..
        //only fopr straddle Sell..
        public String getExecutionLog()
        {
            String logStr = "";
            
            if(_legDefinationList.Count !=0)
                logStr += _legDefinationList[0].OrderCommand.IVInfo.IVInstrument.Instrument.DisplayName + "-" +
                    _legDefinationList[1].OrderCommand.IVInfo.IVInstrument.Instrument.DisplayName + "->" + getAvgExecutionSpread().ToString() + ":Qty->" + this.CurrentRound.ToString();
            
            return logStr;
        }
        public double LastExecutedSpread
        {
            get
            {
                if (_executionSpreadList.Count() <= 0)
                    return 0;
                else
                    return _executionSpreadList[_executionSpreadList.Count - 1];
            }
        }
        //all this thong can be customized based on the straddles and Two legs..
        //No need to jhavefor loop, can have custom logic..
        public double GetMarketSpread()
        {
            double marketSpread = 0;

            foreach (LegDefination legDefination in _legDefinationList)
            {
                if (legDefination.LegOrderSide == OrderSide.Buy)
                    marketSpread += legDefination.LotSize * legDefination.OrderCommand.IVInfo.MarketDataContainer.BestAskPrice;

                else if (legDefination.LegOrderSide == OrderSide.Sell)
                    marketSpread += legDefination.LotSize * legDefination.OrderCommand.IVInfo.MarketDataContainer.BestBidPrice;

            }
            return (spreadNature * marketSpread);
        }


        public double GetQuotePrice(double userBenchMark)
        {
            double marketSpread = 0;

            //foreach (int LegDefination legDefination in _legDefinationList)
            for (int i = 1; i < _legDefinationList.Count; i++)
            {
                LegDefination legDefination = _legDefinationList[i];

                if (legDefination.LegOrderSide == OrderSide.Buy)
                    marketSpread += legDefination.LotSize * legDefination.OrderCommand.IVInfo.MarketDataContainer.BestAskPrice;

                else if (legDefination.LegOrderSide == OrderSide.Sell)
                    marketSpread += legDefination.LotSize * legDefination.OrderCommand.IVInfo.MarketDataContainer.BestBidPrice;
            }

            double quotePrice = ((((spreadNature) * userBenchMark) - marketSpread)) / (_legDefinationList[0].LotSize);

            return quotePrice;
        }


        //Sort in the Strike Order...
        //assume:  legDefination --all legs will be either CE or PE,
        //No combination of the CE,PE..
        public bool BidAskPricer()
        {
            bool flag = true;
            foreach(LegDefination leg in _legDefinationList)
            {
                    flag  = flag && !((leg.OrderCommand.IVInfo.MarketDataContainer.BestAskPrice - leg.OrderCommand.IVInfo.MarketDataContainer.BestBidPrice) > 2);
            }
            return flag;
        }

        //dont return Double marketspread anymore..
        public void Execute(double maxSpreadBound, double userSpreadBenchMark = 0)
        {
            this.userSpread = userSpreadBenchMark;
            this.maxSpreadBound = maxSpreadBound;

            double marketSpread = GetMarketSpread();
            //double marketSpread = 0;
            bool triggerExecution = false;
            if (spreadNature == -1)
            {
                triggerExecution = (marketSpread >= maxSpreadBound) && Math.Abs(CurrentRound) <= MaxLot;
            }
            else if (spreadNature == 1)
            {
                triggerExecution = (marketSpread <= maxSpreadBound) && Math.Abs(CurrentRound) <= MaxLot;
               
            }


            //Check for the bid/ask imbalance and the liquidity..
           
            if (triggerExecution)
            {
                if ((_legDefinationList[0].OrderCommand.CurrentOrder != null &&
                    _legDefinationList[0].OrderCommand.CurrentOrder.CummulativeQuantity == 0 &&
                    _legDefinationList[0].OrderCommand.IsCurrentOrderClosed()))
                {
                    // Reset the bidding leg in case some order has gone and cancelled due to Trigger execution false
                    _legDefinationList[0].OrderCommand.Reset(out _errorText);
                }

                if (_isOrderResettable && _legDefinationList[0].OrderCommand.CurrentOrder != null)
                {
                    Reset(); //All 
                }

                if (_legDefinationList[0].OrderCommand.CurrentOrder == null ||
                    (_legDefinationList[0].OrderCommand.CurrentOrderID > 0 && _legDefinationList[0].OrderCommand.IsCurrentOrderClosed() == false))
                {
                    double limitPrice = 0;

                    if (userSpreadBenchMark == 0 && BidAskPricer())
                    {
                        // quote the first leg

                        //What if, We make other side + tick size, it will fasten up execution,
                        //as we any way calculating the spread based on the fist bid/ask..
                        //limitPrice = _legDefinationList[0].OrderCommand.IVInfo.MarketDataContainer.GetBestBiddingPrice(_legDefinationList[0].LegOrderSide);

                        //for the checking the Best bidding..
                       // limitPrice = _legDefinationList[0].OrderCommand.IVInfo.MarketDataContainer.GetBestBiddingPrice(_legDefinationList[0].LegOrderSide);
                        
                        if (_legDefinationList[0].LegOrderSide == OrderSide.Buy)
                            limitPrice = _legDefinationList[0].OrderCommand.IVInfo.MarketDataContainer.BestAskPrice + 0.05;
                        else
                            limitPrice = _legDefinationList[0].OrderCommand.IVInfo.MarketDataContainer.BestBidPrice - 0.05;
                    
                    }

                    else
                    {
                        // quote the first leg


                        //How to check  userBchMark spread is uptodate with the userSpread..???
                        limitPrice = GetQuotePrice(userSpreadBenchMark);
                        if (limitPrice <= 0)
                            return;
                    }


                    _isOrderResettable = false;
                    _legDefinationList[0].OrderCommand.Set(true,
                        _legDefinationList[0].LegOrderSide,
                        OrderType.Limit,
                       Math.Abs(_legDefinationList[0].LotSize) * _legDefinationList[0].OrderCommand.IVInfo.LotSize,
                       limitPrice, 0);

                    return;
                }
            }
            else
            {
                // spread is not in favour
                if (_legDefinationList[0].OrderCommand.CurrentOrder != null &&
                    _legDefinationList[0].OrderCommand.CurrentOrderID > 0 &&
                    _legDefinationList[0].OrderCommand.IsCurrentOrderClosed() == false &&
                    _legDefinationList[0].OrderCommand.TotalTradedQuantity == 0)
                {
                    _legDefinationList[0].OrderCommand.Set(false, OrderSide.None, OrderType.Unknown, 0, 0, 0);
                }
            }

            if (_isOrderResettable)
                return;

            int executedLegCounter = 1;

            if (_legDefinationList[0].OrderCommand.CurrentOrderID > 0 &&
                _legDefinationList[0].OrderCommand.IsCurrentOrderClosed() &&
                _legDefinationList[0].OrderCommand.TotalTradedQuantity > 0 &&
                _legDefinationList[0].OrderCommand.TotalTradedQuantity == _legDefinationList[0].OrderCommand.OrderQuantity)
            {
                // Exit Order
                for (int i = 1; i < _legDefinationList.Count; i++)
                {
                    LegDefination legDefination = _legDefinationList[i];

                    if (_legDefinationList[i].OrderCommand.CurrentOrder != null && _legDefinationList[i].OrderCommand.CurrentOrder.LeavesQuantity == 0)
                    {
                        // Check if all order legs are filled with desired quantity
                        executedLegCounter++;
                        continue;
                    }

                    double orderPrice = legDefination.LegOrderSide == OrderSide.Buy ? legDefination.OrderCommand.IVInfo.MarketDataContainer.BestAskPrice :
                        legDefination.OrderCommand.IVInfo.MarketDataContainer.BestBidPrice;

                    _legDefinationList[i].OrderCommand.Set(true,
                        _legDefinationList[i].LegOrderSide,
                        OrderType.Limit,
                       Math.Abs(_legDefinationList[i].LotSize) * _legDefinationList[i].OrderCommand.IVInfo.LotSize,
                       orderPrice, 0);
                }
            }

            if (executedLegCounter == _legDefinationList.Count)
            {
                if (_isOrderResettable == false)
                    CurrentRound++;

                //double lastExecutionSpread = GetExecutionSpread();
                _executionSpreadList.Add(GetExecutionSpread());

                _isOrderResettable = true;

            }

            return;
        }


        private void Reset()
        {
            if (_isOrderResettable)
            {
                for (int i = 0; i < _legDefinationList.Count; i++)
                {
                    LegDefination legDefination = _legDefinationList[i];
                    if (!legDefination.OrderCommand.IsCurrentOrderClosed())
                        legDefination.OrderCommand.Set(false, OrderSide.None, OrderType.Unknown, 0, 0, 0);
                    else
                        legDefination.OrderCommand.Reset(out _errorText);
                }
            }
        }



        private double GetExecutionSpread()
        {
            double executionSpread = 0;

            foreach (LegDefination legDefination in _legDefinationList)
            {
                if (legDefination.LegOrderSide == OrderSide.Buy)
                {
                    executionSpread += legDefination.LotSize * legDefination.OrderCommand.CurrentOrder.AverageTradedPrice;
                }
                else if (legDefination.LegOrderSide == OrderSide.Sell)
                {
                    executionSpread += (legDefination.LotSize) * legDefination.OrderCommand.CurrentOrder.AverageTradedPrice;
                }
            }

            return executionSpread;
        }

        private void OnSmartOrderRejected(SmartOrderRejectedEventArgs eventArgs)
        {

        }

        private void OnSmartOrderSendingFailed(SmartOrderSendngFailedEventArgs eventArgs)
        {

        }

        private void OnSmartOrderTraded(SmartOrderTradedEventArgs eventArgs)
        {
            
            Execute(this.maxSpreadBound,this.userSpread);
            
            
            
        }
    }
}
