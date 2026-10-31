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
	private bool _canFlySurvival = false; 

	// МОДЕЛЬКА И НИКНЕЙМ КАСТОМИЗАЦИИ
	private MeshInstance3D _bodyMesh;
	private Label3D _nameTag;

	public struct SlotData
	{
		public byte BlockType;
		public int Count;
	}

	private readonly SlotData[] _inventory = new SlotData[36];
	private int _currentSlot = 0; 

	private readonly SlotData[] _craftingGrid = new SlotData[4];
	private SlotData _craftResult = new SlotData();
	private SlotData _cursorItem = new SlotData();

	private bool _isMining = false;
	private bool _isPlacing = false;
	private float _placeCooldownTimer = 0f;
	private const float PlaceCooldown = 0.22f; 
	
	private float _jumpCooldown = 0f;
	private bool _wasOnFloor = false;

	private Vector3I _currentMiningBlock = new Vector3I(int.MinValue, 0, 0);
	private float _miningProgressTimer = 0f;
	private MeshInstance3D _crackMeshInstance;
	private static ImageTexture[] _crackTextures;

	private CanvasLayer _uiLayer;
	private HBoxContainer _hotbarContainer;
	private TextureRect[] _hotbarSlotsUI; 
	private Label[] _hotbarCountLabels;
	private Label _blockNameLabel;
	private float _labelFadeTimer = 0f;

	private bool _isInventoryOpen = false;
	private Panel _inventoryPanel;
	private TextureRect[] _craftingSlotUI = new TextureRect[4];
	private Label[] _craftingSlotLabels = new Label[4];
	private TextureRect _craftResultUI;
	private Label _craftResultLabel;
	private TextureRect[] _fullInventorySlotUI = new TextureRect[36];
	private Label[] _fullInventorySlotLabels = new Label[36];

	private Control _cursorIconContainer;
	private TextureRect _cursorTextureRect;
	private Label _cursorCountLabel;

	private bool _isChatOpen = false;
	private VBoxContainer _chatBoxContainer;
	private VBoxContainer _chatMessagesList;
	private LineEdit _chatInputField;

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
		AddToGroup("Players");
		InitCrackTextures();

		SetupBodyAndNametag();

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

		CreateCrackMeshOverlay();
		InitInventoryByGameMode();

		if (isAuthority)
		{
			Input.MouseMode = Input.MouseModeEnum.Captured;
			if (_camera != null) _camera.Current = true;

			CreateMainUI();
			UpdateHotbarUI();

			// Локальный игрок берет свои настройки из WorldSettings и регистрирует их в NetworkManager
			ApplyProfile(WorldSettings.PlayerNickname, WorldSettings.PlayerColor);
			long myId = Multiplayer.MultiplayerPeer != null ? Multiplayer.GetUniqueId() : 1;
			NetworkManager.Instance?.RegisterAndBroadcastProfile(myId, WorldSettings.PlayerNickname, WorldSettings.PlayerColor);
		}
		else
		{
			if (_camera != null) _camera.Current = false;

			// Если это чужой игрок — проверяем, знает ли NetworkManager его цвет и ник
			NetworkManager.Instance?.ApplyKnownProfileIfExists(this);
		}
	}

	private void SetupBodyAndNametag()
	{
		_bodyMesh = FindChild("MeshInstance3D", true, false) as MeshInstance3D;
		if (_bodyMesh == null)
		{
			_bodyMesh = new MeshInstance3D
			{
				Mesh = new CapsuleMesh { Radius = 0.4f, Height = 1.8f },
				Position = new Vector3(0, 0.9f, 0)
			};
			AddChild(_bodyMesh);
		}

		_nameTag = new Label3D
		{
			Text = "Player",
			Position = new Vector3(0, 2.1f, 0),
			Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
			NoDepthTest = true,
			FontSize = 26,
			OutlineSize = 6,
			OutlineModulate = new Color(0, 0, 0, 0.9f)
		};
		AddChild(_nameTag);

		if (IsMultiplayerAuthority())
		{
			_nameTag.Visible = false;
		}
	}

	public void ApplyProfile(string nickname, Color color)
	{
		if (_nameTag != null)
		{
			_nameTag.Text = nickname;
		}

		if (_bodyMesh != null)
		{
			var mat = new StandardMaterial3D
			{
				AlbedoColor = color,
				Roughness = 0.8f
			};
			_bodyMesh.MaterialOverride = mat;
		}
	}

	public override void _ExitTree()
	{
		if (_crackMeshInstance != null && IsInstanceValid(_crackMeshInstance))
		{
			_crackMeshInstance.QueueFree();
		}
	}

	private void InitInventoryByGameMode()
	{
		if (WorldSettings.CurrentGameMode == WorldSettings.GameMode.Creative)
		{
			byte[] creativeBlocks = new byte[] { 2, 3, 9, 7, 4, 5, 6, 10, 8 };
			for (int i = 0; i < 9; i++)
			{
				_inventory[i] = new SlotData { BlockType = creativeBlocks[i], Count = 1 };
			}
		}
		else
		{
			for (int i = 0; i < 36; i++)
			{
				_inventory[i] = new SlotData { BlockType = 0, Count = 0 };
			}
		}
	}

	public bool CanPickupItem(byte blockType, int count)
	{
		for (int i = 0; i < 36; i++)
		{
			if (_inventory[i].BlockType == blockType && _inventory[i].Count < 64)
				return true;
		}
		for (int i = 0; i < 36; i++)
		{
			if (_inventory[i].BlockType == 0 || _inventory[i].Count <= 0)
				return true;
		}
		return false;
	}

	public bool TryPickupItem(byte blockType, int count)
	{
		for (int i = 0; i < 36; i++)
		{
			if (_inventory[i].BlockType == blockType && _inventory[i].Count < 64)
			{
				int space = 64 - _inventory[i].Count;
				int toAdd = Mathf.Min(space, count);
				_inventory[i].Count += toAdd;
				count -= toAdd;

				UpdateHotbarUI();
				UpdateFullInventoryUI();
				if (count <= 0) return true;
			}
		}

		for (int i = 0; i < 36; i++)
		{
			if (_inventory[i].BlockType == 0 || _inventory[i].Count <= 0)
			{
				_inventory[i].BlockType = blockType;
				_inventory[i].Count = count;
				UpdateHotbarUI();
				UpdateFullInventoryUI();
				return true;
			}
		}

		return false;
	}

	private static void InitCrackTextures()
	{
		if (_crackTextures != null) return;
		_crackTextures = new ImageTexture[10];

		byte[,] fractureMap = new byte[16, 16];
		for (int x = 0; x < 16; x++)
			for (int y = 0; y < 16; y++)
				fractureMap[x, y] = 255;

		void Line(int x0, int y0, int x1, int y1, int startStage, int endStage)
		{
			int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
			int dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
			int err = dx + dy, e2;
			int steps = Math.Max(dx, -dy);
			int currentStep = 0;

			while (true)
			{
				float t = steps > 0 ? (float)currentStep / steps : 0f;
				byte stage = (byte)Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(startStage, endStage, t)), 0, 9);
				if (x0 >= 0 && x0 < 16 && y0 >= 0 && y0 < 16)
				{
					if (fractureMap[x0, y0] > stage) fractureMap[x0, y0] = stage;
				}
				if (x0 == x1 && y0 == y1) break;
				e2 = 2 * err;
				if (e2 >= dy) { err += dy; x0 += sx; }
				if (e2 <= dx) { err += dx; y0 += sy; }
				currentStep++;
			}
		}

		fractureMap[7, 7] = 0; fractureMap[8, 7] = 0;
		fractureMap[7, 8] = 0; fractureMap[8, 8] = 0;

		Line(7, 7, 1, 1, 1, 8);
		Line(8, 8, 14, 14, 1, 8);
		Line(8, 7, 14, 2, 2, 9);
		Line(7, 8, 2, 14, 2, 9);
		Line(7, 6, 7, 0, 3, 7);
		Line(8, 9, 8, 15, 3, 7);
		Line(6, 7, 0, 7, 3, 8);
		Line(9, 8, 15, 8, 3, 8);

		Line(4, 4, 1, 6, 4, 9);
		Line(11, 11, 14, 9, 4, 9);
		Line(11, 4, 8, 1, 5, 9);
		Line(4, 11, 7, 14, 5, 9);

		for (int stage = 0; stage < 10; stage++)
		{
			var img = Image.CreateEmpty(16, 16, false, Image.Format.Rgba8);
			img.Fill(new Color(0, 0, 0, 0));

			for (int x = 0; x < 16; x++)
			{
				for (int y = 0; y < 16; y++)
				{
					if (fractureMap[x, y] <= stage)
					{
						img.SetPixel(x, y, new Color(0.02f, 0.02f, 0.02f, 0.85f));
					}
				}
			}

			_crackTextures[stage] = ImageTexture.CreateFromImage(img);
		}
	}

	private void CreateCrackMeshOverlay()
	{
		_crackMeshInstance = new MeshInstance3D
		{
			Mesh = new BoxMesh { Size = new Vector3(1.0025f, 1.0025f, 1.0025f) },
			Visible = false
		};

		var mat = new StandardMaterial3D
		{
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
			Uv1Scale = new Vector3(3, 2, 1),
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			AlbedoColor = new Color(1, 1, 1, 1),
			CullMode = BaseMaterial3D.CullModeEnum.Back,
			RenderPriority = 12
		};

		_crackMeshInstance.MaterialOverride = mat;
		GetTree().CurrentScene.CallDeferred("add_child", _crackMeshInstance);
	}

	private void CreateMainUI()
	{
		_uiLayer = new CanvasLayer();
		AddChild(_uiLayer);

		_hotbarContainer = new HBoxContainer();
		_hotbarContainer.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
		_hotbarContainer.GrowHorizontal = Control.GrowDirection.Both;
		_hotbarContainer.GrowVertical = Control.GrowDirection.Begin;
		
		float containerWidth = 9 * 55f + 10f;
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

		_hotbarSlotsUI = new TextureRect[9];
		_hotbarCountLabels = new Label[9];

		for (int i = 0; i < 9; i++)
		{
			var slotBackground = new ColorRect
			{
				CustomMinimumSize = new Vector2(50, 50),
				Color = new Color(0f, 0f, 0f, 0.45f)
			};
			_hotbarContainer.AddChild(slotBackground);

			_hotbarSlotsUI[i] = new TextureRect
			{
				CustomMinimumSize = new Vector2(40, 40),
				ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
				TextureFilter = CanvasItem.TextureFilterEnum.Nearest
			};
			_hotbarSlotsUI[i].SetAnchorsPreset(Control.LayoutPreset.Center);
			_hotbarSlotsUI[i].GrowHorizontal = Control.GrowDirection.Both;
			_hotbarSlotsUI[i].GrowVertical = Control.GrowDirection.Both;
			slotBackground.AddChild(_hotbarSlotsUI[i]);

			_hotbarCountLabels[i] = new Label();
			_hotbarCountLabels[i].SetAnchorsPreset(Control.LayoutPreset.BottomRight);
			_hotbarCountLabels[i].OffsetLeft = -30;
			_hotbarCountLabels[i].OffsetTop = -22;
			_hotbarCountLabels[i].OffsetRight = -4;
			_hotbarCountLabels[i].OffsetBottom = -2;
			_hotbarCountLabels[i].HorizontalAlignment = HorizontalAlignment.Right;
			_hotbarCountLabels[i].AddThemeFontSizeOverride("font_size", 14);
			slotBackground.AddChild(_hotbarCountLabels[i]);
		}

		BuildInventoryWindowUI();
		BuildCursorIconUI();
		BuildChatUI();
	}

	private void BuildChatUI()
	{
		_chatBoxContainer = new VBoxContainer
		{
			CustomMinimumSize = new Vector2(450, 220),
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		_chatBoxContainer.SetAnchorsPreset(Control.LayoutPreset.BottomLeft);
		_chatBoxContainer.OffsetLeft = 20;
		_chatBoxContainer.OffsetBottom = -80;
		_chatBoxContainer.OffsetTop = -300;
		_chatBoxContainer.OffsetRight = 470;
		_uiLayer.AddChild(_chatBoxContainer);

		_chatMessagesList = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
		_chatBoxContainer.AddChild(_chatMessagesList);

		_chatInputField = new LineEdit
		{
			PlaceholderText = "Type message or /command...",
			Visible = false,
			CustomMinimumSize = new Vector2(450, 35)
		};
		var lineEditStyle = new StyleBoxFlat
		{
			BgColor = new Color(0.1f, 0.1f, 0.12f, 0.85f),
			BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1,
			BorderColor = new Color(0.4f, 0.4f, 0.45f, 1f)
		};
		_chatInputField.AddThemeStyleboxOverride("normal", lineEditStyle);
		_chatInputField.TextSubmitted += OnChatSubmitted;
		_chatBoxContainer.AddChild(_chatInputField);
	}

	public void AddChatMessage(string text, Color? color = null)
	{
		var lbl = new Label { Text = text, Modulate = color ?? new Color(1f, 1f, 1f, 1f) };
		lbl.AddThemeFontSizeOverride("font_size", 15);
		_chatMessagesList.AddChild(lbl);

		if (_chatMessagesList.GetChildCount() > 9)
		{
			_chatMessagesList.GetChild(0).QueueFree();
		}

		var timer = GetTree().CreateTimer(12.0f);
		timer.Timeout += () =>
		{
			if (IsInstanceValid(lbl) && !_isChatOpen)
			{
				lbl.QueueFree();
			}
		};
	}

	private void OpenChat(string defaultText = "")
	{
		_isChatOpen = true;
		_chatInputField.Visible = true;
		_chatInputField.Text = defaultText;
		_chatInputField.CaretColumn = defaultText.Length;
		_chatInputField.GrabFocus();
		Input.MouseMode = Input.MouseModeEnum.Visible;
	}

	private void CloseChat()
	{
		_isChatOpen = false;
		_chatInputField.Visible = false;
		_chatInputField.ReleaseFocus();
		Input.MouseMode = Input.MouseModeEnum.Captured;
	}

	private void OnChatSubmitted(string text)
	{
		text = text.Trim();
		if (!string.IsNullOrEmpty(text))
		{
			if (text.StartsWith("/"))
			{
				ExecuteCommand(text.Substring(1).Trim());
			}
			else
			{
				AddChatMessage($"<{WorldSettings.PlayerNickname}> {text}");
			}
		}
		CloseChat();
	}

	private void ExecuteCommand(string cmdLine)
	{
		string[] parts = cmdLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		if (parts.Length == 0) return;

		string cmd = parts[0].ToLower();

		if (cmd == "fly")
		{
			_canFlySurvival = !_canFlySurvival;
			if (_canFlySurvival)
			{
				AddChatMessage("[System] Flight unlocked! Press 'H' to toggle flying.", new Color(0.4f, 1f, 0.4f));
			}
			else
			{
				_isFlying = false;
				AddChatMessage("[System] Flight locked.", new Color(1f, 0.4f, 0.4f));
			}
		}
		else if (cmd == "give")
		{
			if (parts.Length < 2)
			{
				AddChatMessage("Usage: /give <item_name> [amount]", new Color(1f, 0.4f, 0.4f));
				return;
			}

			string itemName = parts[1].ToLower().Replace("_", "");
			int count = 1;
			if (parts.Length >= 3 && int.TryParse(parts[2], out int parsedCount))
			{
				count = Mathf.Max(1, parsedCount);
			}

			byte blockType = ParseBlockType(itemName);
			if (blockType == 0)
			{
				AddChatMessage($"[Error] Unknown block: '{parts[1]}'.", new Color(1f, 0.4f, 0.4f));
				return;
			}

			int remaining = count;
			while (remaining > 0)
			{
				int giveAmount = Mathf.Min(remaining, 64);
				if (!TryPickupItem(blockType, giveAmount))
				{
					DropItemFromPlayer(blockType, giveAmount);
				}
				remaining -= giveAmount;
			}

			AddChatMessage($"[System] Given {GetBlockDisplayName(blockType)} x{count} to Player.", new Color(0.4f, 1f, 0.4f));
		}
		else if (cmd == "help")
		{
			AddChatMessage("Available commands:", new Color(1f, 0.9f, 0.4f));
			AddChatMessage("  /give <item> [amount] - Give blocks", new Color(0.85f, 0.85f, 0.85f));
			AddChatMessage("  /fly - Toggle flight ability (use H to fly)", new Color(0.85f, 0.85f, 0.85f));
		}
		else
		{
			AddChatMessage($"[Error] Unknown command: '/{cmd}'. Type /help for list.", new Color(1f, 0.4f, 0.4f));
		}
	}

	private byte ParseBlockType(string name)
	{
		return name switch
		{
			"bedrock" => 1,
			"stone" => 2,
			"grass" or "grassblock" => 3,
			"oaklog" or "log" or "wood" => 4,
			"leaves" or "leaf" => 5,
			"oakplanks" or "planks" or "plank" => 6,
			"sand" => 7,
			"water" => 8,
			"dirt" => 9,
			"obsidian" => 10,
			_ => 0
		};
	}

	private void BuildInventoryWindowUI()
	{
		_inventoryPanel = new Panel
		{
			CustomMinimumSize = new Vector2(480, 420),
			Visible = false
		};
		_inventoryPanel.SetAnchorsPreset(Control.LayoutPreset.Center);
		_inventoryPanel.OffsetLeft = -240;
		_inventoryPanel.OffsetRight = 240;
		_inventoryPanel.OffsetTop = -210;
		_inventoryPanel.OffsetBottom = 210;

		var styleBox = new StyleBoxFlat
		{
			BgColor = new Color(0.12f, 0.12f, 0.14f, 0.96f),
			CornerRadiusBottomLeft = 8, CornerRadiusBottomRight = 8,
			CornerRadiusTopLeft = 8, CornerRadiusTopRight = 8,
			BorderWidthLeft = 2, BorderWidthTop = 2, BorderWidthRight = 2, BorderWidthBottom = 2,
			BorderColor = new Color(0.3f, 0.3f, 0.35f, 1f)
		};
		_inventoryPanel.AddThemeStyleboxOverride("panel", styleBox);
		_uiLayer.AddChild(_inventoryPanel);

		var title = new Label { Text = "Crafting", Position = new Vector2(140, 15) };
		title.AddThemeFontSizeOverride("font_size", 16);
		_inventoryPanel.AddChild(title);

		var craftGrid = new GridContainer { Columns = 2, Position = new Vector2(140, 45) };
		_inventoryPanel.AddChild(craftGrid);

		for (int i = 0; i < 4; i++)
		{
			int slotIndex = i;
			var btn = CreateSlotControl(out _craftingSlotUI[i], out _craftingSlotLabels[i]);
			btn.GuiInput += (@event) => OnCraftSlotGuiInput(@event, slotIndex);
			craftGrid.AddChild(btn);
		}

		var arrow = new Label { Text = "->", Position = new Vector2(250, 68) };
		arrow.AddThemeFontSizeOverride("font_size", 22);
		_inventoryPanel.AddChild(arrow);

		var resultBtn = CreateSlotControl(out _craftResultUI, out _craftResultLabel);
		resultBtn.Position = new Vector2(285, 58);
		resultBtn.CustomMinimumSize = new Vector2(52, 52);
		resultBtn.GuiInput += OnCraftResultGuiInput;
		_inventoryPanel.AddChild(resultBtn);

		var invTitle = new Label { Text = "Inventory", Position = new Vector2(25, 145) };
		invTitle.AddThemeFontSizeOverride("font_size", 16);
		_inventoryPanel.AddChild(invTitle);

		var mainInvGrid = new GridContainer { Columns = 9, Position = new Vector2(25, 175) };
		_inventoryPanel.AddChild(mainInvGrid);

		for (int i = 9; i < 36; i++)
		{
			int slotIndex = i;
			var btn = CreateSlotControl(out _fullInventorySlotUI[i], out _fullInventorySlotLabels[i]);
			btn.GuiInput += (@event) => OnInventorySlotGuiInput(@event, slotIndex);
			mainInvGrid.AddChild(btn);
		}

		var hotbarGrid = new GridContainer { Columns = 9, Position = new Vector2(25, 340) };
		_inventoryPanel.AddChild(hotbarGrid);

		for (int i = 0; i < 9; i++)
		{
			int slotIndex = i;
			var btn = CreateSlotControl(out _fullInventorySlotUI[i], out _fullInventorySlotLabels[i]);
			btn.GuiInput += (@event) => OnInventorySlotGuiInput(@event, slotIndex);
			hotbarGrid.AddChild(btn);
		}
	}

	private Control CreateSlotControl(out TextureRect texRect, out Label countLabel)
	{
		var basePanel = new PanelContainer { CustomMinimumSize = new Vector2(44, 44) };
		var slotStyle = new StyleBoxFlat
		{
			BgColor = new Color(0.2f, 0.2f, 0.22f, 1f),
			BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1,
			BorderColor = new Color(0.1f, 0.1f, 0.1f, 1f)
		};
		basePanel.AddThemeStyleboxOverride("panel", slotStyle);

		texRect = new TextureRect
		{
			CustomMinimumSize = new Vector2(34, 34),
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		texRect.SetAnchorsPreset(Control.LayoutPreset.Center);
		basePanel.AddChild(texRect);

		countLabel = new Label
		{
			HorizontalAlignment = HorizontalAlignment.Right,
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		countLabel.AddThemeFontSizeOverride("font_size", 13);
		countLabel.SetAnchorsPreset(Control.LayoutPreset.BottomRight);
		countLabel.OffsetLeft = -28;
		countLabel.OffsetTop = -18;
		countLabel.OffsetRight = -2;
		countLabel.OffsetBottom = -1;
		basePanel.AddChild(countLabel);

		return basePanel;
	}

	private void BuildCursorIconUI()
	{
		_cursorIconContainer = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
		_uiLayer.AddChild(_cursorIconContainer);

		_cursorTextureRect = new TextureRect
		{
			CustomMinimumSize = new Vector2(36, 36),
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		_cursorIconContainer.AddChild(_cursorTextureRect);

		_cursorCountLabel = new Label
		{
			HorizontalAlignment = HorizontalAlignment.Right,
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		_cursorCountLabel.AddThemeFontSizeOverride("font_size", 14);
		_cursorCountLabel.OffsetLeft = 10;
		_cursorCountLabel.OffsetTop = 14;
		_cursorIconContainer.AddChild(_cursorCountLabel);
	}

	private void ToggleInventory()
	{
		if (_isChatOpen) return;

		_isInventoryOpen = !_isInventoryOpen;
		_inventoryPanel.Visible = _isInventoryOpen;

		if (_isInventoryOpen)
		{
			Input.MouseMode = Input.MouseModeEnum.Visible;
			UpdateFullInventoryUI();
			CheckCraftingRecipe();
		}
		else
		{
			Input.MouseMode = Input.MouseModeEnum.Captured;

			if (_cursorItem.Count > 0) ReturnItemToInventoryOrDrop(ref _cursorItem);

			for (int i = 0; i < 4; i++)
			{
				if (_craftingGrid[i].Count > 0) ReturnItemToInventoryOrDrop(ref _craftingGrid[i]);
			}

			_craftResult = new SlotData();
			UpdateCursorUI();
			UpdateHotbarUI();
		}
	}

	private void ReturnItemToInventoryOrDrop(ref SlotData item)
	{
		if (item.Count <= 0 || item.BlockType == 0) return;

		for (int i = 0; i < 36; i++)
		{
			if (_inventory[i].BlockType == item.BlockType && _inventory[i].Count < 64)
			{
				int space = 64 - _inventory[i].Count;
				int toAdd = Mathf.Min(space, item.Count);
				_inventory[i].Count += toAdd;
				item.Count -= toAdd;
				if (item.Count <= 0) break;
			}
		}

		if (item.Count > 0)
		{
			for (int i = 0; i < 36; i++)
			{
				if (_inventory[i].Count <= 0 || _inventory[i].BlockType == 0)
				{
					_inventory[i] = item;
					item = new SlotData();
					break;
				}
			}
		}

		if (item.Count > 0)
		{
			DropItemFromPlayer(item.BlockType, item.Count);
			item = new SlotData();
		}
	}

	private void OnInventorySlotGuiInput(InputEvent @event, int index)
	{
		if (@event is InputEventMouseButton mb && mb.Pressed)
		{
			if (mb.ButtonIndex == MouseButton.Left)
				HandleSlotClick(ref _inventory[index], isRightClick: false);
			else if (mb.ButtonIndex == MouseButton.Right)
				HandleSlotClick(ref _inventory[index], isRightClick: true);

			UpdateFullInventoryUI();
			UpdateHotbarUI();
			UpdateCursorUI();
		}
	}

	private void OnCraftSlotGuiInput(InputEvent @event, int index)
	{
		if (@event is InputEventMouseButton mb && mb.Pressed)
		{
			if (mb.ButtonIndex == MouseButton.Left)
				HandleSlotClick(ref _craftingGrid[index], isRightClick: false);
			else if (mb.ButtonIndex == MouseButton.Right)
				HandleSlotClick(ref _craftingGrid[index], isRightClick: true);

			CheckCraftingRecipe();
			UpdateFullInventoryUI();
			UpdateCursorUI();
		}
	}

	private void OnCraftResultGuiInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
		{
			if (_craftResult.BlockType == 0 || _craftResult.Count <= 0) return;

			if (_cursorItem.Count == 0)
			{
				_cursorItem = _craftResult;
				ConsumeCraftingIngredients();
			}
			else if (_cursorItem.BlockType == _craftResult.BlockType && _cursorItem.Count + _craftResult.Count <= 64)
			{
				_cursorItem.Count += _craftResult.Count;
				ConsumeCraftingIngredients();
			}

			CheckCraftingRecipe();
			UpdateFullInventoryUI();
			UpdateCursorUI();
		}
	}

	private void HandleSlotClick(ref SlotData slot, bool isRightClick)
	{
		if (!isRightClick)
		{
			if (_cursorItem.Count == 0)
			{
				_cursorItem = slot;
				slot = new SlotData();
			}
			else if (slot.Count == 0)
			{
				slot = _cursorItem;
				_cursorItem = new SlotData();
			}
			else if (slot.BlockType == _cursorItem.BlockType)
			{
				int space = 64 - slot.Count;
				int toAdd = Mathf.Min(space, _cursorItem.Count);
				slot.Count += toAdd;
				_cursorItem.Count -= toAdd;
				if (_cursorItem.Count <= 0) _cursorItem = new SlotData();
			}
			else
			{
				var temp = slot;
				slot = _cursorItem;
				_cursorItem = temp;
			}
		}
		else
		{
			if (_cursorItem.Count == 0 && slot.Count > 0)
			{
				int take = Mathf.CeilToInt(slot.Count / 2f);
				_cursorItem = new SlotData { BlockType = slot.BlockType, Count = take };
				slot.Count -= take;
				if (slot.Count <= 0) slot = new SlotData();
			}
			else if (_cursorItem.Count > 0)
			{
				if (slot.Count == 0)
				{
					slot = new SlotData { BlockType = _cursorItem.BlockType, Count = 1 };
					_cursorItem.Count--;
					if (_cursorItem.Count <= 0) _cursorItem = new SlotData();
				}
				else if (slot.BlockType == _cursorItem.BlockType && slot.Count < 64)
				{
					slot.Count++;
					_cursorItem.Count--;
					if (_cursorItem.Count <= 0) _cursorItem = new SlotData();
				}
			}
		}
	}

	private void CheckCraftingRecipe()
	{
		int totalItems = 0;
		byte singleType = 0;

		for (int i = 0; i < 4; i++)
		{
			if (_craftingGrid[i].Count > 0)
			{
				totalItems++;
				singleType = _craftingGrid[i].BlockType;
			}
		}

		if (totalItems == 1 && (singleType == 4 || singleType == 18 || singleType == 19))
		{
			_craftResult = new SlotData { BlockType = 6, Count = 4 };
		}
		else if (totalItems == 4 && _craftingGrid[0].BlockType == 6 && _craftingGrid[1].BlockType == 6 &&
				 _craftingGrid[2].BlockType == 6 && _craftingGrid[3].BlockType == 6)
		{
			_craftResult = new SlotData { BlockType = 9, Count = 4 };
		}
		else if (totalItems == 4 && _craftingGrid[0].BlockType == 2 && _craftingGrid[1].BlockType == 2 &&
				 _craftingGrid[2].BlockType == 2 && _craftingGrid[3].BlockType == 2)
		{
			_craftResult = new SlotData { BlockType = 10, Count = 1 };
		}
		else
		{
			_craftResult = new SlotData();
		}

		UpdateCraftingUI();
	}

	private void ConsumeCraftingIngredients()
	{
		for (int i = 0; i < 4; i++)
		{
			if (_craftingGrid[i].Count > 0)
			{
				_craftingGrid[i].Count--;
				if (_craftingGrid[i].Count <= 0) _craftingGrid[i] = new SlotData();
			}
		}
	}

	private void UpdateCraftingUI()
	{
		for (int i = 0; i < 4; i++)
		{
			ApplySlotDataToUI(_craftingSlotUI[i], _craftingSlotLabels[i], _craftingGrid[i]);
		}
		ApplySlotDataToUI(_craftResultUI, _craftResultLabel, _craftResult);
	}

	private void UpdateFullInventoryUI()
	{
		for (int i = 0; i < 36; i++)
		{
			ApplySlotDataToUI(_fullInventorySlotUI[i], _fullInventorySlotLabels[i], _inventory[i]);
		}
		UpdateCraftingUI();
	}

	private void UpdateCursorUI()
	{
		if (_cursorItem.Count > 0 && _cursorItem.BlockType > 0)
		{
			_cursorIconContainer.Visible = true;
			string path = GetTexturePathForBlock(_cursorItem.BlockType);
			_cursorTextureRect.Texture = ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;
			_cursorCountLabel.Text = _cursorItem.Count > 1 ? _cursorItem.Count.ToString() : "";
		}
		else
		{
			_cursorIconContainer.Visible = false;
		}
	}

	private void ApplySlotDataToUI(TextureRect tex, Label lbl, SlotData data)
	{
		if (tex == null || lbl == null) return;
		if (data.BlockType > 0 && data.Count > 0)
		{
			string path = GetTexturePathForBlock(data.BlockType);
			tex.Texture = ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;
			lbl.Text = data.Count > 1 ? data.Count.ToString() : "";
		}
		else
		{
			tex.Texture = null;
			lbl.Text = "";
		}
	}

	private void UpdateHotbarUI()
	{
		if (_hotbarSlotsUI == null) return;

		for (int i = 0; i < 9; i++)
		{
			if (_hotbarSlotsUI[i] != null)
			{
				var bg = _hotbarSlotsUI[i].GetParent<ColorRect>();
				bg.Color = (i == _currentSlot) ? new Color(0.35f, 0.35f, 0.35f, 0.8f) : new Color(0f, 0f, 0f, 0.45f);

				ApplySlotDataToUI(_hotbarSlotsUI[i], _hotbarCountLabels[i], _inventory[i]);
			}
		}

		if (_blockNameLabel != null)
		{
			byte currentType = _inventory[_currentSlot].BlockType;
			if (currentType > 0 && _inventory[_currentSlot].Count > 0)
			{
				_blockNameLabel.Text = GetBlockDisplayName(currentType);
				_blockNameLabel.Modulate = new Color(1, 1, 1, 1); 
				_labelFadeTimer = 1.5f;
			}
			else
			{
				_blockNameLabel.Text = "";
			}
		}
	}

	private void DropCurrentHotbarItem()
	{
		if (_inventory[_currentSlot].Count <= 0 || _inventory[_currentSlot].BlockType == 0) return;

		byte type = _inventory[_currentSlot].BlockType;
		_inventory[_currentSlot].Count--;
		if (_inventory[_currentSlot].Count <= 0)
		{
			_inventory[_currentSlot] = new SlotData();
		}

		UpdateHotbarUI();

		Vector3 spawnPos = (_camera != null ? _camera.GlobalPosition : GlobalPosition + Vector3.Up * 1.5f) - _camera.GlobalTransform.Basis.Z * 0.5f;
		Vector3 throwDir = -_camera.GlobalTransform.Basis.Z * 5.5f + Vector3.Up * 1.5f;

		if (NetworkManager.Instance != null)
		{
			NetworkManager.Instance.ChallengeDropItem(spawnPos, throwDir, type, 1);
		}
	}

	private void DropItemFromPlayer(byte blockType, int count)
	{
		Vector3 spawnPos = GlobalPosition + Vector3.Up * 1.0f;
		Vector3 throwDir = -Transform.Basis.Z * 3.0f + Vector3.Up * 2.0f;

		if (NetworkManager.Instance != null)
		{
			NetworkManager.Instance.ChallengeDropItem(spawnPos, throwDir, blockType, count);
		}
	}

	public override void _Input(InputEvent @event)
	{
		if (!Multiplayer.HasMultiplayerPeer() || !IsMultiplayerAuthority()) return;

		if (@event is InputEventKey chatKeyEv && chatKeyEv.Pressed && !chatKeyEv.Echo)
		{
			if (chatKeyEv.Keycode == Key.T && !_isChatOpen && !_isInventoryOpen)
			{
				OpenChat();
				GetViewport().SetInputAsHandled();
				return;
			}
			if (chatKeyEv.Keycode == Key.Slash && !_isChatOpen && !_isInventoryOpen)
			{
				OpenChat("/");
				GetViewport().SetInputAsHandled();
				return;
			}
			if (chatKeyEv.Keycode == Key.Escape && _isChatOpen)
			{
				CloseChat();
				GetViewport().SetInputAsHandled();
				return;
			}
		}

		if (_isChatOpen) return;

		if (@event is InputEventKey keyEv && keyEv.Pressed && !keyEv.Echo)
		{
			if (keyEv.Keycode == Key.E)
			{
				ToggleInventory();
				return;
			}
			if (keyEv.Keycode == Key.Escape && _isInventoryOpen)
			{
				ToggleInventory();
				return;
			}
			if (keyEv.Keycode == Key.Q && !_isInventoryOpen)
			{
				DropCurrentHotbarItem();
				return;
			}
			
			bool flyAllowed = (WorldSettings.CurrentGameMode == WorldSettings.GameMode.Creative) || _canFlySurvival;
			if (keyEv.Keycode == Key.H && flyAllowed && !_isInventoryOpen)
			{
				_isFlying = !_isFlying;
				Velocity = Vector3.Zero; 
			}
		}

		if (_isInventoryOpen) return;

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

		if (@event is InputEventMouseButton mouseButton)
		{
			if (mouseButton.ButtonIndex == MouseButton.Right)
			{
				_isPlacing = mouseButton.Pressed;
				if (_isPlacing)
				{
					_placeCooldownTimer = 0f;
					TryPlaceBlock(); 
				}
			}
			else if (mouseButton.ButtonIndex == MouseButton.Left)
			{
				_isMining = mouseButton.Pressed;
				if (!_isMining) ResetMining();
			}
		}

		for (int k = 0; k < 9; k++)
		{
			if (Input.IsKeyPressed(Key.Key1 + k) && _currentSlot != k)
			{
				_currentSlot = k;
				UpdateHotbarUI();
				break;
			}
		}
	}

	public override void _PhysicsProcess(double delta)
	{
		if (!Multiplayer.HasMultiplayerPeer() || !IsMultiplayerAuthority()) return;

		float dt = (float)delta;

		if (_isChatOpen) return;

		if (_isInventoryOpen)
		{
			if (_cursorIconContainer != null && _cursorIconContainer.Visible)
			{
				_cursorIconContainer.Position = GetViewport().GetMousePosition() + new Vector2(10, 10);
			}
			return;
		}

		if (_isPlacing)
		{
			_placeCooldownTimer += dt;
			if (_placeCooldownTimer >= PlaceCooldown)
			{
				_placeCooldownTimer = 0f;
				TryPlaceBlock();
			}
		}

		ProcessMining(dt);

		Vector3 velocity = Velocity;

		bool feetInWater = false;
		bool headInWater = false;

		if (GetTree().CurrentScene is WorldGenerator world)
		{
			int px = Mathf.FloorToInt(GlobalPosition.X);
			int pyFeet = Mathf.FloorToInt(GlobalPosition.Y + 0.1f);
			int pyHead = Mathf.FloorToInt(GlobalPosition.Y + 1.2f);
			int pz = Mathf.FloorToInt(GlobalPosition.Z);

			feetInWater = Chunk.IsWater(world.GetBlockAt(px, pyFeet, pz));
			headInWater = Chunk.IsWater(world.GetBlockAt(px, pyHead, pz));
		}

		Vector2 inputDir = Vector2.Zero;
		if (Input.IsActionPressed("move_forward")) inputDir.Y -= 1;
		if (Input.IsActionPressed("move_backward")) inputDir.Y += 1;
		if (Input.IsActionPressed("move_left")) inputDir.X -= 1;
		if (Input.IsActionPressed("move_right")) inputDir.X += 1;
		inputDir = inputDir.Normalized();

		Vector3 direction = (Transform.Basis * new Vector3(inputDir.X, 0, inputDir.Y)).Normalized();

		bool canFly = (WorldSettings.CurrentGameMode == WorldSettings.GameMode.Creative) || _canFlySurvival;
		if (_isFlying && canFly)
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
		else if (feetInWater)
		{
			bool isAtSurface = !headInWater;

			if (isAtSurface)
			{
				if (Input.IsKeyPressed(Key.Space))
				{
					if (IsOnWall() || IsOnFloor())
					{
						velocity.Y = 3.3f;
					}
					else
					{
						velocity.Y = Mathf.MoveToward(velocity.Y, 1.8f, 10.0f * dt);
					}
				}
				else
				{
					velocity.Y -= 3.0f * dt;
					velocity.Y = Mathf.MoveToward(velocity.Y, -1.2f, 3.0f * dt);
				}

				float currentWaterSpeed = IsOnWall() ? Speed * 0.75f : Speed * 0.6f;
				if (direction != Vector3.Zero)
				{
					velocity.X = direction.X * currentWaterSpeed;
					velocity.Z = direction.Z * currentWaterSpeed;
				}
				else
				{
					velocity.X = Mathf.MoveToward(Velocity.X, 0, currentWaterSpeed * 4f * dt);
					velocity.Z = Mathf.MoveToward(Velocity.Z, 0, currentWaterSpeed * 4f * dt);
				}
			}
			else
			{
				if (Input.IsKeyPressed(Key.Space))
				{
					velocity.Y = Mathf.MoveToward(velocity.Y, 2.5f, 10.0f * dt);
				}
				else if (Input.IsKeyPressed(Key.Shift))
				{
					velocity.Y = Mathf.MoveToward(velocity.Y, -3.0f, 10.0f * dt);
				}
				else
				{
					velocity.Y -= 2.5f * dt;
					velocity.Y = Mathf.MoveToward(velocity.Y, -1.5f, 4.0f * dt);
				}

				float currentWaterSpeed = Speed * 0.5f;
				if (direction != Vector3.Zero)
				{
					velocity.X = direction.X * currentWaterSpeed;
					velocity.Z = direction.Z * currentWaterSpeed;
				}
				else
				{
					velocity.X = Mathf.MoveToward(Velocity.X, 0, currentWaterSpeed * 4f * dt);
					velocity.Z = Mathf.MoveToward(Velocity.Z, 0, currentWaterSpeed * 4f * dt);
				}
			}
		}
		else
		{
			if (!IsOnFloor()) velocity.Y -= 9.8f * dt;

			bool onFloor = IsOnFloor();
			if (_jumpCooldown > 0f) _jumpCooldown -= dt;

			if (onFloor && !_wasOnFloor)
			{
				_jumpCooldown = Mathf.Max(_jumpCooldown, 0.12f);
			}
			_wasOnFloor = onFloor;

			if (Input.IsKeyPressed(Key.Space) && onFloor && _jumpCooldown <= 0f)
			{
				velocity.Y = JumpVelocity;
				_jumpCooldown = 0.28f; 
			}

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

	private void ResetMining()
	{
		_miningProgressTimer = 0f;
		_currentMiningBlock = new Vector3I(int.MinValue, 0, 0);
		if (_crackMeshInstance != null) _crackMeshInstance.Visible = false;
	}

	private void ProcessMining(float delta)
	{
		if (!_isMining || _camera == null)
		{
			ResetMining();
			return;
		}

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
			Vector3 targetBlockPos = hitPos - hitNormal * 0.1f;

			Vector3I blockCoords = new Vector3I(
				Mathf.FloorToInt(targetBlockPos.X),
				Mathf.FloorToInt(targetBlockPos.Y),
				Mathf.FloorToInt(targetBlockPos.Z)
			);

			if (GetTree().CurrentScene is WorldGenerator world)
			{
				byte blockType = world.GetBlockAt(blockCoords.X, blockCoords.Y, blockCoords.Z);

				if (blockType == 1 || Chunk.IsWater(blockType) || blockType == 0)
				{
					ResetMining();
					return;
				}

				if (WorldSettings.CurrentGameMode == WorldSettings.GameMode.Creative)
				{
					BreakBlockInstant(blockCoords, blockType);
					ResetMining();
					return;
				}

				if (blockCoords != _currentMiningBlock)
				{
					_currentMiningBlock = blockCoords;
					_miningProgressTimer = 0f;
				}

				float hardness = GetBlockHardness(blockType);
				_miningProgressTimer += delta;

				float progress = Mathf.Clamp(_miningProgressTimer / hardness, 0f, 1f);
				int stage = Mathf.Clamp(Mathf.FloorToInt(progress * 10f), 0, 9);

				if (_crackMeshInstance != null)
				{
					_crackMeshInstance.Visible = true;
					_crackMeshInstance.GlobalPosition = new Vector3(blockCoords.X + 0.5f, blockCoords.Y + 0.5f, blockCoords.Z + 0.5f);

					if (_crackMeshInstance.MaterialOverride is StandardMaterial3D crackMat)
					{
						crackMat.AlbedoTexture = _crackTextures[stage];
					}
				}

				if (_miningProgressTimer >= hardness)
				{
					BreakBlockWithDrop(blockCoords, blockType);
					ResetMining();
				}
			}
		}
		else
		{
			ResetMining();
		}
	}

	private void BreakBlockInstant(Vector3I coords, byte blockType)
	{
		if (NetworkManager.Instance != null)
			NetworkManager.Instance.ChallengeBlockChange(coords, 0);
	}

	private void BreakBlockWithDrop(Vector3I coords, byte blockType)
	{
		if (NetworkManager.Instance != null)
			NetworkManager.Instance.ChallengeBlockChange(coords, 0);
	}

	private float GetBlockHardness(byte type)
	{
		return type switch
		{
			1 => -1f,
			5 => 0.25f,
			7 => 0.5f,
			9 => 0.55f,
			3 => 0.65f,
			6 => 0.95f,
			4 or 18 or 19 => 1.25f,
			2 => 1.55f,
			10 => 3.2f,
			_ => 0.8f
		};
	}

	private void TryPlaceBlock()
	{
		if (_camera == null || _isInventoryOpen || _isChatOpen) return;

		var currentSlotData = _inventory[_currentSlot];
		if (currentSlotData.BlockType == 0 || currentSlotData.Count <= 0) return;

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

			if (spaceState.IntersectShape(shapeQuery).Count > 0) return;

			byte blockToPlace = currentSlotData.BlockType;

			if (blockToPlace == 4)
			{
				if (Mathf.Abs(hitNormal.X) > 0.5f) blockToPlace = 18; 
				else if (Mathf.Abs(hitNormal.Z) > 0.5f) blockToPlace = 19; 
				else blockToPlace = 4;  
			}
			
			if (NetworkManager.Instance != null)
			{
				NetworkManager.Instance.ChallengeBlockChange(new Vector3I(px, py, pz), blockToPlace);
			}

			if (WorldSettings.CurrentGameMode == WorldSettings.GameMode.Survival)
			{
				_inventory[_currentSlot].Count--;
				if (_inventory[_currentSlot].Count <= 0)
				{
					_inventory[_currentSlot] = new SlotData();
				}
				UpdateHotbarUI();
			}
		}
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

	public void SpawnBlockParticlesAt(Vector3 pos, byte blockType)
	{
		var particles = new CpuParticles3D();
		GetTree().CurrentScene.AddChild(particles);
		particles.GlobalPosition = pos + new Vector3(0.5f, 0.5f, 0.5f);

		particles.Amount = 18;
		particles.Lifetime = 0.35f;
		particles.OneShot = true;
		particles.Explosiveness = 1.0f;
		particles.EmissionShape = CpuParticles3D.EmissionShapeEnum.Box;
		particles.EmissionBoxExtents = new Vector3(0.35f, 0.35f, 0.35f);
		particles.Direction = new Vector3(0, 1, 0);
		particles.Spread = 180f;
		particles.InitialVelocityMin = 2.0f;
		particles.InitialVelocityMax = 4.0f;
		particles.Gravity = new Vector3(0, -12f, 0);
		particles.ScaleAmountMin = 0.06f;
		particles.ScaleAmountMax = 0.12f;

		StandardMaterial3D mat = GetParticleMaterial(blockType);
		if (mat != null)
		{
			particles.Mesh = new QuadMesh { Size = new Vector2(0.12f, 0.12f), Material = mat };
		}

		particles.Emitting = true;
		var timer = GetTree().CreateTimer(0.45f);
		timer.Timeout += () => particles.QueueFree();
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
				CullMode = BaseMaterial3D.CullModeEnum.Disabled
			};

			ParticleMaterials[blockType] = mat;
			return mat;
		}
		return null;
	}

	public static string GetTexturePathForBlock(byte type)
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

	private static string GetBlockDisplayName(byte type)
	{
		return type switch
		{
			1 => "Bedrock",
			2 => "Stone",
			3 => "Grass Block",
			4 or 18 or 19 => "Oak Log",
			5 => "Leaves",
			6 => "Oak Planks",
			7 => "Sand",
			8 => "Water",
			9 => "Dirt",
			10 => "Obsidian",
			_ => "Block"
		};
	}
}
