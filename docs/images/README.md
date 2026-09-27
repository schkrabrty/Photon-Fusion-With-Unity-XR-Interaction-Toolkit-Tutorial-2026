# README image sources

## Scene captures

`game-overview.png`, `cube-lessons.png`, and `waiting-room.png` were rendered with Unity **6000.3.23f1** from copies of this repository's **Game Scene** and **Lobby Scene**.

These are **offline scene previews**, not multiplayer gameplay recordings. The capture uses a separate camera for legible framing. It faces the Game scene's existing world-space prompt toward that camera and enables the Lobby's existing player-count and countdown labels, as the setup instructions require. It does not invent connected players, run a countdown, or stage RPC effects. Materials, geometry, and saved text come from the project. No Photon App IDs appear in the images.

The original working scenes were not saved or changed by the capture. Photon App IDs were cleared in the disposable capture copy.

To recapture, use a **temporary project copy**, not your active working Editor:

1. Copy `Assets`, `Packages`, and `ProjectSettings` into a temporary project directory. Unity can rebuild its own Library there.
2. Copy [ReadmeCapture.cs](../tools/ReadmeCapture.cs) into that temporary project's `Assets/Editor` folder.
3. Start the matching Unity Editor in batch mode with `-projectPath <temporary-project>` and `-executeMethod ReadmeCapture.Capture`. Do not use `-nographics`: the capture needs a graphics device. Do not add `-quit`: the helper waits for rendering, then exits the temporary Editor itself.
4. Set the environment variable `FUSION_DOCS_OUTPUT` to the output directory you want. Without it, the helper writes to `docs/images` under its working directory.
5. Inspect all three images before replacing the README assets. Initial import and shader compilation can take several minutes.

## Script images

`code-*.png` are browser-rendered screenshots of syntax-highlighted **source excerpts**, not screenshots of an IDE. Filenames and line numbers identify the source. The full scripts remain linked from the README for copying and accessibility.

[code-excerpts.json](code-excerpts.json) records the source path, exact text, and line range for each image. `code-cube-lesson.png` is explicitly an **optional exercise**, sourced from [CubeColorLesson.cs](../examples/CubeColorLesson.cs), not an already-attached scene component.

Regenerate from the repository root:

```bash
python3 -m venv .venv-docs
.venv-docs/bin/pip install pygments playwright
.venv-docs/bin/python -m playwright install chromium
.venv-docs/bin/python docs/tools/render_code_images.py
```

On Windows, use the corresponding executables under `.venv-docs\Scripts`. Optionally set `CHROMIUM_PATH` to an existing Chromium executable instead of using Playwright's downloaded browser. Keep the virtual environment out of version control.
