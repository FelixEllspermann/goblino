# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project state

Fresh Unity scaffold — no gameplay code exists yet. `Assets/` contains only the default URP 2D template files, and `RTSCL.slnx` is empty (it stays empty until the first `.cs` file forces Unity to regenerate `.csproj`s). When you start writing scripts, expect Unity to populate `Assembly-CSharp.csproj` automatically; do not hand-author project files.

## Engine & rendering

- **Unity 6000.4.3f1** (Unity 6). The editor version is pinned in `ProjectSettings/ProjectVersion.txt` — match it locally or Unity will silently upgrade the project on open.
- **Universal Render Pipeline 2D** (`com.unity.render-pipelines.universal` 17.4.0). Renderer/quality assets live at `Assets/Settings/Renderer2D.asset` and `Assets/Settings/UniversalRP.asset`; the global URP settings are `Assets/UniversalRenderPipelineGlobalSettings.asset`. Lit 2D scenes should be created from `Assets/Settings/Lit2DSceneTemplate.scenetemplate`, not by duplicating SampleScene.
- **New Input System** (`com.unity.inputsystem` 1.19.0) is active — the old `UnityEngine.Input` API is disabled. Bindings live in `Assets/InputSystem_Actions.inputactions`; extend that asset rather than adding a parallel one.
- 2D toolset is installed (animation, aseprite, psdimporter, spriteshape, tilemap + extras). Aseprite/PSB files import as rigged sprites by default.

## Steamworks integration

- **App ID: `4775350`** (defined as `SteamManager.AppId` in `Assets/Scripts/SteamManager.cs` and in `steam_appid.txt` at the project root).
- **Binding**: Steamworks.NET `2025.163.0` via UPM Git URL in `Packages/manifest.json`. This wraps the **C++ SDK 1.63**, one version behind the raw SDK in `sdk/` (v1.64). Do not mix native libs across versions — Steamworks.NET ships its own `steam_api64.dll` inside the package, and that is what gets used at runtime.
- **`sdk/`** (raw Valve C++ SDK v1.64) sits outside `Assets/` on purpose so Unity doesn't try to import the C headers. Treat it as a reference dump: `sdk/Readme.txt` is the authoritative changelog if you need to check what an `ISteam*` method does, and `sdk/tools/ContentBuilder/` is for uploading depots when shipping.
- **`steam_appid.txt`** (7 ASCII bytes, no BOM, no trailing newline) lets `SteamAPI.Init()` succeed when launching from the Unity Editor or directly from the build folder — Steam normally injects the AppID via env var when launching through the client. **Never ship this file in a public/Steam build** — Steam treats its presence as "bypass ownership check," which is a dev-only shim. Strip it from the build output before uploading to Steam.
- **`SteamManager`** auto-bootstraps via `RuntimeInitializeOnLoadMethod(BeforeSceneLoad)` — no need to add it to a scene. It runs `RestartAppIfNecessary` (re-launches through Steam if the .exe was launched directly), `Packsize`/`DllCheck`, `SteamAPI.InitEx`, sets a warning hook, and pumps `SteamAPI.RunCallbacks()` each frame.
- **Adding features** (achievements, cloud, lobbies, networking): write thin wrapper classes that depend on `SteamManager.Initialized`. Do not call Steam APIs before `BeforeSceneLoad` completes, and do not call `SteamAPI.Shutdown()` manually — `SteamManager.OnDestroy` owns shutdown.

## Workflow notes

- This is not a git repository. If versioning matters, `git init` before any significant work — Unity's `Library/`, `Temp/`, `Logs/`, and `UserSettings/` must be `.gitignore`d (standard Unity gitignore applies).
- Unity serializes scenes/prefabs as YAML. Edit them through the Editor; hand-editing `.unity`/`.prefab` files corrupts GUIDs.
- `.meta` files are load-bearing — never delete a `.meta` without its asset and vice versa.
- Builds, play-mode tests, and asset operations require launching the Unity Editor; there is no headless toolchain configured. Batch mode invocation pattern when needed:
  `"C:\Program Files\Unity\Hub\Editor\6000.4.3f1\Editor\Unity.exe" -batchmode -projectPath "C:\Users\fe199\RTSCL" -quit -executeMethod <Class>.<Method>`
