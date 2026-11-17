# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this repository is

Two Unity packages (Unity `6000.3`, HDRP) extracted from the Oxipital-Engine project, so the engine can be added to
any Unity project:

- `Core/` = `com.gamgie.oxipital.core` ("Oxipital Core"): the particle "ballet" of glowing orbs (VFX Graph), lights, force
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
- **A prefab must work when dropped alone in an empty scene of another project.** References set only on scene
  objects in Oxipital-Engine do not travel with the package: `Cameras.prefab` shipped in 1.1.0 with four of them
  empty (the cameras' `virtualCamera`, `CameraController.camera` and `cinemachineBrain`). No machine path either
  (`Screenshot.filepath` is left empty in the prefab, each project sets its own folder).
- **Wrap legacy `UnityEngine.Input` reads in `#if ENABLE_LEGACY_INPUT_MANAGER`.** A project set to the Input System
  only throws on every such read (see `Screenshot.cs`). `MouseOrbitImproved` and `OpticalFlowTestSource` still read
  it unguarded: they are in no prefab.
- Releasing: bump `version` in both `package.json`, commit, then tag the commit `vX.Y.Z` (one tag covers both
  packages). Projects pick it up by changing `#vX.Y.Z` in their manifest.
- **No allocation in per-frame code** (`Update`, `LateUpdate`, `FixedUpdate`): the show runs at 60 FPS with
  almost no margin. Use `Shader.PropertyToID` instead of property names, reuse lists and arrays
  (`GetComponentsInChildren(list)`), never read `gameObject.name` or attributes per frame, and send values to a VFX
  only when they change when that is costly (gradients, textures). Measured on 2026-10-20: these rules took the
  orb and force group scripts from about 2.6 ms and 104 KB of garbage per frame to about 0.4 ms and none.

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
    (`Core/Runtime/Scripts/Ballet/InBufferAttribute.cs`). `InBufferLayout` (same file) reads these attributes once
    per type and compiles a getter per field; each frame `DancerGroup.fillBufferData` writes the values into a
    reused float array, without reflection nor allocation. When adding a new buffer-exposed field on
    an `OrbGroup`/`DancerGroup` subclass, give it the next free `InBuffer` index and keep `DANCER_DATA_SIZE`
    (per-dancer floats) and the VFX Graph's buffer-reading nodes in sync.
    - Indices are field slots, not field counts: a `Vector3`/`Color` takes 3 consecutive slots (e.g. `emitterScale`
      at 32 uses 32–34). Inserting a field in the middle shifts every later index, so every graph read of
      "Orb Buffer" (`OrbVFX.vfx`, `OxipitalInit/Update/OutputLit/OutputQuadUnlit/OutputMesh`, `StandardForce`,
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
    `Assets/StreamingAssets/emitters`) and optional Spout-fed textures. `MeshLoader` gives glTFast an
    `UninterruptedDeferAgent` (1.7.4): without one, glTFast creates a "glTF-StableFramerate" GameObject, and as
    `loadMeshes` is `async`, a Play mode stopped mid-load finished in the Editor, where that object stayed in the
    open scene and got saved with it. The few small emitter files now load in one go at start.
  - Emitter shapes: Unity only reads `StreamingAssets` from the project's `Assets`, so the basic shapes ship in
    `Core/Emitters~` and `Core/Editor/EmitterInstaller.cs` copies them into the project (automatically when the
    project has no `emitters` folder yet, or from the menu *Oxipital > Installer les émetteurs de base*).
  - `particleHyperSize` (1.6.1, 0 to 1): like `dancerHyperSize` for the particles. `particleSize` lost its
    `[InBuffer(23)]`: slot 23 is now the hidden `particleSizeOut` = particleSize x (1 + 100 x particleHyperSize), set
    at the start of `OrbGroup.Update`; the graphs scale linearly with it (Sample Buffer 23, then Multiply), so values
    above 1 just make bigger particles. Same slot, so the graphs and the Notion buffer table keep their index.
  - **Render types** (`OrbGroup.renderType`, `[InBuffer(24)]`): `UnlitOpaque` and `UnlitAdditive` each have a quad
    output in `OrbVFX.vfx` (block `OxipitalOutputQuadUnlit`, its "Render Type Index" keeps only its type).
    `LitCube`, `LitSphere` and `LitMesh` share ONE HDRP Lit Mesh output (block `OxipitalOutputMesh`): it keeps
    render types >= 2 and sets `meshIndex = renderType - 2` (mesh0 built-in cube, mesh1 `VFX/Meshes/OrbIcosphere`
    of 80 triangles, mesh2 the exposed "Particle Mesh", set from `OrbGroup.particleMesh` in the Inspector, one per group).
    Each grain gets a fixed random rotation. The output casts shadows and uses compute culling, so the particles
    of a group in another render type are culled before being drawn.
    - Cost is driven by vertices per grain, drawn once per pass (depth, color, each shadow cascade). Measured on a
      laptop RTX 3060, 348 k grains at 1080p: cube (24 vertices) 44 ms GPU, icosphere (42) 122 ms, smooth
      octahedron (6) about 21 ms; each sun shadow cascade adds about 9 ms, the shadow map resolution nothing.
      `VFX/Meshes/OrbOctahedron` (6 vertices, smooth normals, same apparent size as the cube) is the cheap grain
      to put in Particle Mesh; since 1.6.1 it is the default `particleMesh` of the Orb Group and Orb Group Lit prefabs
      (the icosphere stays the VFX default when the field is empty). Measured in Soledad act 3 (dome 2048), 264 k
      LitMesh grains: icosphere about 200 ms, octahedron 33 ms with 5 shadowed point lights, 17 ms without them: a
      point light with shadows renders 6 faces, each drawing every grain again. A new render type goes at the END of
    the enum (Unity serializes the index; the Chataigne module lists the values).
  - **Infinite life mode** (`OrbGroup.infiniteLife`, `Core/Runtime/VFX/Shaders/InfiniteLifeInit.hlsl` /
    `InfiniteLifeUpdate.hlsl`): particles never die and the dancer intensity sets the particle count
    (`intensity × maxInfiniteParticles`) instead of the spawn rate. The rate spawner is stopped
    (`Emitter Intensity` = 0) and `updateInfiniteLife()` sends a `SpawnInfinite` event with the missing count,
    capped to the free VFX capacity (alive count readback lags ~1 s, so spawns from the last 2 s are counted as
    used). Each spawned particle gets a unique custom attribute `infiniteRank` (`infiniteRankBase + spawnIndex + 1`,
    0 = normal particle). In Update, ranks above `infiniteCount` are killed (lowering intensity removes particles)
    and the others have their age held at `INFINITE_LIFE_HOLD` (10%) of their lifetime so over-life curves still
    apply. Switching the mode off or `kill()` clears ranks so particles age and fade out normally. Buffer fields:
    `infiniteLife` = 1, `infiniteCount` = 2, `infiniteRankBase` = 35 (next free `InBuffer` index since 1.7.0: 45).
    `gridDimension` (9–11) / `gridDensity` (12) are reserved in the buffer for the upcoming grid emission and not
    read by the VFX yet.
    Known limits: KillDarkParticles can kill some ranks at spawn in UnlitAdditive; counts can drift if the effect
    isn't simulated on a frame where the count changes (toggle the mode off/on to resync).
  - Custom HLSL blocks that read "Orb Buffer" in a context where a Sample Buffer node also reads it must declare
    it `StructuredBuffer<Single>` (`#define Single float` in `OxipitalHelpers.hlsl`), otherwise VFX Graph sees
    two different buffer types.
  - **Lit material** (1.7.0): `OrbGroup.metallic` (0) and `smoothness` (0.5) feed the exposed "Metallic" and
    "Smoothness" of `OrbVFX` (linked to the Lit Mesh output's slots, so one value per group), set every frame. Metal
    reflects its surroundings: in a dark scene it only shows the highlights of the lights (raise smoothness).
  - **Bounds box** (1.7.0, `OrbGroup.boundsMode` Off / Wall / Soft / Wrap, `boundsSize`, `boundsStrength`,
    `boundsCenter`): keeps every particle in a box on the world axes centred on the group + `boundsCenter`. Done at
    the start of `UpdateInfiniteLife` (the Custom HLSL block at the end of Update already reading the Orb Buffer;
    written inline because the block finds the attributes it uses in the function body), before the velocity is
    integrated. Wall: clamped on the face, outward speed bounced (strength = bounce). Soft: spring stiffening with
    the distance out (`over x (1 + over) x 20 x strength`) plus braking while out, and a hard limit a quarter of the
    box size beyond each face (a strong outward force, such as a repelling radial, beats the spring alone). Wrap: back in through the opposite face (even density under turbulence). Buffer: `boundsMode`
    36, `boundsSize` 37-39, `boundsStrength` 40, hidden `boundsCenterOut` 41-43 (world centre) and `boundsStep` 44
    (`VFXManager.fixedTimeStep`: OrbVFX updates with a fixed delta time). Gizmo when the group is selected.
    A change to an HLSL file reimports `OrbVFX`, but an effect already playing keeps the old code: restart Play mode.

### Noise pattern and master ballets (1.6.0)

- Pattern blending: the patterns add up in pattern units (Add, Multiply, Replace, each by its smoothed `weight`), then
  the group scales the sum once by `patternSize` (spread by `patternSizeSpread`), then the `AfterSize` patterns (the
  noise) apply in local units. Before 1.6.0 each pattern rescaled the running sum, so two active patterns put the first
  one at patternSize squared. `LineDancePattern.lineAxis` (X by default) and `oscillationAxis`: with lineAxis Z,
  across the circle's XY plane, Line + Circle (both Add) is a helix.
- `NoiseDancePattern` (`Ballet/Patterns`, on every group prefab, weight 0): brownian wandering of each dancer
  (`amplitude` in group local units, `frequency`, `axes`, `seed`), added after the patternSize scaling, on its own
  clock (moves even when the patterns stand still). Keep it after the other patterns.
  Everything that moves a dancer is a pattern; groups only add looks (light, particles, force).
- Master / slave: every `DancerGroup` has `targetMasterBallet` (the name of another group, `None` = free; the
  Inspector shows a dropdown of the groups of the open scenes, plus the names the managers will give in Play mode,
  `Force Group 1`...) and `masterBalletTransition` (s). After its own patterns, dancer i goes onto the master's
  dancer i (modulo the master's count). Once synced it sits exactly there, no smoothing: the transition only shapes
  the changes (the weight ramps on sync and back on release, and a change of master fades out the gap between the
  old place and the new master). The slave first calls `UpdateDancers()` on its master (each group places its
  dancers once per frame), so it reads the master's positions of this very frame, whatever the Update order. A synced slave also
  takes its master's count, ramped from its own over the transition (through `BaseManager.activeCount`, fractional, so
  dancers appear and leave one by one with the usual fade, `dancerWeightSizeFactor` included); its own `count` field
  is untouched and comes back on release. Options `masterSyncIntensity` and
  `masterSyncSize` (on by default): its dancers also take the intensity / size of their master dancer, so the
  master's fades show on the slave; each option has its own ramp over the transition, so switching it never jumps. Guarded against two groups following each
  other. The rotation (look-at) is computed after, from the new place. Groups
  register in `DancerGroups.All` (`IDancerGroup`: name cached at OnEnable, dancer count and positions).

### Lights (`Core/Runtime/Scripts/Lights/`, 1.5.0)

A ballet of HDRP lights, on the same item/manager pattern: `LightManager : BaseManager<LightGroup>` (`Ballet/Light
Groups`, count 0 in the prefab) spawns `Light Group.prefab`s; `LightGroup : DancerGroup<LightDancer>` places
`Light Dancer.prefab`s with the usual dance patterns (Line, Circle, Manual, copied from the Force Group; the circle
lies in the group's local XY plane, so rotate the group 90 degrees on X for a flat ring). A `LightDancer` holds a child
`Light` (point or spot, Lumen unit, `HDAdditionalLightData`) and a child `Mesh` (built-in sphere, `Materials/Light
Sphere.mat`, HDRP/Unlit; its colour is set per light through a MaterialPropertyBlock on `_UnlitColor`; no shadow
casting, so the light is not trapped in its own mesh; any `mesh` can replace the sphere).
- Light: `shape`, `color`, `intensity` (lumen), `range`, `spotAngle` / `spotSoftness`, `lightRadius` (HDRP shape
  radius), `volumetric` / `volumetricIntensity` (halos and beams in the volumetric fog), `shadows`.
- Orientation and movement belong to the group and its patterns, like every dancer group: a light points where its
  dancer points (`dancerLookAt`, `dancerLookAtMode`, `dancerRotation`, blended so a change never jumps), and a
  `NoiseDancePattern` makes it wander.
- Effects, all multiplied, so they stack with `dancerIntensity` and the count fade: `wave` (Constant, Sine, Triangle,
  Saw, Square, Noise; `waveDepth`, `waveSpeed` in Hz or per beat, `wavePhaseDelay` between dancers), beat (`bpm`,
  0 = off; each dancer flashes for `flashLength` of every `beatsPerFlash` beats, `flashSoftness` fades,
  `beatDelay` between dancers = a chase), and `colorNoise` (hue and brightness, `noiseSpeed`, `seed`). A flicker is
  the Noise wave. `ResetBeat()` restarts the clock (on a downbeat).
- Time accumulates from `Time.deltaTime` and the noise is seeded (Recorder renders repeat). Light properties are
  written only when they change; a light below 0.01 lm is disabled. Like every dancer group, lights exist in Play mode
  only. The Chataigne module does not list Light Groups yet (OSCQuery exposes them).

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
  one `CinemachineCamera` per mode) and switches between `CameraMovementType.Orbital / Spaceship / Sensor / Spline`
  (`OrbitalMovement`, `SpaceshipMovement`, `SensorMovement`, `SplineMovement`, each a `CameraMovement` subclass;
  a new type goes at the end of the enum, and `splineCamera` is optional: null-checked). On switch it
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
- Timing (1.7.0): `CameraController` calls `UpdateMovement` in `FixedUpdate`, which only the Spaceship uses
  (rigidbody forces, the rigidbody interpolates). Every other rig moves in its own `Update` (Orbital since 1.7.0,
  Sensor, Spline), and the distance and lens are applied in `Update`. A rig moved in `FixedUpdate` without a
  rigidbody makes the brain's Smart Update move its camera at 50 Hz: frames rendered at another rate get two steps
  or none, an irregular stutter (the Orbital camera did this before 1.7.0).
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
- `SplineMovement` (`Spline Camera target` + `CM Spline Camera`) rides a spline: the first spline of `spline`, a
  `SplineContainer` usually handed at run time by the scene that owns it (`SetSpline`, not exposed to Chataigne:
  show scenes come and go, the rig stays). Distances are metres along the spline, the container's position and
  rotation apply but not its scale (as in Cinemachine's spline cameras). The rig computes the pose in `Update`
  (not `FixedUpdate`: no physics, no 50 Hz judder), only while active; `CM Spline Camera` follows it exactly
  (CinemachineFollow LockToTarget, no position damping) and turns with it (`CinemachineRotateWithFollowTarget`,
  damping = `rotationDamping`), with the shared Perlin noise and the brain's blends.
  - By progress (`useProgress`, 1.4.0): `progress` (0 = start of the spline, 1 = end) places the camera, speed and
    loop are ignored; meant for a Timeline, where a position is easier to place than a speed. Riding by speed,
    `progress` shows where the ride is.
  - Travel: `speed` (m/s, negative = backwards) eased by `speedSmoothing`; `offset` (x right, y up) and `altitude`
    eased by `offsetSmoothing`. Smoothing the inputs, not the output, keeps the camera exactly on the path at a
    steady speed (no lag). `distance` is written every frame and can be set to jump; `loop` wraps (a cut unless
    the spline is closed). Taking over from another camera, `joinNearestPoint` starts at the point nearest to it
    and the speed eases in from 0, unless `ResetRide()` was called while another camera was shown: then
    it starts at `startDistance` at full speed (a show scene can reset the ride, then switch to Spline).
  - Ground: a scene can hand an `ICameraGround` (`SetGround`): the height becomes ground + `altitude` + the
    spline's height above its container, the ground anticipated over `groundLookAhead` (rises before a hill, as
    the Soledad Act 2 flight did) and eased by `groundSmoothing`.
  - Up (`splineUp`, 1.3.0): 0 = the world's up (level horizon); 1 = the spline's own up from its knot rotations, so
    the camera turns with the spline in every direction (up a wall, round a loop). The heading, `followPitch`,
    `bank` (turn rate measured around that up) and `offset` (x right, y up) all use the blended up.
  - Chaining splines: `SetSpline(other, false)` carries on from the point of `other` nearest to the camera, keeping
    speed and look (`SetSpline(other, true)` restarts at `startDistance`). Soledad's Act 2 hands over from its
    flight path to Bogota's path this way.
  - Rotation (`rotationMode`): `Spline` = aimed `aimAhead` metres ahead on the path (softer than the tangent),
    level unless `followPitch` / `followRoll` (spline up vectors and a `CinemachineSplineRoll` on the spline's
    object), `bank` leans into turns; `SplineAndLook` = that plus a gamepad look (`yaw1D` / `pitch1D` / `roll1D`,
    same signs as the Spaceship) that points the view like a head: full stick = `maxLookYaw` / `maxLookPitch` /
    `maxLookRoll` from the path's view, released = back on the path, through a critically damped spring
    (`Vector3.SmoothDamp`, `lookSmoothing` seconds, capped at `maxLookSpeed` deg/s): an exponential ease started a
    swing at full speed, a jerk when turning round (1.2.2); `maxLookYaw` stops at 170 so full left and full right
    stay apart. It used to
    integrate rates: a stick that never rests at exactly 0 then turned the view forever; `stickDeadzone` (0.1)
    applies in both look modes. `Free` = the gamepad alone turns the camera at `yawSpeed` / `pitchSpeed` /
    `rollSpeed`, from the view it took over.
  - Drift: brownian (3-octave simplex) wandering of position and view, seeded by `driftSeed` and timed by
    `Time.deltaTime`, so a Recorder render repeats (Cinemachine's Perlin noise reseeds at random each run).
- `DomeCameraRigFollow` re-targets a dome rig transform to follow a camera target, compensating for the
  `com.pfc.dome-tools` rig being offset from the projection center (the dome-tools package itself is only
  needed by projects with dome scenes, it is not a dependency of the package). It follows right after the brain
  has moved the camera (`CinemachineCore.CameraUpdatedEvent`, plus a late `LateUpdate`): following in `Update`
  showed the previous frame's pose, so the dome jittered with every change of frame time (fixed in 1.7.0).
- `CameraSpoutManager` (`[ExecuteInEditMode]`) owns the output `RenderTexture` that the main camera renders
  into, and streams it out via `Klak.Spout.SpoutSender` on Windows or `Klak.Syphon.SyphonServer` on macOS
  (platform is chosen at runtime via `Application.platform`); output resolution is persisted in `PlayerPrefs`.
  Since 1.7.2 it is part of `Cameras.prefab`: child `Unity Spout Oxipital` (same name as in the Engine scenes, a
  Chataigne address), sender "Oxipital Spout" / server "Oxipital Syphon", 1920 x 1080 by default. While it is on,
  the camera renders into the texture, so its child `Spout Preview` (1.7.3, under `Unity Spout Oxipital` since
  1.7.5 so the two switch off together; a Screen Space Overlay canvas, sorting order -100, under any other UI) shows it full screen through a RawImage driven by
  `SpoutFeedback` (`[ExecuteAlways]`, reads `CameraSpoutManager.OutputTexture`, so Syphon too; hidden when there is
  no texture, since an empty RawImage draws white, and in a full dome). The preview is stretched to the window: a
  window of another aspect than the output deforms it, the output itself is not. Deactivating
  `Unity Spout Oxipital` gives the camera back to the screen and frees the texture (`OnDisable`). Off in a full dome (`isFullDome`). The texture is `DontSave`
  (it used to be saved into the scene through the camera's Target Texture) and released when its size changes.
  The Spout input (`SpoutReceiver` "Oxipital_InputTexture" into Core's `InputTexture`) stays in the scenes: it
  feeds Core, which the Camera package does not depend on.

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
