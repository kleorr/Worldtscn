using Godot;
using System;

public partial class MainMenu : Control
{
	private LineEdit _seedInput;

	public override void _Ready()
	{
		// Находим наше поле ввода
		_seedInput = GetNode<LineEdit>("VBoxContainer/SeedInput");
	}

	private void ProcessSeed()
	{
		string text = _seedInput.Text.Trim();

		if (string.IsNullOrEmpty(text))
		{
			// Если пусто — генерируем случайное число
			WorldSettings.CurrentSeed = (int)GD.Randi();
		}
		else if (int.TryParse(text, out int result))
		{
			// Если ввели число — используем его
			WorldSettings.CurrentSeed = result;
		}
		else
		{
			// Если ввели текст (например "lehab") — превращаем строку в уникальное число (хэш)
			WorldSettings.CurrentSeed = text.GetHashCode();
		}
	}

	private void _on_normal_world_button_pressed()
	{
		WorldSettings.CurrentMode = WorldSettings.GeneratorMode.Normal;
		ProcessSeed(); // Считываем сид перед запуском
		GetTree().ChangeSceneToFile("res://world.tscn");
	}

	private void _on_flat_world_button_pressed()
	{
		WorldSettings.CurrentMode = WorldSettings.GeneratorMode.Flat;
		ProcessSeed(); // Считываем сид перед запуском
		GetTree().ChangeSceneToFile("res://world.tscn");
	}
	public override void _UnhandledInput(InputEvent @event)
{
	// Проверяем, нажата ли клавиша F11
	if (@event is InputEventKey keyEvent && keyEvent.Pressed && keyEvent.Keycode == Key.F11)
	{
		// Если сейчас полный экран — делаем оконный режим
		if (DisplayServer.WindowGetMode() == DisplayServer.WindowMode.Fullscreen)
		{
			DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
		}
		// Если оконный — бахаем на весь экран
		else
		{
			DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen);
		}
	}
}
}
