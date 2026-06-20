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
		_noise.Frequency = 0.03f; // Гладкость холмов

		// Генерируем стартовую карту 3х3 чанка
		for (int x = -1; x <= 1; x++)
		{
			for (int z = -1; z <= 1; z++)
			{
				GenerateChunk(x, z);
			}
		}
	}

	private void GenerateChunk(int cx, int cz)
	{
		Chunk chunk = new Chunk();
		chunk.ChunkX = cx;
		chunk.ChunkZ = cz;
		chunk.Name = $"Chunk_{cx}_{cz}";
		AddChild(chunk); // Теперь чанк в дереве сцены и знает о глобальных координатах
		chunk.GlobalPosition = new Vector3(cx * Chunk.Width, 0, cz * Chunk.Width);
		
		_chunks[new Vector2I(cx, cz)] = chunk;

		Random rand = new Random(cx * 31 + cz * 7);

		// ЭТАП 1: Генерация базового ландшафта (без деревьев)
		for (int x = 0; x < Chunk.Width; x++)
		{
			for (int z = 0; z < Chunk.Width; z++)
			{
				int globalX = cx * Chunk.Width + x;
				int globalZ = cz * Chunk.Width + z;

				float noiseVal = _noise.GetNoise2D(globalX, globalZ);
				int height = Mathf.FloorToInt((noiseVal + 1f) * 0.5f * 8) + 3; 

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

		// ЭТАП 2: Посадка деревьев поверх готового ландшафта
		// Это предотвращает затирание листвы "воздухом" при генерации соседних блоков
		for (int x = 2; x < Chunk.Width - 2; x++)
		{
			for (int z = 2; z < Chunk.Width - 2; z++)
			{
				int globalX = cx * Chunk.Width + x;
				int globalZ = cz * Chunk.Width + z;

				float noiseVal = _noise.GetNoise2D(globalX, globalZ);
				int height = Mathf.FloorToInt((noiseVal + 1f) * 0.5f * 8) + 3;

				if (rand.NextDouble() < 0.02) // 2% шанс
				{
					GrowTree(chunk, x, height, z, rand);
				}
			}
		}

		chunk.RegenerateMesh(null);
	}

	private void GrowTree(Chunk chunk, int tx, int startY, int tz, Random rand)
	{
		int treeHeight = rand.Next(4, 6); // Высота ствола

		// Проверяем жесткие границы чанка
		if (tx < 2 || tx > Chunk.Width - 3 || tz < 2 || tz > Chunk.Width - 3 || startY + treeHeight + 2 >= Chunk.Height)
			return;

		// 1. Ставим ствол (ID 4)
		for (int i = 0; i < treeHeight; i++)
		{
			chunk.Blocks[tx, startY + i, tz] = 4;
		}

		// 2. Генерируем шапку листвы (ID 5)
		int leafStart = startY + treeHeight - 2;

		for (int ly = leafStart; ly <= startY + treeHeight + 1; ly++)
		{
			int radius = (ly >= startY + treeHeight) ? 1 : 2;

			for (int lx = tx - radius; lx <= tx + radius; lx++)
			{
				for (int lz = tz - radius; lz <= tz + radius; lz++)
				{
					// Убрали лишнее условие MathF.Abs, которое делало листву слишком "ромбовидной" и обрубленной
					if (chunk.Blocks[lx, ly, lz] == 0)
					{
						chunk.Blocks[lx, ly, lz] = 5; // Листва
					}
				}
			}
		}
	}

	// Исправленный метод изменения блоков (учитывает отрицательные координаты)
	public void SetBlockAt(int globalX, int globalY, int globalZ, byte type)
	{
		int cx = Mathf.FloorToInt((float)globalX / Chunk.Width);
		int cz = Mathf.FloorToInt((float)globalZ / Chunk.Width);

		Vector2I key = new Vector2I(cx, cz);
		if (_chunks.ContainsKey(key))
		{
			Chunk chunk = _chunks[key];
			
			// Магическая воксельная математика для правильного остатка от деления
			int bx = globalX - cx * Chunk.Width;
			int by = globalY;
			int bz = globalZ - cz * Chunk.Width;

			if (bx >= 0 && bx < Chunk.Width && by >= 0 && by < Chunk.Height && bz >= 0 && bz < Chunk.Width)
			{
				if (type == 0 && chunk.Blocks[bx, by, bz] == 1) return; // Бедрок не ломаем

				chunk.Blocks[bx, by, bz] = type;
				chunk.RegenerateMesh(null);
			}
		}
	}
}
