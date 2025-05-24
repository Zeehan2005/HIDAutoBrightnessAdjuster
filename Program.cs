using System;
using System.Collections.Generic;
using System.Management;
using System.Threading;
using Windows.Devices.Sensors;

namespace HIDAutoBrightnessAdjuster
{
    class Program
    {
        // Ambient light sensor instance
        static LightSensor _sensor;

        // Parameters for smooth brightness transition
        const int transitionDurationMs = 5000; // Total duration for brightness transition (ms)
        const int transitionStepMs = 100;      // Time between each brightness step (ms)

        // For periodic garbage collection to avoid memory leaks in long-running process
        private static DateTime _lastGcTime = DateTime.Now;

        static void Main(string[] args)
        {
            // Print usage instructions for custom minLux and maxLux
            Console.WriteLine("Starting AutoBrightnessAdjuster...");
            Console.WriteLine("Usage: HIDAutoBrightnessAdjuster.exe [minLux] [maxLux]");
            Console.WriteLine("  minLux: Minimum lux threshold for adjustment (default: 5.0)");
            Console.WriteLine("  maxLux: Maximum lux value for mapping (default: 500.0)");
            _sensor = LightSensor.GetDefault();
            if (_sensor == null)
            {
                Console.WriteLine("No ambient light sensor found. Exiting.");
                return;
            }
            Console.WriteLine("Ambient light sensor detected.");

            // Support custom min/max lux via command line arguments
            double minLux = 5.0;      // Minimum lux threshold for adjustment
            double maxLux = 500.0;   // Maximum lux value for mapping
            if (args.Length >= 1 && double.TryParse(args[0], out double userMinLux))
                minLux = userMinLux;
            if (args.Length >= 2 && double.TryParse(args[1], out double userMaxLux))
                maxLux = userMaxLux;
            Console.WriteLine($"Using minLux={minLux}, maxLux={maxLux}");

            byte lastBrightness = GetCurrentBrightness(); // Track last set brightness
            double lastLux = -1;                          // Track last measured lux

            while (true)
            {
                var reading = _sensor.GetCurrentReading();
                if (reading != null)
                {
                    double lux = reading.IlluminanceInLux;

                    // If detected brightness is less than 5 lux, do not adjust brightness, wait 5 seconds before checking again
                    if (lux < 5.0)
                    {
                        Console.WriteLine($"Ambient light: {lux:F1} lux is too low (<5), skipping adjustment. Waiting 5 seconds...");
                        Thread.Sleep(5000);
                        continue;
                    }

                    // Map the current lux value to a target brightness percentage (1-100)
                    int targetBrightness = MapLuxToBrightness(lux, maxLux, 0.6, minLux);
                    byte currentBrightness = GetCurrentBrightness();

                    // Detect manual brightness adjustment by user (if system brightness changed externally)
                    if (Math.Abs(currentBrightness - lastBrightness) > 2)
                    {
                        Console.WriteLine("Manual brightness adjustment detected. Skipping this cycle.");
                        lastBrightness = currentBrightness;
                        lastLux = lux; // Sync lux value to avoid repeated triggers
                        Thread.Sleep(2000);
                        continue;
                    }

                    // If lux change is very small, do not change brightness to avoid unnecessary adjustments
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
                    // Smoothly transition to the new brightness value
                    SmoothSetBrightness((byte)targetBrightness, transitionDurationMs, transitionStepMs, ref lastBrightness);
                    lastBrightness = GetCurrentBrightness();
                    lastLux = lux; // Update last measured lux at the end of each detection round
                }

                // Periodic garbage collection every minute to keep memory usage low
                if (DateTime.Now - _lastGcTime > TimeSpan.FromMinutes(1))
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    _lastGcTime = DateTime.Now;
                }

                Thread.Sleep(2000); // Main loop delay (2 seconds)
            }
        }

        /// <summary>
        /// Maps a given lux value to a brightness percentage (1-100) using gamma correction.
        /// </summary>
        /// <param name="lux">Current ambient light in lux</param>
        /// <param name="maxLux">Maximum lux for normalization</param>
        /// <param name="gamma">Gamma correction factor</param>
        /// <param name="minLux">Minimum lux for normalization</param>
        /// <returns>Brightness percentage (1-100)</returns>
        static int MapLuxToBrightness(double lux, double maxLux, double gamma, double minLux = 5.0)
        {
            // If below or equal to minLux, return minimum brightness
            if (lux <= minLux)
                return 1;
            // If above or equal to maxLux, return maximum brightness
            if (lux >= maxLux)
                return 100;
            // Normalize lux between minLux and maxLux
            double normalized = (lux - minLux) / (maxLux - minLux);
            double corrected = Math.Pow(normalized, gamma); // Apply gamma correction
            int perc = (int)Math.Round(corrected * 100);
            return Math.Max(1, Math.Min(100, perc)); // Ensure within 1-100
        }

        /// <summary>
        /// Gets the current monitor brightness using WMI.
        /// </summary>
        /// <returns>Current brightness (0-100)</returns>
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

        /// <summary>
        /// Gets the WMI object for setting monitor brightness.
        /// </summary>
        /// <returns>ManagementObject for brightness control, or null if not found</returns>
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

        /// <summary>
        /// Smoothly transitions the monitor brightness to the target value over a specified duration.
        /// Aborts if manual adjustment is detected during the transition.
        /// </summary>
        /// <param name="target">Target brightness (0-100)</param>
        /// <param name="durationMs">Total transition duration in milliseconds</param>
        /// <param name="stepMs">Delay between each step in milliseconds</param>
        /// <param name="lastBrightness">Reference to last brightness value for manual adjustment detection</param>
        static void SmoothSetBrightness(byte target, int durationMs, int stepMs, ref byte lastBrightness)
        {
            byte current = GetCurrentBrightness();
            if (current == target) return;

            int steps = Math.Max(1, durationMs / stepMs); // Calculate number of steps
            double delta = (target - current) / (double)steps; // Brightness increment per step

            var brightnessObject = GetBrightnessObject();
            if (brightnessObject == null)
            {
                Console.WriteLine("Unable to get WMI brightness object.");
                return;
            }

            for (int i = 1; i <= steps; i++)
            {
                byte next = (byte)Math.Round(current + delta * i);

                // Check for manual adjustment during transition
                byte now = GetCurrentBrightness();
                if (Math.Abs(now - lastBrightness) > 2)
                {
                    Console.WriteLine("Manual brightness adjustment detected during transition. Aborting smooth adjustment.");
                    lastBrightness = now;
                    break;
                }

                try
                {
                    // Set brightness using WMI method
                    brightnessObject.InvokeMethod("WmiSetBrightness", new object[] { 1, next });
                    lastBrightness = next;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to set brightness: {ex.Message}");
                    break;
                }
                Thread.Sleep(stepMs); // Wait before next step
            }

            brightnessObject.Dispose();
        }
    }
}
