using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace LinkFlow.Services;

public static class ProcessMonitor
{
    private static HashSet<string>? _cachedRunningProcesses;
    private static DateTime _lastRefresh = DateTime.MinValue;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(2);

    public static HashSet<string> GetRunningProcesses(bool forceRefresh = false)
    {
        if (!forceRefresh && _cachedRunningProcesses != null && DateTime.UtcNow - _lastRefresh < CacheDuration)
        {
            return _cachedRunningProcesses;
        }

        try
        {
            var processes = Process.GetProcesses();
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in processes)
            {
                try
                {
                    set.Add(p.ProcessName);
                }
                catch
                {
                    // Ignore processes that exited
                }
                finally
                {
                    p.Dispose();
                }
            }

            _cachedRunningProcesses = set;
            _lastRefresh = DateTime.UtcNow;
            return set;
        }
        catch
        {
            return _cachedRunningProcesses ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public static bool IsRunning(string? exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath))
            return false;

        try
        {
            var cleanPath = exePath.Trim('"', '\'').Trim();
            var nameWithoutExt = Path.GetFileNameWithoutExtension(cleanPath);
            if (string.IsNullOrEmpty(nameWithoutExt))
                return false;

            var running = GetRunningProcesses();
            return running.Contains(nameWithoutExt);
        }
        catch
        {
            return false;
        }
    }
}
