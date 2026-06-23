using System.Diagnostics;
using UnityEngine;

namespace Assets.Scripts.Tools
{
    /// <summary>
    /// Verbose logging that compiles out entirely unless GW_VERBOSE_LOGS is defined
    /// (Project Settings > Player > Scripting Define Symbols).
    ///
    /// Use this for high-frequency / per-frame traces (physics stay callbacks, detection
    /// loops, etc.) so they never flood the Console in normal play. A flooded Console makes
    /// the Editor re-run TextGenerator over thousands of entries every repaint, which can
    /// stall the main thread for seconds — keep hot-path logs behind this.
    ///
    /// Calls to these methods (and evaluation of their arguments) are removed by the
    /// compiler when the symbol is absent, so there is zero runtime cost.
    /// </summary>
    public static class GwLog
    {
        [Conditional("GW_VERBOSE_LOGS")]
        public static void Verbose(string message, Object context = null)
        {
            UnityEngine.Debug.Log(message, context);
        }

        [Conditional("GW_VERBOSE_LOGS")]
        public static void VerboseWarning(string message, Object context = null)
        {
            UnityEngine.Debug.LogWarning(message, context);
        }
    }
}
