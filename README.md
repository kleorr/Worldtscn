
# Worldtscn

## A custom 3D voxel engine and sandbox framework built from scratch using the Godot 4 game engine and C#.

## 🚀 Current Stage: Pre-Alpha
At this point, the project is a stable tech demo that establishes a solid foundation for dynamic world generation and sandbox gameplay mechanics.

## Key Features & Architecture (v1.2):
**Optimized Rendering Pipeline:** Efficient world management split into dynamic chunks (Chunk.cs). Chunk meshes are generated programmatically at runtime for maximum performance.

**Procedural Terrain:** Infinite landscape generation utilizing 2D Perlin noise (FastNoiseLite) to create natural hills, valleys, and biomes.

**Environment & Visuals:** Integrated a custom material and texturing system for distinct voxel types (various terrain surfaces, organic structures, and flora) along with procedural forest generation.

**Character Controller & UI:** Full first-person navigation with responsive mouse/keyboard controls and an interactive toolbar for rapid inventory item selection.

---

## 📦 Download & Updates
> ### 🚀 Get the Worldtscn Launcher
> To easily play the latest builds without manually dealing with zip files, check out our official standalone desktop application. It automatically tracks releases, handles updates, and manages game installations.
> 
> **[Download Launcher](https://github.com/kleorr/Worldtscn-Launcher)**
> 
> <img width="966" height="382" alt="Снимок экрана 2026-06-21 114315" src="https://github.com/user-attachments/assets/32b6f5b1-aeab-4fcf-8705-d583d085d659" />

---

## 🛠 Tech Stack
**Engine:** Godot 4.x (Mono/C# version)

**Language:** C#

**Algorithm:** FastNoiseLite (Perlin Noise) for procedural generation
