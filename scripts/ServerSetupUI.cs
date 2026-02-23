using Godot;
using System;
using System.IO;
using System.Threading.Tasks;

public partial class ServerSetupUI : Window
{
    private LineEdit _nameInput;
    private LineEdit _pathInput;
    private Button _browseButton;
    private OptionButton _branchSelection;
    private Label _statusLabel;
    private ProgressBar _progressBar;
    private Button _createButton;
    private Label _waitLabel;
    private Timer _blinkTimer;
    private FileDialog _fileDialog;

    private SteamCmdHandler _steamCmdHandler;
    private bool _isInstalling = false;

    [Signal] public delegate void ServerCreatedEventHandler(string name);

    public override void _Ready()
    {
        _nameInput = GetNode<LineEdit>("%NameInput");
        _pathInput = GetNode<LineEdit>("%PathInput");
        _browseButton = GetNode<Button>("%BrowseButton");
        _branchSelection = GetNode<OptionButton>("%BranchSelection");
        _statusLabel = GetNode<Label>("%StatusLabel");
        _progressBar = GetNode<ProgressBar>("%ProgressBar");
        _createButton = GetNode<Button>("%CreateButton");
        _waitLabel = GetNode<Label>("%WaitLabel");
        _blinkTimer = GetNode<Timer>("%BlinkTimer");
        _fileDialog = GetNode<FileDialog>("%FileDialog");

        _steamCmdHandler = GetNode<SteamCmdHandler>("/root/SteamCmdHandler");
        _steamCmdHandler.ProgressUpdated += OnProgressUpdated;

        _browseButton.Pressed += () => _fileDialog.PopupCentered();
        _fileDialog.DirSelected += (path) => _pathInput.Text = path;
        _createButton.Pressed += OnCreatePressed;
        _blinkTimer.Timeout += () => _waitLabel.Visible = !_waitLabel.Visible;

        CloseRequested += OnCloseRequested;

        LoadBranches();
    }

    private async void LoadBranches()
    {
        _branchSelection.Clear();
        _branchSelection.AddItem("Fetching branches...");
        _branchSelection.Disabled = true;

        var branchData = await _steamCmdHandler.GetAvailableBranches();

        _branchSelection.Clear();
        int idx = 0;
        foreach (var kvp in branchData)
        {
            _branchSelection.AddItem(kvp.Value); // Display the description
            _branchSelection.SetItemMetadata(idx, kvp.Key); // Store the actual branch ID
            idx++;
        }
        _branchSelection.Disabled = false;

        // Default to public/stable if available
        for (int i = 0; i < _branchSelection.ItemCount; i++)
        {
            if ((string)_branchSelection.GetItemMetadata(i) == "public")
            {
                _branchSelection.Select(i);
                break;
            }
        }
    }

    private async void OnCreatePressed()
    {
        string name = _nameInput.Text.Trim();
        string path = _pathInput.Text.Trim();

        int selectedIdx = _branchSelection.Selected;
        string branch = (string)_branchSelection.GetItemMetadata(selectedIdx);

        if (branch == "public") branch = ""; // Standard stable branch is empty string for app_update

        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(path))
        {
            _statusLabel.Text = "Error: Name and Path are required.";
            return;
        }

        _createButton.Disabled = true;
        _statusLabel.Text = "Initializing SteamCMD...";
        _isInstalling = true;
        _waitLabel.Show();
        _blinkTimer.Start();

        await _steamCmdHandler.InstallOrUpdateServer(path, branch);

        _statusLabel.Text = "Creating server profile...";
        var profile = new ProfileManager.ServerProfile
        {
            Name = name,
            Path = path,
            Branch = branch
        };
        ProfileManager.SaveProfile(profile);

        // Intelligently find or create the config directory
        string zomboidConfigDir = Path.Combine(path, "Zomboid", "Server");
        string targetIniPath = ConfigManager.GetConfigPath(path, name, ".ini");

        // If no config exists yet, we'll create the standard one
        if (!File.Exists(targetIniPath))
        {
            if (!Directory.Exists(zomboidConfigDir)) Directory.CreateDirectory(zomboidConfigDir);

            string finalPath = Path.Combine(zomboidConfigDir, $"{name}.ini");
            File.WriteAllText(finalPath, "# EzPz Generated Config\nPublic=false\nWorkshopItems=\nMods=\n");
            GD.Print($"[ServerSetupUI] Created new config at {finalPath}");
        }
        else
        {
            GD.Print($"[ServerSetupUI] Found existing config at {targetIniPath}, skipping dummy creation.");
        }

        _statusLabel.Text = "Server Setup Complete!";
        _isInstalling = false;
        _waitLabel.Hide();
        _blinkTimer.Stop();

        EmitSignal(SignalName.ServerCreated, name);
        Hide();
        _createButton.Disabled = false;
    }

    private void OnCloseRequested()
    {
        if (_isInstalling)
        {
            var confirm = new ConfirmationDialog();
            confirm.Title = "Installation in Progress";
            confirm.DialogText = "A server is currently being installed. Closing this window might interrupt the process. Are you sure you want to close?";
            confirm.OkButtonText = "Close Anyway";
            confirm.CancelButtonText = "Stay";

            AddChild(confirm);
            confirm.PopupCentered();
            confirm.Confirmed += () =>
            {
                _isInstalling = false; // Reset so next close doesn't trigger it
                Hide();
                confirm.QueueFree();
            };
            confirm.Canceled += () => confirm.QueueFree();
        }
        else
        {
            Hide();
        }
    }

    private void OnProgressUpdated(string status, float progress)
    {
        _statusLabel.Text = status;
        _progressBar.Value = progress * 100;
    }
}
