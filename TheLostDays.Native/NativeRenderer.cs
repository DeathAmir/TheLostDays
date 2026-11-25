using OpenTK;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using SDL3;

namespace TheLostDays.Native;

internal sealed class NativeRenderer : IDisposable
{
    private readonly IntPtr _window;
    private readonly int _program;
    private readonly int _vao;
    private readonly int _vbo;
    private readonly int _vertexCount;
    private readonly int _mvpLocation;
    private const string VertexShader = """
        #version 330 core
        layout(location = 0) in vec3 aPosition;
        layout(location = 1) in vec3 aNormal;
        layout(location = 2) in vec3 aColor;
        uniform mat4 uMvp;
        out vec3 vNormal;
        out vec3 vColor;
        void main()
        {
            gl_Position = uMvp * vec4(aPosition, 1.0);
            vNormal = aNormal;
            vColor = aColor;
        }
        """;
    private const string FragmentShader = """
        #version 330 core
        in vec3 vNormal;
        in vec3 vColor;
        out vec4 FragColor;
        void main()
        {
            vec3 lightDir = normalize(vec3(-0.35, 0.85, 0.28));
            float diffuse = max(dot(normalize(vNormal), lightDir), 0.0);
            float light = 0.34 + diffuse * 0.76;
            FragColor = vec4(vColor * light, 1.0);
        }
        """;

    public NativeRenderer(IntPtr window, NativeWorld world)
    {
        _window = window;
        GL.LoadBindings(new SdlBindingsContext());
        GL.Enable(EnableCap.DepthTest);
        GL.Enable(EnableCap.CullFace);
        GL.CullFace(CullFaceMode.Back);
        GL.ClearColor(0.43f, 0.58f, 0.68f, 1f);

        _program = CreateProgram(VertexShader, FragmentShader);
        _mvpLocation = GL.GetUniformLocation(_program, "uMvp");
        _vao = GL.GenVertexArray();
        _vbo = GL.GenBuffer();
        float[] mesh = world.BuildMesh();
        _vertexCount = mesh.Length / 9;

        GL.BindVertexArray(_vao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
        GL.BufferData(BufferTarget.ArrayBuffer, (IntPtr)(mesh.Length * sizeof(float)), mesh, BufferUsageHint.StaticDraw);
        int stride = 9 * sizeof(float);
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, 0);
        GL.EnableVertexAttribArray(1);
        GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, 3 * sizeof(float));
        GL.EnableVertexAttribArray(2);
        GL.VertexAttribPointer(2, 3, VertexAttribPointerType.Float, false, stride, 6 * sizeof(float));
        GL.BindVertexArray(0);
    }

    public void Render(System.Numerics.Vector3 position, float yaw, float pitch, int width, int height)
    {
        GL.Viewport(0, 0, width, height);
        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
        float yr = MathHelper.DegreesToRadians(yaw);
        float pr = MathHelper.DegreesToRadians(pitch);
        var front = new Vector3(MathF.Cos(yr) * MathF.Cos(pr), MathF.Sin(pr), MathF.Sin(yr) * MathF.Cos(pr));
        var eye = new Vector3(position.X, position.Y, position.Z);
        var view = Matrix4.LookAt(eye, eye + front, Vector3.UnitY);
        var projection = Matrix4.CreatePerspectiveFieldOfView(MathHelper.DegreesToRadians(72f), width / (float)height, 0.1f, 1400f);
        var mvp = projection * view;

        GL.UseProgram(_program);
        GL.UniformMatrix4(_mvpLocation, false, ref mvp);
        GL.BindVertexArray(_vao);
        GL.DrawArrays(PrimitiveType.Triangles, 0, _vertexCount);
        GL.BindVertexArray(0);
    }

    private static int CreateProgram(string vertexSource, string fragmentSource)
    {
        int vertex = CompileShader(ShaderType.VertexShader, vertexSource);
        int fragment = CompileShader(ShaderType.FragmentShader, fragmentSource);
        int program = GL.CreateProgram();
        GL.AttachShader(program, vertex);
        GL.AttachShader(program, fragment);
        GL.LinkProgram(program);
        GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int linked);
        GL.DeleteShader(vertex);
        GL.DeleteShader(fragment);
        if (linked == 0)
        {
            string log = GL.GetProgramInfoLog(program);
            GL.DeleteProgram(program);
            throw new InvalidOperationException($"OpenGL program link failed: {log}");
        }
        return program;
    }

    private static int CompileShader(ShaderType type, string source)
    {
        int shader = GL.CreateShader(type);
        GL.ShaderSource(shader, source);
        GL.CompileShader(shader);
        GL.GetShader(shader, ShaderParameter.CompileStatus, out int compiled);
        if (compiled == 0)
        {
            string log = GL.GetShaderInfoLog(shader);
            GL.DeleteShader(shader);
            throw new InvalidOperationException($"OpenGL shader compilation failed: {log}");
        }
        return shader;
    }

    public void Dispose()
    {
        GL.DeleteBuffer(_vbo);
        GL.DeleteVertexArray(_vao);
        GL.DeleteProgram(_program);
    }

    private sealed class SdlBindingsContext : IBindingsContext
    {
        public IntPtr GetProcAddress(string procName) => SDL.GLGetProcAddress(procName);
    }
}
