using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class PlayerManager : Window
{
    private Tree _playerList;
    private Button _refreshButton;
    private Button _kickButton;
    private Button _banButton;
    private Button _tpButton;
    private Button _messageButton;
    private Button _closeButton;

    private string _currentProfileName;
    private ServerManager _serverManager;
    private string _selectedPlayer;

    public override void _Ready()
    {
        _playerList = GetNode<Tree>("%PlayerList");
        _refreshButton = GetNode<Button>("%RefreshButton");
        _kickButton = GetNode<Button>("%KickButton");
        _banButton = GetNode<Button>("%BanButton");
        _tpButton = GetNode<Button>("%TPButton");
        _messageButton = GetNode<Button>("%MessageButton");
        _closeButton = GetNode<Button>("%CloseButton");

        _playerList.SetColumnTitle(0, "Username");
        _playerList.SetColumnTitle(1, "Status");

        _serverManager = GetNode<ServerManager>("/root/ServerManager");
        _serverManager.PlayerListReceived += OnPlayerListReceived;

        _refreshButton.Pressed += OnRefreshPressed;
        _kickButton.Pressed += OnKickPressed;
        _banButton.Pressed += OnBanPressed;
        _tpButton.Pressed += OnTPPressed;
        _closeButton.Pressed += () => Hide();

        _playerList.ItemSelected += () =>
        {
            var selected = _playerList.GetSelected();
            _selectedPlayer = selected?.GetText(0);
            UpdateButtons();
        };

        UpdateButtons();
    }

    public void Open(string profileName)
    {
        _currentProfileName = profileName;
        RefreshList();
        PopupCentered();
    }

    private void OnRefreshPressed()
    {
        RefreshList();
    }

    private void RefreshList()
    {
        if (string.IsNullOrEmpty(_currentProfileName)) return;

        _playerList.Clear();
        var root = _playerList.CreateItem();
        var loading = _playerList.CreateItem(root);
        loading.SetText(0, "Refreshing...");
        loading.SetText(1, "Working...");

        _serverManager.SendCommand(_currentProfileName, "players");
    }

    private void OnPlayerListReceived(string profileName, string[] players)
    {
        if (profileName != _currentProfileName) return;

        _playerList.Clear();
        var root = _playerList.CreateItem();
        foreach (var p in players)
        {
            var item = _playerList.CreateItem(root);
            item.SetText(0, p);
            item.SetText(1, "Online");
        }
        _selectedPlayer = null;
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        bool hasSelection = !string.IsNullOrEmpty(_selectedPlayer);
        _kickButton.Disabled = !hasSelection;
        _banButton.Disabled = !hasSelection;
        _tpButton.Disabled = !hasSelection;
        _messageButton.Disabled = !hasSelection;
    }

    private void OnKickPressed()
    {
        if (string.IsNullOrEmpty(_selectedPlayer)) return;
        _serverManager.SendCommand(_currentProfileName, $"kickuser \"{_selectedPlayer}\" \"Kicked by admin\"");
        RefreshList();
    }

    private void OnBanPressed()
    {
        if (string.IsNullOrEmpty(_selectedPlayer)) return;
        _serverManager.SendCommand(_currentProfileName, $"banuser \"{_selectedPlayer}\" \"Banned by admin\"");
        RefreshList();
    }

    private void OnTPPressed()
    {
        // TP admin to player or vice versa - requires admin name which we don't strictly have here
        // but we can ask or assume 'admin'. 
        if (string.IsNullOrEmpty(_selectedPlayer)) return;
        _serverManager.SendCommand(_currentProfileName, $"teleportto \"{_selectedPlayer}\"");
    }
}
