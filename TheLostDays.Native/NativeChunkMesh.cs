using System.Numerics;
using LostSpawns.Models;
using LostSpawns.Rendering;
using Raylib_cs;

namespace TheLostDays.Native;

internal sealed unsafe class NativeChunkMesh : IDisposable
{
    private Mesh _mesh;
    private bool _uploaded;

    public NativeChunkMesh(ChunkData chunk)
    {
        float[] source = VoxelMesher.GenerateMesh(chunk);
        int vertexCount = source.Length / 9;
        if (vertexCount == 0)
            return;

        _mesh = new Mesh(vertexCount, vertexCount / 3);
        _mesh.AllocVertices();
        _mesh.AllocNormals();
        _mesh.AllocColors();

        Span<float> vertices = _mesh.VerticesAs<float>();
        Span<float> normals = _mesh.NormalsAs<float>();
        Span<byte> colors = _mesh.ColorsAs<byte>();

        for (int i = 0; i < vertexCount; i++)
        {
            int s = i * 9;
            int v = i * 3;
            int c = i * 4;
            vertices[v] = source[s];
            vertices[v + 1] = source[s + 1];
            vertices[v + 2] = source[s + 2];
            normals[v] = source[s + 3];
            normals[v + 1] = source[s + 4];
            normals[v + 2] = source[s + 5];
            colors[c] = ToByte(source[s + 6]);
            colors[c + 1] = ToByte(source[s + 7]);
            colors[c + 2] = ToByte(source[s + 8]);
            colors[c + 3] = 255;
        }

        fixed (Mesh* mesh = &_mesh)
            Raylib.UploadMesh(mesh, false);
        _uploaded = true;
    }

    public void Draw(Material material)
    {
        if (_uploaded)
            Raylib.DrawMesh(_mesh, material, Matrix4x4.Identity);
    }

    public void Dispose()
    {
        if (_uploaded)
        {
            Raylib.UnloadMesh(_mesh);
            _uploaded = false;
        }
    }

    private static byte ToByte(float value) => (byte)Math.Clamp((int)(value * 255f + 0.5f), 0, 255);
}