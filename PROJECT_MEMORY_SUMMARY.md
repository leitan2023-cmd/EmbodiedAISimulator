# Project Memory Summary

This document summarizes what was built, why certain decisions were made, how the current pipeline works, and what remains unfinished.

## 1. Project Goal

The goal of this project is to turn MANSION / MansionWorld generated building JSON into a Unity scene that can be explored and later used as the basis for embodied AI research and training.

Concretely, the work focused on:

- loading multi-floor building JSON into Unity
- reconstructing floors and walls
- loading available Objathor assets when possible
- falling back gracefully when full assets are unavailable
- enabling floor switching and first-person exploration
- keeping the pipeline compatible with MANSION scene generation on macOS

## 2. Local Workspace Layout Used During Development

### Unity project

- `/Users/ray/RobotsTest`

### MANSION repository

- `/Users/ray/Desktop/Projects/1111具身智能/仿真测试/MANSION`

### MansionWorld dataset

- `/Users/ray/Desktop/Projects/1111具身智能/仿真测试/MansionWorld`

### Objathor assets

- `~/.objathor-assets/2023_09_23/assets`

### Unity streaming asset links / copies

- `/Users/ray/RobotsTest/Assets/StreamingAssets/ObjathorAssets`
  - symlink to local Objathor assets
- `/Users/ray/RobotsTest/Assets/StreamingAssets/Buildings/test_building`
  - example building copied from MansionWorld

## 3. Environment Work Already Completed

The following setup work was completed during the project:

- installed local Miniconda
- created `mansion` conda environment
- cloned the `MANSION` repository
- installed `requirements.txt`
- installed custom `ai2thor`
- downloaded Objathor base data / annotations / features
- cloned `MansionWorld`
- installed MANSION patch content
- confirmed that macOS can be used for JSON generation even though the patched AI2-THOR build itself is Linux-only

Important conclusion:

- on macOS, we can use MANSION to generate JSON
- on macOS, we should avoid AI2-THOR rendering stages in this workflow

## 4. MANSION Side Decisions

### Mac JSON-only pipeline

A dedicated local entry point was used:

- `/Users/ray/Desktop/Projects/1111具身智能/仿真测试/MANSION/mansion_quickstart_mac_json.py`

This entry point intentionally uses:

- `generate_image=False`
- `include_small_objects=False`

Reason:

- `generate_image=True` requires AI2-THOR rendering flow
- `include_small_objects=True` also triggers AI2-THOR / Controller usage
- those stages are not reliable on the current macOS setup because the patched local build is Linux-targeted

### LLM generation role

The recommended role of the LLM in this system is:

- building requirement interpretation
- room program planning
- topology planning
- large object category planning

The LLM is not intended to directly control runtime Unity transforms.

## 5. Unity Project Work Completed

### Core loader and runtime scripts

The following runtime files were created or substantially implemented:

- `Assets/Scripts/Builder/MansionSceneBuilder.cs`
- `Assets/Scripts/Data/FloorData.cs`
- `Assets/Scripts/Data/ObjathorMeshData.cs`
- `Assets/Scripts/Util/PolygonTriangulator.cs`
- `Assets/Scripts/FloorManager.cs`
- `Assets/Scripts/FloorHotkeys.cs`
- `Assets/Scripts/SimpleFPSCamera.cs`

### Supporting docs

- `Assets/Scripts/MANSION_UNITY_SETUP.md`
- `README.md`

## 6. Main Technical Problems Solved

### 6.1 JSON schema mismatch

At first, the Unity-side data model assumed a more ProcTHOR-like or simplified JSON schema.

Actual MANSION / MansionWorld JSON differed in multiple places:

- room polygons were objects with `{x,y,z}` instead of `List<float>`
- wall polygons were objects with `{x,y,z}`
- door and window hole polygons were objects with `{x,y,z}`
- some fields were objects or arrays in unexpected shapes
- many optional fields varied across files

Solution:

- `FloorData.cs` was progressively adapted to the real JSON
- only fields required by the current loader were kept strongly typed
- many unstable or not-yet-used fields were relaxed to `JToken`

This was necessary to stop the endless chain of deserialization failures and make the loader robust enough to read sample buildings.

### 6.2 Polygon triangulation

Simple floor generation needed reliable triangulation for room polygons.

Solution:

- adopted `LibTessDotNet`
- implemented `PolygonTriangulator.cs`

Important note:

- a naming collision happened because the project also defined its own `Vec3`
- this was fixed by explicitly referencing `LibTessDotNet.Vec3`

### 6.3 Scene loading and floor reconstruction

`MansionSceneBuilder.cs` now does the following:

- scans `StreamingAssets/Buildings/<building_name>` for `floor_*.json`
- creates one GameObject root per floor
- generates floor meshes from room polygons
- generates basic wall meshes from wall polygons
- loads or approximates furniture
- optionally builds one NavMesh per floor
- wires generated floors into `FloorManager`

### 6.4 Floor switching and navigation

Implemented:

- `FloorManager.cs`
- `FloorHotkeys.cs`
- `SimpleFPSCamera.cs`

Current runtime controls:

- `1-9`: switch floors
- `WASD`: move
- mouse: look
- `Q/E`: move vertically
- `Esc`: release cursor

### 6.5 Asset loading problem

The biggest practical issue was that the original loader assumed:

- `ObjathorAssets/<assetId>/<assetId>.glb`

But real local assets were often not stored as ready-to-load `.glb`.

Observed local asset formats included:

- `.pkl.gz`
- textures like `albedo.jpg`, `normal.jpg`, `emission.jpg`
- some `.obj` assets from patch content

Also, many building JSON objects referenced symbolic ids like:

- `RoboTHOR_office_chair_volmar`
- `Desk_320_2_1`
- `Laptop_27`

Those names often did not directly correspond to actual local asset folders.

### 6.6 Objathor mesh conversion fallback

To make more objects visible in Unity, an intermediate conversion approach was introduced.

Created script:

- `/Users/ray/Desktop/Projects/1111具身智能/仿真测试/export_objathor_meshes.py`

Purpose:

- scan building JSON for used `assetId`s
- convert available Objathor `pkl.gz` assets into lightweight:
  - `<assetId>.mesh.json.gz`

Unity then loads these converted meshes through:

- `ObjathorMeshData.cs`
- extended logic in `MansionSceneBuilder.cs`

### 6.7 Semantic proxy fallback

Even after converted mesh loading was added, many named assets still had no local direct counterpart.

To avoid a scene full of identical small cubes, semantic proxy geometry was added.

Examples of proxy categories:

- table / desk / counter
- chair / stool
- sofa
- laptop
- bin
- toilet
- stair

This makes scenes much more readable and useful for prototyping.

### 6.8 Object grounding

Initially many furniture objects appeared floating above the floor.

Reason:

- the raw `position.y` in scene JSON behaves more like an approximate model-center placement than a floor-contact placement

Solution:

- added grounding correction in `ApplyObjectTransform`
- estimated local model center from mesh bounds
- shifted objects downward accordingly

Result:

- furniture now lands much closer to the floor

## 7. Current Runtime Behavior

When Play is pressed in Unity:

1. the loader scans the selected building folder
2. all floor roots are created
3. room floors and walls are generated
4. objects are loaded in this order:
   - `.glb`
   - converted `.mesh.json.gz`
   - semantic proxy
   - final placeholder
5. only the active floor is shown
6. Console prints per-floor object loading stats

Example stats:

```text
Floor 1: GLB=0, Converted=176, Proxy=40, Placeholder=4, Skipped=4
```

This is useful because it immediately tells us whether asset availability or alias mapping is improving over time.

## 8. Why Some Objects Are Still Not Real Assets

There are still unresolved classes of object references:

- symbolic MANSION / RoboTHOR names with no local direct asset folder
- patch objects that may exist as `.obj` but are not yet fully integrated into the generic pipeline
- doors / doorway markers that are logical scene elements rather than final furniture meshes

Current strategy:

- real local converted assets are used where possible
- unresolved named assets use semantic proxies
- some doorway-like placeholders are intentionally skipped to reduce clutter

## 9. Git / Repository Work Completed

The Unity project at `/Users/ray/RobotsTest` was:

- initialized as a new git repository
- given a Unity-appropriate `.gitignore`
- committed
- pushed to:
  - `https://github.com/leitan2023-cmd/EmbodiedAISimulator`

Important `.gitignore` choice:

- local `ObjathorAssets` symlink is ignored
- heavy local generated / cache directories are ignored
- Unity temp / library / csproj noise is ignored

This keeps the GitHub repository lightweight and portable.

## 10. Current Repository Files Of Interest

### Unity runtime

- `Assets/Scripts/Builder/MansionSceneBuilder.cs`
- `Assets/Scripts/Data/FloorData.cs`
- `Assets/Scripts/Data/ObjathorMeshData.cs`
- `Assets/Scripts/Util/PolygonTriangulator.cs`
- `Assets/Scripts/FloorManager.cs`
- `Assets/Scripts/FloorHotkeys.cs`
- `Assets/Scripts/SimpleFPSCamera.cs`

### Example content

- `Assets/StreamingAssets/Buildings/test_building`

### Docs

- `README.md`
- `Assets/Scripts/MANSION_UNITY_SETUP.md`
- `PROJECT_MEMORY_SUMMARY.md`

## 11. Recommended Architecture Going Forward

Recommended separation of responsibilities:

### LLM layer

Use the LLM for:

- building requirement interpretation
- floor program generation
- topology and adjacency planning
- object category planning

### Rule / validation layer

Use deterministic code for:

- corridor clearance
- room reachability
- navigation feasibility
- object overlap validation
- required room presence
- task validity

### Unity simulation layer

Use Unity for:

- scene reconstruction
- navigation / agent runtime
- interaction probes
- sensor simulation
- task execution

This layered approach is more stable and more suitable for embodied training than letting the LLM directly control low-level runtime placement.

## 12. What Is Still Missing

The project is already usable as a scene viewer / prototype simulator, but several important pieces are still unfinished:

- cutting actual wall openings for doors and windows
- a full asset alias mapping system for symbolic MANSION object ids
- loading patch `.obj` assets in a more general way
- a scene validation module
- a task generation module
- embodied agent controllers
- reward and evaluation logic
- collision / placement refinement for all asset families

## 13. Best Next Steps

If development continues, the most valuable next additions would be:

1. `scene_validator.py`
   - validate walkability, room coverage, overlap, door clearance

2. task generation system
   - produce navigation / search / pick-and-place tasks from loaded scenes

3. asset alias resolver
   - map symbolic object ids to concrete local assets where possible

4. door / window geometry support
   - cut holes and instantiate matching openings

5. agent integration
   - connect the current scene pipeline to embodied training loops

## 14. Practical Summary

At the end of this work, the project reached this state:

- MANSION can generate JSON on macOS in JSON-only mode
- Unity can load a real multi-floor MANSION scene
- floor geometry and wall geometry are reconstructed
- many assets render as converted local meshes
- unresolved assets fall back to meaningful semantic proxies
- floors can be switched interactively
- the scene can be explored in first person
- the project is in GitHub with a usable README

This is now a solid prototype base for the next stage: turning scene generation into a rule-governed embodied AI training pipeline.

