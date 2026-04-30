# Starship Craft

A 3D space shooter built in Unity. Fly a procedurally-generated dart ship, blast through waves of rocky asteroids that split apart on hit, and rack up the highest score you can before you run out of lives.

---

## Scene Setup

The entire game is code-driven — no prefabs or scene objects needed beyond one empty GameObject.

1. Open Unity (2020.3.16f1)
2. Open or create any scene
3. Delete everything in the Hierarchy (or start from an empty scene)
4. Create an empty GameObject and name it **GameManager**
5. Add the **GameManager** script component to it
6. Hit **Play**

The GameManager creates the player ship, follow camera, and all asteroid meshes at runtime.

---

## Controls

| Key / Input | Action |
|---|---|
| W / S | Forward / backward thrust |
| A / D | Strafe left / right |
| Mouse | Pitch and yaw |
| Q / E | Roll left / right |
| Left Shift | Boost (2.5× thrust) |
| X | Brake |
| Space or LMB | Fire |
| R | Restart (game over screen only) |

---

## Game Rules

- You start with **3 lives**. Losing all 3 ends the game.
- Colliding with an asteroid costs one life. After losing a life you respawn at the origin with 3 seconds of invincibility (ship flashes).
- **Large asteroids** (100 pts) split into 2 medium on hit.
- **Medium asteroids** (50 pts) split into 2 small on hit.
- **Small asteroids** (25 pts) are destroyed cleanly.
- A new wave begins as soon as all asteroids on screen are cleared. Each wave spawns `5 + (wave - 1) × 2` large asteroids.

---

## Code Architecture

All scripts are in `Assets/Script/`. The game is fully self-contained — no save system, no scene manager, no external assets required.

### `GameManager.cs`
The single entry point and game loop. Handles:
- Game states: **Menu → Playing → Dead → GameOver**
- Spawning and tracking all asteroids
- Spawning and respawning the player ship
- Wave progression (clears when asteroid list is empty)
- Follow camera (smooth lerp behind the ship)
- The entire HUD via `OnGUI` (score, wave, lives, crosshair, game over screen)

Key public methods called by other scripts:
```
OnAsteroidDestroyed(Asteroid)  — scores points, splits asteroid, removes from list
OnPlayerHit()                  — decrements lives, triggers respawn or game over
SpawnAsteroid(pos, size)       — instantiates an asteroid GameObject at runtime
```

### `PlayerShip.cs`
Rigidbody-based 6DOF ship controller. Attached to the player GameObject by GameManager at runtime.
- `FixedUpdate` applies thrust, strafe, boost, brake, and mouse torque (pitch/yaw/roll)
- `Update` handles shooting and invincibility flicker
- `BeginInvincible(duration)` disables the collider and flashes the renderer for the given duration
- Fires bullets by creating a primitive sphere with a `Bullet` component; the bullet inherits ship velocity so it doesn't fall behind

### `Asteroid.cs`
Minimal behaviour script. The physics (Rigidbody velocity, angular velocity) and mesh are set up by `GameManager.SpawnAsteroid`. This script only handles:
- `Hit()` — notifies GameManager then destroys the GameObject
- `OnCollisionEnter` — notifies GameManager when the player tag is detected

### `Bullet.cs`
Auto-destroys after 4 seconds. On collision with an `Asteroid`, calls `ast.Hit()` and destroys itself. Nothing else.

### `MeshFactory.cs`
Static utility class. Generates all meshes in code — no `.fbx` or `.blend` imports needed.
- `CreateShipMesh()` — 6-vertex dart shape (nose, two wing tips, tail, dorsal fin, belly) assembled from 8 triangles
- `CreateAsteroidMesh(seed, radius)` — icosphere subdivided twice, then each vertex displaced along its normal using Perlin noise seeded per-asteroid, giving each one a unique rocky silhouette

---

## Project Notes

- **Save system removed** — was previously `File.WriteAllText` which breaks in WebGL. The ship builder was removed in favour of this simpler game.
- **All meshes are procedural** — no dependency on the `.blend` files in `Assets/3D models/`, those are legacy from the builder prototype.
- **WebGL compatible** — no file I/O, no platform-specific APIs. Build via *File → Build Settings → WebGL* to publish online.
- Unity version: **2020.3.16f1**
