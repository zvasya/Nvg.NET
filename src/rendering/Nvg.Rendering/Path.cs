using System;
using System.Collections.Generic;
using System.Drawing;
using System.Numerics;

using Collections.Pooled;

using NvgNET.Common;
using NvgNET.Graphics;
using NvgNET.Paths;

namespace NvgNET.Rendering
{
    public sealed class Path : IDisposable
    {
        private const int INIT_POINTS_SIZE = 128;
        private const int INIT_VERTS_SIZE = 256;

        private readonly PooledList<Point> _points = new PooledList<Point>(INIT_POINTS_SIZE);

        private readonly PooledList<Vertex> _fill = new PooledList<Vertex>(INIT_VERTS_SIZE);
        private readonly PooledList<Vertex> _stroke = new PooledList<Vertex>(INIT_VERTS_SIZE);

        private readonly PixelRatio _pixelRatio;
        private uint _bevelCount;
        private RectangleF _bounds;

        public bool Closed { get; private set; }

        public uint BevelCount => _bevelCount;

        public int FillCount => _fill.Count;
        public ReadOnlySpan<Vertex> Fill => _fill.Span;

        public int StrokeCount => _stroke.Count;
        public ReadOnlySpan<Vertex> Stroke => _stroke.Span;

        public bool Convex { get; private set; }

        public Winding Winding { get; internal set; }

        internal RectangleF Bounds => _bounds;

        internal Path(Winding winding, PixelRatio pixelRatio)
        {
            Winding = winding;
            _pixelRatio = pixelRatio;

            _bounds = RectangleF.FromLTRB(1e6f, 1e6f, -1e6f, -1e6f);
        }

        internal Vector2 LastPoint
        {
            get
            {
                if (_points.Count > 0)
                {
                    return _points[^1].Position;
                }
                return default;
            }
        }

        internal uint PointCount => (uint)_points.Count;

        internal void AddPoint(Vector2 position, PointFlags flags)
        {
            if (_points.Count > 0)
            {
                ref Point pt = ref _points.Span[^1];
                if (Point.Equals(pt.Position, position, _pixelRatio.DistTol))
                {
                    pt.Flags |= flags;
                    return;
                }
            }

            Point point = new Point(position, flags);
            
            _points.Add(point);
        }

        internal void Close()
        {
            Closed = true;
        }

        private void PolyReverse()
        {
	        var pooledList = _points.Span;
	        int i = 0, j = pooledList.Length - 1;
            while (i < j)
            {
                (pooledList[i], pooledList[j]) = (pooledList[j], pooledList[i]);
                i++;
                j--;
            }
        }

        internal void Flatten()
        {
            ref Point p0 = ref _points.Span[^1];
            ref Point p1 = ref _points.Span[0];
            if (Point.Equals(p0, p1, _pixelRatio.DistTol))
            {
                _points.RemoveAt(_points.Count - 1);
                p0 = ref _points.Span[^1];
                Close();
            }

            if (_points.Count > 2)
            {
                float area = Point.PolyArea(_points.Span);
                if (Winding == Winding.Ccw && area < 0.0f)
                {
                    PolyReverse();
                    p0 = ref _points.Span[^1];
                }
                if (Winding == Winding.Cw && area > 0.0f)
                {
                    PolyReverse();
                    p0 = ref _points.Span[^1];
                }
            }

	        Span<Point> pointsSpan = _points.Span;
            for (int i = 0; i < pointsSpan.Length; i++)
            {
	            ref Point point = ref pointsSpan[i];
	            p0.SetDeterminant(point);

	            float xMin = MathF.Min(_bounds.Left, p0.Position.X);
	            float yMin = MathF.Min(_bounds.Top, p0.Position.Y);
	            float xMax = MathF.Max(_bounds.Right, p0.Position.X);
	            float yMax = MathF.Max(_bounds.Bottom, p0.Position.Y);

	            _bounds = RectangleF.FromLTRB(xMin, yMin, xMax, yMax);

	            p0 = ref point;
            }
        }

        private void ButtCapStart(in Point p, Vector2 delta, float w, float d, float aa, float u0, float u1)
        {
            Vector2 pPos = p.Position - delta * d;
            Vector2 dl = new Vector2(delta.Y, -delta.X);
            _stroke.Add(new Vertex(pPos + (dl * w) - (delta * aa), u0, 0.0f));
            _stroke.Add(new Vertex(pPos - (dl * w) - (delta * aa), u1, 0.0f));
            _stroke.Add(new Vertex(pPos + (dl * w), u0, 1.0f));
            _stroke.Add(new Vertex(pPos - (dl * w), u1, 1.0f));
        }

        private void ButtCapEnd(in Point p, Vector2 delta, float w, float d, float aa, float u0, float u1)
        {
            Vector2 pPos = p.Position + delta * d;
            Vector2 dl = new Vector2(delta.Y, -delta.X);
            _stroke.Add(new Vertex(pPos + (dl * w), u0, 1.0f));
            _stroke.Add(new Vertex(pPos - (dl * w), u1, 1.0f));
            _stroke.Add(new Vertex(pPos + (dl * w) + (delta * aa), u0, 0.0f));
            _stroke.Add(new Vertex(pPos - (dl * w) + (delta * aa), u1, 0.0f));
        }

        private void RoundCapStart(in Point p, Vector2 delta, float w, uint ncap, float u0, float u1)
        {
            Vector2 pPos = p.Position;
            Vector2 dl = new Vector2(delta.Y, -delta.X);
            for (int i = 0; i < ncap; i++)
            {
                float a = i / (float)(ncap - 1) * MathF.PI;
                float ax = MathF.Cos(a) * w;
                float ay = MathF.Sin(a) * w;
                _stroke.Add(new Vertex(pPos - (dl * ax) - (delta * ay), u0, 1.0f));
                _stroke.Add(new Vertex(pPos, 0.5f, 1.0f));
            }
            _stroke.Add(new Vertex(pPos + (dl * w), u0, 1.0f));
            _stroke.Add(new Vertex(pPos - (dl * w), u1, 1.0f));
        }

        private void RoundCapEnd(in Point p, Vector2 delta, float w, uint ncap, float u0, float u1)
        {
            Vector2 pPos = p.Position;
            Vector2 dl = new Vector2(delta.Y, -delta.X);
            _stroke.Add(new Vertex(pPos + (dl * w), u0, 1.0f));
            _stroke.Add(new Vertex(pPos - (dl * w), u1, 1.0f));
            for (int i = 0; i < ncap; i++)
            {
                float a = i / (float)(ncap - 1) * MathF.PI;
                float ax = MathF.Cos(a) * w;
                float ay = MathF.Sin(a) * w;
                _stroke.Add(new Vertex(pPos, 0.5f, 1.0f));
                _stroke.Add(new Vertex(pPos - (dl * ax) + (delta * ay), u0, 1.0f));
            }
        }

        private void BevelJoin(ref Point p0, ref Point p1, float lw, float rw, float lu, float ru)
        {
            Vector2 dl0 = new Vector2(p0.Determinant.Y, -p0.Determinant.X);
            Vector2 dl1 = new Vector2(p1.Determinant.Y, -p1.Determinant.X);

            p1.JoinBevel(lw, rw, lu, ru, dl0, dl1, p0, _stroke);
        }

        internal void CalculateJoins(float iw, LineCap lineJoin, float miterLimit)
        {
	        Span<Point> pointsSpan = _points.Span;
	        ref Point p0 = ref pointsSpan[^1];
            ref Point p1 = ref pointsSpan[0];
            uint nleft = 0;

            _bevelCount = 0;

            for (int i = 0; i < pointsSpan.Length; i++)
            {
	            p1 = ref pointsSpan[i];
	            bool bevelOrRound = (lineJoin == LineCap.Bevel) || (lineJoin == LineCap.Round);
	            p1.Join(ref p0, iw, bevelOrRound, miterLimit, ref nleft, ref _bevelCount);

	            p0 = ref p1;
            }

            Convex = nleft == _points.Count;
        }

        internal void ExpandStroke(float aa, float u0, float u1, float w, LineCap lineCap, LineCap lineJoin, uint ncap)
        {
	        var pooledList = _points.Span;
            _fill.Clear();

            bool loop = Closed;
            _stroke.Clear();

            Point p0, p1;
            int s, e;
            if (loop)
            {
                p0 = pooledList[^1];
                p1 = pooledList[0];
                s = 0;
                e = pooledList.Length;
            }
            else
            {
                p0 = pooledList[0];
                p1 = pooledList[1];
                s = 1;
                e = pooledList.Length - 1;
            }

            if (!loop)
            {
                Vector2 d = p1.Position - p0.Position;
                d = Vector2.Normalize(d);
                if (lineCap is LineCap.Butt)
                {
                    ButtCapStart(p0, d, w, -aa * 0.5f, aa, u0, u1);
                }
                else if (lineCap is LineCap.Square)
                {
                    ButtCapStart(p0, d, w, w - aa, aa, u0, u1);
                }
                else if (lineCap is LineCap.Round)
                {
                    RoundCapStart(p0, d, w, ncap, u0, u1);
                }
            }

            for (int i = s; i < e; i++)
            {
	            p1 = pooledList[i];

                if (p1.Flags.HasFlag(PointFlags.Bevel) || p1.Flags.HasFlag(PointFlags.Innerbevel))
                {
                    if (lineJoin == LineCap.Round)
                    {
                        p1.RoundJoin(w, w, u0, u1, ncap, p0, _stroke);
                    }
                    else
                    {
                        p1.BevelJoin(w, w, u0, u1, p0, _stroke);
                    }
                }
                else
                {
                    _stroke.Add(new Vertex(p1.Position + (p1.MatrixDeterminant * w), u0, 1.0f));
                    _stroke.Add(new Vertex(p1.Position - (p1.MatrixDeterminant * w), u1, 1.0f));
                }

                p0 = p1;
            }
            if (s > 0)
            {
                p1 = pooledList[e];
            }

            if (loop)
            {
                _stroke.Add(new Vertex(_stroke[0].Pos, u0, 1.0f));
                _stroke.Add(new Vertex(_stroke[1].Pos, u1, 1.0f));
            }
            else
            {
                Vector2 d = p1.Position - p0.Position;
                d = Vector2.Normalize(d);
                if (lineCap is LineCap.Butt)
                {
                    ButtCapEnd(p1, d, w, -aa * 0.5f, aa, u0, u1);
                }
                else if (lineCap is LineCap.Square)
                {
                    ButtCapEnd(p1, d, w, w - aa, aa, u0, u1);
                }
                else if (lineCap is LineCap.Round)
                {
                    RoundCapEnd(p1, d, w, ncap, u0, u1);
                }
            }
        }

        private void ExpandFillFill(float woff, bool fringe)
        {
	        var points =  _points.Span;
            Point p0 = points[^1];
            Point p1 = points[0];

            foreach (Point point in points)
            {
                p1 = point;
                Point.Vertex(p0, p1, woff, _fill);

                p0 = p1;
            }
        }

        private void ExpandFillStroke(float woff, bool fringe, bool convex, float w)
        {
            if (fringe)
            {
                float lw = w + woff;
                float rw = w - woff;
                float lu = 0, ru = 1;

                if (convex)
                {
                    lw = woff;
                    lu = 0.5f;
                }

	            Span<Point> pointsSpan = _points.Span;
                ref Point p0 = ref pointsSpan[^1];
                ref Point p1 = ref pointsSpan[0];

                for (int i = 0; i < pointsSpan.Length; i++)
                {
	                ref Point point = ref pointsSpan[i];
	                p1 = ref point;
	                if ((p1.Flags & (PointFlags.Bevel | PointFlags.Innerbevel)) != 0)
	                {
		                BevelJoin(ref p0, ref p1, lw, rw, lu, ru);
	                }
	                else
	                {
		                _stroke.Add(new Vertex(p1.Position + (p1.MatrixDeterminant * lw), lu, 1.0f));
		                _stroke.Add(new Vertex(p1.Position - (p1.MatrixDeterminant * rw), ru, 1.0f));
	                }

	                p0 = ref p1;
                }

                _stroke.Add(new Vertex(_stroke[0].Pos, lu, 1.0f));
                _stroke.Add(new Vertex(_stroke[1].Pos, ru, 1.0f));
            }
            else
            {
                _stroke.Clear();
            }
        }

        internal void ExpandFill(float aa, bool fringe, bool convex, float w)
        {
            float woff = 0.5f * aa;
            ExpandFillFill(woff, fringe);
            ExpandFillStroke(woff, fringe, convex, w);
        }

        public void Dispose()
        {
	        _bevelCount = default;
		    _bounds = RectangleF.FromLTRB(1e6f, 1e6f, -1e6f, -1e6f);
	        Closed = default;
	        Convex = default;
	        Winding = default;
	        
	        _points.Dispose();
	        _points.Capacity = INIT_POINTS_SIZE;
	        _fill.Dispose();
	        _fill.Capacity = INIT_VERTS_SIZE;
	        _stroke.Dispose();
	        _stroke.Capacity = INIT_VERTS_SIZE;
        }
    }
}
