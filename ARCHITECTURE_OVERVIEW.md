# Architecture Overview

This document describes the current system architecture of the Unity + MANSION embodied AI prototype.

The focus is the full loop:

```text
LLM -> Scene Generation -> Unity Scene Reconstruction -> Agent Control -> Observation Recording
```

It is intended as a practical engineering reference for further development, integration, and training-system design.

## 1. System Goal

The current system is designed to support embodied AI experimentation on procedurally generated indoor scenes.

At a high level, it aims to:

- generate multi-floor building layouts from natural-language requirements
- convert those layouts into Unity runtime scenes
- provide an embodied agent API for stepping through those scenes
- capture observations and trajectories for debugging, evaluation, or training

This is not yet a full RL platform, but it already provides the core scene-agent-recording pipeline.

## 2. Top-Level Architecture

```text
User Prompt
  ->
LLM-backed MANSION generation
  ->
floor_*.json building output
  ->
Unity SceneOrchestrator / local building loader
  ->
MansionSceneBuilder reconstructs floors, walls, and objects
  ->
EmbodiedAgentController executes actions and captures observations
  ->
ObservationRecorder writes episode data to disk
```

## 3. Main Modules

### 3.1 Scene generation layer

Primary components:

- `/Users/ray/Desktop/Projects/1111具身智能/仿真测试/MANSION/mansion_quickstart_mac_json.py`
- `/Users/ray/RobotsTest/mansion_server.py`

Responsibilities:

- interpret text requirements
- configure MANSION generation parameters
- run the portable MANSION pipeline
- return generated floor JSONs

Important current design choices:

- `generate_image=False`
- `include_small_objects=False`

These settings intentionally avoid the AI2-THOR rendering-dependent parts of the pipeline on the current macOS setup.

### 3.2 Scene orchestration layer

Primary component:

- `/Users/ray/RobotsTest/Assets/Scripts/Network/SceneOrchestrator.cs`

Responsibilities:

- send prompt requests to the local generation server
- receive building JSON payloads
- write them into `StreamingAssets/Buildings/<building_name>`
- trigger Unity-side scene rebuilds

This layer acts as the runtime bridge between external scene generation and Unity reconstruction.

### 3.3 Unity scene reconstruction layer

Primary components:

- `/Users/ray/RobotsTest/Assets/Scripts/Builder/MansionSceneBuilder.cs`
- `/Users/ray/RobotsTest/Assets/Scripts/Builder/DoorCutter.cs`
- `/Users/ray/RobotsTest/Assets/Scripts/Util/PolygonTriangulator.cs`
- `/Users/ray/RobotsTest/Assets/Scripts/Data/FloorData.cs`
- `/Users/ray/RobotsTest/Assets/Scripts/Data/ObjathorMeshData.cs`

Responsibilities:

- load per-floor JSON
- rebuild room floors
- rebuild wall geometry
- cut wall openings for doors
- instantiate objects
- add runtime colliders
- support rebuild from a different building folder
- maintain floor visibility state

This is the heart of the Unity runtime scene system.

### 3.4 Agent layer

Primary components:

- `/Users/ray/RobotsTest/Assets/Scripts/Agent/EmbodiedAgentController.cs`
- `/Users/ray/RobotsTest/Assets/Scripts/Agent/AgentAction.cs`
- `/Users/ray/RobotsTest/Assets/Scripts/Agent/AgentObservation.cs`

Responsibilities:

- expose a discrete `Step(action)` API
- expose `Observe()` for passive observation capture
- support teleport / reset
- move an embodied character with collision
- capture RGB and depth images
- expose interaction hooks such as pickup / put / open

This layer defines the runtime embodied interface that external planners, scripted policies, or learning code will use.

### 3.5 Recording layer

Primary component:

- `/Users/ray/RobotsTest/Assets/Scripts/Recording/ObservationRecorder.cs`

Responsibilities:

- begin and end episodes
- save RGB and depth image sequences
- write trajectory metadata
- optionally auto-record keyboard-driven debugging sessions

This layer is the first piece of the future data-collection and evaluation pipeline.

### 3.6 Runtime utilities

Primary components:

- `/Users/ray/RobotsTest/Assets/Scripts/FloorManager.cs`
- `/Users/ray/RobotsTest/Assets/Scripts/FloorHotkeys.cs`
- `/Users/ray/RobotsTest/Assets/Scripts/SimpleFPSCamera.cs`
- `/Users/ray/RobotsTest/Assets/Scripts/Editor/AgentSetupHelper.cs`

Responsibilities:

- switch floor visibility
- provide debug hotkeys
- provide first-person debugging movement
- simplify scene setup in the Unity editor

These are support systems rather than the core training pipeline, but they are important for prototyping and verification.

## 4. Data Flow

## 4.1 Prompt to scene JSON

Input:

- natural-language requirement
- floor count

Flow:

1. a prompt is sent to `mansion_server.py`
2. `mansion_server.py` calls the local MANSION portable pipeline
3. MANSION generates `floor_*.json`
4. the server returns:
   - `building_name`
   - `floors`

Output:

- structured scene JSON per floor

## 4.2 JSON to Unity scene

Input:

- `Assets/StreamingAssets/Buildings/<building_name>/floor_*.json`

Flow:

1. `MansionSceneBuilder` scans the building folder
2. it creates one GameObject root per floor
3. room polygons are triangulated into floor meshes
4. wall polygons are built into quads or segmented door-cut walls
5. objects are loaded from local assets or fallback systems
6. floor visibility and NavMesh are finalized

Output:

- runtime Unity scene geometry and object instances

## 4.3 Scene to agent observations

Input:

- Unity runtime scene
- discrete action

Flow:

1. external caller invokes `EmbodiedAgentController.Step(action)`
2. the controller executes movement or interaction
3. the controller captures RGB and optional depth
4. the controller returns an `AgentObservation`

Output:

- position
- rotation
- floor id
- success flag
- held object
- RGB frame
- depth frame

## 4.4 Agent trajectory to episode record

Input:

- `AgentAction`
- `AgentObservation`
- optional reward / done / success labels

Flow:

1. `ObservationRecorder.BeginEpisode(...)`
2. `ObservationRecorder.RecordStep(...)` per action
3. `ObservationRecorder.EndEpisode(...)`

Output:

- `meta.json`
- `trajectory.json`
- saved RGB images
- saved depth images

## 5. Object Loading Strategy

Object reconstruction is currently tiered:

1. direct `.glb` asset
2. converted Objathor mesh payload `.mesh.json.gz`
3. semantic proxy furniture
4. final placeholder cube

This strategy exists because current MANSION / MansionWorld scenes contain a mix of:

- directly available hashed Objathor assets
- symbolic names like `RoboTHOR_*`, `Desk_*`, `Laptop_*`

Not all symbolic names currently map to local, Unity-loadable assets.

The semantic proxy layer preserves scene readability and supports continued experimentation even when exact assets are missing.

## 6. Geometry And Collision Strategy

Current geometry reconstruction includes:

- room floors from polygon triangulation
- walls from quads
- door openings cut from walls using `DoorCutter`

Current collision strategy includes:

- `MeshCollider` for floor geometry
- `BoxCollider` for wall segments
- `MeshCollider` for loaded object meshes when enabled
- primitive colliders retained on proxy objects
- `CharacterController` based movement for the debug camera
- `CharacterController` based movement for the embodied agent

Important note:

collision now exists across much more of the scene than earlier iterations, but the placement system still does not fully enforce semantic navigability or door clearance correctness.

## 7. Runtime Entry Points

### 7.1 Local static scene loading

Use `MansionSceneBuilder` directly with a building folder already placed in:

```text
Assets/StreamingAssets/Buildings/<building_name>
```

### 7.2 Prompt-driven scene generation

Use `SceneOrchestrator` to:

- send prompt to the local generation server
- write returned floor JSONs
- trigger `RebuildFromFolder(...)`

### 7.3 Agent stepping

Use `EmbodiedAgentController` methods:

- `Step(AgentAction action)`
- `Observe()`
- `Teleport(Vector3 position, float yRotation)`

### 7.4 Episode recording

Use `ObservationRecorder` methods:

- `BeginEpisode(...)`
- `RecordStep(...)`
- `EndEpisode(...)`

## 8. Current Constraints And Boundaries

The current architecture intentionally separates responsibilities:

### LLM / generation layer

Good for:

- interpreting task requirements
- proposing building function
- room program and topology generation
- furnishing plan generation

Not ideal for:

- final collision validity
- runtime navigability guarantees
- reward design
- exact embodied training logic

### Rule / validation layer

This layer is still missing in a complete form, but should eventually own:

- door clearance validation
- corridor width checks
- overlap / collision validation
- target reachability checks
- task feasibility checks

### Unity runtime layer

Owns:

- rendering
- collision
- observation capture
- embodied stepping
- interactive debugging

## 9. Current Known Weaknesses

The architecture is already useful, but there are still important limitations:

- symbolic asset alias resolution is incomplete
- furniture placement can still obstruct doorways or circulation
- stair / vertical navigation is not yet a robust multi-floor movement system
- wall openings exist, but full door / frame / window instantiation is incomplete
- no dedicated scene validation stage exists yet
- no full task specification / reward loop exists yet
- episode outputs are currently suited to prototyping, not yet a standardized benchmark format

## 10. Recommended Next Architectural Additions

### 10.1 Scene validator

Add a deterministic validation stage between generation and training:

```text
LLM Scene -> Scene Validator -> Approved Scene -> Unity
```

Suggested checks:

- doorway clearance
- object overlap
- corridor width
- room accessibility
- walkable connectivity

### 10.2 Task specification layer

Add structured task generation on top of the loaded scene:

- navigation targets
- search tasks
- pick-and-place tasks
- multi-floor traversal tasks

### 10.3 Environment API wrapper

Wrap Unity stepping into a more formal environment API:

- `reset()`
- `step(action)`
- `observation`
- `reward`
- `done`
- `info`

This would make downstream training integration much easier.

### 10.4 Asset alias resolution layer

Add a mapping layer:

```text
symbolic MANSION asset id -> local available asset or proxy policy
```

That will reduce the number of semantic proxies over time.

## 11. Recommended Mental Model

The current system should be understood as:

```text
LLM-driven scene planning
  +
Unity-based scene realization
  +
step-based embodied agent interface
  +
episode recording scaffold
```

This is already a valid prototype architecture for:

- embodied scene debugging
- task prototyping
- dataset generation experiments
- environment interface experiments

It becomes a training platform once the missing validator / task / reward layers are added.

## 12. File Map

### Generation / server

- `/Users/ray/RobotsTest/mansion_server.py`
- `/Users/ray/Desktop/Projects/1111具身智能/仿真测试/MANSION/mansion_quickstart_mac_json.py`

### Scene runtime

- `/Users/ray/RobotsTest/Assets/Scripts/Builder/MansionSceneBuilder.cs`
- `/Users/ray/RobotsTest/Assets/Scripts/Builder/DoorCutter.cs`
- `/Users/ray/RobotsTest/Assets/Scripts/Data/FloorData.cs`
- `/Users/ray/RobotsTest/Assets/Scripts/Data/ObjathorMeshData.cs`
- `/Users/ray/RobotsTest/Assets/Scripts/Util/PolygonTriangulator.cs`

### Agent / recording

- `/Users/ray/RobotsTest/Assets/Scripts/Agent/EmbodiedAgentController.cs`
- `/Users/ray/RobotsTest/Assets/Scripts/Agent/AgentAction.cs`
- `/Users/ray/RobotsTest/Assets/Scripts/Agent/AgentObservation.cs`
- `/Users/ray/RobotsTest/Assets/Scripts/Recording/ObservationRecorder.cs`

### Runtime support

- `/Users/ray/RobotsTest/Assets/Scripts/Network/SceneOrchestrator.cs`
- `/Users/ray/RobotsTest/Assets/Scripts/FloorManager.cs`
- `/Users/ray/RobotsTest/Assets/Scripts/FloorHotkeys.cs`
- `/Users/ray/RobotsTest/Assets/Scripts/SimpleFPSCamera.cs`
- `/Users/ray/RobotsTest/Assets/Scripts/Editor/AgentSetupHelper.cs`

## 13. Final Summary

The current project has successfully crossed the line from:

```text
JSON scene viewer
```

to:

```text
LLM-connected embodied simulation prototype
```

That means the essential architectural spine now exists.

The next stage is not another major rewrite. It is the addition of:

- stronger validation
- better placement constraints
- explicit task specification
- reward / evaluation logic

Those additions can now be built on top of a working scene-agent-recording stack.

