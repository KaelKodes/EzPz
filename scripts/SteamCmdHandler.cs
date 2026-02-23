using Godot;
using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading.Tasks;
using System.Diagnostics;

public partial class SteamCmdHandler : Node
{
    private static readonly string SteamCmdUrl = "https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip";
    private static readonly string SteamCmdDir = Path.Combine(OS.GetUserDataDir(), "steamcmd");
    private static readonly string SteamCmdExe = Path.Combine(SteamCmdDir, "steamcmd.exe");

    [Signal] public delegate void ProgressUpdatedEventHandler(string status, float progress);
    [Signal] public delegate void OperationFinishedEventHandler(bool success);
    [Signal] public delegate void LogReceivedEventHandler(string message, bool isError);

    public async Task<bool> EnsureSteamCmd()
    {
        if (File.Exists(SteamCmdExe)) return true;

        if (!Directory.Exists(SteamCmdDir)) Directory.CreateDirectory(SteamCmdDir);

        EmitSignal(SignalName.ProgressUpdated, "Downloading SteamCMD...", 0.1f);

        try
        {
            using (var client = new System.Net.Http.HttpClient())
            {
                var response = await client.GetAsync(SteamCmdUrl);
                response.EnsureSuccessStatusCode();
                var zipPath = Path.Combine(SteamCmdDir, "steamcmd.zip");
                using (var fs = new FileStream(zipPath, FileMode.Create))
                {
                    await response.Content.CopyToAsync(fs);
                }

                EmitSignal(SignalName.ProgressUpdated, "Extracting SteamCMD...", 0.5f);
                await Task.Run(() => ZipFile.ExtractToDirectory(zipPath, SteamCmdDir, true));
                File.Delete(zipPath);
            }
            return true;
        }
        catch (Exception e)
        {
            GD.PrintErr($"Failed to setup SteamCMD: {e.Message}");
            return false;
        }
    }

    public async Task InstallOrUpdateServer(string installDir, string branch = "")
    {
        await EnsureSteamCmd();

        string arguments = $"+force_install_dir \"{installDir}\" +login anonymous +app_update 380870";
        if (!string.IsNullOrEmpty(branch))
        {
            arguments += $" -beta {branch}";
        }
        arguments += " validate +quit";

        EmitSignal(SignalName.ProgressUpdated, $"Starting SteamCMD for {branch ?? "stable"} build...", 0.1f);

        await RunSteamCmd(arguments);
        CallDeferred(MethodName.EmitSignal, SignalName.OperationFinished, true);
    }

    public async Task<System.Collections.Generic.Dictionary<string, string>> GetAvailableBranches()
    {
        CallDeferred(MethodName.EmitSignal, SignalName.LogReceived, "- DOWNLOADING BUILD LIST", false);
        await EnsureSteamCmd();
        string arguments = "+login anonymous +app_info_print 380870 +quit";
        string output = await RunSteamCmd(arguments, true);

        var branchData = new System.Collections.Generic.Dictionary<string, string>();
        branchData["public"] = "Stable build";

        // Brace-depth aware parsing for "branches" section
        int depth = 0;
        bool branchesFound = false;
        string currentBranchName = "";
        string currentDescription = "";
        string currentBuildId = "";

        var lines = output.Split('\n');
        foreach (var line in lines)
        {
            string trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;

            if (!branchesFound)
            {
                if (trimmed.Equals("\"branches\"", StringComparison.OrdinalIgnoreCase))
                {
                    branchesFound = true;
                }
                continue;
            }

            if (trimmed == "{")
            {
                depth++;
                continue;
            }
            if (trimmed == "}")
            {
                if (depth == 2 && !string.IsNullOrEmpty(currentBranchName))
                {
                    string info = currentBranchName;
                    if (!string.IsNullOrEmpty(currentDescription) && !currentDescription.Equals(currentBranchName, StringComparison.OrdinalIgnoreCase))
                    {
                        info = $"{currentBranchName} - {currentDescription}";
                    }

                    if (!string.IsNullOrEmpty(currentBuildId))
                    {
                        info += $" [Build {currentBuildId}]";
                    }

                    branchData[currentBranchName] = info;

                    currentBranchName = "";
                    currentDescription = "";
                    currentBuildId = "";
                }
                depth--;
                if (depth == 0) break; // Finished "branches" block
                continue;
            }

            // We are looking for keys at depth 1 (the branch names)
            if (depth == 1)
            {
                var match = System.Text.RegularExpressions.Regex.Match(trimmed, "^\"([^\"]+)\"$");
                if (match.Success)
                {
                    currentBranchName = match.Groups[1].Value;
                }
            }
            // We are looking for values at depth 2 (description, buildid)
            else if (depth == 2)
            {
                var descMatch = System.Text.RegularExpressions.Regex.Match(trimmed, "^\"description\"\\s+\"([^\"]+)\"$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (descMatch.Success)
                {
                    currentDescription = descMatch.Groups[1].Value;
                }
                var buildMatch = System.Text.RegularExpressions.Regex.Match(trimmed, "^\"buildid\"\\s+\"([^\"]+)\"$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (buildMatch.Success)
                {
                    currentBuildId = buildMatch.Groups[1].Value;
                }
            }
        }

        return branchData;
    }

    private async Task<string> RunSteamCmd(string arguments, bool captureOutput = false)
    {
        string fullOutput = "";
        await Task.Run(() =>
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = SteamCmdExe,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using (var process = new Process { StartInfo = startInfo })
            {
                process.OutputDataReceived += (s, e) =>
                {
                    if (e.Data != null)
                    {
                        if (captureOutput) fullOutput += e.Data + "\n";

                        // Pipe to app console
                        CallDeferred(MethodName.EmitSignal, SignalName.LogReceived, e.Data, false);

                        // Parse progress: [ 30%] or "progress: 12.34"
                        var pctMatch = System.Text.RegularExpressions.Regex.Match(e.Data, @"\[\s*(\d+)%\]");
                        var valMatch = System.Text.RegularExpressions.Regex.Match(e.Data, @"progress:\s*(\d+\.\d+)");

                        if (pctMatch.Success)
                        {
                            float p = float.Parse(pctMatch.Groups[1].Value) / 100f;
                            CallDeferred(MethodName.EmitSignal, SignalName.ProgressUpdated, e.Data.Trim(), p);
                        }
                        else if (valMatch.Success)
                        {
                            float p = float.Parse(valMatch.Groups[1].Value) / 100f;
                            CallDeferred(MethodName.EmitSignal, SignalName.ProgressUpdated, e.Data.Trim(), p);
                        }
                        else if (e.Data.Contains("Checking for available updates") || e.Data.Contains("Verifying installation"))
                        {
                            CallDeferred(MethodName.EmitSignal, SignalName.ProgressUpdated, e.Data.Trim(), 0.1f);
                        }
                    }
                };
                process.ErrorDataReceived += (s, e) =>
                {
                    if (e.Data != null)
                    {
                        CallDeferred(MethodName.EmitSignal, SignalName.LogReceived, e.Data, true);
                    }
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                process.WaitForExit();
            }
        });
        return fullOutput;
    }
}
