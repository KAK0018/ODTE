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

//Trying to code new qty Slice ALgo here...

namespace QX.Blitz.Strategy.ODTE_Sell
{
    sealed class QuantitySliceX1 : QuantityDerivationAlgo
    {
        public int maxFreezeQty { get; set; }
        public QuantitySliceX1(long instrumentID, string ivObjectName, int maxFreezeQty)
            : base(instrumentID, ivObjectName)
        {
            try
            {
                this.maxFreezeQty = maxFreezeQty;
            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message);
            }
          
          
        }

      
        public override string GetName()
        {
            return "QuantitySliceX1";
        }


        public override void GetOrderQuantityToSend(IVInfo ivInfo, QuantityDerivationEventArgs eventArgs)
        {
            if (base.LastReceivedMarketDataEvent == null)
                return;

            if (eventArgs.Logger.ActivateProfiling)
            {
                eventArgs.Logger.TraceLogInfo("Query for Alog [ " + GetName() + "]");
            }

            if ((eventArgs.OriginalQuantity % base.LastReceivedMarketDataEvent.MarketDataContainerInfo.Instrument.LotSize) != 0)
            {
                eventArgs.Logger.TraceLogInfo("Supplied Quantity Must Be In Multiple Of Lot Size");
                return;
            }

            int depthOptimumQty = 0;
            if (eventArgs.TargetOrderSide == OrderSide.Buy)
            {
                depthOptimumQty = Math.Min(ivInfo.MarketDataContainer.GetBidSizeAt(0),
                                                ivInfo.MarketDataContainer.GetBidSizeAt(1));

                if (eventArgs.CurrentOrderInfo != null &&
                    eventArgs.CurrentOrderInfo.OrderQuantity >= ivInfo.MarketDataContainer.GetBidSizeAt(1))
                {
                    depthOptimumQty -= eventArgs.CurrentOrderInfo.OrderQuantity;
                }
            }
            else if (eventArgs.TargetOrderSide == OrderSide.Sell)
            {
                depthOptimumQty = Math.Min(ivInfo.MarketDataContainer.GetAskSizeAt(0),
                                                ivInfo.MarketDataContainer.GetAskSizeAt(1));

                if (eventArgs.CurrentOrderInfo != null &&
                    eventArgs.CurrentOrderInfo.OrderQuantity <= ivInfo.MarketDataContainer.GetAskSizeAt(1))
                {
                    depthOptimumQty -= eventArgs.CurrentOrderInfo.OrderQuantity;
                }
            }

            // Max disclosed quantity is always less then 20% of total quantity
            int maxOrderQuantityLot = (int)(eventArgs.OriginalQuantity * 0.20) / ivInfo.IVInstrument.Instrument.LotSize;
            int maxOrderQuantity = maxOrderQuantityLot * ivInfo.IVInstrument.Instrument.LotSize;
            int orderQuantity = Math.Max(ivInfo.IVInstrument.Instrument.LotSize, Math.Min(depthOptimumQty, maxOrderQuantity));

            



            if (eventArgs.FilledQuantity < eventArgs.OriginalQuantity)
            {
                int pendingOrderQty = eventArgs.OriginalQuantity - eventArgs.FilledQuantity;
                int orderQtyToSend = Math.Min(orderQuantity, pendingOrderQty);

                eventArgs.NewOrderQuantity = Math.Min(orderQtyToSend,maxFreezeQty);
                eventArgs.OrderFlag = true;
            }

            if (eventArgs.Logger.ActivateProfiling)
            {
                eventArgs.Logger.TraceLogInfo(string.Format("Quantity Query. AlgoName:{0}, OriginalOrderQuantity:{1}, FilledOrderQuantity:{2}, NewOrderQuantity:{3}, OrifQty: {4}, DepthOptimumQty: {5}, OrderFlag:{6}",
                   GetName(),
                   eventArgs.OriginalQuantity,
                   eventArgs.FilledQuantity,
                   eventArgs.NewOrderQuantity,
                    eventArgs.OriginalQuantity,
                   depthOptimumQty,
                   eventArgs.OrderFlag));
            }
        }
    }

    sealed class PriceFinderAlgoX1 : PriceDerivationAlgo
    {
        private DateTime _instructionDateTime = DateTime.Now;

        public PriceFinderAlgoX1(long instrumentID, string ivObjectName)
            : base(instrumentID, ivObjectName)
        {

        }


        public override string GetName()
        {
            return "PriceFinderAlgoX1";
        }

        public override void GetOrderPriceAndValidity(IVInfo ivInfo, PriceDerivationEventArgs eventArgs)
        {
            if (eventArgs.Logger.ActivateProfiling)
            {
                eventArgs.Logger.TraceLogInfo("Query for Alog [ " + GetName() + "]");
            }

            //Not Used in Logic
            if (eventArgs.InstructionType == OrderCommandExInstructionType.DecreaseLong)
                return;

            TimeSpan ts = DateTime.Now - eventArgs.InstructionSetTime;

            eventArgs.NewValidity = TimeInForce.GFD;
            
            if (eventArgs.TargetOrderSide == OrderSide.Buy)
            {
                if (ts.TotalSeconds < 5)
                {
                    if (eventArgs.CurrentOrderInfo != null)
                        eventArgs.NewPrice = ivInfo.MarketDataContainer.GetBestBiddingPrice(eventArgs.CurrentOrderInfo);
                    else
                        eventArgs.NewPrice = LastReceivedMarketDataEvent.TouchLineInfo.BestBidPrice + LastReceivedMarketDataEvent.InstrumentInfo.TickSize;
                }
                else if (ts.TotalSeconds >= 5 && ts.TotalSeconds < 10)
                {
                    eventArgs.NewPrice = (ivInfo.MarketDataContainer.BestBidPrice + ivInfo.MarketDataContainer.BestAskPrice) / 2.0;
                }
                else
                {
                    eventArgs.NewPrice = ivInfo.MarketDataContainer.BestAskPrice;
                }

                eventArgs.OrderFlag = true;
            }
            else if (eventArgs.TargetOrderSide == OrderSide.Sell)
            {
                if (ts.TotalSeconds < 5)
                {
                    if (eventArgs.CurrentOrderInfo != null)
                        eventArgs.NewPrice = ivInfo.MarketDataContainer.GetBestBiddingPrice(eventArgs.CurrentOrderInfo);
                    else
                        eventArgs.NewPrice = LastReceivedMarketDataEvent.TouchLineInfo.BestAskPrice - LastReceivedMarketDataEvent.InstrumentInfo.TickSize;
                }
                else if (ts.TotalSeconds >= 5 && ts.TotalSeconds < 10)
                {
                    eventArgs.NewPrice = (ivInfo.MarketDataContainer.BestBidPrice + ivInfo.MarketDataContainer.BestAskPrice) / 2.0;
                }
                else
                {
                    eventArgs.NewPrice = ivInfo.MarketDataContainer.BestBidPrice;
                }
                eventArgs.OrderFlag = true;
            }
            else
                eventArgs.OrderFlag = false;

            if (eventArgs.Logger.ActivateProfiling)
            {
                eventArgs.Logger.TraceLogInfo(string.Format("Price Query. Instrument: {0}, AlgoName: {1}, InstructionType: {2}, TargetOrderSide: {3}, RefPrice: {4}, NewPrice: {5}, Validitiy: {6}, BidPrice: {7}, AskPrice: {8}, LastPrice: {9}, TimeElapsed: {10}, OrderConditionHit: {11}, AppOrderID: {12}",
                   ivInfo.IVInstrument.Instrument.DisplayName,
                   GetName(),
                   eventArgs.InstructionType,
                   eventArgs.TargetOrderSide,
                   eventArgs.RefPrice,
                   eventArgs.NewPrice,
                   eventArgs.NewValidity,
                   LastReceivedMarketDataEvent.TouchLineInfo.BestBidPrice,
                   LastReceivedMarketDataEvent.TouchLineInfo.BestAskPrice,
                   LastReceivedMarketDataEvent.TouchLineInfo.LastPrice,
                   ts.TotalSeconds,
                   eventArgs.OrderFlag ? "Y" : "N",
                   eventArgs.CurrentOrderInfo != null ? eventArgs.CurrentOrderInfo.AppOrderID : 0));
            }
        }


    }

    sealed class TriggerEntryAlgoX1 : TriggerEntryAlgo
    {
        public TriggerEntryAlgoX1(long instrumentID, string ivObjectName)
            : base(instrumentID, ivObjectName)
        {
        }

        public override string GetName()
        {
            return "TriggerEntryX1";
        }

        public override void IsTriggered(IMarketDataContainerInfo marketDataContainer, double ltpAtTriggerEntrySet, TriggerEntryEventArgs eventArgs)
        {
            if (base.InstrumentID != marketDataContainer.Instrument.InstrumentID)
                return;

            if (eventArgs.IsForceComplete)
            {
                eventArgs.IsTriggered = true;
            }
            else
            {
                if (ltpAtTriggerEntrySet >= eventArgs.TriggerRefPrice)
                {
                    eventArgs.IsTriggered = (marketDataContainer.LastPrice <= eventArgs.TriggerRefPrice);
                }
                else if (ltpAtTriggerEntrySet <= eventArgs.TriggerRefPrice)
                {
                    eventArgs.IsTriggered = (marketDataContainer.LastPrice >= eventArgs.TriggerRefPrice);
                }
            }
        }

        public override void Reset()
        {
        }
    }

    sealed class PTargetPriceProviderX1 : ProfitTargetPriceDerivationAlgo
    {
        public PTargetPriceProviderX1(long instrumentID, string ivObjectName, string instrumentDisplayName)
            : base(instrumentID, ivObjectName)
        {

        }

        public override string GetName()
        {
            return "ProfitTargetPriceProviderX1";
        }

        protected override void OnMarketDataEvent(MarketDataUpdateEventArgs eventArgs)
        {
            if (base.InstrumentID != eventArgs.InstrumentInfo.InstrumentID)
                return;
        }

        public override void GetProfitTagetOrderPrice(IVInfo ivInfo, ProfitTargetPriceDerivationEventArgs eventArgs)
        {
            if (base.InstrumentID != ivInfo.IVInstrument.InstrumentID)
                return;

            eventArgs.NewValidity = TimeInForce.GFD;
            eventArgs.NewPrice = ivInfo.MarketDataContainer.GetMarketBiddingPrice(eventArgs.TargetOrderSide);
            eventArgs.OrderFlag = true;
        }

        private void Reset()
        {
        }

    }

    sealed class SLPriceProviderX1 : StopPriceDerivationAlgo
    {
        public SLPriceProviderX1(long instrumentID, string ivObjectName)
            : base(instrumentID, ivObjectName)
        {
        }


        public override string GetName()
        {
            return "StopLossPriceProviderX3";
        }

        public override void GetStopLossOrderPrice(IVInfo ivInfo, StopPriceDerivationEventArgs eventArgs)
        {
            if (base.InstrumentID != ivInfo.IVInstrument.InstrumentID)
                return;

            if (eventArgs.IsNative)
            {
                eventArgs.NewValidity = TimeInForce.GFD;
                eventArgs.NewPrice = eventArgs.TriggerPrice;
                eventArgs.OrderFlag = true;
            }
            else
            {
                if (eventArgs.IsTriggered && eventArgs.IsHitOnCurrentLTP)
                {
                    eventArgs.NewValidity = TimeInForce.GFD;
                    eventArgs.NewPrice = ivInfo.MarketDataContainer.GetMarketBiddingPrice(eventArgs.TargetOrderSide);
                    eventArgs.OrderFlag = true;
                }
                else
                {
                    eventArgs.OrderFlag = false;
                }
            }
        }
    }
}
