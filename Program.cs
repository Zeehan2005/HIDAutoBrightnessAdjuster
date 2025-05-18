/*
AutoBrightnessAdjuster

A simple C# console application that reads ambient light sensor data and adjusts the screen brightness accordingly on Windows using gamma correction with smooth transitions.

Requirements:
- Windows 8 or later with an ambient light sensor.
- .NET 7.0 or later targeting Windows.
- Run the application with administrator privileges (needed to adjust brightness via WMI).

Usage:
1. Build the project via `dotnet build`.
2. Run the executable as Administrator via `dotnet run` or from the published EXE.
3. The app polls the light sensor every 2 seconds and maps measured lux to a brightness percentage using gamma correction, then smoothly transitions to the target brightness.

Gamma Mapping Logic:
- brightness = 100 * (clamp(lux, 0, maxLux) / maxLux) ^ gamma
- maxLux: reference maximum lux (e.g., 1000)
- gamma: exponent controlling curve (e.g., 0.6)

Smooth Transition:
- durationMs: total transition time (e.g., 500ms)
- stepMs: interval between brightness updates (e.g., 50ms)

Adjust parameters in MapLuxToBrightness and SmoothSetBrightness as needed.
*/

using System;
using System.Management;
using System.Threading;
using Windows.Devices.Sensors;

namespace AutoBrightnessAdjuster
{
    class Program
    {
        static LightSensor _sensor;
        static void Main(string[] args)
        {
            Console.WriteLine("Starting AutoBrightnessAdjuster with Gamma Correction and Smooth Transition...");
            _sensor = LightSensor.GetDefault();
            if (_sensor == null)
            {
                Console.WriteLine("No ambient light sensor found. Exiting.");
                return;
            }
            Console.WriteLine("Ambient light sensor detected.");

            const double maxLux = 1000.0;
            const double gamma = 0.6;
            const int transitionDurationMs = 5000;
            const int transitionStepMs = 10;

            while (true)
            {
                var reading = _sensor.GetCurrentReading();
                if (reading != null)
                {
                    double lux = reading.IlluminanceInLux;
                    int targetBrightness = MapLuxToBrightness(lux, maxLux, gamma);
                    Console.WriteLine($"Ambient light: {lux:F1} lux -> Target brightness: {targetBrightness}%");
                    SmoothSetBrightness((byte)targetBrightness, transitionDurationMs, transitionStepMs);
                }
                Thread.Sleep(2000);
            }
        }

        /// <summary>
        /// Maps ambient lux to brightness percentage [1-100] using gamma correction.
        /// </summary>
        static int MapLuxToBrightness(double lux, double maxLux, double gamma)
        {
            double clamped = Math.Min(Math.Max(lux, 0), maxLux);
            double normalized = clamped / maxLux;
            double corrected = Math.Pow(normalized, gamma);
            int brightnessPercent = (int)Math.Round(corrected * 100);
            return Math.Max(1, Math.Min(100, brightnessPercent));
        }

        /// <summary>
        /// Smoothly transitions brightness from current level to target over durationMs, updating every stepMs.
        /// </summary>
        static void SmoothSetBrightness(byte target, int durationMs, int stepMs)
        {
            try
            {
                byte current = GetCurrentBrightness();
                if (current == target) return;

                int steps = Math.Max(1, durationMs / stepMs);
                double delta = (target - current) / (double)steps;

                for (int i = 1; i <= steps; i++)
                {
                    byte interim = (byte)Math.Round(current + delta * i);
                    SetBrightness(interim);
                    Thread.Sleep(stepMs);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Smooth transition failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Reads the current screen brightness via WMI.
        /// </summary>
        static byte GetCurrentBrightness()
        {
            try
            {
                var scope = new ManagementScope("\\\\.\\root\\wmi");
                var query = new SelectQuery("WmiMonitorBrightness");
                using (var searcher = new ManagementObjectSearcher(scope, query))
                {
                    foreach (ManagementObject mo in searcher.Get())
                    {
                        return (byte)mo["CurrentBrightness"];
                    }
                }
            }
            catch { }
            return 0;
        }

        /// <summary>
        /// Immediately sets screen brightness via WMI.
        /// </summary>
        static void SetBrightness(byte brightness)
        {
            try
            {
                var scope = new ManagementScope("\\\\.\\root\\wmi");
                var query = new SelectQuery("WmiMonitorBrightnessMethods");
                using (var searcher = new ManagementObjectSearcher(scope, query))
                {
                    foreach (ManagementObject mo in searcher.Get())
                    {
                        mo.InvokeMethod("WmiSetBrightness", new object[] { UInt32.MaxValue, brightness });
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to set brightness: {ex.Message}");
            }
        }
    }
}
