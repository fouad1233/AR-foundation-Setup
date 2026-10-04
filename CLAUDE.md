# AR Foundation Setup

Unity **6000.6.3f1** (Unity 6.6), URP 17.6, AR Foundation / ARCore / ARKit **6.6.2**, Input System 1.20.
Single scene: `Assets/Scenes/SampleScene.unity`. Tested in the Editor with **XR Simulation** and on Android (ARCore).

## Layout

- `Assets/Scenes/SampleScene.unity`: **XR Origin** (ARRaycastManager, ARPlaneManager, ARPlaceCube, ARTrackedImageManager), **AR Session**, Directional Light, Global Volume.
- `Assets/Scripts/ARPlaceCube.cs`: tap/click to place `placementPrefab` on a raycast hit (new Input System, EnhancedTouch).
- `Assets/Prefabs/`: `Prefab_Parent` (placed / tracked-image content), `AR Default Plane`.
- `Assets/XR/`: XR Plug-in Management. Standalone → XR Simulation loader, Android → ARCore loader.
- `Assets/Settings/`: URP assets. `Mobile_Renderer` must keep the **ARBackgroundRendererFeature** or the camera feed is black on device.

Only on `feature/lipstick-ar`:

- `Assets/ImageTrackingResources/`: `ReferenceImageLibrary` (kaust_cup, 5 × 7.4 cm).
- `Assets/LipstickAR/`: the "Lipstick AR" app.
  - `Scripts/LipstickScanner.cs` (on the `LipstickApp` object): blits the AR background material into a 72×128 RT, which gives a screen-aligned camera image. It finds the largest lipstick-coloured blob, picks the nearest reference colour (Dior / YSL) and estimates depth from the blob size. It keeps the character standing on the lipstick. On-screen buttons are the fallback.
  - `Scripts/LipstickDancer.cs`: PlayableGraph with the dance clip plus a frozen "calm pose" (mixer). Two-bone IK on the right arm brings the lipstick to the mouth, and overlay lip meshes on the head bone are recoloured.
  - `Prefabs/LipstickGirl.prefab`: Mixamo **Michelle** (`Models/Michelle.glb`, from three.js examples, imported with glTFast as **Mecanim**). Lipstick model: `Models/lipstick.fbx`, **CC-BY by rattenheimer, credit required**.
  - `Editor/BuildAndroid.cs`: menu **LipstickAR > Build Android APK** → `Builds/RujAR.apk`.

## Gotchas

- **XR Origin Camera Y Offset must be 0.** With the default 1.1176 m, AR content does not line up with the XR Simulation camera, so tracked images and placed objects appear in the wrong place.
- **XR Simulation environments are not in git** (`Assets/UnityXRContent/`, `ContentPackages/` are ignored). The manifest points at `file:../ContentPackages/com.unity.xr-content.xr-sim-environments-2.1.1.tgz`, so a fresh clone needs that tgz put back before packages resolve. The selected environment is Backyard (`XRSimulationPreferences`).
- **Simulated tracked images** need a `SimulatedTrackedImage` in the *environment prefab* whose texture is the same asset (GUID) as in the reference library. The quad is one-sided and faces `transform.up`. Detection needs it inside the frustum and close enough (≈10 cm image detectable up to ~2.5 m).
- **ARCore reference images are scored at build time** (`arcoreimg`). Scores below 75 only warn. An image with too few keypoints **fails the Android build**. Glossy 3D objects such as lipsticks cannot be image-tracked, and ARCore has no 3D object tracking.
- **Android player:** IL2CPP + ARM64, OpenGLES3 only, minSdk 24, portrait. Active Input Handling is **Input System only**. "Both" opens a modal dialog during Editor builds that blocks the build until clicked.
- After editing scripts, let the Editor **recompile before building**. Otherwise the build fails with "script class layout is incompatible between the editor and the player".
- URP: create materials as assets with `Universal Render Pipeline/Lit` (`_BaseColor`). `Shader.Find` at runtime may return a stripped shader.
- glTFast imports animation as **Legacy** by default. Set `importSettings.animationMethod = Mecanim` on the importer. Its clips may not be flagged as looping, so loop manually.

## Building

- Editor: **LipstickAR > Build Android APK** (feature branch), or File > Build Profiles (Android).
- Batch mode, with the Editor closed because the project is locked while it is open:
  `Unity -batchmode -quit -projectPath . -buildTarget Android -executeMethod LipstickAR.Editor.BuildAndroid.Build -logFile build.log`.
  Exit code is non-zero on failure. Check the log for `LipstickAR build:`.
- `Builds/` and `*.apk` are git-ignored.

## Editor automation

`com.unity.pipeline` (experimental) exposes the running Editor to the `unity` CLI, for example `unity command eval_file --file x.cs`, `editor_play`, `editor_stop` and `capture_game_view`.
- The CLI's main-thread call times out after **5 s**. Long operations such as `BuildPlayer` keep running after the CLI reports a timeout, so watch the Editor log instead.
- Capture save paths must be inside the project.
