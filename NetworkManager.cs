using Godot;
using System;

public partial class NetworkManager : Node
{
	[Export] public PackedScene PlayerScene;
	private const int DefaultPort = 25565;
	
	private CanvasLayer _loadingScreen;

	public override void _Ready()
	{
		// Подключаем базовые сигналы
		Multiplayer.PeerConnected += OnPeerConnected;
		Multiplayer.PeerDisconnected += OnPeerDisconnected;
		
		// Сигналы для контроля подключения на стороне клиента
		Multiplayer.ConnectedToServer += OnConnectedToServer;
		Multiplayer.ConnectionFailed += OnConnectionFailed;
		Multiplayer.ServerDisconnected += OnServerDisconnected;

		if (WorldSettings.IsMultiplayerClient)
		{
			// Клиент: показываем загрузку и подключаемся
			ShowLoadingScreen("Connecting to server...");
			JoinGame(WorldSettings.ServerIp);
		}
		else
		{
			// Хост / Синглплеер: генерируем мир локально
			StartLocalWorld();
		}
	}

	private async void StartLocalWorld()
	{
		ShowLoadingScreen("Generating singleplayer world...");
		
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
		// ИСПРАВЛЕНО: Проверяем только если это РЕАЛЬНЫЙ сетевой пир ENet, игнорируя дефолтный офлайн-пир
		if (Multiplayer.MultiplayerPeer is ENetMultiplayerPeer enetPeer && enetPeer.GetConnectionStatus() != MultiplayerPeer.ConnectionStatus.Disconnected)
		{
			GD.Print("[СЕТЬ] Сервер уже работает.");
			return;
		}

		var peer = new ENetMultiplayerPeer();
		var error = peer.CreateServer(DefaultPort, 4);
		
		if (error != Error.Ok)
		{
			GD.PrintErr($"[СЕТЬ] Ошибка запуска сервера: {error}");
			return;
		}

		Multiplayer.MultiplayerPeer = peer;
		GD.Print($"[СЕТЬ] Мир успешно открыт для LAN на порту {DefaultPort}!");
	}

	private void JoinGame(string ipAddress)
	{
		var peer = new ENetMultiplayerPeer();
		var error = peer.CreateClient(ipAddress, DefaultPort);
		
		if (error != Error.Ok)
		{
			GD.PrintErr($"[СЕТЬ] Ошибка инициализации клиента: {error}");
			HideLoadingScreen();
			return;
		}

		Multiplayer.MultiplayerPeer = peer;
		GD.Print($"[СЕТЬ] Попытка подключения к хосту {ipAddress}...");
	}

	// === СИГНАЛЫ КЛИЕНТА ===

	private void OnConnectedToServer()
	{
		GD.Print("[СЕТЬ] Успешно подключились к серверу! Ожидаем данные мира...");
		ShowLoadingScreen("Syncing world data...");
	}

	private void OnConnectionFailed()
	{
		GD.PrintErr("[СЕТЬ] Не удалось подключиться к серверу.");
		ShowLoadingScreen("Error: Server unreachable!");
	}

	private void OnServerDisconnected()
	{
		GD.Print("[СЕТЬ] Соединение с сервером разорвано.");
		ShowLoadingScreen("Connection lost...");
	}

	// === СЕТЕВАЯ ЛОГИКА ===

	private void OnPeerConnected(long id)
	{
		if (Multiplayer.IsServer())
		{
			GD.Print($"[СЕТЬ] Игрок {id} вошел в LAN. Отправляем ему настройки мира...");
			RpcId(id, nameof(SyncWorldFromServer), WorldSettings.CurrentSeed, (int)WorldSettings.CurrentMode);
		}
	}

	[Rpc(MultiplayerApi.RpcMode.Authority)]
	private async void SyncWorldFromServer(int serverSeed, int serverMode)
	{
		GD.Print($"[СЕТЬ] Получены данные сервера. Сид: {serverSeed}. Строим карту...");
		ShowLoadingScreen("Generating world chunks...");
		
		WorldSettings.CurrentSeed = serverSeed;
		WorldSettings.CurrentMode = (WorldSettings.GeneratorMode)serverMode;

		var worldGenerator = GetTree().CurrentScene as WorldGenerator;
		if (worldGenerator != null)
		{
			await worldGenerator.GenerateWorld();
		}

		GD.Print("[СЕТЬ] Карта построена. Запрашиваем у сервера спавн персонажа...");
		RpcId(1, nameof(ClientIsReadyToSpawn));
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer)]
	private void ClientIsReadyToSpawn()
	{
		long clientId = Multiplayer.GetRemoteSenderId();
		GD.Print($"[СЕТЬ] Клиент {clientId} готов к спавну.");
		
		SpawnPlayer(clientId);

		float centerCoordinate = (32 * Chunk.Width) / 2f;
		Vector3 spawnPos = new Vector3(centerCoordinate, Chunk.Height + 5f, centerCoordinate);
		
		RpcId(clientId, nameof(ForceClientPosition), spawnPos);
	}

	[Rpc(MultiplayerApi.RpcMode.Authority)]
	private void ForceClientPosition(Vector3 position)
	{
		CallDeferred(nameof(DeferredSetPosition), position);
	}

	private void DeferredSetPosition(Vector3 position)
	{
		string myIdStr = Multiplayer.GetUniqueId().ToString();
		var myPlayer = GetTree().CurrentScene.GetNodeOrNull<Node3D>(myIdStr);
		
		if (myPlayer != null)
		{
			myPlayer.GlobalPosition = position;
			GD.Print($"[СЕТЬ] Персонаж на месте. Игра начинается!");
		}
		// Карта готова, игрок на месте — убираем надпись "Wait"
		HideLoadingScreen();
	}

	private void OnPeerDisconnected(long id)
	{
		var playerNode = GetTree().CurrentScene.GetNodeOrNull(id.ToString());
		if (playerNode != null)
		{
			playerNode.QueueFree();
			GD.Print($"[СЕТЬ] Игрок {id} вышел, его узел удален.");
		}
	}

	private void SpawnPlayer(long id)
	{
		if (PlayerScene == null)
		{
			PlayerScene = ResourceLoader.Exists("res://player.tscn") 
				? GD.Load<PackedScene>("res://player.tscn") 
				: GD.Load<PackedScene>("res://Player.tscn");
		}

		if (PlayerScene == null)
		{
			GD.PrintErr("[ОШИБКА] Файл сцены игрока не найден!");
			return;
		}

		var player = PlayerScene.Instantiate() as Node3D;
		if (player == null) return;

		player.Name = id.ToString();
		player.SetMultiplayerAuthority((int)id);
		
		float centerCoordinate = (32 * Chunk.Width) / 2f;
		player.Position = new Vector3(centerCoordinate, Chunk.Height + 5f, centerCoordinate);
		
		GetTree().CurrentScene.CallDeferred(Node.MethodName.AddChild, player);
		GD.Print($"[СЕТЬ] Игрок {id} заспавнен в {player.Position}");
	}

	// === ВСПОМОГАТЕЛЬНЫЙ ИНТЕРФЕЙС ЗАГРУЗКИ ===

	private void ShowLoadingScreen(string message)
	{
		if (_loadingScreen != null)
		{
			var existingLabel = _loadingScreen.GetChild(0).GetChild<Label>(0);
			if (existingLabel != null) existingLabel.Text = message;
			return;
		}

		_loadingScreen = new CanvasLayer();
		_loadingScreen.Layer = 100; // Поверх основного интерфейса и меню

		var background = new ColorRect();
		background.Color = new Color(0.1f, 0.1f, 0.12f, 1.0f); // Приятный темно-угольный цвет
		background.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		
		var label = new Label();
		label.Text = message;
		label.HorizontalAlignment = HorizontalAlignment.Center;
		label.VerticalAlignment = VerticalAlignment.Center;
		label.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		label.AddThemeFontSizeOverride("font_size", 26); // Делаем текст крупным и читаемым

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
