# Starship Craft

Build a spaceship out of blocks, Minecraft-style — then fly the thing you built.
Your design **is** the physics: mass, center of mass, where you bolted the engines
and RCS thrusters all decide how the ship handles. Outside the shipyard waits a
giant planet with real gravity you can land on (or crash into), a glittering
orbital asteroid belt, and friend-and-foe NPC ships — with difficulty you control.

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

**Menu → Settings (optional) → Build → Fly → Stranded → Rebuild → Fly …**

The hard rule: **you never die**. Your Core is indestructible. But when the last
engine is shot off, you're stranded — guns and RCS still answer, so you can keep
fighting from the drifting hulk, or press **R** to respawn via the shipyard.
Score and mission clock carry over across respawns; they only reset when you
start a new run from the main menu.

### Build mode (the Shipyard)

| Input | Action |
|---|---|
| Mouse aim | Highlight a block face (ghost preview shows placement) |
| LMB | Place selected block on the aimed face |
| RMB | Remove aimed block (blocks cut off from the Core break off too) |
| 1 / 2 / 3 / 4 / 5 | Select Hull / Thruster / RCS / Gun / Armor |
| Same number, or Tab | Toggle Mk I ↔ Mk II of the selected block |
| WASD or middle-drag | Orbit camera · Scroll to zoom |
| Enter | Launch (needs at least one Thruster) |

**Block types** — every component comes in **Mk I** and **Mk II** (better *and*
cooler-looking, at the cost of extra mass):

| Block | Mk I | Mk II |
|---|---|---|
| **Core** | Indestructible heart of the ship, always at the origin. Mass 2.0 | — |
| **Hull** | Structure. Mass 1.0, 1 hit | Lighter alloy with a glowing waistband. Mass 0.8, 2 hits |
| **Thruster** | Engine with nozzle bell; pushes *from where it's mounted*. Mass 1.4 | 1.8× thrust, bigger bell, fins, blue exhaust. Mass 1.8 |
| **RCS (turning jets)** | 4-nozzle pod; turn authority scales with count and lever arm. Mass 0.8 | 1.9× authority, bigger jets in a glowing gyro ring. Mass 1.0 |
| **Gun** | Long-barrel cannon; more guns = faster combined fire. Mass 1.1 | Twin barrels, ~1.7× fire rate, faster bolts. Mass 1.4 |
| **Armor** | Bulky plated slab, soaks **3 hits**. Mass 2.2 | Reinforced glowing plates, soaks **5 hits**. Mass 3.0 |

The intended tension: more thrusters = more force, more RCS = sharper turns —
so you *can* build a massive armored battleship, but without banks of engines
it will wallow like a brick.

### Flight mode

| Input | Action |
|---|---|
| W / S | Throttle forward / reverse (reverse at half power) |
| Mouse | Pitch and yaw |
| Q / E | Roll |
| Left Shift | Boost (with a camera FOV kick) |
| X | Brake |
| Space or LMB | Fire |
| H | Toggle the controls help panel |
| R | Respawn via the shipyard — only offered while stranded (all engines gone) |

A circular **radar** sits in the bottom-right corner of the flight HUD: the top
of the dial is whatever your nose points at. Enemies are red, allies green,
asteroids grey; a dim blip pinned to the rim means the contact is out of range
in that direction.

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
- **Per-block damage & hit points** — bullets and asteroid impacts damage the
  block nearest the hit point; each block has HP (Armor soaks 3–5 hits, and
  blocks visibly scorch as they take damage). Blocks disconnected from the Core
  snap off as debris. Losing thrusters slows you, losing RCS makes you sluggish.
- **No death, only stranding** — the Core shrugs off every hit with a shield
  flare. A ship with zero thrusters left is stranded: keep fighting with
  whatever still works, or respawn. Maybe you'll find a clever way out.

---

## The Planet

A giant procedural planet (350 m radius) looms 1.2 km from the shipyard — blue
blip on the radar. It is a real physical place, not a backdrop:

- **Gravity well** — inverse-square gravity (16 m/s² at the surface) grips
  everything inside 3 planet radii: your ship, NPCs, even loose asteroids.
  Mass cancels out of free-fall, but not out of climbing: a heavy ship with
  weak engines can descend and *never make it back out*. Bring thrust.
- **Landing & crashing** — touch down under ~14 m/s and you just kick up dust;
  you can settle onto the terrain, slide along it, and take off again. Hit
  harder and the impact smashes a block off (two blocks past 30 m/s). Mountains
  are real collision geometry poking out of a glossy ocean, under a hazy
  atmosphere shell.
- **The belt** — a tilted ring of ~70 large rocks slowly orbits the planet
  amid sparkling ring dust. Gentle nudges (under 6 m/s) bounce off harmlessly,
  so a careful pilot can thread the ring; ramming one at speed costs a block.
  Belt rocks can be shot, but score nothing — they're scenery with teeth.

## Enemies, Allies, Asteroids

- **Asteroids** drift through the belt and split when shot
  (Large 100 pts → 2 Medium 50 pts → 2 Small 25 pts). Ramming one smashes a
  block off your ship.
- **Hostile ships** (red) hunt you and your allies, lead their shots, and
  boost to close distance. Kill: **500 pts**.
- **Allied ships** (green) hunt hostiles and fly in formation near you when
  the sky is clear.
- NPC ships are built from the same block system and obey the same physics —
  shoot their thrusters off to finish them. Past skill 1.3 on the settings
  slider, NPCs fly Mk II hardware.

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
- Procedural radar dial with faction-colored blips, and an H-key controls overlay

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
| `Planet.cs` | The planet: gravity well, terrain/ocean/atmosphere, orbiting belt ring |
| `Asteroid.cs` | Split/score notifications, planet gravity, ram damage with a gentle-bump threshold |
| `MeshFactory.cs` | Procedural meshes: hard-edged cube, combined hull mesh for the convex collider, Perlin-displaced icosphere asteroids |
| `FX.cs` | All effects: materials, engine flames, explosions, debris, starfield, fading lights |

---

## Project Notes

- **WebGL compatible** — no file I/O, no platform-specific APIs.
- The `.blend` files and prefabs under `Assets/` are legacy from an earlier
  prototype and are unused.
- Unity version: **2020.3.16f1**
