using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;

public partial class ModBrowser : Window
{
    private LineEdit _workshopInput;
    private LineEdit _modIdInput;
    private Button _addButton;
    private Tree _modList;
    private Button _closeButton;
    private ConfirmationDialog _deleteConfirm;
    private PopupMenu _actionMenu;

    private string _currentIniPath;
    private string _currentServerPath;
    private string _currentServerName;

    private string _pendingModId;
    private string _pendingWorkshopId;

    public override void _Ready()
    {
        _workshopInput = GetNode<LineEdit>("%WorkshopInput");
        _modIdInput = GetNode<LineEdit>("%ModIdInput");
        _addButton = GetNode<Button>("%AddButton");
        _modList = GetNode<Tree>("%ModList");
        _closeButton = GetNode<Button>("%CloseButton");
        _deleteConfirm = GetNode<ConfirmationDialog>("%DeleteConfirm");

        // Initialize Action Menu
        _actionMenu = new PopupMenu();
        AddChild(_actionMenu);
        _actionMenu.AddItem("⬆️ Move Up", 1);
        _actionMenu.AddItem("⬇️ Move Down", 2);
        _actionMenu.AddSeparator();
        _actionMenu.AddItem("🚫 Disable Mod (Config Only)", 0);
        _actionMenu.AddItem("🔥 Delete Mod Files (Full Wipe)", 3);
        _actionMenu.IdPressed += OnActionIdPressed;

        _modList.Columns = 4;
        _modList.SetColumnTitle(0, "Load Order / Mod ID");
        _modList.SetColumnTitle(1, "Workshop ID");
        _modList.SetColumnTitle(2, "Last Updated");
        _modList.SetColumnTitle(3, "Actions");

        _addButton.Pressed += OnAddPressed;
        _closeButton.Pressed += () => Hide();
        _modList.GuiInput += OnModListGuiInput;
        _deleteConfirm.Confirmed += OnDeleteConfirmed;
    }

    public void Open(string serverPath, string serverName)
    {
        _currentServerPath = serverPath;
        _currentServerName = serverName;
        _currentIniPath = ConfigManager.GetConfigPath(serverPath, serverName, ".ini");
        RefreshList();
        PopupCentered();
    }

    private void RefreshList()
    {
        _modList.Clear();
        var root = _modList.CreateItem();

        var settings = ConfigManager.ParseIni(_currentIniPath);
        string currentWorkshop = settings.ContainsKey("WorkshopItems") ? settings["WorkshopItems"].Value : "";
        string currentMods = settings.ContainsKey("Mods") ? settings["Mods"].Value : "";

        var workshopIds = currentWorkshop.Split(';', StringSplitOptions.RemoveEmptyEntries).ToList();
        var modIds = currentMods.Split(';', StringSplitOptions.RemoveEmptyEntries).ToList();

        // Pre-fetch all metadata to check dependencies
        var allInstalledMeta = new Dictionary<string, WorkshopManager.ModMetadata>();
        foreach (var wId in workshopIds)
        {
            var metaList = WorkshopManager.GetModMetadataFromWorkshop(_currentServerPath, wId);
            foreach (var meta in metaList)
            {
                if (!allInstalledMeta.ContainsKey(meta.ModId))
                    allInstalledMeta[meta.ModId] = meta;
            }
        }

        for (int i = 0; i < modIds.Count; i++)
        {
            var item = _modList.CreateItem(root);
            string mId = modIds[i];
            item.SetText(0, $"{i + 1}. {mId}");
            item.SetMetadata(0, mId);

            // Fetch metadata for this specific mod
            allInstalledMeta.TryGetValue(mId, out var meta);

            item.SetText(1, meta?.WorkshopId ?? "-");
            item.SetText(2, meta != null ? meta.LastUpdated.ToString("yyyy-MM-dd") : "Unknown");

            // Dependency Check
            string footerText = "";
            if (meta != null && meta.Dependencies.Count > 0)
            {
                var missing = meta.Dependencies.Where(d => !modIds.Contains(d)).ToList();
                if (missing.Count > 0)
                {
                    footerText = $"[MISSING DEPS]";
                    item.SetCustomColor(0, Colors.Orange);
                }
            }
            item.SetText(3, string.IsNullOrEmpty(footerText) ? "[Manage...]" : $"{footerText} [Manage...]");
            item.SetTextAlignment(3, HorizontalAlignment.Center);
        }
    }

    private async void OnAddPressed()
    {
        string input = _workshopInput.Text.Trim();
        string workshopId = ExtractWorkshopId(input);
        string manualModId = _modIdInput.Text.Trim();

        if (string.IsNullOrEmpty(workshopId)) return;

        _addButton.Disabled = true;
        _addButton.Text = "Scanning...";

        List<string> modIdsToUse = new List<string>();

        // 1. Try to scan local files first
        var detected = WorkshopManager.GetModIdsFromWorkshop(_currentServerPath, workshopId);
        if (detected.Count > 0)
        {
            modIdsToUse.AddRange(detected);
            GD.Print($"[ModBrowser] Detected {detected.Count} Mod IDs for Workshop {workshopId} from local files.");
        }
        else
        {
            // 2. Fallback: Scrape the workshop page
            GD.Print($"[ModBrowser] No local files for {workshopId}. Scraping Steam Workshop...");
            var scraped = await WorkshopManager.ScrapeModIdsFromWorkshopAsync(workshopId);
            if (scraped.Count > 0)
            {
                modIdsToUse.AddRange(scraped);
                GD.Print($"[ModBrowser] Scraped {scraped.Count} Mod IDs for Workshop {workshopId}");
            }
        }

        // 3. Last fallback: manual input or risky guess
        if (modIdsToUse.Count == 0)
        {
            if (!string.IsNullOrEmpty(manualModId))
            {
                modIdsToUse.Add(manualModId);
            }
            else
            {
                GD.PushWarning($"[ModBrowser] Could not detect Mod ID for Workshop {workshopId}. Falling back to Workshop ID (Risky).");
                modIdsToUse.Add(workshopId);
            }
        }

        WorkshopManager.AddMod(_currentIniPath, workshopId, modIdsToUse);
        _workshopInput.Clear();
        _modIdInput.Clear();
        _addButton.Disabled = false;
        _addButton.Text = "Add Mod";
        RefreshList();
    }

    private void OnModListGuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mb && mb.Pressed)
        {
            var item = _modList.GetItemAtPosition(mb.Position);
            if (item == null) return;

            int col = _modList.GetColumnAtPosition(mb.Position);

            // Show menu on Right Click ANYWHERE, or Left Click on Actions column (3)
            if (mb.ButtonIndex == MouseButton.Right || (mb.ButtonIndex == MouseButton.Left && col == 3))
            {
                _pendingModId = (string)item.GetMetadata(0);
                _pendingWorkshopId = item.GetText(1) == "-" ? null : item.GetText(1);

                // Position the menu at the global mouse position
                _actionMenu.Position = (Vector2I)(GetWindow().Position + (Vector2I)GetViewport().GetMousePosition());
                _actionMenu.Popup();
            }
        }
    }

    private void OnActionIdPressed(long id)
    {
        if (id == 0) // Soft Remove (Disable)
        {
            WorkshopManager.RemoveMod(_currentIniPath, _pendingWorkshopId, _pendingModId, _currentServerPath, false);
            RefreshList();
        }
        else if (id == 3) // Hard Remove (Delete)
        {
            _deleteConfirm.PopupCentered();
        }
        else if (id == 1) // Up
        {
            WorkshopManager.MoveMod(_currentIniPath, _pendingModId, true);
            RefreshList();
        }
        else if (id == 2) // Down
        {
            WorkshopManager.MoveMod(_currentIniPath, _pendingModId, false);
            RefreshList();
        }
    }

    private void OnDeleteConfirmed()
    {
        WorkshopManager.RemoveMod(_currentIniPath, _pendingWorkshopId, _pendingModId, _currentServerPath, true);
        _pendingModId = null;
        _pendingWorkshopId = null;
        RefreshList();
    }

    private string ExtractWorkshopId(string input)
    {
        if (string.IsNullOrEmpty(input)) return "";
        if (long.TryParse(input, out _)) return input;

        // Try URL parse
        var match = System.Text.RegularExpressions.Regex.Match(input, @"id=(\d+)");
        return match.Success ? match.Groups[1].Value : "";
    }
}
