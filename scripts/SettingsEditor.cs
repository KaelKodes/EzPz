using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

public partial class SettingsEditor : Window
{
    private VBoxContainer _detailsVBox;
    private VBoxContainer _serverVBox;
    private VBoxContainer _sandboxVBox;
    private VBoxContainer _launchVBox;
    private LineEdit _sandboxSearch;
    private LineEdit _serverSearch;
    private Button _saveButton;
    private Button _cancelButton;

    private string _currentIniPath;
    private string _currentSandboxPath;
    private ProfileManager.ServerProfile _currentProfile;

    private Dictionary<string, LineEdit> _detailsControls = new Dictionary<string, LineEdit>();
    private Dictionary<string, LineEdit> _serverControls = new Dictionary<string, LineEdit>();
    private Dictionary<string, LineEdit> _sandboxControls = new Dictionary<string, LineEdit>();
    private Dictionary<string, LineEdit> _profileControls = new Dictionary<string, LineEdit>();
    private List<HBoxContainer> _sandboxRows = new List<HBoxContainer>();
    private List<HBoxContainer> _serverRows = new List<HBoxContainer>();
    private Label _previewLabel;

    public override void _Ready()
    {
        _detailsVBox = GetNode<VBoxContainer>("%DetailsVBox");
        _serverVBox = GetNode<VBoxContainer>("%ServerVBox");
        _sandboxVBox = GetNode<VBoxContainer>("%SandboxVBox");
        _launchVBox = GetNode<VBoxContainer>("%LaunchVBox");
        _sandboxSearch = GetNode<LineEdit>("%SandboxSearch");
        _serverSearch = GetNode<LineEdit>("%ServerSearch");
        _saveButton = GetNode<Button>("%SaveButton");
        _cancelButton = GetNode<Button>("%CancelButton");

        _saveButton.Pressed += OnSavePressed;
        _cancelButton.Pressed += () => Hide();
        _sandboxSearch.TextChanged += OnSandboxSearchChanged;
        _serverSearch.TextChanged += OnServerSearchChanged;
    }

    public void Open(ProfileManager.ServerProfile profile)
    {
        _currentProfile = profile;
        _currentIniPath = ConfigManager.GetConfigPath(profile.Path, profile.Name, ".ini");
        _currentSandboxPath = ConfigManager.GetConfigPath(profile.Path, profile.Name, "_SandboxVars.lua");

        _sandboxSearch.Clear();
        _serverSearch.Clear();
        LoadSettings();
        PopupCentered();
    }

    private void LoadSettings()
    {
        ClearVBox(_detailsVBox, _detailsControls);
        ClearVBox(_serverVBox, _serverControls, _serverRows);
        ClearVBox(_sandboxVBox, _sandboxControls, _sandboxRows);
        ClearVBox(_launchVBox, _profileControls);

        var iniSettings = ConfigManager.ParseIni(_currentIniPath);

        // --- Details Tab (Identification & Mod Overviews) ---
        AddSettingRow(_detailsVBox, null, null, "Profile Name", _currentProfile.Name, false, "The internal name of this server profile.");
        AddSettingRow(_detailsVBox, null, null, "Server Path", _currentProfile.Path, false, "The directory where the server files are located.");

        if (iniSettings.ContainsKey("WorkshopItems"))
            AddSettingRow(_detailsVBox, null, null, "Workshop Items", iniSettings["WorkshopItems"].Value, false, "List of Workshop IDs (Managed via Mod Browser).");

        if (iniSettings.ContainsKey("Mods"))
        {
            AddSettingRow(_detailsVBox, null, null, "Mods", iniSettings["Mods"].Value, false, "List of Mod IDs (Managed via Mod Browser).");
        }

        // --- Server Tab (Full Editable INI) ---
        foreach (var kvp in iniSettings)
        {
            // Skip Mod lists in the main server list as they are multi-line/complex and managed elsewhere
            if (kvp.Key == "WorkshopItems" || kvp.Key == "Mods") continue;

            AddSettingRow(_serverVBox, _serverControls, _serverRows, kvp.Key, kvp.Value.Value, true, kvp.Value.Description);
        }

        if (File.Exists(_currentSandboxPath))
        {
            var sandboxSettings = ConfigManager.ParseSandboxLua(_currentSandboxPath);
            foreach (var kvp in sandboxSettings)
            {
                AddSettingRow(_sandboxVBox, _sandboxControls, _sandboxRows, kvp.Key, kvp.Value.Value, true, kvp.Value.Description);
            }
        }
        else
        {
            var warning = new Label
            {
                Text = "Sandbox vars file not found.\nStart Server once to generate file.",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                SizeFlagsVertical = Control.SizeFlags.ExpandFill
            };
            warning.AddThemeColorOverride("font_color", Colors.Orange);
            _sandboxVBox.AddChild(warning);
        }

        AddSettingRow(_launchVBox, _profileControls, null, "Min RAM (Xms)", _currentProfile.MinRam, true, "Minimum RAM (Xms) assigned to the server.");
        AddSettingRow(_launchVBox, _profileControls, null, "Max RAM (Xmx)", _currentProfile.MaxRam, true, "Maximum RAM (Xmx) assigned to the server.");

        string cleanFlags = StripRamFlags(_currentProfile.JvmFlags);
        AddSettingRow(_launchVBox, _profileControls, null, "Extra JVM Flags", cleanFlags, true, "Additional Java Virtual Machine flags for advanced users.");

        // Add Preview Row
        var previewHBox = new HBoxContainer();
        var previewTitle = new Label { Text = "Resulting Flags", CustomMinimumSize = new Vector2(200, 0) };
        _previewLabel = new Label { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _previewLabel.AddThemeColorOverride("font_color", Colors.DarkGray);
        previewHBox.AddChild(previewTitle);
        previewHBox.AddChild(_previewLabel);
        _launchVBox.AddChild(previewHBox);

        UpdatePreview();
    }

    private void OnSandboxSearchChanged(string text)
    {
        ApplyFilter(_sandboxRows, text);
    }

    private void OnServerSearchChanged(string text)
    {
        ApplyFilter(_serverRows, text);
    }

    private void ApplyFilter(List<HBoxContainer> rows, string text)
    {
        string query = text.ToLower().Trim();
        foreach (var row in rows)
        {
            var label = row.GetChild<Label>(0);
            bool match = string.IsNullOrEmpty(query) || label.Text.ToLower().Contains(query) || label.TooltipText.ToLower().Contains(query);
            row.Visible = match;
        }
    }

    private string StripRamFlags(string flags)
    {
        if (string.IsNullOrEmpty(flags)) return "";
        var parts = flags.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                         .Where(p => !p.StartsWith("-Xmx", StringComparison.OrdinalIgnoreCase) &&
                                     !p.StartsWith("-Xms", StringComparison.OrdinalIgnoreCase));
        return string.Join(" ", parts);
    }

    private void UpdatePreview()
    {
        if (_previewLabel == null) return;

        string min = _profileControls.ContainsKey("Min RAM (Xms)") ? _profileControls["Min RAM (Xms)"].Text : _currentProfile.MinRam;
        string max = _profileControls.ContainsKey("Max RAM (Xmx)") ? _profileControls["Max RAM (Xmx)"].Text : _currentProfile.MaxRam;
        string extra = _profileControls.ContainsKey("Extra JVM Flags") ? _profileControls["Extra JVM Flags"].Text : "";

        _previewLabel.Text = $"-Xms{min} -Xmx{max} {extra}".Trim();
    }

    private void ClearVBox(VBoxContainer vbox, Dictionary<string, LineEdit> controls, List<HBoxContainer> rows = null)
    {
        if (vbox == null) return;
        foreach (Node child in vbox.GetChildren()) child.QueueFree();
        if (controls != null) controls.Clear();
        if (rows != null) rows.Clear();
    }

    private void AddSettingRow(VBoxContainer vbox, Dictionary<string, LineEdit> controls, List<HBoxContainer> rows, string key, string value, bool editable, string description = "")
    {
        var hbox = new HBoxContainer();
        var label = new Label
        {
            Text = key,
            CustomMinimumSize = new Vector2(200, 0),
            TooltipText = description,
            MouseFilter = Control.MouseFilterEnum.Pass // Ensure tooltips show on hover
        };
        var input = new LineEdit
        {
            Text = value,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            Editable = editable,
            FocusMode = editable ? Control.FocusModeEnum.All : Control.FocusModeEnum.None,
            TooltipText = description
        };
        input.TextChanged += (text) => UpdatePreview();

        hbox.AddChild(label);
        hbox.AddChild(input);
        vbox.AddChild(hbox);

        if (rows != null) rows.Add(hbox);
        if (controls != null && editable) controls[key] = input;
    }

    private void OnSavePressed()
    {
        var iniSettings = _serverControls.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Text);

        // Ensure we preserve the mod lists which weren't in the editable list
        var existingIni = ConfigManager.ParseIni(_currentIniPath);
        if (existingIni.ContainsKey("WorkshopItems")) iniSettings["WorkshopItems"] = existingIni["WorkshopItems"].Value;
        if (existingIni.ContainsKey("Mods")) iniSettings["Mods"] = existingIni["Mods"].Value;

        ConfigManager.SaveIni(_currentIniPath, iniSettings);

        var sandboxSettings = _sandboxControls.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Text);
        ConfigManager.SaveSandboxLua(_currentSandboxPath, sandboxSettings);

        if (_profileControls.ContainsKey("Min RAM (Xms)"))
            _currentProfile.MinRam = _profileControls["Min RAM (Xms)"].Text;
        if (_profileControls.ContainsKey("Max RAM (Xmx)"))
            _currentProfile.MaxRam = _profileControls["Max RAM (Xmx)"].Text;
        if (_profileControls.ContainsKey("Extra JVM Flags"))
            _currentProfile.JvmFlags = _profileControls["Extra JVM Flags"].Text;

        ProfileManager.SaveProfile(_currentProfile);

        Hide();
    }
}
