using Godot;
using System;

public partial class Player : CharacterBody3D
{
	[Export] public float Speed = 5.0f;
	[Export] public float JumpVelocity = 4.5f;
	[Export] public float MouseSensitivity = 0.003f;

	private Camera3D _camera;
	private Node3D _highlight;
	private float _rotationX = 0f;

	private byte[] _hotbar = new byte[] { 2, 3, 4, 5, 6, 7 };
	private int _currentSlot = 0; 

	private CanvasLayer _uiLayer;
	private HBoxContainer _hotbarContainer;
	private TextureRect[] _slots = new TextureRect[6];
	private Label _blockNameLabel;

	// Переменные для красивого затухания текста (Майнкрафт-эффект)
	private float _labelFadeTimer = 0f;
	private const float LabelDisplayTime = 1.0f; 
	private const float LabelFadeTime = 0.5f;    

	private string[] _blockFullNames = { "Stone", "Grass", "Oak Log", "Leaves", "Oak Planks", "Obsidian" };
	private string[] _texturePaths = {
		"res://textures/stone.png",
		"res://textures/grassblock_top.png",
		"res://textures/oaklog_side.png",
		"res://textures/leaves.png",
		"res://textures/oakplanks.png",
		"res://textures/obsidian.png"
	};

	// Точный фикс сетевой ошибки движка. Выставляем ID в момент входа в дерево,
	// чтобы MultiplayerSynchronizer на клиенте сразу знал своего владельца.
	public override void _EnterTree()
	{
		if (int.TryParse(Name, out int peerId))
		{
			SetMultiplayerAuthority(peerId);

			var sync = GetNodeOrNull<MultiplayerSynchronizer>("MultiplayerSynchronizer");
			if (sync != null)
			{
				sync.SetMultiplayerAuthority(peerId);
			}
		}
	}

	public override void _Ready()
	{
		if (GlobalPosition == Vector3.Zero)
		{
			GlobalPosition = new Vector3(256f, 69f, 256f);
		}
		if (HasNode("Camera3D")) _camera = GetNode<Camera3D>("Camera3D");
		else if (HasNode("Head/Camera3D")) _camera = GetNode<Camera3D>("Head/Camera3D");
			
		if (GetTree().CurrentScene.HasNode("BlockHighlight"))
			_highlight = GetTree().CurrentScene.GetNode<Node3D>("BlockHighlight");

		if (IsMultiplayerAuthority())
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

	private void CreateGraphicHotbarUI()
	{
		_uiLayer = new CanvasLayer();
		AddChild(_uiLayer);

		_hotbarContainer = new HBoxContainer();
		_hotbarContainer.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
		_hotbarContainer.GrowVertical = Control.GrowDirection.Begin;
		
		_hotbarContainer.CustomMinimumSize = new Vector2(330, 50);
		_hotbarContainer.OffsetLeft = -165;
		_hotbarContainer.OffsetRight = 165;
		_hotbarContainer.OffsetTop = -60;  
		_hotbarContainer.OffsetBottom = -10;
		_uiLayer.AddChild(_hotbarContainer);

		_blockNameLabel = new Label();
		_blockNameLabel.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
		_blockNameLabel.OffsetLeft = -150;
		_blockNameLabel.OffsetRight = 150;
		_blockNameLabel.OffsetTop = -95;   
		_blockNameLabel.OffsetBottom = -65;
		_blockNameLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_uiLayer.AddChild(_blockNameLabel);

		for (int i = 0; i < 6; i++)
		{
			var slotBackground = new ColorRect();
			slotBackground.CustomMinimumSize = new Vector2(50, 50);
			slotBackground.Color = new Color(0f, 0f, 0f, 0.4f); 
			_hotbarContainer.AddChild(slotBackground);

			_slots[i] = new TextureRect();
			_slots[i].CustomMinimumSize = new Vector2(40, 40);
			_slots[i].SetAnchorsPreset(Control.LayoutPreset.Center);
			_slots[i].GrowHorizontal = Control.GrowDirection.Both;
			_slots[i].GrowVertical = Control.GrowDirection.Both;
			_slots[i].ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
			_slots[i].TextureFilter = CanvasItem.TextureFilterEnum.Nearest;

			if (ResourceLoader.Exists(_texturePaths[i]))
			{
				_slots[i].Texture = GD.Load<Texture2D>(_texturePaths[i]);
			}

			slotBackground.AddChild(_slots[i]);
		}
	}

	private void UpdateUI()
	{
		for (int i = 0; i < 6; i++)
		{
			if (_slots[i] != null)
			{
				var bg = _slots[i].GetParent<ColorRect>();
				bg.Color = (i == _currentSlot) ? new Color(0.3f, 0.3f, 0.3f, 0.7f) : new Color(0f, 0f, 0f, 0.4f);
			}
		}

		if (_blockNameLabel != null)
		{
			_blockNameLabel.Text = _blockFullNames[_currentSlot];
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

	public override void _Input(InputEvent @event)
	{
		if (!IsMultiplayerAuthority()) return;

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
			if (mouseButton.ButtonIndex == MouseButton.Right)
			{
				ModifyWorldBlock(true);
			}
			else if (mouseButton.ButtonIndex == MouseButton.Left)
			{
				ModifyWorldBlock(false);
			}
		}

		if (Input.IsKeyPressed(Key.Key1) && _currentSlot != 0) { _currentSlot = 0; UpdateUI(); }
		if (Input.IsKeyPressed(Key.Key2) && _currentSlot != 1) { _currentSlot = 1; UpdateUI(); }
		if (Input.IsKeyPressed(Key.Key3) && _currentSlot != 2) { _currentSlot = 2; UpdateUI(); }
		if (Input.IsKeyPressed(Key.Key4) && _currentSlot != 3) { _currentSlot = 3; UpdateUI(); }
		if (Input.IsKeyPressed(Key.Key5) && _currentSlot != 4) { _currentSlot = 4; UpdateUI(); }
		if (Input.IsKeyPressed(Key.Key6) && _currentSlot != 5) { _currentSlot = 5; UpdateUI(); }
	}

	public override void _PhysicsProcess(double delta)
	{
		if (IsMultiplayerAuthority())
		{
			UpdateTextVisibility((float)delta);
		}

		if (!IsMultiplayerAuthority()) return;

		Vector3 velocity = Velocity;

		if (!IsOnFloor())
			velocity.Y -= 9.8f * (float)delta;

		if (Input.IsActionJustPressed("ui_accept") && IsOnFloor())
			velocity.Y = JumpVelocity;

		Vector2 inputDir = Vector2.Zero;
		if (Input.IsActionPressed("move_forward")) inputDir.Y -= 1;
		if (Input.IsActionPressed("move_backward")) inputDir.Y += 1;
		if (Input.IsActionPressed("move_left")) inputDir.X -= 1;
		if (Input.IsActionPressed("move_right")) inputDir.X += 1;
		inputDir = inputDir.Normalized();

		Vector3 direction = (Transform.Basis * new Vector3(inputDir.X, 0, inputDir.Y)).Normalized();
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

				// ПРОВЕРКА: Игрок не может поставить блок в себя
				Aabb blockAabb = new Aabb(new Vector3(px, py, pz), Vector3.One);
				Vector3 playerMin = new Vector3(GlobalPosition.X - 0.3f, GlobalPosition.Y + 0.01f, GlobalPosition.Z - 0.3f);
				Vector3 playerSize = new Vector3(0.6f, 1.75f, 0.6f);
				Aabb playerAabb = new Aabb(playerMin, playerSize);

				if (playerAabb.Intersects(blockAabb)) return;

				byte blockToPlace = _hotbar[_currentSlot];
				CallUpdateBlockInWorld(px, py, pz, blockToPlace);
			}
			else
			{
				CallUpdateBlockInWorld(bx, by, bz, 0);
			}
		}
	}

	private void CallUpdateBlockInWorld(int x, int y, int z, byte type)
	{
		// Ищем метод SetBlockAt СТРОГО на корневом узле текущей запущенной сцены (Node3D)
		var world = GetTree().CurrentScene;

		if (world != null && world.HasMethod("SetBlockAt"))
		{
			world.Call("SetBlockAt", x, y, z, type);
		}
	}
}
