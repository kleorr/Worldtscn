using Godot;
using System;

public static class WorldSettings
{
	public enum GeneratorMode { Normal, Flat }

	public static readonly string SaveFolderPath = System.IO.Path.Combine(
		System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile), 
		"Worldtscn_Games", 
		"saves"
	);

	public static string CurrentWorldName = "World_1"; 
	public static int CurrentSeed = 12345;
	public static GeneratorMode CurrentMode = GeneratorMode.Normal;
	public static bool IsLoadingExistingWorld = false;

	// СЕТЕВЫЕ ПЕРЕМЕННЫЕ
	public static bool IsMultiplayerClient = false; // Заходим ли мы как клиент
	public static string ServerIp = "127.0.0.1";    // IP сервера для подключения
	public static Godot.Vector3 SpawnPoint = new Godot.Vector3(0, 30, 0);
}
