using Godot;
using System;

public partial class NetworkManager : Node
{
	// Статическая ссылка, чтобы легко вызывать сетевые методы из других скриптов
	public static NetworkManager Instance { get; private set; }

	[Export] public PackedScene PlayerScene; 
	private const int DefaultPort = 25565;
	
	private CanvasLayer _loadingScreen;

	public override void _Ready()
	{
		Instance = this; // Инициализируем синглтон

		Multiplayer.PeerConnected += OnPeerConnected;
		Multiplayer.PeerDisconnected += OnPeerDisconnected;
		
		// Сигналы для контроля подключения клиента
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

	private async void StartLocalWorld()
	{
		ShowLoadingScreen("Generating world. Please wait...");
		
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
		// Проверяем, запущен ли уже реальный ENet сервер, игнорируя встроенный офлайн-пир Godot
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
		GD.Print($"[СЕТЬ] Сервер запущен на порту {DefaultPort}");
	}

	private void JoinGame(string ipAddress)
	{
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

	// === СИГНАЛЫ КЛИЕНТА ===

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

	// === СЕТЕВАЯ ЛОГИКА ===

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
		else
		{
			GD.PrintErr("[ОШИБКА] Не найден скрипт WorldGenerator на главной сцене клиента!");
		}

		GD.Print("[СЕТЬ] Карта построена. Запрашиваем спавн у сервера...");
		RpcId(1, nameof(ClientIsReadyToSpawn));
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void ClientIsReadyToSpawn()
	{
		long clientId = Multiplayer.GetRemoteSenderId();
		GD.Print($"[СЕТЬ] Клиент {clientId} полностью готов. Спавним его персонажа.");
		
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
		foreach (Node player in GetTree().GetNodesInGroup("Players"))
		{
			if (player is Node3D player3D && player3D.GetMultiplayerAuthority() == Multiplayer.GetUniqueId())
			{
				player3D.GlobalPosition = position;
				GD.Print("[СЕТЬ] Персонаж установлен на точку спавна. Игра началась!");
				break;
			}
		}
		HideLoadingScreen();
	}

	private void OnPeerDisconnected(long id)
	{
		GD.Print($"[СЕТЬ] Игрок {id} отключился. Удаляем его модельку...");
		
		// ИСПРАВЛЕНО: Ищем игрока по его реальному Сетевому ID в группе, а не по имени ноды
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
			{
				PlayerScene = GD.Load<PackedScene>("res://player.tscn");
			}
			else
			{
				PlayerScene = GD.Load<PackedScene>("res://Player.tscn");
			}
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
		
		// ИСПРАВЛЕНО: Добавляем игрока в специальную группу для точного отслеживания сети
		player.AddToGroup("Players");
		
		float centerCoordinate = (32 * Chunk.Width) / 2f;
		player.Position = new Vector3(centerCoordinate, Chunk.Height + 5f, centerCoordinate); 
		
		GetTree().CurrentScene.CallDeferred(Node.MethodName.AddChild, player);
		GD.Print($"[СЕТЬ] Игрок {id} успешно заспавнен в координатах {player.Position}");
	}

	// ========================================================
	// СИНХРОНИЗАЦИЯ БЛОКОВ (НОВЫЙ ФУНКЦИОНАЛ ДЛЯ ВЕРСИИ 1.4.1)
	// ========================================================

	// Вызывай этот метод из скрипта ломания/копания блоков игрока вместо прямого обращения к генератору!
	public void ChallengeBlockChange(Vector3I blockCoords, int blockType)
	{
		// Если мы подключены к сети — шлем RPC запрос
		if (Multiplayer.MultiplayerPeer is ENetMultiplayerPeer enetPeer && enetPeer.GetConnectionStatus() == MultiplayerPeer.ConnectionStatus.Connected)
		{
			Rpc(nameof(ServerChangeBlockRpc), blockCoords, blockType);
		}
		else
		{
			// Если играем в одиночку — просто меняем блок на месте
			ApplyBlockChangeLocally(blockCoords, blockType);
		}
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void ServerChangeBlockRpc(Vector3I blockCoords, int blockType)
	{
		if (!Multiplayer.IsServer()) return;

		// Сервер меняет у себя
		ApplyBlockChangeLocally(blockCoords, blockType);
		// И принудительно рассылает всем клиентам
		Rpc(nameof(ClientChangeBlockRpc), blockCoords, blockType);
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void ClientChangeBlockRpc(Vector3I blockCoords, int blockType)
	{
		if (Multiplayer.IsServer()) return; // Сервер уже применил это у себя ранее
		
		// Клиенты меняют у себя
		ApplyBlockChangeLocally(blockCoords, blockType);
	}

	private void ApplyBlockChangeLocally(Vector3I blockCoords, int blockType)
	{
		var worldGenerator = GetTree().CurrentScene as WorldGenerator;
		if (worldGenerator != null)
		{
			// !!! ВНИМАНИЕ: Замени метод ниже на РЕАЛЬНОЕ название метода установки блоков в твоем скрипте WorldGenerator !!!
			// Например, если у тебя метод принимает три инта: worldGenerator.SetBlock(blockCoords.X, blockCoords.Y, blockCoords.Z, blockType);
			// Или если принимает Vector3I: worldGenerator.SetBlock(blockCoords, blockType);
			
			// worldGenerator.SetBlock(blockCoords, blockType); 
			GD.Print($"[БЛОКИ] Изменен блок в точке {blockCoords} на тип {blockType}");
		}
	}

	// === ИНТЕРФЕЙС ЗАГРУЗКИ (ENGLISH) ===

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
