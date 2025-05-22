using System;
using System.Collections.Generic;
using System.Management;
using System.Threading;
using Windows.Devices.Sensors;

namespace HIDAutoBrightnessAdjuster
{
    class Program
    {
        static LightSensor _sensor;

        // Smooth transition parameters
        const int transitionDurationMs = 5000;
        const int transitionStepMs = 100;

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

            byte lastBrightness = GetCurrentBrightness();
            double lastLux = -1; // 新增：记录上次lux

            while (true)
            {
                var reading = _sensor.GetCurrentReading();
                if (reading != null)
                {
                    double lux = reading.IlluminanceInLux;

                    // 1. 若检测到亮度小于5lux，不调整亮度，等待5秒后再检测
                    if (lux < 5.0)
                    {
                        Console.WriteLine($"Ambient light: {lux:F1} lux is too low (<5), skipping adjustment. Waiting 5 seconds...");
                        Thread.Sleep(5000);
                        continue;
                    }

                    int targetBrightness = MapLuxToBrightness(lux, 1000.0, 0.6);
                    byte currentBrightness = GetCurrentBrightness();

                    // 2. 检测人工调整亮度
                    if (Math.Abs(currentBrightness - lastBrightness) > 2)
                    {
                        Console.WriteLine("Manual brightness adjustment detected. Skipping this cycle.");
                        lastBrightness = currentBrightness;
                        lastLux = lux; // 新增：同步lux
                        Thread.Sleep(2000);
                        continue;
                    }

                    // 新增：如果lux变化极小，不改变亮度
                    if (lastLux >= 0 && Math.Abs(lux - lastLux) < 2.0)
                    {
                        Console.WriteLine($"Ambient light: {lux:F1} lux");
                        Console.WriteLine($"Ambient light change too small (<2 lux) (last: {lastLux:F1}, current: {lux:F1}), skipping adjustment.");
                        Thread.Sleep(2000);
                        lastBrightness = currentBrightness;
                        lastLux = lux;
                        continue;
                    }

                    Console.WriteLine($"Ambient light: {lux:F1} lux -> Setting brightness to {targetBrightness}%");
                    SmoothSetBrightness((byte)targetBrightness, transitionDurationMs, transitionStepMs, ref lastBrightness);
                    lastBrightness = GetCurrentBrightness();
                    lastLux = lux; // 新增：每轮检测结束时写入
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

        // 修改SmoothSetBrightness，支持检测人工调整
        static void SmoothSetBrightness(byte target, int durationMs, int stepMs, ref byte lastBrightness)
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

                // 检查人工调整
                byte now = GetCurrentBrightness();
                if (Math.Abs(now - lastBrightness) > 2)
                {
                    Console.WriteLine("Manual brightness adjustment detected during transition. Aborting smooth adjustment.");
                    lastBrightness = now;
                    break;
                }

                try
                {
                    brightnessObject.InvokeMethod("WmiSetBrightness", new object[] { 1, next });
                    lastBrightness = next;
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
