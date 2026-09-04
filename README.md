# EPGHybrid-VR

**EPGHybrid-VR** is a Unity-based panorama–geometry hybrid virtual reality framework with 3D eye tracking for large-scale wayfinding experiments. The framework integrates panorama-based scene presentation, node-based navigation, multimodal behavioral recording, world-space gaze representation, gaze-to-object mapping, behavioral visualization, and structured AOI analysis within a common Unity environment.

> **Manuscript version:** The implementation described in the submitted manuscript corresponds to release `v0.1.0`.

---

## 1. Framework Overview

The Unity project supports a common scene/data framework that is operated in two practical modes—Experiment Mode and Analysis Mode—while the manuscript describes the underlying workflow in three methodological stages:

1. **Experimental scene construction and configuration**  
   Simplified scene geometry, panoramic imagery, node relationships, and task configuration are integrated into a panorama–geometry hybrid VR environment. Processed node pose information used by the runtime framework is stored in `Assets/StreamingAssets/ReadData/NodeInfo.csv`.

2. **Wayfinding experiment and data recording**  
   Participants navigate between predefined panorama nodes using VR controller interaction. The framework synchronously records task information, visited nodes, HMD/camera state, and eye-tracking data. During recording, the device-local gaze representation is retained and a corresponding world-space gaze ray is generated in the Unity coordinate system.

3. **Data interpretation, visualization, and analysis**  
   Recorded world-space gaze rays are read by the analysis tools and raycast against predefined semantic scene geometry to obtain gaze-hit positions and object identities. The framework also provides session replay, trajectory reconstruction, 3D gaze/hit-point visualization, surface-based gaze heatmaps, and structured AOI outputs.

---

## 2. Unity Project Structure

The repository preserves the original Unity runtime-oriented organization rather than reorganizing scripts according to manuscript subsection numbers.

```text
EPGHybrid-VR/
├── Assets/
│   ├── 01_Main/
│   │   └── Scripts/
│   │       ├── Analysis/
│   │       ├── DataReader/
│   │       ├── Effect/
│   │       ├── Global/
│   │       ├── Interactor/
│   │       ├── Recordings/
│   │       ├── Scenes/
│   │       └── Visualize/
│   │
│   └── StreamingAssets/
│       ├── Config/
│       │   └── PathConfig.json
│       ├── AOI/
│       ├── ImageFolder/
│       ├── ReadData/
│       │   ├── NodeInfo.csv
│       │   ├── NodeVisible.csv
│       │   └── TaskList.csv
│       ├── Reorder/
│       ├── Reorder_GazeHit/
│       └── SaveData/
│
├── Packages/
├── ProjectSettings/
├── README.md
└── LICENSE
```

`Assets`, `Packages`, and `ProjectSettings` should be kept together so that the repository can be opened directly as a Unity project.

---

## 3. Manuscript-to-Implementation Mapping

| Manuscript component | Main implementation area | Main responsibility |
|---|---|---|
| **3.2 Experimental Scene Construction** | `DataReader`, `Global`, `Scenes` | Read node/scene information, initialize scene state, load panoramic content, maintain node relationships, and control the panorama–geometry hybrid scene at runtime |
| **3.3 Experimental Design and Procedure** | `Scenes`, `Interactor`, `Global`, `Effect` | Initialize tasks, manage scene transitions, support controller-based node navigation, target interaction, task hints, and experiment flow |
| **3.3.4 Data Format / Recording** | `Recordings` | Record task information, node/path events, HMD/camera state, and eye-tracking samples |
| **3.4.1 3D Gaze Reconstruction** | `Recordings` | Preserve local gaze data and generate/save the corresponding world-space gaze origin and direction in the Unity coordinate system |
| **3.4.2 Raycasting and Semantic Mapping** | `Analysis` | Read valid world-space gaze rays, perform raycasting against predefined AOI geometry, and resolve object/category information |
| **3.4.3 Behavioral Process Visualization** | `Visualize` | Session replay, path reconstruction, 3D gaze/hit-point visualization, heatmaps, and additional view-export utilities |
| **3.4.4 Data Export and Analysis** | `Analysis` | Generate frame-level gaze-hit data and object-/node-level AOI summaries for downstream analysis |

> The manuscript refers to **functional modules** rather than individual script filenames. This keeps the paper independent of future refactoring of the Unity project.

---

## 4. Main Script Groups

### 4.1 `DataReader`

Provides the configuration-data layer used by the runtime framework.

Main responsibilities include:

- reading node geometry and panorama parameters;
- reading node visibility/connectivity relationships;
- parsing task definitions and task-to-scene mappings;
- supplying shared node/task information to scene, interaction, and recording modules.

The main runtime configuration files are stored under:

```text
Assets/StreamingAssets/ReadData/
```

including:

- `NodeInfo.csv` — processed node pose and scene information used by the runtime framework;
- `NodeVisible.csv` — node visibility/connectivity relationships;
- `TaskList.csv` — task definitions and task-to-scene mappings.

### 4.2 `Global`

Contains core runtime control for the panorama–geometry environment.

Main responsibilities include:

- maintaining global task state;
- controlling node visibility and available targets;
- loading and updating node-specific panoramic content;
- managing panorama resources;
- coordinating node switching and related runtime updates;
- providing the centralized path-configuration utility.

### 4.3 `Scenes`

Controls experiment stages and scene transitions.

Main responsibilities include:

- loading task information;
- initializing target scenes;
- preloading scene-dependent panorama resources;
- managing task-completion and task-failure transitions.

### 4.4 `Interactor`

Implements participant interaction in VR.

Main responsibilities include:

- SteamVR controller-ray interaction;
- selection of accessible panorama nodes;
- target-object validation and confirmation;
- task-hint and give-up UI;
- runtime interaction-ray control.

### 4.5 `Recordings`

Records synchronized experimental data.

Main outputs include:

- task-level participant and experiment information;
- node visits and path timing;
- HTC Vive Pro Eye / SRanipal eye-tracking data;
- HMD/camera pose;
- camera field-of-view parameters;
- device-local gaze origin/direction;
- Unity world-space gaze origin/direction.

The current implementation converts the recorded gaze origin and direction from the device/camera-local reference into Unity world coordinates during recording and stores both representations for subsequent interpretation.

### 4.6 `Analysis`

Implements programmatic gaze-to-object interpretation and structured AOI outputs.

The current analysis tools can:

- select one or multiple task datasets and node IDs;
- read recorded world-space gaze rays;
- raycast valid gaze samples against predefined AOI geometry;
- resolve semantic category and object identity;
- export frame-level gaze-hit results;
- calculate trial-level and node-level object/AOI summaries.

### 4.7 `Visualize`

Provides tools for inspecting reconstructed behavioral and gaze data.

Current functions include:

- single- and multi-record trajectory reconstruction;
- single-camera session replay;
- single- and multi-record 3D gaze-ray / hit-point visualization;
- surface splat heatmaps;
- surface-density heatmaps;
- decision-point visualization;
- decision-point view export.

### 4.8 `Effect`

Contains supporting visual/UI effects used during experiment runtime, such as scene fading and interaction cues. These scripts support the experimental interface but are not core analytical components.

---

## 5. Runtime Data and Folder Organization

The reference project uses `Assets/StreamingAssets` as the main location for runtime configuration and example data.

```text
StreamingAssets/
├── Config/
│   └── PathConfig.json
├── AOI/
├── ImageFolder/
├── ReadData/
├── Reorder/
├── Reorder_GazeHit/
└── SaveData/
```

### 5.1 `ReadData`

Contains runtime configuration files used by the Unity project.

```text
ReadData/
├── NodeInfo.csv
├── NodeVisible.csv
└── TaskList.csv
```

`NodeInfo.csv` contains the processed node information required by the scene runtime, including node position/orientation and related scene parameters. The framework reads this file directly during scene initialization and visualization.

### 5.2 `ImageFolder`

Contains panoramic resources used by the runtime scene. In the current configuration, panorama AssetBundles are expected under:

```text
ImageFolder/AssetBundles/
```

### 5.3 `Reorder`

Contains organized experiment records used by trajectory and decision-point tools.

### 5.4 `Reorder_GazeHit`

Contains organized gaze/gaze-hit records used by gaze replay, 3D gaze visualization, and related visualization tools.

### 5.5 `SaveData`

Contains generated experimental or visualization outputs, depending on the configured paths.

---

## 6. Centralized Path Configuration

Machine-specific absolute paths have been removed from the main workflow. Paths are centrally configured in:

```text
Assets/StreamingAssets/Config/PathConfig.json
```

The current configuration fields are:

```json
{
  "panoramaAssetBundleRoot": "ImageFolder/AssetBundles",
  "recordingSaveRoot": "SaveData/Recordings",
  "gazeHitDataRoot": "Reorder_GazeHit",
  "reorderDataRoot": "Reorder",
  "decisionPointExportRoot": "SaveData/DecisionPointViews"
}
```

### Path resolution

- **Relative paths** are resolved relative to `Assets/StreamingAssets`.
- **Absolute paths** can also be used when datasets are stored outside the Unity project.

For example:

```json
{
  "recordingSaveRoot": "D:/EPGHybridVR_Data/Recordings"
}
```

can be used when experimental recordings should be stored on an external drive.

The configuration is loaded by:

```text
Assets/01_Main/Scripts/Global/ProjectPathConfig.cs
```

Users normally only need to edit `PathConfig.json`; the corresponding scripts obtain their paths from this centralized configuration.

---

## 7. Main Data Flow

A typical experiment/analysis workflow is:

```text
ReadData configuration
(NodeInfo / NodeVisible / TaskList)
        ↓
Scene and node initialization
        ↓
Panorama loading
        ↓
Node-based VR navigation
        ↓
Task / path / HMD / camera / eye-tracking recording
        ↓
Local gaze + world-space gaze ray
        ↓
Recorded world-space gaze data
        ↓
Raycasting against semantic AOI geometry
        ↓
Frame-level gaze-hit data
        ↓
Object-/node-level AOI summaries
        ↓
Replay / trajectory / 3D gaze / heatmap visualization
```

---

## 8. Requirements

The reference implementation used in the manuscript was developed with:

- **Unity:** 2020.3.48f1c1
- **Language:** C#
- **Operating system:** Windows
- **HMD / eye tracker:** HTC Vive Pro Eye
- **Eye-tracking SDK:** Vive SRanipal SDK
- **VR interaction:** SteamVR / Valve VR

The current implementation uses HTC Vive Pro Eye as the reference device. Other HMD and eye-tracking systems may be integrated by adapting the device-specific interface and coordinate/data definitions.

---

## 9. Getting Started and Operating Modes

EPGHybrid-VR can be used in two main modes:

1. **Experiment Mode** — for running VR wayfinding experiments and recording synchronized behavioral and eye-tracking data.
2. **Analysis Mode** — for replaying, visualizing, interpreting, and exporting previously recorded data.

Before using either mode, configure the project paths in `PathConfig.json`.

### 9.1 Step 0 — Configure project paths

Open:

```text
Assets/StreamingAssets/Config/PathConfig.json
```

The default project-relative configuration is:

```json
{
  "panoramaAssetBundleRoot": "ImageFolder/AssetBundles",
  "recordingSaveRoot": "SaveData/Recordings",
  "gazeHitDataRoot": "Reorder_GazeHit",
  "reorderDataRoot": "Reorder",
  "decisionPointExportRoot": "SaveData/DecisionPointViews"
}
```

Relative paths are resolved against:

```text
Assets/StreamingAssets/
```

Absolute paths may also be used when panoramic resources, experimental recordings, or processed datasets are stored outside the Unity project.

The runtime configuration files should also be available under:

```text
Assets/StreamingAssets/ReadData/
```

including:

```text
NodeInfo.csv
NodeVisible.csv
TaskList.csv
```

`NodeInfo.csv` contains the processed node information used by the Unity runtime, including node position/orientation and related scene parameters.

---

### 9.2 Experiment Mode

Experiment Mode is used to conduct the VR wayfinding experiment and collect synchronized task, path, HMD/camera, and eye-tracking data.

#### Hardware and software preparation

1. Connect the required VR hardware, including the HMD, controllers, and eye-tracking device.
2. Start SteamVR and confirm that the HMD and controllers are recognized.
3. Open the EPGHybrid-VR project in Unity.
4. Open `StartScene`.
5. Analysis and visualization utilities may remain disabled during data collection.

#### Start an experiment

1. Enter **Play Mode** from `StartScene`.
2. Enter the participant information requested by the start interface, such as:
   - Tester ID
   - Age
   - other experiment-specific participant information
3. Specify the corresponding **TaskOrder**.
4. Click **Start**.
5. The framework reads the task configuration and loads the corresponding task scene.
6. Follow the on-screen instructions and complete the wayfinding task using the VR controller.
7. The experiment proceeds according to the configured task and node network until the target is reached, the task is terminated, or the experiment ends.

#### Data recording

During the experiment, EPGHybrid-VR records synchronized data including:

- participant/task information;
- node visits and path timing;
- HMD and virtual-camera state;
- eye-tracking samples;
- device-local gaze origin/direction;
- Unity world-space gaze origin/direction.

Recorded files are written to the directory configured by:

```json
"recordingSaveRoot"
```

The default project-relative location is:

```text
Assets/StreamingAssets/SaveData/Recordings/
```

> Analysis and visualization GameObjects are not required during normal experiment execution and may remain disabled.

---

### 9.3 Analysis Mode

Analysis Mode is used after data collection to inspect recorded behavior, replay sessions, generate spatial visualizations, perform gaze-to-object mapping, and export analytical outputs.

#### Open the scene to be analyzed

1. Open the Unity scene corresponding to the dataset to be analyzed.
2. Ensure that the scene contains the required geometric surfaces, node configuration, and semantic AOI objects.
3. Enable only the analysis or visualization tool required for the current task.
4. Configure the tool-specific parameters in the Inspector.
5. Enter **Play Mode**.
6. Inspect the generated result in the **Game** and/or **Scene** view, depending on the selected tool.

Analysis tools can remain disabled when they are not being used.

#### Main analysis and visualization utilities

The reference scenes include the following optional analysis utilities:

| Utility / GameObject | Purpose |
|---|---|
| **DecisionPointViewExporter** | Exports panoramic/decision-point views for selected locations |
| **PanoramaTestManager** | Loads and inspects panoramic content independently of the experiment workflow |
| **GazeReplayController** | Replays recorded gaze and viewing behavior |
| **MultiVisualizer** | Parent group for multi-record spatial visualization tools |
| └ `PathTrajectoryMultiVisualizer` | Reconstructs and overlays participant/task trajectories |
| └ `GazeHitMultiVisualizer` | Displays reconstructed 3D gaze rays and gaze-hit points |
| └ `GazeDensityHeatmapMulti` | Generates gaze-hit density visualizations |
| └ `GazeHeatmapMulti` | Generates surface-based gaze heatmaps |
| **AOIAnalyzer** | Performs gaze-to-object mapping and structured AOI analysis |

The exact Inspector parameters and usage of each utility are described separately in the corresponding module documentation.

#### Data sources used in Analysis Mode

The main analysis input directories are configured in `PathConfig.json`:

```json
"gazeHitDataRoot": "Reorder_GazeHit",
"reorderDataRoot": "Reorder"
```

The corresponding default folders are:

```text
Assets/StreamingAssets/Reorder/
Assets/StreamingAssets/Reorder_GazeHit/
```

Generated decision-point views are written to the path configured by:

```json
"decisionPointExportRoot"
```

with the default location:

```text
Assets/StreamingAssets/SaveData/DecisionPointViews/
```

---

### 9.4 Recommended workflow

A typical use sequence is:

```text
0. Configure PathConfig.json
        ↓
1. Experiment Mode
   StartScene
        ↓
   Participant information + TaskOrder
        ↓
   Run wayfinding experiment
        ↓
   Save synchronized recordings
        ↓
2. Analysis Mode
   Open target scene
        ↓
   Enable required analysis/visualization utility
        ↓
   Configure tool parameters
        ↓
   Enter Play Mode
        ↓
   Inspect Game / Scene view
        ↓
   Export visualization or structured analysis results
```

This separation allows the same Unity scene framework to support both controlled data collection and post-experiment interpretation without requiring all analysis components to remain active during the experiment.

## 10. AOI Preparation and Semantic Mapping

For object-level gaze interpretation, analyzable scene objects should:

1. have valid collision geometry;
2. be organized according to the intended semantic AOI structure;
3. provide the object/category information required by the analysis module.

During analysis, each valid world-space gaze ray is raycast into the corresponding Unity scene. The first valid collision is used to associate the gaze sample with a spatial hit position and semantic object information.

This allows the recorded gaze stream to be transformed into structured gaze-hit and object-level AOI data without frame-by-frame video annotation.

---

## 11. Demo and Example Data

For public release, the repository is intended to provide a representative project example rather than the complete experimental dataset or all study scenes.

A compact demo may include:

- one representative Unity scene;
- example `NodeInfo.csv`, `NodeVisible.csv`, and `TaskList.csv`;
- representative panoramic resources;
- a small anonymized path dataset;
- a small anonymized eye-tracking dataset;
- example gaze-hit outputs;
- representative trajectory, gaze, and heatmap outputs.

---

## 12. Version Associated with the Manuscript

The implementation corresponding to the submitted manuscript is archived as:

**EPGHybrid-VR v0.1.0 — Submission Version**

Future development may modify folder organization, interfaces, documentation, or device compatibility. The release tag preserves the implementation state associated with the manuscript.

---

## 13. Citation

If you use EPGHybrid-VR in academic work, please cite the accompanying publication.

The manuscript is currently under submission. Full citation information will be added after publication.

---

## 14. License

A software license will be specified in the public release. Users should refer to the `LICENSE` file in the repository for permitted use, modification, and redistribution.

---

## 15. Contact

Questions, bug reports, and suggestions can be submitted through the repository's **Issues** page.
