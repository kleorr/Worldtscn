
# Worldtscn

## A custom 3D voxel engine and sandbox framework built from scratch using the Godot 4 game engine and C#.

## 🚀 Current Stage: Pre-Alpha
At this point, the project features a stable core mechanics foundation, supporting survival systems, inventory management, crafting, multiplayer interaction, and procedural world generation.

<img width="1280" height="719" alt="image" src="https://github.com/user-attachments/assets/e8624e6a-fb98-4207-9e41-2277af09e0c5" />
<img width="1918" height="1079" alt="image" src="https://github.com/user-attachments/assets/47aaf2c4-e9d9-4b96-b15a-087bf7983efb" />



## Key Features & Architecture (v1.7):
- **Dynamic Generation Pipeline:** Procedural terrain generation using 2D Perlin noise (`FastNoiseLite`), supporting oceans, rivers, and mountains. Terrain chunks dynamically build and load relative to the player's position, backed by rendering optimizations.
- **Survival & Creative Modes:** Choose between full Survival mode (with block mining hardness, item drops, and pick-up physics) or Creative mode (instant break, flight, and free resources).
- **Inventory & Crafting System:** 36-slot inventory, hotbar with stack limits (up to 64), and a 2x2 crafting grid with functional recipes (e.g., logs to planks, planks to items).
- **Procedural Water Mechanics:** Real-time cellular-automata fluid simulation supporting dynamic water spread, level updates, and interactive physics.
- **Character Customization:** In-game profile customization allowing players to set custom nicknames and colors, rendered via 3D character previews in the menu and fully synced over multiplayer.
- **Multiplayer & Networking:** Built-in ENet peer-to-peer multiplayer, LAN/IP hosting, real-time synchronization of player profiles, item drops, block changes, and a fully functional in-game chat with console commands (`/give`, `/fly`).
- **Optimized Multi-Mesh Rendering & Mining:** Dual-pass mesh generation separating opaque blocks from transparent fluids (`Chunk.cs`), plus dynamic procedural destruction crack textures (0–9 stages) while mining.
- **Saves & Settings:** Comprehensive world and player data saving/loading via JSON, customizable seed input, render distance scaling, and custom terrain generators (Standard & Flat).

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
- **Engine:** Godot 4.x (Mono/C# version)
- **Language:** C#
- **Noise Generation:** FastNoiseLite (Perlin Noise) 

<img width="541" height="468" alt="image" src="https://github.com/user-attachments/assets/45b727da-fb75-4a82-917e-23014e992b9f" />

```
