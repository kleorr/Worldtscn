using Godot;
using System;
using System.Collections.Generic;

public partial class Player : CharacterBody3D
{
	[Export] public float Speed = 5.0f;
	[Export] public float FlySpeed = 15.0f; 
	[Export] public float JumpVelocity = 4.5f;
	[Export] public float MouseSensitivity = 0.003f;

	private Camera3D _camera;
	private Node3D _highlight;
	private float _rotationX = 0f;
	private bool _isFlying = false; 

	private readonly byte[] _hotbar = new byte[] { 2, 3, 9, 7, 4, 5, 6, 10, 8 };
	private int _currentSlot = 0; 

	private CanvasLayer _uiLayer;
	private HBoxContainer _hotbarContainer;
	private TextureRect[] _slots; 
	private Label _blockNameLabel;

	private float _labelFadeTimer = 0f;
	private const float LabelDisplayTime = 1.0f;
	private const float LabelFadeTime = 0.5f;    

	private static readonly string[] BlockFullNames = { 
		"Stone", "Grass", "Dirt", "Sand", "Oak Log", "Leaves", "Oak Planks", "Obsidian", "Water Source" 
	};
	private static readonly string[] TexturePaths = {
		"res://textures/stone.png",
		"res://textures/grassblock_top.png",
		"res://textures/dirt.png",
		"res://textures/sand.png",
		"res://textures/oaklog_side.png",
		"res://textures/leaves.png",
		"res://textures/oakplanks.png",
		"res://textures/obsidian.png",
		"res://textures/water.png" 
	};

	private static readonly Dictionary<byte, StandardMaterial3D> ParticleMaterials = new Dictionary<byte, StandardMaterial3D>();

	public override void _EnterTree()
	{
		if (int.TryParse(Name, out int peerId))
		{
			SetMultiplayerAuthority(peerId);
			var sync = GetNodeOrNull<MultiplayerSynchronizer>("MultiplayerSynchronizer");
			if (sync != null) sync.SetMultiplayerAuthority(peerId);
		}
	}

	public override void _Ready()
	{
		if (Multiplayer != null)
		{
			Multiplayer.PeerDisconnected += OnPeerDisconnected;
		}
		
		AddToGroup("Players");

		bool isAuthority = Multiplayer.HasMultiplayerPeer() && IsMultiplayerAuthority();

		if (isAuthority)
		{
			if (WorldSettings.IsLoadingExistingWorld)
				GlobalPosition = WorldSettings.SavedPlayerPosition;
			else if (GlobalPosition == Vector3.Zero)
				GlobalPosition = new Vector3(0f, 70f, 0f);
		}

		if (HasNode("Camera3D")) _camera = GetNode<Camera3D>("Camera3D");
		else if (HasNode("Head/Camera3D")) _camera = GetNode<Camera3D>("Head/Camera3D");
			
		if (GetTree().CurrentScene.HasNode("BlockHighlight"))
			_highlight = GetTree().CurrentScene.GetNode<Node3D>("BlockHighlight");

		if (isAuthority)
		{
			Input.MouseMode = Input.MouseModeEnum.Captured;
			if (_camera != null) _camera.Current = true;

			CreateGraphicHotbarUI();
			UpdateUI();
		}
		else
		{
			if (_camera != null) _camera.Current = false;
		}
	}

	public override void _ExitTree()
	{
		if (Multiplayer != null) Multiplayer.PeerDisconnected -= OnPeerDisconnected;
	}

	private void OnPeerDisconnected(long id)
	{
		if (Multiplayer.HasMultiplayerPeer() && GetMultiplayerAuthority() == id)
		{
			QueueFree();
		}
	}

	private void CreateGraphicHotbarUI()
	{
		_uiLayer = new CanvasLayer();
		AddChild(_uiLayer);

		_hotbarContainer = new HBoxContainer();
		_hotbarContainer.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
		_hotbarContainer.GrowHorizontal = Control.GrowDirection.Both;
		_hotbarContainer.GrowVertical = Control.GrowDirection.Begin;
		
		float containerWidth = _hotbar.Length * 55f + 10f;
		_hotbarContainer.CustomMinimumSize = new Vector2(containerWidth, 55);
		_hotbarContainer.OffsetLeft = -(containerWidth / 2f);
		_hotbarContainer.OffsetRight = (containerWidth / 2f);
		_hotbarContainer.OffsetTop = -65;  
		_hotbarContainer.OffsetBottom = -10;
		_uiLayer.AddChild(_hotbarContainer);

		_blockNameLabel = new Label();
		_blockNameLabel.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
		_blockNameLabel.OffsetLeft = -150;
		_blockNameLabel.OffsetRight = 150;
		_blockNameLabel.OffsetTop = -100;   
		_blockNameLabel.OffsetBottom = -70;
		_blockNameLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_uiLayer.AddChild(_blockNameLabel);

		_slots = new TextureRect[_hotbar.Length];

		for (int i = 0; i < _hotbar.Length; i++)
		{
			var slotBackground = new ColorRect
			{
				CustomMinimumSize = new Vector2(50, 50),
				Color = new Color(0f, 0f, 0f, 0.4f)
			};
			_hotbarContainer.AddChild(slotBackground);

			_slots[i] = new TextureRect
			{
				CustomMinimumSize = new Vector2(40, 40),
				ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
				TextureFilter = CanvasItem.TextureFilterEnum.Nearest
			};
			_slots[i].SetAnchorsPreset(Control.LayoutPreset.Center);
			_slots[i].GrowHorizontal = Control.GrowDirection.Both;
			_slots[i].GrowVertical = Control.GrowDirection.Both;

			if (ResourceLoader.Exists(TexturePaths[i]))
			{
				_slots[i].Texture = GD.Load<Texture2D>(TexturePaths[i]);
			}

			slotBackground.AddChild(_slots[i]);
		}
	}

	private void UpdateUI()
	{
		if (_slots == null) return;

		for (int i = 0; i < _slots.Length; i++)
		{
			if (_slots[i] != null)
			{
				var bg = _slots[i].GetParent<ColorRect>();
				bg.Color = (i == _currentSlot) ? new Color(0.3f, 0.3f, 0.3f, 0.7f) : new Color(0f, 0f, 0f, 0.4f);
			}
		}

		if (_blockNameLabel != null && _currentSlot < BlockFullNames.Length)
		{
			_blockNameLabel.Text = BlockFullNames[_currentSlot];
			_blockNameLabel.Modulate = new Color(1, 1, 1, 1); 
			_labelFadeTimer = LabelDisplayTime + LabelFadeTime;
		}
	}

	private void UpdateTextVisibility(float delta)
	{
		if (_labelFadeTimer > 0f)
		{
			_labelFadeTimer -= delta;
			if (_labelFadeTimer <= LabelFadeTime)
			{
				float alpha = Mathf.Max(0f, _labelFadeTimer / LabelFadeTime);
				_blockNameLabel.Modulate = new Color(1, 1, 1, alpha);
			}
		}
	}

	private bool IsInWater()
	{
		if (GetTree().CurrentScene is WorldGenerator world)
		{
			int px = Mathf.FloorToInt(GlobalPosition.X);
			int pyFeet = Mathf.FloorToInt(GlobalPosition.Y + 0.2f);
			int pz = Mathf.FloorToInt(GlobalPosition.Z);

			byte blockFeet = world.GetBlockAt(px, pyFeet, pz);
			return Chunk.IsWater(blockFeet); 
		}
		return false;
	}

	public override void _Input(InputEvent @event)
	{
		if (!Multiplayer.HasMultiplayerPeer() || !IsMultiplayerAuthority()) return;

		if (@event is InputEventKey keyEvent && keyEvent.Pressed && !keyEvent.Echo)
		{
			if (keyEvent.Keycode == Key.H)
			{
				_isFlying = !_isFlying;
				Velocity = Vector3.Zero; 
			}
		}

		if (@event is InputEventMouseMotion mouseMotion)
		{
			RotateY(-mouseMotion.Relative.X * MouseSensitivity);
			_rotationX -= mouseMotion.Relative.Y * MouseSensitivity;
			_rotationX = Mathf.Clamp(_rotationX, Mathf.DegToRad(-89f), Mathf.DegToRad(89f));
			if (_camera != null)
			{
				Vector3 camRot = _camera.Rotation;
				camRot.X = _rotationX;
				_camera.Rotation = camRot;
			}
		}

		if (@event is InputEventMouseButton mouseButton && mouseButton.Pressed)
		{
			if (mouseButton.ButtonIndex == MouseButton.Right) ModifyWorldBlock(true);
			else if (mouseButton.ButtonIndex == MouseButton.Left) ModifyWorldBlock(false);
		}

		if (Input.IsKeyPressed(Key.Key1) && _currentSlot != 0) { _currentSlot = 0; UpdateUI(); }
		else if (Input.IsKeyPressed(Key.Key2) && _currentSlot != 1) { _currentSlot = 1; UpdateUI(); }
		else if (Input.IsKeyPressed(Key.Key3) && _currentSlot != 2) { _currentSlot = 2; UpdateUI(); }
		else if (Input.IsKeyPressed(Key.Key4) && _currentSlot != 3) { _currentSlot = 3; UpdateUI(); }
		else if (Input.IsKeyPressed(Key.Key5) && _currentSlot != 4) { _currentSlot = 4; UpdateUI(); }
		else if (Input.IsKeyPressed(Key.Key6) && _currentSlot != 5) { _currentSlot = 5; UpdateUI(); }
		else if (Input.IsKeyPressed(Key.Key7) && _currentSlot != 6) { _currentSlot = 6; UpdateUI(); }
		else if (Input.IsKeyPressed(Key.Key8) && _currentSlot != 7) { _currentSlot = 7; UpdateUI(); }
		else if (Input.IsKeyPressed(Key.Key9) && _currentSlot != 8) { _currentSlot = 8; UpdateUI(); }
	}

	public override void _PhysicsProcess(double delta)
	{
		if (!Multiplayer.HasMultiplayerPeer() || !IsMultiplayerAuthority()) return;

		UpdateTextVisibility((float)delta);

		Vector3 velocity = Velocity;
		bool waterState = IsInWater();

		Vector2 inputDir = Vector2.Zero;
		if (Input.IsActionPressed("move_forward")) inputDir.Y -= 1;
		if (Input.IsActionPressed("move_backward")) inputDir.Y += 1;
		if (Input.IsActionPressed("move_left")) inputDir.X -= 1;
		if (Input.IsActionPressed("move_right")) inputDir.X += 1;
		inputDir = inputDir.Normalized();

		Vector3 direction = (Transform.Basis * new Vector3(inputDir.X, 0, inputDir.Y)).Normalized();

		if (_isFlying)
		{
			float verticalSpeed = 0f;
			if (Input.IsKeyPressed(Key.Space)) verticalSpeed = FlySpeed;
			else if (Input.IsKeyPressed(Key.Shift)) verticalSpeed = -FlySpeed;
			
			velocity.Y = verticalSpeed;

			if (direction != Vector3.Zero)
			{
				velocity.X = direction.X * FlySpeed;
				velocity.Z = direction.Z * FlySpeed;
			}
			else
			{
				velocity.X = Mathf.MoveToward(Velocity.X, 0, FlySpeed);
				velocity.Z = Mathf.MoveToward(Velocity.Z, 0, FlySpeed);
			}
		}
		else if (waterState)
		{
			velocity.Y -= 2.0f * (float)delta;
			velocity.Y = Mathf.MoveToward(velocity.Y, -1.5f, 5.0f * (float)delta);

			if (Input.IsKeyPressed(Key.Space)) 
				velocity.Y = 3.0f; 
			else if (Input.IsKeyPressed(Key.Shift)) 
				velocity.Y = -3.0f; 

			float currentWaterSpeed = Speed * 0.5f; 
			if (direction != Vector3.Zero)
			{
				velocity.X = direction.X * currentWaterSpeed;
				velocity.Z = direction.Z * currentWaterSpeed;
			}
			else
			{
				velocity.X = Mathf.MoveToward(Velocity.X, 0, currentWaterSpeed);
				velocity.Z = Mathf.MoveToward(Velocity.Z, 0, currentWaterSpeed);
			}
		}
		else
		{
			if (!IsOnFloor())
				velocity.Y -= 9.8f * (float)delta;

			if (Input.IsActionJustPressed("ui_accept") && IsOnFloor())
				velocity.Y = JumpVelocity;

			if (direction != Vector3.Zero)
			{
				velocity.X = direction.X * Speed;
				velocity.Z = direction.Z * Speed;
			}
			else
			{
				velocity.X = Mathf.MoveToward(Velocity.X, 0, Speed);
				velocity.Z = Mathf.MoveToward(Velocity.Z, 0, Speed);
			}
		}

		Velocity = velocity;
		MoveAndSlide();

		UpdateBlockHighlight();
	}

	private void UpdateBlockHighlight()
	{
		if (_camera == null || _highlight == null) return;

		Vector3 from = _camera.GlobalPosition;
		Vector3 to = from - _camera.GlobalTransform.Basis.Z * 5.0f;

		var spaceState = GetWorld3D().DirectSpaceState;
		var query = PhysicsRayQueryParameters3D.Create(from, to);
		query.Exclude = new Godot.Collections.Array<Rid> { GetRid() }; 

		var result = spaceState.IntersectRay(query);

		if (result.Count > 0)
		{
			Vector3 hitPos = (Vector3)result["position"];
			Vector3 hitNormal = (Vector3)result["normal"];
			Vector3 targetBlock = hitPos - hitNormal * 0.1f;

			int bx = Mathf.FloorToInt(targetBlock.X);
			int by = Mathf.FloorToInt(targetBlock.Y);
			int bz = Mathf.FloorToInt(targetBlock.Z);

			_highlight.Visible = true;
			_highlight.GlobalPosition = new Vector3(bx + 0.5f, by + 0.5f, bz + 0.5f);
		}
		else
		{
			_highlight.Visible = false;
		}
	}

	private void ModifyWorldBlock(bool isPlacing)
	{
		if (_camera == null) return;

		Vector3 from = _camera.GlobalPosition;
		Vector3 to = from - _camera.GlobalTransform.Basis.Z * 5.0f;

		var spaceState = GetWorld3D().DirectSpaceState;
		var query = PhysicsRayQueryParameters3D.Create(from, to);
		query.Exclude = new Godot.Collections.Array<Rid> { GetRid() };

		var result = spaceState.IntersectRay(query);

		if (result.Count > 0)
		{
			Vector3 hitPos = (Vector3)result["position"];
			Vector3 hitNormal = (Vector3)result["normal"];
			Vector3 targetBlock = hitPos - hitNormal * 0.1f;

			int bx = Mathf.FloorToInt(targetBlock.X);
			int by = Mathf.FloorToInt(targetBlock.Y);
			int bz = Mathf.FloorToInt(targetBlock.Z);

			if (isPlacing)
			{
				Vector3 placePos = targetBlock + hitNormal;
				int px = Mathf.FloorToInt(placePos.X);
				int py = Mathf.FloorToInt(placePos.Y);
				int pz = Mathf.FloorToInt(placePos.Z);

				var boxShape = new BoxShape3D { Size = new Vector3(0.98f, 0.98f, 0.98f) }; 

				var shapeQuery = new PhysicsShapeQueryParameters3D
				{
					Shape = boxShape,
					Transform = new Transform3D(Basis.Identity, new Vector3(px + 0.5f, py + 0.5f, pz + 0.5f)),
				};

				var intersections = spaceState.IntersectShape(shapeQuery);

				if (intersections.Count > 0)
				{
					return;
				}

				if (Mathf.FloorToInt(GlobalPosition.X) == px && Mathf.FloorToInt(GlobalPosition.Z) == pz && py == Mathf.FloorToInt(GlobalPosition.Y - 1f))
				{
					float topY = py + 1.0f;
					if (GlobalPosition.Y >= topY - 0.05f && GlobalPosition.Y <= topY + 0.3f)
					{
						GlobalPosition = new Vector3(GlobalPosition.X, topY, GlobalPosition.Z);
						if (Velocity.Y < 0f)
						{
							Velocity = new Vector3(Velocity.X, 0f, Velocity.Z);
						}
					}
				}

				byte blockToPlace = _hotbar[_currentSlot];

				// Ротация бревна в зависимости от грани
				if (blockToPlace == 4)
				{
					if (Mathf.Abs(hitNormal.X) > 0.5f)
						blockToPlace = 18; // Горизонтально по X
					else if (Mathf.Abs(hitNormal.Z) > 0.5f)
						blockToPlace = 19; // Горизонтально по Z
					else
						blockToPlace = 4;  // Вертикально по Y
				}
				
				if (NetworkManager.Instance != null)
				{
					NetworkManager.Instance.ChallengeBlockChange(new Vector3I(px, py, pz), blockToPlace);
				}
			}
			else
			{
				if (GetTree().CurrentScene is WorldGenerator world)
				{
					byte oldBlock = world.GetBlockAt(bx, by, bz);
					
					// Запрещаем ломать бедрок
					if (oldBlock == 1) return;

					if (oldBlock > 0 && !Chunk.IsWater(oldBlock))
					{
						SpawnBlockParticles(new Vector3(bx, by, bz), oldBlock);
					}
				}

				if (NetworkManager.Instance != null)
				{
					NetworkManager.Instance.ChallengeBlockChange(new Vector3I(bx, by, bz), 0);
				}
			}
		}
	}

	private StandardMaterial3D GetParticleMaterial(byte blockType)
	{
		if (ParticleMaterials.TryGetValue(blockType, out var cachedMat))
			return cachedMat;

		string texPath = GetTexturePathForBlock(blockType);
		if (ResourceLoader.Exists(texPath))
		{
			var mat = new StandardMaterial3D
			{
				AlbedoTexture = GD.Load<Texture2D>(texPath),
				TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
				BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
				CullMode = BaseMaterial3D.CullModeEnum.Disabled,
				AlbedoColor = new Color(1f, 1f, 1f, 1f)
			};

			ParticleMaterials[blockType] = mat;
			return mat;
		}
		return null;
	}

	private void SpawnBlockParticles(Vector3 pos, byte blockType)
	{
		var particles = new CpuParticles3D();

		GetTree().CurrentScene.AddChild(particles);
		particles.GlobalPosition = pos + new Vector3(0.5f, 0.5f, 0.5f);

		particles.Amount = 20;
		particles.Lifetime = 0.4f;
		particles.OneShot = true;
		particles.Explosiveness = 1.0f;
		
		particles.EmissionShape = CpuParticles3D.EmissionShapeEnum.Box;
		particles.EmissionBoxExtents = new Vector3(0.35f, 0.35f, 0.35f);
		
		particles.Direction = new Vector3(0, 1, 0);
		particles.Spread = 180f;
		particles.InitialVelocityMin = 2.5f;
		particles.InitialVelocityMax = 4.5f;
		particles.Gravity = new Vector3(0, -12f, 0);
		particles.ScaleAmountMin = 0.06f;
		particles.ScaleAmountMax = 0.12f;

		StandardMaterial3D mat = GetParticleMaterial(blockType);
		if (mat != null)
		{
			var quadMesh = new QuadMesh
			{
				Size = new Vector2(0.12f, 0.12f),
				Material = mat
			};
			particles.Mesh = quadMesh;
		}

		particles.Emitting = true;

		var timer = GetTree().CreateTimer(0.5f);
		timer.Timeout += () => particles.QueueFree();
	}

	private static string GetTexturePathForBlock(byte type)
	{
		return type switch
		{
			1 => "res://textures/bedrock.png",
			2 => "res://textures/stone.png",
			3 => "res://textures/grassblock_top.png",
			4 or 18 or 19 => "res://textures/oaklog_side.png",
			5 => "res://textures/leaves.png",
			6 => "res://textures/oakplanks.png",
			7 => "res://textures/sand.png",
			8 => "res://textures/water.png",
			9 => "res://textures/dirt.png",
			10 => "res://textures/obsidian.png",
			_ => "res://textures/stone.png"
		};
	}
}
