using System;
using System.Collections.Generic;

namespace DSPQualitySystem
{
    /// <summary>
    /// Tracks which planet's factory is currently ticking (M1). Struct components such as
    /// AssemblerComponent carry no factory reference, so patches need this ambient scope.
    /// FactorySystem.GameTick runs serially per planet, so a single static field is safe for the
    /// serial path. Parallel facility ticks (GameLogic._assembler_parallel etc., P3) run on worker
    /// threads without Enter/Exit, so they fall back to the last planet id seen by the serial tick
    /// (<see cref="_lastPlanetId"/>), then to GameMain.localPlanet. The fallback is observable via
    /// <see cref="AmbientPlanetId"/> and the "[QSystem][speed] ... fallback=" counters.
    /// </summary>
    public static class QualityEnv
    {
        private static int _planetId = -1;
        private static int _lastPlanetId = -1;
        private static readonly HashSet<string> _warned = new HashSet<string>();

        /// <summary>The planet id of the serial GameTick currently in scope, or -1 between ticks/on worker threads.</summary>
        public static int AmbientPlanetId
        {
            get { return _planetId; }
        }

        public static int CurrentPlanetId
        {
            get
            {
                if (_planetId > 0)
                {
                    return _planetId;
                }

                // Parallel facility ticks have no Enter/Exit scope; reuse the last serially-ticked
                // planet. In single player the serial tick runs for the current planet every frame,
                // so this is the same planet the worker threads are processing.
                if (_lastPlanetId > 0)
                {
                    return _lastPlanetId;
                }

                try
                {
                    PlanetData local = GameMain.localPlanet;
                    if (local != null)
                    {
                        _lastPlanetId = local.id;
                        return local.id;
                    }
                }
                catch (Exception)
                {
                    // GameMain not ready yet; treat as "no planet".
                }

                return -1;
            }
        }

        public static void Enter(FactorySystem system)
        {
            if (system == null || system.planet == null)
            {
                _planetId = -1;
                return;
            }
            _planetId = system.planet.id;
            if (_planetId > 0)
            {
                _lastPlanetId = _planetId;
            }
        }

        public static void Exit()
        {
            _planetId = -1;
        }

        /// <summary>Hot-path safe warning: only the first occurrence of each key is logged.</summary>
        public static void WarnOnce(string key, Exception e)
        {
            try
            {
                if (!_warned.Add(key))
                {
                    return;
                }
                var log = Plugin.Log;
                if (log != null)
                {
                    log.LogWarning("[QSystem][patch] " + key + " failed: " + e);
                }
            }
            catch (Exception)
            {
                // never let diagnostics break the game
            }
        }

        /// <summary>Same once-per-key warning without an exception (v0.12.1 diagnostics).</summary>
        public static void WarnOnce(string key, string message)
        {
            try
            {
                if (!_warned.Add(key))
                {
                    return;
                }
                var log = Plugin.Log;
                if (log != null)
                {
                    log.LogWarning("[QSystem] " + message);
                }
            }
            catch (Exception)
            {
                // never let diagnostics break the game
            }
        }

        /// <summary>v0.13.0: plain info line, safe to call before/after the plugin log exists.</summary>
        public static void LogInfo(string message)
        {
            try
            {
                var log = Plugin.Log;
                if (log != null)
                {
                    log.LogInfo(message);
                }
            }
            catch (Exception)
            {
                // never let diagnostics break the game
            }
        }
    }
}
