# MANSION Unity Minimal Setup

## Files already added

- `Assets/Scripts/Data/FloorData.cs`
  - JSON data model for the current MANSION sample format.
- `Assets/Scripts/Util/PolygonTriangulator.cs`
  - Floor polygon triangulation via `LibTessDotNet`.
- `Assets/Scripts/Builder/MansionSceneBuilder.cs`
  - Minimal scene loader for floors, walls, and furniture GLBs.
- `Assets/Scripts/FloorManager.cs`
  - Runtime floor visibility switching.
- `Assets/Scripts/FloorHotkeys.cs`
  - Number-key shortcuts for floor switching.
- `Assets/Scripts/SimpleFPSCamera.cs`
  - Minimal WASD + mouse look camera.

## Required folder layout

The project currently expects:

- `Assets/StreamingAssets/ObjathorAssets`
  - Symlink or folder pointing to Objaverse-THOR assets.
- `Assets/StreamingAssets/Buildings/test_building`
  - Building folder containing `floor_1.json`, `floor_2.json`, ...

Current sample building:

- `Assets/StreamingAssets/Buildings/test_building/floor_1.json`

## Scene setup in Unity

1. Open your scene.
2. Create an empty GameObject.
3. Rename it to `MansionLoader`.
4. Add the `MansionSceneBuilder` component.
5. Add the `FloorManager` component to the same GameObject.
6. Add the `FloorHotkeys` component to the same GameObject.

Optional but recommended:

7. Put a Camera in the scene.
8. Add `SimpleFPSCamera` to that Camera.

## Inspector settings

Recommended first run:

- `Building Folder`: `test_building`
- `Initial Floor`: `1`
- `Floor Height`: `3.0`

Materials:

- `Wall Mat`
  - Optional. If empty, the script creates a simple URP/Lit light gray material at runtime.
- `Floor Mat`
  - Optional. If empty, the script creates a simple URP/Lit brown material at runtime.

Runtime:

- `Build Nav Mesh`
  - Recommended `true` for now.
- `Floor Manager`
  - If left empty and `FloorManager` is on the same GameObject, `MansionSceneBuilder` will auto-find it.

Floor hotkeys:

- `Floor Manager`
  - If left empty and `FloorHotkeys` is on the same GameObject, it will auto-find `FloorManager`.
- `Min Floor`
  - Usually `1`
- `Max Floor`
  - Set to your building's max floor count, for the sample use `6`

## What should happen on Play

On play, the loader will:

1. Scan `StreamingAssets/Buildings/test_building` for all `floor_*.json`.
2. Create one root GameObject per floor:
   - `Floor_1`
   - `Floor_2`
   - ...
3. Build room floor meshes from `room.floorPolygon`.
4. Build wall quads from `wall.polygon`.
5. Try to load each furniture GLB from:
   - `StreamingAssets/ObjathorAssets/<assetId>/<assetId>.glb`
6. If a GLB is missing, spawn a small cube placeholder instead.
7. Only the floor matching `Initial Floor` starts active.
8. If `Build Nav Mesh` is enabled, each floor root gets a `NavMeshSurface` and bakes after geometry is created.

## Expected first visual result

The first usable result is:

- floor slabs appear for each room
- wall surfaces appear as simple quads
- many furniture assets should load as GLBs
- missing assets appear as small cubes

This is a geometry/debug viewer first, not a finished runtime importer yet.

## Floor switching

`FloorManager` is now wired to receive generated floor roots from `MansionSceneBuilder`.

You can switch floors from your own UI or test code with:

```csharp
floorManager.SwitchTo(2);
floorManager.SwitchTo(3);
```

The active floor is enabled and the previous one is disabled.

If `FloorHotkeys` is attached, you can also press:

- `1` to show floor 1
- `2` to show floor 2
- ...
- `6` to show floor 6

Both main keyboard number keys and numeric keypad keys are supported.

## First-person camera

If you add `SimpleFPSCamera` to the scene camera:

- `WASD` moves
- mouse looks around
- `Esc` unlocks the cursor

Recommended first test:

- place the camera around `(8, 1.6, 8)`
- face slightly downward toward the first floor

## Current limitations

- Wall holes for doors/windows are not cut yet.
- Doors and windows are parsed, but not instantiated as dedicated geometry yet.
- Furniture transform alignment is basic and may need per-asset correction.
- There is no floor switching UI yet, only script-level switching via `FloorManager`.
- Floor switching is keyboard-driven for now; there is no on-screen UI yet.
- NavMesh is baked, but no agent controller is included yet.

## If Play shows nothing

Check these first:

1. `ObjathorAssets` symlink exists under `Assets/StreamingAssets`.
2. `Buildings/test_building` contains `floor_1.json`.
3. `LibTessDotNet` source is present under `Assets/Plugins/LibTessDotNet`.
4. Console has no compile errors.
5. The scene camera is placed near the generated building.

## Next useful step

After this loads successfully, the next practical addition is:

- a simple floor switcher
- door/window hole cutting
- a JSON probe script that prints room/object counts at runtime
- a NavMeshAgent-based click-to-move explorer
