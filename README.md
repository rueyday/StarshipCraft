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
| A / D | Turn left / right (yaw, same as mouse) |
| Q / E | Roll |
| G | Anchor lock — the ship freezes dead still against gravity, impacts, everything; guns still fire (turret mode) |
| F | Armor mode — while raised, every hit is soaked by your nearest Armor block; the shield holds until your plating is shot away |
| Left Shift | Boost (with a camera FOV kick) |
| X | Brake |
| Space or LMB | Fire |
| T | Turbo — huge straight-line speed for travel; drains the engine heat bar (Mk II engines last much longer), guns offline until 1 s after dropping out |
| E | Refit at the carrier (land slowly on the glowing pad first) |
| 1 / 2 / 3 | Camera: chase / rear view / free spectator cam (free cam flies with WASD + mouse while the ship drifts) |
| M (or click the radar) | 3D system map — the camera orbits the real world from afar with labeled bodies; drag to orbit, scroll to zoom |
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

## The Star System — one continuous world

No maps, no loading screens: everywhere is just coordinates. You launch from
the **carrier** and fly wherever the radar blips point. Gravity uses the
**cloud-layer model**: constant gravity below each world's cloud deck, fading
linearly to zero inside the clouds, pure zero-g above them — space battles
stay clean, and punching up through a cloud deck is how you feel the ship go
weightless.

- **The Carrier** (cyan blip, your spawn) — a landable flight deck with glowing
  runway strips. Settle onto the marked pad under 6 m/s and press **E** to
  refit in the shipyard without leaving the world; launches and stranded
  respawns return you to the deck.
Planets work No Man's Sky-style. In space you see every globe at once —
they're landmarks you fly **into**, not meshes you land on. Punch through a
globe's translucent **cloud shell** and the game swaps you into that planet's
own surface scene: a **flat** ground under that world's weather (flat terrain
is cheap to render and plays better than a giant sphere). Each surface
**loops**: fly straight for 3 km and you seamlessly arrive back where you
started — that's how a flat map plays "round". Climb back above the clouds
(~650 m) and you pop out into space beside the globe.

- **Korrath** (blue blip) — a 1.4 km rocky globe with oceans and a tilted
  orbital belt of ~70 rocks amid ring dust (gentle bumps under 6 m/s bounce
  harmlessly). Inside, its sky is **cloud banks**: soft white fog and huge
  drifting puffs over dark rock. Land under ~14 m/s for dust, not damage;
  harder impacts smash blocks (two past 30 m/s).
- **Vessa** (blue blip) — a 650 m ice globe. Inside: a **snow storm** —
  pale-blue fog and flakes streaming down over pale ground.
- **Titanhold** (orange blip) — the Saturn of the system: a 3.5 km
  butterscotch globe wrapped in a **four-band golden ring** (9–12 km out,
  spinning, with embedded debris racing past at over a kilometer per second).
  The ring is lethal: anything inside the band loses a block roughly twice a
  second. Inside the globe: a **giant dust storm**, sand-gold fog and
  wind-blown grit down to wind-scoured flats.

**Turbo** covers the distances: hold T for ~220 m/s straight-line flight. It
drains a heat pool fed by your engines (Mk I ≈ 4 s each, Mk II ≈ 10 s each,
regenerating at half rate), turns go wide, and weapons stay offline until a
second after you drop out — no turbo-sniping.

## Enemies, Allies, Asteroids

- **Asteroids** drift through the belt and split when shot
  (Large 100 pts → 2 Medium 50 pts → 2 Small 25 pts). Ramming one smashes a
  block off your ship.
- **Hostile ships** (red) hunt you and your allies, lead their shots, and
  boost to close distance. Kill: **500 pts**. About one in five is an
  **armored gunship** — an 18-block heavy with an armor prow, four engines
  and triple guns, worth **900 pts**.
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
| Mouse sensitivity | 0.2–1.5 | Flight stick feel (plus an Invert Y toggle) |

---

## Visuals

Everything is procedural — no textures, models, or prefabs on disk:

- Distinct block silhouettes — engine bells, gun barrels, four-nozzle RCS pods — identical in the shipyard and in flight
- Emissive neon block materials, faction-tinted (player cyan / ally green / enemy red)
- Engine plumes (particles + light) that track your throttle, RCS puffs while turning, pulsing power core
- Muzzle flashes, bullet trails, impact sparks, explosions with debris and light flashes
- Block "pop-in" placement animation and ship warp-in spawn animation
- 1,200-star particle starfield, a blazing sun disc with halo, slowly rotating
  planet globes with counter-drifting cloud shells
- The carrier lives: blinking nav beacons (green bow / red stern / cyan mast),
  glowing runway chevrons, triple engine bells, antenna masts
- Surface dressing per world: dunes and wind-scour on Titanhold, ice mounds and
  frozen lakes on Vessa, moss fields, ponds and worn slabs on Korrath — plus
  per-world sky colors and lightning deep in Korrath's cloud banks
- Damaged blocks scorch, then leak sparks and smoke until they break; Mk II
  engines burn blue-hot
- Title cards on scene transitions ("ENTERING KORRATH — CLOUD BANKS"), a
  speed/altitude readout, camera shake on damage, FOV boost kick
- Procedural radar dial with faction-colored blips, and an H-key controls overlay
- Shipyard engineering panel (mass, acceleration, turn, turbo pool) with
  center-of-mass and thrust-centroid markers — align them to fly straight

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
| `GravityField.cs` | Cloud-layer gravity: constant below clouds, fades in-band, zero above (space itself is zero-g) |
| `Planet.cs` | `PlanetDef` world data + `SpacePlanet` globes: terrain/ocean/cloud shell, belt, the deadly spinning ring |
| `SurfaceWorld.cs` | Per-planet flat surface scene with toroidal wrap (the 3 km loop) |
| `Weather.cs` | Per-world atmosphere scenes: dust storm / cloud banks / snow storm |
| `Carrier.cs` | The carrier: landable deck, refit pad, spawn point |
| `Asteroid.cs` | Split/score notifications, planet gravity, ram damage with a gentle-bump threshold |
| `MeshFactory.cs` | Procedural meshes: hard-edged cube, combined hull mesh for the convex collider, Perlin-displaced icosphere asteroids |
| `FX.cs` | All effects: materials, engine flames, explosions, debris, starfield, fading lights |

---

## Project Notes

- **WebGL compatible** — no file I/O, no platform-specific APIs.
- The `.blend` files and prefabs under `Assets/` are legacy from an earlier
  prototype and are unused.
- Unity version: **2020.3.16f1**
