using System;
using System.Collections.Generic;

namespace SilkyNvg.Rendering.OpenGL
{
    internal class VertexCollection
    {

        private Vertex[] _vertices;
        private int _count;

        public int CurrentsOffset => _count;

        public ReadOnlySpan<Vertex> Vertices => _vertices;

        public VertexCollection()
        {
            _vertices = Array.Empty<Vertex>();
            _count = 0;
        }

        private void AllocVerts(int n)
        {
            if (_count + n > _vertices.Length)
            {
                int cverts = Math.Max(_count + n, 4096) + _vertices.Length / 2;
                Array.Resize(ref _vertices, cverts);
            }
        }

        public void AddVertex(Vertex vertex)
        {
            AllocVerts(1);
            _vertices[_count++] = vertex;
        }

        public void AddVertices(ReadOnlySpan<Vertex> vertices)
        {
            AllocVerts(vertices.Length);
            vertices.CopyTo(_vertices.AsSpan(_count));
            _count += vertices.Length;
        }

        public void Clear()
        {
            _count = 0;
        }

    }
}