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

	private string[] _blockFullNames = { "Stone", "Grass", "Oak Log", "Leaves", "Oak Planks", "Obsidian" };
	private string[] _texturePaths = {
		"res://textures/stone.png",
		"res://textures/grassblock_top.png",
		"res://textures/oaklog_side.png",
		"res://textures/leaves.png",
		"res://textures/oakplanks.png",
        "res://textures/obsidian.png"
	};

	public override void _Ready()
	{
		if (HasNode("Camera3D")) _camera = GetNode<Camera3D>("Camera3D");
		else if (HasNode("Head/Camera3D")) _camera = GetNode<Camera3D>("Head/Camera3D");
			
		if (GetTree().CurrentScene.HasNode("BlockHighlight"))
			_highlight = GetTree().CurrentScene.GetNode<Node3D>("BlockHighlight");

		Input.MouseMode = Input.MouseModeEnum.Captured;

		CreateGraphicHotbarUI();
		UpdateUI();
	}

	private void CreateGraphicHotbarUI()
	{
		_uiLayer = new CanvasLayer();
		AddChild(_uiLayer);

		// Контейнер для иконок блоков
		_hotbarContainer = new HBoxContainer();
		_hotbarContainer.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
		_hotbarContainer.GrowVertical = Control.GrowDirection.Begin;
		_hotbarContainer.Position += new Vector2(-180, -90); // Смещаем чуть выше под текст
		_hotbarContainer.CustomMinimumSize = new Vector2(360, 60);
		_uiLayer.AddChild(_hotbarContainer);

		// Текстовая подпись под хотбаром
		_blockNameLabel = new Label();
		_blockNameLabel.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
		_blockNameLabel.GrowVertical = Control.GrowDirection.Begin;
		_blockNameLabel.Position += new Vector2(-100, -25);
		_blockNameLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_uiLayer.AddChild(_blockNameLabel);

		for (int i = 0; i < 6; i++)
		{
			// Подложка слота
			var slotBackground = new ColorRect();
			slotBackground.CustomMinimumSize = new Vector2(55, 55);
			slotBackground.Color = new Color(0.1f, 0.1f, 0.1f, 0.6f);
			_hotbarContainer.AddChild(slotBackground);

			// Иконка-текстура твоего блока внутри слота
			_slots[i] = new TextureRect();
			_slots[i].CustomMinimumSize = new Vector2(45, 45);
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
				// Подсвечиваем активный слот рамкой/цветом
				bg.Color = (i == _currentSlot) ? new Color(0.8f, 0.8f, 0.1f, 0.8f) : new Color(0.1f, 0.1f, 0.1f, 0.6f);
			}
		}

		if (_blockNameLabel != null)
		{
			_blockNameLabel.Text = _blockFullNames[_currentSlot];
		}
	}

	public override void _Input(InputEvent @event)
	{
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

		// Одиночные клики мыши (ИСПРАВЛЕНИЕ ФАСТПЛЕЙСА)
		if (@event is InputEventMouseButton mouseButton && mouseButton.Pressed)
		{
			if (mouseButton.ButtonIndex == MouseButton.Right)
			{
				ModifyWorldBlock(true); // Ставить
			}
			else if (mouseButton.ButtonIndex == MouseButton.Left)
			{
				ModifyWorldBlock(false); // Ломать
			}
		}

		if (Input.IsKeyPressed(Key.Key1)) { _currentSlot = 0; UpdateUI(); }
		if (Input.IsKeyPressed(Key.Key2)) { _currentSlot = 1; UpdateUI(); }
		if (Input.IsKeyPressed(Key.Key3)) { _currentSlot = 2; UpdateUI(); }
		if (Input.IsKeyPressed(Key.Key4)) { _currentSlot = 3; UpdateUI(); }
		if (Input.IsKeyPressed(Key.Key5)) { _currentSlot = 4; UpdateUI(); }
		if (Input.IsKeyPressed(Key.Key6)) { _currentSlot = 5; UpdateUI(); }
	}

	public override void _PhysicsProcess(double delta)
	{
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

		// Обновление прицела/рамки каждый физический кадр
		UpdateBlockHighlight();
	}

	private void UpdateBlockHighlight()
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

			if (_highlight != null)
			{
				_highlight.Visible = true;
				_highlight.GlobalPosition = new Vector3(bx + 0.5f, by + 0.5f, bz + 0.5f);
			}
		}
		else
		{
			if (_highlight != null) _highlight.Visible = false;
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
		var world = GetNodeOrNull("/root/WorldGenerator");
		if (world == null) world = GetTree().CurrentScene;

		if (world != null && world.HasMethod("SetBlockAt"))
		{
			world.Call("SetBlockAt", x, y, z, type);
		}
	}
}
