using Godot;
using System;
using System.IO;
using System.Text.Json;

public static class WorldSettings
{
	public enum GeneratorMode { Normal, Flat }
	public enum GameMode { Survival, Creative }

	// Явное указание System.Environment устраняет неоднозначность с Godot.Environment
	public static readonly string BaseGamePath = Path.Combine(
		System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile), 
		"Worldtscn_Games"
	);

	public static readonly string SaveFolderPath = Path.Combine(BaseGamePath, "saves");
	public static readonly string ProfileFilePath = Path.Combine(BaseGamePath, "profile.json");

	public static string CurrentWorldName = "World_1"; 
	public static int CurrentSeed = 12345;
	public static GeneratorMode CurrentMode = GeneratorMode.Normal;
	public static GameMode CurrentGameMode = GameMode.Survival;
	public static bool IsLoadingExistingWorld = false;

	// НАСТРОЙКИ КАСТОМИЗАЦИИ ИГРОКА
	public static string PlayerNickname = "Player";
	public static Color PlayerColor = new Color(0.2f, 0.6f, 1.0f);

	// НАСТРОЙКИ ОПТИМИЗАЦИИ МИРА
	public static int RenderDistance = 6; 

	// СЕТЕВЫЕ ПЕРЕМЕННЫЕ
	public static bool IsMultiplayerClient = false; 
	public static string ServerIp = "127.0.0.1";    
	public static Vector3 SpawnPoint = new Vector3(0, 70, 0); 
	public static Vector3 SavedPlayerPosition = new Vector3(0f, 70f, 0f);

	public class PlayerProfileData
	{
		public string Nickname { get; set; } = "Player";
		public float R { get; set; } = 0.2f;
		public float G { get; set; } = 0.6f;
		public float B { get; set; } = 1.0f;
	}

	public static void SaveProfile()
	{
		try
		{
			if (!Directory.Exists(BaseGamePath))
			{
				Directory.CreateDirectory(BaseGamePath);
			}

			var profile = new PlayerProfileData
			{
				Nickname = PlayerNickname,
				R = PlayerColor.R,
				G = PlayerColor.G,
				B = PlayerColor.B
			};

			string json = JsonSerializer.Serialize(profile);
			File.WriteAllText(ProfileFilePath, json);
			GD.Print($"[ПРОФИЛЬ] Сохранен: Ник: {PlayerNickname}, Цвет: {PlayerColor}");
		}
		catch (Exception e)
		{
			GD.PrintErr($"[ОШИБКА СОХРАНЕНИЯ ПРОФИЛЯ] {e.Message}");
		}
	}

	public static void LoadProfile()
	{
		try
		{
			if (File.Exists(ProfileFilePath))
			{
				string json = File.ReadAllText(ProfileFilePath);
				var profile = JsonSerializer.Deserialize<PlayerProfileData>(json);
				if (profile != null)
				{
					if (!string.IsNullOrEmpty(profile.Nickname))
						PlayerNickname = profile.Nickname;

					PlayerColor = new Color(profile.R, profile.G, profile.B);
					GD.Print($"[ПРОФИЛЬ] Загружен: {PlayerNickname}");
				}
			}
		}
		catch (Exception e)
		{
			GD.PrintErr($"[ОШИБКА ЗАГРУЗКИ ПРОФИЛЯ] {e.Message}");
		}
	}
}
