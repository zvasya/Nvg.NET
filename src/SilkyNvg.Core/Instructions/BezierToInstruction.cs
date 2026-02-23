using SilkyNvg.Common;
using SilkyNvg.Core.Paths;
using System;
using System.Numerics;
using SilkyNvg.Rendering;

namespace SilkyNvg.Core.Instructions
{
    internal struct BezierToInstruction
    {
        private const byte MAX_TESSELATION_DEPTH = 10;

        private readonly Vector2 _p0;
        private readonly Vector2 _p1;
        private readonly Vector2 _p2;

        private readonly float _tessTol;
        private readonly PathCache _pathCache;

        public BezierToInstruction(Vector2 p0, Vector2 p1, Vector2 p2, float tessTol, PathCache pathCache)
        {
            _p0 = p0;
            _p1 = p1;
            _p2 = p2;

            _tessTol = tessTol;
            _pathCache = pathCache;
        }

        private static void TesselateBezier(BezierToInstruction lineToInstruction, Path path, Vector2 p1, Vector2 p2, Vector2 p3, Vector2 p4, byte level, PointFlags flags)
        {
            if (level > MAX_TESSELATION_DEPTH)
            {
                return;
            }

            Vector2 p12 = (p1 + p2) * 0.5f;
            Vector2 p23 = (p2 + p3) * 0.5f;
            Vector2 p34 = (p3 + p4) * 0.5f;
            Vector2 p123 = (p12 + p23) * 0.5f;

            Vector2 d = p4 - p1;
            Vector2 ds = new Vector2(d.Y, d.X);
            Vector2 p2m4d = (p2 - p4) * ds;
            Vector2 p3m4d = (p4 - p1) * ds;
            float d2 = MathF.Abs(p2m4d.X - p2m4d.Y);
            float d3 = MathF.Abs(p3m4d.X - p3m4d.Y);

            float d23 = d2 + d3;
            if (d23 * d23 < lineToInstruction._tessTol * (d.X * d.X + d.Y * d.Y))
            {
	            path.AddPoint(p4, flags);
                return;
            }

            Vector2 p234 = (p23 + p34) * 0.5f;
            Vector2 p1234 = (p123 + p234) * 0.5f;

            TesselateBezier(lineToInstruction, path, p1, p12, p123, p1234, (byte)(level + 1), 0);
            TesselateBezier(lineToInstruction, path, p1234, p234, p34, p4, (byte)(level + 1), flags);
        }

        public static void BuildPaths(BezierToInstruction lineToInstruction)
        {
            if (lineToInstruction._pathCache.LastPath.PointCount > 0)
            {
                Vector2 last = lineToInstruction._pathCache.LastPath.LastPoint;
                TesselateBezier(lineToInstruction, lineToInstruction._pathCache.LastPath, last, lineToInstruction._p0, lineToInstruction._p1, lineToInstruction._p2, 0, PointFlags.Corner);
            }
        }

    }
}
