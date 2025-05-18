using System;
using System.Collections.Generic;
using System.Management;
using System.Threading;
using Windows.Devices.Sensors;

namespace AutoBrightnessAdjuster
{
    class Program
    {
        // History of last 5 lux readings
        private static readonly Queue<double> _luxHistory = new Queue<double>(5);
        static LightSensor _sensor;

        // Smooth transition parameters
        const int transitionDurationMs = 5000;
        const int transitionStepMs = 10;

        // For periodic garbage collection
        private static DateTime _lastGcTime = DateTime.Now;

        static void Main(string[] args)
        {
            Console.WriteLine("Starting AutoBrightnessAdjuster...");
            _sensor = LightSensor.GetDefault();
            if (_sensor == null)
            {
                Console.WriteLine("No ambient light sensor found. Exiting.");
                return;
            }
            Console.WriteLine("Ambient light sensor detected.");

            while (true)
            {
                var reading = _sensor.GetCurrentReading();
                if (reading != null)
                {
                    double lux = reading.IlluminanceInLux;
                    EnqueueLux(lux);

                    int targetBrightness = MapLuxToBrightness(lux, 1000.0, 0.6);
                    Console.WriteLine($"Ambient light: {lux:F1} lux -> Setting brightness to {targetBrightness}%");
                    SmoothSetBrightness((byte)targetBrightness, transitionDurationMs, transitionStepMs);
                }

                // Periodic garbage collection
                if (DateTime.Now - _lastGcTime > TimeSpan.FromMinutes(1))
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    _lastGcTime = DateTime.Now;
                }

                Thread.Sleep(2000);
            }
        }

        static void EnqueueLux(double lux)
        {
            _luxHistory.Enqueue(lux);
            if (_luxHistory.Count > 5)
            {
                _luxHistory.Dequeue();
            }
        }

        static int MapLuxToBrightness(double lux, double maxLux, double gamma)
        {
            double clamped = Math.Min(lux, maxLux);
            double normalized = clamped / maxLux;
            double corrected = Math.Pow(normalized, gamma);
            int perc = (int)Math.Round(corrected * 100);
            return Math.Max(1, Math.Min(100, perc));
        }

        static byte GetCurrentBrightness()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("root\\wmi", "SELECT CurrentBrightness FROM WmiMonitorBrightness");
                foreach (ManagementObject mo in searcher.Get())
                {
                    return (byte)mo["CurrentBrightness"];
                }
            }
            catch { }
            return 0;
        }

        static ManagementObject GetBrightnessObject()
        {
            try
            {
                var scope = new ManagementScope("\\\\.\\root\\wmi");
                var query = new SelectQuery("WmiMonitorBrightnessMethods");
                using var searcher = new ManagementObjectSearcher(scope, query);
                foreach (ManagementObject mo in searcher.Get())
                {
                    return mo;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to get brightness WMI object: {ex.Message}");
            }
            return null;
        }

        static void SmoothSetBrightness(byte target, int durationMs, int stepMs)
        {
            byte current = GetCurrentBrightness();
            if (current == target) return;

            int steps = Math.Max(1, durationMs / stepMs);
            double delta = (target - current) / (double)steps;

            var brightnessObject = GetBrightnessObject();
            if (brightnessObject == null)
            {
                Console.WriteLine("Unable to get WMI brightness object.");
                return;
            }

            for (int i = 1; i <= steps; i++)
            {
                byte next = (byte)Math.Round(current + delta * i);
                try
                {
                    brightnessObject.InvokeMethod("WmiSetBrightness", new object[] { 1, next });
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to set brightness: {ex.Message}");
                    break;
                }
                Thread.Sleep(stepMs);
            }

            brightnessObject.Dispose();
        }
    }
}
