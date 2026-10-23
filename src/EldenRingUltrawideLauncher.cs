using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

internal static class EldenRingUltrawideLauncher
{
    private const string GameDirectory = @"C:\Program Files (x86)\Steam\steamapps\common\ELDEN RING\Game";
    private const string OriginalPath = GameDirectory + @"\eldenring.exe";
    private const string TemporaryPath = GameDirectory + @"\eldenring_ultrawide_tmp.exe";
    private const string StagingPath = GameDirectory + @"\.eldenring_ultrawide_tmp.exe.new";
    private const string LogPath = @"C:\EldenRingTools\elden-ring-ultrawide.log";

    // Patches are located by byte pattern ("??" = any byte) each launch, so game updates that only
    // move code around keep working. Every pattern must match exactly once or it is skipped.
    // Black bars: the JZ that applies 16:9 scaling becomes JMP (gurrgur/er-patcher --ultrawide).
    private const string BarsPattern = "74 4F 45 8B 94 CC";

    // The game only accepts resolutions from a built-in 16:9 table (<int>width <int>height pairs).
    // Overwrite the 1920x1080 entry (followed by 2048x1152) with the custom resolution, as
    // uberhalit/EldenRingFpsUnlockAndMore does. The resolution is the screen size unless
    // C:\EldenRingTools\resolution.txt holds WIDTHxHEIGHT ("auto" or a missing file means screen size).
    private const string ResolutionPath = @"C:\EldenRingTools\resolution.txt";

    // Patches only make sense on screens wider than 16:9; on a 16:9 or 16:10 screen (e.g. a
    // laptop on its own) the clean game is run unmodified.
    private const double WideAspect = 16.0 / 9.0 * 1.02;
    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);
    private static readonly byte[] ResolutionTableSignature =
        { 0x80, 0x07, 0x00, 0x00, 0x38, 0x04, 0x00, 0x00, 0x00, 0x08, 0x00, 0x00, 0x80, 0x04, 0x00, 0x00 };

    // Vignette is sized for 16:9 and darkens the outer edges of wider screens. Replace
    // MULSS XMM8,XMM10 with SUBSS XMM0,XMM0; NOP (gurrgur/er-patcher --disable-vignette).
    private const string VignettePattern =
        "F3 0F 10 ?? ?? F3 0F 59 ?? ?? ?? ?? ?? E8 ?? ?? ?? ?? F3 41 0F ?? ?? F3 45 0F ?? ?? 4C 8D ?? ?? ?? ?? ?? ?? 48";
    private const int VignetteOffset = 23;
    private static readonly byte[] VignetteOriginal = { 0xF3, 0x45, 0x0F, 0x59, 0xC2 };
    private static readonly byte[] VignettePatch = { 0xF3, 0x0F, 0x5C, 0xC0, 0x90 };

    // Entities outside the 16:9 view get low-rate animation updates, so NPCs at the sides of
    // a wider screen move in slow motion. Replace DIVSS XMM1,[R12+0x54] with XORPS XMM1,XMM1;
    // PXOR XMM1,XMM1 (gurrgur/er-patcher --increase-animation-distance).
    private const string AnimationPattern =
        "E8 ?? ?? ?? ?? 0F 28 ?? 0F 28 ?? E8 ?? ?? ?? ?? F3 0F ?? ?? 0F 28 ?? F3 41 0F 5E 4C 24 54";
    private const int AnimationOffset = 23;
    private static readonly byte[] AnimationOriginal = { 0xF3, 0x41, 0x0F, 0x5E, 0x4C, 0x24, 0x54 };
    private static readonly byte[] AnimationPatch = { 0x0F, 0x57, 0xC9, 0x66, 0x0F, 0xEF, 0xC9 };

    // Returns the single match of a "??"-wildcard hex pattern, or -1 if it is missing or ambiguous.
    private static int FindPattern(byte[] data, string pattern)
    {
        string[] tokens = pattern.Split(' ');
        var bytes = new byte[tokens.Length];
        var wild = new bool[tokens.Length];
        for (int k = 0; k < tokens.Length; k++)
        {
            wild[k] = tokens[k] == "??";
            if (!wild[k])
                bytes[k] = Convert.ToByte(tokens[k], 16);
        }
        int found = -1;
        for (int i = 0; i <= data.Length - bytes.Length; i++)
        {
            if (data[i] != bytes[0])
                continue;
            int j = 1;
            while (j < bytes.Length && (wild[j] || data[i + j] == bytes[j]))
                j++;
            if (j < bytes.Length)
                continue;
            if (found >= 0)
                return -1;
            found = i;
        }
        return found;
    }

    private static bool PatchPattern(byte[] image, string pattern, int offset, byte[] original, byte[] patch, string name)
    {
        int at = FindPattern(image, pattern);
        if (at < 0)
        {
            Log(name + " pattern not found exactly once; left unchanged");
            return false;
        }
        at += offset;
        for (int i = 0; i < original.Length; i++)
        {
            if (image[at + i] != original[i])
            {
                Log(name + " signature mismatch; left unchanged");
                return false;
            }
        }
        patch.CopyTo(image, at);
        return true;
    }

    // Started outside Steam, the game asks Steam to relaunch it (which runs the stock EAC
    // executable) unless steam_appid.txt is present; it also exits at once if Steam is not running.
    private const string SteamAppIdPath = GameDirectory + @"\steam_appid.txt";
    private const string SteamPath = @"C:\Program Files (x86)\Steam\steam.exe";
    private const int MaxLaunchAttempts = 4;
    private static readonly TimeSpan QuickExit = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan SteamStartupWait = TimeSpan.FromSeconds(20);

    // Optional: Dziggy's Ultrawide UI Fixes (Nexus mod 283) loaded through me3 fixes the 16:9
    // loading screens, map, fades and menus. Used only when both are installed.
    private const string Me3Path = @"C:\EldenRingTools\me3\bin\me3.exe";
    private const string UiFixesPath = @"C:\EldenRingTools\ui-fixes";
    private const string TemporaryProcessName = "eldenring_ultrawide_tmp";
    // Optional: rumble/er_rumble.dll forwards XInput vibration to the macOS er-rumble-helper.
    private const string RumbleDllPath = @"C:\EldenRingTools\rumble\er_rumble.dll";

    private static bool UiFixesInstalled()
    {
        return File.Exists(Me3Path) && Directory.Exists(UiFixesPath + @"\menu");
    }

    private static int RunGame(bool useMe3)
    {
        var startInfo = useMe3
            ? new ProcessStartInfo
            {
                FileName = Me3Path,
                Arguments = "launch -g elden-ring --exe \"" + TemporaryPath + "\" --package \"" + UiFixesPath + "\""
                    + (File.Exists(RumbleDllPath) ? " --native \"" + RumbleDllPath + "\"" : ""),
                WorkingDirectory = Path.GetDirectoryName(Me3Path),
                UseShellExecute = false,
                CreateNoWindow = true,
                // me3 logs every hooked file open at DEBUG by default; keep it to warnings so
                // asset-heavy loads (death, fast travel) don't pay for logging under Wine.
                EnvironmentVariables = { { "ME3_FILE_LOG_LEVEL", "warn" }, { "ME3_CONSOLE_LOG_LEVEL", "warn" } }
            }
            : new ProcessStartInfo
            {
                FileName = TemporaryPath,
                WorkingDirectory = GameDirectory,
                UseShellExecute = false
            };
        int exitCode;
        using (var process = Process.Start(startInfo))
        {
            if (process == null)
                throw new InvalidOperationException("Windows did not return a process for " + startInfo.FileName);
            process.WaitForExit();
            exitCode = process.ExitCode;
        }
        // me3 may return before the game it started has exited; the temporary copy must outlive it.
        while (Process.GetProcessesByName(TemporaryProcessName).Length > 0)
            Thread.Sleep(2000);
        return exitCode;
    }

    private static bool SteamRunning()
    {
        return Process.GetProcessesByName("steam").Length > 0;
    }

    private static void EnsureSteam()
    {
        if (!File.Exists(SteamAppIdPath))
        {
            File.WriteAllText(SteamAppIdPath, "1245620");
            Log("steam_appid.txt created");
        }
        if (SteamRunning())
            return;
        Log("Steam not running; starting it");
        Process.Start(SteamPath, "-silent");
        Thread.Sleep(SteamStartupWait);
    }

    private static void ReadResolution(int screenWidth, int screenHeight, out int width, out int height)
    {
        width = screenWidth;
        height = screenHeight;
        if (!File.Exists(ResolutionPath))
            return;
        string text = File.ReadAllText(ResolutionPath).Trim().ToLowerInvariant();
        if (text == "auto" || text.Length == 0)
            return;
        string[] parts = text.Split('x');
        int w, h;
        if (parts.Length == 2 && int.TryParse(parts[0], out w) && int.TryParse(parts[1], out h) && w > 0 && h > 0)
        {
            width = w;
            height = h;
        }
        else
        {
            Log("resolution.txt ignored: expected WIDTHxHEIGHT");
        }
    }

    private static int FindUnique(byte[] data, byte[] pattern)
    {
        int found = -1;
        for (int i = 0; i <= data.Length - pattern.Length; i++)
        {
            if (data[i] != pattern[0])
                continue;
            int j = 1;
            while (j < pattern.Length && data[i + j] == pattern[j])
                j++;
            if (j < pattern.Length)
                continue;
            if (found >= 0)
                throw new InvalidDataException("resolution table signature is not unique");
            found = i;
        }
        if (found < 0)
            throw new InvalidDataException("resolution table signature not found");
        return found;
    }

    private static void Log(string message)
    {
        File.AppendAllText(LogPath, DateTime.Now.ToString("s") + " " + message + Environment.NewLine);
    }

    private static void DeleteTemporaryFiles()
    {
        try { if (File.Exists(StagingPath)) File.Delete(StagingPath); } catch { }
        try { if (File.Exists(TemporaryPath)) File.Delete(TemporaryPath); } catch { }
    }

    [STAThread]
    private static int Main()
    {
        bool ownsMutex;
        using (var mutex = new Mutex(true, @"Local\EldenRingUltrawideLauncher", out ownsMutex))
        {
            if (!ownsMutex)
            {
                Log("launch ignored: another launcher is already active");
                return 0;
            }

            Log("cold launch started");
            try
            {
                DeleteTemporaryFiles();

                int screenWidth = GetSystemMetrics(SM_CXSCREEN);
                int screenHeight = GetSystemMetrics(SM_CYSCREEN);
                bool wide = screenHeight > 0 && (double)screenWidth / screenHeight > WideAspect;

                byte[] image = File.ReadAllBytes(OriginalPath);
                int bars = wide ? FindPattern(image, BarsPattern) : -1;
                if (wide && bars < 0)
                {
                    Log("ultrawide patch not found exactly once in this game version; running unpatched (16:9)");
                    wide = false;
                }
                if (wide)
                {
                    int width, height;
                    ReadResolution(screenWidth, screenHeight, out width, out height);

                    image[bars] = 0xEB;
                    int table = FindUnique(image, ResolutionTableSignature);
                    BitConverter.GetBytes(width).CopyTo(image, table);
                    BitConverter.GetBytes(height).CopyTo(image, table + 4);

                    bool vignette = PatchPattern(image, VignettePattern, VignetteOffset, VignetteOriginal, VignettePatch, "vignette");
                    bool animation = PatchPattern(image, AnimationPattern, AnimationOffset, AnimationOriginal, AnimationPatch, "animation distance");
                    Log("screen " + screenWidth + "x" + screenHeight + ": bars at 0x" + bars.ToString("X") + ", 1920x1080 entry -> " + width + "x" + height
                        + (vignette ? ", vignette off" : "") + (animation ? ", full-rate animation" : ""));
                }
                else
                {
                    Log("screen " + screenWidth + "x" + screenHeight + " is not wider than 16:9; running unpatched");
                }
                File.WriteAllBytes(StagingPath, image);
                image = null;

                File.Move(StagingPath, TemporaryPath);
                Log("temporary executable ready");

                EnsureSteam();

                bool useMe3 = wide && UiFixesInstalled();
                Log(useMe3 ? "launching through me3 with UI fixes from " + UiFixesPath + (File.Exists(RumbleDllPath) ? " and rumble forwarding" : "") : "launching directly");
                for (int attempt = 1; attempt <= MaxLaunchAttempts; attempt++)
                {
                    var started = DateTime.Now;
                    int exitCode = RunGame(useMe3);
                    Log("game process finished: launcher_exit=" + exitCode);
                    // A near-instant exit means Steam was still signing in; give it time and retry.
                    if (DateTime.Now - started >= QuickExit || attempt == MaxLaunchAttempts)
                        break;
                    Log("game exited quickly (attempt " + attempt + "); waiting for Steam and retrying");
                    Thread.Sleep(SteamStartupWait);
                }
                return 0;
            }
            catch (Exception error)
            {
                Log("ERROR: " + error.GetType().Name + ": " + error.Message);
                return 1;
            }
            finally
            {
                DeleteTemporaryFiles();
                Log("temporary executable cleaned");
                mutex.ReleaseMutex();
            }
        }
    }
}

