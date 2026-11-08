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
        public double? ThresholdAccumulative { get; set; }
        public string AlertStatus { get; set; } = "NORMAL";
        public string LatestTs { get; set; } = string.Empty;
        public int? MinutesSinceLastReading { get; set; }
    }

    public class SlopeSensorRawDto
    {
        public string AlertStatus { get; set; } = "NORMAL";
        public string LatestTs { get; set; } = string.Empty;
        public int? MinutesSinceLastReading { get; set; }
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
}

