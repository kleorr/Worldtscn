using Godot;
using System;
using System.IO;

public partial class WorldListMenu : Control
{
	private VBoxContainer _savesList;

	public override void _Ready()
	{
		_savesList = FindChild("SavesList", true, false) as VBoxContainer;
		
		// Находим кнопку Back по имени узла и вешаем на нее клик
		var backBtn = FindChild("BackButton", true, false) as Button;
		if (backBtn != null)
		{
			backBtn.Pressed += OnBackPressed;
		}

		UpdateSavesList();
	}

	public void UpdateSavesList()
{
	if (_savesList == null) return;

	// Очищаем старые кнопки из UI, чтобы они не дублировались
	foreach (Node child in _savesList.GetChildren())
	{
		child.QueueFree();
	}

	// Если папки лаунчера еще нет — создаем её и выходим (миров пока нет)
	if (!System.IO.Directory.Exists(WorldSettings.SaveFolderPath))
	{
		System.IO.Directory.CreateDirectory(WorldSettings.SaveFolderPath);
		return;
	}

	// Получаем все .json файлы из папки лаунчера C:\Users\lehab\Worldtscn_Games\saves
	string[] files = System.IO.Directory.GetFiles(WorldSettings.SaveFolderPath, "*.json");

	foreach (string file in files)
	{
		// Получаем чистое имя файла без пути и без расширения .json (это и есть имя мира)
		string worldName = System.IO.Path.GetFileNameWithoutExtension(file);
		
		Button btn = new Button();
		btn.Text = worldName;
		btn.CustomMinimumSize = new Vector2(0, 50);

		// При клике на кнопку загружаем этот конкретный мир
		btn.Pressed += () => LoadWorld(worldName);
		
		_savesList.AddChild(btn);
	}
}

	private void LoadWorld(string worldName)
	{
		WorldSettings.CurrentWorldName = worldName;
		WorldSettings.IsLoadingExistingWorld = true;
		
		GetTree().ChangeSceneToFile("res://world.tscn");
	}

	private void OnBackPressed()
	{
		// Возвращаем игрока в главное меню
		GetTree().ChangeSceneToFile("res://MainMenu.tscn");
	}
}
