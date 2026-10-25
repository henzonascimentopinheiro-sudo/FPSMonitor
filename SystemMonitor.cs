
using System;
using System.Diagnostics;

namespace FPSMonitor
{
    public class SystemMonitor : IDisposable
    {
        private readonly PerformanceCounter cpuCounter =
            new PerformanceCounter(
                "Processor",
                "% Processor Time",
                "_Total");

        public float GetCpuUsage()
        {
            return Math.Clamp(
                cpuCounter.NextValue(),
                0f,
                100f);
        }

        public float GetRamUsage()
        {
            using var process = Process.GetCurrentProcess();

            return process.WorkingSet64 /
                   1024f / 1024f;
        }

        public void Dispose()
        {
            cpuCounter.Dispose();
        }
    }
}
