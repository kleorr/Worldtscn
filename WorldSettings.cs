using Godot;

public static class WorldSettings
{
	public enum GeneratorMode { Normal, Flat }
	public static GeneratorMode CurrentMode = GeneratorMode.Normal;
	
	// ДОБАВИЛИ: переменная для сида мира
	public static int CurrentSeed = 0;
}
