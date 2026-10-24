using System;
using System.IO;
using HarmonyLib;

namespace CombatVFX
{
    public static class ModEntry
    {
        public static string ModDirectory { get; private set; }
        private static StreamWriter _log;

        public const string Version = "0.3.0";

        public static void Init(string directory, string settingsJson)
        {
            ModDirectory = directory;

            try
            {
                string logPath = Path.Combine(directory, "CombatVFX_log.txt");
                _log = new StreamWriter(logPath, append: false) { AutoFlush = true };
            }
            catch { /* log unavailable — swallow */ }

            Log("═══════════════════════════════════════════════════════");
            Log($"  CombatVFX v{Version}");
            Log("  Ballistic deflection and recipe weapon effects (Effects Studio)");
            Log($"  Directory: {directory}");
            Log("═══════════════════════════════════════════════════════");

            Settings.Load(settingsJson);
            Fx.FxSettings.Load(settingsJson);

            var harmony = new Harmony("com.combatvfx");
            harmony.PatchAll();

            Log("[CombatVFX] Harmony patches applied");
        }

        public static void Log(string message)
        {
            try
            {
                string line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
                _log?.WriteLine(line);
            }
            catch { /* never let logging crash the game */ }
        }
    }
}
