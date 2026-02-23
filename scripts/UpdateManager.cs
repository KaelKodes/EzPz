using Godot;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

public partial class UpdateManager : Window
{
    private OptionButton _branchSelector;
    private Button _fetchButton;
    private Button _updateButton;
    private Button _closeButton;
    private RichTextLabel _logOutput;
    private ProgressBar _progressBar;
    private Label _statusLabel;

    private SteamCmdHandler _steamCmdHandler;
    private string _currentInstallDir;
    private bool _isUpdating = false;

    public override void _Ready()
    {
        _branchSelector = GetNode<OptionButton>("%BranchSelector");
        _fetchButton = GetNode<Button>("%FetchButton");
        _updateButton = GetNode<Button>("%UpdateButton");
        _closeButton = GetNode<Button>("%CloseButton");
        _logOutput = GetNode<RichTextLabel>("%LogOutput");
        _progressBar = GetNode<ProgressBar>("%ProgressBar");
        _statusLabel = GetNode<Label>("%StatusLabel");

        _steamCmdHandler = GetNode<SteamCmdHandler>("/root/SteamCmdHandler");

        _steamCmdHandler.LogReceived += OnSteamLog;
        _steamCmdHandler.ProgressUpdated += OnSteamProgress;
        _steamCmdHandler.OperationFinished += OnSteamFinished;

        _fetchButton.Pressed += OnFetchPressed;
        _updateButton.Pressed += OnUpdatePressed;
        _closeButton.Pressed += () => Hide();

        _updateButton.Disabled = true;
    }

    public void Open(string installDir)
    {
        _currentInstallDir = installDir;
        _logOutput.Clear();
        _progressBar.Value = 0;
        _statusLabel.Text = "Idle";
        PopupCentered();

        if (_branchSelector.ItemCount == 0)
        {
            OnFetchPressed();
        }
    }

    private async void OnFetchPressed()
    {
        _fetchButton.Disabled = true;
        _statusLabel.Text = "Fetching branches...";

        try
        {
            var branches = await _steamCmdHandler.GetAvailableBranches();
            _branchSelector.Clear();
            foreach (var branch in branches)
            {
                _branchSelector.AddItem(branch.Value);
                _branchSelector.SetItemMetadata(_branchSelector.ItemCount - 1, branch.Key);
            }

            _statusLabel.Text = "Branches loaded.";
            _updateButton.Disabled = false;
        }
        catch (Exception e)
        {
            _statusLabel.Text = "Error fetching branches.";
            Log($"Error: {e.Message}", true);
        }
        finally
        {
            _fetchButton.Disabled = false;
        }
    }

    private async void OnUpdatePressed()
    {
        if (_isUpdating) return;

        int idx = _branchSelector.GetSelectedId();
        string branchKey = (string)_branchSelector.GetItemMetadata(idx);
        if (branchKey == "public") branchKey = ""; // empty means stable/public

        _isUpdating = true;
        SetUIState(false);
        _logOutput.Clear();
        Log($"Starting update for branch: {branchKey ?? "stable"}...");

        await _steamCmdHandler.InstallOrUpdateServer(_currentInstallDir, branchKey);
    }

    private void SetUIState(bool enabled)
    {
        _branchSelector.Disabled = !enabled;
        _fetchButton.Disabled = !enabled;
        _updateButton.Disabled = !enabled;
        _closeButton.Disabled = !enabled;
    }

    private void OnSteamLog(string message, bool isError)
    {
        Log(message, isError);
    }

    private void OnSteamProgress(string status, float progress)
    {
        _statusLabel.Text = status;
        _progressBar.Value = progress;
    }

    private void OnSteamFinished(bool success)
    {
        _isUpdating = false;
        SetUIState(true);
        _statusLabel.Text = success ? "Update Complete!" : "Update Failed.";
        Log(success ? "SUCCESS: Server is up to date." : "FAILURE: Update encountered an error.", !success);
    }

    private void Log(string message, bool isError = false)
    {
        string color = isError ? "red" : "white";
        _logOutput.AppendText($"[color={color}]{message}[/color]\n");
    }
}
