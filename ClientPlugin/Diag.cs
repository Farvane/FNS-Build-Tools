using System;
using Keen.VRage.Library.Diagnostics;

namespace Farvane.BuildTools;

internal static class Diag
{
    internal static void Info(string message) => Log.Default.WriteLine("[FNS Build Tools] " + message);
    internal static void Failed(string action, Exception error) =>
        Info(action + " failed (" + error.GetType().Name + "): " + error.Message);
}

