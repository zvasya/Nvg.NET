using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using NvgNET;
using NvgNET.Rendering.OpenGL;
using System.Numerics;
using NvgNET.Graphics;
using NvgNET.Paths;

namespace OpenGL_Example
{
    public class Program
    {

        private static GL gl;
        private static Nvg nvg;

        private static IWindow window;
        
        private static void Load()
        {
            gl = window.CreateOpenGL();

            OpenGLRenderer nvgRenderer = new(CreateFlags.StencilStrokes | CreateFlags.Debug, gl);
            nvg = Nvg.Create(nvgRenderer);
        }

        private static void Render(double _)
        {
            Vector2 winSize = window.Size.As<float>().ToSystem();
            Vector2 fbSize = window.FramebufferSize.As<float>().ToSystem();

            float pxRatio = fbSize.X / winSize.X;

            gl.Viewport(0, 0, (uint)fbSize.X, (uint)fbSize.Y);
            gl.ClearColor(0, 0, 0, 0);
            gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit);

            nvg.BeginFrame(winSize, pxRatio);

            nvg.BeginPath();
            nvg.MoveTo(250f, 75f);
            nvg.LineTo(323f, 301f);
            nvg.LineTo(131f, 161f);
            nvg.LineTo(369f, 161f);
            nvg.LineTo(177f, 301f);
            nvg.ClosePath();
            nvg.FillColour(Colour.Red);
            nvg.Fill();
            
            nvg.EndFrame();
        }

        private static void Close()
        {
            nvg.Dispose();
            gl.Dispose();
        }

        static void Main()
        {
            WindowOptions windowOptions = WindowOptions.Default;
            windowOptions.FramesPerSecond = -1;
            windowOptions.ShouldSwapAutomatically = true;
            windowOptions.Size = new Vector2D<int>(1000, 600);
            windowOptions.Title = "Nvg";
            windowOptions.VSync = false;
            windowOptions.PreferredDepthBufferBits = 24;
            windowOptions.PreferredStencilBufferBits = 8;

            window = Window.Create(windowOptions);
            window.Load += Load;
            window.Render += Render;
            window.Closing += Close;
            window.Run();

            window.Dispose();
        }
    }
}
