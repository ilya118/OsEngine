using System;
using System.Collections.Generic;
using OsEngine.Entity;
using OsEngine.Indicators;
using OsEngine.OsTrader.Panels;
using OsEngine.OsTrader.Panels.Attributes;
using OsEngine.OsTrader.Panels.Tab;
using OsEngine.Market;
using System;
using OsEngine.Logging;
using System.Net;
using System.Threading;
using OsEngine.Market.Servers;

namespace OsEngine.Robots.AO
{
    [Bot("A_Strategy_MACD_4")] // We create an attribute so that we don't write anything to the BotFactory
    public class A_Strategy_MACD_4 : BotPanel
    {
        private BotTabSimple _tab;

        // Basic Settings
        private StrategyParameterString Regime;
        public StrategyParameterString VolumeType;
        public StrategyParameterDecimal Volume;
        //private StrategyParameterDecimal Slippage;
        private StrategyParameterInt OnlyLong;
        private StrategyParameterTimeOfDay StartTradeTime;
        private StrategyParameterTimeOfDay EndTradeTime;

        // EMA MACD Indicator setting 
        private StrategyParameterInt _EmaPeriod;
        private StrategyParameterInt MACD_Coeffitient;
        private StrategyParameterDecimal LongLevelMACD;
        private StrategyParameterDecimal ShortLevelMACD;

        // ATR Indicator setting 
        public StrategyParameterInt AtrLength;
        public StrategyParameterBool AtrFilterIsOn;
        public StrategyParameterDecimal AtrGrowPercent;
        public StrategyParameterInt AtrGrowLookBack;

        // Indicator
        private Aindicator _Ema;
        private Aindicator _MACD;
        private Aindicator _pc;
        private Aindicator _atr;
        private Aindicator _volatilityStages;


        // The last value of the indicator
        private decimal _lastEma;
        private decimal _lastMacdHistogram;
        private decimal _lastMacdSignal;

        // The prev value of the indicator
        private decimal _prevMacdHistogram;

        // Переменные для телеграм сообщений Long|Short Buy|Sell
        private string positionDirection;
        private string positionState;

        // Переменные для Volatility Stages
        public StrategyParameterBool VolatilityFilterIsOn;
        public StrategyParameterString VolatilityStageToTrade;
        public StrategyParameterInt VolatilitySlowSmaLength;
        public StrategyParameterInt VolatilityFastSmaLength;
        public StrategyParameterDecimal VolatilityChannelDeviation;

        // Exit
        private StrategyParameterDecimal TrailingValue;

        // Was there a connection to server
        private bool _isConnect;

        // Telegram Allerts Settings
        private StrategyParameterString AlertsRegime;

        public A_Strategy_MACD_4(string name, StartProgram startProgram) : base(name, startProgram)
        {
            TabCreate(BotTabType.Simple);
            _tab = TabsSimple[0];

            // Indicator setting
            _EmaPeriod = CreateParameter("Ema Length", 30, 30, 480, 30, "Base");
            MACD_Coeffitient = CreateParameter("MACD Coeffitient", 1, 1, 4, 1, "Base");
            LongLevelMACD = CreateParameter("MACD Long Level", 0m, 0, 10, 2, "Base");
            ShortLevelMACD = CreateParameter("MACD Short Level", 0m, -10, 0, 2, "Base");

            // ATR
            AtrLength = CreateParameter("Atr length", 25, 10, 80, 3, "Base");
            AtrGrowPercent = CreateParameter("Atr grow percent", 3, 1.0m, 50, 4, "Base");
            AtrGrowLookBack = CreateParameter("Atr grow look back", 20, 1, 50, 4, "Base");

            // Volatility Stages
            VolatilityStageToTrade = CreateParameter("Volatility stage to trade", "2", new[] { "1", "2", "3", "4" }, "Base");
            VolatilitySlowSmaLength = CreateParameter("Volatility slow sma length", 25, 10, 80, 3, "Base");
            VolatilityFastSmaLength = CreateParameter("Volatility fast sma length", 7, 10, 80, 3, "Base");
            VolatilityChannelDeviation = CreateParameter("Volatility channel deviation", 0.5m, 1.0m, 50, 4, "Base");

            // Exit
            TrailingValue = CreateParameter("Stop Value", 0m, 1, 2, 0.5m, "Base");

            // Basic setting
            Regime = CreateParameter("Regime", "On", new[] { "On", "OnlyLong", "OnlyShort" }, "Base");
            AtrFilterIsOn = CreateParameter("Atr filter is on", false, "Base");
            VolatilityFilterIsOn = CreateParameter("Volatility filter is on", false, "Base");
            AlertsRegime = CreateParameter("Alerts Regime", "Off", new[] { "Off", "On" }, "Base");
			//OnlyLong = CreateParameter("OnlyLong", 0, 0, 1, 1, "Base");
            VolumeType = CreateParameter("Volume type", "Contract currency", new[] { "Contracts", "Contract currency", "Deposit percent" }, "Base");
            Volume = CreateParameter("Volume", 50000, 1.0m, 50, 4, "Base");
            StartTradeTime = CreateParameterTimeOfDay("Start Trade Time", 10, 32, 0, 0, "Base");
            EndTradeTime = CreateParameterTimeOfDay("End Trade Time", 18, 25, 0, 0, "Base");

            // Create indicator Ema
            _Ema = IndicatorsFactory.CreateIndicatorByName("Ema", name + "EMA", false);
            _Ema = (Aindicator)_tab.CreateCandleIndicator(_Ema, "Prime");
            ((IndicatorParameterInt)_Ema.Parameters[0]).ValueInt = _EmaPeriod.ValueInt;
            _Ema.Save();

            // Create indicator MACD
            _MACD = IndicatorsFactory.CreateIndicatorByName("MACD", name + "MACD", false);
            _MACD = (Aindicator)_tab.CreateCandleIndicator(_MACD, "MACDArea");
            ((IndicatorParameterInt)_MACD.Parameters[0]).ValueInt = 12 * MACD_Coeffitient.ValueInt;
            ((IndicatorParameterInt)_MACD.Parameters[1]).ValueInt = 26 * MACD_Coeffitient.ValueInt;
            ((IndicatorParameterInt)_MACD.Parameters[2]).ValueInt = 9 * MACD_Coeffitient.ValueInt;
            _MACD.Save();

            // Create indicator ATR
            _atr = IndicatorsFactory.CreateIndicatorByName("ATR", name + "Atr", false);
            _atr = (Aindicator)_tab.CreateCandleIndicator(_atr, "AtrArea");
            _atr.ParametersDigit[0].Value = AtrLength.ValueInt;

            // Create indicator Volatility Stages
            _volatilityStages = IndicatorsFactory.CreateIndicatorByName("VolatilityStagesAW", name + "VolatilityStages", false);
            _volatilityStages = (Aindicator)_tab.CreateCandleIndicator(_volatilityStages, "VolatilityStagesArea");
            _volatilityStages.ParametersDigit[0].Value = VolatilitySlowSmaLength.ValueInt;
            _volatilityStages.ParametersDigit[1].Value = VolatilityFastSmaLength.ValueInt;
            _volatilityStages.ParametersDigit[2].Value = VolatilityChannelDeviation.ValueDecimal;

            // Subscribe to the indicator update event
            ParametrsChangeByUser += A_Strategy_MACD_4_ParametrsChangeByUser; ;

            // Subscribe to the candle finished event
            _tab.CandleFinishedEvent += _tab_CandleFinishedEvent;

            // Subscribe to event of successful opening of a position
            _tab.PositionOpeningSuccesEvent += _tab_PositionOpeningSuccesEvent;

            // Subscribe to event of successful closing of a position
            _tab.PositionClosingSuccesEvent += _tab_PositionClosingSuccesEvent;

            // If this is a real connection (OsTrader) - run server connection check in a separate thread
            if (startProgram == StartProgram.IsOsTrader)
            {
                Thread worker = new Thread(CheckConnect);
                worker.IsBackground = true;
                worker.Start();
            }
        }

        private void _tab_PositionOpeningSuccesEvent(Position position)
        {
            if (AlertsRegime.ValueString == "On")
            {
                if (position.Direction == Side.Buy)
                {
                    positionDirection = "Long";
                    positionState = "Buy";
                }
                if (position.Direction == Side.Sell)
                {
                    positionDirection = "Short";
                    positionState = "Sell";
                }
                string message = "🟧 %23" + position.Number + "_" + _tab.NameStrategy + " " + position.State +
                "\r\n" + "Позиция: " + positionDirection +
                "\r\n" + "Направление: " + positionState +
                "\r\n" + "Бумага: $" + position.SecurityName +
                "\r\n" + "Цена входа: " + position.EntryPrice +
                "\r\n" + "Объём: " + position.MaxVolume +
                "\r\n" + "Cумма сделки: " + Math.Round(position.EntryPrice * position.MaxVolume * position.Lots, 2) +
                "\r\n" + "Комиссия: " + Math.Round(position.ComissionValue * position.EntryPrice * position.MaxVolume * position.Lots / 100, 2);

                SendTelegramMessageAsync(message);
            }
        }

        private void _tab_PositionClosingSuccesEvent(Position position)
        {
            if (AlertsRegime.ValueString == "On")
            {
                if (position.Direction == Side.Buy)
                {
                    positionDirection = "Long";
                    positionState = "Sell";
                }
                if (position.Direction == Side.Sell)
                {
                    positionDirection = "Short";
                    positionState = "Buy";
                }
                string emotion = position.ProfitOperationPunkt * position.MaxVolume * position.Lots - position.ComissionValue * (position.EntryPrice + position.ClosePrice) * position.MaxVolume * position.Lots / 100 > 0 ? "😄" : "🤬";

                string message = "🟩 %23" + position.Number + "_" + _tab.NameStrategy + " " + position.State +
                "\r\n" + "Позиция: " + positionDirection +
                "\r\n" + "Направление: " + positionState +
                "\r\n" + "Бумага: $" + position.SecurityName +
                "\r\n" + "Цена входа: " + position.EntryPrice +
                "\r\n" + "Цена выхода: " + position.ClosePrice +
                "\r\n" + "Объём: " + position.MaxVolume +
                "\r\n" + "Cумма сделки: " + Math.Round(position.ClosePrice * position.MaxVolume * position.Lots, 2) +
                "\r\n" + "Профит проценты: " + Math.Round((position.ProfitOperationPunkt * position.MaxVolume * position.Lots - position.ComissionValue * (position.EntryPrice + position.ClosePrice) * position.MaxVolume * position.Lots / 100) / (position.EntryPrice * position.MaxVolume * position.Lots) * 100, 2) + "%" +
                "\r\n" + "Total Профит: " + Math.Round(position.ProfitOperationPunkt * position.MaxVolume * position.Lots - position.ComissionValue * (position.EntryPrice + position.ClosePrice) * position.MaxVolume * position.Lots / 100, 2) + " " + emotion +
                "\r\n" + "Total Комиссия: " + Math.Round(position.ComissionValue * (position.EntryPrice + position.ClosePrice) * position.MaxVolume * position.Lots / 100, 2);

                SendTelegramMessageAsync(message);
            }
        }

        private void A_Strategy_MACD_4_ParametrsChangeByUser()
        {
            ((IndicatorParameterInt)_Ema.Parameters[0]).ValueInt = _EmaPeriod.ValueInt;
            _Ema.Save();
            _Ema.Reload();
            ((IndicatorParameterInt)_MACD.Parameters[0]).ValueInt = 12 * MACD_Coeffitient.ValueInt;
            ((IndicatorParameterInt)_MACD.Parameters[1]).ValueInt = 26 * MACD_Coeffitient.ValueInt;
            ((IndicatorParameterInt)_MACD.Parameters[2]).ValueInt = 9 * MACD_Coeffitient.ValueInt;
            _MACD.Save();
            _MACD.Reload();

            if (_atr.ParametersDigit[0].Value != AtrLength.ValueInt)
            {
                _atr.ParametersDigit[0].Value = AtrLength.ValueInt;
                _atr.Reload();
                _atr.Save();
            }

            if (_volatilityStages.ParametersDigit[0].Value != VolatilitySlowSmaLength.ValueInt
                || _volatilityStages.ParametersDigit[1].Value != VolatilityFastSmaLength.ValueInt
                || _volatilityStages.ParametersDigit[2].Value != VolatilityChannelDeviation.ValueDecimal)
            {
                _volatilityStages.ParametersDigit[0].Value = VolatilitySlowSmaLength.ValueInt;
                _volatilityStages.ParametersDigit[1].Value = VolatilityFastSmaLength.ValueInt;
                _volatilityStages.ParametersDigit[2].Value = VolatilityChannelDeviation.ValueDecimal;

                _volatilityStages.Reload();
            }
        }

        // The name of the robot in OsEngine
        public override string GetNameStrategyType()
        {
            return "A_Strategy_MACD_4";
        }
        public override void ShowIndividualSettingsDialog()
        {
        }

        // Candle Finished Event
        private void _tab_CandleFinishedEvent(List<Candle> candles)
        {
            // If there are not enough candles to build an indicator, we exit
            if (candles.Count < _EmaPeriod.ValueInt ||
                candles.Count < 12 * MACD_Coeffitient.ValueInt ||
                candles.Count < 26 * MACD_Coeffitient.ValueInt ||
                candles.Count < 9 * MACD_Coeffitient.ValueInt)
            {
                return;
            }

            // If the time does not match, we leave
            if (StartTradeTime.Value > _tab.TimeServerCurrent ||
                EndTradeTime.Value < _tab.TimeServerCurrent)
            {
                return;
            }

            List<Position> openPositions = _tab.PositionsOpenAll;

            // If there are positions, then go to the position closing method
            if (openPositions == null || openPositions.Count == 0)
            {
                LogicOpenPosition(candles);
            }
            else
            {
                LogicClosePosition(candles, openPositions[0]);
            }
        }

        // Opening logic
        private void LogicOpenPosition(List<Candle> candles)
        {
            // The last value of the indicator
            _lastEma = _Ema.DataSeries[0].Last;
            _lastMacdHistogram = _MACD.DataSeries[0].Last;
            _lastMacdSignal = _MACD.DataSeries[2].Last;

            // The prev value of the indicator
            _prevMacdHistogram = _MACD.DataSeries[0].Values[_MACD.DataSeries[0].Values.Count - 2];

            List<Position> openPositions = _tab.PositionsOpenAll;

            if (openPositions == null || openPositions.Count == 0)
            {
                decimal lastPrice = candles[candles.Count - 1].Close;

                // Long
                if (lastPrice > _lastEma
                    && _prevMacdHistogram <= 0
                    && _lastMacdHistogram > 0
                    && _lastMacdSignal < LongLevelMACD.ValueDecimal
                    && Regime.ValueString != "OnlyShort")
                {
                    if (AtrFilterIsOn.ValueBool == true)
                    {
                        if (_atr.DataSeries[0].Values.Count - 1 - AtrGrowLookBack.ValueInt <= 0)
                        {
                            return;
                        }

                        decimal atrLast = _atr.DataSeries[0].Values[_atr.DataSeries[0].Values.Count - 1];
                        decimal atrLookBack =
                            _atr.DataSeries[0].Values[_atr.DataSeries[0].Values.Count - 1 - AtrGrowLookBack.ValueInt];

                        if (atrLast == 0
                            || atrLookBack == 0)
                        {
                            return;
                        }

                        decimal atrGrowPercent = atrLast / (atrLookBack / 100) - 100;

                        if (atrGrowPercent < AtrGrowPercent.ValueDecimal)
                        {
                            return;
                        }
                    }

                    if (VolatilityFilterIsOn.ValueBool == true)
                    {
                        decimal stage = _volatilityStages.DataSeries[0].Values[_volatilityStages.DataSeries[0].Values.Count - 2];

                        if (stage != VolatilityStageToTrade.ValueString.ToDecimal())
                        {
                            return;
                        }
                    }

                    _tab.BuyAtMarket(GetVolume(_tab));
                }

                if (lastPrice < _lastEma
                    && _prevMacdHistogram >= 0
                    && _lastMacdHistogram < 0
                    && _lastMacdSignal > ShortLevelMACD.ValueDecimal
                    && Regime.ValueString != "OnlyLong")
                {
                    if (AtrFilterIsOn.ValueBool == true)
                    {
                        if (_atr.DataSeries[0].Values.Count - 1 - AtrGrowLookBack.ValueInt <= 0)
                        {
                            return;
                        }

                        decimal atrLast = _atr.DataSeries[0].Values[_atr.DataSeries[0].Values.Count - 1];
                        decimal atrLookBack =
                            _atr.DataSeries[0].Values[_atr.DataSeries[0].Values.Count - 1 - AtrGrowLookBack.ValueInt];

                        if (atrLast == 0
                            || atrLookBack == 0)
                        {
                            return;
                        }

                        decimal atrGrowPercent = atrLast / (atrLookBack / 100) - 100;

                        if (atrGrowPercent < AtrGrowPercent.ValueDecimal)
                        {
                            return;
                        }
                    }

                    if (VolatilityFilterIsOn.ValueBool == true)
                    {
                        decimal stage = _volatilityStages.DataSeries[0].Values[_volatilityStages.DataSeries[0].Values.Count - 2];

                        if (stage != VolatilityStageToTrade.ValueString.ToDecimal())
                        {
                            return;
                        }
                    }

                    _tab.SellAtMarket(GetVolume(_tab));
                }
            }
        }


        // Logic close position
        private void LogicClosePosition(List<Candle> candles, Position position)
        {
            List<Position> openPositions = _tab.PositionsOpenAll;
            Position pos = openPositions[0];

            decimal stopPrice;

            for (int i = 0; openPositions != null && i < openPositions.Count; i++)
            {
                Position positions = openPositions[i];

                if (positions.State != PositionStateType.Open)
                {
                    continue;
                }

                if (openPositions[i].Direction == Side.Buy) // If the direction of the position is purchase
                {
                    decimal lov = candles[candles.Count - 1].Low;
                    stopPrice = lov - lov * TrailingValue.ValueDecimal / 100;
                }
                else // If the direction of the position is sale
                {
                    decimal high = candles[candles.Count - 1].High;
                    stopPrice = high + high * TrailingValue.ValueDecimal / 100;
                }
                _tab.CloseAtTrailingStop(pos, stopPrice, stopPrice);
            }
        }

        // Method for calculating the volume of entry into a position
        private decimal GetVolume(BotTabSimple tab)
        {
            decimal volume = 0;

            if (VolumeType.ValueString == "Contracts")
            {
                volume = Volume.ValueDecimal;
            }
            else if (VolumeType.ValueString == "Contract currency")
            {
                decimal contractPrice = tab.PriceBestAsk;
                volume = Volume.ValueDecimal / contractPrice;

                if (StartProgram == StartProgram.IsOsTrader)
                {
                    IServerPermission serverPermission = ServerMaster.GetServerPermission(tab.Connector.ServerType);

                    if (serverPermission != null &&
                        serverPermission.IsUseLotToCalculateProfit &&
                    tab.Security.Lot != 0 &&
                        tab.Security.Lot > 1)
                    {
                        volume = Volume.ValueDecimal / (contractPrice * tab.Security.Lot);
                    }

                    volume = Math.Round(volume, tab.Security.DecimalsVolume);
                }
                else // Tester or Optimizer
                {
                    volume = Math.Round(volume, 6);
                }
            }
            else if (VolumeType.ValueString == "Deposit percent")
            {
                Portfolio myPortfolio = tab.Portfolio;

                if (myPortfolio == null)
                {
                    return 0;
                }

                decimal portfolioPrimeAsset = 0;

                List<PositionOnBoard> positionOnBoard = myPortfolio.GetPositionOnBoard();

                if (positionOnBoard == null)
                {
                    return 0;
                }

                for (int i = 0; i < positionOnBoard.Count; i++)
                {

                    portfolioPrimeAsset = positionOnBoard[i].ValueCurrent;
                    break;

                }

                if (portfolioPrimeAsset == 0)
                {
                    SendNewLogMessage("Can`t found portfolio ", Logging.LogMessageType.Error);
                    return 0;
                }

                decimal moneyOnPosition = portfolioPrimeAsset * (Volume.ValueDecimal / 100);

                decimal qty = moneyOnPosition / tab.PriceBestAsk / tab.Security.Lot;

                if (tab.StartProgram == StartProgram.IsOsTrader)
                {
                    qty = Math.Round(qty, tab.Security.DecimalsVolume);
                }
                else
                {
                    qty = Math.Round(qty, 7);
                }

                return qty;
            }

            return volume;
        }

        // Method sending message to Telegram
        private async void SendTelegramMessageAsync(string message)
        {
            // Collecting query string
            string reqStr = "https://api.telegram.org/bot7192179868:AAGwaGc9LZGV_hjlI-RGRGChZYsomswYm2o/sendMessage?chat_id=-1002450626729&text=" + message;

            try
            {
                WebRequest request = WebRequest.Create(reqStr);
                using (await request.GetResponseAsync()) { }
            }
            catch (Exception ex)
            {
                SendNewLogMessage("Check that Telegram ID and Bot Token are entered correctly", LogMessageType.Error);
            }
        }

        // Method of checking connection to server
        private void CheckConnect()
        {
            while (true)
            {
                // Check server status every 60 seconds
                Thread.Sleep(60000);

                // Check connection to server starts working after first connection to it
                if (_tab.ServerStatus == Market.Servers.ServerConnectStatus.Connect && _isConnect == false)
                {
                    _isConnect = true;
                }

                if (AlertsRegime.ValueString == "On")
                {
                    if (_tab.ServerStatus == Market.Servers.ServerConnectStatus.Disconnect && _isConnect == true)
                    {
                        string message = "Connection to server is lost (" + _tab.NameStrategy + ")!";
                        SendTelegramMessageAsync(message);
                        // Attention - it will spam every 10 seconds until you connect (process it additionally or turn it off)
                    }
                }
            }
        }
    }
}