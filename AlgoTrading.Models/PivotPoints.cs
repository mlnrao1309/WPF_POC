using System;

namespace AlgoTrading.Models
{
    public struct PivotPoints
    {
        private readonly PivotPointType storedType;

        public PivotPointType Type
        {
            get
            {
                return this.storedType;
            }
        }
        private readonly double storedHigh;

        public double High
        {
            get
            {
                return this.storedHigh;
            }
        }
        private readonly double storedLow;

        public double Low
        {
            get
            {
                return this.storedLow;
            }
        }
        private readonly double storedClose;

        public double Close
        {
            get
            {
                return this.storedClose;
            }
        }

        private readonly double storedPP;

        public double PP
        {
            get
            {
                return this.storedPP;
            }
        }
        private readonly double storedS1;

        public double S1
        {
            get
            {
                return this.storedS1;
            }
        }
        private readonly double storedS2;

        public double S2
        {
            get
            {
                return this.storedS2;
            }
        }
        private readonly double storedS3;

        public double S3
        {
            get
            {
                return this.storedS3;
            }
        }
        private readonly double storedS4;

        public double S4
        {
            get
            {
                return this.storedS4;
            }
        }
        private readonly double storedS5;

        public double S5
        {
            get
            {
                return this.storedS5;
            }
        }

        private readonly double storedR1;

        public double R1
        {
            get
            {
                return this.storedR1;
            }
        }
        private readonly double storedR2;

        public double R2
        {
            get
            {
                return this.storedR2;
            }
        }
        private readonly double storedR3;

        public double R3
        {
            get
            {
                return this.storedR3;
            }
        }
        private readonly double storedR4;

        public double R4
        {
            get
            {
                return this.storedR4;
            }
        }
        private readonly double storedR5;

        public double R5
        {
            get
            {
                return this.storedR5;
            }
        }

        private readonly PivotTimeframe storedTimeframe;

        public PivotTimeframe Timeframe
        {
            get
            {
                return this.storedTimeframe;
            }
        }

        public PivotPoints(double high, double low, double close, PivotPointType pivotPointType, PivotTimeframe pivotTimeframe)
        {
            this.storedHigh = high;
            this.storedLow = low;
            this.storedClose = close;
            this.storedType = pivotPointType;
            this.storedTimeframe = pivotTimeframe;
            // Calculate pivot points based on the type
            switch (this.Type)
            {
                case PivotPointType.Classic:
                    this.storedPP = (High + Low + Close) / 3.0;
                    double range = High - Low;

                    this.storedR1 = (2 * PP) - Low;
                    this.storedS1 = (2 * PP) - High;

                    this.storedR2 = PP + range;
                    this.storedS2 = PP - range;

                    this.storedR3 = High + 2 * (PP - Low);
                    this.storedS3 = Low - 2 * (High - PP);

                    this.storedR4 = R3 + range;
                    this.storedS4 = S3 - range;

                    this.storedR5 = R4 + range;
                    this.storedS5 = S4 - range;
                    break;
                default:
                    throw new NotImplementedException($"Pivot point calculation for {this.Type} is not implemented.");
            }
        }

    }
}
