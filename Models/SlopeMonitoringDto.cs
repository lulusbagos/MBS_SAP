using System;
using System.Collections.Generic;

namespace MBS_SAP.Models
{
    public class SlopeDeviceDto
    {
        public int DeviceId { get; set; }
        public string DeviceCode { get; set; } = string.Empty;
        public string DeviceName { get; set; } = string.Empty;
        public string SiteLocation { get; set; } = string.Empty;
        public string Status { get; set; } = "NORMAL";
        public string LastReadingTs { get; set; } = string.Empty;
        public double MaxDeltaPreviousScan { get; set; }
        public string MovementTrend { get; set; } = "STABLE";
        public List<SlopeSensorItemDto> Sensors { get; set; } = new();
    }

    public class SlopeSensorItemDto
    {
        public int SensorId { get; set; }
        public int SensorSlot { get; set; }
        public string SensorCode { get; set; } = string.Empty;
        public string SensorName { get; set; } = string.Empty;
        public string SensorTypeName { get; set; } = "Displacement";
        public string UnitSuffix { get; set; } = "mm";
        public string LocationDesc { get; set; } = string.Empty;
        public double BaselineValue { get; set; }
        public double LatestValue { get; set; }
        public double DeltaValue { get; set; }
        public double AccumulativeDisplacement { get; set; }
        public double LateralDisplacement { get; set; }
        public double VelocityPerHour { get; set; }
        public double VelocityPerDay { get; set; }
        public double? ThresholdVelocity { get; set; }
        public double? ThresholdAccumulative { get; set; } = 200.0;
        public string AlertStatus { get; set; } = "NORMAL";
        public string LatestTs { get; set; } = string.Empty;
        public int? MinutesSinceLastReading { get; set; }
        public double PreviousValue { get; set; }
        public double DeltaPreviousScan { get; set; }
        public string MovementTrend { get; set; } = "STABLE"; // UP, DOWN, STABLE
        
        // Titik Koordinat & Mapping Geoteknik
        public string UniqueId { get; set; } = string.Empty;
        public string ExtensoId { get; set; } = string.Empty;
        public string Area { get; set; } = string.Empty;
        public string LocationCoord { get; set; } = string.Empty;
        public double? CoordinateN { get; set; }
        public double? CoordinateE { get; set; }
    }

    public class SlopeSensorRawDto
    {
        public int SensorId { get; set; }
        public string AlertStatus { get; set; } = "NORMAL";
        public string LatestTs { get; set; } = string.Empty;
        public int? MinutesSinceLastReading { get; set; }
        public double LatestValue { get; set; }
        public double VelocityPerHour { get; set; }
        public string SensorName { get; set; } = string.Empty;
        public string DeviceName { get; set; } = string.Empty;
    }

    public class SlopeAlertDto
    {
        public long AlertId { get; set; }
        public string DeviceName { get; set; } = string.Empty;
        public string SiteLocation { get; set; } = string.Empty;
        public string SensorName { get; set; } = string.Empty;
        public string AlertLevel { get; set; } = "INFO";
        public string MetricType { get; set; } = string.Empty;
        public double MeasuredValue { get; set; }
        public double ThresholdValue { get; set; }
        public string AlertMessage { get; set; } = string.Empty;
        public string ReadingTs { get; set; } = string.Empty;
    }

    public class SlopeHourlyTrendDto
    {
        public int DeviceId { get; set; }
        public string DeviceName { get; set; } = string.Empty;
        public int SensorId { get; set; }
        public string SensorName { get; set; } = string.Empty;
        public string UnitSuffix { get; set; } = "mm";
        public string HourStr { get; set; } = string.Empty;
        public double AvgAccumulative { get; set; }
        public double MaxVelocity { get; set; }
    }

    public class SlopeAiPredictionDto
    {
        public double RiskScore { get; set; } // 0 - 100%
        public string RiskLevel { get; set; } = "LOW"; // LOW, MODERATE, HIGH, CRITICAL
        public string CreepRegime { get; set; } = "Primary (Stabil)";
        public double AccelerationRate { get; set; } // mm/jam^2
        public string TimeToFailureForecast { get; set; } = "N/A (Lereng Stabil)";
        public string MostCriticalSensor { get; set; } = string.Empty;
        public double ConfidenceScore { get; set; } = 94.8;
        public string AiRecommendation { get; set; } = string.Empty;
        public List<SlopeSensorPredictionItemDto> TopSensorsPrediction { get; set; } = new();
    }

    public class SlopeSensorPredictionItemDto
    {
        public int SensorId { get; set; }
        public string SensorName { get; set; } = string.Empty;
        public string DeviceName { get; set; } = string.Empty;
        public double CurrentVelocity { get; set; }
        public double Acceleration { get; set; }
        public double PredictedDisplacementNext6H { get; set; }
        public string TrendDirection { get; set; } = "STABLE"; // ACCELERATING, STABLE, DECELERATING
        public double RiskProbability { get; set; }
    }

    public class SlopeScanComparisonDto
    {
        public string LastScanTime { get; set; } = string.Empty;
        public int SecondsSinceLastScan { get; set; }
        public int SensorsMovingUp { get; set; }
        public int SensorsMovingDown { get; set; }
        public int SensorsStable { get; set; }
        public string MaxDeltaSensorName { get; set; } = string.Empty;
        public double MaxDeltaMm { get; set; }
        public string SummaryText { get; set; } = string.Empty;
    }

    public class SlopeSensorCoordDto
    {
        public int No { get; set; }
        public string UniqueId { get; set; } = string.Empty;
        public string ExtensoId { get; set; } = string.Empty;
        public string Area { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public double N { get; set; }
        public double E { get; set; }
    }
}

