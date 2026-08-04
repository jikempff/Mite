using System;
using Mite.Core.Geometry;

namespace Mite.Core.Dynamics;

public class MeshPhysics
{
    private readonly MeshData _mesh;
    private Vec3d[] _positions;
    private Vec3d[] _velocities;
    private readonly double[] _masses;
    private readonly bool[] _fixed;
    private readonly (int v0, int v1)[] _edges;
    private readonly double[] _restLengths;

    public Vec3d[] Positions => _positions;

    public MeshPhysics(MeshData mesh, bool[]? fixedVertices = null, double[]? masses = null)
    {
        _mesh = mesh;
        _positions = new Vec3d[mesh.VertexCount];
        Array.Copy(mesh.Vertices, _positions, mesh.VertexCount);
        _velocities = new Vec3d[mesh.VertexCount];
        _fixed = fixedVertices ?? new bool[mesh.VertexCount];
        _masses = masses ?? CreateUniformMasses(mesh.VertexCount, 1.0);

        _edges = mesh.BuildEdges();
        _restLengths = new double[_edges.Length];
        for (int i = 0; i < _edges.Length; i++)
            _restLengths[i] = (_positions[_edges[i].v0] - _positions[_edges[i].v1]).Length;
    }

    public void Step(double dt, Vec3d[]? externalForces = null)
    {
        var forces = new Vec3d[_mesh.VertexCount];

        if (externalForces != null)
            Array.Copy(externalForces, forces, Math.Min(externalForces.Length, forces.Length));

        for (int i = 0; i < _mesh.VertexCount; i++)
        {
            if (_fixed[i]) continue;
            Vec3d a = forces[i] / _masses[i];
            _velocities[i] = _velocities[i] + dt * a;
            _positions[i] = _positions[i] + dt * _velocities[i];
        }
    }

    public Vec3d[] ComputeSpringForces(double stiffness)
    {
        var forces = new Vec3d[_mesh.VertexCount];
        for (int e = 0; e < _edges.Length; e++)
        {
            int v0 = _edges[e].v0, v1 = _edges[e].v1;
            Vec3d diff = _positions[v1] - _positions[v0];
            double len = diff.Length;
            if (len < 1e-15) continue;
            double stretch = len - _restLengths[e];
            Vec3d force = stiffness * stretch * diff / len;
            forces[v0] = forces[v0] + force;
            forces[v1] = forces[v1] - force;
        }
        return forces;
    }

    public Vec3d[] ComputeGravityForces(Vec3d gravity)
    {
        var forces = new Vec3d[_mesh.VertexCount];
        for (int i = 0; i < _mesh.VertexCount; i++)
            forces[i] = _masses[i] * gravity;
        return forces;
    }

    public Vec3d[] ComputeDragForces(double coefficient)
    {
        var forces = new Vec3d[_mesh.VertexCount];
        for (int i = 0; i < _mesh.VertexCount; i++)
            forces[i] = -coefficient * _velocities[i];
        return forces;
    }

    public Vec3d[] ComputeSmoothnessForces(double strength)
    {
        var neighbors = _mesh.BuildVertexNeighbors();
        var forces = new Vec3d[_mesh.VertexCount];

        for (int i = 0; i < _mesh.VertexCount; i++)
        {
            if (neighbors[i].Length == 0) continue;
            Vec3d avg = Vec3d.Zero;
            foreach (int n in neighbors[i])
                avg = avg + _positions[n];
            avg = avg / neighbors[i].Length;
            forces[i] = strength * (avg - _positions[i]);
        }
        return forces;
    }

    public Vec3d[] ComputeAreaMinimizationForces(double strength)
    {
        var triMesh = _mesh.ToTriangulated();
        var forces = new Vec3d[_mesh.VertexCount];

        foreach (var f in triMesh.Faces)
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

    public static Vec3d[] CombineForces(params Vec3d[][] forceSets)
    {
        if (forceSets.Length == 0) return Array.Empty<Vec3d>();
        int n = forceSets[0].Length;
        var result = new Vec3d[n];
        foreach (var fs in forceSets)
            for (int i = 0; i < n; i++)
                result[i] = result[i] + fs[i];
        return result;
    }

    private static double[] CreateUniformMasses(int count, double mass)
    {
        var m = new double[count];
        for (int i = 0; i < m.Length; i++) m[i] = mass;
        return m;
    }
}
