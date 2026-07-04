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

	public override void _Ready()
	{
		_opaqueMeshInstance = new MeshInstance3D();
		_waterMeshInstance = new MeshInstance3D();
		_collisionShape = new CollisionShape3D();
		
		AddChild(_opaqueMeshInstance);
		AddChild(_waterMeshInstance);
		AddChild(_collisionShape);
	}

	private static void InitializeGlobalMaterials()
	{
		if (_opaqueMaterial != null && _waterMaterial != null) return;

		string[] paths = new string[]
		{
			"res://textures/bedrock.png",         
			"res://textures/stone.png",           
			"res://textures/dirt.png",            
			"res://textures/grassblock_top.png",  
			"res://textures/grassblock_side.png", 
			"res://textures/oaklog_side.png",     
			"res://textures/oaklog_top.png",      
			"res://textures/leaves.png",          
			"res://textures/oakplanks.png",       
			"res://textures/obsidian.png",        
			"res://textures/water.png"            
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

		_opaqueMaterial = new StandardMaterial3D();
		_opaqueMaterial.AlbedoTexture = texture;
		_opaqueMaterial.TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest;
		_opaqueMaterial.Roughness = 1.0f;
		_opaqueMaterial.Transparency = BaseMaterial3D.TransparencyEnum.Disabled;

		_waterMaterial = new StandardMaterial3D();
		_waterMaterial.AlbedoTexture = texture;
		_waterMaterial.TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest;
		_waterMaterial.Roughness = 0.2f; 
		_waterMaterial.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
		_waterMaterial.AlbedoColor = new Color(1f, 1f, 1f, 0.65f); 
	}

	public void RegenerateMesh(Dictionary<byte, StandardMaterial3D> materials)
	{
		InitializeGlobalMaterials();

		var stOpaque = new SurfaceTool();
		stOpaque.Begin(Mesh.PrimitiveType.Triangles);

		var stWater = new SurfaceTool();
		stWater.Begin(Mesh.PrimitiveType.Triangles);

		bool hasOpaqueGeometry = false;
		bool hasWaterGeometry = false;

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
						if (ShouldRenderFace(x, y, z, blockType, i))
						{
							if (blockType == 8) 
							{
								AddFace(stWater, x, y, z, i, blockType);
								hasWaterGeometry = true;
							}
							else 
							{
								AddFace(stOpaque, x, y, z, i, blockType);
								hasOpaqueGeometry = true;
							}
						}
					}
				}
			}
		}

		if (hasOpaqueGeometry)
		{
			ArrayMesh opaqueMesh = stOpaque.Commit();
			_opaqueMeshInstance.Mesh = opaqueMesh;
			_opaqueMeshInstance.MaterialOverride = _opaqueMaterial;
			_collisionShape.Shape = opaqueMesh.CreateTrimeshShape();
		}
		else
		{
			_opaqueMeshInstance.Mesh = null;
			_collisionShape.Shape = null;
		}

		if (hasWaterGeometry)
		{
			_waterMeshInstance.Mesh = stWater.Commit();
			_waterMeshInstance.MaterialOverride = _waterMaterial;
		}
		else
		{
			_waterMeshInstance.Mesh = null;
		}
	}

	private bool ShouldRenderFace(int x, int y, int z, byte currentBlock, int direction)
	{
		int nx = x + Directions[direction].X;
		int ny = y + Directions[direction].Y;
		int nz = z + Directions[direction].Z;

		if (ny < 0 || ny >= Height) return true;

		byte neighborBlock = 0;

		// Фикс барьера: Если выходим за рамки чанка, честно спрашиваем блок у WorldGenerator
		if (nx < 0 || nx >= Width || nz < 0 || nz >= Width)
		{
			int globalX = ChunkX * Width + nx;
			int globalY = ny;
			int globalZ = ChunkZ * Width + nz;

			var generator = GetParent<WorldGenerator>();
			if (generator != null)
			{
				neighborBlock = generator.GetBlockAt(globalX, globalY, globalZ);
			}
			else
			{
				return true;
			}
		}
		else
		{
			neighborBlock = Blocks[nx, ny, nz];
		}

		if (neighborBlock == 0) return true; 

		if (currentBlock == 8)
		{
			// Вода скрывает грань, если рядом тоже вода (включая воду в соседнем чанке!)
			return neighborBlock == 0;
		}

		if (neighborBlock == 8) return true;

		return false; 
	}

	private void AddFace(SurfaceTool st, int x, int y, int z, int direction, byte blockType)
	{
		int textureSlot = 0;

		switch (blockType)
		{
			case 1: textureSlot = 0; break; 
			case 2: textureSlot = 1; break; 
			case 3: 
				if (direction == 4)      textureSlot = 3; 
				else if (direction == 5) textureSlot = 2; 
				else                     textureSlot = 4; 
				break;
			case 4: 
				if (direction == 4 || direction == 5) textureSlot = 6;
				else                                  textureSlot = 5;
				break;
			case 5: textureSlot = 7; break;  
			case 6: textureSlot = 8; break;  
			case 7: textureSlot = 9; break;  
			case 8: textureSlot = 10; break; 
		}

		float step = 1f / 11f; 
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
