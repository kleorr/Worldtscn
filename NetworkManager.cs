using Godot;
using System;

public partial class NetworkManager : Node
{
	public static NetworkManager Instance { get; private set; }

	[Export] public PackedScene PlayerScene; 
	private const int DefaultPort = 25565;
	
	private CanvasLayer _loadingScreen;

	public override void _Ready()
	{
		Instance = this;

		Multiplayer.PeerConnected += OnPeerConnected;
		Multiplayer.PeerDisconnected += OnPeerDisconnected;
		
		Multiplayer.ConnectedToServer += OnConnectedToServer;
		Multiplayer.ConnectionFailed += OnConnectionFailed;
		Multiplayer.ServerDisconnected += OnServerDisconnected;

		if (WorldSettings.IsMultiplayerClient)
		{
			ShowLoadingScreen("Connecting to server...");
			JoinGame(WorldSettings.ServerIp);
		}
		else
		{
			StartLocalWorld();
		}
	}

	public override void _ExitTree()
	{
		if (Multiplayer != null)
		{
			Multiplayer.PeerConnected -= OnPeerConnected;
			Multiplayer.PeerDisconnected -= OnPeerDisconnected;
			Multiplayer.ConnectedToServer -= OnConnectedToServer;
			Multiplayer.ConnectionFailed -= OnConnectionFailed;
			Multiplayer.ServerDisconnected -= OnServerDisconnected;
		}

		DisconnectNetwork();
	}

	public void DisconnectNetwork()
	{
		if (Multiplayer != null && Multiplayer.MultiplayerPeer != null)
		{
			Multiplayer.MultiplayerPeer.Close();
			Multiplayer.MultiplayerPeer = null;
			GD.Print("[СЕТЬ] Соединение явно закрыто.");
		}
	}

	private async void StartLocalWorld()
	{
		ShowLoadingScreen("Generating world. Please wait...");
		
		// Закрываем прошлые сетевые соединения и создаем локальный OfflinePeer для одиночной игры
		DisconnectNetwork();
		Multiplayer.MultiplayerPeer = new OfflineMultiplayerPeer();

		var worldGenerator = GetTree().CurrentScene as WorldGenerator;
		if (worldGenerator != null)
		{
			GD.Print("[СЕТЬ] Ожидание генерации мира перед спавном хоста...");
			await worldGenerator.GenerateWorld();
		}
		
		SpawnPlayer(1);
		HideLoadingScreen();
	}

	public void HostGame()
	{
		DisconnectNetwork();

		var peer = new ENetMultiplayerPeer();
		var error = peer.CreateServer(DefaultPort, 4);
		
		if (error != Error.Ok)
		{
			GD.PrintErr($"[СЕТЬ] Ошибка запуска сервера: {error}");
			return;
		}

		Multiplayer.MultiplayerPeer = peer;
		GD.Print($"[СЕТЬ] Сервер запущен на порту {DefaultPort}");
	}

	private void JoinGame(string ipAddress)
	{
		DisconnectNetwork();

		var peer = new ENetMultiplayerPeer();
		var error = peer.CreateClient(ipAddress, DefaultPort);
		
		if (error != Error.Ok)
		{
			GD.PrintErr($"[СЕТЬ] Ошибка подключения к серверу: {error}");
			HideLoadingScreen();
			return;
		}

		Multiplayer.MultiplayerPeer = peer;
		GD.Print($"[СЕТЬ] Подключение к хосту {ipAddress}...");
	}

	private void OnConnectedToServer()
	{
		GD.Print("[СЕТЬ] Успешно подключились! Ожидаем данные мира...");
		ShowLoadingScreen("Syncing world data...");
	}

	private void OnConnectionFailed()
	{
		GD.PrintErr("[СЕТЬ] Не удалось подключиться.");
		ShowLoadingScreen("Error: Server unavailable!");
	}

	private void OnServerDisconnected()
	{
		GD.Print("[СЕТЬ] Соединение потеряно.");
		ShowLoadingScreen("Connection lost...");
	}

	private void OnPeerConnected(long id)
	{
		if (Multiplayer.IsServer())
		{
			GD.Print($"[СЕТЬ] Игрок {id} подключился. Отправляем ему настройки мира...");
			RpcId(id, nameof(SyncWorldFromServer), WorldSettings.CurrentSeed, (int)WorldSettings.CurrentMode);
		}
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private async void SyncWorldFromServer(int serverSeed, int serverMode)
	{
		GD.Print($"[СЕТЬ] Получены данные сервера. Синхронизируем сид: {serverSeed}");
		ShowLoadingScreen("Generating world chunks...");
		
		WorldSettings.CurrentSeed = serverSeed;
		WorldSettings.CurrentMode = (WorldSettings.GeneratorMode)serverMode;

		var worldGenerator = GetTree().CurrentScene as WorldGenerator;
		if (worldGenerator != null)
		{
			await worldGenerator.GenerateWorld();
		}

		GD.Print("[СЕТЬ] Карта построена. Запрашиваем спавн у сервера...");
		RpcId(1, nameof(ClientIsReadyToSpawn));
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void ClientIsReadyToSpawn()
	{
		if (!Multiplayer.IsServer()) return;

		long clientId = Multiplayer.GetRemoteSenderId();
		GD.Print($"[СЕТЬ] Клиент {clientId} полностью готов. Спавним его персонажа.");
		
		SpawnPlayer(clientId);
		RpcId(clientId, nameof(OnClientSpawnSuccess));
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void OnClientSpawnSuccess()
	{
		GD.Print("[СЕТЬ] Спавн успешно завершен! Игра началась.");
		HideLoadingScreen();
	}

	private void OnPeerDisconnected(long id)
	{
		GD.Print($"[СЕТЬ] Игрок {id} отключился. Удаляем его модельку...");
		
		foreach (Node player in GetTree().GetNodesInGroup("Players"))
		{
			if (player is Node3D player3D && player3D.GetMultiplayerAuthority() == id)
			{
				player3D.QueueFree();
				GD.Print($"[УСПЕХ] Моделька игрока {id} успешно удалена из мира.");
				break;
			}
		}
	}

	private void SpawnPlayer(long id)
	{
		if (PlayerScene == null)
		{
			if (ResourceLoader.Exists("res://player.tscn"))
				PlayerScene = GD.Load<PackedScene>("res://player.tscn");
			else
				PlayerScene = GD.Load<PackedScene>("res://Player.tscn");
		}

		if (PlayerScene == null)
		{
			GD.PrintErr("[ОШИБКА] Файл player.tscn не найден в проекте!");
			return;
		}

		var player = PlayerScene.Instantiate() as Node3D;
		if (player == null) return;

		player.Name = id.ToString();
		player.SetMultiplayerAuthority((int)id);
		player.AddToGroup("Players"); 
		player.Position = new Vector3(0f, Chunk.Height + 5f, 0f); 
		
		GetTree().CurrentScene.AddChild(player);
		GD.Print($"[СЕТЬ] Игрок {id} успешно заспавнен в координатах {player.Position}");
	}

	public void ChallengeBlockChange(Vector3I blockCoords, int blockType)
	{
		if (Multiplayer.MultiplayerPeer is ENetMultiplayerPeer enetPeer && enetPeer.GetConnectionStatus() == MultiplayerPeer.ConnectionStatus.Connected)
		{
			if (Multiplayer.IsServer())
			{
				Rpc(nameof(BroadcastBlockChangeRpc), blockCoords, blockType);
			}
			else
			{
				RpcId(1, nameof(RequestBlockChangeRpc), blockCoords, blockType);
			}
		}
		else
		{
			ApplyBlockChangeLocally(blockCoords, blockType);
		}
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void RequestBlockChangeRpc(Vector3I blockCoords, int blockType)
	{
		if (!Multiplayer.IsServer()) return;

		var worldGenerator = GetTree().CurrentScene as WorldGenerator;
		if (worldGenerator != null)
		{
			byte currentBlock = worldGenerator.GetBlockAt(blockCoords.X, blockCoords.Y, blockCoords.Z);
			if (currentBlock == 1) return;
		}

		Rpc(nameof(BroadcastBlockChangeRpc), blockCoords, blockType);
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void BroadcastBlockChangeRpc(Vector3I blockCoords, int blockType)
	{
		ApplyBlockChangeLocally(blockCoords, blockType);
	}

	private void ApplyBlockChangeLocally(Vector3I blockCoords, int blockType)
	{
		var worldGenerator = GetTree().CurrentScene as WorldGenerator;
		if (worldGenerator != null)
		{
			worldGenerator.SetBlockAt(blockCoords.X, blockCoords.Y, blockCoords.Z, (byte)blockType); 
			GD.Print($"[БЛОКИ] Изменен блок в точке {blockCoords} на тип {blockType}");
		}
	}

	private void ShowLoadingScreen(string message)
	{
		if (_loadingScreen != null)
		{
			var existingLabel = _loadingScreen.GetChild(0).GetChild<Label>(0);
			if (existingLabel != null) existingLabel.Text = message;
			return;
		}

		_loadingScreen = new CanvasLayer();
		_loadingScreen.Layer = 100;

		var background = new ColorRect();
		background.Color = new Color(0.1f, 0.1f, 0.12f, 1.0f);
		background.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		
		var label = new Label();
		label.Text = message;
		label.HorizontalAlignment = HorizontalAlignment.Center;
		label.VerticalAlignment = VerticalAlignment.Center;
		label.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		label.AddThemeFontSizeOverride("font_size", 26);

		background.AddChild(label);
		_loadingScreen.AddChild(background);
		AddChild(_loadingScreen);
	}

	private void HideLoadingScreen()
	{
		if (_loadingScreen != null)
		{
			_loadingScreen.QueueFree();
			_loadingScreen = null;
		}
	}
}
