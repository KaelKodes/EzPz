using Godot;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;

public partial class ServerManager : Node
{
    [Signal] public delegate void LogReceivedEventHandler(string profileName, string message, bool isError);
    [Signal] public delegate void StatusChangedEventHandler(string profileName, string newStatus);
    [Signal] public delegate void PlayerListReceivedEventHandler(string profileName, string[] players);

    private Dictionary<string, Process> _serverProcesses = new Dictionary<string, Process>();
    private Dictionary<string, List<string>> _pendingPlayerLists = new Dictionary<string, List<string>>();
    private Dictionary<string, bool> _awaitingPlayerList = new Dictionary<string, bool>();

    public bool IsRunning(string profileName) => _serverProcesses.ContainsKey(profileName) && !_serverProcesses[profileName].HasExited;

    public void StartServer(string profileName, string path, string adminPassword, string minRam = "2G", string maxRam = "4G", string extraJvmFlags = "")
    {
        if (IsRunning(profileName)) return;

        // Common PZ Dedicated Server launcher names
        string[] possibleExes = { "StartServer64.bat", "ProjectZomboid64.exe", "StartServer64.exe" };
        string exePath = FindExecutable(profileName, path, possibleExes);

        if (string.IsNullOrEmpty(exePath))
        {
            EmitSignal(SignalName.LogReceived, profileName, $"Error: Could not find a valid Project Zomboid server launcher in {path}", true);
            return;
        }

        string workingDir = Path.GetDirectoryName(exePath);
        bool isBatch = exePath.EndsWith(".bat", StringComparison.OrdinalIgnoreCase);

        string javaPath = "";
        string launchArguments = "";

        if (isBatch)
        {
            // If it's a batch file, we bypass it and launch java directly to avoid the %1 %2 argument limit
            string jrePath = Path.Combine(workingDir, "jre64", "bin", "java.exe");
            if (!File.Exists(jrePath)) jrePath = Path.Combine(workingDir, "jre", "bin", "java.exe");

            if (File.Exists(jrePath))
            {
                javaPath = jrePath;
                // Split JVM flags from the target class to allow inserting RAM/extra flags in between
                launchArguments = "-Djava.awt.headless=true -Dzomboid.steam=1 -Dzomboid.znetlog=1 -XX:+UseZGC -XX:-CreateCoredumpOnCrash -XX:-OmitStackTraceInFastThrow -Djava.library.path=natives/;natives/win64/;. -cp java/;java/projectzomboid.jar";
            }
            else
            {
                // Fallback to batch if java not found (risky but better than nothing)
                javaPath = "cmd.exe";
                launchArguments = $"/c \"\"{exePath}\"";
            }
        }
        else
        {
            javaPath = exePath;
        }

        // Find the actual config path to determine if we need -cachedir
        string iniPath = ConfigManager.GetConfigPath(path, profileName, ".ini");
        string configDir = Path.GetDirectoryName(iniPath);

        // Construct our config arguments
        // IMPORTANT: We only pass -cachedir if the config is NOT in the default UserProfile location.
        // And if it IS in a local folder, we point -cachedir to the PARENT of the 'Server' or 'Zomboid' folder.
        string configArgs = $"-servername \"{profileName}\" -adminpassword \"{adminPassword}\"";

        // IMPORTANT: Project Zomboid dedicated server looks for configs in Zomboid/Server
        // if we are in a custom folder, -cachedir should point to the folder ABOVE 'Zomboid'
        string userProfilePath = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile), "Zomboid", "Server");
        bool isUserProfile = configDir.StartsWith(userProfilePath, StringComparison.OrdinalIgnoreCase);

        if (!isUserProfile)
        {
            // If the user has it in Path/Server or Path/Zomboid/Server, we need to make sure PZ finds it.
            // When we pass -cachedir="E:/PZTEST", PZ looks in E:/PZTEST/Zomboid/Server
            configArgs += $" -cachedir=\"{path}\"";

            // B42+ requires a 'mods' folder in the root or cachedir sometimes causes issues if missing
            string modsDir = Path.Combine(path, "mods");
            if (!Directory.Exists(modsDir)) Directory.CreateDirectory(modsDir);
        }

        // Construct JVM arguments (MUST come before the class name)
        string jvmArgs = "";
        if (!extraJvmFlags.Contains("-Xmx", StringComparison.OrdinalIgnoreCase))
        {
            jvmArgs += $" -Xmx{maxRam}";
        }
        if (!extraJvmFlags.Contains("-Xms", StringComparison.OrdinalIgnoreCase))
        {
            jvmArgs += $" -Xms{minRam}";
        }

        if (!string.IsNullOrEmpty(extraJvmFlags))
        {
            jvmArgs += $" {extraJvmFlags}";
        }

        // Combine launch, JVM, and config arguments
        string finalArguments = "";
        if (isBatch && javaPath != "cmd.exe")
        {
            // java.exe [jvmArgs] [standardPZArgs] zombie.network.GameServer [configArgs]
            finalArguments = $"{jvmArgs} {launchArguments} zombie.network.GameServer {configArgs}";
        }
        else if (isBatch)
        {
            // cmd.exe /c "StartServer64.bat" [configArgs]
            finalArguments = $"/c \"\"{exePath}\" {configArgs}\"";
        }
        else
        {
            // ProjectZomboid64.exe [configArgs] (usually handles its own JVM args internally)
            finalArguments = configArgs;
        }

        ProcessStartInfo startInfo = new ProcessStartInfo
        {
            FileName = javaPath,
            Arguments = finalArguments,
            WorkingDirectory = workingDir,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        Process process = new Process { StartInfo = startInfo };
        process.EnableRaisingEvents = true;
        process.OutputDataReceived += (s, e) => { if (e.Data != null) CallDeferred(MethodName.HandleLog, profileName, e.Data, false); };
        process.ErrorDataReceived += (s, e) => { if (e.Data != null) CallDeferred(MethodName.HandleLog, profileName, e.Data, true); };
        process.Exited += (s, e) => CallDeferred(MethodName.HandleExit, profileName);

        _serverProcesses[profileName] = process;

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            EmitSignal(SignalName.StatusChanged, profileName, "Starting");
        }
        catch (Exception ex)
        {
            EmitSignal(SignalName.LogReceived, profileName, "Failed to start: " + ex.Message, true);
            EmitSignal(SignalName.StatusChanged, profileName, "Stopped");
        }
    }

    public void SendCommand(string profileName, string command)
    {
        if (IsRunning(profileName))
        {
            _serverProcesses[profileName].StandardInput.WriteLine(command);
        }
    }

    public void StopServer(string profileName)
    {
        if (IsRunning(profileName))
        {
            SendCommand(profileName, "quit"); // "quit" is the standard PZ command to stop
            EmitSignal(SignalName.StatusChanged, profileName, "Stopping");
        }
    }

    private void HandleLog(string profileName, string message, bool isError)
    {
        EmitSignal(SignalName.LogReceived, profileName, message, isError);

        if (message.Contains("Server started"))
        {
            EmitSignal(SignalName.StatusChanged, profileName, "Running");
        }

        // Parsing "players" command response
        // Project Zomboid outputs "Players connected (X):" followed by "- username" lines
        if (message.Contains("Players connected ("))
        {
            _awaitingPlayerList[profileName] = true;
            _pendingPlayerLists[profileName] = new List<string>();

            // Check if there are 0 players immediately
            if (message.Contains("(0)"))
            {
                EmitSignal(SignalName.PlayerListReceived, profileName, new string[0]);
                _awaitingPlayerList[profileName] = false;
            }
        }
        else if (_awaitingPlayerList.ContainsKey(profileName) && _awaitingPlayerList[profileName])
        {
            string trimmed = message.Trim();
            if (trimmed.StartsWith("- "))
            {
                string username = trimmed.Substring(2).Trim();
                _pendingPlayerLists[profileName].Add(username);
            }
            else if (string.IsNullOrEmpty(trimmed) || trimmed.Contains("Players connected"))
            {
                // We leave it open for now, but if we see another "Players connected" or a long time passes it might be done.
                // However, usually PZ sends a list then a blank line.
                if (_pendingPlayerLists[profileName].Count > 0)
                {
                    EmitSignal(SignalName.PlayerListReceived, profileName, _pendingPlayerLists[profileName].ToArray());
                    _awaitingPlayerList[profileName] = false;
                }
            }
            else
            {
                // If it's not a "- " line and we were awaiting, and it's some other server info, the list is likely done.
                EmitSignal(SignalName.PlayerListReceived, profileName, _pendingPlayerLists[profileName].ToArray());
                _awaitingPlayerList[profileName] = false;
            }
        }
    }

    private string FindExecutable(string profileName, string root, string[] names)
    {
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
        {
            return null;
        }

        Queue<string> queue = new Queue<string>();
        queue.Enqueue(root);

        while (queue.Count > 0)
        {
            string currentDir = queue.Dequeue();

            try
            {
                // Check files in current directory for any of the names
                foreach (string name in names)
                {
                    string[] files = Directory.GetFiles(currentDir, name);
                    if (files.Length > 0)
                    {
                        return files[0];
                    }
                }

                // Queue subdirectories
                foreach (string subDir in Directory.GetDirectories(currentDir))
                {
                    queue.Enqueue(subDir);
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Skip inaccessible directories silently
            }
            catch (Exception ex)
            {
                EmitSignal(SignalName.LogReceived, profileName, $"Error searching {currentDir}: {ex.Message}", true);
            }
        }

        return null;
    }

    private void HandleExit(string profileName)
    {
        EmitSignal(SignalName.StatusChanged, profileName, "Stopped");
        if (_serverProcesses.ContainsKey(profileName))
        {
            _serverProcesses[profileName].Dispose();
            _serverProcesses.Remove(profileName);
        }
    }
}
