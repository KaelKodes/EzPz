using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

public partial class MainDashboard : Control
{
    private RichTextLabel _consoleOutput;
    private LineEdit _commandInput;
    private Button _startStopButton;
    private Label _statusLabel;
    private ItemList _serverList;
    private Button _newServerButton;
    private Button _cloneButton;
    private Button _deleteButton;

    private Button _settingsButton;
    private Button _modsButton;
    private ModBrowser _modBrowser;
    private SettingsEditor _settingsEditor;
    private ServerSetupUI _serverSetupUI;
    private PlayerManager _playerManager;
    private Button _playersButton;
    private UpdateManager _updateManager;
    private Button _updateServerButton;

    private ServerManager _serverManager;
    private SteamCmdHandler _steamCmdHandler;
    private List<ProfileManager.ServerProfile> _profiles = new List<ProfileManager.ServerProfile>();
    private ProfileManager.ServerProfile _currentProfile;

    public override void _Ready()
    {
        _consoleOutput = GetNode<RichTextLabel>("%ConsoleOutput");
        _consoleOutput.SelectionEnabled = true;
        _commandInput = GetNode<LineEdit>("%CommandInput");
        _startStopButton = GetNode<Button>("%StartStopButton");
        _statusLabel = GetNode<Label>("%StatusLabel");
        _serverList = GetNode<ItemList>("%ServerList");
        _newServerButton = GetNode<Button>("%NewServerButton");

        _settingsButton = GetNode<Button>("%SettingsButton");
        _modsButton = GetNode<Button>("%ModsButton");
        _modBrowser = GetNode<ModBrowser>("%ModBrowser");
        _settingsEditor = GetNode<SettingsEditor>("%SettingsEditor");
        _serverSetupUI = GetNode<ServerSetupUI>("%ServerSetupUI");
        _playerManager = GetNode<PlayerManager>("%PlayerManager");
        _playersButton = GetNode<Button>("%PlayersButton");
        _updateManager = GetNode<UpdateManager>("%UpdateManager");
        _updateServerButton = GetNode<Button>("%UpdateServerButton");
        _cloneButton = GetNode<Button>("%CloneButton");
        _deleteButton = GetNode<Button>("%DeleteButton");

        _serverManager = GetNode<ServerManager>("/root/ServerManager");
        _serverManager.LogReceived += OnLogReceived;
        _serverManager.StatusChanged += OnStatusChanged;

        _steamCmdHandler = GetNode<SteamCmdHandler>("/root/SteamCmdHandler");
        _steamCmdHandler.LogReceived += (msg, err) => Log(msg, err);

        _startStopButton.Pressed += OnStartStopPressed;
        _serverList.ItemSelected += OnServerSelected;
        _modsButton.Pressed += OnModsButtonPressed;
        _settingsButton.Pressed += OnSettingsButtonPressed;
        _playersButton.Pressed += OnPlayersButtonPressed;
        _updateServerButton.Pressed += OnUpdateServerButtonPressed;
        _newServerButton.Pressed += () => _serverSetupUI.PopupCentered();
        _cloneButton.Pressed += OnClonePressed;
        _deleteButton.Pressed += OnDeletePressed;
        _serverSetupUI.ServerCreated += (name) => RefreshServers();
        _commandInput.TextSubmitted += OnCommandSubmitted;

        RefreshServers();
        UpdateUI();
        Log("EzPz Project Zomboid Server Tool Initialized.");
    }

    private void OnCommandSubmitted(string text)
    {
        if (string.IsNullOrEmpty(text) || _currentProfile == null) return;
        _serverManager.SendCommand(_currentProfile.Name, text);
        _commandInput.Clear();
    }

    private void OnModsButtonPressed()
    {
        if (_currentProfile == null) return;
        _modBrowser.Open(_currentProfile.Path, _currentProfile.Name);
    }

    private void OnSettingsButtonPressed()
    {
        if (_currentProfile == null) return;
        _settingsEditor.Open(_currentProfile);
    }

    private void OnPlayersButtonPressed()
    {
        if (_currentProfile == null) return;
        _playerManager.Open(_currentProfile.Name);
    }

    private void OnUpdateServerButtonPressed()
    {
        if (_currentProfile == null) return;
        _updateManager.Open(_currentProfile.Path);
    }

    private void RefreshServers()
    {
        _profiles = ProfileManager.LoadProfiles();
        _serverList.Clear();
        foreach (var p in _profiles)
        {
            _serverList.AddItem(p.Name);
        }
    }

    private void OnServerSelected(long index)
    {
        _currentProfile = _profiles[(int)index];
        Log($"Selected server: {_currentProfile.Name}");
        UpdateUI();
    }

    private void UpdateUI()
    {
        bool hasProfile = _currentProfile != null;

        // Settings and Mods
        _settingsButton.Disabled = !hasProfile;
        _modsButton.Disabled = !hasProfile;
        _playersButton.Disabled = !hasProfile;
        _updateServerButton.Disabled = !hasProfile;

        // Console Controls
        _commandInput.Editable = hasProfile;
        _startStopButton.Disabled = !hasProfile;

        // Management
        _cloneButton.Disabled = !hasProfile;
        _deleteButton.Disabled = !hasProfile;

        if (!hasProfile)
        {
            _startStopButton.Text = "Start Server";
            _statusLabel.Text = "Status: No Server Selected";
            return;
        }

        bool running = _serverManager.IsRunning(_currentProfile.Name);
        _startStopButton.Text = running ? "Stop Server" : "Start Server";
        _statusLabel.Text = $"Status: {(running ? "Running" : "Stopped")}";
    }

    private void OnStartStopPressed()
    {
        if (_currentProfile == null) return;

        if (_serverManager.IsRunning(_currentProfile.Name))
        {
            _serverManager.StopServer(_currentProfile.Name);
        }
        else
        {
            string iniPath = ConfigManager.GetConfigPath(_currentProfile.Path, _currentProfile.Name, ".ini");
            var ini = ConfigManager.ParseIni(iniPath);
            string adminPW = "admin123"; // Fallback
            if (ini.ContainsKey("Password") && !string.IsNullOrWhiteSpace(ini["Password"].Value))
                adminPW = ini["Password"].Value;

            _serverManager.StartServer(_currentProfile.Name, _currentProfile.Path, adminPW, _currentProfile.MinRam, _currentProfile.MaxRam, _currentProfile.JvmFlags);
        }
    }

    private void OnStatusChanged(string profileName, string newStatus)
    {
        if (_currentProfile != null && profileName == _currentProfile.Name)
        {
            UpdateUI();
        }
    }

    private void OnLogReceived(string profileName, string message, bool isError)
    {
        if (_currentProfile != null && profileName == _currentProfile.Name)
        {
            Log(message, isError);
        }
    }

    private void OnClonePressed()
    {
        if (_currentProfile == null) return;

        var dialog = new ConfirmationDialog();
        dialog.Title = "Clone Server";
        var vbox = new VBoxContainer();
        var label = new Label { Text = "New Server Name:" };
        var input = new LineEdit { Text = _currentProfile.Name + "_Clone" };
        vbox.AddChild(label);
        vbox.AddChild(input);
        dialog.AddChild(vbox);

        AddChild(dialog);
        dialog.PopupCentered(new Vector2I(300, 100));

        dialog.Confirmed += () =>
        {
            string newName = input.Text.Trim();
            if (string.IsNullOrEmpty(newName)) return;

            // Simple cloning path: neighbor folder
            string newPath = Path.Combine(Path.GetDirectoryName(_currentProfile.Path), newName);
            var cloned = ProfileManager.CloneProfile(_currentProfile, newName, newPath);
            if (cloned != null)
            {
                Log($"Cloned server {_currentProfile.Name} to {newName}");
                RefreshServers();
            }
            dialog.QueueFree();
        };
        dialog.Canceled += () => dialog.QueueFree();
    }

    private void OnDeletePressed()
    {
        if (_currentProfile == null) return;

        var dialog = new ConfirmationDialog();
        dialog.Title = "Delete Server";
        dialog.DialogText = $"Are you sure you want to remove '{_currentProfile.Name}' from EzPz?";

        var checkBox = new CheckBox { Text = "Also delete server files from disk?" };
        dialog.AddChild(checkBox);

        AddChild(dialog);
        dialog.PopupCentered(new Vector2I(350, 120));

        dialog.Confirmed += () =>
        {
            Log($"Deleting server: {_currentProfile.Name} (Files: {checkBox.ButtonPressed})");
            ProfileManager.DeleteProfile(_currentProfile, checkBox.ButtonPressed);
            _currentProfile = null;
            RefreshServers();
            UpdateUI();
            dialog.QueueFree();
        };
        dialog.Canceled += () => dialog.QueueFree();
    }

    public void Log(string message, bool isError = false)
    {
        string color = isError ? "red" : "white";
        _consoleOutput.AppendText($"[{DateTime.Now:HH:mm:ss}] [color={color}]{message}[/color]\n");
    }
}
