using Godot;
using System;
using System.Collections.Generic;

public partial class NetworkManager : Node
{
	public static NetworkManager Instance { get; private set; }

	[Export] public PackedScene PlayerScene; 
	private const int DefaultPort = 25565;
	
	private CanvasLayer _loadingScreen;
	private long _itemCounter = 0;

	// Кэш профилей игроков (PeerId -> Nickname, Color)
	private readonly Dictionary<long, (string Nickname, Color Color)> _playerProfiles = new();

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
			GD.Print("[СЕТЬ] Соединение закрыто.");
		}
	}

	private bool IsOnline()
	{
		return Multiplayer.MultiplayerPeer is ENetMultiplayerPeer enet &&
			   enet.GetConnectionStatus() == MultiplayerPeer.ConnectionStatus.Connected;
	}

	public Player GetLocalPlayer()
	{
		var players = GetTree().GetNodesInGroup("Players");
		foreach (Node p in players)
		{
			if (p is Player player && player.IsMultiplayerAuthority())
			{
				return player;
			}
		}
		return null;
	}

	private async void StartLocalWorld()
	{
		ShowLoadingScreen("Generating world. Please wait...");
		
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
		GD.Print("[СЕТЬ] Подключено к серверу. Ожидаем мир...");
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
			GD.Print($"[СЕТЬ] Игрок {id} подключился. Передаем настройки, мир и профили...");

			var worldGen = GetTree().CurrentScene as WorldGenerator;
			int[] modCoords = Array.Empty<int>();
			byte[] modTypes = Array.Empty<byte>();

			if (worldGen != null)
			{
				var data = worldGen.GetModifiedBlocksData();
				modCoords = data.coords;
				modTypes = data.types;
			}

			RpcId(id, nameof(SyncWorldFromServer), 
				WorldSettings.CurrentSeed, 
				(int)WorldSettings.CurrentMode, 
				(int)WorldSettings.CurrentGameMode,
				modCoords,
				modTypes
			);

			SyncDroppedItemsToClient(id);
			SyncProfilesToNewClient(id);
		}
	}

	private void SyncDroppedItemsToClient(long peerId)
	{
		var droppedItems = GetTree().GetNodesInGroup("DroppedItems");
		foreach (Node node in droppedItems)
		{
			if (node is DroppedItem item && !item.IsQueuedForDeletion())
			{
				RpcId(peerId, nameof(BroadcastSpawnItemRpc), item.Name, item.Position, Vector3.Zero, (int)item.BlockType, item.Count);
			}
		}
	}

	// ------------------ НАДЕЖНАЯ СИНХРОНИЗАЦИЯ ПРОФИЛЕЙ (НИК И ЦВЕТ) ------------------
	public void RegisterAndBroadcastProfile(long peerId, string nickname, Color color)
	{
		_playerProfiles[peerId] = (nickname, color);

		if (IsOnline())
		{
			Rpc(nameof(SyncProfileRpc), peerId, nickname, color.R, color.G, color.B);
		}
		else
		{
			ApplyProfileToLocalNode(peerId, nickname, color);
		}
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void SyncProfileRpc(long peerId, string nickname, float r, float g, float b)
	{
		Color color = new Color(r, g, b);
		_playerProfiles[peerId] = (nickname, color);
		ApplyProfileToLocalNode(peerId, nickname, color);
	}

	private void SyncProfilesToNewClient(long newPeerId)
	{
		foreach (var pair in _playerProfiles)
		{
			RpcId(newPeerId, nameof(SyncProfileRpc), pair.Key, pair.Value.Nickname, pair.Value.Color.R, pair.Value.Color.G, pair.Value.Color.B);
		}
	}

	public void ApplyKnownProfileIfExists(Player player)
	{
		long auth = player.GetMultiplayerAuthority();
		if (_playerProfiles.TryGetValue(auth, out var profile))
		{
			player.ApplyProfile(profile.Nickname, profile.Color);
		}
	}

	private void ApplyProfileToLocalNode(long peerId, string nickname, Color color)
	{
		var players = GetTree().GetNodesInGroup("Players");
		foreach (Node p in players)
		{
			if (p is Player player && player.GetMultiplayerAuthority() == peerId)
			{
				player.ApplyProfile(nickname, color);
				break;
			}
		}
	}

	// ------------------ СИНХРОНИЗАЦИЯ МИРА И СПАВН ------------------
	[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private async void SyncWorldFromServer(int serverSeed, int serverMode, int serverGameMode, int[] modCoords, byte[] modTypes)
	{
		GD.Print($"[СЕТЬ] Данные сервера получены. Сид: {serverSeed}, режим: {serverGameMode}");
		ShowLoadingScreen("Generating world chunks...");
		
		WorldSettings.CurrentSeed = serverSeed;
		WorldSettings.CurrentMode = (WorldSettings.GeneratorMode)serverMode;
		WorldSettings.CurrentGameMode = (WorldSettings.GameMode)serverGameMode;

		var worldGenerator = GetTree().CurrentScene as WorldGenerator;
		if (worldGenerator != null)
		{
			if (modCoords != null && modTypes != null && modCoords.Length == modTypes.Length * 3)
			{
				worldGenerator.ApplyModifiedBlocksData(modCoords, modTypes);
			}

			await worldGenerator.GenerateWorld();
		}

		GD.Print("[СЕТЬ] Карта построена. Запрашиваем спавн...");
		RpcId(1, nameof(ClientIsReadyToSpawn));
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void ClientIsReadyToSpawn()
	{
		if (!Multiplayer.IsServer()) return;

		long clientId = Multiplayer.GetRemoteSenderId();
		GD.Print($"[СЕТЬ] Клиент {clientId} готов к спавну.");
		
		SpawnPlayer(clientId);
		RpcId(clientId, nameof(OnClientSpawnSuccess));
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void OnClientSpawnSuccess()
	{
		GD.Print("[СЕТЬ] Спавн завершен! Игра началась.");
		HideLoadingScreen();
	}

	private void OnPeerDisconnected(long id)
	{
		GD.Print($"[СЕТЬ] Игрок {id} отключился.");
		_playerProfiles.Remove(id);

		// ФИКС ERR_UNAUTHORIZED:
		// Если используется MultiplayerSpawner, удалять узлы обязан ТОЛЬКО сервер!
		// Спавнер сам автоматически разошлет корректный деспавн клиентам.
		if (Multiplayer.IsServer())
		{
			foreach (Node player in GetTree().GetNodesInGroup("Players"))
			{
				if (player is Node3D player3D && player3D.GetMultiplayerAuthority() == id)
				{
					player3D.QueueFree();
					break;
				}
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
			GD.PrintErr("[ОШИБКА] Файл player.tscn не найден!");
			return;
		}

		var player = PlayerScene.Instantiate() as Node3D;
		if (player == null) return;

		player.Name = id.ToString();
		player.SetMultiplayerAuthority((int)id);
		player.AddToGroup("Players"); 
		player.Position = new Vector3(0f, Chunk.Height + 5f, 0f); 
		
		GetTree().CurrentScene.AddChild(player);
		GD.Print($"[СЕТЬ] Игрок {id} заспавнен в {player.Position}");
	}

	// ------------------ ИЗМЕНЕНИЕ БЛОКОВ ------------------
	public void ChallengeBlockChange(Vector3I blockCoords, int blockType)
	{
		if (IsOnline())
		{
			if (Multiplayer.IsServer())
			{
				HandleServerBlockBreakDrop(blockCoords, blockType);
				Rpc(nameof(BroadcastBlockChangeRpc), blockCoords, blockType);
			}
			else
			{
				RpcId(1, nameof(RequestBlockChangeRpc), blockCoords, blockType);
			}
		}
		else
		{
			var worldGen = GetTree().CurrentScene as WorldGenerator;
			if (worldGen != null)
			{
				byte oldBlock = worldGen.GetBlockAt(blockCoords.X, blockCoords.Y, blockCoords.Z);
				if (oldBlock > 0 && blockType == 0 && WorldSettings.CurrentGameMode == WorldSettings.GameMode.Survival && !Chunk.IsWater(oldBlock))
				{
					Vector3 dropPos = new Vector3(blockCoords.X + 0.5f, blockCoords.Y + 0.4f, blockCoords.Z + 0.5f);
					Vector3 dropVel = new Vector3(GD.Randf() * 2f - 1f, 3.2f, GD.Randf() * 2f - 1f);
					SpawnItemDirectly($"drop_{++_itemCounter}", dropPos, dropVel, oldBlock, 1);
				}
			}
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

		HandleServerBlockBreakDrop(blockCoords, blockType);
		Rpc(nameof(BroadcastBlockChangeRpc), blockCoords, blockType);
	}

	private void HandleServerBlockBreakDrop(Vector3I blockCoords, int blockType)
	{
		var worldGenerator = GetTree().CurrentScene as WorldGenerator;
		if (worldGenerator != null)
		{
			byte currentBlock = worldGenerator.GetBlockAt(blockCoords.X, blockCoords.Y, blockCoords.Z);
			if (currentBlock > 0 && blockType == 0 && WorldSettings.CurrentGameMode == WorldSettings.GameMode.Survival && !Chunk.IsWater(currentBlock))
			{
				Vector3 dropPos = new Vector3(blockCoords.X + 0.5f, blockCoords.Y + 0.4f, blockCoords.Z + 0.5f);
				Vector3 dropVel = new Vector3(GD.Randf() * 2f - 1f, 3.2f, GD.Randf() * 2f - 1f);
				string dropId = $"drop_{++_itemCounter}";
				
				Rpc(nameof(BroadcastSpawnItemRpc), dropId, dropPos, dropVel, (int)currentBlock, 1);
			}
		}
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
			byte oldBlock = worldGenerator.GetBlockAt(blockCoords.X, blockCoords.Y, blockCoords.Z);

			if (oldBlock > 0 && blockType == 0)
			{
				var localPlayer = GetLocalPlayer();
				if (localPlayer != null)
				{
					localPlayer.SpawnBlockParticlesAt(new Vector3(blockCoords.X, blockCoords.Y, blockCoords.Z), oldBlock);
				}
			}

			worldGenerator.SetBlockAt(blockCoords.X, blockCoords.Y, blockCoords.Z, (byte)blockType); 
		}
	}

	// ------------------ ПРЕДМЕТЫ (ВЫБРОС И ПОДБОР) ------------------
	public void ChallengeDropItem(Vector3 pos, Vector3 vel, byte type, int count)
	{
		if (IsOnline())
		{
			if (Multiplayer.IsServer())
			{
				string id = $"drop_q_{++_itemCounter}";
				Rpc(nameof(BroadcastSpawnItemRpc), id, pos, vel, (int)type, count);
			}
			else
			{
				RpcId(1, nameof(RequestDropItemRpc), pos, vel, (int)type, count);
			}
		}
		else
		{
			string id = $"drop_q_{++_itemCounter}";
			SpawnItemDirectly(id, pos, vel, type, count);
		}
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void RequestDropItemRpc(Vector3 pos, Vector3 vel, int type, int count)
	{
		if (!Multiplayer.IsServer()) return;
		string id = $"drop_q_{++_itemCounter}";
		Rpc(nameof(BroadcastSpawnItemRpc), id, pos, vel, type, count);
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void BroadcastSpawnItemRpc(string id, Vector3 pos, Vector3 vel, int type, int count)
	{
		SpawnItemDirectly(id, pos, vel, (byte)type, count);
	}

	private void SpawnItemDirectly(string id, Vector3 pos, Vector3 vel, byte type, int count)
	{
		if (GetTree().CurrentScene.HasNode(id)) return;
		var item = DroppedItem.CreateItemInstance(id, pos, vel, type, count);
		if (item != null)
		{
			GetTree().CurrentScene.AddChild(item);
		}
	}

	public void ChallengePickupItem(string itemName, byte type, int count)
	{
		if (IsOnline())
		{
			if (Multiplayer.IsServer())
			{
				ProcessPickupOnServer(itemName, 1);
			}
			else
			{
				RpcId(1, nameof(RequestPickupItemRpc), itemName);
			}
		}
		else
		{
			var item = GetTree().CurrentScene.GetNodeOrNull<DroppedItem>(itemName);
			if (item != null && !item.IsQueuedForDeletion())
			{
				var player = GetLocalPlayer();
				if (player != null && player.TryPickupItem(type, count))
				{
					item.QueueFree();
				}
				else
				{
					item.IsBeingPickedUp = false;
				}
			}
		}
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void RequestPickupItemRpc(string itemName)
	{
		if (!Multiplayer.IsServer()) return;
		long senderId = Multiplayer.GetRemoteSenderId();
		ProcessPickupOnServer(itemName, senderId);
	}

	private void ProcessPickupOnServer(string itemName, long pickerPeerId)
	{
		var item = GetTree().CurrentScene.GetNodeOrNull<DroppedItem>(itemName);
		if (item != null && !item.IsQueuedForDeletion())
		{
			byte type = item.BlockType;
			int count = item.Count;
			Rpc(nameof(BroadcastPickupSuccessRpc), itemName, pickerPeerId, (int)type, count);
		}
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void BroadcastPickupSuccessRpc(string itemName, long pickerPeerId, int type, int count)
	{
		var item = GetTree().CurrentScene.GetNodeOrNull<DroppedItem>(itemName);
		if (item != null && !item.IsQueuedForDeletion())
		{
			item.QueueFree();
		}

		long myId = Multiplayer.MultiplayerPeer != null ? Multiplayer.GetUniqueId() : 1;
		if (pickerPeerId == myId)
		{
			var localPlayer = GetLocalPlayer();
			if (localPlayer != null)
			{
				localPlayer.TryPickupItem((byte)type, count);
			}
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
