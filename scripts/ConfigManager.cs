using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

public static class ConfigManager
{
    public static string GetConfigPath(string serverPath, string profileName, string extension)
    {
        // 1. Check for standard 'Server' folder directly in the server path (User's preferred "base" location)
        string directPath = Path.Combine(serverPath, "Server", $"{profileName}{extension}");
        if (File.Exists(directPath)) return directPath;

        // 2. Check for 'Zomboid/Server' folder in local server path (Isolated behavior)
        string zomboidPath = Path.Combine(serverPath, "Zomboid", "Server", $"{profileName}{extension}");
        if (File.Exists(zomboidPath)) return zomboidPath;

        // 3. Check default UserProfile folder (C:\Users\USER\Zomboid\Server)
        string userPath = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile), "Zomboid", "Server", $"{profileName}{extension}");
        if (File.Exists(userPath)) return userPath;

        // Default to the direct path if none exist
        return directPath;
    }

    public static Dictionary<string, (string Value, string Description)> ParseIni(string path)
    {
        var settings = new Dictionary<string, (string Value, string Description)>();
        if (!File.Exists(path)) return settings;

        var lines = File.ReadAllLines(path);
        string currentDescription = "";

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("#") || trimmed.StartsWith(";"))
            {
                currentDescription += trimmed.TrimStart('#', ';', ' ').Trim() + "\n";
                continue;
            }

            if (string.IsNullOrEmpty(trimmed))
            {
                currentDescription = ""; // Reset description on empty line
                continue;
            }

            if (trimmed.Contains("="))
            {
                var parts = trimmed.Split('=', 2);
                string key = parts[0].Trim();
                string value = parts[1].Trim();
                settings[key] = (value, currentDescription.Trim());
                currentDescription = ""; // Reset for next setting
            }
        }
        return settings;
    }

    public static void SaveIni(string path, Dictionary<string, string> settings)
    {
        // To preserve comments, we should read the file and replace values
        if (File.Exists(path))
        {
            var lines = File.ReadAllLines(path).ToList();
            var settingsToSave = new Dictionary<string, string>(settings);

            for (int i = 0; i < lines.Count; i++)
            {
                var trimmed = lines[i].Trim();
                if (trimmed.StartsWith("#") || trimmed.StartsWith(";") || string.IsNullOrEmpty(trimmed) || !trimmed.Contains("=")) continue;

                var keyInFile = trimmed.Split('=')[0].Trim();
                // Find a match in our settings using case-insensitive search
                var matchingKey = settingsToSave.Keys.FirstOrDefault(k => string.Equals(k, keyInFile, StringComparison.OrdinalIgnoreCase));

                if (matchingKey != null)
                {
                    lines[i] = $"{keyInFile}={settingsToSave[matchingKey]}";
                    settingsToSave.Remove(matchingKey); // Mark as processed
                }
            }

            // Append any new settings that weren't in the original file
            foreach (var kvp in settingsToSave)
            {
                lines.Add($"{kvp.Key}={kvp.Value}");
            }

            File.WriteAllLines(path, lines);
        }
        else
        {
            var lines = settings.Select(kvp => $"{kvp.Key}={kvp.Value}");
            File.WriteAllLines(path, lines);
        }
    }

    // Simplified Lua table parser/writer for SandboxVars
    public static Dictionary<string, (string Value, string Description)> ParseSandboxLua(string path)
    {
        var settings = new Dictionary<string, (string Value, string Description)>();
        if (!File.Exists(path)) return settings;

        var lines = File.ReadAllLines(path);
        string currentDescription = "";
        Stack<string> context = new Stack<string>();

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("--"))
            {
                currentDescription += trimmed.TrimStart('-', ' ').Trim() + "\n";
                continue;
            }

            if (string.IsNullOrEmpty(trimmed))
            {
                currentDescription = "";
                continue;
            }

            // Handle table start: key = {
            var tableMatch = Regex.Match(trimmed, @"^(\w+)\s*=\s*\{\s*$");
            if (tableMatch.Success)
            {
                context.Push(tableMatch.Groups[1].Value);
                continue;
            }

            // Handle table end: },
            if (trimmed.StartsWith("}") && context.Count > 0)
            {
                context.Pop();
                continue;
            }

            // Look for pattern: key = value,
            var match = Regex.Match(trimmed, @"^(\w+)\s*=\s*(.+?)\s*,?$");
            if (match.Success)
            {
                string key = match.Groups[1].Value;
                string value = match.Groups[2].Value;

                if (value == "{")
                {
                    context.Push(key);
                    continue;
                }

                // Construct flattened key: Parent.Child
                // We skip "SandboxVars" if it's the root to keep names short
                var stackList = context.Reverse().ToList();
                if (stackList.Count > 0 && stackList[0] == "SandboxVars") stackList.RemoveAt(0);

                string fullKey = stackList.Count > 0 ? string.Join(".", stackList) + "." + key : key;

                settings[fullKey] = (value, currentDescription.Trim());
                currentDescription = "";
            }
        }
        return settings;
    }

    public static void SaveSandboxLua(string path, Dictionary<string, string> settings)
    {
        if (!File.Exists(path)) return;

        var lines = File.ReadAllLines(path).ToList();
        Stack<string> context = new Stack<string>();

        for (int i = 0; i < lines.Count; i++)
        {
            string line = lines[i];
            string trimmed = line.Trim();

            // Track context
            var tableMatch = Regex.Match(trimmed, @"^(\w+)\s*=\s*\{\s*$");
            if (tableMatch.Success)
            {
                context.Push(tableMatch.Groups[1].Value);
                continue;
            }
            if (trimmed.StartsWith("}") && context.Count > 0)
            {
                context.Pop();
                continue;
            }

            var match = Regex.Match(trimmed, @"^(\w+)\s*=\s*(.+?)(\s*,?)$");
            if (match.Success)
            {
                string key = match.Groups[1].Value;
                string suffix = match.Groups[3].Value;

                var stackList = context.Reverse().ToList();
                if (stackList.Count > 0 && stackList[0] == "SandboxVars") stackList.RemoveAt(0);
                string fullKey = stackList.Count > 0 ? string.Join(".", stackList) + "." + key : key;

                if (settings.ContainsKey(fullKey))
                {
                    // Better indentation: use everything before the first non-whitespace character
                    int firstChar = line.IndexOf(trimmed[0]);
                    string indent = firstChar >= 0 ? line.Substring(0, firstChar) : "";

                    // Sanitize value: Trim and ensure no illegal characters like $ for this context
                    string newVal = settings[fullKey].Trim().Replace("$", "");

                    lines[i] = $"{indent}{key} = {newVal}{suffix}";
                    GD.Print($"[ConfigManager] Updated SandboxVar {fullKey} to {newVal}");
                }
            }
        }
        File.WriteAllLines(path, lines);
    }
}
