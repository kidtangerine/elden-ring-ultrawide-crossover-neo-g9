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
    private const long PatchOffset = 0x19ED05E;
    private static readonly byte[] ExpectedSignature = { 0x74, 0x4F, 0x45, 0x8B, 0x94, 0xCC };

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
    private const long VignetteOffset = 0x1B8DE7C;
    private static readonly byte[] VignetteOriginal = { 0xF3, 0x45, 0x0F, 0x59, 0xC2 };
    private static readonly byte[] VignettePatch = { 0xF3, 0x0F, 0x5C, 0xC0, 0x90 };

    // Entities outside the 16:9 view get low-rate animation updates, so NPCs at the sides of
    // a wider screen move in slow motion. Replace DIVSS XMM1,[R12+0x54] with XORPS XMM1,XMM1;
    // PXOR XMM1,XMM1 (gurrgur/er-patcher --increase-animation-distance).
    private const long AnimationOffset = 0xB12677;
    private static readonly byte[] AnimationOriginal = { 0xF3, 0x41, 0x0F, 0x5E, 0x4C, 0x24, 0x54 };
    private static readonly byte[] AnimationPatch = { 0x0F, 0x57, 0xC9, 0x66, 0x0F, 0xEF, 0xC9 };

    private static bool PatchIfMatches(byte[] image, long offset, byte[] original, byte[] patch, string name)
    {
        for (int i = 0; i < original.Length; i++)
        {
            if (image[offset + i] != original[i])
            {
                Log(name + " signature mismatch; left unchanged");
                return false;
            }
        }
        patch.CopyTo(image, offset);
        return true;
    }

    // Started outside Steam, the game asks Steam to relaunch it (which runs the stock EAC
    // executable) unless steam_appid.txt is present; it also exits at once if Steam is not running.
    private const string SteamAppIdPath = GameDirectory + @"\steam_appid.txt";
    private const string SteamPath = @"C:\Program Files (x86)\Steam\steam.exe";
    private const int MaxLaunchAttempts = 4;
    private static readonly TimeSpan QuickExit = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan SteamStartupWait = TimeSpan.FromSeconds(20);

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

                using (var source = new FileStream(OriginalPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    if (source.Length <= PatchOffset + ExpectedSignature.Length)
                        throw new InvalidDataException("clean executable is smaller than the expected patch offset");

                    source.Position = PatchOffset;
                    for (int index = 0; index < ExpectedSignature.Length; index++)
                    {
                        int value = source.ReadByte();
                        if (value != ExpectedSignature[index])
                            throw new InvalidDataException("ultrawide signature mismatch; clean executable was not modified");
                    }
                }

                int screenWidth = GetSystemMetrics(SM_CXSCREEN);
                int screenHeight = GetSystemMetrics(SM_CYSCREEN);
                bool wide = screenHeight > 0 && (double)screenWidth / screenHeight > WideAspect;

                byte[] image = File.ReadAllBytes(OriginalPath);
                if (wide)
                {
                    int width, height;
                    ReadResolution(screenWidth, screenHeight, out width, out height);

                    image[PatchOffset] = 0xEB;
                    int table = FindUnique(image, ResolutionTableSignature);
                    BitConverter.GetBytes(width).CopyTo(image, table);
                    BitConverter.GetBytes(height).CopyTo(image, table + 4);

                    bool vignette = PatchIfMatches(image, VignetteOffset, VignetteOriginal, VignettePatch, "vignette");
                    bool animation = PatchIfMatches(image, AnimationOffset, AnimationOriginal, AnimationPatch, "animation distance");
                    Log("screen " + screenWidth + "x" + screenHeight + ": offset=0x19ED05E, 1920x1080 entry -> " + width + "x" + height
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

                var startInfo = new ProcessStartInfo
                {
                    FileName = TemporaryPath,
                    WorkingDirectory = GameDirectory,
                    UseShellExecute = false
                };
                for (int attempt = 1; attempt <= MaxLaunchAttempts; attempt++)
                {
                    var started = DateTime.Now;
                    using (var game = Process.Start(startInfo))
                    {
                        if (game == null)
                            throw new InvalidOperationException("Windows did not return an Elden Ring process");
                        game.WaitForExit();
                        Log("game process finished: launcher_exit=" + game.ExitCode);
                    }
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

