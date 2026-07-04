using Godot;
using System;

public partial class PauseMenu : CanvasLayer
{
	private bool _isPaused = false;
	
	private HSlider _distanceSlider;
	private Label _distanceLabel;

	public override void _Ready()
	{
		// Чтобы меню паузы само не замерзало, когда игра встает на паузу
		ProcessMode = ProcessModeEnum.Always;

		// Ищем кнопки и элементы слайдера в дереве нод
		var resumeBtn = FindChild("ResumeButton", true, false) as Button;
		var saveBtn = FindChild("SaveButton", true, false) as Button;
		var hostBtn = FindChild("HostButton", true, false) as Button; 
		var exitBtn = FindChild("ExitButton", true, false) as Button;
		
		_distanceSlider = FindChild("RenderDistanceSlider", true, false) as HSlider;
		_distanceLabel = FindChild("RenderDistanceLabel", true, false) as Label;

		// Привязываем клики и изменения слайдера к методам
		if (resumeBtn != null) resumeBtn.Pressed += OnResumePressed;
		if (saveBtn != null) saveBtn.Pressed += OnSavePressed;
		if (hostBtn != null) hostBtn.Pressed += OnHostPressed;
		if (exitBtn != null) exitBtn.Pressed += OnExitPressed;
		
		if (_distanceSlider != null)
		{
			_distanceSlider.Value = WorldSettings.RenderDistance; 
			_distanceSlider.ValueChanged += OnRenderDistanceChanged;
			
			// Сразу обновляем текст при старте
			UpdateDistanceUI(_distanceSlider.Value);
		}

		// Прячем меню при запуске игры
		Visible = false;
	}

	public override void _Input(InputEvent @event)
	{
		// Ловим нажатие ESC
		if (@event.IsActionPressed("ui_cancel")) 
		{
			TogglePause();
		}
	}

	public void TogglePause()
	{
		_isPaused = !_isPaused;
		Visible = _isPaused;
		GetTree().Paused = _isPaused;

		// Включаем курсор в паузе и прячем/блокируем в игре
		Input.MouseMode = _isPaused ? Input.MouseModeEnum.Visible : Input.MouseModeEnum.Captured;
	}

	private void OnRenderDistanceChanged(double value)
	{
		int newDistance = (int)value;
		
		// 1. Записываем новую дальность в настройки
		WorldSettings.RenderDistance = newDistance;
		
		// 2. Обновляем текст интерфейса
		UpdateDistanceUI(value);

		// 3. Динамически адаптируем туман под новую дистанцию
		UpdateWorldFog(newDistance);
	}

	private void UpdateDistanceUI(double value)
	{
		if (_distanceLabel != null)
		{
			_distanceLabel.Text = $"Дальность прогрузки: {value} чанков";
		}
	}

	private void UpdateWorldFog(int distance)
	{
		// Ищем узел тумана на текущей активной сцене игры
		var envNode = GetTree().CurrentScene.FindChild("WorldEnvironment", true, false) as WorldEnvironment;
		if (envNode != null && envNode.Environment != null)
		{
			float chunkWidth = 32f; 
			float maxVisibleDistance = distance * chunkWidth;

			envNode.Environment.FogEnabled = true;
			// Конец тумана ставим на границу видимости чанков
			envNode.Environment.FogDepthEnd = maxVisibleDistance;
			// Начало тумана — плавно скрываем мир с 60% от максимальной дистанции
			envNode.Environment.FogDepthBegin = maxVisibleDistance * 0.6f;
		}
	}

	private void OnResumePressed()
	{
		TogglePause();
	}

	private void OnSavePressed()
	{
		// Берем генератор прямо из корня активного мира
		var generator = GetTree().CurrentScene as WorldGenerator;
		
		if (generator != null)
		{
			generator.SaveWorldData();
			GD.Print("[Пауза] Сигнал сохранения успешно передан в WorldGenerator.");
		}
		else
		{
			GD.PrintErr("[ОШИБКА] Не удалось найти скрипт WorldGenerator на сцене!");
		}

		TogglePause(); 
	}

	private void OnHostPressed()
	{
		// Ищем сетевой менеджер на сцене
		var networkManager = GetTree().CurrentScene.GetNodeOrNull<NetworkManager>("NetworkManager");
		if (networkManager != null)
		{
			networkManager.HostGame();
		}
		else
		{
			GD.PrintErr("[ОШИБКА] Не удалось найти узел NetworkManager на сцене!");
		}
		
		TogglePause(); 
	}

	private void OnExitPressed()
	{
		// Обязательно отжимаем паузу перед выходом, чтобы главное меню не зависло!
		GetTree().Paused = false;
		GetTree().ChangeSceneToFile("res://MainMenu.tscn");
	}
}
