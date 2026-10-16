using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AlgoTrading.Models.Rules
{
    public enum IndicatorFunction
    {
        SimpleMovingAverage,
        ExponentialMovingAverage,
        RelativeStrengthIndex,
        BollingerMiddleBand,
        BollingerUpperBand,
        BollingerLowerBand,
        SuperTrend,
        DoubleExponentialMovingAverage,
        TripleExponentialMovingAverage,
        WeightedMovingAverage,
        HullMovingAverage,
        MovingAverageConvergenceDivergence,
        AverageTrueRange,
        AverageDirectionalIndex,
        VolumeWeightedAveragePrice,
        OnBalanceVolume,
        Stochastic,
        CommodityChannelIndex,
        VolumeSimpleMovingAverage,
        High52Week,
        Low52Week,
        RateOfChange,
        Momentum,
        TripleExponentialAverage,
        KeltnerChannel,
        DonchianChannel
    }
}


