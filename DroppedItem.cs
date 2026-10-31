using Godot;
using System;

public partial class DroppedItem : Node3D
{
	public byte BlockType { get; set; } = 0;
	public int Count { get; set; } = 1;
	public bool IsBeingPickedUp { get; set; } = false;

	private MeshInstance3D _meshInstance;
	private Vector3 _velocity;
	private float _lifeTime = 0f;
	private float _pickupDelay = 0.5f; 
	private float _baseY;
	private bool _onGround = false;

	public static DroppedItem CreateItemInstance(string nodeName, Vector3 position, Vector3 velocity, byte blockType, int count)
	{
		if (blockType == 0 || Chunk.IsWater(blockType)) return null;

		if (blockType == 18 || blockType == 19) blockType = 4;
		if (blockType == 3) blockType = 9;

		var item = new DroppedItem
		{
			Name = nodeName,
			BlockType = blockType,
			Count = count,
			Position = position,
			_velocity = velocity,
			_pickupDelay = 0.6f
		};
		return item;
	}

	public override void _Ready()
	{
		AddToGroup("DroppedItems");

		_meshInstance = new MeshInstance3D
		{
			Mesh = new BoxMesh { Size = new Vector3(0.28f, 0.28f, 0.28f) }
		};

		string texPath = Player.GetTexturePathForBlock(BlockType);
		if (ResourceLoader.Exists(texPath))
		{
			var mat = new StandardMaterial3D
			{
				AlbedoTexture = GD.Load<Texture2D>(texPath),
				TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
				Uv1Scale = new Vector3(3, 2, 1),
				ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded
			};
			_meshInstance.MaterialOverride = mat;
		}

		AddChild(_meshInstance);

		if (_velocity == Vector3.Zero)
		{
			var rng = new RandomNumberGenerator();
			rng.Randomize();
			_velocity = new Vector3(
				rng.RandfRange(-1.0f, 1.0f),
				rng.RandfRange(2.5f, 3.8f),
				rng.RandfRange(-1.0f, 1.0f)
			);
		}
	}

	public override void _PhysicsProcess(double delta)
	{
		float dt = (float)delta;
		_lifeTime += dt;
		if (_pickupDelay > 0f) _pickupDelay -= dt;

		if (_lifeTime > 300f)
		{
			QueueFree();
			return;
		}

		WorldGenerator world = GetTree().CurrentScene as WorldGenerator;

		if (!_onGround)
		{
			_velocity.Y -= 12f * dt;
			Position += _velocity * dt;

			if (world != null)
			{
				int bx = Mathf.FloorToInt(Position.X);
				int by = Mathf.FloorToInt(Position.Y);
				int bz = Mathf.FloorToInt(Position.Z);

				byte blockUnder = world.GetBlockAt(bx, by, bz);
				if (blockUnder != 0 && !Chunk.IsWater(blockUnder))
				{
					_onGround = true;
					_velocity = Vector3.Zero;
					_baseY = by + 1.15f;
					Position = new Vector3(Position.X, _baseY, Position.Z);
				}
			}
		}
		else
		{
			if (_meshInstance != null)
			{
				_meshInstance.Position = new Vector3(0, Mathf.Sin(_lifeTime * 4.0f) * 0.04f, 0);
			}
		}

		if (_meshInstance != null)
		{
			_meshInstance.RotateY(2.2f * dt);
		}

		// Проверка подбора: только локальный игрок инициирует сетевой запрос подбора
		if (_pickupDelay <= 0f && !IsBeingPickedUp)
		{
			var players = GetTree().GetNodesInGroup("Players");
			foreach (Node p in players)
			{
				if (p is Player player && player.IsMultiplayerAuthority())
				{
					float dist = Position.DistanceTo(player.GlobalPosition + Vector3.Up * 0.5f);

					if (dist < 2.0f)
					{
						Vector3 dir = (player.GlobalPosition + Vector3.Up * 0.5f - Position).Normalized();
						Position += dir * 6.5f * dt;

						if (dist < 0.65f)
						{
							if (player.CanPickupItem(BlockType, Count))
							{
								IsBeingPickedUp = true;
								if (NetworkManager.Instance != null)
								{
									NetworkManager.Instance.ChallengePickupItem(Name, BlockType, Count);
								}
								return;
							}
						}
					}
				}
			}
		}
	}
}
