/*
 * ─────────────────────────────────────────────────────────────────
 *  OBJ Viewer — Silk.NET  |  با file picker داخلی
 *
 *  کنترل‌ها:
 *    کلیک چپ + drag   → چرخش
 *    کلیک راست + drag → جابجایی (Pan)
 *    Scroll           → زوم
 *    O                → باز کردن فایل OBJ  (native OS dialog)
 *    R                → ریست دوربین
 *    Escape           → خروج
 *
 *  وابستگی‌ها (csproj):
 *    <PackageReference Include="Silk.NET"                    Version="2.*" />
 *    <PackageReference Include="NativeFileDialogSharp"       Version="0.4.*" />
 *      یا اگر فقط ویندوز:
 *    <PackageReference Include="System.Windows.Forms"        Version="*" />  (فقط net-windows TFM)
 *
 *  اگر هیچ‌کدام از package‌ها نصب نیست، fallback به
 *  Console.ReadLine() فعال می‌شود.
 * ─────────────────────────────────────────────────────────────────
 */

using Silk.NET.Windowing;
using Silk.NET.OpenGL;
using Silk.NET.Maths;
using Silk.NET.Input;
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;

namespace ObjViewer
{
    // ══════════════════════════════════════════════════════
    //  Model data structures
    // ══════════════════════════════════════════════════════
    public class ObjModel
    {
        public List<Vector3> Vertices { get; set; } = new();
        public List<Vector3> Normals  { get; set; } = new();
        public List<Face>    Faces    { get; set; } = new();
    }

    public class Face
    {
        public List<int> VertexIndices { get; set; } = new();
        public List<int> NormalIndices { get; set; } = new();
    }

    // ══════════════════════════════════════════════════════
    //  Native OS file dialog helper
    //  Priority: NativeFileDialogSharp → Win32 COM → fallback
    // ══════════════════════════════════════════════════════
    internal static class FileDialog
    {
        /// <summary>
        /// Opens a native OS "Open File" dialog filtered to *.obj.
        /// Returns the chosen path, or null if cancelled.
        /// Must be called from the main/STA thread on Windows.
        /// </summary>
        public static string? OpenObjFile(string? startDir = null)
        {
            // ── 1. Try NativeFileDialogSharp (cross-platform, preferred) ──────
            try
            {
                // Late-bind so the code compiles even without the package
                var nfdType = Type.GetType(
                    "NativeFileDialogSharp.Dialog, NativeFileDialogSharp");

                if (nfdType != null)
                {
                    // Dialog.FileOpen("obj", startDir)
                    var method = nfdType.GetMethod("FileOpen",
                        new[] { typeof(string), typeof(string) });
                    dynamic? result = method?.Invoke(null,
                        new object?[] { "obj", startDir });

                    if (result != null && (bool)result.IsOk)
                        return (string)result.Path;

                    return null; // cancelled
                }
            }
            catch { /* package not present, fall through */ }

            // ── 2. Windows: Win32 GetOpenFileName via P/Invoke (no extra deps) ─
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                try { return Win32OpenDialog(startDir); }
                catch { /* fall through */ }
            }

            // ── 3. macOS: open via shell (zenity / osascript) ─────────────────
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                try
                {
                    string script =
                        "set f to choose file of type {\"obj\"} with prompt \"Open OBJ model\"\n" +
                        "POSIX path of f";
                    var proc = System.Diagnostics.Process.Start(
                        new System.Diagnostics.ProcessStartInfo("osascript")
                        {
                            ArgumentList = { "-e", script },
                            RedirectStandardOutput = true,
                            UseShellExecute = false
                        });
                    string? path = proc?.StandardOutput.ReadLine()?.Trim();
                    proc?.WaitForExit();
                    return string.IsNullOrWhiteSpace(path) ? null : path;
                }
                catch { }
            }

            // ── 4. Linux: zenity ──────────────────────────────────────────────
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                try
                {
                    var proc = System.Diagnostics.Process.Start(
                        new System.Diagnostics.ProcessStartInfo("zenity")
                        {
                            ArgumentList =
                            {
                                "--file-selection",
                                "--title=Open OBJ model",
                                "--file-filter=OBJ files | *.obj"
                            },
                            RedirectStandardOutput = true,
                            UseShellExecute = false
                        });
                    string? path = proc?.StandardOutput.ReadLine()?.Trim();
                    proc?.WaitForExit();
                    return string.IsNullOrWhiteSpace(path) ? null : path;
                }
                catch { }
            }

            // ── 5. Console fallback ───────────────────────────────────────────
            Console.Write("[FileDialog] مسیر فایل OBJ را وارد کنید: ");
            string? input = Console.ReadLine()?.Trim().Trim('"');
            return string.IsNullOrWhiteSpace(input) ? null : input;
        }

        // ── Win32 P/Invoke GetOpenFileName ────────────────────────────────────
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct OPENFILENAME
        {
            public int    lStructSize;
            public IntPtr hwndOwner;
            public IntPtr hInstance;
            public string lpstrFilter;
            public string lpstrCustomFilter;
            public int    nMaxCustFilter;
            public int    nFilterIndex;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string lpstrFile;
            public int    nMaxFile;
            public string lpstrFileTitle;
            public int    nMaxFileTitle;
            public string lpstrInitialDir;
            public string lpstrTitle;
            public int    Flags;
            public short  nFileOffset;
            public short  nFileExtension;
            public string lpstrDefExt;
            public IntPtr lCustData;
            public IntPtr lpfnHook;
            public string lpTemplateName;
            public IntPtr pvReserved;
            public int    dwReserved;
            public int    FlagsEx;
        }

        [DllImport("comdlg32.dll", CharSet = CharSet.Unicode)]
        private static extern bool GetOpenFileName(ref OPENFILENAME ofn);

        private static string? Win32OpenDialog(string? startDir)
        {
            var ofn = new OPENFILENAME();
            ofn.lStructSize    = Marshal.SizeOf(ofn);
            ofn.lpstrFilter    = "OBJ Models\0*.obj\0All Files\0*.*\0";
            ofn.lpstrFile      = new string('\0', 512);
            ofn.nMaxFile       = 512;
            ofn.lpstrTitle     = "Open OBJ Model";
            ofn.lpstrInitialDir = startDir ?? Environment.GetFolderPath(
                                      Environment.SpecialFolder.MyDocuments);
            ofn.Flags          = 0x00001000 | 0x00000800; // OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST

            bool ok = GetOpenFileName(ref ofn);
            if (!ok) return null;
            string path = ofn.lpstrFile;
            int nul = path.IndexOf('\0');
            return nul > 0 ? path[..nul] : path;
        }
    }

    // ══════════════════════════════════════════════════════
    //  Main viewer
    // ══════════════════════════════════════════════════════
    public class ObjViewer
    {
        private IWindow?       _window;
        private GL?            _gl;
        private IInputContext? _input;
        private ObjModel       _model        = new();
        private uint           _vao, _vbo;
        private uint           _shaderProgram;

        // Mouse state
        private bool    _leftDrag, _rightDrag;
        private Vector2 _lastMouse = Vector2.Zero;

        // Camera / transform
        private float   _rotX   = 20f;
        private float   _rotY   = 30f;
        private Vector3 _pan    = Vector3.Zero;
        private float   _zoom   = 5f;

        // Pending load request (set from key handler, executed on render thread)
        private string? _pendingLoadPath = null;
        private readonly object _loadLock = new();

        // Shader uniform locations
        private int _locModel, _locView, _locProj;
        private int _locLight, _locViewPos, _locColor;

        private int _vertexCount;

        // ── Entry point ───────────────────────────────────────────────────────
        public void Run()
        {
            var opts = WindowOptions.Default;
            opts.Size  = new Vector2D<int>(900, 650);
            opts.Title = "OBJ Viewer  |  O = Open file   R = Reset   Esc = Quit";
            opts.PreferredDepthBufferBits = 24;

            _window = Window.Create(opts);
            _window.Load             += OnLoad;
            _window.Render           += OnRender;
            _window.FramebufferResize += OnResize;
            _window.Run();
        }

        // ── Initialisation ────────────────────────────────────────────────────
        private unsafe void OnLoad()
        {
            if (_window == null) return;

            _gl    = _window.CreateOpenGL();
            _input = _window.CreateInput();

            _gl.ClearColor(0.12f, 0.12f, 0.18f, 1f);
            _gl.Enable(EnableCap.DepthTest);

            foreach (var m in _input.Mice)
            {
                m.MouseDown += OnMouseDown;
                m.MouseUp   += OnMouseUp;
                m.MouseMove += OnMouseMove;
                m.Scroll    += OnScroll;
            }
            foreach (var k in _input.Keyboards)
                k.KeyDown += OnKeyDown;

            // Load default model if present, otherwise show cube
            if (File.Exists("model.obj"))
                LoadAndUpload("model.obj");
            else
                LoadCube();

            SetupShader();
        }

        // ── Keyboard ──────────────────────────────────────────────────────────
        private void OnKeyDown(IKeyboard kb, Key key, int _)
        {
            switch (key)
            {
                case Key.O:
                    // Open dialog on a background STA thread (required by Win32)
                    var t = new Thread(() =>
                    {
                        string? path = FileDialog.OpenObjFile();
                        if (path != null)
                            lock (_loadLock) { _pendingLoadPath = path; }
                    });
                    t.SetApartmentState(ApartmentState.STA);
                    t.IsBackground = true;
                    t.Start();
                    break;

                case Key.R:
                    _rotX = 20f; _rotY = 30f;
                    _pan  = Vector3.Zero;
                    _zoom = 5f;
                    break;

                case Key.Escape:
                    _window?.Close();
                    break;
            }
        }

        // ── Mouse ─────────────────────────────────────────────────────────────
        private void OnMouseDown(IMouse m, MouseButton btn)
        {
            if (btn == MouseButton.Left)  { _leftDrag  = true; _lastMouse = m.Position; }
            if (btn == MouseButton.Right) { _rightDrag = true; _lastMouse = m.Position; }
        }
        private void OnMouseUp(IMouse m, MouseButton btn)
        {
            if (btn == MouseButton.Left)  _leftDrag  = false;
            if (btn == MouseButton.Right) _rightDrag = false;
        }
        private void OnMouseMove(IMouse m, Vector2 pos)
        {
            var d = pos - _lastMouse;
            _lastMouse = pos;

            if (_leftDrag)
            {
                _rotY += d.X * 0.5f;
                _rotX  = Math.Clamp(_rotX + d.Y * 0.5f, -89f, 89f);
            }
            if (_rightDrag)
            {
                float speed = _zoom * 0.001f;
                _pan.X += d.X * speed;
                _pan.Y -= d.Y * speed;
            }
        }
        private void OnScroll(IMouse _, ScrollWheel s)
            => _zoom = Math.Clamp(_zoom - s.Y * 0.4f, 0.3f, 500f);

        // ── Render loop ───────────────────────────────────────────────────────
        private unsafe void OnRender(double _)
        {
            if (_gl == null || _window == null) return;

            // Check for a pending file load (triggered by O key)
            string? toLoad = null;
            lock (_loadLock)
            {
                toLoad = _pendingLoadPath;
                _pendingLoadPath = null;
            }
            if (toLoad != null)
            {
                LoadAndUpload(toLoad);
                _window.Title = $"OBJ Viewer  |  {Path.GetFileName(toLoad)}  |  O=Open  R=Reset  Esc=Quit";
            }

            _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            var camPos    = new Vector3(_pan.X, _pan.Y, _zoom);
            var camTarget = new Vector3(_pan.X, _pan.Y, 0f);

            var model = Matrix4x4.CreateRotationX(Rad(_rotX))
                      * Matrix4x4.CreateRotationY(Rad(_rotY));
            var view  = Matrix4x4.CreateLookAt(camPos, camTarget, Vector3.UnitY);
            var proj  = Matrix4x4.CreatePerspectiveFieldOfView(
                            Rad(45f),
                            (float)_window.Size.X / _window.Size.Y,
                            0.01f, 1000f);

            _gl.UseProgram(_shaderProgram);
            _gl.UniformMatrix4(_locModel,   1, false, (float*)&model);
            _gl.UniformMatrix4(_locView,    1, false, (float*)&view);
            _gl.UniformMatrix4(_locProj,    1, false, (float*)&proj);
            _gl.Uniform3(_locViewPos, camPos.X, camPos.Y, camPos.Z);

            _gl.BindVertexArray(_vao);
            _gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)_vertexCount);
            _gl.BindVertexArray(0);
            _gl.UseProgram(0);
        }

        private void OnResize(Vector2D<int> sz)
            => _gl?.Viewport(0, 0, (uint)sz.X, (uint)sz.Y);

        // ══════════════════════════════════════════════════════
        //  OBJ Loading
        // ══════════════════════════════════════════════════════
        private void LoadAndUpload(string path)
        {
            var m = ParseObj(path);
            if (m == null) return;
            _model = m;
            UploadModel();
        }

        private ObjModel? ParseObj(string path)
        {
            try
            {
                var m = new ObjModel();
                var ci = System.Globalization.CultureInfo.InvariantCulture;

                foreach (var raw in File.ReadLines(path))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line[0] == '#') continue;
                    var p = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (p.Length == 0) continue;

                    switch (p[0])
                    {
                        case "v" when p.Length >= 4:
                            m.Vertices.Add(new Vector3(
                                float.Parse(p[1], ci),
                                float.Parse(p[2], ci),
                                float.Parse(p[3], ci)));
                            break;

                        case "vn" when p.Length >= 4:
                            m.Normals.Add(new Vector3(
                                float.Parse(p[1], ci),
                                float.Parse(p[2], ci),
                                float.Parse(p[3], ci)));
                            break;

                        case "f" when p.Length >= 4:
                            // Fan-triangulate polygons  (3-vert, 4-vert, n-vert)
                            var vIdx = new List<int>();
                            var nIdx = new List<int>();
                            for (int i = 1; i < p.Length; i++)
                            {
                                var tok = p[i].Split('/');
                                vIdx.Add(int.Parse(tok[0]) - 1);
                                if (tok.Length > 2 && tok[2].Length > 0)
                                    nIdx.Add(int.Parse(tok[2]) - 1);
                                else
                                    nIdx.Add(-1);
                            }
                            for (int i = 1; i < vIdx.Count - 1; i++)
                            {
                                var face = new Face();
                                face.VertexIndices.AddRange(new[] { vIdx[0],   vIdx[i],   vIdx[i+1]   });
                                face.NormalIndices.AddRange(new[] { nIdx[0],   nIdx[i],   nIdx[i+1]   });
                                m.Faces.Add(face);
                            }
                            break;
                    }
                }

                Console.WriteLine($"[OBJ] {m.Vertices.Count} vertices, {m.Faces.Count} triangles — {path}");
                return m;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[OBJ] Error: {ex.Message}");
                return null;
            }
        }

        private void LoadCube()
        {
            Console.WriteLine("[OBJ] model.obj not found — showing sample cube.");
            _model = new ObjModel();

            // Vertex + normal interleaved, 6 floats each, 24 unique verts
            float[] v = {
                -1,-1, 1, 0, 0, 1,   1,-1, 1, 0, 0, 1,   1, 1, 1, 0, 0, 1,  -1, 1, 1, 0, 0, 1,
                -1,-1,-1, 0, 0,-1,   1,-1,-1, 0, 0,-1,   1, 1,-1, 0, 0,-1,  -1, 1,-1, 0, 0,-1,
                -1,-1,-1,-1, 0, 0,  -1,-1, 1,-1, 0, 0,  -1, 1, 1,-1, 0, 0,  -1, 1,-1,-1, 0, 0,
                 1,-1,-1, 1, 0, 0,   1,-1, 1, 1, 0, 0,   1, 1, 1, 1, 0, 0,   1, 1,-1, 1, 0, 0,
                -1, 1,-1, 0, 1, 0,   1, 1,-1, 0, 1, 0,   1, 1, 1, 0, 1, 0,  -1, 1, 1, 0, 1, 0,
                -1,-1,-1, 0,-1, 0,   1,-1,-1, 0,-1, 0,   1,-1, 1, 0,-1, 0,  -1,-1, 1, 0,-1, 0,
            };
            int[] idx = {
                0,1,2, 2,3,0,    4,6,5, 6,4,7,
                8,9,10, 10,11,8,  12,14,13, 14,12,15,
                16,17,18, 18,19,16, 20,22,21, 22,20,23,
            };
            for (int i = 0; i < v.Length; i += 6)
            {
                _model.Vertices.Add(new Vector3(v[i], v[i+1], v[i+2]));
                _model.Normals .Add(new Vector3(v[i+3], v[i+4], v[i+5]));
            }
            for (int i = 0; i < idx.Length; i += 3)
            {
                var f = new Face();
                f.VertexIndices.AddRange(new[]{idx[i],idx[i+1],idx[i+2]});
                f.NormalIndices.AddRange(new[]{idx[i],idx[i+1],idx[i+2]});
                _model.Faces.Add(f);
            }
            UploadModel();
        }

        // ══════════════════════════════════════════════════════
        //  GPU upload
        // ══════════════════════════════════════════════════════
        private unsafe void UploadModel()
        {
            if (_gl == null) return;

            // Auto-center & normalise to [-1, 1]
            if (_model.Vertices.Count > 0)
            {
                var mn = new Vector3(float.MaxValue);
                var mx = new Vector3(float.MinValue);
                foreach (var vtx in _model.Vertices) { mn = Vector3.Min(mn, vtx); mx = Vector3.Max(mx, vtx); }
                var ctr   = (mn + mx) * 0.5f;
                float ext = Math.Max(Math.Max(mx.X - mn.X, mx.Y - mn.Y), mx.Z - mn.Z);
                float sc  = ext > 0 ? 2f / ext : 1f;
                for (int i = 0; i < _model.Vertices.Count; i++)
                    _model.Vertices[i] = (_model.Vertices[i] - ctr) * sc;
            }

            // Build interleaved vertex buffer
            var data = new List<float>(_model.Faces.Count * 3 * 6);
            foreach (var face in _model.Faces)
            {
                // Compute flat normal as fallback
                Vector3 flatN = Vector3.UnitY;
                if (face.VertexIndices.Count >= 3)
                {
                    int ai = face.VertexIndices[0], bi = face.VertexIndices[1], ci = face.VertexIndices[2];
                    if (ai < _model.Vertices.Count && bi < _model.Vertices.Count && ci < _model.Vertices.Count)
                        flatN = Vector3.Normalize(Vector3.Cross(
                            _model.Vertices[bi] - _model.Vertices[ai],
                            _model.Vertices[ci] - _model.Vertices[ai]));
                }

                for (int i = 0; i < face.VertexIndices.Count; i++)
                {
                    int vi = face.VertexIndices[i];
                    if (vi >= _model.Vertices.Count) continue;
                    var pos = _model.Vertices[vi];

                    Vector3 n = flatN;
                    if (i < face.NormalIndices.Count)
                    {
                        int ni = face.NormalIndices[i];
                        if (ni >= 0 && ni < _model.Normals.Count)
                            n = _model.Normals[ni];
                    }

                    data.Add(pos.X); data.Add(pos.Y); data.Add(pos.Z);
                    data.Add(n.X);   data.Add(n.Y);   data.Add(n.Z);
                }
            }
            _vertexCount = data.Count / 6;

            // Delete old GPU resources if they exist
            if (_vao != 0) { _gl.DeleteVertexArray(_vao); _gl.DeleteBuffer(_vbo); }

            _vao = _gl.GenVertexArray();
            _gl.BindVertexArray(_vao);

            _vbo = _gl.GenBuffer();
            _gl.BindBuffer(GLEnum.ArrayBuffer, _vbo);

            fixed (float* ptr = data.ToArray())
                _gl.BufferData(GLEnum.ArrayBuffer, (nuint)(data.Count * sizeof(float)), ptr, GLEnum.StaticDraw);

            uint stride = (uint)(6 * sizeof(float));
            _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, (void*)0);
            _gl.EnableVertexAttribArray(0);
            _gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, (void*)(3 * sizeof(float)));
            _gl.EnableVertexAttribArray(1);

            _gl.BindBuffer(GLEnum.ArrayBuffer, 0);
            _gl.BindVertexArray(0);

            // Reset camera for new model
            _rotX = 20f; _rotY = 30f; _pan = Vector3.Zero; _zoom = 5f;
        }

        // ══════════════════════════════════════════════════════
        //  Shader
        // ══════════════════════════════════════════════════════
        private unsafe void SetupShader()
        {
            if (_gl == null) return;

            const string VS = @"
                #version 330 core
                layout(location=0) in vec3 aPos;
                layout(location=1) in vec3 aNorm;
                uniform mat4 uModel, uView, uProj;
                out vec3 FragPos, Normal;
                void main() {
                    FragPos = vec3(uModel * vec4(aPos, 1.0));
                    Normal  = mat3(transpose(inverse(uModel))) * aNorm;
                    gl_Position = uProj * uView * vec4(FragPos, 1.0);
                }";

            const string FS = @"
                #version 330 core
                in vec3 FragPos, Normal;
                out vec4 FragColor;
                uniform vec3 uLightPos, uViewPos, uColor;
                void main() {
                    vec3 n   = normalize(Normal);
                    vec3 l   = normalize(uLightPos - FragPos);
                    vec3 v   = normalize(uViewPos  - FragPos);
                    vec3 r   = reflect(-l, n);
                    float d  = max(dot(n, l), 0.0);
                    float s  = pow(max(dot(v, r), 0.0), 64.0);
                    vec3 col = (0.25 + d + 0.5 * s) * uColor;
                    FragColor = vec4(col, 1.0);
                }";

            uint Compile(ShaderType type, string src)
            {
                uint sh = _gl.CreateShader(type);
                _gl.ShaderSource(sh, src);
                _gl.CompileShader(sh);
                _gl.GetShader(sh, ShaderParameterName.CompileStatus, out int ok);
                if (ok == 0) Console.WriteLine(_gl.GetShaderInfoLog(sh));
                return sh;
            }

            uint vs = Compile(ShaderType.VertexShader,   VS);
            uint fs = Compile(ShaderType.FragmentShader, FS);
            _shaderProgram = _gl.CreateProgram();
            _gl.AttachShader(_shaderProgram, vs); _gl.AttachShader(_shaderProgram, fs);
            _gl.LinkProgram(_shaderProgram);
            _gl.DetachShader(_shaderProgram, vs); _gl.DeleteShader(vs);
            _gl.DetachShader(_shaderProgram, fs); _gl.DeleteShader(fs);

            _gl.UseProgram(_shaderProgram);
            _locModel   = _gl.GetUniformLocation(_shaderProgram, "uModel");
            _locView    = _gl.GetUniformLocation(_shaderProgram, "uView");
            _locProj    = _gl.GetUniformLocation(_shaderProgram, "uProj");
            _locLight   = _gl.GetUniformLocation(_shaderProgram, "uLightPos");
            _locViewPos = _gl.GetUniformLocation(_shaderProgram, "uViewPos");
            _locColor   = _gl.GetUniformLocation(_shaderProgram, "uColor");
            _gl.Uniform3(_locLight, 3f, 5f, 4f);
            _gl.Uniform3(_locColor, 0.78f, 0.54f, 0.24f);
            _gl.UseProgram(0);
        }

        private static float Rad(float d) => d * MathF.PI / 180f;
    }

    // ══════════════════════════════════════════════════════
    //  Entry point
    // ══════════════════════════════════════════════════════
    class Program
    {
        [STAThread] // needed for Win32 COM dialog
        static void Main() => new ObjViewer().Run();
    }
}