using System;
using Mite.Core.Geometry;

namespace Mite.Core.Dynamics;

/// <summary>
/// Particle-spring model over a mesh's vertices and edges: explicit
/// integration with per-vertex masses and fixed flags, plus force models
/// (edge springs, gravity, drag, Laplacian smoothing, area minimization).
/// Adjacency and triangulation are built once at construction.
/// </summary>
public class MeshPhysics
{
    private readonly MeshData _mesh;
    private readonly MeshData _triMesh;
    private readonly int[][] _neighbors;
    private readonly Vec3d[] _positions;
    private readonly Vec3d[] _velocities;
    private readonly double[] _masses;
    private readonly bool[] _fixed;
    private readonly (int v0, int v1)[] _edges;
    private readonly double[] _restLengths;

    public Vec3d[] Positions => _positions;
    public Vec3d[] Velocities => _velocities;
    public (int v0, int v1)[] Edges => _edges;
    public double[] RestLengths => _restLengths;
    public bool[] Fixed => _fixed;
    public int VertexCount => _mesh.VertexCount;

    public MeshPhysics(MeshData mesh, bool[]? fixedVertices = null, double[]? masses = null)
    {
        _mesh = mesh;
        _triMesh = mesh.ToTriangulated();
        _neighbors = mesh.BuildVertexNeighbors();
        int nv = mesh.VertexCount;

        _positions = new Vec3d[nv];
        Array.Copy(mesh.Vertices, _positions, nv);
        _velocities = new Vec3d[nv];

        _fixed = new bool[nv];
        if (fixedVertices != null)
            Array.Copy(fixedVertices, _fixed, Math.Min(nv, fixedVertices.Length));

        _masses = new double[nv];
        for (int i = 0; i < nv; i++)
        {
            double m = masses != null && i < masses.Length ? masses[i] : 1.0;
            _masses[i] = m > 0 && !double.IsNaN(m) ? m : 1.0;
        }

        _edges = mesh.BuildEdges();
        _restLengths = new double[_edges.Length];
        for (int i = 0; i < _edges.Length; i++)
            _restLengths[i] = (_positions[_edges[i].v0] - _positions[_edges[i].v1]).Length;
    }

    /// <summary>Explicit (semi-implicit Euler) step under the given total forces.</summary>
    public void Step(double dt, Vec3d[]? externalForces = null)
    {
        for (int i = 0; i < _positions.Length; i++)
        {
            if (_fixed[i]) continue;
            Vec3d f = externalForces != null && i < externalForces.Length ? externalForces[i] : Vec3d.Zero;
            Vec3d a = f / _masses[i];
            _velocities[i] = _velocities[i] + dt * a;
            _positions[i] = _positions[i] + dt * _velocities[i];
        }
    }

    public void ResetVelocities() => Array.Clear(_velocities, 0, _velocities.Length);

    public double KineticEnergy()
    {
        double e = 0;
        for (int i = 0; i < _velocities.Length; i++)
            if (!_fixed[i]) e += 0.5 * _masses[i] * _velocities[i].LengthSquared;
        return e;
    }

    /// <summary>Linear edge springs toward the rest lengths (scaled by restScale).</summary>
    public Vec3d[] ComputeSpringForces(double stiffness, double restScale = 1.0)
    {
        var forces = new Vec3d[_positions.Length];
        for (int e = 0; e < _edges.Length; e++)
        {
            int v0 = _edges[e].v0, v1 = _edges[e].v1;
            Vec3d diff = _positions[v1] - _positions[v0];
            double len = diff.Length;
            if (len < 1e-15) continue;
            double stretch = len - restScale * _restLengths[e];
            Vec3d force = stiffness * stretch * diff / len;
            forces[v0] = forces[v0] + force;
            forces[v1] = forces[v1] - force;
        }
        return forces;
    }

    public Vec3d[] ComputeGravityForces(Vec3d gravity)
    {
        var forces = new Vec3d[_positions.Length];
        for (int i = 0; i < forces.Length; i++)
            forces[i] = _masses[i] * gravity;
        return forces;
    }

    public Vec3d[] ComputeDragForces(double coefficient)
    {
        var forces = new Vec3d[_positions.Length];
        for (int i = 0; i < forces.Length; i++)
            forces[i] = -coefficient * _velocities[i];
        return forces;
    }

    /// <summary>Umbrella-Laplacian smoothing force (pull toward the neighbor average).</summary>
    public Vec3d[] ComputeSmoothnessForces(double strength)
    {
        var forces = new Vec3d[_positions.Length];
        for (int i = 0; i < forces.Length; i++)
        {
            if (_neighbors[i].Length == 0) continue;
            Vec3d avg = Vec3d.Zero;
            foreach (int n in _neighbors[i])
                avg = avg + _positions[n];
            avg = avg / _neighbors[i].Length;
            forces[i] = strength * (avg - _positions[i]);
        }
        return forces;
    }

    /// <summary>Negative area gradient per vertex (soap-film tension).</summary>
    public Vec3d[] ComputeAreaMinimizationForces(double strength)
    {
        var forces = new Vec3d[_positions.Length];
        foreach (var f in _triMesh.Faces)
        {
            Vec3d p0 = _positions[f[0]], p1 = _positions[f[1]], p2 = _positions[f[2]];
            Vec3d normal = Vec3d.Cross(p1 - p0, p2 - p0);
            double area = normal.Length * 0.5;
            if (area < 1e-15) continue;
            normal = normal.Normalized();

            Vec3d grad0 = 0.5 * Vec3d.Cross(normal, p2 - p1);
            Vec3d grad1 = 0.5 * Vec3d.Cross(normal, p0 - p2);
            Vec3d grad2 = 0.5 * Vec3d.Cross(normal, p1 - p0);

            forces[f[0]] = forces[f[0]] - strength * grad0;
            forces[f[1]] = forces[f[1]] - strength * grad1;
            forces[f[2]] = forces[f[2]] - strength * grad2;
        }
        return forces;
    }

    /// <summary>Sums force sets; shorter sets are padded with zeros.</summary>
    public static Vec3d[] CombineForces(params Vec3d[][] forceSets)
    {
        int n = 0;
        foreach (var fs in forceSets) n = Math.Max(n, fs.Length);
        var result = new Vec3d[n];
        foreach (var fs in forceSets)
            for (int i = 0; i < fs.Length; i++)
                result[i] = result[i] + fs[i];
        return result;
    }
}
