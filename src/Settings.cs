using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace ThreeFingerWindowDrag
{
    internal enum AppLanguage { System, English, Korean }

    internal sealed class Settings
    {
        public int Speed = 12;
        public bool RestoreMaximized = true;
        public bool FollowCursor = true;
        public bool MaximizeAtTop = true;
        public bool BringToFront = true;
        public bool AllowTopOverflow = true;
        public bool PinchResize = true;
        public bool SnapAtSides = true;
        public bool FlickGestures = true;
        public int FlickSensitivity = 5;
        public bool SetupDone;
        public bool OnboardingShown;
        public bool DirectionalResize = true;
        public double ResizeHoldSeconds = 1;
        public ResizeBehavior ResizeBehavior = ResizeBehavior.Grow;
        public AppLanguage Language = AppLanguage.System;
        internal bool NeedsWelcome { get { return !OnboardingShown && !SetupDone; } }
        internal DirectionalResizeOptions ResizeOptions { get { return new DirectionalResizeOptions {
            Enabled = DirectionalResize, HoldMilliseconds = (int)Math.Round(ResizeTiming.NormalizeSeconds(ResizeHoldSeconds) * 1000),
            Behavior = ResizeBehavior }.Snapshot(); } }
        internal static string FilePath { get { return ResolvePath(AppDomain.CurrentDomain.BaseDirectory,
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "installed-mode"))); } }
        internal static string ResolvePath(string appDirectory, string localDataDirectory, bool installed)
        { return installed ? Path.Combine(localDataDirectory, "Glide", "settings.ini") : Path.Combine(appDirectory, "settings.ini"); }
        internal static Settings Load()
        {
            Settings result = new Settings();
            try
            {
                if (!File.Exists(FilePath)) return result;
                bool hasWelcomeFlag = false;
                foreach (string line in File.ReadAllLines(FilePath))
                {
                    string[] pair = line.Split(new[] { '=' }, 2);
                    if (pair.Length != 2) continue;
                    int speed;
                    if (pair[0] == "Speed" && int.TryParse(pair[1], out speed)) result.Speed = Math.Max(3, Math.Min(36, speed));
                    if (pair[0] == "RestoreMaximized") result.RestoreMaximized = pair[1] == "true";
                    if (pair[0] == "FollowCursor") result.FollowCursor = pair[1] == "true";
                    if (pair[0] == "MaximizeAtTop") result.MaximizeAtTop = pair[1] == "true";
                    if (pair[0] == "BringToFront") result.BringToFront = pair[1] == "true";
                    if (pair[0] == "AllowTopOverflow") result.AllowTopOverflow = pair[1] == "true";
                    if (pair[0] == "PinchResize") result.PinchResize = pair[1] == "true";
                    if (pair[0] == "SnapAtSides") result.SnapAtSides = pair[1] == "true";
                    if (pair[0] == "FlickGestures") result.FlickGestures = pair[1] == "true";
                    if (pair[0] == "FlickSensitivity" && int.TryParse(pair[1], out int sensitivity)) result.FlickSensitivity = Math.Clamp(sensitivity, 1, 10);
                    if (pair[0] == "SetupDone") result.SetupDone = pair[1] == "true";
                    if (pair[0] == "OnboardingShown") { result.OnboardingShown = pair[1] == "true"; hasWelcomeFlag = true; }
                    if (pair[0] == "DirectionalResize") result.DirectionalResize = pair[1] == "true";
                    if (pair[0] == "ResizeBehavior" && Enum.TryParse(pair[1], out ResizeBehavior behavior) && Enum.IsDefined(behavior)) result.ResizeBehavior = behavior;
                    if (pair[0] == "ResizeBehavior" && pair[1] == "Shrink") result.ResizeBehavior = ResizeBehavior.LowerCorners;
                    if (pair[0] == "Language" && Enum.TryParse(pair[1], out AppLanguage language) && Enum.IsDefined(language))
                        result.Language = language;
                    if (pair[0] == "ResizeHoldSeconds" && double.TryParse(pair[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds) && double.IsFinite(seconds))
                        result.ResizeHoldSeconds = ResizeTiming.NormalizeSeconds(seconds);
                }
                if (!hasWelcomeFlag) result.OnboardingShown = true; // An older settings file means this is an upgrade.
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            if (result.SetupDone) result.OnboardingShown = true; // Existing installations already completed the old guide.
            return result;
        }
        internal void MarkWelcomeShown() { OnboardingShown = true; SetupDone = true; }
        internal void Save()
        {
            string body = "Speed=" + Speed.ToString(CultureInfo.InvariantCulture) + "\r\nRestoreMaximized=" + RestoreMaximized.ToString().ToLowerInvariant() +
                "\r\nFollowCursor=" + FollowCursor.ToString().ToLowerInvariant() + "\r\nMaximizeAtTop=" + MaximizeAtTop.ToString().ToLowerInvariant() +
                "\r\nBringToFront=" + BringToFront.ToString().ToLowerInvariant() + "\r\nAllowTopOverflow=" + AllowTopOverflow.ToString().ToLowerInvariant() +
                "\r\nPinchResize=" + PinchResize.ToString().ToLowerInvariant() + "\r\nSnapAtSides=" + SnapAtSides.ToString().ToLowerInvariant() +
                "\r\nFlickGestures=" + FlickGestures.ToString().ToLowerInvariant() + "\r\nFlickSensitivity=" + FlickSensitivity.ToString(CultureInfo.InvariantCulture) +
                "\r\nSetupDone=" + SetupDone.ToString().ToLowerInvariant() + "\r\nOnboardingShown=" + OnboardingShown.ToString().ToLowerInvariant() +
                "\r\nDirectionalResize=" + DirectionalResize.ToString().ToLowerInvariant() +
                "\r\nResizeHoldSeconds=" + ResizeTiming.NormalizeSeconds(ResizeHoldSeconds).ToString("0.0", CultureInfo.InvariantCulture) +
                "\r\nResizeBehavior=" + ResizeBehavior + "\r\nLanguage=" + Language + "\r\n";
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            File.WriteAllText(FilePath, body, new UTF8Encoding(true));
        }
    }
}
