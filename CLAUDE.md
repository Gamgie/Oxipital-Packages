# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this repository is

Two Unity packages (Unity `6000.3`, HDRP) extracted from the Oxipital-Engine project, so the engine can be added to
any Unity project:

- `Core/` = `com.gamgie.oxipital.core` ("Oxipital Core"): the particle "ballet" of glowing orbs (VFX Graph), force
  fields, infinite life mode, optical flow, Gaussian splats, basic emitter shapes.
- `Camera/` = `com.gamgie.oxipital.camera` ("Oxipital Camera"): orbital / spaceship / SOMI sensor camera modes
  (Cinemachine), Spout / Syphon output, dome rig follow, post-processing controls.

The two packages are independent: neither references the other (keep it that way). Everything is driven live from
Chataigne over OSCQuery.

This repo is developed as a git submodule of [Oxipital-Engine](https://github.com/Gamgie/Oxipital-Engine) (at
`Oxipital-Packages/`), whose Unity project references both packages with `file:` paths, so they are editable there.
Other projects install them read-only by git URL and version tag (see README.md). There are no tests and no build
tooling: everything is checked inside the Unity Editor of the Oxipital-Engine project.

## Packaging rules

- **Every file and folder needs a committed `.meta`.** A package installed by git URL is immutable: Unity cannot
  generate missing `.meta` files and ignores those assets. When adding a file or folder from the Oxipital-Engine
  Editor, Unity creates the `.meta` in the submodule: commit it along with the file.
- **Never change a `.meta` GUID.** Scenes and prefabs of every project reference assets by GUID.
- Folders ending with `~` (`Core/Emitters~`) are ignored by Unity's importer: no `.meta` inside.
- Code lives in `namespace Oxipital`. Assemblies: `Gamgie.Oxipital.Core` (`Core/Runtime`),
  `Gamgie.Oxipital.Core.Editor` (`Core/Editor`), `Gamgie.Oxipital.Core.GaussianSplatting`
  (`Core/Runtime/Scripts/GaussianSplatting`, only compiled when `org.nesnausk.gaussian-splatting` is installed),
  `Gamgie.Oxipital.Camera` (`Camera/Runtime`). A new dependency goes both in the `.asmdef` references and in the
  `package.json` dependencies (registry packages only: Unity does not allow git dependencies in a package).
- **Do not rename MonoBehaviour classes, public fields, public methods or prefab GameObjects.** OSCQuery builds the
  Chataigne addresses from the GameObject hierarchy, the short class name (namespace ignored) and the public field
  names, and the Chataigne session/module map them by those names. Public fields and public methods are exposed to
  Chataigne automatically: keep new internal state private or mark it `[OSCQuery.DoNotExpose]`.
- A `[SerializeReference]` type (e.g. `OxiSplatPass`, stored by type name in a CustomPassVolume) that changes
  name, namespace or assembly needs `[MovedFrom]` so existing scenes still find it.
- Releasing: bump `version` in both `package.json`, commit, then tag the commit `vX.Y.Z` (one tag covers both
  packages). Projects pick it up by changing `#vX.Y.Z` in their manifest.

## Architecture

### Generic item/manager pattern (`Core/Runtime/Scripts/Tools/BaseItem.cs`, `BaseManager.cs`)

Most runtime-spawned, live-tunable objects follow a manager/item pair:

- `BaseItem` — a killable `MonoBehaviour` with a fade-out (`kill(time)`) driven by `killProgress` in `Update`.
- `BaseManager<T>` — spawns/despawns `T` (a `BaseItem`) children under itself to match a live `count` float
  each frame (`addItem` / `removeLastItem`), so the number of instances can be smoothly ramped from a show
  controller. Subclasses override `addItem`, `killLastItem`, `getKillTime` for custom spawn/despawn behavior.

This pattern is reused for orbs (`OrbManager : BaseManager<OrbGroup>`), physics force fields
(`StandardForceManager : BaseManager<StandardForceGroup>`), and dancers (`DancerGroup<T> : BaseManager<T>`).

### Ballet / particle system (`Core/Runtime/Scripts/Ballet/`, `OrbManager.cs`, `OrbGroup.cs`)

- `Ballet.cs` is the top-level per-frame conductor: each frame it collects `GraphicsBuffer`s from every
  `StandardForceGroup` (named force fields, e.g. wind/attractors) and hands them to every `OrbGroup`'s VFX Graph
  via `OrbGroup.setForceBuffers`, and sums `aliveParticleCount` across all orb groups into `totalParticles`.
  `OrbVFX.vfx` exposes 4 force buffer slots (`Force Group 1` to `4`, plus their `B` copies).
- `DancerGroup<T>` (generic, `T : Dancer`) manages a set of `Dancer` transforms ("dancers") arranged by one or
  more `DancePattern`s (Circle/Line/NBodyProblem/Manual — `Core/Runtime/Scripts/Ballet/Patterns/`), and packs their
  per-dancer state (position, rotation, intensity, size) plus group-level fields into a single `GraphicsBuffer`
  that's fed straight into a VFX Graph as a Structured Buffer.
  - Group-level float fields destined for that buffer are marked with `[InBuffer(index)]`
    (`Core/Runtime/Scripts/Ballet/InBufferAttribute.cs`); `DancerGroup` uses reflection over these attributes to
    pack the buffer layout (`initBufferAndFieldInfoList` / `getList`). When adding a new buffer-exposed field on
    an `OrbGroup`/`DancerGroup` subclass, give it the next free `InBuffer` index and keep `DANCER_DATA_SIZE`
    (per-dancer floats) and the VFX Graph's buffer-reading nodes in sync.
    - Indices are field slots, not field counts: a `Vector3`/`Color` takes 3 consecutive slots (e.g. `emitterScale`
      at 32 uses 32–34). Inserting a field in the middle shifts every later index, so every graph read of
      "Orb Buffer" (`OrbVFX.vfx`, `OxipitalInit/Update/OutputLit/OutputQuadUnlit`, `StandardForce`,
      `PositionColorSpawn`) and the `ORB_*` defines in `Core/Runtime/VFX/Shaders/OxipitalHelpers.hlsl` must be
      shifted too — prefer appending at the next free index instead.
    - The buffer starts with a 2-float header (dancer count, dancer start index). In the graphs, an orb field is
      read as `Sample Graphics Buffer(Orb Buffer, Add(fieldIndex, Orb Index Offset = 2))` — the constant on the
      Add node is the `InBuffer` index. Custom HLSL uses `getBufferFloatProperty(index, buffer)`, which adds the
      +2 itself. Don't confuse with the `Orb Index` inputs (0–9) on the subgraph blocks in `OrbVFX.vfx`, which
      select the orb, nor with dancer-data reads (`GetDancerFloat/Vector3`, relative to the dancer start index).
    - The canonical layout table is the Notion DB "DancerGroup Buffer Structure" (Documentation Oxipital >
      Standard Buffer Organization); keep it in sync when indices change.
  - `DancePattern.updatePattern<T>` blends a target position onto each dancer's local position using
    `blendMode` (Add/Multiply/Replace) and a smoothed `weight`; concrete patterns only need to implement
    `getPatternPositions`.
  - `OrbGroup : DancerGroup<Dancer>` is the concrete emitter: it owns a `VisualEffect` (VFX Graph) and pushes
    emitter shape/appearance/physics parameters (also `[InBuffer]`-tagged) into it every frame, plus loads
    emitter meshes/textures (via `MeshLoader`, using glTFast to load `.glb` files from the project's
    `Assets/StreamingAssets/emitters`) and optional Spout-fed textures.
  - Emitter shapes: Unity only reads `StreamingAssets` from the project's `Assets`, so the basic shapes ship in
    `Core/Emitters~` and `Core/Editor/EmitterInstaller.cs` copies them into the project (automatically when the
    project has no `emitters` folder yet, or from the menu *Oxipital > Installer les émetteurs de base*).
  - **Infinite life mode** (`OrbGroup.infiniteLife`, `Core/Runtime/VFX/Shaders/InfiniteLifeInit.hlsl` /
    `InfiniteLifeUpdate.hlsl`): particles never die and the dancer intensity sets the particle count
    (`intensity × maxInfiniteParticles`) instead of the spawn rate. The rate spawner is stopped
    (`Emitter Intensity` = 0) and `updateInfiniteLife()` sends a `SpawnInfinite` event with the missing count,
    capped to the free VFX capacity (alive count readback lags ~1 s, so spawns from the last 2 s are counted as
    used). Each spawned particle gets a unique custom attribute `infiniteRank` (`infiniteRankBase + spawnIndex + 1`,
    0 = normal particle). In Update, ranks above `infiniteCount` are killed (lowering intensity removes particles)
    and the others have their age held at `INFINITE_LIFE_HOLD` (10%) of their lifetime so over-life curves still
    apply. Switching the mode off or `kill()` clears ranks so particles age and fade out normally. Buffer fields:
    `infiniteLife` = 1, `infiniteCount` = 2, `infiniteRankBase` = 35 (next free `InBuffer` index: 36).
    `gridDimension` (9–11) / `gridDensity` (12) are reserved in the buffer for the upcoming grid emission and not
    read by the VFX yet.
    Known limits: KillDarkParticles can kill some ranks at spawn in UnlitAdditive; counts can drift if the effect
    isn't simulated on a frame where the count changes (toggle the mode off/on to resync).
  - Custom HLSL blocks that read "Orb Buffer" in a context where a Sample Buffer node also reads it must declare
    it `StructuredBuffer<Single>` (`#define Single float` in `OxipitalHelpers.hlsl`), otherwise VFX Graph sees
    two different buffer types.

### Optical flow (`Core/Runtime/Scripts/OpticalFlow/`, `Core/Runtime/Shaders/OpticalFlow/`)

Kinect → TouchDesigner → Spout ("OxipitalFlow") → `OpticalFlowSource` → `OpticalFlowCompute` →
`OpticalFlowProcessor` → `OpticalFlowMetrics`, run in order by `OpticalFlowManager` in `LateUpdate`. `Ballet`
pushes the result into every `OrbGroup` through `OpticalFlowManager.ApplyTo` (the "Optical Flow Force" block,
`Core/Runtime/VFX/Shaders/OpticalFlowForce.hlsl`). `FlowPlane` places the 2D flow in the 3D scene.

### Live OSC control (OSCQuery / Chataigne)

Public fields on `MonoBehaviour`s are auto-exposed over OSCQuery (via `com.benkuper.oscquery`) for control from
an external show-control tool (Chataigne). Mark any field that should stay internal/not be exposed with
`[OSCQuery.DoNotExpose]` (see `Dancer.cs`, `OrbGroup.meshName`). OSCQuery only reflects flat public fields per
component — it can't see values living in a list on a parent manager — which is why e.g.
`GaussianManager` looks components up by name across children (`SetVisibility(splatName, value)`) rather than
exposing a collection. The Chataigne module itself lives in the Oxipital-Engine repo (`Chataigne/modules/Oxipital`).

### Camera system (`Camera/Runtime/Scripts/`)

- `CameraController` is the top-level camera state machine: it owns a Cinemachine setup (`CinemachineBrain` +
  one `CinemachineCamera` per mode) and switches between `CameraMovementType.Orbital / Spaceship / Sensor`
  (`OrbitalMovement`, `SpaceshipMovement`, `SensorMovement`, each a `CameraMovement` subclass). On switch it
  hands the outgoing camera's live position/rotation to the incoming one (`SetCameraTransform`) so the
  Cinemachine blend starts smoothly instead of snapping. It also drives shared FOV/ortho/noise/transition-time
  parameters into whichever camera is active, and toggles `isFullDome` for dome-rig output. `xWing` (the model
  shown with the spaceship camera, `Camera/Runtime/Models/XWing`) and `domeCameraRig` are optional.
  `renderMainWindow` is exposed to Chataigne but drives nothing. It used to show or hide Graphy through an object
  tagged "Tools Manager" that no scene ever had, so Chataigne sessions keep it off: do not wire it to anything
  without checking them (Graphy is shown by the host project's `ToolsManager.activateGraphy`).
- `CameraMovement` (abstract) is the interface each movement mode implements: `Init`, `UpdateMovement`,
  `SetActive`, `Reset`, `UpdateFOV`/`UpdateOrthoSize`, `UpdateZOffset`, `UpdateNoiseParameter` (Cinemachine Perlin
  noise), `SetCameraTransform`.
- Rig animations (move/rotate to a view, calibration blend) use `CameraTween`, a tiny replacement for the former
  DOTween calls (ease OutQuad, advanced in the owner's `Update`, `Rotate` interpolates each Euler axis the shortest
  way like DOTween's `RotateMode.Fast`). The package must not depend on DOTween (not a UPM package).
- `SensorMovement` drives an orbit camera from a body-worn SOMI-1 IMU: its transform is the pivot, pinned on
  `centerPosition` and rotated by the sensor; `CM Sensor Camera` (CinemachineFollow, LockToTarget binding so roll
  is kept, + RotateWithFollowTarget) sits at `FollowOffset` = (0, 0, -`CameraController.followZOffset`) and looks
  at the center. Chataigne maps the sensor `Orientation` (Euler degrees, as one Point3D) onto
  `sensorOrientation`. The Euler triple is turned into a quaternion right away (`eulerOrder`, then
  `axisSwizzle`/`axisSigns` to Unity's frame) and everything else is quaternion math: the body rotation is a
  delta against the neutral pose (`neutralSensorOrientation`, raw sensor degrees, captured by `CalibrateNeutral()`
  and persisted in `PlayerPrefs`), glitch-filtered per sensor update (`maxTurnSpeed`, deg/s: a faster change is
  held; if it lasts over 0.2 s it re-bases a correction instead of moving — the SOMI's heading sweeps at
  900-3000 deg/s when its X is near ±90, i.e. when it is strapped vertically), split by swing-twist around world up
  into a tilt and a turn that is unwrapped frame to frame into the continuous multi-turn `turnAngle` (so
  `yawAmount` != 1 never jumps at ±180), scaled by `yawAmount`/`tiltAmount`, optionally inverted
  (`invertDirection`, default on = the world turns with the performer), applied on top of `neutralView`
  (where the camera looks in the neutral pose), and slerp-smoothed in `Update` (frame rate, not FixedUpdate).
  Calibration/`Reset` ease in over `calibrationBlendTime` through a decaying offset; switching modes hands
  nothing over (the pivot stays on the center), Cinemachine's blend smooths the switch. Never convert back to
  Euler or smooth per Euler axis (gimbal lock, ±180° spins).
- `DomeCameraRigFollow` re-targets a dome rig transform to follow a camera target, compensating for the
  `com.pfc.dome-tools` rig being offset from the projection center (the dome-tools package itself is only
  needed by projects with dome scenes, it is not a dependency of the package).
- `CameraSpoutManager` (`[ExecuteInEditMode]`) owns the output `RenderTexture` that the main camera renders
  into, and streams it out via `Klak.Spout.SpoutSender` on Windows or `Klak.Syphon.SyphonServer` on macOS
  (platform is chosen at runtime via `Application.platform`); output resolution is persisted in `PlayerPrefs`.

### Gaussian splats (`Core/Runtime/Scripts/GaussianSplatting/`, `Core/Runtime/Scripts/OxiSplat/`)

Splat scenes use `org.nesnausk.gaussian-splatting` (aras-p's `UnityGaussianSplatting`). Each splat has its own
`GaussianSplatVisibility` component (same GameObject as its `GaussianSplatRenderer`) so it can be addressed
directly over OSCQuery; `GaussianManager` is just a by-name lookup/convenience wrapper
(`FadeIn`/`FadeOut`/`SetVisibility`) over the child splats — for the same "OSCQuery can't see a list" reason
described above. These two live in their own optional assembly, compiled only when the splatting package is
installed. `OxiSplat*` is Oxipital's own splat renderer (HDRP CustomPass, `.ply` importer in `Core/Editor/OxiSplat`),
independent of that package.

## Third-party packages of note

- `com.unity.render-pipelines.high-definition` — HDRP.
- `com.benkuper.oscquery` (scoped registry `package.openupm.com`) — OSCQuery server, live parameter exposure to
  Chataigne.
- `jp.keijiro.klak.spout` / `jp.keijiro.klak.syphon` (scoped registry `registry.npmjs.com`) — video texture sharing.
- `org.nesnausk.gaussian-splatting` (git dependency, aras-p/UnityGaussianSplatting) — Gaussian splat rendering,
  optional, must be added by each project itself.
- `com.unity.cloud.gltfast` — runtime `.glb` mesh loading (`MeshLoader` in `OrbGroup.cs`).
- `com.unity.cinemachine` — camera rigs/blending.
