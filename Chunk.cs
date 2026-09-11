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

	private MeshInstance3D _opaqueMeshInstance; 
	private MeshInstance3D _waterMeshInstance;  
	private CollisionShape3D _collisionShape;
	private WorldGenerator _worldGenerator;

	private static StandardMaterial3D _opaqueMaterial;
	private static StandardMaterial3D _waterMaterial;

	private static readonly Vector3I[] Directions = new Vector3I[]
	{
		new Vector3I(0, 0, 1),   // 0: Вперед
		new Vector3I(0, 0, -1),  // 1: Назад
		new Vector3I(1, 0, 0),   // 2: Вправо
		new Vector3I(-1, 0, 0),  // 3: Влево
		new Vector3I(0, 1, 0),   // 4: Вверх
		new Vector3I(0, -1, 0)   // 5: Вниз
	};

	private static readonly int[][] FaceIndices = new int[][]
	{
		new int[] { 5, 4, 7, 6 }, 
		new int[] { 0, 1, 2, 3 }, 
		new int[] { 1, 5, 6, 2 }, 
		new int[] { 4, 0, 3, 7 }, 
		new int[] { 3, 2, 6, 7 }, 
		new int[] { 1, 0, 4, 5 }  
	};

	public class MeshData
	{
		public List<Vector3> OpaqueVertices = new List<Vector3>();
		public List<Vector2> OpaqueUVs = new List<Vector2>();
		public List<int> OpaqueIndices = new List<int>();

		public List<Vector3> WaterVertices = new List<Vector3>();
		public List<Vector2> WaterUVs = new List<Vector2>();
		public List<int> WaterIndices = new List<int>();

		public List<Vector3> CollisionFaces = new List<Vector3>();
	}

	public override void _Ready()
	{
		_opaqueMeshInstance = new MeshInstance3D();
		_waterMeshInstance = new MeshInstance3D();
		_collisionShape = new CollisionShape3D();
		
		AddChild(_opaqueMeshInstance);
		AddChild(_waterMeshInstance);
		AddChild(_collisionShape);

		InitializeGlobalMaterials();
	}

	public static bool IsWater(byte blockType)
	{
		return blockType == 8 || (blockType >= 11 && blockType <= 17);
	}

	private static void InitializeGlobalMaterials()
	{
		if (_opaqueMaterial != null && _waterMaterial != null) return;

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
			"res://textures/sand.png",            // 9
			"res://textures/obsidian.png",        // 10
			"res://textures/water.png"            // 11
		};

		Image atlasImage = Image.CreateEmpty(paths.Length * 16, 16, false, Image.Format.Rgba8);
		for (int i = 0; i < paths.Length; i++)
		{
			if (ResourceLoader.Exists(paths[i]))
			{
				Image img = GD.Load<Texture2D>(paths[i]).GetImage();
				atlasImage.BlitRect(img, new Rect2I(0, 0, 16, 16), new Vector2I(i * 16, 0));
			}
		}
		ImageTexture texture = ImageTexture.CreateFromImage(atlasImage);

		_opaqueMaterial = new StandardMaterial3D
		{
			AlbedoTexture = texture,
			TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
			Roughness = 1.0f,
			Transparency = BaseMaterial3D.TransparencyEnum.Disabled
		};

		_waterMaterial = new StandardMaterial3D
		{
			AlbedoTexture = texture,
			TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
			Roughness = 0.1f,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			AlbedoColor = new Color(1f, 1f, 1f, 0.65f),
			DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.OpaqueOnly,
			RenderPriority = 1,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled
		};
	}

	public MeshData BuildMeshData(Func<int, int, int, byte> getBlockFunc)
	{
		MeshData data = new MeshData();

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
						if (ShouldRenderFace(x, y, z, blockType, i, getBlockFunc))
						{
							AddFaceToData(data, x, y, z, i, blockType, getBlockFunc);
						}
					}
				}
			}
		}

		return data;
	}

	public void ApplyMeshData(MeshData data)
	{
		InitializeGlobalMaterials();

		if (data.OpaqueVertices.Count > 0)
		{
			var arrays = new Godot.Collections.Array();
			arrays.Resize((int)Mesh.ArrayType.Max);
			arrays[(int)Mesh.ArrayType.Vertex] = data.OpaqueVertices.ToArray();
			arrays[(int)Mesh.ArrayType.TexUV] = data.OpaqueUVs.ToArray();
			arrays[(int)Mesh.ArrayType.Index] = data.OpaqueIndices.ToArray();

			var mesh = new ArrayMesh();
			mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);

			_opaqueMeshInstance.Mesh = mesh;
			_opaqueMeshInstance.MaterialOverride = _opaqueMaterial;

			var faces = data.CollisionFaces.ToArray();
			if (_collisionShape.Shape is ConcavePolygonShape3D concaveShape)
			{
				concaveShape.Data = faces;
			}
			else
			{
				_collisionShape.Shape = new ConcavePolygonShape3D { Data = faces };
			}
		}
		else
		{
			_opaqueMeshInstance.Mesh = null;
			_collisionShape.Shape = null;
		}

		if (data.WaterVertices.Count > 0)
		{
			var arrays = new Godot.Collections.Array();
			arrays.Resize((int)Mesh.ArrayType.Max);
			arrays[(int)Mesh.ArrayType.Vertex] = data.WaterVertices.ToArray();
			arrays[(int)Mesh.ArrayType.TexUV] = data.WaterUVs.ToArray();
			arrays[(int)Mesh.ArrayType.Index] = data.WaterIndices.ToArray();

			var waterMesh = new ArrayMesh();
			waterMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);

			_waterMeshInstance.Mesh = waterMesh;
			_waterMeshInstance.MaterialOverride = _waterMaterial;
		}
		else
		{
			_waterMeshInstance.Mesh = null;
		}
	}

	public void RegenerateMesh(Dictionary<byte, StandardMaterial3D> materials = null)
	{
		_worldGenerator ??= GetParent<WorldGenerator>();
		Func<int, int, int, byte> getBlockFunc = (gx, gy, gz) =>
		{
			int bx = gx - ChunkX * Width;
			int by = gy;
			int bz = gz - ChunkZ * Width;
			if (bx >= 0 && bx < Width && by >= 0 && by < Height && bz >= 0 && bz < Width)
				return Blocks[bx, by, bz];
			return _worldGenerator != null ? _worldGenerator.GetBlockAt(gx, gy, gz) : (byte)0;
		};

		MeshData data = BuildMeshData(getBlockFunc);
		ApplyMeshData(data);
	}

	private byte GetNeighborBlock(int nx, int ny, int nz, Func<int, int, int, byte> getBlockFunc)
	{
		if (ny < 0 || ny >= Height) return 0;
		if (nx >= 0 && nx < Width && nz >= 0 && nz < Width)
			return Blocks[nx, ny, nz];

		int globalX = ChunkX * Width + nx;
		int globalY = ny;
		int globalZ = ChunkZ * Width + nz;

		return getBlockFunc(globalX, globalY, globalZ);
	}

	private float GetWaterHeight(int x, int y, int z, byte blockType, Func<int, int, int, byte> getBlockFunc)
	{
		if (IsWater(GetNeighborBlock(x, y + 1, z, getBlockFunc)))
		{
			return 1.0f;
		}

		if (blockType == 8) return 0.9f;
		if (blockType >= 11 && blockType <= 17)
		{
			int level = 18 - blockType;
			return (level / 8.0f) * 0.9f;
		}
		return 1.0f;
	}

	private bool ShouldRenderFace(int x, int y, int z, byte currentBlock, int direction, Func<int, int, int, byte> getBlockFunc)
	{
		int nx = x + Directions[direction].X;
		int ny = y + Directions[direction].Y;
		int nz = z + Directions[direction].Z;

		if (ny < 0 || ny >= Height) return true;

		byte neighborBlock = GetNeighborBlock(nx, ny, nz, getBlockFunc);
		bool isCurrentWater = IsWater(currentBlock);
		bool isNeighborWater = IsWater(neighborBlock);

		if (isCurrentWater)
		{
			if (direction == 4) return !isNeighborWater;
			if (direction == 5) return neighborBlock == 0;

			if (isNeighborWater)
			{
				float currentH = GetWaterHeight(x, y, z, currentBlock, getBlockFunc);
				float neighborH = GetWaterHeight(nx, ny, nz, neighborBlock, getBlockFunc);
				return currentH > neighborH;
			}

			return neighborBlock == 0;
		}

		return neighborBlock == 0 || isNeighborWater;
	}

	private void AddFaceToData(MeshData data, int x, int y, int z, int direction, byte blockType, Func<int, int, int, byte> getBlockFunc)
	{
		int textureSlot = 0;
		bool rotateUv = false;

		if (IsWater(blockType))
		{
			textureSlot = 11;
		}
		else
		{
			switch (blockType)
			{
				case 1: textureSlot = 0; break;
				case 2: textureSlot = 1; break;
				case 3:
					textureSlot = (direction == 4) ? 3 : ((direction == 5) ? 2 : 4);
					break;
				case 4: // Вертикальное бревно (Y)
					textureSlot = (direction == 4 || direction == 5) ? 6 : 5;
					break;
				case 18: // Горизонтальное бревно (X)
					textureSlot = (direction == 2 || direction == 3) ? 6 : 5;
					if (direction == 0 || direction == 1 || direction == 4 || direction == 5)
						rotateUv = true; // Поворачиваем текстуру коры на 90 градусов вдоль оси X
					break;
				case 19: // Горизонтальное бревно (Z)
					textureSlot = (direction == 0 || direction == 1) ? 6 : 5;
					if (direction == 2 || direction == 3)
						rotateUv = true; // Поворачиваем текстуру коры на 90 градусов вдоль оси Z
					break;
				case 5: textureSlot = 7; break;
				case 6: textureSlot = 8; break;
				case 7: textureSlot = 9; break;
				case 9: textureSlot = 2; break;
				case 10: textureSlot = 10; break;
			}
		}

		float step = 1f / 12f;
		float uStart = textureSlot * step;
		float uEnd = uStart + step;

		Vector2 uv00 = new Vector2(uStart, 1f);
		Vector2 uv10 = new Vector2(uEnd, 1f);
		Vector2 uv11 = new Vector2(uEnd, 0f);
		Vector2 uv01 = new Vector2(uStart, 0f);

		Vector2 p0 = uv10;
		Vector2 p1 = uv00;
		Vector2 p2 = uv01;
		Vector2 p3 = uv11;

		if (rotateUv)
		{
			p0 = uv11;
			p1 = uv10;
			p2 = uv00;
			p3 = uv01;
		}

		float waterH = IsWater(blockType) ? GetWaterHeight(x, y, z, blockType, getBlockFunc) : 1.0f;
		float minWaterH = 0.0f;

		if (IsWater(blockType) && direction >= 0 && direction <= 3)
		{
			int nx = x + Directions[direction].X;
			int ny = y + Directions[direction].Y;
			int nz = z + Directions[direction].Z;
			byte neighborBlock = GetNeighborBlock(nx, ny, nz, getBlockFunc);
			if (IsWater(neighborBlock))
			{
				minWaterH = GetWaterHeight(nx, ny, nz, neighborBlock, getBlockFunc);
			}
		}

		Span<Vector3> v = stackalloc Vector3[8];
		v[0] = new Vector3(x, y + minWaterH, z);
		v[1] = new Vector3(x + 1, y + minWaterH, z);
		v[2] = new Vector3(x + 1, y + waterH, z);
		v[3] = new Vector3(x, y + waterH, z);
		v[4] = new Vector3(x, y + minWaterH, z + 1);
		v[5] = new Vector3(x + 1, y + minWaterH, z + 1);
		v[6] = new Vector3(x + 1, y + waterH, z + 1);
		v[7] = new Vector3(x, y + waterH, z + 1);

		int[] f = FaceIndices[direction];

		bool water = IsWater(blockType);
		List<Vector3> verts = water ? data.WaterVertices : data.OpaqueVertices;
		List<Vector2> uvs = water ? data.WaterUVs : data.OpaqueUVs;
		List<int> indices = water ? data.WaterIndices : data.OpaqueIndices;

		int baseIndex = verts.Count;

		verts.Add(v[f[0]]); uvs.Add(p0);
		verts.Add(v[f[1]]); uvs.Add(p1);
		verts.Add(v[f[2]]); uvs.Add(p2);
		verts.Add(v[f[3]]); uvs.Add(p3);

		indices.Add(baseIndex + 0);
		indices.Add(baseIndex + 1);
		indices.Add(baseIndex + 2);
		indices.Add(baseIndex + 0);
		indices.Add(baseIndex + 2);
		indices.Add(baseIndex + 3);

		if (!water)
		{
			data.CollisionFaces.Add(v[f[0]]);
			data.CollisionFaces.Add(v[f[1]]);
			data.CollisionFaces.Add(v[f[2]]);
			data.CollisionFaces.Add(v[f[0]]);
			data.CollisionFaces.Add(v[f[2]]);
			data.CollisionFaces.Add(v[f[3]]);
		}
	}
}
