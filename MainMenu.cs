using Godot;
using System;

public partial class MainMenu : Control
{
	private LineEdit _seedInput;
	private LineEdit _ipInput; // Добавляем поле для ввода IP адреса

	public override void _Ready()
	{
		_seedInput = GetNode<LineEdit>("VBoxContainer/SeedInput");
		
		// Ищем поле для ввода IP. Если его пока нет на сцене, игра не вылетит благодаря GetNodeOrNull
		_ipInput = GetNodeOrNull<LineEdit>("VBoxContainer/IpInput");
	}

	private void ProcessSeed()
	{
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
		WorldSettings.IsMultiplayerClient = false; // Фикс: это одиночная игра, а не клиент
		WorldSettings.CurrentMode = WorldSettings.GeneratorMode.Normal;
		ProcessSeed();
		
		WorldSettings.IsLoadingExistingWorld = false; 
		WorldSettings.CurrentWorldName = "World_" + WorldSettings.CurrentSeed;

		GetTree().ChangeSceneToFile("res://world.tscn");
	}

	private void _on_flat_world_button_pressed()
	{
		WorldSettings.IsMultiplayerClient = false; // Фикс: это одиночная игра, а не клиент
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

	// НАШ НОВЫЙ МЕТОД ДЛЯ КНОПКИ ПОДКЛЮЧЕНИЯ ПО СЕТИ:
	private void _on_join_button_pressed()
	{
		// Переключаем флаг: игра поймет, что мы заходим как гость
		WorldSettings.IsMultiplayerClient = true;

		// Читаем IP из текстового поля. Если там пусто — ставим локалку 127.0.0.1
		if (_ipInput != null && !string.IsNullOrEmpty(_ipInput.Text.Trim()))
		{
			WorldSettings.ServerIp = _ipInput.Text.Trim();
		}
		else
		{
			WorldSettings.ServerIp = "127.0.0.1";
		}

		GD.Print($"[Меню] Пытаемся подключиться к IP: {WorldSettings.ServerIp}");
		
		// Переходим на сцену мира, где NetworkManager сразу начнет коннект
		GetTree().ChangeSceneToFile("res://world.tscn");
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is InputEventKey keyEvent && keyEvent.Pressed && keyEvent.Keycode == Key.F11)
		{
			if (DisplayServer.WindowGetMode() == DisplayServer.WindowMode.Fullscreen)
			{
				DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
			}
			else
			{
				DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen);
			}
		}
	}
}
