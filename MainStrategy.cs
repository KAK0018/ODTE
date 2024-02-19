
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
//using System.Timers;
//using System.Data;
using System.Globalization;

using QX.Base.Common;
using QX.Base.Common.InstrumentInfo;
using QX.Base.Common.Message;
using QX.Base.Core;
using QX.Base.Data;
using QX.Blitz.Core;
using QX.Blitz.Core.Series;
using QX.Common.Helper;
using QX.Common.Lib;
using QX.Base.Financial;
using QX.Blitz.Strategy.OptionsMLX1;
using System.Threading.Tasks;

//For the Manufracturing the 0-DTE Option Sell ...
//Managing by the Stradle,,,
namespace QX.Blitz.Strategy.ODTE_Sell
{

    //{2D65D53F-9E17-46EC-88E7-7FBCC557F482}
    [StrategyAttribute("{2D65D53F-9E17-46EC-88E7-7FBCC557F482}", "ODTE_Sell", "ODTE_Sell", "X2", "Kevin")]
    class MainStrategy : StrategyBase
    {
        private IVObject _ivFuturesInstrument = new IVObject(IVNames.IVSymbolName, "Futures Instrument", false,
                                                         InstrumentType.Futures,
                                                         MarketDataType.All, OrderEventType.None);

        private IVInfo _ivInfoFuturesInstrument = null;
        private IMarketDataContainerInfo _futuresMarketDataContainer = null;

        private List<double> _optionsStrikePriceCollection = new List<double>();
        private List<Options> _allOptionsCallList;
        private List<Options> _allOptionsPutList;

        // Map of Instrument ID and order execution ID
        private Dictionary<long, IOrderCommandEx> _orderExecutionMAP = new Dictionary<long, IOrderCommandEx>();
        private Dictionary<long, IVInfo> _ivInfoMAP = new Dictionary<long, IVInfo>();



        // for the Storing the Strikewise straddles..
        private Dictionary<int, double> straddleDataDictionary = new Dictionary<int, double>();


        #region Input Parameters

        [StrategyParameterAttribute("ClientID", DefaultValue = "", Description = "ClientID", DisplayName = "ClientID", CategoryName = "Input")]
        private string Input_ClientID = "";


        [StrategyParameterAttribute("StartTime", DefaultValue = "09:16", Description = "StartTime", DisplayName = "StartTime", CategoryName = "Input")]
        private string Input_StartTime = "09:16";


        [StrategyParameterAttribute("SquareOffTime", DefaultValue = "15:17", Description = "SquareOffTime", DisplayName = "SquareOffTime", CategoryName = "Input")]
        private string Input_SquareOffTime = "15:17";      

        [StrategyParameterAttribute("Input_OrderLotSize", 100, "Input_OrderLotSize")]
        private int Input_OrderLotSize = 100;
         
        [StrategyParameterAttribute("OptionWeeklyExpiry", "-", "Option Weekly Expiry[i.e 02DEC2020]")]
        private string Input_OptionWeeklyExpiry = "-";  //DayMonthYear

        [StrategyParameterAttribute("DeltaPoint", 100, "delta Point point")]
        private double Input_DeltaPoint = 100;  //do delta at 100 points.

        [StrategyParameterAttribute("HedgeStrikeDistance", 400, "Hedge Strike Distance")]
        private int Input_HedgeGap = 400;  //Hedge Strike distance From Spot.

        [StrategyParameterAttribute("RollStrikeGap", 200, "Strike gap between rolling Hedge Strike")]
        private int Input_RollStrikeGap = 200;  //Strike gap between rolling Hedge Strike

        [StrategyParameterAttribute("tempFlag", 0, "Temp Flag")]
        private int Input_flag = 0;  //


        [StrategyParameterAttribute("MAX_QTY", 60000, "Max Qty on one strike",CategoryName = "Bounds")]
        private int Input_Max_Qty = 60000;  //MAX arbritary upper bound on the particuler Qty.

        [StrategyParameterAttribute("MaxStraddleSpread", 10, "Max Straddle for shorting", CategoryName = "Bounds")]
        private int Input_MaxStraddleSpread = 10;  //MAX arbritary lower bound on straddle spread executor.

        #endregion

        #region Published Parameters



        [StrategyPublishParameter("DeltaNeutral", DefaultValue = 0, Description = "DeltaNeutral", DisplayName = "D/N")]
        private int Output_DN = 0;

        [StrategyPublishParameter("Delta", DefaultValue = 0, Description = "Delta", DisplayName = "Delta Val")]
        private double Output_Delta = 0;

        [StrategyPublishParameter("Gamma", DefaultValue = 0, Description = "Gamma", DisplayName = "Gamma Val")]
        private double Output_Gamma = 0;

        [StrategyPublishParameter("Straddle-Last", DefaultValue = 0, Description = "Straddle-Last", DisplayName = "Last Executed Straddle")]
        private double Output_Straddle_Last = 0;


        [StrategyPublishParameter("SynthFut", DefaultValue = 0, Description = "SynthFut", DisplayName = "Synth Fut")]
        private double Output_SynthFut = 0;

        [StrategyPublishParameter("MTM", DefaultValue = 0, Description = "MTM", DisplayName = "MTM")]
        private double Output_MTM = 0;


        #endregion

        private List<Options> _optionsCollection = new List<Options>();

        private DateTime _squareOffDateTime = DateTime.Now.AddHours(10);
        private DateTime _intTime = DateTime.Now.AddHours(10);
        private DateTime _currRefTime;

        private double _minGap = 100;


        private DateTime _previousLTPEventDT = DateTime.MinValue;
        private bool _hasPosition = false;
        private bool intialTransection = false;
        private bool timeSliceFlag = true;  // make sure it is true , until its changed from ActionCommand...
        private double lastRebBhav;

        private bool _isTimebasedorderTriggered = false;
        private string _indexName = "-";

        private IMarketDataContainerInfo _callOptionsMarketDataContainer = null;
        private IMarketDataContainerInfo _putOptionsMarketDataContainer = null;

        private bool _squareOffTriggeredTimeBased = false;

        private bool _forceExitFlag = false;
        private bool _posExited = false;     //check if forceeXIT or sqr exit has been compeleted!


        private Options _CeInt = null;
        private Options _PeInt = null;


        private static InstrumentProperty instProp_freezeMaxQty = null;
        
        protected override void OnInitialize()
        {

            try
            {
                _ivInfoFuturesInstrument = base.GetIVInfo(_ivFuturesInstrument);
                _futuresMarketDataContainer = base.GetMarketDataContainer(_ivInfoFuturesInstrument.IVInstrument.InstrumentID);

                _squareOffDateTime = DateTime.Now.Date.Add(GetSqureTimeStamp(Input_SquareOffTime));
                _intTime = DateTime.Now.Date.Add(GetSqureTimeStamp(Input_StartTime));
             

                if (Input_OptionWeeklyExpiry.Trim() == "")
                {
                    Input_OptionWeeklyExpiry = ((Futures)(_ivInfoFuturesInstrument.IVInstrument.Instrument)).ContractExpiration.ToString("ddMMMyyyy");
                }
                
                if (_ivInfoFuturesInstrument.InstrumentName.ToUpper().Contains("BANKNIFTY"))
                {
                    _minGap = 100;
                }

                else if (_ivInfoFuturesInstrument.InstrumentName.ToUpper().Contains("MIDCPNIFTY"))
                {
                    _minGap = 25;
                }

                else if (_ivInfoFuturesInstrument.InstrumentName.ToUpper().Contains("FINNIFTY"))
                {
                    _minGap = 50;
                }

                else if (_ivInfoFuturesInstrument.InstrumentName.ToUpper().Contains("NIFTY"))
                {
                    _minGap = 50;
                }
                else
                {
                    _minGap = 50;
                }

                //option chain for the particluer expiry...

                _allOptionsCallList = base.InstrumentProvider.GetAllOptionInstruments(_ivInfoFuturesInstrument.IVInstrument.ExchangeSegment)
                                                                                      .Find(iterator => iterator.Name == _ivInfoFuturesInstrument.IVInstrument.Instrument.Name &&
                                                                                                        iterator.OptionType == OptionType.CE &&
                                                                                                        iterator.ContractExpirationString.Equals(Input_OptionWeeklyExpiry, StringComparison.InvariantCultureIgnoreCase))
                                                                                      .AvailableInstrument.OrderBy(iterator => iterator.ContractExpiration)
                                                                                      .ThenByDescending(iterator => iterator.StrikePrice).ToList();


                _allOptionsPutList = base.InstrumentProvider.GetAllOptionInstruments(_ivInfoFuturesInstrument.IVInstrument.ExchangeSegment)
                                                                                   .Find(iterator => iterator.Name == _ivInfoFuturesInstrument.IVInstrument.Instrument.Name &&
                                                                                                     iterator.OptionType == OptionType.PE &&
                                                                                                     iterator.ContractExpirationString.Equals(Input_OptionWeeklyExpiry, StringComparison.InvariantCultureIgnoreCase))
                                                                                   .AvailableInstrument.OrderBy(iterator => iterator.ContractExpiration)
                                                                                   .ThenByDescending(iterator => iterator.StrikePrice).ToList();



                // Below will subscribe the market data containerin...
                foreach (Options option in _allOptionsCallList)
                {
                    double price = base.GetMarketDataContainer(option.InstrumentID).LastPrice;
                }

                foreach (Options option in _allOptionsPutList)
                {
                    double price = base.GetMarketDataContainer(option.InstrumentID).LastPrice;
                }

                // Reinitialize Executors based on registerd dynamic IVs
                IVObject[] ivObjectList = base.TradableIVObjects;
                foreach (IVObject optionIVObject in ivObjectList)
                {
                    IOrderCommandEx orderExecutor = null;
                    IVInfo optionsIVInfo = base.GetIVInfo(optionIVObject);
                   
                    if (optionsIVInfo.Statistics.NetPosition != 0)
                        _hasPosition = true;

                    Options options = (Options)optionsIVInfo.IVInstrument.Instrument;
                    
                    if (optionsIVInfo == null && options != null)
                    {

                        string errorString = string.Empty;
                        if (!base.RegisterRuntimeIVObject(optionIVObject, options, out errorString))
                        {
                            throw (new Exception(string.Format("Could not register Runtime IVObject[{0}].", options.DisplayName)));
                        }

                        optionsIVInfo = base.GetIVInfo(optionIVObject);
                    }


                    //

                    //get the Freeze Qty of the Instrument...
                    Instrument iv = _ivInfoFuturesInstrument.IVInstrument.Instrument;
                    iv.ExtendedMarketProperties.TryGetValue("FreezeQuantity", out instProp_freezeMaxQty);
                    int maxFreezeQty = int.Parse(instProp_freezeMaxQty.Value) - 1;

                    if (!_orderExecutionMAP.TryGetValue(options.InstrumentID, out orderExecutor))
                    {
                        orderExecutor = this.GetSmartOrderExecutor("IVSmartOrderEX_" + options.DisplayName, optionIVObject);
                        _orderExecutionMAP[options.InstrumentID] = orderExecutor;
                        
                        orderExecutor.SetPriceDerivationAlgo(new PriceFinderAlgoX1(optionsIVInfo.IVInstrument.InstrumentID, optionsIVInfo.IVObject.Name));
                        
                        orderExecutor.SetQuantityDerivationAlgo(new QuantitySliceX1(optionsIVInfo.IVInstrument.InstrumentID, 
                            optionsIVInfo.IVObject.Name, maxFreezeQty));
                        
                        orderExecutor.SetTriggerEntryAlgo(new TriggerEntryAlgoX1(optionsIVInfo.IVInstrument.InstrumentID, optionsIVInfo.IVInstrument.IVObjectName));
                        
                        orderExecutor.SetIncDecOrderQuantityFactor(50);

                        if (!Input_ClientID.Equals(string.Empty) && !Input_ClientID.Equals("1"))
                        {
                            UserExchangeProperty userExchangeProp = new UserExchangeProperty();
                            userExchangeProp.ExchangeClientID = Input_ClientID;

                            orderExecutor.SetUserExchangeProperty(userExchangeProp);
                        }
                    }
                }
                
                _CeInt = null;
                _PeInt = null;
      

                TraceLogInfo("Strategy Initialize Successfully");
            }
            catch (Exception ex)
            {
                TraceLogError("Exception: " + ex);
                TraceLogError(ex.StackTrace.Replace('\n', ',').ToString());
                TraceLogInfo(ex.ToString());

            }
      
        }

        private TimeSpan GetSqureTimeStamp(string inputDTString)
        {
            DateTime now = DateTime.Now;
            int hh = 15;
            int mm = 26;
            TimeSpan ts = new TimeSpan(hh, mm, 0);
            try
            {
                if (!string.IsNullOrEmpty(inputDTString) && inputDTString.IndexOf(':') > 0)
                {
                    string[] timeTokens = inputDTString.Split(':');
                    if (timeTokens.Length >= 1)
                    {
                        hh = int.Parse(timeTokens[0]);
                        mm = int.Parse(timeTokens[1]);
                    }
                }

                ts = new TimeSpan(hh, mm, 0);
            }
            catch (Exception exp)
            {
                ts = new TimeSpan(15, 25, 0);
                TraceLogError("Exception Datetime Initialization: " + exp.Message);
            }

            return ts;
        }

        public override Version BuildVersion
        {
            get
            {
                return new Version(1, 1, 1, 7);
            }
        }

        protected override bool OnStart(out string errorString)
        {
            errorString = string.Empty;
            TraceLogInfo("Strategy Instance [" + base.GetStrategyInstanceInfo().InstanceName + "] Started Successfully.");
            _squareOffTriggeredTimeBased = false;

            //Try to get saveStrategtySetting() if any state is saved..
            Object t1 = GetStrategySettings(varKeys.intialTransection_key.ToString());  
            if (t1 != null)
                intialTransection = (bool)t1;

           
            return true;
        }

        protected override void OnStopping()
        {
            string errorString = string.Empty;
            bool allOrderCancelled = CancelAllOpenOrders(10, out errorString);

           

            //save the curRefTime state...
            base.SaveStrategySettings(varKeys.curRefTime_key.ToString(), _currRefTime.ToString());
                       
            TraceLogInfo("Strategy Instance [" + base.GetStrategyInstanceInfo().InstanceName + "] Stopping.");
        }

        public override IVObject[] IVObjects
        {
            get
            {
                List<IVObject> _ivObjectArray = new List<IVObject>();
                _ivObjectArray.Add(_ivFuturesInstrument);
                return _ivObjectArray.ToArray();
            }
        }

        protected override int MarketDataDelayNotifyTimeInSeconds
        {
            get { return 10; }
        }

        //For Chechking the IV assignment...
        public override bool ValidateInstrumentAssignments(KeyValuePair<IVObject, Instrument>[] ivObjetToInstrumentMap, out string errorString)
        {
            errorString = string.Empty;
            foreach (KeyValuePair<IVObject, Instrument> obj in ivObjetToInstrumentMap)
            {
                Instrument iv = obj.Value;
                TraceLogInfo(iv.DisplayName);
            }

            return true;
        }

        //For the Input Params validation...(need check this...)
        protected override bool ValidateStrategyParameter(string parameterName, object paramterValue, out string errorString)
        {
            errorString = "";
            return true;
        }

        protected override void OnStrategyParameterLoadedFromDatabase()
        {

        }

        protected override void OnStrategyParamterChanged()
        {
            _squareOffDateTime = DateTime.Now.Date.Add(GetSqureTimeStamp(Input_SquareOffTime));
            _intTime = DateTime.Now.Date.Add(GetSqureTimeStamp(Input_StartTime));

            foreach (KeyValuePair<long, IOrderCommandEx> entry in _orderExecutionMAP)
            {
                IOrderCommandEx orderCommandEx = entry.Value;

                if (orderCommandEx.IVInfo.Statistics.NetPosition != 0)
                {
                    if (!Input_ClientID.Equals(string.Empty) && !Input_ClientID.Equals("1"))
                    {
                        UserExchangeProperty userExchangeProp = new UserExchangeProperty();
                        userExchangeProp.ExchangeClientID = Input_ClientID;

                        orderCommandEx.SetUserExchangeProperty(userExchangeProp);
                        base.TraceLogInfo("UserExchangeProperty Set. ExchangeClientID:" + userExchangeProp.ExchangeClientID);
                    }
                }
            }
        }

        public override bool RequiredToPublishParameterInStrategyStopMode
        {
            get
            {
                return true;
            }
        }

        public override bool RequiredToSendMarketDataInStrategyStopMode
        {
            get
            {
                return true;
            }
        }

        protected override void OnOrderCancelled(IVObject ivObject, OrderCancellationAcceptedEventArgs eventArgs)
        {

        }

        protected override void OnMarketDataDelay(StrategyMarketDataDelayEventArgs eventArgs)
        {

        }

        //register new commands here...
        protected override ActionCommandInfo[] GetActionCommands()
        {
            List<ActionCommandInfo> actionCommandList = new List<ActionCommandInfo>();

            actionCommandList.Add(ForceExitActionCommand());          
            actionCommandList.Add(StopAllActionCommand());


            return actionCommandList.ToArray();
        }

        //make Structure of the Command...
        private ActionCommandInfo ForceExitActionCommand()
        {
            //Lets not use any parameters as of now...
            List<ActionCommandFieldInfo> actionCommandParameterInfoList = new List<ActionCommandFieldInfo>();
        
            return CreateActionCommand(ForceExitCommand.CommandStaticID, "**Force Exit**", false, actionCommandParameterInfoList.ToArray());
        }

        
        //commmnad for the stopping all exisiting spread Exectuor..
        private ActionCommandInfo StopAllActionCommand()
        {
            List<ActionCommandFieldInfo> actionCommandParameterInfoList = new List<ActionCommandFieldInfo>();
            return CreateActionCommand(StopAllCommand.CommandStaticID, "*Stop All*", false, actionCommandParameterInfoList.ToArray());
        }

        protected override void ExecuteActionCommand(Guid commandStaticID, ActionCommandFieldInfo[] inputFields)
        {
            base.ExecuteActionCommand(commandStaticID, inputFields);


            if (GoFlatOrderCommand.CommandStaticID.Equals(commandStaticID))
            {
                ExecuteGoFlat(inputFields);
            }

            else if (ForceExitCommand.CommandStaticID.Equals(commandStaticID))
            {
                ExecuteForceExit();

            }
            
            else if (StopAllCommand.CommandStaticID.Equals(commandStaticID))
            {
                ExecuteStopALL();
            }

            else if (NewOrderOnATMStrikePricePairCommand.CommandStaticID.Equals(commandStaticID))
            {
                //ExecuteNewOrderOnATMStrikePricePair(inputFields);
            }
            else if (PrintStatisticsCommand.CommandStaticID.Equals(commandStaticID))
            {
                UpdateStrategyInfoText("*********************PRINT STATISTICS START *****************");

                string indexName = "";
                if (_ivInfoFuturesInstrument.InstrumentName.ToUpper().Contains("BANKNIFTY"))
                {
                    indexName = "Nifty Bank";
                }
                else if (_ivInfoFuturesInstrument.InstrumentName.ToUpper().Contains("NIFTY"))
                {
                    indexName = "Nifty 50";
                }

                IndexData indexData = GetIndexData(ExchangeSegment.NSECM, indexName);
                if (indexData != null)
                {
                    double indexLTP = indexData.IndexValue;
                    UpdateStrategyInfoText(string.Format("Index Data[{0}] LTP is: {1}",
                        indexName,
                        indexLTP));
                }

                UpdateStrategyInfoText("**********************CALL OPTIONS*****************");
                foreach (Options options in _allOptionsCallList)
                {
                    UpdateStrategyInfoText(string.Format("Call Options: Name: {0}",
                                           options.InstrumentDetailedDescription));
                }

                UpdateStrategyInfoText("**********************PUT OPTIONS*****************");
                foreach (Options options in _allOptionsPutList)
                {
                    UpdateStrategyInfoText(string.Format("Put Options: Name: {0}",
                                           options.InstrumentDetailedDescription));
                }

                UpdateStrategyInfoText("***************************************");

                UpdateStrategyInfoText("***********POSITIONS IVs***********");

                foreach (KeyValuePair<long, IOrderCommandEx> entry in _orderExecutionMAP)
                {
                    IOrderCommandEx orderCommandEx = entry.Value;

                    if (orderCommandEx.IVInfo.Statistics.NetPosition != 0)
                    {
                        UpdateStrategyInfoText(string.Format("Instrument: {0}, Position: {1}",
                            orderCommandEx.IVInfo.IVInstrument.Instrument.DisplayName,
                            orderCommandEx.IVInfo.IVInstrument, orderCommandEx.IVInfo.Statistics.NetPosition));
                    }
                }



                UpdateStrategyInfoText("*********************PRINT STATISTICS END *****************");
            }
        }

        private void ExecuteGoFlat(ActionCommandFieldInfo[] inputFields)
        {
            foreach (KeyValuePair<long, IOrderCommandEx> entry in _orderExecutionMAP)
            {
                IOrderCommandEx orderCommandEx = entry.Value;
                Options options = (Options)orderCommandEx.IVInfo.IVInstrument.Instrument;
                if (options != null && orderCommandEx.IVInfo.Statistics.NetPosition != 0)
                {
                    UpdateStrategyInfoText(string.Format("Executing GoFlat. Instrument: {0}, Position: {1}",
                                             options.DisplayName, orderCommandEx.IVInfo.Statistics.NetPosition));
                    orderCommandEx.GoFlat();
                }
            }
        }

       
        //This is function that will be called when ForceExit command Triggerd...
        private void ExecuteForceExit()
        {
            _forceExitFlag = true;
            TraceLogInfo("Force-Exit Triggered! Exiting all the Positions!");

        }

        //Stop all running spread...
        //Like F11 button to stiop all running execution Algos....
        private void ExecuteStopALL()
        {
            TraceLogInfo("Stopping all the spread-execution...");
            //check for the Sell executor..
            if (_straddleSellEx != null)
            {
                _straddleSellEx.Clean();
                _legDefinationListSell.Clear();
                _straddleSellEx = null;
                _CeInt = null;
                _PeInt = null;
            }

            if (_straddleBuyEx != null)
            {
                _straddleBuyEx.Clean();
                _legDefinationListBuy.Clear();
                _straddleBuyEx = null;
                _CeInt = null;
                _PeInt = null;
            }
        }

        private void ExecuteCustOption(Options options, int lotToTrade)
        {
            try
            {
                List<Options> tradableOptionsList = new List<Options>() { options };

                CreateRuntimeIVObject(tradableOptionsList);
                Thread.Sleep(200);
                ExecuteCustPOrder(options, lotToTrade);
            }
            catch (Exception exp)
            {
                base.TraceLogError("Execute-Delta Orders:" + exp.Message);
            }
        }

        private Options getATMCEStrike(double futBhav)
        {


            double temp = (futBhav / _minGap);
            int strikePrice = 0;

            if (temp % 1 > 0.5)
                strikePrice = (int)(((int)temp + 1) * _minGap);

            else
                strikePrice = (int)((int)(temp) * _minGap);


            foreach (Options option in _allOptionsCallList)
            {

                //TraceLogInfo(strikePrice.ToString());

                //find exact strike Ptice match..
                if ((int)option.StrikePrice == strikePrice)
                {

                    return option;
                }
            }

            TraceLogInfo("CE, Strike Price not found in the option-List!" + strikePrice);
            return null;

        }

        private Options getATMPEStrike(double futBhav)
        {


            double temp = (futBhav / _minGap);
            int strikePrice = 0;

            if (temp % 1 > 0.5)
                strikePrice = (int)(((int)temp + 1) * _minGap);

            else
                strikePrice = (int)((int)(temp) * _minGap);


            foreach (Options option in _allOptionsPutList)
            {
                //find exact strike Ptice match..
                if ((int)option.StrikePrice == strikePrice)
                {

                    return option;
                }
            }


            TraceLogInfo("PE, Strike Price not found in the option-List!" + strikePrice);
            return null;

        }


        private Options getOptionObject(int StrikePrice, OptionType optType)
        {


            if (optType == OptionType.CE)
            {

                foreach (Options option in _allOptionsCallList)
                {

                    //find exact strike Ptice match..
                    if ((int)option.StrikePrice == StrikePrice)
                    {

                        return option;
                    }
                }

            }

            else if (optType == OptionType.PE)
            {
                foreach (Options option in _allOptionsPutList)
                {
                    //find exact strike Ptice match..
                    if ((int)option.StrikePrice == StrikePrice)
                    {

                        return option;
                    }
                }
            }


            TraceLogInfo(optType + StrikePrice + "-Strike Not Found in the OptionList!");
            return null;
        }

        private double curFutBhav= 0;
       
        int curSynthStrike = 0;
        double curSynthFut = 0;

        Options curATMCE, curATMPE = null;
        double curCEMid, curPEMid = 0;

        int delta_out = 0;
  

        private List<LegDefination> _legDefinationListSell = new List<LegDefination>();
        private List<LegDefination> _legDefinationListBuy = new List<LegDefination>();


        private SmartMLegExecutorX2 _straddleSellEx = null;
        private SmartMLegExecutorX2 _straddleBuyEx = null;
        private List<LegDefination> GetLegDefinationList(int strikePriceInt, OrderSide orderSide, String smartOrderID="")
        {


            Options _ce = getATMCEStrike(strikePriceInt);
            Options _pe = getATMPEStrike(strikePriceInt);

            IVInfo ceLeg = CreateRuntimeIVInfo(_ce);
            IVInfo peLeg = CreateRuntimeIVInfo(_pe);

            CreateRuntimeIVObject(new List<Options> { _ce, _pe });//can also optimize this two fucntion...

            IOrderCommand orderCommandCE = base.GetSmartOrderCommand("CE_" + smartOrderID + _ce.DisplayName, TimeInForce.GFD, ceLeg.IVObject);
            IOrderCommand orderCommandPE = base.GetSmartOrderCommand("PE_"+ smartOrderID + _pe.DisplayName, TimeInForce.GFD, peLeg.IVObject);

            LegDefination legDefinationCE = new LegDefination(orderCommandCE, orderSide, OptionType.CE, -1);
            LegDefination legDefinationPE = new LegDefination(orderCommandPE, orderSide, OptionType.PE, -1);

            return new List<LegDefination> { legDefinationCE, legDefinationPE };
            
        }


        int[] StrikeList = new int[6];         
      

        public double[] getStrikes(double curFutBhav, int curATM)
        {
         
           // int curATM = (int)getATMCEStrike(curFutBhav).StrikePrice;
            double[] strikes = { curATM - (2 * _minGap), curATM - (_minGap), curATM, curATM + (_minGap), curATM + (2*_minGap) };
            return strikes;
        }


        private bool IsAllSellPositionSquareOff()
        {
            foreach (var abc in _orderExecutionMAP.Values)
            {
                if (abc.IVInfo.Statistics.NetPosition < 0)
                {
                    return false;

                }
            }

            return true;
        }


        private int GetStrikeWithMinStraddle(Dictionary<int, double> dictionary)
        {
            return dictionary.OrderBy(straddlePrice => straddlePrice.Value).FirstOrDefault().Key;
        }


        //make this snippet global..
        IOrderCommandEx ceHedgeCommand = null, peHedgeCommand = null;
        //IOrderCommandEx ceSellHedgeCommand = null, peSellHedgeCommnad = null;

        //executing the Hedge..
        private void ExecuteHedge(Options CeStrike, Options PeStrike, int QtyinLot)
        {
            if(!(CeStrike == null))
            {
                ExecuteCustOption(CeStrike, QtyinLot);
                _orderExecutionMAP.TryGetValue(CeStrike.InstrumentID, out ceHedgeCommand);
            }

            if (!(PeStrike == null))
            {
                ExecuteCustOption(PeStrike, QtyinLot);
                _orderExecutionMAP.TryGetValue(PeStrike.InstrumentID, out peHedgeCommand);
            }

        }


        //For roll-over of the Stirke..
        private void RollOverStrike(IOrderCommandEx curPosCommand, Options newPosition)
        {
            Options CurOptin = (Options)curPosCommand.IVInfo.IVInstrument.Instrument;
            int curHedgeQty = curPosCommand.IVInfo.Statistics.NetPosition;
            double lastExp = curPosCommand.GetAverageExecutionPrice() * curHedgeQty;

            IOrderCommandEx tempCommandEx1 = null, tempCommandEx2 = null;
            
            
            //lets First Sqr-off the Exisiting Positions..
            ExecuteCustOption(CurOptin, -curHedgeQty/ CurOptin.LotSize);
            _orderExecutionMAP.TryGetValue(CurOptin.InstrumentID,out tempCommandEx1);
            //assuming tempCommnadEx is Now Temp Sell Executor..

            //Mark the Prices oF the last HEdges..
           
            //fetch the New Price of the options...
            double priceofOption = base.GetMarketDataContainer(newPosition.InstrumentID).BestAskPrice;
            int newQty = (int)(lastExp / priceofOption);

            //make New transection on the new strike...
            ExecuteCustOption(newPosition, newQty/newPosition.LotSize);
            _orderExecutionMAP.TryGetValue(newPosition.InstrumentID, out tempCommandEx2);

            if (newPosition.OptionType == OptionType.CE)
            {
                ceHedgeCommand = tempCommandEx2; //if call...//can also}
                ceHedgeOption = newPosition;

            }

            //if put Options..
            else
            {
                peHedgeCommand = tempCommandEx2;
                peHedgeOption = newPosition;
            }
        }


        //This also global..
        Options ceHedgeOption = null, peHedgeOption = null;
        bool switchToHedge = false;

        //enum CommandAction
        //{
        //    Rollover,
        //    ExecuteHedge
        //}

        //ConcurrentQueue<CommandAction> _queueCommand = new ConcurrentQueue<CommandAction>();
        //class 
        protected override void OnMarketDataEvent(StrategyMarketDataEventArgs eventArgs)
        {
            try
            {
                //if(_queueCommand.Count != 0)
                //{
                //    CommandAction currentCommand;
                //    _queueCommand.TryPeek(out currentCommand);

                //    if(currentCommand == CommandAction.ExecuteHedge)
                //    {

                //        _queueCommand.TryDequeue();
                //    }
                //}

                //_queueCommand.Enqueue(CommandAction.Rollover);
                //_queueCommand.Enqueue(CommandAction.ExecuteHedge);





                //fetch cur bhav... 
                curFutBhav = base.GetMarketDataContainer(_ivFuturesInstrument).LastPrice;
                DateTime currentLTPDT = eventArgs.MarketDataContainerInfo.TouchLineInfo.LastTradedTimeDT;

                if (curFutBhav == 0 || curFutBhav == double.NaN)
                {
                    TraceLogInfo("curBhavFut is Zero/NaN..");
                    return;
                }

                if (curFutBhav != 0 && (curSynthStrike == 0 || Math.Abs(curSynthStrike - curFutBhav) > _minGap*2))
                {

                    double temp = (curFutBhav / _minGap);
                    curSynthStrike = (int)((int)temp * _minGap);

                    curATMCE = getOptionObject(curSynthStrike, OptionType.CE);
                    curATMPE = getOptionObject(curSynthStrike, OptionType.PE);

                    curCEMid = (base.GetMarketDataContainer(curATMCE.InstrumentID).BestAskPrice + base.GetMarketDataContainer(curATMCE.InstrumentID).BestBidPrice) / 2;
                    curPEMid = (base.GetMarketDataContainer(curATMPE.InstrumentID).BestAskPrice + base.GetMarketDataContainer(curATMPE.InstrumentID).BestBidPrice) / 2;

                    curSynthFut = curSynthStrike + curCEMid - curPEMid;


                    double roundTemp  = (curSynthFut / _minGap);
                    int curAtmStrike = 0;

                    if (temp % 1 > 0.5)
                        curAtmStrike = (int)(((int)roundTemp + 1) * _minGap);

                    else
                        curAtmStrike = (int)((int)(roundTemp) * _minGap);

                    //Make New Array for the strikePrices/...
                    for (int i=0; i<StrikeList.Length; i++)
                    {
                        StrikeList[i] = (int)(curAtmStrike - ((3-i)*_minGap));
                    }                  
                
                }

                curCEMid = (base.GetMarketDataContainer(curATMCE.InstrumentID).BestAskPrice + base.GetMarketDataContainer(curATMCE.InstrumentID).BestBidPrice) / 2;
                curPEMid = (base.GetMarketDataContainer(curATMPE.InstrumentID).BestAskPrice + base.GetMarketDataContainer(curATMPE.InstrumentID).BestBidPrice) / 2;

                curSynthFut = curSynthStrike + curCEMid - curPEMid;


                //Strike lISt Dict..
                //u Can also Make Loop Parallel here.
                foreach (int strike in StrikeList)
                {
                    double thisStrikeStraddle = (base.GetMarketDataContainer(getATMCEStrike(strike).InstrumentID).LastPrice +
                                                base.GetMarketDataContainer(getATMPEStrike(strike).InstrumentID).LastPrice);

                    straddleDataDictionary[strike] = thisStrikeStraddle;
                }

                

                //return ther Function if Positions already exited...
                if (_posExited == true)
                {
                    foreach (var abc in _orderExecutionMAP.Values)
                    {
                        if (abc.IVInfo.Statistics.NetPosition != 0)
                        {
                            IOrderCommandEx orderCommandEx = abc;

                            //Cancel any existing transection first and then assign the transection....
                            if (orderCommandEx.InstructionType != OrderCommandExInstructionType.GoFlat)
                            {
                                orderCommandEx.CancelTransaction();
                                Thread.Sleep(100);
                                orderCommandEx.GoFlat();

                            }

                        }
                    }

                    return;
                }

                    //if sqaure-off time triggerd or Force Exit Triggered...
                if (_forceExitFlag == true || (_squareOffTriggeredTimeBased == false && currentLTPDT >= _squareOffDateTime))
                {

                    if (_squareOffTriggeredTimeBased == false)
                        TraceLogInfo(string.Format("SquareTimer is triggered"));

                    _forceExitFlag = false; //Squre the Positions--set the flag off...
                    _posExited = true;
                    _squareOffTriggeredTimeBased = true;
                    return;
                }
                
                //check for the all short Positions is Sqred off, if hedge Flag is Switch on..
                if(switchToHedge && !IsAllSellPositionSquareOff())
                {
                    TraceLogInfo("Sqauring off all short Positions..");
                    foreach (var abc in _orderExecutionMAP.Values)
                    {
                        if (abc.IVInfo.Statistics.NetPosition < 0)
                        {
                            IOrderCommandEx orderCommandEx = abc;

                            //Cancel any existing transection first and then assign the transection....
                            if (orderCommandEx.InstructionType != OrderCommandExInstructionType.GoFlat)
                            {
                                //orderCommandEx.CancelTransaction();
                                //Thread.Sleep(100);
                                orderCommandEx.GoFlat();

                            }

                        }
                    }
                    return;
                }

                //SmartExecutor Execution...
                //if (_straddleBuyEx != null || _straddleSellEx != null)
                    if (!(_straddleBuyEx == null && _straddleSellEx == null))
                    {

                    //Execution..(If any Pending)
                    if ((base.StrategyRunningMode == StrategyMode.Started) &&
                        (_straddleSellEx != null &&_straddleSellEx.CurrentRound < _straddleSellEx.getMaxLot()) ||
                        (_straddleBuyEx != null &&_straddleBuyEx.CurrentRound < _straddleBuyEx.getMaxLot())
                        )
                    {
                        //Executing at the market....
                        if(_straddleSellEx != null)
                            _straddleSellEx.Execute(Input_MaxStraddleSpread);
                        
                        if(_straddleBuyEx != null)
                            _straddleBuyEx.Execute(Input_MaxStraddleSpread);


                        TraceLogInfo("Executing the straddles...");
                        return;
                    }

                    //Sell Execution Done..
                    //assuming the straddle-spread execution compeleted..
                    if (_straddleSellEx != null && _straddleSellEx.CurrentRound >= _straddleSellEx.getMaxLot())
                    {

                        TraceLogInfo(_straddleSellEx.getExecutionLog());
                        if (_straddleSellEx.IsOrderResettable == true)
                        {
                            Output_Straddle_Last = Math.Round(_straddleSellEx.getAvgExecutionSpread(), 2);
                            _straddleSellEx.Clean();
                            _legDefinationListSell.Clear();
                            _straddleSellEx = null;


                        }
                    }

                    //Buy Execution Done..
                    if (_straddleBuyEx != null && _straddleBuyEx.CurrentRound >= _straddleBuyEx.getMaxLot())
                    {

                        TraceLogInfo(_straddleBuyEx.getExecutionLog());
                        if (_straddleBuyEx.IsOrderResettable == true)
                        {
                            Output_Straddle_Last = Math.Round(_straddleBuyEx.getAvgExecutionSpread(), 2);
                            _straddleBuyEx.Clean();
                            _legDefinationListBuy.Clear();
                            _straddleBuyEx = null;


                        }
                    }


                }

                //need for the Rebalance ...
                //shift Strike above
                if (switchToHedge == false && intialTransection == true && ((curSynthFut-lastRebBhav) > Input_DeltaPoint || (curSynthFut - lastRebBhav) < -Input_DeltaPoint))
                {
                    TraceLogInfo("rolling the sold Straddle..");
                    lastRebBhav = curSynthFut;    
                    int Qty = 0;
                    //buy back the PRevious Executed and Sell new strikes..
                    foreach (var pos in _orderExecutionMAP.Values)
                    {
                        if (pos.IVInfo.Statistics.NetPosition < 0)
                        {
                            IOrderCommandEx orderCommandEx = pos;

                            Qty = pos.IVInfo.Statistics.NetPosition / pos.IVInfo.LotSize; 

                            if (((Options)pos.IVInfo.IVInstrument.Instrument).OptionType == OptionType.CE)
                            {
                                _CeInt = (Options)pos.IVInfo.IVInstrument.Instrument;

                            }

                            else
                            {
                                _PeInt = (Options)pos.IVInfo.IVInstrument.Instrument;

                            }
                        }
                    }
                    //Lets Buy the Sold Straddle..
                    _legDefinationListBuy = new List<LegDefination>();
                    _legDefinationListBuy = GetLegDefinationList((int)_CeInt.StrikePrice,OrderSide.Buy);
                        
                    

                    _straddleBuyEx = new SmartMLegExecutorX2(_legDefinationListBuy, Math.Abs(Qty));
                    _straddleBuyEx.spreadNature = 1;   //for Buying the straddle..

                    //Sell the New Straddle/...
                    //this will enable both the excution simultanenously...
                    intialTransection = false;
                    return;
                }



                //Selling the First Main straddles Positions...
                if (switchToHedge == false && intialTransection == false && currentLTPDT >= _intTime && base.StrategyRunningMode == StrategyMode.Started)
                {
                    //findout Current ATM..
                    int StrikePriceToSell = GetStrikeWithMinStraddle(straddleDataDictionary);

                    if (_straddleSellEx == null)
                    {

                        //need to check here is sit already cleared or not..
                        _legDefinationListSell = new List<LegDefination> ();
                        _legDefinationListSell = GetLegDefinationList(StrikePriceToSell,OrderSide.Sell);
                        
                        //Selling the Straddle here..

                        _straddleSellEx = new SmartMLegExecutorX2(_legDefinationListSell, Input_OrderLotSize);
                        _straddleSellEx.spreadNature = -1;   //for selling the straddle..
                    
                    }


                    //need to buy Hedges as well..
                    if (lastRebBhav == 0)
                    {
                        lastRebBhav = curSynthFut;
                
                        int QtyToHedge = Input_OrderLotSize * 2;

                        ceHedgeOption = getOptionObject(StrikePriceToSell + Input_HedgeGap, OptionType.CE);
                        peHedgeOption = getOptionObject(StrikePriceToSell - Input_HedgeGap, OptionType.PE);

                        TraceLogInfo(String.Format("Taking Hedges {[0]} CE , {[1]} PE", ceHedgeOption.StrikePrice, peHedgeOption.StrikePrice));
                        //It will take both hedge StrikeLong Positions...
                        ExecuteHedge(ceHedgeOption, peHedgeOption, QtyToHedge);
                    
                    }

                  
                    intialTransection = true;
                }

                //check for the Hedges...
                //if market comes to hedges then,,, manage It..
                //switch on certain flag and manage the long Hedges only then..
                
                ////*****
                //It will call it in loop...
                if(!(switchToHedge) && !(ceHedgeOption == null || peHedgeOption == null) && Input_flag ==1)
                //if(!(switchToHedge) && !(ceHedgeOption == null || peHedgeOption == null)&& (curSynthFut >= ceHedgeOption.StrikePrice || curSynthFut <= peHedgeOption.StrikePrice))
                {
                    switchToHedge = true;
                    intialTransection = true;
                    //It will sqr off the Short Position in the upper, above code block...
                }


                //also when Executor is in progress, plz return it form here..
                //Rolling the Strikes...
                //can do it for PUT     ***first cehck for the Rollover logic..***
                if(switchToHedge == true && !(ceHedgeOption == null) && Input_flag == 1)
                //if(switchToHedge == true && !(ceHedgeOption == null) &&curSynthFut >= ceHedgeOption.StrikePrice)
                {
                    
                    Options newStriketoHedgeCE = getATMCEStrike((ceHedgeOption.StrikePrice + Input_RollStrikeGap));
                    TraceLogInfo(String.Format("Rolling CE Strike from : [{0}] to : [{1]", ceHedgeOption.DisplayName, newStriketoHedgeCE.DisplayName));
                    RollOverStrike(ceHedgeCommand, newStriketoHedgeCE);
                }

                if (switchToHedge == true && !(peHedgeOption == null) && Input_flag == 1)
                //if(switchToHedge == true && !(peHedgeOption == null) &&curSynthFut <= peHedgeOption.StrikePrice)
                {

                    Options newStriketoHedgePE = getATMPEStrike((peHedgeOption.StrikePrice - Input_RollStrikeGap));
                    TraceLogInfo(String.Format("Rolling PE Strike from : [{0}] to : [{1]", peHedgeOption.DisplayName, newStriketoHedgePE.DisplayName));
                    RollOverStrike(peHedgeCommand, newStriketoHedgePE);
                }



                //Calculate the Greeks of the Positions...

                double _curGamma = 0, _curDelta = 0;

                foreach (KeyValuePair<long, IOrderCommandEx> entry in _orderExecutionMAP)
                {
                    IOrderCommandEx orderCommandEx = entry.Value;

                    Options options = (Options)orderCommandEx.IVInfo.IVInstrument.Instrument;



                    TimeSpan ts = options.ContractExpiration - currentLTPDT;
                    double totoalDays = ts.TotalDays;

                    double timeToExp = Math.Max(totoalDays / 365.0, 0.0001);


                    double strikeIV = 0, strikeDelta = 0, strikeGamma = 0;

                    //calculate Greeks here if position is open...
                    //Avoid wheen there is no feed...
                    //or in rare case curSynth is NaN..
                    if (orderCommandEx.IVInfo.Statistics.NetPosition != 0 && !double.IsNaN(curSynthFut) && curSynthFut != 0)
                    {


                        double curPrice = base.GetMarketDataContainer(options.InstrumentID).LastPrice;


                        //######################################//
                        //cREATE uNIT TEST AND MARK THE OUTPUT FOR THE BELOW VALUES INTRISTIC STRIKES...
                        if (options.OptionType == OptionType.CE)
                        {
                            
                            strikeIV = BlackScholes.GetCallInitialImpliedVolatility(curSynthFut, options.StrikePrice,
                                                timeToExp, 0, curPrice, 0) / 100;



                            strikeDelta = OptionsGreeks.GetCallOptionDelta(curSynthFut, options.StrikePrice,
                                                                                0, strikeIV, timeToExp, 0) * (orderCommandEx.IVInfo.Statistics.NetPosition);
                            

                        }
                        else if (options.OptionType == OptionType.PE)
                        {

                            strikeIV = BlackScholes.GetPutInitialImpliedVolatility(curSynthFut, options.StrikePrice,
                                                                            timeToExp, 0, curPrice, 0) / 100;

                            strikeDelta = OptionsGreeks.GetPutOptionDelta(curSynthFut, options.StrikePrice,
                                                                                0, strikeIV, timeToExp, 0) * (orderCommandEx.IVInfo.Statistics.NetPosition);
                          
                           
                        }

                        strikeGamma = OptionsGreeks.GetOptionGamma(curSynthFut, options.StrikePrice, 0, strikeIV, timeToExp, 0) * (orderCommandEx.IVInfo.Statistics.NetPosition);

                    }

                    _curDelta = _curDelta + strikeDelta;
                    _curGamma = _curGamma + strikeGamma;

                }

                

                //publish  and update the Parameters.... 
                Output_Delta = Math.Round(_curDelta, 2);
                Output_DN = delta_out;
                Output_Gamma = Math.Round(_curGamma, 2);
                Output_SynthFut = Math.Round(curSynthFut, 2);

                //Now current Tick will become previous tick...
                _previousLTPEventDT = currentLTPDT;

           
            }
            catch (Exception oEx)
            {
                TraceLogError("Exception Occured. Message : " + oEx.Message);
                TraceLogError(oEx.ToString());
                TraceLogError(oEx.StackTrace);
                Stop("Exception Occured. Message : " + oEx.Message);
            }
        }

        protected override void OnTrade(IVObject ivObject, TradeDataEventArgs eventArgs)
        {
            int position = eventArgs.NetPosition;
            _hasPosition = HasPosition();

            if (!_hasPosition)
            {
                Output_MTM = 0;
            }
        }

        private bool HasPosition()
        {
            foreach (KeyValuePair<long, IOrderCommandEx> entry in _orderExecutionMAP)
            {
                IOrderCommandEx orderCommandEx = entry.Value;

                if (orderCommandEx.IVInfo.Statistics.NetPosition != 0)
                {
                    Options options = (Options)orderCommandEx.IVInfo.IVInstrument.Instrument;
                    return true;
                }
            }

            return false;
        }


        //for Custome Qty execution....

        //with the new logic..(User for Delta hedging and Buying in this case.._).
        private void ExecuteCustPOrder(Options option, int lotSizeToTrade)
        {
            UpdateStrategyInfoText(string.Format("*****ExecuteOrder*****"));

            if (lotSizeToTrade > 500)
            {
                UpdateStrategyInfoText(string.Format("ExecuteOrder : The OrderAction is ignore as OrderLotSize[{0}] is greater than permissible limit.",
                    lotSizeToTrade));

                return;
            }


            try
            {
                IOrderCommandEx orderCommandEx = null;


                if (_orderExecutionMAP.TryGetValue(option.InstrumentID, out orderCommandEx) &&
                    orderCommandEx != null)
                {
                    orderCommandEx.SetIgnoreRefLimitPriceFlag(true);


                    //Always Buying , so positive qty
                    int orderQty = lotSizeToTrade * orderCommandEx.IVInfo.LotSize;
                    int desireQty = orderCommandEx.IVInfo.Statistics.NetPosition + orderQty;


                    if (orderCommandEx.OrderQuantity != Input_Max_Qty)
                        orderCommandEx.SetOrderQuantity(Input_Max_Qty);

                    if (Math.Abs(desireQty) > orderCommandEx.OrderQuantity)
                    {
                        UpdateStrategyInfoText(string.Format("ExecuteOrder : The OrderAction is ignore as totalPosition[{0}] is greater than permissible limit on this strike.",
                       desireQty));


                    }

                    if (option.OptionType == OptionType.CE)
                        _callOptionsMarketDataContainer = base.GetMarketDataContainer(option.InstrumentID);
                    else if (option.OptionType == OptionType.PE)
                        _putOptionsMarketDataContainer = base.GetMarketDataContainer(option.InstrumentID);


                    orderCommandEx.SetPosition(0, desireQty);

                    UpdateStrategyInfoText(string.Format("Executing Order[{0}]. OrderQty: {1}",
                        option.DisplayName,
                        orderQty));

                    Thread.Sleep(10);
                }
                else
                {
                    UpdateStrategyInfoText(string.Format("Executing Order[{0}]. Failed. Order not found in execution MAP",
                       option.DisplayName));
                }

            }
            catch (Exception ex)
            {
                TraceLogError(string.Format("Exception in ExecuteOrder: {0}", ex.Message));
            }
        }

      
        private IVInfo CreateRuntimeIVInfo(Options options)
        {
            try
            {
                IVInfo optionsIVInfo;
                if (_ivInfoMAP.TryGetValue(options.InstrumentID, out optionsIVInfo) && optionsIVInfo != null)
                {
                    return optionsIVInfo;
                }

                IVObject optionIVObject = new IVObject(options.DisplayName, options.DisplayName, true,
                                                                InstrumentType.Options, MarketDataType.All, OrderEventType.All);

                optionsIVInfo = base.GetIVInfo(optionIVObject);


                if (optionsIVInfo == null)
                {
                    string errorString = string.Empty;
                    if (!base.RegisterRuntimeIVObject(optionIVObject, options, out errorString))
                    {
                        throw (new Exception(string.Format("Could not register Runtime IVObject[{0}].", options.DisplayName)));
                    }

                    optionsIVInfo = base.GetIVInfo(optionIVObject);
                }

                _ivInfoMAP[options.InstrumentID] = optionsIVInfo;

                return optionsIVInfo;
            }
            catch (Exception exp)
            {
                base.TraceLogError("CreateRuntimeIVObject : " + exp.Message);
            }

            return null;
        }
        private void CreateRuntimeIVObject(List<Options> filteredOptions)
        {
            try
            {
                foreach (Options options in filteredOptions)
                {
                    // Create a smart order executor for each options
                    IOrderCommandEx orderExecutor = null;

                    if (_orderExecutionMAP.TryGetValue(options.InstrumentID, out orderExecutor) && orderExecutor != null)
                    {
                        continue;
                    }

                    IVObject optionIVObject = new IVObject(options.DisplayName, options.DisplayName, true,
                                                                    InstrumentType.Options, MarketDataType.All, OrderEventType.All);

                    IVInfo optionsIVInfo = base.GetIVInfo(optionIVObject);


                    if (optionsIVInfo == null)
                    {
                        string errorString = string.Empty;
                        if (!base.RegisterRuntimeIVObject(optionIVObject, options, out errorString))
                        {
                            throw (new Exception(string.Format("Could not register Runtime IVObject[{0}].", options.DisplayName)));
                        }

                        optionsIVInfo = base.GetIVInfo(optionIVObject);
                    }

                    Instrument iv = _ivInfoFuturesInstrument.IVInstrument.Instrument;
                    iv.ExtendedMarketProperties.TryGetValue("FreezeQuantity", out instProp_freezeMaxQty);
                    int maxFreezeQty = int.Parse(instProp_freezeMaxQty.Value) - 1;

                    if (!_orderExecutionMAP.TryGetValue(options.InstrumentID, out orderExecutor))
                    {
                        orderExecutor = this.GetSmartOrderExecutor("IVSmartOrderEX_" + options.DisplayName, optionIVObject);
                        _orderExecutionMAP[options.InstrumentID] = orderExecutor;
                        orderExecutor.SetPriceDerivationAlgo(new PriceFinderAlgoX1(optionsIVInfo.IVInstrument.InstrumentID, optionsIVInfo.IVObject.Name));
                        orderExecutor.SetQuantityDerivationAlgo(new QuantitySliceX1(optionsIVInfo.IVInstrument.InstrumentID,
                            optionsIVInfo.IVObject.Name, maxFreezeQty));
                        orderExecutor.SetTriggerEntryAlgo(new TriggerEntryAlgoX1(optionsIVInfo.IVInstrument.InstrumentID, optionsIVInfo.IVInstrument.IVObjectName));
                        orderExecutor.SetIncDecOrderQuantityFactor(50);

                        if (!Input_ClientID.Equals(string.Empty) && !Input_ClientID.Equals("1"))
                        {
                            UserExchangeProperty userExchangeProp = new UserExchangeProperty();
                            userExchangeProp.ExchangeClientID = Input_ClientID;

                            orderExecutor.SetUserExchangeProperty(userExchangeProp);
                        }

                        UpdateStrategyInfoText(string.Format("Creating a RunTimeIV: {0}", options.DisplayName));
                    }
                }
            }
            catch (Exception exp)
            {
                base.TraceLogError("CreateRuntimeIVObject : " + exp.Message);
                TraceLogError(exp.StackTrace);
                TraceLogError(exp.Message);
            }
        }

        public void UpdateStrategyInfoText(string infoText)
        {
            StrategyExecutionMessage strategyExecutionMessageObject = null;
            strategyExecutionMessageObject = base.CreateExecutionMessage(StrategyExecutionMessageType.Info, infoText);
            base.TraceStrategyExecutionMessage(strategyExecutionMessageObject);

            base.TraceLogInfo(strategyExecutionMessageObject.ExecutionMessage);
        }

        protected override void OnDispose()
        {
            if (!IsInitialized)
                return;
            if (base.GetStrategyInstanceInfo() != null)
            {
                TraceLogInfo("Strategy Instance [" + base.GetStrategyInstanceInfo().InstanceName + "] Disposing.");
            }
        }

    }
    
}




