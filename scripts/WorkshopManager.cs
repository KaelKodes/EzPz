using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Text.RegularExpressions;

public static class WorkshopManager
{
    public class ModMetadata
    {
        public string ModId { get; set; }
        public string WorkshopId { get; set; }
        public string Name { get; set; }
        public List<string> Dependencies { get; set; } = new List<string>();
        public DateTime LastUpdated { get; set; }
    }

    public static List<ModMetadata> GetModMetadataFromWorkshop(string serverPath, string workshopId)
    {
        var metadataList = new List<ModMetadata>();
        string workshopPath = Path.Combine(serverPath, "steamapps", "workshop", "content", "108600", workshopId);

        if (!Directory.Exists(workshopPath)) return metadataList;

        var lastUpdated = Directory.GetLastWriteTime(workshopPath);
        var modInfoFiles = Directory.GetFiles(workshopPath, "mod.info", SearchOption.AllDirectories);

        foreach (var file in modInfoFiles)
        {
            try
            {
                var meta = new ModMetadata { WorkshopId = workshopId, LastUpdated = lastUpdated };
                var lines = File.ReadAllLines(file);
                foreach (var line in lines)
                {
                    string trim = line.Trim();
                    if (trim.StartsWith("id=")) meta.ModId = trim.Split('=')[1].Trim();
                    if (trim.StartsWith("name=")) meta.Name = trim.Split('=')[1].Trim();
                    if (trim.StartsWith("require="))
                    {
                        var deps = trim.Split('=')[1].Split(',', StringSplitOptions.RemoveEmptyEntries);
                        foreach (var d in deps) meta.Dependencies.Add(d.Trim());
                    }
                }
                if (!string.IsNullOrEmpty(meta.ModId)) metadataList.Add(meta);
            }
            catch { }
        }
        return metadataList;
    }

    public static List<string> GetModIdsFromWorkshop(string serverPath, string workshopId)
    {
        return GetModMetadataFromWorkshop(serverPath, workshopId).Select(m => m.ModId).ToList();
    }

    public static async Task<List<string>> ScrapeModIdsFromWorkshopAsync(string workshopId)
    {
        var modIds = new List<string>();
        string url = $"https://steamcommunity.com/sharedfiles/filedetails/?id={workshopId}";

        try
        {
            using (var client = new System.Net.Http.HttpClient())
            {
                // Steam might block default user agent
                client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");
                string html = await client.GetStringAsync(url);

                // PZ Mod IDs are usually listed as "Mod ID: <id>" or "ModID: <id>"
                // We use a regex that looks for those patterns in the description
                var matches = Regex.Matches(html, @"Mod\s*ID:\s*([^\s<]+)", RegexOptions.IgnoreCase);
                foreach (Match match in matches)
                {
                    string id = match.Groups[1].Value.Trim();
                    // Remove trailing punctuation common in descriptions
                    id = id.TrimEnd('.', ',', ';', '!');
                    if (!modIds.Contains(id)) modIds.Add(id);
                }
            }
        }
        catch (Exception e)
        {
            GD.PushError($"[WorkshopManager] Failed to scrape workshop page {url}: {e.Message}");
        }

        return modIds;
    }

    public static void AddMod(string iniPath, string workshopId, List<string> modIds)
    {
        var settings = ConfigManager.ParseIni(iniPath);

        string currentWorkshop = settings.ContainsKey("WorkshopItems") ? settings["WorkshopItems"].Value : "";
        string currentMods = settings.ContainsKey("Mods") ? settings["Mods"].Value : "";

        var workshopList = currentWorkshop.Split(';', StringSplitOptions.RemoveEmptyEntries)
                                     .Where(id => long.TryParse(id.Trim(), out _)) // Ensure only numbers
                                     .ToList();

        var modList = currentMods.Split(';', StringSplitOptions.RemoveEmptyEntries).ToList();

        if (!workshopList.Contains(workshopId) && long.TryParse(workshopId, out _))
            workshopList.Add(workshopId);

        foreach (var modId in modIds)
        {
            string formattedId = modId.StartsWith("\\") ? modId : "\\" + modId;
            if (!modList.Contains(formattedId)) modList.Add(formattedId);
        }

        var saveDict = settings.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Value);
        saveDict["WorkshopItems"] = string.Join(";", workshopList);
        saveDict["Mods"] = string.Join(";", modList);

        ConfigManager.SaveIni(iniPath, saveDict);
    }

    public static void MoveMod(string iniPath, string modId, bool up)
    {
        var settings = ConfigManager.ParseIni(iniPath);
        if (!settings.ContainsKey("Mods")) return;

        var modList = settings["Mods"].Value.Split(';', StringSplitOptions.RemoveEmptyEntries).ToList();
        int index = modList.IndexOf(modId);
        if (index == -1) return;

        int target = up ? index - 1 : index + 1;
        if (target >= 0 && target < modList.Count)
        {
            string temp = modList[index];
            modList[index] = modList[target];
            modList[target] = temp;

            var saveDict = settings.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Value);
            saveDict["Mods"] = string.Join(";", modList);
            ConfigManager.SaveIni(iniPath, saveDict);
        }
    }

    public static void RemoveMod(string iniPath, string workshopId, string modId, string serverPath, bool deleteFiles = false)
    {
        var settings = ConfigManager.ParseIni(iniPath);

        string currentWorkshop = settings.ContainsKey("WorkshopItems") ? settings["WorkshopItems"].Value : "";
        string currentMods = settings.ContainsKey("Mods") ? settings["Mods"].Value : "";

        var workshopList = currentWorkshop.Split(';', StringSplitOptions.RemoveEmptyEntries)
                                     .Where(id => long.TryParse(id.Trim(), out _))
                                     .ToList();

        var modList = currentMods.Split(';', StringSplitOptions.RemoveEmptyEntries).ToList();

        if (modId != null) modList.Remove(modId);

        if (!string.IsNullOrEmpty(workshopId))
        {
            var workshopModIds = GetModIdsFromWorkshop(serverPath, workshopId);

            if (deleteFiles)
            {
                // Remove EVERYTHING related to this workshop ID from the config
                modList.RemoveAll(m => workshopModIds.Contains(m));
                if (modId != null) modList.Remove(modId); // Just in case it wasn't in the scan
                workshopList.Remove(workshopId);

                string workshopPath = Path.Combine(serverPath, "steamapps", "workshop", "content", "108600", workshopId);
                try
                {
                    if (Directory.Exists(workshopPath))
                    {
                        Directory.Delete(workshopPath, true);
                        GD.Print($"[WorkshopManager] Deleted all mod files and config entries for Workshop ID {workshopId}");
                    }
                }
                catch (Exception e)
                {
                    GD.PushError($"[WorkshopManager] Failed to delete mod files at {workshopPath}: {e.Message}");
                }
            }
            else
            {
                // Soft remove only the specific modId
                if (modId != null) modList.Remove(modId);

                // Only remove the workshop entry if NO other mods from this workshop are in the load order
                bool stillHasMods = modList.Any(m => workshopModIds.Contains(m));
                if (!stillHasMods)
                {
                    workshopList.Remove(workshopId);
                }
            }
        }

        var saveDict = settings.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Value);
        saveDict["WorkshopItems"] = string.Join(";", workshopList);
        saveDict["Mods"] = string.Join(";", modList);

        ConfigManager.SaveIni(iniPath, saveDict);
    }
}
