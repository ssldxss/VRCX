using System;
using System.Collections.Generic;
using Xunit;

namespace VRCX.Tests
{
    /// <summary>
    /// Tests the WallpaperEngineManager state machine core (Evaluate), settings
    /// validation (UpdateSettings), and the pure libraryfolders.vdf parser.
    /// Environment-dependent pieces (live process detection, sending -control)
    /// are not tested here.
    /// </summary>
    public class WallpaperEngineManagerTests
    {
        private static WallpaperEngineManager Manager => WallpaperEngineManager.Instance;

        // ---------- Evaluate: feature disabled ----------

        [Fact]
        public void Evaluate_Disabled_DoesNothing()
        {
            var (control, pausedByUs) = Manager.Evaluate(
                enabled: false,
                vrRunning: true,
                wasVrRunning: false,
                wallpaperRunning: true,
                pausedByUs: false,
                resumeOnExit: true,
                action: WallpaperEngineManager.ActionStop);

            Assert.Null(control);
            Assert.False(pausedByUs);
        }

        [Fact]
        public void Evaluate_Disabled_WhileWePaused_ResumeWallpaper()
        {
            var (control, pausedByUs) = Manager.Evaluate(
                enabled: false,
                vrRunning: true,
                wasVrRunning: true,
                wallpaperRunning: true,
                pausedByUs: true,
                resumeOnExit: true,
                action: WallpaperEngineManager.ActionStop);

            Assert.Equal(WallpaperEngineManager.ActionPlay, control);
            Assert.False(pausedByUs);
        }

        // ---------- Evaluate: trigger starts ----------

        [Fact]
        public void Evaluate_TriggerStarts_WallpaperRunning_SendsStop()
        {
            var (control, pausedByUs) = Manager.Evaluate(
                enabled: true,
                vrRunning: true,
                wasVrRunning: false,
                wallpaperRunning: true,
                pausedByUs: false,
                resumeOnExit: true,
                action: WallpaperEngineManager.ActionStop);

            Assert.Equal(WallpaperEngineManager.ActionStop, control);
            Assert.True(pausedByUs);
        }

        [Fact]
        public void Evaluate_TriggerStarts_PauseAction_SendsPause()
        {
            var (control, pausedByUs) = Manager.Evaluate(
                enabled: true,
                vrRunning: true,
                wasVrRunning: false,
                wallpaperRunning: true,
                pausedByUs: false,
                resumeOnExit: true,
                action: WallpaperEngineManager.ActionPause);

            Assert.Equal(WallpaperEngineManager.ActionPause, control);
            Assert.True(pausedByUs);
        }

        [Fact]
        public void Evaluate_TriggerStarts_WallpaperNotRunning_SendsNothing()
        {
            var (control, pausedByUs) = Manager.Evaluate(
                enabled: true,
                vrRunning: true,
                wasVrRunning: false,
                wallpaperRunning: false,
                pausedByUs: false,
                resumeOnExit: true,
                action: WallpaperEngineManager.ActionStop);

            Assert.Null(control);
            Assert.False(pausedByUs);
        }

        [Fact]
        public void Evaluate_TriggerAlreadyRunning_SendsNothing()
        {
            var (control, pausedByUs) = Manager.Evaluate(
                enabled: true,
                vrRunning: true,
                wasVrRunning: true,
                wallpaperRunning: true,
                pausedByUs: false,
                resumeOnExit: true,
                action: WallpaperEngineManager.ActionStop);

            Assert.Null(control);
            Assert.False(pausedByUs);
        }

        // ---------- Evaluate: trigger ends ----------

        [Fact]
        public void Evaluate_TriggerEnds_WePaused_ResumeEnabled_SendsPlay()
        {
            var (control, pausedByUs) = Manager.Evaluate(
                enabled: true,
                vrRunning: false,
                wasVrRunning: true,
                wallpaperRunning: false,
                pausedByUs: true,
                resumeOnExit: true,
                action: WallpaperEngineManager.ActionStop);

            Assert.Equal(WallpaperEngineManager.ActionPlay, control);
            Assert.False(pausedByUs);
        }

        [Fact]
        public void Evaluate_TriggerEnds_WePaused_ResumeDisabled_SendsNothing()
        {
            var (control, pausedByUs) = Manager.Evaluate(
                enabled: true,
                vrRunning: false,
                wasVrRunning: true,
                wallpaperRunning: false,
                pausedByUs: true,
                resumeOnExit: false,
                action: WallpaperEngineManager.ActionStop);

            Assert.Null(control);
            Assert.False(pausedByUs);
        }

        [Fact]
        public void Evaluate_TriggerEnds_NotPausedByUs_SendsNothing()
        {
            var (control, pausedByUs) = Manager.Evaluate(
                enabled: true,
                vrRunning: false,
                wasVrRunning: true,
                wallpaperRunning: false,
                pausedByUs: false,
                resumeOnExit: true,
                action: WallpaperEngineManager.ActionStop);

            Assert.Null(control);
            Assert.False(pausedByUs);
        }

        // ---------- UpdateSettings ----------

        [Fact]
        public void UpdateSettings_StoresValidValues()
        {
            Manager.UpdateSettings(
                true,
                WallpaperEngineManager.TriggerSteamVR,
                WallpaperEngineManager.ActionPause,
                false);

            Assert.True(Manager.Enabled);
            Assert.Equal(WallpaperEngineManager.TriggerSteamVR, Manager.Trigger);
            Assert.Equal(WallpaperEngineManager.ActionPause, Manager.Action);
            Assert.False(Manager.ResumeOnExit);
        }

        [Fact]
        public void UpdateSettings_InvalidTrigger_KeepsPrevious()
        {
            Manager.UpdateSettings(true, WallpaperEngineManager.TriggerVrChat, WallpaperEngineManager.ActionStop, true);
            Manager.UpdateSettings(true, "garbage", WallpaperEngineManager.ActionStop, true);

            Assert.Equal(WallpaperEngineManager.TriggerVrChat, Manager.Trigger);
        }

        [Fact]
        public void UpdateSettings_InvalidAction_KeepsPrevious()
        {
            Manager.UpdateSettings(true, WallpaperEngineManager.TriggerVrChat, WallpaperEngineManager.ActionStop, true);
            Manager.UpdateSettings(true, WallpaperEngineManager.TriggerVrChat, "garbage", true);

            Assert.Equal(WallpaperEngineManager.ActionStop, Manager.Action);
        }

        [Fact]
        public void Start_IsIdempotent()
        {
            Manager.Start();
            Manager.Start();
        }

        // ---------- ParseLibraryFoldersVdf ----------

        [Fact]
        public void ParseLibraryFoldersVdf_SinglePath()
        {
            var vdf =
                "libraryfolders\n" +
                "{\n" +
                "\t\"0\"\n" +
                "\t{\n" +
                "\t\t\"path\"\t\"C:\\\\Program Files (x86)\\\\Steam\"\n" +
                "\t\t\"label\"\t\"\"\n" +
                "\t\t\"contentid\"\t\"0\"\n" +
                "\t\t\"apps\"\n" +
                "\t\t{\n" +
                "\t\t}\n" +
                "\t}\n" +
                "}\n";

            var paths = WallpaperEngineManager.ParseLibraryFoldersVdf(vdf);

            Assert.Single(paths);
            Assert.Equal(@"C:\Program Files (x86)\Steam", paths[0]);
        }

        [Fact]
        public void ParseLibraryFoldersVdf_MultiplePaths()
        {
            var vdf =
                "libraryfolders\n" +
                "{\n" +
                "\t\"0\"\n" +
                "\t{\n" +
                "\t\t\"path\"\t\"C:\\\\Steam\"\n" +
                "\t}\n" +
                "\t\"1\"\n" +
                "\t{\n" +
                "\t\t\"path\"\t\"D:\\\\Games\\\\SteamLibrary\"\n" +
                "\t}\n" +
                "}\n";

            var paths = WallpaperEngineManager.ParseLibraryFoldersVdf(vdf);

            Assert.Equal(new List<string> { @"C:\Steam", @"D:\Games\SteamLibrary" }, paths);
        }

        [Fact]
        public void ParseLibraryFoldersVdf_EmptyContent_ReturnsEmpty()
        {
            Assert.Empty(WallpaperEngineManager.ParseLibraryFoldersVdf(""));
            Assert.Empty(WallpaperEngineManager.ParseLibraryFoldersVdf(null));
        }
    }
}
