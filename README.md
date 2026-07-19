# Starship Craft

Build a spaceship out of blocks, Minecraft-style — then fly the thing you built.
Your design **is** the physics: mass, center of mass, where you bolted the engines
and RCS thrusters all decide how the ship handles. Survive an asteroid belt
patrolled by friendly and hostile NPC ships, with difficulty you control.

---

## Scene Setup

The entire game is code-driven — no prefabs or scene objects needed.

1. Open Unity (**2020.3.16f1**)
2. Open or create an empty scene
3. Create an empty GameObject and name it **GameManager**
4. Add the **GameManager** script component to it
5. Hit **Play**

Everything else — menus, shipyard, ships, asteroids, NPCs, starfield, effects —
is generated at runtime.

---

## Game Flow

**Menu → Settings (optional) → Build → Fly → Ship destroyed → Rebuild → Fly …**

Your blueprint survives death, so you can refit and relaunch immediately.

### Build mode (the Shipyard)

| Input | Action |
|---|---|
| Mouse aim | Highlight a block face (ghost preview shows placement) |
| LMB | Place selected block on the aimed face |
| RMB | Remove aimed block (blocks cut off from the Core break off too) |
| 1 / 2 / 3 / 4 | Select Hull / Thruster / RCS / Gun |
| WASD or middle-drag | Orbit camera · Scroll to zoom |
| Enter | Launch (needs at least one Thruster) |

**Block types**

- **Core** (cyan, pulsing) — always at the origin, exactly one. Lose it and the ship is destroyed.
- **Hull** — cheap structure and armor. Mass 1.0.
- **Thruster** — engine block with a flared nozzle bell; pushes the ship forward *from where it is mounted*. Mass 1.4.
- **RCS (turning jets)** — small pod with four side nozzles; provides turn authority, more effective mounted far from the center of mass. Mass 0.8.
- **Gun** — armored block with a long barrel; forward-firing energy cannon, more guns = higher total fire rate. Mass 1.1.

### Flight mode

| Input | Action |
|---|---|
| W / S | Throttle forward / reverse (reverse at half power) |
| Mouse | Pitch and yaw |
| Q / E | Roll |
| Left Shift | Boost (with a camera FOV kick) |
| X | Brake |
| Space or LMB | Fire |
| R | Return to shipyard (after your ship is destroyed) |

---

## The Physics Model

This is the heart of the game — the ship is a single Rigidbody assembled from
your blocks:

- **Mass & center of mass** are the mass-weighted sum of every block.
- **Convex hull collider** — dynamic Rigidbodies in PhysX require convex
  colliders, so the combined block mesh is cooked into the ship's convex hull
  (`MeshCollider.convex`). Deep concave notches are smoothed over by the hull;
  the block-level detail still matters for damage (see below).
- **Propulsion points** — each Thruster applies its force **at its own mounted
  position** (`AddForceAtPosition`). Mount both engines on the left side and
  the ship will genuinely yaw right under throttle. Balance your build around
  the center of mass or fly in circles.
- **Steering points** — RCS blocks generate torque. Authority scales with each
  block's lever arm from the center of mass, so a big heavy ship needs more
  (and better-placed) RCS to stay agile.
- **Per-block damage** — bullets and asteroid impacts destroy the block nearest
  the hit point. Blocks disconnected from the Core snap off as debris. Losing
  thrusters slows you, losing RCS makes you sluggish, losing the Core kills you.

---

## Enemies, Allies, Asteroids

- **Asteroids** drift through the belt and split when shot
  (Large 100 pts → 2 Medium 50 pts → 2 Small 25 pts). Ramming one smashes a
  block off your ship.
- **Hostile ships** (red) hunt you and your allies, lead their shots, and
  boost to close distance. Kill: **500 pts**.
- **Allied ships** (green) hunt hostiles and fly in formation near you when
  the sky is clear.
- NPC ships are built from the same block system and obey the same physics —
  shoot their thrusters off and they drift.

### Settings (difficulty)

From the main menu, **Settings** exposes sliders plus Easy / Normal / Hard presets:

| Slider | Range | What it controls |
|---|---|---|
| Asteroids | 0–30 | How many asteroids are kept alive around you |
| Asteroid speed | 1–20 | Drift speed of the rocks |
| Enemy ships | 0–8 | Hostile NPCs kept alive |
| Allied ships | 0–4 | Friendly NPCs kept alive |
| NPC speed / skill | 0.5–2 | NPC turn rate, throttle and aggression |

---

## Visuals

Everything is procedural — no textures, models, or prefabs on disk:

- Distinct block silhouettes — engine bells, gun barrels, four-nozzle RCS pods — identical in the shipyard and in flight
- Emissive neon block materials, faction-tinted (player cyan / ally green / enemy red)
- Engine plumes (particles + light) that track your throttle, RCS puffs while turning, pulsing power core
- Muzzle flashes, bullet trails, impact sparks, explosions with debris and light flashes
- Block "pop-in" placement animation and ship warp-in spawn animation
- 1,200-star particle starfield, camera shake on damage, FOV boost kick

---

## Code Architecture

All scripts live in `Assets/Script/`.

| File | Role |
|---|---|
| `GameManager.cs` | State machine (Menu/Settings/Build/Playing/GameOver), spawning & population control, follow camera + shake, all HUD via `OnGUI` |
| `GameSettings.cs` | Static difficulty settings + presets |
| `ShipBlueprint.cs` | Grid dictionary of blocks, adjacency rules, flood-fill connectivity pruning |
| `ShipBuilder.cs` | Build-mode editor: face raycasting, ghost preview, orbit camera |
| `Ship.cs` | Runtime ship: builds visuals from a blueprint, computes mass/CoM/convex hull, applies thrust at propulsion points, steering torque, per-block damage |
| `PlayerController.cs` | Input → Ship |
| `NPCController.cs` | Friend/foe dogfight AI → Ship |
| `Bullet.cs` | Faction-aware energy bolt with trail |
| `Asteroid.cs` | Split/score notifications, smashes ship blocks on contact |
| `MeshFactory.cs` | Procedural meshes: hard-edged cube, combined hull mesh for the convex collider, Perlin-displaced icosphere asteroids |
| `FX.cs` | All effects: materials, engine flames, explosions, debris, starfield, fading lights |

---

## Project Notes

- **WebGL compatible** — no file I/O, no platform-specific APIs.
- The `.blend` files and prefabs under `Assets/` are legacy from an earlier
  prototype and are unused.
- Unity version: **2020.3.16f1**
