using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

using Dalamud.Common;
using Dalamud.Common.Game;

using Dalamud.Common.Util;
using Newtonsoft.Json;
using Reloaded.Memory.Buffers;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Dalamud.Injector
{
    /// <summary>
    /// Entrypoint to the program.
    /// </summary>
    public sealed class Program
    {
        /// <summary>
        /// Start the Dalamud injector.
        /// </summary>
        /// <param name="argsArray">Command line arguments.</param>
        /// <returns>Return value (HRESULT).</returns>
        public static int Main(string[] argsArray)
        {
            try
            {
                // API14 TODO: Refactor
                var args = argsArray.ToList();
                args.Insert(0, Assembly.GetExecutingAssembly().Location);

                Init(args);
                args.Remove("-v"); // Remove "verbose" flag

                DalamudStartInfo? startInfo = null;
                if (args.Count == 1)
                {
#if !DEBUG
                    Log.Error("You must provide at least one argument.");
                    return 1;
#else
                    // No command defaults to inject
                    args.Add("inject");
                    args.Add("--all");
#endif
                }
                else if (int.TryParse(args[1], out var _))
                {
                    // Assume that PID has been passed.
                    args.Insert(1, "inject");

                    // If originally second parameter exists, then assume that it's a base64 encoded start info.
                    // Dalamud.Injector.exe inject [pid] [base64]
                    if (args.Count == 4)
                    {
                        startInfo = JsonConvert.DeserializeObject<DalamudStartInfo>(Encoding.UTF8.GetString(Convert.FromBase64String(args[3])));
                        args.RemoveAt(3);
                    }
                }

                startInfo = ExtractAndInitializeStartInfoFromArguments(startInfo, args);
                // Remove already handled arguments
                args.Remove("--debug-directx");
                args.Remove("--console");
                args.Remove("--msgbox1");
                args.Remove("--msgbox2");
                args.Remove("--msgbox3");
                args.Remove("--etw");
                args.Remove("--no-legacy-corrupted-state-exceptions");
                args.Remove("--veh");
                args.Remove("--veh-full");
                args.Remove("--no-plugin");
                args.Remove("--no-3rd-plugin");
                args.Remove("--crash-handler-console");

                var mainCommand = args[1].ToLowerInvariant();
                if (mainCommand.Length > 0 && mainCommand.Length <= 6 && "inject"[..mainCommand.Length] == mainCommand)
                {
                    return ProcessInjectCommand(args, startInfo);
                }
                else if (mainCommand.Length > 0 && mainCommand.Length <= 4 &&
                         "help"[..mainCommand.Length] == mainCommand)
                {
                    return ProcessHelpCommand(args, args.Count >= 3 ? args[2] : null);
                }
                else
                {
                    throw new CommandLineException($"\"{mainCommand}\" is not a valid command.");
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Operation failed.");
                return e.HResult;
            }
        }

        private static string GetLogPath(string? baseDirectory, string fileName, string? logName)
        {
            baseDirectory ??= Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            baseDirectory ??= Environment.CurrentDirectory;
            fileName = !string.IsNullOrEmpty(logName) ? $"{fileName}-{logName}.log" : $"{fileName}.log";

            // TODO(api9): remove
            var previousLogPath = Path.Combine(baseDirectory, "..", "..", "..", fileName);
            if (File.Exists(previousLogPath))
                File.Delete(previousLogPath);

            return Path.Combine(baseDirectory, fileName);
        }

        private static void Init(List<string> args)
        {
            InitLogging(args.Any(x => x == "-v"), args);
            InitUnhandledException(args);

            var cwd = new FileInfo(Assembly.GetExecutingAssembly().Location).Directory
                      ?? throw new DirectoryNotFoundException("Could not determine binary location.");
            if (cwd.FullName != Directory.GetCurrentDirectory())
            {
                Log.Debug($"Changing cwd to {cwd}");
                Directory.SetCurrentDirectory(cwd.FullName);
            }
        }

        private static void InitUnhandledException(List<string> args)
        {
            AppDomain.CurrentDomain.UnhandledException += (sender, eventArgs) =>
            {
                var exObj = eventArgs.ExceptionObject;

                if (exObj is CommandLineException clex)
                {
                    Console.WriteLine();
                    Console.WriteLine("Command line error: {0}", clex.Message);
                    Console.WriteLine();
                    ProcessHelpCommand(args);
                }
                else if (Log.Logger == null)
                {
                    Console.WriteLine($"A fatal error has occurred: {eventArgs.ExceptionObject}");
                }
                else if (exObj is Exception ex)
                {
                    Log.Error(ex, "A fatal error has occurred");
                }
                else
                {
                    Log.Error("A fatal error has occurred: {Exception}", eventArgs.ExceptionObject.ToString());
                }

                Log.CloseAndFlush();
                Environment.Exit(-1);
            };
        }

        private static void InitLogging(bool verbose, IEnumerable<string> args)
        {
            var levelSwitch = new LoggingLevelSwitch
            {
                MinimumLevel = verbose ? LogEventLevel.Verbose : LogEventLevel.Information,
            };

            var logName = args.FirstOrDefault(x => x.StartsWith("--logname="))?[10..];
            var logBaseDir = args.FirstOrDefault(x => x.StartsWith("--logpath="))?[10..];
            var logPath = GetLogPath(logBaseDir, "dalamud.injector", logName);

            CullLogFile(logPath, 1 * 1024 * 1024);

            const long maxLogSize = 100 * 1024 * 1024; // 100MB
            Log.Logger = new LoggerConfiguration()
                         .WriteTo.Console(standardErrorFromLevel: LogEventLevel.Debug)
                         .WriteTo.File(logPath, fileSizeLimitBytes: maxLogSize)
                         .MinimumLevel.ControlledBy(levelSwitch)
                         .CreateLogger();

            Log.Information(new string('-', 80));
            Log.Information("Dalamud.Injector, (c) 2023 XIVLauncher Contributors");
        }

        private static void CullLogFile(string logPath, int cullingFileSize)
        {
            try
            {
                var bufferSize = 4096;

                var logFile = new FileInfo(logPath);

                // Leave it to serilog
                if (!logFile.Exists)
                {
                    return;
                }

                if (logFile.Length <= cullingFileSize)
                {
                    return;
                }

                var amountToCull = logFile.Length - cullingFileSize;

                if (amountToCull < bufferSize)
                {
                    return;
                }

                using var reader = new BinaryReader(logFile.Open(FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
                using var writer = new BinaryWriter(logFile.Open(FileMode.Open, FileAccess.Write, FileShare.ReadWrite));

                reader.BaseStream.Seek(amountToCull, SeekOrigin.Begin);

                var read = -1;
                var total = 0;
                var buffer = new byte[bufferSize];
                while (read != 0)
                {
                    read = reader.Read(buffer, 0, buffer.Length);
                    writer.Write(buffer, 0, read);
                    total += read;
                }

                writer.BaseStream.SetLength(total);
            }
            catch (Exception)
            {
                /*
                var caption = "XIVLauncher Error";
                var message = $"Log cull threw an exception: {ex.Message}\n{ex.StackTrace ?? string.Empty}";
                _ = MessageBoxW(IntPtr.Zero, message, caption, MessageBoxType.IconError | MessageBoxType.Ok);
                */
            }
        }

        private static OSPlatform DetectPlatformHeuristic()
        {
            var ntdll = Windows.Win32.PInvoke.GetModuleHandle("ntdll.dll");
            var wineServerCallPtr = Windows.Win32.PInvoke.GetProcAddress(ntdll, "wine_server_call");
            var wineGetHostVersionPtr = Windows.Win32.PInvoke.GetProcAddress(ntdll, "wine_get_host_version");
            var winePlatform = GetWinePlatform(wineGetHostVersionPtr);
            var isWine = wineServerCallPtr != nint.Zero;

            static unsafe string? GetWinePlatform(nint wineGetHostVersionPtr)
            {
                if (wineGetHostVersionPtr == nint.Zero) return null;

                var methodDelegate = (delegate* unmanaged[Cdecl]<out char*, out char*, void>)wineGetHostVersionPtr;
                methodDelegate(out var platformPtr, out var _);

                if (platformPtr == null) return null;

                return Marshal.PtrToStringAnsi((nint)platformPtr);
            }

            if (!isWine)
                return OSPlatform.Windows;

            if (winePlatform == "Darwin")
                return OSPlatform.OSX;

            return OSPlatform.Linux;
        }

        private static DalamudStartInfo ExtractAndInitializeStartInfoFromArguments(DalamudStartInfo? startInfo, List<string> args)
        {
            int len;
            string key;

            startInfo ??= new DalamudStartInfo();

            var workingDirectory = startInfo.WorkingDirectory;
            var configurationPath = startInfo.ConfigurationPath;
            var pluginDirectory = startInfo.PluginDirectory;
            var assetDirectory = startInfo.AssetDirectory;
            var runtimeDirectory = startInfo.RuntimeDirectory;
            var tempDirectory = startInfo.TempDirectory;
            var delayInitializeMs = startInfo.DelayInitializeMs;
            var logName = startInfo.LogName;
            var logPath = startInfo.LogPath;
            var languageStr = startInfo.Language.ToString().ToLowerInvariant();
            var platformStr = startInfo.Platform.ToString().ToLowerInvariant();
            var unhandledExceptionStr = startInfo.UnhandledException.ToString().ToLowerInvariant();
            var troubleshootingData = "{\"empty\": true, \"description\": \"No troubleshooting data supplied.\"}";
            var launcherDirectory = startInfo.LauncherDirectory;

            // env vars are brought in prior to launch args, since args can override them.
            if (EnvironmentUtils.TryGetEnvironmentVariable("XL_PLATFORM", out var xlPlatformEnv))
                platformStr = xlPlatformEnv.ToLowerInvariant();

            for (var i = 2; i < args.Count; i++)
            {
                if (args[i].StartsWith(key = "--dalamud-working-directory="))
                {
                    workingDirectory = args[i][key.Length..];
                }
                else if (args[i].StartsWith(key = "--dalamud-configuration-path="))
                {
                    configurationPath = args[i][key.Length..];
                }
                else if (args[i].StartsWith(key = "--dalamud-plugin-directory="))
                {
                    pluginDirectory = args[i][key.Length..];
                }
                else if (args[i].StartsWith(key = "--dalamud-asset-directory="))
                {
                    assetDirectory = args[i][key.Length..];
                }
                else if (args[i].StartsWith(key = "--dalamud-runtime-directory="))
                {
                    runtimeDirectory = args[i][key.Length..];
                }
                else if (args[i].StartsWith(key = "--dalamud-temp-directory="))
                {
                    tempDirectory = args[i][key.Length..];
                }
                else if (args[i].StartsWith(key = "--dalamud-delay-initialize="))
                {
                    delayInitializeMs = int.Parse(args[i][key.Length..]);
                }
                else if (args[i].StartsWith(key = "--dalamud-client-language="))
                {
                    languageStr = args[i][key.Length..].ToLowerInvariant();
                }
                else if (args[i].StartsWith(key = "--dalamud-platform="))
                {
                    platformStr = args[i][key.Length..].ToLowerInvariant();
                }
                else if (args[i].StartsWith(key = "--dalamud-tspack-b64="))
                {
                    troubleshootingData = Encoding.UTF8.GetString(Convert.FromBase64String(args[i][key.Length..]));
                }
                else if (args[i].StartsWith(key = "--logname="))
                {
                    logName = args[i][key.Length..];
                }
                else if (args[i].StartsWith(key = "--logpath="))
                {
                    logPath = args[i][key.Length..];
                }
                else if (args[i].StartsWith(key = "--unhandled-exception="))
                {
                    unhandledExceptionStr = args[i][key.Length..];
                }
                else if (args[i].StartsWith(key = "--launcher-directory="))
                {
                    launcherDirectory = args[i][key.Length..];
                }
                else
                {
                    continue;
                }

                args.RemoveAt(i);
                i--;
            }

            var appDataDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var standaloneDir = Path.Combine(appDataDir, "DalamudStandaloneCN");

            workingDirectory ??= Directory.GetCurrentDirectory();
            configurationPath ??= Path.Combine(standaloneDir, "dalamudConfig.json");
            pluginDirectory ??= Path.Combine(standaloneDir, "installedPlugins");
            assetDirectory ??= Path.Combine(standaloneDir, "assets", "current");
            runtimeDirectory ??= Path.Combine(standaloneDir, "runtime");

            ClientLanguage clientLanguage;
            if (languageStr[0..(len = Math.Min(languageStr.Length, (key = "english").Length))] == key[0..len])
            {
                clientLanguage = ClientLanguage.English;
            }
            else if (languageStr[0..(len = Math.Min(languageStr.Length, (key = "japanese").Length))] == key[0..len])
            {
                clientLanguage = ClientLanguage.Japanese;
            }
            else if (languageStr[0..(len = Math.Min(languageStr.Length, (key = "日本語").Length))] == key[0..len])
            {
                clientLanguage = ClientLanguage.Japanese;
            }
            else if (languageStr[0..(len = Math.Min(languageStr.Length, (key = "german").Length))] == key[0..len])
            {
                clientLanguage = ClientLanguage.German;
            }
            else if (languageStr[0..(len = Math.Min(languageStr.Length, (key = "deutsch").Length))] == key[0..len])
            {
                clientLanguage = ClientLanguage.German;
            }
            else if (languageStr[0..(len = Math.Min(languageStr.Length, (key = "french").Length))] == key[0..len])
            {
                clientLanguage = ClientLanguage.French;
            }
            else if (languageStr[0..(len = Math.Min(languageStr.Length, (key = "français").Length))] == key[0..len])
            {
                clientLanguage = ClientLanguage.French;
            }
            else if (languageStr[0..(len = Math.Min(languageStr.Length, (key = "chinesesimplified").Length))] == key[0..len])
            {
                clientLanguage = ClientLanguage.ChineseSimplified;
            }
            else if (int.TryParse(languageStr, out var languageInt) && Enum.IsDefined((ClientLanguage)languageInt))
            {
                clientLanguage = (ClientLanguage)languageInt;
            }
            else
            {
                throw new CommandLineException($"\"{languageStr}\" is not a valid supported language.");
            }

            OSPlatform platform;

            // covers both win32 and Windows
            if (platformStr[0..(len = Math.Min(platformStr.Length, (key = "win").Length))] == key[0..len])
            {
                platform = OSPlatform.Windows;
            }
            else if (platformStr[0..(len = Math.Min(platformStr.Length, (key = "linux").Length))] == key[0..len])
            {
                platform = OSPlatform.Linux;
            }
            else if (platformStr[0..(len = Math.Min(platformStr.Length, (key = "macos").Length))] == key[0..len])
            {
                platform = OSPlatform.OSX;
            }
            else if (platformStr[0..(len = Math.Min(platformStr.Length, (key = "osx").Length))] == key[0..len])
            {
                platform = OSPlatform.OSX;
            }
            else
            {
                platform = DetectPlatformHeuristic();
                Log.Warning("Heuristically determined host system platform as {platform}", platform);
            }

            startInfo.WorkingDirectory = workingDirectory;
            startInfo.ConfigurationPath = configurationPath;
            startInfo.PluginDirectory = pluginDirectory;
            startInfo.AssetDirectory = assetDirectory;
            startInfo.RuntimeDirectory = runtimeDirectory;
            startInfo.TempDirectory = tempDirectory;
            startInfo.Language = clientLanguage;
            startInfo.Platform = platform;
            startInfo.DelayInitializeMs = delayInitializeMs;
            startInfo.GameVersion = null;
            startInfo.TroubleshootingPackData = troubleshootingData;
            startInfo.LogName = logName;
            startInfo.LogPath = logPath;
            startInfo.LauncherDirectory = launcherDirectory;

#if DEBUG
            startInfo.LogPath ??= startInfo.WorkingDirectory;
#else
            startInfo.LogPath ??= Path.Combine(standaloneDir, "logs");
#endif
            startInfo.LogName ??= string.Empty;

            // Set boot defaults
            startInfo.BootDebugDirectX = args.Contains("--debug-directx");
            startInfo.BootShowConsole = args.Contains("--console");
            startInfo.BootEnableEtw = args.Contains("--etw");
            startInfo.BootLogPath = GetLogPath(startInfo.LogPath, "dalamud.boot", startInfo.LogName);
            startInfo.BootEnabledGameFixes = new()
            {
                // See: xivfixes.h, xivfixes.cpp
                "prevent_devicechange_crashes",
                "disable_game_openprocess_access_check",
                "redirect_openprocess",
                "backup_userdata_save",
                "prevent_icmphandle_crashes",
                "symbol_load_patches",
                "disable_game_debugging_protection",
                "faster_decompression",
            };
            startInfo.BootDotnetOpenProcessHookMode = 0;
            startInfo.BootWaitMessageBox |= args.Contains("--msgbox1") ? 1 : 0;
            startInfo.BootWaitMessageBox |= args.Contains("--msgbox2") ? 2 : 0;
            startInfo.BootWaitMessageBox |= args.Contains("--msgbox3") ? 4 : 0;
            // startInfo.BootVehEnabled = args.Contains("--veh");
            startInfo.BootVehEnabled = true;
            startInfo.BootVehFull = args.Contains("--veh-full");
            startInfo.NoLoadPlugins = args.Contains("--no-plugin");
            startInfo.NoLoadThirdPartyPlugins = args.Contains("--no-3rd-plugin");
            // startInfo.BootUnhookDlls = new List<string>() { "kernel32.dll", "ntdll.dll", "user32.dll" };
            startInfo.CrashHandlerShow = args.Contains("--crash-handler-console");
            startInfo.UnhandledException =
                Enum.TryParse<UnhandledExceptionHandlingMode>(
                    unhandledExceptionStr,
                    true,
                    out var parsedUnhandledException)
                    ? parsedUnhandledException
                    : throw new CommandLineException(
                          $"\"{unhandledExceptionStr}\" is not a valid unhandled exception handling mode.");

            return startInfo;
        }

        private static int ProcessHelpCommand(List<string> args, string? particularCommand = default)
        {
            var exeName = Path.GetFileName(args[0]);

            if (particularCommand is null or "help")
            {
                Console.WriteLine("{0} help [command]", exeName);
            }

            if (particularCommand is null or "inject")
            {
                Console.WriteLine("{0} inject [-h/--help] [-a/--all] [--warn] [--fix-acl] [--se-debug-privilege] [pid1] [pid2] [pid3] ...", exeName);
            }

            Console.WriteLine("Specifying dalamud start info: [--dalamud-working-directory=path] [--dalamud-configuration-path=path]");
            Console.WriteLine("                               [--dalamud-plugin-directory=path] [--dalamud-platform=win32|linux|macOS]");
            Console.WriteLine("                               [--dalamud-asset-directory=path] [--dalamud-runtime-directory=path]");
            Console.WriteLine("                               [--dalamud-delay-initialize=0(ms)]");
            Console.WriteLine("                               [--dalamud-client-language=0-3|j(apanese)|e(nglish)|d|g(erman)|f(rench)]");

            Console.WriteLine("Verbose logging:\t[-v]");
            Console.WriteLine("Show Console:\t[--console] [--crash-handler-console]");
            Console.WriteLine("Enable ETW:\t[--etw]");
            Console.WriteLine("Disable legacy corrupted state exceptions:\t[--no-legacy-corrupted-state-exceptions]");
            Console.WriteLine("Enable VEH:\t[--veh], [--veh-full], [--unhandled-exception=default|stalldebug|none]");
            Console.WriteLine("Show messagebox:\t[--msgbox1], [--msgbox2], [--msgbox3]");
            Console.WriteLine("No plugins:\t[--no-plugin] [--no-3rd-plugin]");
            Console.WriteLine("Logging:\t[--logname=<logfile suffix>] [--logpath=<log base directory>]");

            return 0;
        }

        private static int ProcessInjectCommand(List<string> args, DalamudStartInfo dalamudStartInfo)
        {
            List<Process> processes = new();

            var targetProcessSpecified = false;
            var warnManualInjection = false;
            var showHelp = args.Count <= 2;
            var tryFixAcl = false;
            var tryClaimSeDebugPrivilege = false;

            for (var i = 2; i < args.Count; i++)
            {
                if (int.TryParse(args[i], out int pid))
                {
                    targetProcessSpecified = true;
                    try
                    {
                        processes.Add(Process.GetProcessById(pid));
                    }
                    catch (ArgumentException)
                    {
                        Log.Error("Could not find process with PID: {Pid}", pid);
                    }

                    continue;
                }

                if (args[i] == "-h" || args[i] == "--help")
                {
                    showHelp = true;
                }
                else if (args[i] == "-a" || args[i] == "--all")
                {
                    targetProcessSpecified = true;
                    processes.AddRange(Process.GetProcessesByName("ffxiv_dx11"));
                }
                else if (args[i] == "--fix-acl" || args[i] == "--acl-fix")
                {
                    tryFixAcl = true;
                }
                else if (args[i] == "--se-debug-privilege")
                {
                    tryClaimSeDebugPrivilege = true;
                }
                else if (args[i] == "--warn")
                {
                    warnManualInjection = true;
                }
                else
                {
                    Log.Warning($"\"{args[i]}\" is not a valid command line argument, ignoring.");
                }
            }

            if (showHelp)
            {
                ProcessHelpCommand(args, "inject");
                return args.Count <= 2 ? -1 : 0;
            }

            if (!targetProcessSpecified)
            {
                throw new CommandLineException("No target process has been specified. Use -a(--all) option to inject to all ffxiv_dx11.exe processes.");
            }
            else if (!processes.Any())
            {
                Log.Error("No suitable target process has been found.");
                return -1;
            }

            if (warnManualInjection)
            {
                var result = Windows.Win32.PInvoke.MessageBox(
                    HWND.Null,
                    $"Take care: you are manually injecting Dalamud into FFXIV({string.Join(", ", processes.Select(x => $"{x.Id}"))}).\n\nIf you are doing this to use plugins before they are officially whitelisted on patch days, things may go wrong and you may get into trouble.\nWe discourage you from doing this and you won't be warned again in-game.",
                    "Dalamud",
                    MESSAGEBOX_STYLE.MB_ICONWARNING | MESSAGEBOX_STYLE.MB_OKCANCEL);

                if (result == MESSAGEBOX_RESULT.IDCANCEL)
                {
                    Log.Information("User cancelled injection");
                    return -2;
                }
            }

            if (tryClaimSeDebugPrivilege)
            {
                try
                {
                    GameStart.ClaimSeDebug();
                    Log.Information("SeDebugPrivilege claimed.");
                }
                catch (Win32Exception e2)
                {
                    Log.Warning(e2, "Failed to claim SeDebugPrivilege");
                }
            }

            foreach (var process in processes)
            {
                var processBinaryPath = process.MainModule?.FileName
                    ?? throw new CommandLineException($"Could not determine binary path for process {process.Id}.");

                Inject(process, AdjustStartInfo(dalamudStartInfo, processBinaryPath), tryFixAcl);
            }

            Log.CloseAndFlush();
            return 0;
        }

        private static DalamudStartInfo AdjustStartInfo(DalamudStartInfo startInfo, string gamePath)
        {
            var ffxivDir = Path.GetDirectoryName(gamePath)
                           ?? throw new DirectoryNotFoundException($"Could not determine parent directory of {gamePath}.");
            var gameVerStr = File.ReadAllText(Path.Combine(ffxivDir, "ffxivgame.ver"));
            var gameVer = GameVersion.Parse(gameVerStr);

            return startInfo with
            {
                GameVersion = gameVer,
            };
        }

        private static void Inject(Process process, DalamudStartInfo startInfo, bool tryFixAcl = false)
        {
            if (tryFixAcl)
            {
                try
                {
                    GameStart.CopyAclFromSelfToTargetProcess(process.SafeHandle.DangerousGetHandle());
                }
                catch (Win32Exception e1)
                {
                    Log.Warning(e1, "Failed to copy ACL");
                }
            }

            var bootName = "Dalamud.Boot.dll";
            var bootPath = Path.GetFullPath(bootName);

            // ======================================================

            using var injector = new Injector(process, false);

            injector.LoadLibrary(bootPath, out var bootModule);

            // ======================================================

            var startInfoJson = JsonConvert.SerializeObject(startInfo);
            var startInfoBytes = Encoding.UTF8.GetBytes(startInfoJson);

            using var startInfoBuffer = new MemoryBufferHelper(process).CreatePrivateMemoryBuffer(startInfoBytes.Length + 0x8);
            var startInfoAddress = startInfoBuffer.Add(startInfoBytes);

            if (startInfoAddress == 0)
            {
                throw new Exception("Unable to allocate start info JSON");
            }

            injector.GetFunctionAddress(bootModule, "Initialize", out var initAddress);
            var exitCode = injector.CallRemoteFunction(initAddress, startInfoAddress);

            // ======================================================

            if (exitCode > 0)
            {
                Log.Error("Dalamud.Boot::Initialize returned {ExitCode}", exitCode);
                return;
            }

            Log.Information("Done");
        }

        private class CommandLineException : Exception
        {
            public CommandLineException(string cause)
                : base(cause)
            {
            }
        }
    }
}
