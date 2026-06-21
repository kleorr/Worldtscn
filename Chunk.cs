using Godot;
using System;
using System.Collections.Generic;

public partial class Chunk : StaticBody3D
{
	public const int Width = 16;
	public const int Height = 64;
	
	public byte[,,] Blocks = new byte[Width, Height, Width];
	
	public int ChunkX { get; set; }
	public int ChunkZ { get; set; }

	private MeshInstance3D _meshInstance;
	private CollisionShape3D _collisionShape;

	private static readonly Vector3I[] Directions = new Vector3I[]
	{
		new Vector3I(0, 0, 1),   // 0: Вперед
		new Vector3I(0, 0, -1),  // 1: Назад
		new Vector3I(1, 0, 0),   // 2: Вправо
		new Vector3I(-1, 0, 0),  // 3: Влево
		new Vector3I(0, 1, 0),   // 4: Вверх
		new Vector3I(0, -1, 0)   // 5: Вниз
	};

	public override void _Ready()
	{
		_meshInstance = new MeshInstance3D();
		_collisionShape = new CollisionShape3D();
		AddChild(_meshInstance);
		AddChild(_collisionShape);
	}

	public void RegenerateMesh(Dictionary<byte, StandardMaterial3D> materials)
	{
		var st = new SurfaceTool();
		st.Begin(Mesh.PrimitiveType.Triangles);

		for (int x = 0; x < Width; x++)
		{
			for (int y = 0; y < Height; y++)
			{
				for (int z = 0; z < Width; z++)
				{
					byte blockType = Blocks[x, y, z];
					if (blockType == 0) continue;

					for (int i = 0; i < 6; i++)
					{
						int nx = x + Directions[i].X;
						int ny = y + Directions[i].Y;
						int nz = z + Directions[i].Z;

						if (IsAir(nx, ny, nz))
						{
							AddFace(st, x, y, z, i, blockType);
						}
					}
				}
			}
		}

		ArrayMesh mesh = st.Commit();
		_meshInstance.Mesh = mesh;

		// ЗАГРУЗКА И СКЛЕЙКА ТЕКСТУР В РЕЖИМЕ РЕАЛЬНОГО ВРЕМЕНИ
		var globalMat = new StandardMaterial3D();

		string[] paths = new string[]
		{
			"res://textures/bedrock.png",         // 0
			"res://textures/stone.png",           // 1
			"res://textures/dirt.png",            // 2
			"res://textures/grassblock_top.png",  // 3
			"res://textures/grassblock_side.png", // 4
			"res://textures/oaklog_side.png",     // 5
			"res://textures/oaklog_top.png",      // 6
			"res://textures/leaves.png",          // 7
			"res://textures/oakplanks.png",       // 8
			"res://textures/obsidian.png"         // 9
		};

		// Создаем пустой большой холст в памяти (16 пикселей в высоту, 160 в ширину для 10 текстур)
		Image atlasImage = Image.CreateEmpty(160, 16, false, Image.Format.Rgba8);

		for (int i = 0; i < paths.Length; i++)
		{
			Image img = GD.Load<Texture2D>(paths[i]).GetImage();
			// Копируем маленькую текстуру на нужное место в общем атласе
			atlasImage.BlitRect(img, new Rect2I(0, 0, 16, 16), new Vector2I(i * 16, 0));
		}

		ImageTexture texture = ImageTexture.CreateFromImage(atlasImage);

		globalMat.AlbedoTexture = texture;
		globalMat.TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest; 
		globalMat.Roughness = 1.0f;

		_meshInstance.MaterialOverride = globalMat;

		if (mesh.GetSurfaceCount() > 0)
		{
			_collisionShape.Shape = mesh.CreateTrimeshShape();
		}
		else
		{
			_collisionShape.Shape = null;
		}
	}

	private bool IsAir(int x, int y, int z)
	{
		if (x < 0 || x >= Width || y < 0 || y >= Height || z < 0 || z >= Width)
			return true;
		
		return Blocks[x, y, z] == 0;
	}

	private void AddFace(SurfaceTool st, int x, int y, int z, int direction, byte blockType)
	{
		int textureSlot = 0;

		switch (blockType)
		{
			case 1: textureSlot = 0; break; // Бедрок
			case 2: textureSlot = 1; break; // Камень
			case 3: // Трава
				if (direction == 4)      textureSlot = 3; // Верх
				else if (direction == 5) textureSlot = 2; // Низ
				else                     textureSlot = 4; // Бока
				break;
			case 4: // Бревно
				if (direction == 4 || direction == 5) textureSlot = 6; // Верх/Низ
				else                                  textureSlot = 5; // Бока
				break;
			case 5: textureSlot = 7; break; // Листва
			case 6: textureSlot = 8; break; // Доски
			case 7: textureSlot = 9; break; // Обсидиан
		}

		// Всего у нас 10 текстур, значит шаг одной картинки в UV-координатах = 1 / 10 = 0.1
		float step = 0.1f;
		float uStart = textureSlot * step;
		float uEnd = uStart + step;

		Vector2 uv00 = new Vector2(uStart, 1f);
		Vector2 uv10 = new Vector2(uEnd, 1f);
		Vector2 uv11 = new Vector2(uEnd, 0f);
		Vector2 uv01 = new Vector2(uStart, 0f);

		Vector3[] v = {
			new Vector3(x, y, z),          new Vector3(x + 1, y, z),
			new Vector3(x + 1, y + 1, z),  new Vector3(x, y + 1, z),
			new Vector3(x, y, z + 1),      new Vector3(x + 1, y, z + 1),
			new Vector3(x + 1, y + 1, z + 1), new Vector3(x, y + 1, z + 1)
		};

		int[][] faces = {
			new int[] { 5, 4, 7, 6 }, 
			new int[] { 0, 1, 2, 3 }, 
			new int[] { 1, 5, 6, 2 }, 
			new int[] { 4, 0, 3, 7 }, 
			new int[] { 3, 2, 6, 7 }, 
			new int[] { 1, 0, 4, 5 }  
		};

		int[] f = faces[direction];

		st.SetUV(uv10); st.AddVertex(v[f[0]]);
		st.SetUV(uv00); st.AddVertex(v[f[1]]);
		st.SetUV(uv01); st.AddVertex(v[f[2]]);

		st.SetUV(uv10); st.AddVertex(v[f[0]]);
		st.SetUV(uv01); st.AddVertex(v[f[2]]);
		st.SetUV(uv11); st.AddVertex(v[f[3]]);
	}
}
