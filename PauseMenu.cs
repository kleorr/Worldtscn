using Godot;
using System;

public partial class PauseMenu : CanvasLayer
{
	private bool _isPaused = false;

	public override void _Ready()
	{
		// Чтобы меню паузы само не замерзало, когда игра встает на паузу
		ProcessMode = ProcessModeEnum.Always;

		// Ищем кнопки в твоем дереве нод
		var resumeBtn = FindChild("ResumeButton", true, false) as Button;
		var saveBtn = FindChild("SaveButton", true, false) as Button;
		var hostBtn = FindChild("HostButton", true, false) as Button; // Переименуй Button3 в Godot в HostButton!
		var exitBtn = FindChild("ExitButton", true, false) as Button;

		// Привязываем клики к методам
		if (resumeBtn != null) resumeBtn.Pressed += OnResumePressed;
		if (saveBtn != null) saveBtn.Pressed += OnSavePressed;
		if (hostBtn != null) hostBtn.Pressed += OnHostPressed;
		if (exitBtn != null) exitBtn.Pressed += OnExitPressed;

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
