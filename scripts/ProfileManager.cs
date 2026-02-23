using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Linq;

public static class ProfileManager
{
    private static readonly string RegistryPath = Path.Combine(OS.GetUserDataDir(), "registry.json");
    private const string LocalProfileName = ".ezpz_profile.json";

    public class ServerProfile
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public string Branch { get; set; } = "stable";
        public string MaxRam { get; set; } = "4G";
        public string MinRam { get; set; } = "2G";
        public string JvmFlags { get; set; } = "";
        public DateTime LastUsed { get; set; }
    }

    private class Registry
    {
        public List<string> ServerPaths { get; set; } = new List<string>();
    }

    public static List<ServerProfile> LoadProfiles()
    {
        if (!File.Exists(RegistryPath)) return new List<ServerProfile>();

        try
        {
            string json = File.ReadAllText(RegistryPath);
            var registry = JsonSerializer.Deserialize<Registry>(json);
            if (registry == null) return new List<ServerProfile>();

            var profiles = new List<ServerProfile>();
            var validPaths = new List<string>();

            foreach (var path in registry.ServerPaths)
            {
                if (!Directory.Exists(path)) continue;

                string profilePath = Path.Combine(path, LocalProfileName);
                if (File.Exists(profilePath))
                {
                    try
                    {
                        string profJson = File.ReadAllText(profilePath);
                        var p = JsonSerializer.Deserialize<ServerProfile>(profJson);
                        if (p != null)
                        {
                            p.Path = path;
                            profiles.Add(p);
                            validPaths.Add(path);
                        }
                    }
                    catch { }
                }
            }

            if (validPaths.Count != registry.ServerPaths.Count)
            {
                registry.ServerPaths = validPaths;
                SaveRegistry(registry);
            }

            return profiles.OrderByDescending(p => p.LastUsed).ToList();
        }
        catch (Exception e)
        {
            GD.PrintErr($"[ProfileManager] Failed to load profiles: {e.Message}");
            return new List<ServerProfile>();
        }
    }

    public static void SaveProfile(ServerProfile profile)
    {
        if (string.IsNullOrEmpty(profile.Path) || !Directory.Exists(profile.Path)) return;

        profile.LastUsed = DateTime.Now;

        try
        {
            string profJson = JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(Path.Combine(profile.Path, LocalProfileName), profJson);

            var registry = LoadRegistry();
            if (!registry.ServerPaths.Any(p => p.TrimEnd(Path.DirectorySeparatorChar) == profile.Path.TrimEnd(Path.DirectorySeparatorChar)))
            {
                registry.ServerPaths.Add(profile.Path);
                SaveRegistry(registry);
            }
        }
        catch (Exception e)
        {
            GD.PrintErr($"[ProfileManager] Failed to save profile {profile.Name}: {e.Message}");
        }
    }

    public static void DeleteProfile(ServerProfile profile, bool deleteFiles)
    {
        try
        {
            // Remove from registry first
            var registry = LoadRegistry();
            string normalizedPath = profile.Path.TrimEnd(Path.DirectorySeparatorChar);
            registry.ServerPaths.RemoveAll(p => p.TrimEnd(Path.DirectorySeparatorChar) == normalizedPath);
            SaveRegistry(registry);

            // Delete local profile file
            string profileFile = Path.Combine(profile.Path, LocalProfileName);
            if (File.Exists(profileFile)) File.Delete(profileFile);

            // Delete entire directory if requested
            if (deleteFiles && Directory.Exists(profile.Path))
            {
                Directory.Delete(profile.Path, true);
            }
        }
        catch (Exception e)
        {
            GD.PrintErr($"[ProfileManager] Failed to delete profile {profile.Name}: {e.Message}");
        }
    }

    public static ServerProfile CloneProfile(ServerProfile source, string newName, string newPath)
    {
        try
        {
            if (!Directory.Exists(newPath)) Directory.CreateDirectory(newPath);

            // Copy all files from source to destination (simple recursive copy)
            CopyDirectory(source.Path, newPath);

            var cloned = new ServerProfile
            {
                Name = newName,
                Path = newPath,
                Branch = source.Branch,
                MaxRam = source.MaxRam,
                MinRam = source.MinRam,
                JvmFlags = source.JvmFlags,
                LastUsed = DateTime.Now
            };

            // Update the local profile file in the new location
            string profJson = JsonSerializer.Serialize(cloned, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(Path.Combine(newPath, LocalProfileName), profJson);

            // Add to registry
            var registry = LoadRegistry();
            if (!registry.ServerPaths.Contains(newPath))
            {
                registry.ServerPaths.Add(newPath);
                SaveRegistry(registry);
            }

            return cloned;
        }
        catch (Exception e)
        {
            GD.PrintErr($"[ProfileManager] Failed to clone profile {source.Name}: {e.Message}");
            return null;
        }
    }

    private static void CopyDirectory(string sourceDir, string destDir)
    {
        string fullSource = Path.GetFullPath(sourceDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string fullDest = Path.GetFullPath(destDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (fullDest.StartsWith(fullSource, StringComparison.OrdinalIgnoreCase))
        {
            GD.PrintErr($"[ProfileManager] Aborting copy: Destination {fullDest} is inside source {fullSource}");
            return;
        }

        Directory.CreateDirectory(destDir);
        foreach (var file in Directory.GetFiles(sourceDir))
        {
            string fileName = Path.GetFileName(file);
            if (fileName == LocalProfileName) continue; // Skip the old profile metadata
            File.Copy(file, Path.Combine(destDir, fileName), true);
        }
        foreach (var subDir in Directory.GetDirectories(sourceDir))
        {
            // Skip SteamCMD or other system folders if they accidentally end up here?
            // Usually PZ server dir just has Zomboid folder and the exe
            CopyDirectory(subDir, Path.Combine(destDir, Path.GetFileName(subDir)));
        }
    }

    private static Registry LoadRegistry()
    {
        if (!File.Exists(RegistryPath)) return new Registry();
        try
        {
            string json = File.ReadAllText(RegistryPath);
            return JsonSerializer.Deserialize<Registry>(json) ?? new Registry();
        }
        catch { return new Registry(); }
    }

    private static void SaveRegistry(Registry reg)
    {
        try
        {
            string json = JsonSerializer.Serialize(reg, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(RegistryPath, json);
        }
        catch (Exception e)
        {
            GD.PrintErr($"[ProfileManager] Failed to save registry: {e.Message}");
        }
    }
}
