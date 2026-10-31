using Godot;
using System;

public partial class MainMenu : Control
{
	private LineEdit _seedInput;
	private LineEdit _ipInput;
	private Button _gameModeBtn;
	private Button _customizationBtn;

	// ЭЛЕМЕНТЫ ОКНА КАСТОМИЗАЦИИ
	private Panel _customizationPanel;
	private LineEdit _nickInput;
	private MeshInstance3D _previewMesh;
	private Label3D _previewNameTag;
	private Node3D _previewRotator;

	public override void _Ready()
	{
		WorldSettings.LoadProfile();

		_seedInput = GetNodeOrNull<LineEdit>("VBoxContainer/SeedInput");
		_ipInput = GetNodeOrNull<LineEdit>("VBoxContainer/IpInput");

		SetupButtons();
		BuildCustomizationMenuUI();
	}

	public override void _Process(double delta)
	{
		// Плавное вращение 3D-модельки предпросмотра
		if (_previewRotator != null && _customizationPanel != null && _customizationPanel.Visible)
		{
			_previewRotator.RotateY((float)delta * 1.5f);
		}
	}

	private void SetupButtons()
	{
		// Ищем кнопки по всему дереву сцены на любой глубине вложенности
		_gameModeBtn = FindChild("GameModeButton", true, false) as Button;
		_customizationBtn = FindChild("CustomizationButton", true, false) as Button;

		if (_gameModeBtn != null)
		{
			UpdateGameModeButtonText();
			_gameModeBtn.Pressed += OnGameModeToggle;
		}

		if (_customizationBtn != null)
		{
			_customizationBtn.Pressed += OnCustomizationPressed;
		}
		else
		{
			// Запасная кнопка, если узел еще не создан вручную в сцене
			var container = FindChild("*VBox*", true, false) as VBoxContainer;
			if (container != null)
			{
				_customizationBtn = new Button 
				{ 
					Name = "CustomizationButton", 
					Text = "Customization", 
					CustomMinimumSize = new Vector2(0, 45) 
				};
				container.AddChild(_customizationBtn);
				_customizationBtn.Pressed += OnCustomizationPressed;
			}
		}
	}

	private void OnGameModeToggle()
	{
		WorldSettings.CurrentGameMode = WorldSettings.CurrentGameMode == WorldSettings.GameMode.Survival
			? WorldSettings.GameMode.Creative
			: WorldSettings.GameMode.Survival;

		UpdateGameModeButtonText();
	}

	private void UpdateGameModeButtonText()
	{
		if (_gameModeBtn != null)
		{
			_gameModeBtn.Text = $"Game Mode: {WorldSettings.CurrentGameMode.ToString().ToUpper()}";
		}
	}

	// ------------------ ПОЛНОЭКРАННОЕ МЕНЮ КАСТОМИЗАЦИИ ------------------
	private void BuildCustomizationMenuUI()
	{
		_customizationPanel = new Panel();
		_customizationPanel.SetAnchorsPreset(LayoutPreset.FullRect);
		_customizationPanel.Visible = false;

		var bgStyle = new StyleBoxFlat
		{
			BgColor = new Color(0.08f, 0.08f, 0.10f, 0.98f)
		};
		_customizationPanel.AddThemeStyleboxOverride("panel", bgStyle);
		AddChild(_customizationPanel);

		var mainLayout = new VBoxContainer();
		mainLayout.SetAnchorsPreset(LayoutPreset.FullRect);
		mainLayout.OffsetLeft = 40;
		mainLayout.OffsetRight = -40;
		mainLayout.OffsetTop = 30;
		mainLayout.OffsetBottom = -30;
		_customizationPanel.AddChild(mainLayout);

		// Заголовок
		var title = new Label
		{
			Text = "CHARACTER CUSTOMIZATION",
			HorizontalAlignment = HorizontalAlignment.Center
		};
		title.AddThemeFontSizeOverride("font_size", 28);
		mainLayout.AddChild(title);

		mainLayout.AddChild(new HSeparator { CustomMinimumSize = new Vector2(0, 20) });

		// Центральная часть (2 колонки: 3D модель слева, настройки справа)
		var centerHBox = new HBoxContainer
		{
			SizeFlagsVertical = SizeFlags.ExpandFill,
			Alignment = BoxContainer.AlignmentMode.Center
		};
		centerHBox.AddThemeConstantOverride("separation", 50);
		mainLayout.AddChild(centerHBox);

		// ЛЕВАЯ КОЛОНКА: 3D Вьюпорт
		var previewFrame = new PanelContainer
		{
			CustomMinimumSize = new Vector2(360, 420),
			SizeFlagsVertical = SizeFlags.ShrinkCenter
		};
		var frameStyle = new StyleBoxFlat
		{
			BgColor = new Color(0.05f, 0.05f, 0.06f, 0.9f),
			CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6,
			CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6,
			BorderWidthLeft = 2, BorderWidthTop = 2, BorderWidthRight = 2, BorderWidthBottom = 2,
			BorderColor = new Color(0.25f, 0.25f, 0.32f, 1f)
		};
		previewFrame.AddThemeStyleboxOverride("panel", frameStyle);
		centerHBox.AddChild(previewFrame);

		var viewportContainer = new SubViewportContainer
		{
			Stretch = true,
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			SizeFlagsVertical = SizeFlags.ExpandFill
		};
		previewFrame.AddChild(viewportContainer);

		var subViewport = new SubViewport
		{
			Size = new Vector2I(360, 420),
			TransparentBg = true,
			OwnWorld3D = true
		};
		viewportContainer.AddChild(subViewport);

		var cam = new Camera3D
		{
			Position = new Vector3(0, 1.05f, 3.2f),
			Fov = 42
		};
		subViewport.AddChild(cam);

		var light = new DirectionalLight3D
		{
			RotationDegrees = new Vector3(-25, 40, 0),
			LightEnergy = 1.3f
		};
		subViewport.AddChild(light);

		_previewRotator = new Node3D { Position = new Vector3(0, 0, 0) };
		subViewport.AddChild(_previewRotator);

		_previewMesh = new MeshInstance3D
		{
			Mesh = new CapsuleMesh { Radius = 0.4f, Height = 1.8f },
			Position = new Vector3(0, 0.9f, 0)
		};
		_previewRotator.AddChild(_previewMesh);

		_previewNameTag = new Label3D
		{
			Text = WorldSettings.PlayerNickname,
			Position = new Vector3(0, 2.15f, 0),
			Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
			FontSize = 32,
			OutlineSize = 8,
			OutlineModulate = new Color(0, 0, 0, 1)
		};
		_previewRotator.AddChild(_previewNameTag);

		// ПРАВАЯ КОЛОНКА: Настройки
		var controlsVBox = new VBoxContainer
		{
			CustomMinimumSize = new Vector2(380, 420),
			SizeFlagsVertical = SizeFlags.ShrinkCenter
		};
		controlsVBox.AddThemeConstantOverride("separation", 14);
		centerHBox.AddChild(controlsVBox);

		var nickLabel = new Label { Text = "PLAYER NICKNAME:" };
		nickLabel.AddThemeFontSizeOverride("font_size", 16);
		controlsVBox.AddChild(nickLabel);

		_nickInput = new LineEdit
		{
			Text = WorldSettings.PlayerNickname,
			PlaceholderText = "Enter nickname...",
			CustomMinimumSize = new Vector2(0, 42)
		};
		_nickInput.TextChanged += OnNickChanged;
		controlsVBox.AddChild(_nickInput);

		controlsVBox.AddChild(new HSeparator { CustomMinimumSize = new Vector2(0, 10) });

		var colorLabel = new Label { Text = "COLOR PALETTE:" };
		colorLabel.AddThemeFontSizeOverride("font_size", 16);
		controlsVBox.AddChild(colorLabel);

		var colorGrid = new GridContainer
		{
			Columns = 4
		};
		colorGrid.AddThemeConstantOverride("h_separation", 10);
		colorGrid.AddThemeConstantOverride("v_separation", 10);
		controlsVBox.AddChild(colorGrid);

		Color[] presetColors = new Color[]
		{
			new Color(0.9f, 0.2f, 0.2f),   // Red
			new Color(0.2f, 0.8f, 0.2f),   // Green
			new Color(0.2f, 0.5f, 1.0f),   // Blue
			new Color(0.95f, 0.85f, 0.2f), // Yellow
			new Color(0.95f, 0.5f, 0.1f),  // Orange
			new Color(0.7f, 0.25f, 0.9f),  // Purple
			new Color(0.1f, 0.85f, 0.85f), // Cyan
			new Color(0.92f, 0.92f, 0.92f) // White
		};

		foreach (var color in presetColors)
		{
			var btn = new Button { CustomMinimumSize = new Vector2(85, 38) };
			var btnStyle = new StyleBoxFlat
			{
				BgColor = color,
				CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4,
				CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4
			};
			btn.AddThemeStyleboxOverride("normal", btnStyle);
			btn.AddThemeStyleboxOverride("hover", btnStyle);
			btn.AddThemeStyleboxOverride("pressed", btnStyle);

			Color colCapture = color;
			btn.Pressed += () => SelectColor(colCapture);
			colorGrid.AddChild(btn);
		}

		controlsVBox.AddChild(new HSeparator { CustomMinimumSize = new Vector2(0, 10) });

		var colorPickerBtn = new ColorPickerButton
		{
			Text = "Custom Color...",
			CustomMinimumSize = new Vector2(0, 42),
			Color = WorldSettings.PlayerColor
		};
		colorPickerBtn.ColorChanged += SelectColor;
		controlsVBox.AddChild(colorPickerBtn);

		mainLayout.AddChild(new HSeparator { CustomMinimumSize = new Vector2(0, 20) });

		// НИЖНЯЯ ПАНЕЛЬ: Кнопка выхода (DONE)
		var bottomHBox = new HBoxContainer
		{
			Alignment = BoxContainer.AlignmentMode.Center
		};
		mainLayout.AddChild(bottomHBox);

		var doneBtn = new Button
		{
			Text = "DONE",
			CustomMinimumSize = new Vector2(300, 48)
		};
		doneBtn.AddThemeFontSizeOverride("font_size", 18);
		doneBtn.Pressed += OnSaveAndCloseCustomization;
		bottomHBox.AddChild(doneBtn);

		UpdatePreview();
	}

	private void OnCustomizationPressed()
	{
		_nickInput.Text = WorldSettings.PlayerNickname;
		UpdatePreview();
		_customizationPanel.Visible = true;
	}

	private void OnNickChanged(string newText)
	{
		WorldSettings.PlayerNickname = string.IsNullOrWhiteSpace(newText) ? "Player" : newText.Trim();
		if (_previewNameTag != null) _previewNameTag.Text = WorldSettings.PlayerNickname;
	}

	private void SelectColor(Color color)
	{
		WorldSettings.PlayerColor = color;
		UpdatePreview();
	}

	private void UpdatePreview()
	{
		if (_previewMesh != null)
		{
			var mat = new StandardMaterial3D
			{
				AlbedoColor = WorldSettings.PlayerColor,
				Roughness = 0.8f
			};
			_previewMesh.MaterialOverride = mat;
		}

		if (_previewNameTag != null)
		{
			_previewNameTag.Text = WorldSettings.PlayerNickname;
		}
	}

	private void OnSaveAndCloseCustomization()
	{
		WorldSettings.SaveProfile();
		_customizationPanel.Visible = false;
	}

	// ------------------ ЗАПУСК МИРА И ПОДКЛЮЧЕНИЕ ------------------
	private void ProcessSeed()
	{
		if (_seedInput == null) return;
		string text = _seedInput.Text.Trim();

		if (string.IsNullOrEmpty(text))
		{
			WorldSettings.CurrentSeed = (int)GD.Randi();
		}
		else if (int.TryParse(text, out int result))
		{
			WorldSettings.CurrentSeed = result;
		}
		else
		{
			WorldSettings.CurrentSeed = text.GetHashCode();
		}
	}

	private void _on_normal_world_button_pressed()
	{
		WorldSettings.IsMultiplayerClient = false;
		WorldSettings.CurrentMode = WorldSettings.GeneratorMode.Normal;
		ProcessSeed();
		
		WorldSettings.IsLoadingExistingWorld = false; 
		WorldSettings.CurrentWorldName = "World_" + WorldSettings.CurrentSeed;

		GetTree().ChangeSceneToFile("res://world.tscn");
	}

	private void _on_flat_world_button_pressed()
	{
		WorldSettings.IsMultiplayerClient = false;
		WorldSettings.CurrentMode = WorldSettings.GeneratorMode.Flat;
		ProcessSeed();
		
		WorldSettings.IsLoadingExistingWorld = false;
		WorldSettings.CurrentWorldName = "World_" + WorldSettings.CurrentSeed;

		GetTree().ChangeSceneToFile("res://world.tscn");
	}

	private void _on_saves_button_pressed()
	{
		GetTree().ChangeSceneToFile("res://WorldListMenu.tscn"); 
	}

	private void _on_join_button_pressed()
	{
		WorldSettings.IsMultiplayerClient = true;

		if (_ipInput != null && !string.IsNullOrEmpty(_ipInput.Text.Trim()))
			WorldSettings.ServerIp = _ipInput.Text.Trim();
		else
			WorldSettings.ServerIp = "127.0.0.1";

		GD.Print($"[Menu] Connecting to IP: {WorldSettings.ServerIp}");
		GetTree().ChangeSceneToFile("res://world.tscn");
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is InputEventKey keyEvent && keyEvent.Pressed)
		{
			// Закрытие меню кастомизации по клавише Escape
			if (keyEvent.Keycode == Key.Escape && _customizationPanel != null && _customizationPanel.Visible)
			{
				OnSaveAndCloseCustomization();
				GetViewport().SetInputAsHandled();
				return;
			}

			if (keyEvent.Keycode == Key.F11)
			{
				if (DisplayServer.WindowGetMode() == DisplayServer.WindowMode.Fullscreen)
					DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
				else
					DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen);
			}
		}
	}
}
