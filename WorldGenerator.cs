using Godot;
using System;
using System.Collections.Generic;

public partial class WorldGenerator : Node3D
{
	private Dictionary<Vector2I, Chunk> _chunks = new Dictionary<Vector2I, Chunk>();
	private FastNoiseLite _noise;

	public override void _Ready()
	{
		_noise = new FastNoiseLite();
		_noise.NoiseType = FastNoiseLite.NoiseTypeEnum.Perlin;
		
		// Настройка частоты: чем меньше число, тем шире и массивнее горы. 
		// 0.02f — золотая середина для красивого рельефа
		_noise.Frequency = 0.02f; 

		_noise.Seed = WorldSettings.CurrentSeed;

		for (int x = 0; x < 32; x++)
		{
			for (int z = 0; z < 32; z++)
			{
				GenerateChunk(x, z);
			}
		}

		TeleportPlayerToCenter();
		CreateInvisibleBarriers();
		CreateSeedLabelUI();
	}

	private void GenerateChunk(int cx, int cz)
	{
		Chunk chunk = new Chunk();
		chunk.ChunkX = cx;
		chunk.ChunkZ = cz;
		chunk.Name = $"Chunk_{cx}_{cz}";
		AddChild(chunk);
		chunk.GlobalPosition = new Vector3(cx * Chunk.Width, 0, cz * Chunk.Width);
		
		_chunks[new Vector2I(cx, cz)] = chunk;

		Random rand = new Random(cx * 31 + cz * 7 + WorldSettings.CurrentSeed);

		// ЭТАП 1: Генерация базового ландшафта
		for (int x = 0; x < Chunk.Width; x++)
		{
			for (int z = 0; z < Chunk.Width; z++)
			{
				int height = 5; // Высота для плоского мира

				if (WorldSettings.CurrentMode == WorldSettings.GeneratorMode.Normal)
				{
					int globalX = cx * Chunk.Width + x;
					int globalZ = cz * Chunk.Width + z;

					float noiseVal = _noise.GetNoise2D(globalX, globalZ); // выдает от -1 до 1
					
					// Базовый уровень земли сделаем на 10 блоках, 
					// а горы смогут подниматься еще на +25 блоков вверх (итого до 35 блоков, что отлично влезет в высоту 64)
					height = Mathf.FloorToInt((noiseVal + 1f) * 0.5f * 25) + 10; 
				}

				for (int y = 0; y < Chunk.Height; y++)
				{
					if (y == 0)
						chunk.Blocks[x, y, z] = 1; // Бедрок
					else if (y < height - 1)
						chunk.Blocks[x, y, z] = 2; // Камень
					else if (y == height - 1)
						chunk.Blocks[x, y, z] = 3; // Трава
					else
						chunk.Blocks[x, y, z] = 0; // Воздух
				}
			}
		}

		// ЭТАП 2: Посадка деревьев
		if (WorldSettings.CurrentMode == WorldSettings.GeneratorMode.Normal)
		{
			for (int x = 2; x < Chunk.Width - 2; x++)
			{
				for (int z = 2; z < Chunk.Width - 2; z++)
				{
					int globalX = cx * Chunk.Width + x;
					int globalZ = cz * Chunk.Width + z;

					float noiseVal = _noise.GetNoise2D(globalX, globalZ);
					int height = Mathf.FloorToInt((noiseVal + 1f) * 0.5f * 25) + 10;

					if (rand.NextDouble() < 0.02) 
					{
						GrowTree(chunk, x, height, z, rand);
					}
				}
			}
		}

		chunk.RegenerateMesh(null);
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

	// ИСПРАВЛЕНО: Спавн строго по центру карты и выше потолка чанка во избежание застревания
	private void TeleportPlayerToCenter()
	{
		Node3D player = GetNodeOrNull<Node3D>("../Player") ?? GetNodeOrNull<Node3D>("Player");
		
		if (player != null)
		{
			float centerCoordinate = (32 * Chunk.Width) / 2f;
			player.GlobalPosition = new Vector3(centerCoordinate, Chunk.Height + 5f, centerCoordinate);
		}
	}

	// Создание невидимых физических барьеров по краям карты 32х32
	private void CreateInvisibleBarriers()
	{
		float mapSize = 32 * Chunk.Width;
		float center = mapSize / 2f;

		Vector3 horizSize = new Vector3(mapSize, 256f, 2f);
		Vector3 vertSize = new Vector3(2f, 256f, mapSize);

		AddBarrierWall(new Vector3(center, 128f, 0f), horizSize);         // Север
		AddBarrierWall(new Vector3(center, 128f, mapSize), horizSize);    // Юг
		AddBarrierWall(new Vector3(0f, 128f, center), vertSize);         // Запад
		AddBarrierWall(new Vector3(mapSize, 128f, center), vertSize);    // Восток
	}

	private void AddBarrierWall(Vector3 position, Vector3 size)
	{
		StaticBody3D wall = new StaticBody3D();
		CollisionShape3D shape = new CollisionShape3D();
		BoxShape3D box = new BoxShape3D();
		
		box.Size = size;
		shape.Shape = box;
		wall.AddChild(shape);
		
		// ИСПРАВЛЕНО: Сначала добавляем узел в дерево сцены, только ПОТОМ задаем GlobalPosition
		AddChild(wall); 
		wall.GlobalPosition = position;
	}

	// Создание текста с сидом и режимом в верхнем левом углу
	private void CreateSeedLabelUI()
	{
		CanvasLayer canvas = new CanvasLayer();
		Label label = new Label();
		label.Text = $"Seed: {WorldSettings.CurrentSeed} | Mode: {WorldSettings.CurrentMode}";
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
				chunk.RegenerateMesh(null);
			}
		}
	}
	public override void _UnhandledInput(InputEvent @event)
{
	// Проверяем, нажата ли клавиша F11
	if (@event is InputEventKey keyEvent && keyEvent.Pressed && keyEvent.Keycode == Key.F11)
	{
		// Если сейчас полный экран — делаем оконный режим
		if (DisplayServer.WindowGetMode() == DisplayServer.WindowMode.Fullscreen)
		{
			DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
		}
		// Если оконный — бахаем на весь экран
		else
		{
			DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen);
		}
	}
}
}
