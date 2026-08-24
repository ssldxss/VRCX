using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using NLog;

namespace VRCX
{
    /// <summary>
    /// Monitors the configured VR trigger (SteamVR or VRChat) and pauses/stops
    /// Wallpaper Engine while it is running, resuming it when the trigger exits.
    /// Only acts on an already-running Wallpaper Engine instance — it never
    /// launches one, except to resume after we paused/stopped it.
    /// </summary>
    public class WallpaperEngineManager
    {
        public static readonly WallpaperEngineManager Instance = new();

        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        private static readonly Regex VdfPathRegex =
            new("\"path\"\\s+\"((?:[^\"\\\\]|\\\\.)*)\"", RegexOptions.Compiled);

        /// <summary>Process names Wallpaper Engine may run under (32/64 bit, incl. web process).</summary>
        public static readonly string[] WallpaperProcessNames =
        {
            "wallpaper32",
            "wallpaper64",
            "webwallpaper32",
            "webwallpaper64"
        };

        public const string TriggerVrChat = "vrchat";
        public const string TriggerSteamVR = "steamvr";
        public const string ActionStop = "stop";
        public const string ActionPause = "pause";
        public const string ActionPlay = "play";

        private readonly object _gate = new();
        private readonly System.Timers.Timer _timer;

        private bool _enabled;
        private string _trigger = TriggerVrChat;
        private string _action = ActionStop;
        private bool _resumeOnExit = true;
        private bool _vrRunning;
        private bool _pausedByUs;

        // Injectable for unit tests.
        public Func<bool> VrRunningCheck { get; set; }
        public Func<bool> WallpaperRunningCheck { get; set; } = IsWallpaperEngineRunning;
        public Func<string, bool> ControlSender { get; set; } = SendControl;

        private WallpaperEngineManager()
        {
            // Default: route through VRCX's own "is VR running" detection,
            // which is implemented per build (Cef: ProcessMonitor, Electron:
            // direct process enumeration).
            VrRunningCheck = () =>
            {
                var api = Program.AppApiInstance;
                if (api == null)
                    return false;

                return _trigger == TriggerSteamVR ? api.IsSteamVRRunning() : api.IsGameRunning();
            };

            _timer = new System.Timers.Timer(3000) { AutoReset = true };
            _timer.Elapsed += (s, e) => Tick();
        }

        public bool Enabled
        {
            get { lock (_gate) return _enabled; }
        }

        public string Trigger
        {
            get { lock (_gate) return _trigger; }
        }

        public string Action
        {
            get { lock (_gate) return _action; }
        }

        public bool ResumeOnExit
        {
            get { lock (_gate) return _resumeOnExit; }
        }

        /// <summary>Starts the polling timer. Idempotent.</summary>
        public void Start()
        {
            _timer.Start();
        }

        /// <summary>
        /// Applies new settings from the UI. Invalid trigger/action values are
        /// ignored (previous value kept). Idempotently starts the poll timer.
        /// </summary>
        public void UpdateSettings(bool enabled, string trigger, string action, bool resumeOnExit)
        {
            lock (_gate)
            {
                _enabled = enabled;
                if (trigger == TriggerVrChat || trigger == TriggerSteamVR)
                    _trigger = trigger;
                if (action == ActionStop || action == ActionPause)
                    _action = action;
                _resumeOnExit = resumeOnExit;
            }

            Start();
        }

        /// <summary>
        /// Pure state-machine core: given the previous state and the current
        /// inputs, decides which control command (if any) to send and the new
        /// "paused by us" state.
        /// </summary>
        public (string Control, bool PausedByUs) Evaluate(
            bool enabled,
            bool vrRunning,
            bool wasVrRunning,
            bool wallpaperRunning,
            bool pausedByUs,
            bool resumeOnExit,
            string action)
        {
            if (!enabled)
            {
                // Re-enable safety: if we left the wallpaper paused/stopped,
                // bring it back before the feature goes away.
                return pausedByUs ? (ActionPlay, false) : (null, false);
            }

            if (vrRunning && !wasVrRunning)
            {
                if (wallpaperRunning)
                    return (action, true);
                return (null, false);
            }

            if (!vrRunning && wasVrRunning)
            {
                if (pausedByUs && resumeOnExit)
                    return (ActionPlay, false);
                return (null, false);
            }

            // Steady state (still in VR / never was): keep current state.
            return (null, pausedByUs);
        }

        /// <summary>One polling iteration: reads current state, runs <see cref="Evaluate"/>, applies the result.</summary>
        public void Tick()
        {
            Func<bool> vrCheck;
            Func<bool> wpCheck;
            lock (_gate)
            {
                vrCheck = VrRunningCheck;
                wpCheck = WallpaperRunningCheck;
            }

            bool vr = false;
            try
            {
                if (vrCheck != null)
                    vr = vrCheck();
            }
            catch (Exception ex)
            {
                logger.Warn("Wallpaper pause: VR check failed: {0}", ex.Message);
            }

            bool wallpaper = false;
            try
            {
                if (wpCheck != null)
                    wallpaper = wpCheck();
            }
            catch (Exception ex)
            {
                logger.Warn("Wallpaper pause: wallpaper check failed: {0}", ex.Message);
            }

            string control = null;
            lock (_gate)
            {
                var (controlOut, pausedByUs) = Evaluate(
                    _enabled, vr, _vrRunning, wallpaper, _pausedByUs, _resumeOnExit, _action);
                _vrRunning = vr;
                _pausedByUs = pausedByUs;
                control = controlOut;
            }

            if (control != null)
            {
                Func<string, bool> sender = ControlSender;
                bool ok = false;
                try
                {
                    if (sender != null)
                        ok = sender(control);
                }
                catch (Exception ex)
                {
                    logger.Error("Wallpaper pause: failed to send '{0}': {1}", control, ex.Message);
                }

                if (!ok)
                {
                    // Command did not land; don't believe we paused it.
                    lock (_gate)
                    {
                        _pausedByUs = false;
                    }
                }
            }
        }

        /// <summary>Returns true if any Wallpaper Engine process is currently running.</summary>
        public static bool IsWallpaperEngineRunning()
        {
            Process[] processes;
            try
            {
                processes = Process.GetProcesses();
            }
            catch (Exception ex)
            {
                logger.Warn("Wallpaper pause: process scan failed: {0}", ex.Message);
                return false;
            }

            try
            {
                foreach (var p in processes)
                {
                    using (p)
                    {
                        string name;
                        try
                        {
                            name = p.ProcessName;
                        }
                        catch
                        {
                            continue;
                        }

                        if (WallpaperProcessNames.Contains(name))
                            return true;
                    }
                }
            }
            finally
            {
                foreach (var p in processes)
                {
                    try
                    {
                        p.Dispose();
                    }
                    catch
                    {
                    }
                }
            }

            return false;
        }

        /// <summary>Sends a -control command (stop/pause/play) to Wallpaper Engine. Returns true on success.</summary>
        public static bool SendControl(string action)
        {
            if (action != ActionStop && action != ActionPause && action != ActionPlay)
                return false;

            string exe = FindRunningWallpaperExe() ?? FindWallpaperEngineExe();
            if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
            {
                logger.Error("Wallpaper pause: Wallpaper Engine executable not found, cannot send '{0}'", action);
                return false;
            }

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = "-control " + action,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi);
                logger.Info("Wallpaper pause: sent '{0}' to Wallpaper Engine ({1})", action, exe);
                return proc != null;
            }
            catch (Exception ex)
            {
                logger.Error("Wallpaper pause: failed to send '{0}' to Wallpaper Engine: {1}", action, ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Returns the exe path of a currently running Wallpaper Engine process,
        /// preferring wallpaper32.exe from its install directory.
        /// </summary>
        private static string FindRunningWallpaperExe()
        {
            Process[] processes;
            try
            {
                processes = Process.GetProcesses();
            }
            catch
            {
                return null;
            }

            try
            {
                foreach (var p in processes)
                {
                    using (p)
                    {
                        string name;
                        try
                        {
                            name = p.ProcessName;
                        }
                        catch
                        {
                            continue;
                        }

                        if (!WallpaperProcessNames.Contains(name))
                            continue;

                        try
                        {
                            string file = p.MainModule.FileName;
                            if (!File.Exists(file))
                                continue;

                            string dir = Path.GetDirectoryName(file);
                            string alt = Path.Combine(dir, "wallpaper32.exe");
                            if (File.Exists(alt))
                                return alt;

                            return file;
                        }
                        catch
                        {
                            continue;
                        }
                    }
                }
            }
            finally
            {
                foreach (var p in processes)
                {
                    try
                    {
                        p.Dispose();
                    }
                    catch
                    {
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Locates the Wallpaper Engine executable via the Steam installation
        /// (registry SteamPath + steamapps/libraryfolders.vdf libraries).
        /// Returns the exe path or null.
        /// </summary>
        public static string FindWallpaperEngineExe()
        {
            var candidateDirs = new List<string>();

            try
            {
                string steamPath = null;
                using (var lm = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Wow6432Node\Valve\Steam"))
                {
                    if (lm?.GetValue("SteamPath") is string p)
                        steamPath = p;
                }

                if (string.IsNullOrEmpty(steamPath))
                {
                    using var cu = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
                    if (cu?.GetValue("SteamPath") is string p2)
                        steamPath = p2;
                }

                if (!string.IsNullOrEmpty(steamPath))
                {
                    candidateDirs.Add(Path.Combine(steamPath, "steamapps", "common", "wallpaper_engine"));

                    string vdfPath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
                    if (File.Exists(vdfPath))
                    {
                        foreach (var lib in ParseLibraryFoldersVdf(File.ReadAllText(vdfPath)))
                            candidateDirs.Add(Path.Combine(lib, "common", "wallpaper_engine"));
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Warn("Wallpaper pause: Steam lookup failed: {0}", ex.Message);
            }

            foreach (string dir in candidateDirs.Distinct())
            {
                foreach (string exeName in new[] { "wallpaper32.exe", "wallpaper64.exe" })
                {
                    try
                    {
                        string candidate = Path.Combine(dir, exeName);
                        if (File.Exists(candidate))
                            return candidate;
                    }
                    catch
                    {
                    }
                }
            }

            return null;
        }

        /// <summary>Extracts library folder paths from libraryfolders.vdf content.</summary>
        public static List<string> ParseLibraryFoldersVdf(string content)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(content))
                return result;

            foreach (Match m in VdfPathRegex.Matches(content))
                result.Add(UnescapeVdf(m.Groups[1].Value));

            return result;
        }

        /// <summary>Single-pass VDF unescape: \X → X.</summary>
        private static string UnescapeVdf(string value)
        {
            var sb = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                if (value[i] == '\\' && i + 1 < value.Length)
                {
                    sb.Append(value[i + 1]);
                    i++;
                }
                else
                {
                    sb.Append(value[i]);
                }
            }

            return sb.ToString();
        }
    }
}
