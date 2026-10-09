using System;
using System.Diagnostics;
using System.IO;
using UnityEngine;

namespace MineHeim
{
    /// <summary>
    /// Minecraft comes and goes with Valheim: when Valheim starts and no Minecraft is listening, the MineHeim instance is
    /// started through Prism Launcher (installed by Install.cmd, signed in with your own Microsoft account), or through
    /// Minecraft.LaunchCommand. While attached its window is hidden (the picture is in Valheim's), and it saves and quits
    /// when Valheim closes.
    /// </summary>
    internal static class MinecraftLauncher
    {
        public const string PrismInstance = "MineHeim";
        private static bool _tried, _toldHidden;
        private static float _startAt = -1f, _startedAt = -100f;

        /// <summary>Where Install.cmd puts its Prism Launcher (portable): %LOCALAPPDATA%\MineHeim\PrismLauncher.</summary>
        public static string PrismExe => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MineHeim", "PrismLauncher", "prismlauncher.exe");

        public static void Tick()
        {
            if (Link.Connected)
            {
                if (!_toldHidden)
                {
                    _toldHidden = true;
                    // and: if Valheim goes without saying so (a crash, Task Manager), Minecraft follows it when QuitWithValheim is on
                    Link.Send("{\"t\":\"window\",\"hidden\":" + (Plugin.HideMinecraftWindow.Value ? "true" : "false") +
                              ",\"host\":" + Process.GetCurrentProcess().Id + ",\"quitWithHost\":" + (Plugin.QuitWithValheim.Value ? "true" : "false") + "}");
                }

                return;
            }

            _toldHidden = false;
            if (_tried || !Plugin.AutoStart.Value)
            {
                return;
            }

            // give an already running Minecraft a few seconds to answer before starting one
            if (_startAt < 0f)
            {
                _startAt = Time.unscaledTime + 4f;
                return;
            }

            if (Time.unscaledTime < _startAt)
            {
                return;
            }

            _tried = true;
            Start();
        }

        /// <summary>
        /// The status key while Minecraft is not attached: start it again (it crashed, or was closed). Not twice within
        /// a minute: Minecraft takes a while to load, and a second one would fight the first for the link.
        /// </summary>
        public static string StartAgain()
        {
            if (Link.Connected)
            {
                return "Minecraft is running";
            }

            if (Time.unscaledTime - _startedAt < 60f)
            {
                return "Minecraft is starting (give it a minute)";
            }

            return Start() ? "starting Minecraft..." : "cannot start Minecraft by itself: start it yourself (see BepInEx/LogOutput.log)";
        }

        private static bool Start()
        {
            string command = Plugin.LaunchCommand.Value.Trim();
            string exe, args;
            if (command.Length > 0)
            {
                // "C:\path\to\program.exe" --some args   (quotes optional when the path has no spaces)
                if (command.StartsWith("\""))
                {
                    int end = command.IndexOf('"', 1);
                    exe = end > 0 ? command.Substring(1, end - 1) : command.Trim('"');
                    args = end > 0 ? command.Substring(end + 1).Trim() : "";
                }
                else
                {
                    int space = command.IndexOf(' ');
                    exe = space > 0 ? command.Substring(0, space) : command;
                    args = space > 0 ? command.Substring(space + 1).Trim() : "";
                }
            }
            else if (File.Exists(PrismExe))
            {
                exe = PrismExe;
                args = "--launch " + PrismInstance;
            }
            else
            {
                Plugin.Log.LogInfo("minecraft auto-start: no Prism Launcher from Install.cmd and no Minecraft.LaunchCommand; start Minecraft yourself");
                return false;
            }

            try
            {
                Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe) ?? "" });
                Plugin.Log.LogInfo("minecraft auto-start: " + exe + " " + args);
                _startedAt = Time.unscaledTime;
                return true;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("minecraft auto-start failed (" + exe + "): " + e.Message);
                return false;
            }
        }

        /// <summary>Valheim is closing: Minecraft saves and quits too.</summary>
        public static void OnQuit()
        {
            if (Plugin.QuitWithValheim.Value && Link.Connected)
            {
                Link.Send("{\"t\":\"quit\"}"); // Link.Stop, right after, waits for it to leave
            }
        }
    }
}
