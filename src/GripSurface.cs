using System.Numerics;

namespace NivalisMods.Cigarette;

// Front-most shell in mask-local XY, looking from the palm along -Z. Keeping
// everything behind that shell solid prevents a hollow bowl accepting fingers.
internal sealed class GripSurface
{
    private const float Cell = 0.002f;
    private readonly float[] _depth;
    private readonly List<float>?[] _stem;
    private readonly float _minX, _minY;
    private readonly int _width, _height;

    internal GripSurface(Vector3[] vertices, int[] triangles, float bowlLimit = float.PositiveInfinity)
    {
        _minX = vertices.Min(v => v.X) - Cell;
        _minY = vertices.Min(v => v.Y) - Cell;
        _width = (int)MathF.Ceiling((vertices.Max(v => v.X) - _minX) / Cell) + 2;
        _height = (int)MathF.Ceiling((vertices.Max(v => v.Y) - _minY) / Cell) + 2;
        if (_width < 2 || _height < 2 || _width > 512 || _height > 512)
            throw new InvalidDataException("Unexpected mask dimensions for finger fitting.");
        _depth = Enumerable.Repeat(float.NegativeInfinity, _width * _height).ToArray();
        _stem = new List<float>?[_depth.Length];
        for (var i = 0; i < triangles.Length; i += 3)
        {
            var a = vertices[triangles[i]]; var b = vertices[triangles[i + 1]]; var c = vertices[triangles[i + 2]];
            var denominator = (b.Y - c.Y) * (a.X - c.X) + (c.X - b.X) * (a.Y - c.Y);
            if (MathF.Abs(denominator) < 1e-10f) continue;
            var x0 = Math.Max(0, (int)MathF.Floor((Math.Min(a.X, Math.Min(b.X, c.X)) - _minX) / Cell));
            var x1 = Math.Min(_width - 1, (int)MathF.Ceiling((Math.Max(a.X, Math.Max(b.X, c.X)) - _minX) / Cell));
            var y0 = Math.Max(0, (int)MathF.Floor((Math.Min(a.Y, Math.Min(b.Y, c.Y)) - _minY) / Cell));
            var y1 = Math.Min(_height - 1, (int)MathF.Ceiling((Math.Max(a.Y, Math.Max(b.Y, c.Y)) - _minY) / Cell));
            for (var y = y0; y <= y1; y++)
            for (var x = x0; x <= x1; x++)
            {
                var px = _minX + x * Cell; var py = _minY + y * Cell;
                var u = ((b.Y - c.Y) * (px - c.X) + (c.X - b.X) * (py - c.Y)) / denominator;
                var v = ((c.Y - a.Y) * (px - c.X) + (a.X - c.X) * (py - c.Y)) / denominator;
                if (u < -0.0001f || v < -0.0001f || u + v > 1.0001f) continue;
                var index = y * _width + x;
                var z = u * a.Z + v * b.Z + (1f - u - v) * c.Z;
                if (z <= bowlLimit) _depth[index] = Math.Max(_depth[index], z);
                else (_stem[index] ??= new List<float>()).Add(z);
            }
        }
        foreach (var column in _stem)
        {
            if (column == null) continue;
            column.Sort();
            for (var i = column.Count - 1; i > 0; i--)
                if (column[i] - column[i - 1] < 0.0001f) column.RemoveAt(i);
            // An odd crossing count means the stem connects through the bowl cut.
            if (column.Count % 2 != 0) column.Insert(0, bowlLimit);
        }
    }

    internal float Depth(float x, float y)
    {
        var ix = (int)MathF.Floor((x - _minX) / Cell);
        var iy = (int)MathF.Floor((y - _minY) / Cell);
        if (ix < 0 || iy < 0 || ix + 1 >= _width || iy + 1 >= _height) return float.NegativeInfinity;
        // Conservative cell corners avoid cracks between voxel faces/triangles.
        return Math.Max(Math.Max(_depth[iy * _width + ix], _depth[iy * _width + ix + 1]),
            Math.Max(_depth[(iy + 1) * _width + ix], _depth[(iy + 1) * _width + ix + 1]));
    }

    private float ColumnClearance(float x, float y, float z)
    {
        var clearance = z - Depth(x, y);
        var ix = (int)MathF.Floor((x - _minX) / Cell);
        var iy = (int)MathF.Floor((y - _minY) / Cell);
        if (ix < 0 || iy < 0 || ix + 1 >= _width || iy + 1 >= _height) return clearance;
        for (var row = 0; row <= 1; row++)
        for (var col = 0; col <= 1; col++)
        {
            var spans = _stem[(iy + row) * _width + ix + col];
            if (spans == null) continue;
            for (var i = 0; i + 1 < spans.Count; i += 2)
            {
                var distance = z < spans[i] ? spans[i] - z : z > spans[i + 1] ? z - spans[i + 1] :
                    -Math.Min(z - spans[i], spans[i + 1] - z);
                clearance = Math.Min(clearance, distance);
            }
        }
        return clearance;
    }

    internal float Clearance(Vector3 point, float radius)
    {
        var clearance = ColumnClearance(point.X, point.Y, point.Z) - radius;
        for (var i = 0; i < 8; i++)
        {
            var angle = i * MathF.PI / 4f;
            var offset = radius * 0.8f;
            clearance = Math.Min(clearance, ColumnClearance(point.X + MathF.Cos(angle) * offset,
                point.Y + MathF.Sin(angle) * offset, point.Z) - radius * 0.6f);
        }
        return clearance;
    }
}
