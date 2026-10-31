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
		public int GameMode { get; set; } 
		public float PlayerX { get; set; }
		public float PlayerY { get; set; }
		public float PlayerZ { get; set; }
		public List<BlockSaveData> ModifiedBlocks { get; set; }
	}

	private readonly Dictionary<Vector3I, byte> _modifiedBlocksDict = new Dictionary<Vector3I, byte>();
	private readonly Dictionary<Vector2I, Dictionary<Vector3I, byte>> _modifiedBlocksByChunk = new Dictionary<Vector2I, Dictionary<Vector3I, byte>>();
	private readonly Dictionary<Vector2I, Chunk> _chunks = new Dictionary<Vector2I, Chunk>();
	
	private FastNoiseLite _noise;          
	private FastNoiseLite _mountainNoise;  
	private FastNoiseLite _riverNoise;     
	private FastNoiseLite _oceanNoise;     

	private float _processTimer = 0f;
	private const float ChunkUpdateInterval = 0.15f;
	private bool _isUpdatingChunks = false;
	private readonly HashSet<Vector2I> _chunksToKeep = new HashSet<Vector2I>();
	private Vector2I _lastPlayerChunk = new Vector2I(int.MinValue, int.MinValue);
	
	private const int SeaLevel = 24; 

	private Queue<Vector3I> _currentWaterQueue = new Queue<Vector3I>();
	private Queue<Vector3I> _nextWaterQueue = new Queue<Vector3I>();
	private readonly HashSet<Vector3I> _queuedWaterPositions = new HashSet<Vector3I>();
	private readonly HashSet<Vector2I> _dirtyChunksThisTick = new HashSet<Vector2I>();
	private float _waterPhysicsTimer = 0f;
	private const float WaterTickRate = 0.15f; 

	private static readonly Vector3I[] CardDirs = new Vector3I[] {
		Vector3I.Forward, Vector3I.Back, Vector3I.Left, Vector3I.Right
	};

	private readonly List<Vector2I> _missingChunksCache = new List<Vector2I>();

	private Label _infoLabel;

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

		_waterPhysicsTimer += (float)delta;
		if (_waterPhysicsTimer > 1.0f) _waterPhysicsTimer = 1.0f; 

		while (_waterPhysicsTimer >= WaterTickRate)
		{
			_waterPhysicsTimer -= WaterTickRate; 
			ProcessWaterPhysics();
		}

		if (_infoLabel != null)
		{
			Vector3 playerPos = Vector3.Zero;
			var players = GetTree().GetNodesInGroup("Players");

			foreach (Node player in players)
			{
				if (player is CharacterBody3D player3D && player3D.IsMultiplayerAuthority())
				{
					playerPos = player3D.GlobalPosition;
					break;
				}
			}

			int x = Mathf.FloorToInt(playerPos.X);
			int y = Mathf.FloorToInt(playerPos.Y);
			int z = Mathf.FloorToInt(playerPos.Z);
			int fps = Mathf.RoundToInt((float)Engine.GetFramesPerSecond());

			_infoLabel.Text = $"FPS: {fps}\nWorld: {WorldSettings.CurrentWorldName} | Seed: {WorldSettings.CurrentSeed} | Mode: {WorldSettings.CurrentMode} | GameMode: {WorldSettings.CurrentGameMode}\nXYZ: {x} / {y} / {z}";
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

	// УПАКОВКА В int[] И byte[] ДЛЯ БЕЗОШИБОЧНОЙ СЕТЕВОЙ ПЕРЕДАЧИ В VARIANT
	public (int[] coords, byte[] types) GetModifiedBlocksData()
	{
		int count = _modifiedBlocksDict.Count;
		int[] coords = new int[count * 3];
		byte[] types = new byte[count];
		int i = 0;

		foreach (var pair in _modifiedBlocksDict)
		{
			coords[i * 3 + 0] = pair.Key.X;
			coords[i * 3 + 1] = pair.Key.Y;
			coords[i * 3 + 2] = pair.Key.Z;
			types[i] = pair.Value;
			i++;
		}

		return (coords, types);
	}

	public void ApplyModifiedBlocksData(int[] coords, byte[] types)
	{
		_modifiedBlocksDict.Clear();
		_modifiedBlocksByChunk.Clear();

		if (coords == null || types == null) return;
		int count = types.Length;
		if (coords.Length != count * 3) return;

		for (int i = 0; i < count; i++)
		{
			Vector3I pos = new Vector3I(coords[i * 3 + 0], coords[i * 3 + 1], coords[i * 3 + 2]);
			byte type = types[i];
			_modifiedBlocksDict[pos] = type;

			int bcx = Mathf.FloorToInt((float)pos.X / Chunk.Width);
			int bcz = Mathf.FloorToInt((float)pos.Z / Chunk.Width);
			Vector2I chunkKey = new Vector2I(bcx, bcz);

			if (!_modifiedBlocksByChunk.TryGetValue(chunkKey, out var dictInChunk))
			{
				dictInChunk = new Dictionary<Vector3I, byte>();
				_modifiedBlocksByChunk[chunkKey] = dictInChunk;
			}
			dictInChunk[pos] = type;
		}
	}

	public async Task GenerateWorld()
	{
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

		InitNoises();

		int d = WorldSettings.RenderDistance;
		List<Vector2I> initialPositions = new List<Vector2I>((2 * d + 1) * (2 * d + 1));

		for (int x = -d; x <= d; x++)
		{
			for (int z = -d; z <= d; z++)
			{
				initialPositions.Add(new Vector2I(x, z));
			}
		}

		initialPositions.Sort((a, b) => (a.X * a.X + a.Y * a.Y).CompareTo(b.X * b.X + b.Y * b.Y));

		foreach (var pos in initialPositions)
		{
			byte[,,] blocks = await Task.Run(() => GenerateChunkBlockDataAsync(pos.X, pos.Y));
			Chunk chunk = CreateChunkInstance(pos.X, pos.Y, blocks);
			_chunks[pos] = chunk;
		}

		int batchCount = 0;
		foreach (var pair in _chunks)
		{
			Chunk chunk = pair.Value;
			Chunk.MeshData data = await Task.Run(() => chunk.BuildMeshData((gx, gy, gz) => GetBlockAt(gx, gy, gz)));
			chunk.ApplyMeshData(data);

			batchCount++;
			if (batchCount % 4 == 0)
			{
				await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			}
		}

		CreateSeedLabelUI();
		GD.Print($"[ГЕНЕРАТОР] Мир создан. Сид: {WorldSettings.CurrentSeed}");
	}

	private void InitNoises()
	{
		if (_noise != null) return;

		_noise = new FastNoiseLite { NoiseType = FastNoiseLite.NoiseTypeEnum.Perlin, Frequency = 0.008f, FractalOctaves = 4, Seed = WorldSettings.CurrentSeed };
		_mountainNoise = new FastNoiseLite { NoiseType = FastNoiseLite.NoiseTypeEnum.Perlin, Frequency = 0.004f, FractalOctaves = 4, Seed = WorldSettings.CurrentSeed + 500 };
		_riverNoise = new FastNoiseLite { NoiseType = FastNoiseLite.NoiseTypeEnum.Perlin, Frequency = 0.006f, Seed = WorldSettings.CurrentSeed + 777 };
		_oceanNoise = new FastNoiseLite { NoiseType = FastNoiseLite.NoiseTypeEnum.Perlin, Frequency = 0.003f, Seed = WorldSettings.CurrentSeed + 999 };
	}

	private int GetTerrainHeightRaw(int globalX, int globalZ)
	{
		float baseN  = (_noise.GetNoise2D(globalX, globalZ) + 1f) * 0.5f;             
		float mtnN   = (_mountainNoise.GetNoise2D(globalX, globalZ) + 1f) * 0.5f;     
		float oceanN = _oceanNoise.GetNoise2D(globalX, globalZ);

		float baseHeight = 26f + baseN * 6f;

		float mountainHeight = 0f;
		if (mtnN > 0.50f)
		{
			float mountainFactor = (mtnN - 0.50f) / 0.50f;
			float smoothMtn = Mathf.SmoothStep(0f, 1f, mountainFactor);
			mountainHeight = smoothMtn * 28f;
		}

		float terrain = baseHeight + mountainHeight;

		float maxRiverAllowedHeight = SeaLevel + 6f; 
		if (terrain < maxRiverAllowedHeight + 4f)
		{
			float rNoise = Mathf.Abs(_riverNoise.GetNoise2D(globalX, globalZ));
			if (rNoise < 0.045f)
			{
				float riverFactor = 1f - (rNoise / 0.045f);
				float smoothRiver = Mathf.SmoothStep(0f, 1f, riverFactor);
				float mountainDampener = Mathf.Clamp((maxRiverAllowedHeight + 4f - terrain) / 4f, 0f, 1f);
				float targetRiverLevel = SeaLevel - 3f; 
				terrain = Mathf.Lerp(terrain, targetRiverLevel, smoothRiver * mountainDampener);
			}
		}

		if (oceanN < -0.2f)
		{
			float oceanFactor = Mathf.Clamp((-0.2f - oceanN) / 0.3f, 0f, 1f);
			float smoothOcean = Mathf.SmoothStep(0f, 1f, oceanFactor);
			terrain = Mathf.Lerp(terrain, 12f + baseN * 4f, smoothOcean);
		}

		return Mathf.Clamp(Mathf.FloorToInt(terrain), 2, Chunk.Height - 3);
	}

	private async void UpdateWorldChunksAroundPlayers()
	{
		if (_isUpdatingChunks) return;

		var players = GetTree().GetNodesInGroup("Players");
		Vector3 playerPos = Vector3.Zero;
		Vector2 playerLookDir = Vector2.Up;
		bool foundPlayer = false;

		foreach (Node player in players)
		{
			if (player is CharacterBody3D player3D && player3D.IsMultiplayerAuthority())
			{
				playerPos = player3D.GlobalPosition;
				Camera3D cam = GetViewport().GetCamera3D();
				Vector3 forward = cam != null ? -cam.GlobalTransform.Basis.Z : -player3D.GlobalTransform.Basis.Z;
				playerLookDir = new Vector2(forward.X, forward.Z).Normalized();
				foundPlayer = true;
				break;
			}
		}

		if (!foundPlayer) return;

		int pCx = Mathf.FloorToInt(playerPos.X / Chunk.Width);
		int pCz = Mathf.FloorToInt(playerPos.Z / Chunk.Width);

		if (pCx == _lastPlayerChunk.X && pCz == _lastPlayerChunk.Y) return;

		_isUpdatingChunks = true;
		_lastPlayerChunk = new Vector2I(pCx, pCz);

		InitNoises();
		int d = WorldSettings.RenderDistance;
		_chunksToKeep.Clear();
		_missingChunksCache.Clear();

		for (int x = pCx - d; x <= pCx + d; x++)
		{
			for (int z = pCz - d; z <= pCz + d; z++)
			{
				Vector2I key = new Vector2I(x, z);
				_chunksToKeep.Add(key);
				if (!_chunks.ContainsKey(key))
				{
					_missingChunksCache.Add(key);
				}
			}
		}

		List<Vector2I> chunksToRemove = null;
		foreach (var key in _chunks.Keys)
		{
			if (!_chunksToKeep.Contains(key))
			{
				chunksToRemove ??= new List<Vector2I>();
				chunksToRemove.Add(key);
			}
		}

		if (chunksToRemove != null)
		{
			foreach (var key in chunksToRemove)
			{
				if (_chunks.TryGetValue(key, out Chunk chunk))
				{
					chunk.QueueFree();
					_chunks.Remove(key);
				}
			}
		}

		if (_missingChunksCache.Count == 0)
		{
			_isUpdatingChunks = false;
			return;
		}

		_missingChunksCache.Sort((a, b) =>
		{
			Vector2 dirA = new Vector2(a.X - pCx, a.Y - pCz);
			Vector2 dirB = new Vector2(b.X - pCx, b.Y - pCz);

			float distSqA = dirA.LengthSquared();
			float distSqB = dirB.LengthSquared();

			float dotA = distSqA > 0.01f ? dirA.Normalized().Dot(playerLookDir) : 1f;
			float dotB = distSqB > 0.01f ? dirB.Normalized().Dot(playerLookDir) : 1f;

			float scoreA = distSqA - (dotA * 10f);
			float scoreB = distSqB - (dotB * 10f);

			return scoreA.CompareTo(scoreB);
		});

		HashSet<Vector2I> affectedChunks = new HashSet<Vector2I>();

		foreach (var targetPos in _missingChunksCache)
		{
			if (!_chunks.ContainsKey(targetPos))
			{
				byte[,,] blocks = await Task.Run(() => GenerateChunkBlockDataAsync(targetPos.X, targetPos.Y));

				if (!IsInsideTree()) return;

				Chunk chunk = CreateChunkInstance(targetPos.X, targetPos.Y, blocks);
				_chunks[targetPos] = chunk;
				affectedChunks.Add(targetPos);

				Vector2I[] neighborDirs = new Vector2I[] {
					new Vector2I(targetPos.X + 1, targetPos.Y),
					new Vector2I(targetPos.X - 1, targetPos.Y),
					new Vector2I(targetPos.X, targetPos.Y + 1),
					new Vector2I(targetPos.X, targetPos.Y - 1)
				};

				foreach (var nKey in neighborDirs)
				{
					if (_chunks.ContainsKey(nKey))
					{
						affectedChunks.Add(nKey);
					}
				}
			}
		}

		int batchCount = 0;
		foreach (var chunkPos in affectedChunks)
		{
			if (!_chunks.TryGetValue(chunkPos, out Chunk chunk)) continue;

			Chunk.MeshData meshData = await Task.Run(() => chunk.BuildMeshData((gx, gy, gz) => GetBlockAt(gx, gy, gz)));

			if (!IsInsideTree()) return;

			chunk.ApplyMeshData(meshData);

			batchCount++;
			if (batchCount >= 3)
			{
				batchCount = 0;
				await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			}
		}

		_isUpdatingChunks = false;
	}

	private Chunk CreateChunkInstance(int cx, int cz, byte[,,] blocks)
	{
		Chunk chunk = new Chunk
		{
			ChunkX = cx,
			ChunkZ = cz,
			Name = $"Chunk_{cx}_{cz}",
			Position = new Vector3(cx * Chunk.Width, 0, cz * Chunk.Width),
			Blocks = blocks
		};

		AddChild(chunk);

		Vector2I chunkKey = new Vector2I(cx, cz);
		if (_modifiedBlocksByChunk.TryGetValue(chunkKey, out var modifiedInChunk))
		{
			foreach (var pair in modifiedInChunk)
			{
				int bx = pair.Key.X - cx * Chunk.Width;
				int by = pair.Key.Y;
				int bz = pair.Key.Z - cz * Chunk.Width;

				if (bx >= 0 && bx < Chunk.Width && by >= 0 && by < Chunk.Height && bz >= 0 && bz < Chunk.Width)
				{
					chunk.Blocks[bx, by, bz] = pair.Value;
				}
			}
		}

		return chunk;
	}

	private byte[,,] GenerateChunkBlockDataAsync(int cx, int cz)
	{
		byte[,,] blocks = new byte[Chunk.Width, Chunk.Height, Chunk.Width];

		if (WorldSettings.CurrentMode == WorldSettings.GeneratorMode.Flat)
		{
			int flatHeight = 5;
			for (int x = 0; x < Chunk.Width; x++)
			{
				for (int z = 0; z < Chunk.Width; z++)
				{
					for (int y = 0; y < Chunk.Height; y++)
					{
						if (y == 0) blocks[x, y, z] = 1;                  
						else if (y == flatHeight - 1) blocks[x, y, z] = 3; 
						else if (y < flatHeight - 1) blocks[x, y, z] = 9;  
						else blocks[x, y, z] = 0;
					}
				}
			}
			return blocks;
		}

		Span<int> hMap = stackalloc int[18 * 18];
		for (int x = -1; x <= Chunk.Width; x++)
		{
			for (int z = -1; z <= Chunk.Width; z++)
			{
				hMap[(x + 1) * 18 + (z + 1)] = GetTerrainHeightRaw(cx * Chunk.Width + x, cz * Chunk.Width + z);
			}
		}

		Random rand = new Random(cx * 31 + cz * 7 + WorldSettings.CurrentSeed);

		for (int x = 0; x < Chunk.Width; x++)
		{
			for (int z = 0; z < Chunk.Width; z++)
			{
				int height = hMap[(x + 1) * 18 + (z + 1)];

				int hNorth = hMap[(x + 1) * 18 + z];
				int hSouth = hMap[(x + 1) * 18 + (z + 2)];
				int hWest  = hMap[x * 18 + (z + 1)];
				int hEast  = hMap[(x + 2) * 18 + (z + 1)];

				int maxDiff = Mathf.Max(
					Mathf.Abs(height - hNorth),
					Mathf.Max(Mathf.Abs(height - hSouth),
					Mathf.Max(Mathf.Abs(height - hWest), Mathf.Abs(height - hEast)))
				);

				bool isHighMountain = height > SeaLevel + 12;
				bool isCliff = isHighMountain && maxDiff >= 4;
				bool isBeachOrWater = height <= SeaLevel + 2;

				for (int y = 0; y < Chunk.Height; y++)
				{
					if (y == 0)
					{
						blocks[x, y, z] = 1; 
					}
					else if (y < height)
					{
						if (isCliff && y == height - 1)
						{
							blocks[x, y, z] = 2; 
						}
						else if (isBeachOrWater)
						{
							if (y >= height - 3)
								blocks[x, y, z] = 7; 
							else
								blocks[x, y, z] = 2; 
						}
						else
						{
							if (y == height - 1)
								blocks[x, y, z] = 3; 
							else if (y >= height - 4)
								blocks[x, y, z] = 9; 
							else
								blocks[x, y, z] = 2; 
						}
					}
					else
					{
						if (y <= SeaLevel)
							blocks[x, y, z] = 8; 
						else
							blocks[x, y, z] = 0; 
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
					if (rand.NextDouble() < 0.012) 
					{
						int height = hMap[(x + 1) * 18 + (z + 1)];

						if (height > SeaLevel + 1 && blocks[x, height - 1, z] == 3)
						{
							GrowTreeInBlocks(blocks, x, height, z, rand);
						}
					}
				}
			}
		}

		return blocks;
	}

	private void GrowTreeInBlocks(byte[,,] blocks, int tx, int startY, int tz, Random rand)
	{
		int treeHeight = rand.Next(4, 6);

		if (tx < 2 || tx > Chunk.Width - 3 || tz < 2 || tz > Chunk.Width - 3 || startY + treeHeight + 2 >= Chunk.Height)
			return;

		for (int i = 0; i < treeHeight; i++)
		{
			blocks[tx, startY + i, tz] = 4;
		}

		int leafStart = startY + treeHeight - 2;
		for (int ly = leafStart; ly <= startY + treeHeight + 1; ly++)
		{
			int radius = (ly >= startY + treeHeight) ? 1 : 2;

			for (int lx = tx - radius; lx <= tx + radius; lx++)
			{
				for (int lz = tz - radius; lz <= tz + radius; lz++)
				{
					if (blocks[lx, ly, lz] == 0)
					{
						blocks[lx, ly, lz] = 5;
					}
				}
			}
		}
	}

	private static int GetWaterLevel(byte blockType)
	{
		if (blockType == 8) return 8; 
		if (blockType >= 11 && blockType <= 17) return 18 - blockType; 
		return 0;
	}

	private static byte GetBlockTypeForWaterLevel(int level)
	{
		if (level >= 8) return 8;
		if (level <= 0) return 0;
		return (byte)(18 - level);
	}

	private static bool CanWaterFlowInto(byte blockType)
	{
		return blockType == 0 || (blockType >= 11 && blockType <= 17);
	}

	private void EnqueueWaterPos(Vector3I pos)
	{
		if (pos.Y >= 0 && pos.Y < Chunk.Height && _queuedWaterPositions.Add(pos))
		{
			_nextWaterQueue.Enqueue(pos); 
		}
	}

	private void ProcessWaterPhysics()
	{
		if (_currentWaterQueue.Count == 0)
		{
			if (_nextWaterQueue.Count == 0) return;

			var temp = _currentWaterQueue;
			_currentWaterQueue = _nextWaterQueue;
			_nextWaterQueue = temp;

			_queuedWaterPositions.Clear();
		}

		_dirtyChunksThisTick.Clear();

		while (_currentWaterQueue.Count > 0)
		{
			Vector3I pos = _currentWaterQueue.Dequeue();

			byte currentType = GetBlockAt(pos.X, pos.Y, pos.Z);
			if (!Chunk.IsWater(currentType)) continue;

			int currentLevel = GetWaterLevel(currentType);
			int idealLevel = 0;

			if (currentType == 8)
			{
				idealLevel = 8;
			}
			else
			{
				byte topType = GetBlockAt(pos.X, pos.Y + 1, pos.Z);
				if (Chunk.IsWater(topType))
				{
					idealLevel = 7;
				}
				else
				{
					int maxNeighborLevel = 0;
					foreach (var dir in CardDirs)
					{
						Vector3I nPos = pos + dir;
						byte nType = GetBlockAt(nPos.X, nPos.Y, nPos.Z);
						if (Chunk.IsWater(nType))
						{
							int nLevel = GetWaterLevel(nType);
							if (nLevel > maxNeighborLevel)
							{
								maxNeighborLevel = nLevel;
							}
						}
					}
					idealLevel = maxNeighborLevel - 1;
				}
			}

			if (idealLevel < 0) idealLevel = 0;

			if (idealLevel != currentLevel)
			{
				byte newType = GetBlockTypeForWaterLevel(idealLevel);
				if (currentType != newType)
				{
					SetBlockAtInternal(pos.X, pos.Y, pos.Z, newType, _dirtyChunksThisTick);

					EnqueueWaterPos(pos + Vector3I.Down);
					EnqueueWaterPos(pos + Vector3I.Up);
					foreach (var dir in CardDirs) EnqueueWaterPos(pos + dir);
				}
				continue;
			}

			if (currentLevel <= 0) continue;

			Vector3I downPos = pos + Vector3I.Down;
			bool canFlowDown = false;

			if (downPos.Y >= 0)
			{
				byte downType = GetBlockAt(downPos.X, downPos.Y, downPos.Z);
				if (downType == 8)
				{
					canFlowDown = true;
				}
				else if (CanWaterFlowInto(downType))
				{
					canFlowDown = true;
					int downLevel = GetWaterLevel(downType);
					if (downLevel < 7 || downType != 11)
					{
						SetBlockAtInternal(downPos.X, downPos.Y, downPos.Z, 11, _dirtyChunksThisTick); 
						EnqueueWaterPos(downPos);
					}
				}
			}

			if (!canFlowDown && currentLevel > 1)
			{
				int spreadLevel = currentLevel - 1;
				byte spreadType = GetBlockTypeForWaterLevel(spreadLevel);

				foreach (var dir in CardDirs)
				{
					Vector3I sidePos = pos + dir;
					byte sideType = GetBlockAt(sidePos.X, sidePos.Y, sidePos.Z);

					if (CanWaterFlowInto(sideType))
					{
						int sideLevel = GetWaterLevel(sideType);
						if (sideLevel < spreadLevel)
						{
							SetBlockAtInternal(sidePos.X, sidePos.Y, sidePos.Z, spreadType, _dirtyChunksThisTick);
							EnqueueWaterPos(sidePos);
						}
					}
				}
			}
		}

		foreach (var chunkKey in _dirtyChunksThisTick)
		{
			if (_chunks.TryGetValue(chunkKey, out Chunk chunk))
			{
				chunk.RegenerateMesh(null);
			}
		}
	}

	private void CreateSeedLabelUI()
	{
		CanvasLayer canvas = new CanvasLayer();
		_infoLabel = new Label
		{
			Position = new Vector2(20, 20)
		};
		
		canvas.AddChild(_infoLabel);
		AddChild(canvas);
	}

	private void SetBlockAtInternal(int globalX, int globalY, int globalZ, byte type, HashSet<Vector2I> dirtyChunks)
	{
		if (globalY < 0 || globalY >= Chunk.Height) return;

		int cx = Mathf.FloorToInt((float)globalX / Chunk.Width);
		int cz = Mathf.FloorToInt((float)globalZ / Chunk.Width);

		Vector2I key = new Vector2I(cx, cz);
		Vector3I globalPos = new Vector3I(globalX, globalY, globalZ);

		if (type == 0)
			_modifiedBlocksDict.Remove(globalPos);
		else
			_modifiedBlocksDict[globalPos] = type;

		if (!_modifiedBlocksByChunk.TryGetValue(key, out var dictInChunk))
		{
			dictInChunk = new Dictionary<Vector3I, byte>();
			_modifiedBlocksByChunk[key] = dictInChunk;
		}
		
		if (type == 0)
			dictInChunk.Remove(globalPos);
		else
			dictInChunk[globalPos] = type;

		if (_chunks.TryGetValue(key, out Chunk chunk))
		{
			int bx = globalX - cx * Chunk.Width;
			int by = globalY;
			int bz = globalZ - cz * Chunk.Width;

			if (bx >= 0 && bx < Chunk.Width && by >= 0 && by < Chunk.Height && bz >= 0 && bz < Chunk.Width)
			{
				chunk.Blocks[bx, by, bz] = type;
				dirtyChunks.Add(key);

				if (bx == 0) dirtyChunks.Add(new Vector2I(cx - 1, cz));
				if (bx == Chunk.Width - 1) dirtyChunks.Add(new Vector2I(cx + 1, cz));
				if (bz == 0) dirtyChunks.Add(new Vector2I(cx, cz - 1));
				if (bz == Chunk.Width - 1) dirtyChunks.Add(new Vector2I(cx, cz + 1));
			}
		}
	}

	public void SetBlockAt(int globalX, int globalY, int globalZ, byte type)
	{
		SetBlockAt(globalX, globalY, globalZ, type, false);
	}

	public void SetBlockAt(int globalX, int globalY, int globalZ, byte type, bool fromPhysics)
	{
		HashSet<Vector2I> dirtyChunks = new HashSet<Vector2I>();
		SetBlockAtInternal(globalX, globalY, globalZ, type, dirtyChunks);

		Vector3I globalPos = new Vector3I(globalX, globalY, globalZ);

		if (Chunk.IsWater(type))
		{
			EnqueueWaterPos(globalPos);
		}

		EnqueueWaterPos(globalPos + Vector3I.Up);
		EnqueueWaterPos(globalPos + Vector3I.Down);
		EnqueueWaterPos(globalPos + Vector3I.Forward);
		EnqueueWaterPos(globalPos + Vector3I.Back);
		EnqueueWaterPos(globalPos + Vector3I.Left);
		EnqueueWaterPos(globalPos + Vector3I.Right);

		foreach (var chunkKey in dirtyChunks)
		{
			if (_chunks.TryGetValue(chunkKey, out Chunk chunk))
			{
				chunk.RegenerateMesh(null);
			}
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
			if (!Directory.Exists(WorldSettings.SaveFolderPath))
			{
				Directory.CreateDirectory(WorldSettings.SaveFolderPath);
			}

			string filePath = Path.Combine(WorldSettings.SaveFolderPath, $"{WorldSettings.CurrentWorldName}.json");

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
				GameMode = (int)WorldSettings.CurrentGameMode,
				PlayerX = pX,
				PlayerY = pY,
				PlayerZ = pZ,
				ModifiedBlocks = new List<BlockSaveData>(_modifiedBlocksDict.Count)
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
		string filePath = Path.Combine(WorldSettings.SaveFolderPath, $"{WorldSettings.CurrentWorldName}.json");

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
				WorldSettings.CurrentGameMode = (WorldSettings.GameMode)saveModel.GameMode;
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

					if (!_modifiedBlocksByChunk.TryGetValue(chunkKey, out var dictInChunk))
					{
						dictInChunk = new Dictionary<Vector3I, byte>();
						_modifiedBlocksByChunk[chunkKey] = dictInChunk;
					}
					
					dictInChunk[pos] = blockData.Type;
				}
			}
		}
		catch (Exception e)
		{
			GD.PrintErr($"[ОШИБКА ЗАГРУЗКИ] {e.Message}");
		}
	}
}
