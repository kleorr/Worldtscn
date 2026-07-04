using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

public partial class WorldGenerator : Node3D
{
	public class BlockSaveData
	{
		public int X { get; set; }
		public int Y { get; set; }
		public int Z { get; set; }
		public byte Type { get; set; }
	}

	public class WorldSaveModel
	{
		public string WorldName { get; set; }
		public int Seed { get; set; }
		public int Mode { get; set; }
		public float PlayerX { get; set; }
		public float PlayerY { get; set; }
		public float PlayerZ { get; set; }
		public List<BlockSaveData> ModifiedBlocks { get; set; }
	}

	// Структура для обсчёта физики растекания воды
	public struct WaterTick
	{
		public Vector3I Position;
		public int SpreadLeft; // Сколько блоков вбок вода ещё может пройти (макс. 4)
	}

	private Dictionary<Vector3I, byte> _modifiedBlocksDict = new Dictionary<Vector3I, byte>();
	private Dictionary<Vector2I, Dictionary<Vector3I, byte>> _modifiedBlocksByChunk = new Dictionary<Vector2I, Dictionary<Vector3I, byte>>();
	
	private Dictionary<Vector2I, Chunk> _chunks = new Dictionary<Vector2I, Chunk>();
	private FastNoiseLite _noise;
	private FastNoiseLite _riverNoise; 

	private float _processTimer = 0f;
	private const float ChunkUpdateInterval = 0.4f;
	private bool _isUpdatingChunks = false;
	private HashSet<Vector2I> _chunksToKeep = new HashSet<Vector2I>();
	
	private const int SeaLevel = 14; 

	// Физика воды
	private Queue<WaterTick> _waterQueue = new Queue<WaterTick>();
	private float _waterPhysicsTimer = 0f;
	private const float WaterTickRate = 0.25f; // Задержка обновления воды (0.25с — строго как 5 тиков в Майнкрафте!)

	public override void _Ready()
	{
		if (WorldSettings.IsLoadingExistingWorld)
		{
			LoadWorldData();
		}
	}

	public override void _Process(double delta)
	{
		_processTimer += (float)delta;
		if (_processTimer >= ChunkUpdateInterval)
		{
			_processTimer = 0f;
			UpdateWorldChunksAroundPlayers();
		}

		// Вызов тика физики воды
		_waterPhysicsTimer += (float)delta;
		if (_waterPhysicsTimer >= WaterTickRate)
		{
			_waterPhysicsTimer = 0f;
			ProcessWaterPhysics();
		}
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is InputEventKey keyEvent && keyEvent.Pressed && keyEvent.Keycode == Key.F11)
		{
			if (DisplayServer.WindowGetMode() == DisplayServer.WindowMode.Fullscreen)
				DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
			else
				DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen);
		}
	}

	public async Task GenerateWorld()
	{
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

		if (_noise == null)
		{
			_noise = new FastNoiseLite();
			_noise.NoiseType = FastNoiseLite.NoiseTypeEnum.Perlin;
			_noise.Frequency = 0.015f;
		}

		if (_riverNoise == null)
		{
			_riverNoise = new FastNoiseLite();
			_riverNoise.NoiseType = FastNoiseLite.NoiseTypeEnum.Perlin;
			_riverNoise.Frequency = 0.007f; 
		}

		_noise.Seed = WorldSettings.CurrentSeed;
		_riverNoise.Seed = WorldSettings.CurrentSeed + 777; 

		int d = WorldSettings.RenderDistance;
		int chunksProcessed = 0;

		for (int x = -d; x <= d; x++)
		{
			for (int z = -d; z <= d; z++)
			{
				GenerateChunkData(x, z);
				chunksProcessed++;

				if (chunksProcessed % 8 == 0)
				{
					await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
				}
			}
		}

		foreach (var pair in _chunks)
		{
			pair.Value.RegenerateMesh(null);
		}

		CreateSeedLabelUI();
		GD.Print($"[ГЕНЕРАТОР] Мир создан. Сид: {WorldSettings.CurrentSeed}");
	}

	private async void UpdateWorldChunksAroundPlayers()
	{
		if (_isUpdatingChunks) return;
		_isUpdatingChunks = true;

		var players = GetTree().GetNodesInGroup("Players");
		_chunksToKeep.Clear();

		foreach (Node player in players)
		{
			if (player is Node3D player3D)
			{
				Vector3 pos = player3D.GlobalPosition;
				int pCx = Mathf.FloorToInt(pos.X / Chunk.Width);
				int pCz = Mathf.FloorToInt(pos.Z / Chunk.Width);

				int d = WorldSettings.RenderDistance;
				for (int x = pCx - d; x <= pCx + d; x++)
				{
					for (int z = pCz - d; z <= pCz + d; z++)
					{
						_chunksToKeep.Add(new Vector2I(x, z));
					}
				}
			}
		}

		List<Vector2I> chunksToRemove = new List<Vector2I>();
		foreach (var key in _chunks.Keys)
		{
			if (!_chunksToKeep.Contains(key))
			{
				chunksToRemove.Add(key);
			}
		}

		foreach (var key in chunksToRemove)
		{
			if (_chunks.TryGetValue(key, out Chunk chunk))
			{
				chunk.QueueFree();
				_chunks.Remove(key);
			}
		}

		foreach (var targetPos in _chunksToKeep)
		{
			if (!_chunks.ContainsKey(targetPos))
			{
				GenerateChunkData(targetPos.X, targetPos.Y);
				_chunks[targetPos].RegenerateMesh(null);

				await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
				if (!IsInsideTree()) return; 
			}
		}

		_isUpdatingChunks = false;
	}

	private void GenerateChunkData(int cx, int cz)
	{
		Chunk chunk = new Chunk();
		chunk.ChunkX = cx;
		chunk.ChunkZ = cz;
		chunk.Name = $"Chunk_{cx}_{cz}";
		
		chunk.Position = new Vector3(cx * Chunk.Width, 0, cz * Chunk.Width);
		AddChild(chunk);
		
		_chunks[new Vector2I(cx, cz)] = chunk;

		Random rand = new Random(cx * 31 + cz * 7 + WorldSettings.CurrentSeed);

		for (int x = 0; x < Chunk.Width; x++)
		{
			for (int z = 0; z < Chunk.Width; z++)
			{
				int height = 5; 
				int globalX = cx * Chunk.Width + x;
				int globalZ = cz * Chunk.Width + z;

				// ИСКЛЮЧЕНИЕ ДЛЯ ПЛОСКОГО МИРА — Строго сухой и плоский!
				if (WorldSettings.CurrentMode == WorldSettings.GeneratorMode.Flat)
				{
					height = 5;
					for (int y = 0; y < Chunk.Height; y++)
					{
						if (y == 0)
							chunk.Blocks[x, y, z] = 1; // Бедрок
						else if (y == height - 1)
							chunk.Blocks[x, y, z] = 3; // Трава
						else if (y < height - 1)
							chunk.Blocks[x, y, z] = 2; // Камень
						else
							chunk.Blocks[x, y, z] = 0; // Воздух
					}
					continue; 
				}

				// Нормальный мир с реками на поверхности
				float noiseVal = _noise.GetNoise2D(globalX, globalZ);
				float baseHeight = (noiseVal + 1f) * 0.5f * 22f + 13f; 

				// Плавная генерация рельефа рек
				float rNoise = _riverNoise.GetNoise2D(globalX, globalZ);
				float absRiver = Mathf.Abs(rNoise);

				if (absRiver < 0.035f) // Ширина русла
				{
					float intensity = 1f - (absRiver / 0.035f); // 1 в центре реки, 0 у берегов
					// Вместо каньона, плавно прижимаем ландшафт к уровню воды
					baseHeight = Mathf.Lerp(baseHeight, SeaLevel - 2f, intensity);
				}

				height = Mathf.FloorToInt(baseHeight);

				for (int y = 0; y < Chunk.Height; y++)
				{
					if (y == 0)
						chunk.Blocks[x, y, z] = 1;
					else if (y < height - 1)
						chunk.Blocks[x, y, z] = 2;
					else if (y == height - 1)
					{
						chunk.Blocks[x, y, z] = (height <= SeaLevel) ? (byte)2 : (byte)3;
					}
					else
					{
						if (y <= SeaLevel)
							chunk.Blocks[x, y, z] = 8; // Заливаем русло
						else
							chunk.Blocks[x, y, z] = 0;
					}
				}
			} 
		} 

		if (WorldSettings.CurrentMode == WorldSettings.GeneratorMode.Normal)
		{
			for (int x = 2; x < Chunk.Width - 2; x++)
			{
				for (int z = 2; z < Chunk.Width - 2; z++)
				{
					if (rand.NextDouble() < 0.02) 
					{
						int globalX = cx * Chunk.Width + x;
						int globalZ = cz * Chunk.Width + z;
						
						float noiseVal = _noise.GetNoise2D(globalX, globalZ);
						float baseHeight = (noiseVal + 1f) * 0.5f * 22f + 13f;
						float rNoise = _riverNoise.GetNoise2D(globalX, globalZ);

						if (Mathf.Abs(rNoise) < 0.035f)
						{
							baseHeight = Mathf.Lerp(baseHeight, SeaLevel - 2f, 1f - (Mathf.Abs(rNoise) / 0.035f));
						}
						
						int height = Mathf.FloorToInt(baseHeight);

						if (height > SeaLevel && Mathf.Abs(rNoise) >= 0.035f)
						{
							GrowTree(chunk, x, height, z, rand);
						}
					}
				}
			}
		}

		Vector2I chunkKey = new Vector2I(cx, cz);
		if (_modifiedBlocksByChunk.TryGetValue(chunkKey, out var modifiedInChunk))
		{
			foreach (var pair in modifiedInChunk)
			{
				Vector3I globalPos = pair.Key;
				int bx = globalPos.X - cx * Chunk.Width;
				int by = globalPos.Y;
				int bz = globalPos.Z - cz * Chunk.Width;

				if (bx >= 0 && bx < Chunk.Width && by >= 0 && by < Chunk.Height && bz >= 0 && bz < Chunk.Width)
				{
					chunk.Blocks[bx, by, bz] = pair.Value;
				}
			}
		}
	}

	// Алгоритм физики симуляции жидкостей (Исправлено: разлив идет волнами через задержку)
	private void ProcessWaterPhysics()
	{
		int currentQueueCount = _waterQueue.Count;
		if (currentQueueCount == 0) return;

		int maxUpdatesPerTick = 400; // Лимит блоков за один тик, чтобы не проседал FPS

		// Берем строго то количество элементов, которое БЫЛО в очереди НА НАЧАЛО этого тика.
		// Новые сгенерированные блоки воды внутри этого цикла будут обработаны только в следующем тике!
		int toProcess = Mathf.Min(currentQueueCount, maxUpdatesPerTick);

		for (int i = 0; i < toProcess; i++)
		{
			if (_waterQueue.Count == 0) break;

			WaterTick current = _waterQueue.Dequeue();

			if (GetBlockAt(current.Position.X, current.Position.Y, current.Position.Z) != 8)
				continue; 

			// 1. Проверяем блок под водой
			Vector3I downPos = current.Position + Vector3I.Down;
			if (downPos.Y > 0)
			{
				byte downBlock = GetBlockAt(downPos.X, downPos.Y, downPos.Z);
				if (downBlock == 0) // Под нами воздух -> падаем вниз!
				{
					SetBlockAt(downPos.X, downPos.Y, downPos.Z, 8, true);
					// При падении вниз сбрасываем счетчик растекания вбок обратно на 4
					_waterQueue.Enqueue(new WaterTick { Position = downPos, SpreadLeft = 4 });
					continue; 
				}
			}

			// 2. Если внизу препятствие -> пытаемся течь вбок во все 4 стороны
			if (current.SpreadLeft > 0)
			{
				Vector3I[] horizontalSides = {
					current.Position + Vector3I.Forward,
					current.Position + Vector3I.Back,
					current.Position + Vector3I.Left,
					current.Position + Vector3I.Right
				};

				foreach (var side in horizontalSides)
				{
					if (GetBlockAt(side.X, side.Y, side.Z) == 0) // Если сбоку воздух
					{
						SetBlockAt(side.X, side.Y, side.Z, 8, true);
						_waterQueue.Enqueue(new WaterTick { Position = side, SpreadLeft = current.SpreadLeft - 1 });
					}
				}
			}
		}
	}

	private void GrowTree(Chunk chunk, int tx, int startY, int tz, Random rand)
	{
		int treeHeight = rand.Next(4, 6);

		if (tx < 2 || tx > Chunk.Width - 3 || tz < 2 || tz > Chunk.Width - 3 || startY + treeHeight + 2 >= Chunk.Height)
			return;

		for (int i = 0; i < treeHeight; i++)
		{
			chunk.Blocks[tx, startY + i, tz] = 4;
		}

		int leafStart = startY + treeHeight - 2;
		for (int ly = leafStart; ly <= startY + treeHeight + 1; ly++)
		{
			int radius = (ly >= startY + treeHeight) ? 1 : 2;

			for (int lx = tx - radius; lx <= tx + radius; lx++)
			{
				for (int lz = tz - radius; lz <= tz + radius; lz++)
				{
					if (chunk.Blocks[lx, ly, lz] == 0)
					{
						chunk.Blocks[lx, ly, lz] = 5;
					}
				}
			}
		}
	}

	private void CreateSeedLabelUI()
	{
		CanvasLayer canvas = new CanvasLayer();
		Label label = new Label();
		label.Text = $"World: {WorldSettings.CurrentWorldName} | Seed: {WorldSettings.CurrentSeed} | Mode: {WorldSettings.CurrentMode}";
		label.Position = new Vector2(20, 20); 
		
		canvas.AddChild(label);
		AddChild(canvas);
	}

	// ПЕРЕГРУЗКА: Этот метод вызывается Игроком и внешними скриптами
	public void SetBlockAt(int globalX, int globalY, int globalZ, byte type)
	{
		SetBlockAt(globalX, globalY, globalZ, type, false);
	}

	// ОСНОВНОЙ МЕТОД: Используется внутри симуляции физики и перегрузки
	public void SetBlockAt(int globalX, int globalY, int globalZ, byte type, bool fromPhysics)
	{
		int cx = Mathf.FloorToInt((float)globalX / Chunk.Width);
		int cz = Mathf.FloorToInt((float)globalZ / Chunk.Width);

		Vector2I key = new Vector2I(cx, cz);
		Vector3I globalPos = new Vector3I(globalX, globalY, globalZ);
		_modifiedBlocksDict[globalPos] = type;

		if (!_modifiedBlocksByChunk.ContainsKey(key))
			_modifiedBlocksByChunk[key] = new Dictionary<Vector3I, byte>();
		_modifiedBlocksByChunk[key][globalPos] = type;

		if (_chunks.ContainsKey(key))
		{
			Chunk chunk = _chunks[key];
			int bx = globalX - cx * Chunk.Width;
			int by = globalY;
			int bz = globalZ - cz * Chunk.Width;

			if (bx >= 0 && bx < Chunk.Width && by >= 0 && by < Chunk.Height && bz >= 0 && bz < Chunk.Width)
			{
				if (type == 0 && chunk.Blocks[bx, by, bz] == 1) return;
				chunk.Blocks[bx, by, bz] = type;
				chunk.RegenerateMesh(null);

				// Если поставлена вода И это ручная установка ведра (НЕ симуляция физики)
				if (type == 8 && !fromPhysics)
				{
					_waterQueue.Enqueue(new WaterTick { Position = globalPos, SpreadLeft = 4 });
				}

				// Если блок уничтожен (стал воздухом), будим соседнюю воду, чтобы она текла сюда
				if (type == 0)
				{
					Vector3I[] neighbors = {
						globalPos + Vector3I.Up,
						globalPos + Vector3I.Forward,
						globalPos + Vector3I.Back,
						globalPos + Vector3I.Left,
						globalPos + Vector3I.Right
					};

					foreach (var n in neighbors)
					{
						if (GetBlockAt(n.X, n.Y, n.Z) == 8)
						{
							_waterQueue.Enqueue(new WaterTick { Position = n, SpreadLeft = 4 });
						}
					}
				}

				// УБИРАЕМ КУБИЧЕСКИЕ БАРЬЕРЫ
				if (bx == 0) NotifyChunkToUpdate(cx - 1, cz);
				if (bx == Chunk.Width - 1) NotifyChunkToUpdate(cx + 1, cz);
				if (bz == 0) NotifyChunkToUpdate(cx, cz - 1);
				if (bz == Chunk.Width - 1) NotifyChunkToUpdate(cx, cz + 1);
			}
		}
	}

	private void NotifyChunkToUpdate(int cx, int cz)
	{
		Vector2I neighborKey = new Vector2I(cx, cz);
		if (_chunks.TryGetValue(neighborKey, out Chunk neighborChunk))
		{
			neighborChunk.RegenerateMesh(null);
		}
	}
	
	public byte GetBlockAt(int globalX, int globalY, int globalZ)
	{
		if (globalY < 0 || globalY >= Chunk.Height) return 0;

		int cx = Mathf.FloorToInt((float)globalX / Chunk.Width);
		int cz = Mathf.FloorToInt((float)globalZ / Chunk.Width);
		Vector2I key = new Vector2I(cx, cz);

		if (_chunks.TryGetValue(key, out Chunk chunk))
		{
			int bx = globalX - cx * Chunk.Width;
			int by = globalY;
			int bz = globalZ - cz * Chunk.Width;

			if (bx >= 0 && bx < Chunk.Width && by >= 0 && by < Chunk.Height && bz >= 0 && bz < Chunk.Width)
			{
				return chunk.Blocks[bx, by, bz];
			}
		}
		return 0;
	}

	public void SaveWorldData()
	{
		try
		{
			if (!System.IO.Directory.Exists(WorldSettings.SaveFolderPath))
			{
				System.IO.Directory.CreateDirectory(WorldSettings.SaveFolderPath);
			}

			string filePath = System.IO.Path.Combine(WorldSettings.SaveFolderPath, $"{WorldSettings.CurrentWorldName}.json");

			float pX = 0f, pY = 70f, pZ = 0f;
			var players = GetTree().GetNodesInGroup("Players");
			foreach (Node player in players)
			{
				if (player is CharacterBody3D player3D && player3D.IsMultiplayerAuthority())
				{
					pX = player3D.GlobalPosition.X;
					pY = player3D.GlobalPosition.Y;
					pZ = player3D.GlobalPosition.Z;
					break;
				}
			}

			WorldSaveModel saveModel = new WorldSaveModel
			{
				WorldName = WorldSettings.CurrentWorldName,
				Seed = WorldSettings.CurrentSeed,
				Mode = (int)WorldSettings.CurrentMode,
				PlayerX = pX,
				PlayerY = pY,
				PlayerZ = pZ,
				ModifiedBlocks = new List<BlockSaveData>()
			};

			foreach (var pair in _modifiedBlocksDict)
			{
				saveModel.ModifiedBlocks.Add(new BlockSaveData
				{
					X = pair.Key.X,
					Y = pair.Key.Y,
					Z = pair.Key.Z,
					Type = pair.Value
				});
			}

			string jsonString = JsonSerializer.Serialize(saveModel);
			File.WriteAllText(filePath, jsonString);
		}
		catch (Exception e)
		{
			GD.PrintErr($"[ОШИБКА СОХРАНЕНИЯ] {e.Message}");
		}
	}

	public void LoadWorldData()
	{
		string filePath = System.IO.Path.Combine(WorldSettings.SaveFolderPath, $"{WorldSettings.CurrentWorldName}.json");

		if (!File.Exists(filePath))
		{
			return;
		}

		try
		{
			string jsonString = File.ReadAllText(filePath);
			WorldSaveModel saveModel = JsonSerializer.Deserialize<WorldSaveModel>(jsonString);

			if (saveModel != null)
			{
				WorldSettings.CurrentSeed = saveModel.Seed;
				WorldSettings.CurrentMode = (WorldSettings.GeneratorMode)saveModel.Mode;
				WorldSettings.SavedPlayerPosition = new Vector3(saveModel.PlayerX, saveModel.PlayerY, saveModel.PlayerZ);

				_modifiedBlocksDict.Clear();
				_modifiedBlocksByChunk.Clear();

				foreach (var blockData in saveModel.ModifiedBlocks)
				{
					Vector3I pos = new Vector3I(blockData.X, blockData.Y, blockData.Z);
					_modifiedBlocksDict[pos] = blockData.Type;

					int bcx = Mathf.FloorToInt((float)pos.X / Chunk.Width);
					int bcz = Mathf.FloorToInt((float)pos.Z / Chunk.Width);
					Vector2I chunkKey = new Vector2I(bcx, bcz);

					if (!_modifiedBlocksByChunk.ContainsKey(chunkKey))
						_modifiedBlocksByChunk[chunkKey] = new Dictionary<Vector3I, byte>();
					
					_modifiedBlocksByChunk[chunkKey][pos] = blockData.Type;
				}
			}
		}
		catch (Exception e)
		{
			GD.PrintErr($"[ОШИБКА ЗАГРУЗКИ] {e.Message}");
		}
	}
}
