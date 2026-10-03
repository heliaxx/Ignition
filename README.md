# Ignition

A 3D space combat game built with **Godot 4**. Fly a fully Newtonian
6-DOF fighter through asteroid fields, dogfight AI enemies or other players
and race through checkpoints - in your cockpit or from a cinematic external view.

## Game modes

- **Free Flight** - open sandbox with station, asteroid fields and unlimited ammo.
- **Rush** - high-speed scoring run: chase rings through a dense, endless asteroid field.
- **Waves** - survive escalating waves of enemy fighters; scored by kills.
- **Skirmish** - attack a squadron of enemy fighters guarding their base; scored by how fast you take care of them.
- **Multiplayer deathmatch** - up to 12 player lobbies. The host picks a networking option, and sets kill (5-25) and time limits (5-20 min). When all players ready up, he can start the game.

## Features

- **Newtonian flight model** - full 6 degrees of freedom with real inertia: thrust, strafe on three axes, roll/pitch/yaw, boost with mechanic, and a precision-stop assist.
- **Flight assist** - the thrusters cancel drift and unwanted rotation and the throttle sets a target speed (thrust or throttle mode). The toggle key switches between full assist and a second setting chosen in Options -> Controls: off (pure Newtonian flight) or partial (rotation held, drift kept).
- **Weapons** - gimbal-tracking main gun with lead prediction reticle, and physics-based guided missiles.
- **Targeting** - target cycling, missile lock-on with gimbal-cone lock timer, lead indicator for guns.
- **AI opponents** - complex-behavior fighters (pursue, evade, orbit, joust, flee) with obstacle avoidance.
- **Procedural world** - Endless asteroid field realised with Poisson Disc Sampling, GPU instancing , destructible asteroids, and priority chunk loading for maximum performance and immersion.
- **Multiplayer** - server-authoritative netcode with a lobby browser. Lobbies run over **Steam** or **Epic Online Services (EOS)**. You can also host and join directly over IP (ENET, port 30500).
- **Settings** - rebindable controls, mouse and flight assist options, graphics options (window mode, resolution, render scale, AntiAliasing, VSync etc.), and audio volumes - all persistent in `user://settings.ini`.

## Default controls

| Input | Action |
|---|---|
| Mouse | Yaw/Pitch |
| `W` / `S` | Thrust forward / backward |
| `A` / `D` | Strafe left / right |
| `Space` / `Alt` | Strafe up / down |
| `Q` / `E` | Roll left / right |
| `Tab` | Boost |
| `Shift` | Precision stop |
| `Z` | Flight assist on / off |
| `X` | Relative mouse on / off (aim cursor re-centres or stays put) |
| Left mouse | Fire gatling |
| Right mouse | Fire missile |
| Middle mouse (hold) | Look around |
| `T` | Select target ahead |
| `F` | Cycle targets |
| `C` | Switch camera (cockpit / external) |
| `L` | Toggle lights |
| `F1` (hold) | Scoreboard (multiplayer) |
| `Esc` | Pause menu |

Controls are rebindable in **Options -> Controls**.

## Running from source

1. Install [Godot 4.7.2 (.NET/mono edition)](https://godotengine.org/download) and the [.NET SDK](https://dotnet.microsoft.com/download) (8.0+).
2. Clone the repository and open `project.godot` in the Godot editor.
3. Build the C# solution (Godot prompts on first run, or use `dotnet build Ignition.csproj`).
4. Run with <kbd>F5 / Launch project</kbd>.

Multiplayer transports:

- **ENET** (direct IP) always works.
- **Steam** lobbies appear when the Steam client is running.
- **EOS** lobbies need the EOS client secret, which is not in the repository. Put it in an untracked `Scripts/EosSecret.gd`:

  ```gdscript
  const CLIENT_SECRET := "your-client-secret"
  ```

  Without it, the game runs normally and simply does not offer EOS option.

## Project layout

```
Scenes/     Game scenes (levels, menus, ships, weapons, structures)
Scripts/    Game code (mainly C#)
States/     NPC state machine
Shaders/    Visual shaders
Resources/  Shared materials and collision shapes
Imports/    Third-party models, textures, sounds, fonts etc.
addons/     Current Godot addons: GodotSteam and Epic Online Services
```

## License

The **source code** is licensed under the **GNU GPL v3.0** - see [LICENSE](LICENSE).

For **third-party assets** (models, textures, audio, fonts, addons)
attribution and per-asset licensing, see [credits.md](credits.md).