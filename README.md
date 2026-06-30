# ObjViewer

A lightweight, cross-platform **.OBJ 3D model viewer** built in C# with [Silk.NET](https://github.com/dotnet/Silk.NET) and OpenGL. Load any Wavefront `.obj` file, rotate, pan, and zoom around it with simple mouse controls — no external 3D engine, no Unity, just raw OpenGL through .NET.

## Features

- 📦 **Custom OBJ parser** — reads vertices, normals, and faces directly, with fan-triangulation support for quads and n-gons
- 🖱️ **Interactive camera** — orbit, pan, and zoom with the mouse
- 💡 **Phong-style lighting** — a simple, real-time specular/diffuse shader for clear shading on any mesh
- 🗂️ **Open files at runtime** — press `O` to bring up a native file picker (Windows, macOS, and Linux) and load a new model without restarting
- 📐 **Auto-centering & normalization** — models are automatically centered and scaled to fit the view, regardless of their original size
- 🧊 **Built-in fallback cube** — if no `model.obj` is found, a sample cube is shown so the viewer always has something to render

## Controls

| Input | Action |
|---|---|
| Left click + drag | Rotate the model |
| Right click + drag | Pan the camera |
| Scroll wheel | Zoom in / out |
| `O` | Open an `.obj` file |
| `R` | Reset camera |
| `Esc` | Quit |

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- A GPU/driver with OpenGL 3.3+ support

## Getting Started

```bash
git clone https://github.com/BenyVK/ObjViewer.git
cd ObjViewer/ObjViewer
dotnet run
```

By default, the viewer looks for a `model.obj` (and optional `model.mtl`) file in the working directory. If none is found, it falls back to a sample cube. You can also press `O` at any time to open a different `.obj` file through your OS's native file dialog.

## How It Works

- **Windowing & input** are handled by `Silk.NET.Windowing` and `Silk.NET.Input`.
- **Rendering** uses raw `Silk.NET.OpenGL` calls — a single VAO/VBO holds interleaved position + normal data, drawn with a minimal GLSL vertex/fragment shader pair.
- **OBJ parsing** is hand-written (no external mesh-loading library): it reads `v`, `vn`, and `f` lines, triangulates polygonal faces, and computes a flat fallback normal for faces missing normal data.
- **File dialogs** use a tiered approach — it tries `NativeFileDialogSharp` if present, falls back to a native Win32 `GetOpenFileName` call on Windows, `osascript`/`zenity` on macOS/Linux, and finally a console prompt if nothing else is available.

## Dependencies

| Package | Purpose |
|---|---|
| `Silk.NET` | Core windowing/input/OpenGL bindings |
| `Silk.NET.Windowing` | Cross-platform window creation |
| `Silk.NET.OpenGL` | OpenGL bindings |
| `Silk.NET.Input` | Mouse/keyboard input |

## License

This project is open source. Feel free to use, modify, and share it.

## Author

Made by [BenyVK](https://github.com/BenyVK)
