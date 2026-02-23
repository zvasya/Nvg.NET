using Silk.NET.OpenGL;

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Numerics;

using NvgNET;
using NvgNET.Rendering;

using NvgNET.Blending;
using NvgNET.Images;
using NvgNET.Rendering.OpenGL.Blending;
using NvgNET.Rendering.OpenGL.Calls;
using NvgNET.Rendering.OpenGL.Shaders;
using NvgNET.Rendering.OpenGL.Textures;
using NvgNET.Rendering.OpenGL.Utils;

using Shader = NvgNET.Rendering.OpenGL.Shaders.Shader;
using Texture = Silk.NET.OpenGL.Texture;

namespace NvgNET.Rendering.OpenGL
{
	using Shaders_Shader = Shader;

	public sealed class OpenGLRenderer : INvgRenderer
    {

        private readonly CreateFlags _flags;
        private readonly VertexCollection _vertexCollection;
        private readonly CallQueue _callQueue;
        private VAO _vao;

        private SizeF _size;

        internal GL Gl { get; }

        internal bool StencilStrokes => _flags.HasFlag(CreateFlags.StencilStrokes);

        internal bool Debug => _flags.HasFlag(CreateFlags.Debug);

        internal StateFilter Filter { get; private set; }

        internal Shaders_Shader Shader { get; private set; }

        internal int DummyTex { get; private set; }

        internal TextureManager TextureManager { get; }

        public bool EdgeAntiAlias => _flags.HasFlag(CreateFlags.Antialias);

        public OpenGLRenderer(CreateFlags flags, GL gl)
        {
            _flags = flags;
            Gl = gl;

            _vertexCollection = new VertexCollection();
            _callQueue = new CallQueue();
            TextureManager = new TextureManager(this);
        }

        internal void StencilMask(uint mask)
        {
            if (Filter.StencilMask != mask)
            {
                Filter.StencilMask = mask;
                Gl.StencilMask(mask);
            }
        }

        internal void StencilFunc(StencilFunction func, int @ref, uint mask)
        {
            if (Filter.StencilFunc != func ||
                Filter.StencilFuncRef != @ref ||
                Filter.StencilFuncMask != mask)
            {
                Filter.StencilFunc = func;
                Filter.StencilFuncRef = @ref;
                Filter.StencilFuncMask = mask;
                Gl.StencilFunc(func, @ref, mask);
            }
        }

        internal void CheckError(string str)
        {
            if (!Debug)
            {
                return;
            }

            GLEnum err = Gl.GetError();
            if (err != GLEnum.NoError)
            {
                Console.Error.WriteLine("Error " + err + " after" + Environment.NewLine + str);
                return;
            }
        }

        public bool Create()
        {
            CheckError("init");

            Shader = new Shaders_Shader("Nvg-OpenGL-Shader", EdgeAntiAlias, this);
            if (!Shader.Status)
            {
                return false;
            }

            CheckError("uniform locations");
            Shader.GetUniforms();

            _vao = new VAO(Gl);
            _vao.Vbo = new VBO(Gl);

            Filter = new StateFilter();

            Shader.BindUniformBlock();

            // Dummy tex will always be at index 0.
            DummyTex = CreateTexture(Texture.Alpha, new Size(1, 1), 0, null);

            CheckError("create done!");

            Gl.Finish();

            return true;
        }

        public int CreateTexture(Texture type, Size size, ImageFlags imageFlags, ReadOnlySpan<byte> data)
        {
            ref var tex = ref TextureManager.AllocTexture();
            tex.Load(size, imageFlags, type, data);
            CheckError("creating texture.");
            return tex.Id;
        }

        public bool DeleteTexture(int image)
        {
            return TextureManager.DeleteTexture(image);
        }

        public bool UpdateTexture(int image, Rectangle bounds, ReadOnlySpan<byte> data)
        {
            ref var tex = ref TextureManager.FindTexture(image);
            if (tex.Id == 0)
            {
                return false;
            }
            tex.Update(bounds, data);
            CheckError("updating texture.");
            return true;
        }

        public bool GetTextureSize(int image, out Size size)
        {
            ref var tex = ref TextureManager.FindTexture(image);
            if (tex.Id == 0)
            {
                size = default;
                return false;
            }
            size = tex.Size;
            return false;
        }

        public void Viewport(SizeF size, float devicePixelRatio)
        {
            _size = size;
        }

        public void Cancel()
        {
            _vertexCollection.Clear();
            _callQueue.Clear();
            Shader.UniformManager.Clear();
        }

        public void Flush()
        {
            if (_callQueue.HasCalls)
            {
                Gl.PointSize(2.0f);
                Shader.Start();

                Gl.Enable(EnableCap.CullFace);
                Gl.CullFace(TriangleFace.Back);
                Gl.FrontFace(FrontFaceDirection.Ccw);
                Gl.Enable(EnableCap.Blend);
                Gl.Disable(EnableCap.DepthTest);
                Gl.Disable(EnableCap.ScissorTest);
                Gl.ColorMask(true, true, true, true);
                Gl.StencilMask(0xffffffff);
                Gl.StencilOp(StencilOp.Keep, StencilOp.Keep, StencilOp.Keep);
                Gl.StencilFunc(StencilFunction.Always, 0, 0xffffffff);
                Gl.ActiveTexture(TextureUnit.Texture0);
                Gl.BindTexture(TextureTarget.Texture2D, 0);
                Filter.BoundTexture = 0;
                Filter.StencilMask = 0xffffffff;
                Filter.StencilFunc = StencilFunction.Always;
                Filter.StencilFuncRef = 0;
                Filter.StencilFuncMask = 0xffffffff;
                Filter.BlendFunc = new Blend(GLEnum.InvalidEnum, GLEnum.InvalidEnum, GLEnum.InvalidEnum, GLEnum.InvalidEnum);

                Shader.UploadUniformData();

                _vao.Bind();
                _vao.Vbo.Update(_vertexCollection.Vertices);

                Shader.LoadInt(UniformLoc.Tex, 0);
                Shader.LoadVector(UniformLoc.ViewSize, new Vector2(_size.Width, _size.Height));

                Shader.BindUniformBuffer();
                _callQueue.Run();

                Gl.DisableVertexAttribArray(0);
                Gl.DisableVertexAttribArray(1);

                _vao.Unbind();

                Gl.Disable(EnableCap.CullFace);
                Shader.Stop();

                if (Filter.BoundTexture != 0)
                {
                    Filter.BoundTexture = 0;
                    Gl.BindTexture(TextureTarget.Texture2D, 0);
                }
            }

            _vertexCollection.Clear();
            _callQueue.Clear();
            Shader.UniformManager.Clear();
        }

        public void Fill(Paint paint, CompositeOperationState compositeOperation, Scissor scissor, float fringe, RectangleF bounds, ReadOnlySpan<Rendering.Path> paths)
        {
            int offset = _vertexCollection.CurrentsOffset;
            Path[] renderPaths = new Path[paths.Length];
            for (int i = 0; i < paths.Length; i++)
            {
                Rendering.Path path = paths[i];
                renderPaths[i] = new Path(
                    _vertexCollection.CurrentsOffset, path.FillCount,
                    _vertexCollection.CurrentsOffset + path.FillCount, path.StrokeCount
                );
                _vertexCollection.AddVertices(path.Fill);
                _vertexCollection.AddVertices(path.Stroke);
                offset += path.FillCount;
                offset += path.StrokeCount;
            }

            FragUniforms uniforms = new FragUniforms(paint, scissor, fringe, fringe, -1.0f, this);
            Call call;
            if ((paths.Length == 1) && paths[0].Convex) // Convex
            {
                int uniformOffset = Shader.UniformManager.AddUniform(uniforms);
                call = new ConvexFillCall(paint.Image, renderPaths, uniformOffset, compositeOperation, this);
            }
            else
            {
                _vertexCollection.AddVertex(new Vertex(bounds.Right, bounds.Bottom, 0.5f, 1.0f));
                _vertexCollection.AddVertex(new Vertex(bounds.Right, bounds.Top, 0.5f, 1.0f));
                _vertexCollection.AddVertex(new Vertex(bounds.Left, bounds.Bottom, 0.5f, 1.0f));
                _vertexCollection.AddVertex(new Vertex(bounds.Left, bounds.Top, 0.5f, 1.0f));

                FragUniforms stencilUniforms = new FragUniforms(-1.0f, Shaders.ShaderType.Simple);
                int uniformOffset = Shader.UniformManager.AddUniform(stencilUniforms);
                _ = Shader.UniformManager.AddUniform(uniforms);

                call = new FillCall(paint.Image, renderPaths, offset, uniformOffset, compositeOperation, this);
            }

            _callQueue.Add(call);
        }

        public void Stroke(Paint paint, CompositeOperationState compositeOperation, Scissor scissor, float fringe, float strokeWidth, ReadOnlySpan<Rendering.Path> paths)
        {
            int offset = _vertexCollection.CurrentsOffset;
            Path[] renderPaths = new Path[paths.Length];
            for (int i = 0; i < paths.Length; i++)
            {
                if (paths[i].StrokeCount > 0)
                {
                    renderPaths[i] = new Path(0, 0, offset, paths[i].StrokeCount);
                }
                else
                {
                    renderPaths[i] = default;
                }
                _vertexCollection.AddVertices(paths[i].Stroke);
                offset += paths[i].StrokeCount;
            }

            FragUniforms uniforms = new FragUniforms(paint, scissor, strokeWidth, fringe, -1.0f, this);
            Call call;
            if (StencilStrokes)
            {
                FragUniforms stencilUniforms = new FragUniforms(paint, scissor, strokeWidth, fringe, 1.0f - 0.5f / 255.0f, this);
                int uniformOffset = Shader.UniformManager.AddUniform(uniforms);
                _ = Shader.UniformManager.AddUniform(stencilUniforms);

                call = new StencilStrokeCall(paint.Image, renderPaths, uniformOffset, compositeOperation, this);
            }
            else
            {
                int uniformOffset = Shader.UniformManager.AddUniform(uniforms);
                call = new StrokeCall(paint.Image, renderPaths, uniformOffset, compositeOperation, this);
            }
            _callQueue.Add(call);
        }

        public void Triangles(Paint paint, CompositeOperationState compositeOperation, Scissor scissor, ReadOnlySpan<Vertex> vertices, float fringe)
        {
            int offset = _vertexCollection.CurrentsOffset;
            _vertexCollection.AddVertices(vertices);

            FragUniforms uniforms = new FragUniforms(paint, scissor, fringe, this);
            int uniformOffset = Shader.UniformManager.AddUniform(uniforms);
            Call call = new TrianglesCall(paint.Image, new Blend(compositeOperation, this), offset, (uint)vertices.Length, uniformOffset, this);
            _callQueue.Add(call);
        }

        public void Dispose()
        {
            Shader.Dispose();

            _vao.Dispose();

            TextureManager.Dispose();

            _callQueue.Clear();
            _vertexCollection.Clear();
        }

    }
}