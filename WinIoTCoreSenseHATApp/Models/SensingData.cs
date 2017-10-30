using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WinIoTCoreSenseHATApp.Models
{
    public class SenseHATSensorReading
    {
        public double Temperature { get; set; }
        public double Humidity { get; set; }
        public double Pressure { get; set; }
        public double AccelX { get; set; }
        public double AccelY { get; set; }
        public double AccelZ { get; set; }
        public double GyroX { get; set; }
        public double GyroY { get; set; }
        public double GyroZ { get; set; }
        public double MagX { get; set; }
        public double MagY { get; set; }
        public double MagZ { get; set; }
        public DateTime MeasuredTime { get; set; }
    }

    public class SensorReadingForIoT
    {
        public string deviceId { get; set; }
        public string msgId { get; set; }
        public double Temperature { get; set; }
        public double Humidity { get; set; }
        public double Pressure { get; set; }
        public double AccelX { get; set; }
        public double AccelY { get; set; }
        public double AccelZ { get; set; }
        public double GyroX { get; set; }
        public double GyroY { get; set; }
        public double GyroZ { get; set; }
        public double MagX { get; set; }
        public double MagY { get; set; }
        public double MagZ { get; set; }
        public DateTime MeasuredTime { get; set; }
    }
}
