using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks; // Добавлено для работы с Task

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
		public List<BlockSaveData> ModifiedBlocks { get; set; }
	}

	private Dictionary<Vector3I, byte> _modifiedBlocksDict = new Dictionary<Vector3I, byte>();
	private Dictionary<Vector2I, Chunk> _chunks = new Dictionary<Vector2I, Chunk>();
	private FastNoiseLite _noise;

	public override void _Ready()
	{
		if (WorldSettings.IsLoadingExistingWorld)
		{
			LoadWorldData();
		}
	}

	// Метод снова асинхронный, возвращает Task. Никаких блокировок потока!
	public async Task GenerateWorld()
	{
		// КРИТИЧЕСКИЙ НАВИГАТОР: Ждем один кадр, чтобы Godot вышел из сетевой фазы и разблокировал дерево сцены
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

		if (_noise == null)
		{
			_noise = new FastNoiseLite();
			_noise.NoiseType = FastNoiseLite.NoiseTypeEnum.Perlin;
			_noise.Frequency = 0.02f;
		}

		_noise.Seed = WorldSettings.CurrentSeed;

		int chunksProcessed = 0;

		// 1. Генерируем данные блоков
		for (int x = 0; x < 32; x++)
		{
			for (int z = 0; z < 32; z++)
			{
				GenerateChunkData(x, z);
				chunksProcessed++;

				// Чтобы игра не фризилась, даем Godot «подышать» каждые 64 чанка
				if (chunksProcessed % 64 == 0)
				{
					await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
				}
			}
		}

		// 2. Собираем меши для чанков
		int meshesBuilt = 0;
		foreach (var pair in _chunks)
		{
			pair.Value.RegenerateMesh(null);
			meshesBuilt++;

			if (meshesBuilt % 64 == 0)
			{
				await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			}
		}

		CreateInvisibleBarriers();
		CreateSeedLabelUI();
		GD.Print($"[ГЕНЕРАТОР] Карта успешно создана. Сид: {WorldSettings.CurrentSeed}");
	}

	private void GenerateChunkData(int cx, int cz)
	{
		Chunk chunk = new Chunk();
		chunk.ChunkX = cx;
		chunk.ChunkZ = cz;
		chunk.Name = $"Chunk_{cx}_{cz}";
		
		// ЧИСТЫЙ КОД: Задаем локальную позицию ДО добавления в дерево. Это убирает ошибку !is_inside_tree()
		chunk.Position = new Vector3(cx * Chunk.Width, 0, cz * Chunk.Width);
		AddChild(chunk);
		
		_chunks[new Vector2I(cx, cz)] = chunk;

		Random rand = new Random(cx * 31 + cz * 7 + WorldSettings.CurrentSeed);

		for (int x = 0; x < Chunk.Width; x++)
		{
			for (int z = 0; z < Chunk.Width; z++)
			{
				int height = 5; 

				if (WorldSettings.CurrentMode == WorldSettings.GeneratorMode.Normal)
				{
					int globalX = cx * Chunk.Width + x;
					int globalZ = cz * Chunk.Width + z;

					float noiseVal = _noise.GetNoise2D(globalX, globalZ);
					height = Mathf.FloorToInt((noiseVal + 1f) * 0.5f * 25) + 10; 
				}

				for (int y = 0; y < Chunk.Height; y++)
				{
					if (y == 0)
						chunk.Blocks[x, y, z] = 1; 
					else if (y < height - 1)
						chunk.Blocks[x, y, z] = 2; 
					else if (y == height - 1)
						chunk.Blocks[x, y, z] = 3; 
					else
						chunk.Blocks[x, y, z] = 0; 
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
						int height = Mathf.FloorToInt((noiseVal + 1f) * 0.5f * 25) + 10;

						GrowTree(chunk, x, height, z, rand);
					}
				}
			}
		}

		foreach (var pair in _modifiedBlocksDict)
		{
			Vector3I globalPos = pair.Key;
			int bcx = Mathf.FloorToInt((float)globalPos.X / Chunk.Width);
			int bcz = Mathf.FloorToInt((float)globalPos.Z / Chunk.Width);

			if (bcx == cx && bcz == cz)
			{
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

	private void CreateInvisibleBarriers()
	{
		float mapSize = 32 * Chunk.Width;
		float center = mapSize / 2f;

		Vector3 horizSize = new Vector3(mapSize, 256f, 2f);
		Vector3 vertSize = new Vector3(2f, 256f, mapSize);

		AddBarrierWall(new Vector3(center, 128f, 0f), horizSize);         
		AddBarrierWall(new Vector3(center, 128f, mapSize), horizSize);    
		AddBarrierWall(new Vector3(0f, 128f, center), vertSize);         
		AddBarrierWall(new Vector3(mapSize, 128f, center), vertSize);    
	}

	private void AddBarrierWall(Vector3 position, Vector3 size)
	{
		StaticBody3D wall = new StaticBody3D();
		CollisionShape3D shape = new CollisionShape3D();
		BoxShape3D box = new BoxShape3D();
		
		box.Size = size;
		shape.Shape = box;
		wall.AddChild(shape);
		
		// ЧИСТЫЙ КОД: Задаем позицию ДО AddChild
		wall.Position = position;
		AddChild(wall); 
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

	public void SetBlockAt(int globalX, int globalY, int globalZ, byte type)
	{
		int cx = Mathf.FloorToInt((float)globalX / Chunk.Width);
		int cz = Mathf.FloorToInt((float)globalZ / Chunk.Width);

		Vector2I key = new Vector2I(cx, cz);
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

				Vector3I globalPos = new Vector3I(globalX, globalY, globalZ);
				_modifiedBlocksDict[globalPos] = type;

				chunk.RegenerateMesh(null);
			}
		}
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

			WorldSaveModel saveModel = new WorldSaveModel
			{
				WorldName = WorldSettings.CurrentWorldName,
				Seed = WorldSettings.CurrentSeed,
				Mode = (int)WorldSettings.CurrentMode,
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
			GD.Print($"[УСПЕХ] Мир сохранен в папку лаунчера: {filePath}");
		}
		catch (Exception e)
		{
			GD.PrintErr($"[ОШИБКА СОХРАНЕНИЯ] Не удалось записать файл: {e.Message}");
		}
	}

	public void LoadWorldData()
	{
		string filePath = System.IO.Path.Combine(WorldSettings.SaveFolderPath, $"{WorldSettings.CurrentWorldName}.json");

		if (!File.Exists(filePath))
		{
			GD.Print($"[Загрузка] Файл сохранения {filePath} не найден. Создается новый мир.");
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

				_modifiedBlocksDict.Clear();
				foreach (var blockData in saveModel.ModifiedBlocks)
				{
					Vector3I pos = new Vector3I(blockData.X, blockData.Y, blockData.Z);
					_modifiedBlocksDict[pos] = blockData.Type;
				}
				GD.Print($"[УСПЕХ] Мир {WorldSettings.CurrentWorldName} успешно загружен!");
			}
		}
		catch (Exception e)
		{
			GD.PrintErr($"[ОШИБКА ЗАГРУЗКИ] Не удалось прочитать файл: {e.Message}");
		}
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is InputEventKey keyEvent && keyEvent.Pressed && keyEvent.Keycode == Key.F11)
		{
			if (DisplayServer.WindowGetMode() == DisplayServer.WindowMode.Fullscreen)
			{
				DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
			}
			else
			{
				DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen);
			}
		}
	}
}
