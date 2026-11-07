// The Mite Net data type: a GH_Goo around the core's immutable LathNet and
// the parameter that carries it (McNeel, Grasshopper SDK: "Simple Data
// Types" — derive from GH_Goo<T>; "Simple Parameters" — derive from
// GH_Param<T>, never implement IGH_Param directly). The goo previews the
// families in Mite.Core's palette — black, white and neon — as roles
// (ViewportPalette: family A neon blue, family B neon red, T-junction ends
// the mark colour, free ends the warning red) resolved against the viewport's
// own background, so on Rhino's grey the neons are darkened to WCAG 2.1's
// 3:1 and on a white or black background they are the web app's colours; it
// bakes each family onto its own layer (Mite::A, Mite::B) in those colours.
// Internalised data is stored as LathNet.ToBytes().
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using Mite.Core.Display;
using Mite.Core.Geometry;
using Mite.Core.Gridshells;

namespace Mite.Grasshopper;

/// <summary>One Mite Net value on a Grasshopper wire.</summary>
public sealed class MiteNetGoo : GH_Goo<LathNet>, IGH_PreviewData, IGH_BakeAwareData
{
    /// <summary>The layer a baked family lands on (a sublayer of "Mite").</summary>
    public const string LayerRoot = "Mite";
    public static string LayerPath(bool familyA) => LayerRoot + "::" + (familyA ? "A" : "B");

    // Preview cache, built once per value
    private Polyline[]? _a, _b;
    private Point3d[]? _onLath, _free;
    private BoundingBox _box = BoundingBox.Unset;

    public MiteNetGoo() { }
    public MiteNetGoo(LathNet net) : base(net) { }

    public override bool IsValid => Value != null && Value.LathCount > 0;
    public override string IsValidWhyNot => Value == null ? "No net." : Value.LathCount == 0 ? "The net has no laths." : string.Empty;
    public override string TypeName => "Mite Net";
    public override string TypeDescription => "A gridshell net: surface, lath families A and B and their profiles";

    // LathNet is immutable, so sharing it is a valid duplicate
    public override IGH_Goo Duplicate() => new MiteNetGoo(Value);

    public override string ToString()
    {
        if (Value == null) return "Null Mite Net";
        string kind = Value.Kind == NetKind.Custom ? "" : " " + Value.Kind.ToString().ToLowerInvariant();
        return $"Mite Net{kind} ({Value.CountA} A + {Value.CountB} B laths{(Value.Surface != null ? ", surface" : "")})";
    }

    public override bool CastTo<Q>(ref Q target)
    {
        if (Value == null) return false;
        // a net plugged into a Mesh input gives its surface
        if (typeof(Q).IsAssignableFrom(typeof(GH_Mesh)) && Value.Surface != null)
        {
            target = (Q)(object)new GH_Mesh(MeshConvert.ToRhinoMesh(Value.Surface));
            return true;
        }
        return false;
    }

    public override bool CastFrom(object source)
    {
        if (source is LathNet net) { Value = net; return true; }
        if (source is MiteNetGoo goo) { Value = goo.Value; return true; }
        return false;
    }

    public override bool Write(GH_IWriter writer)
    {
        if (Value != null) writer.SetByteArray("MiteNet", Value.ToBytes());
        return true;
    }

    public override bool Read(GH_IReader reader)
    {
        if (!reader.ItemExists("MiteNet")) { Value = null!; return true; }
        try { Value = LathNet.FromBytes(reader.GetByteArray("MiteNet")); return true; }
        catch (Exception) { Value = null!; return false; }
    }

    // ---------- preview ----------

    private void EnsureCache()
    {
        if (_a != null || Value == null) return;
        _a = Value.FamilyA.Select(ToPolyline).ToArray();
        _b = Value.FamilyB.Select(ToPolyline).ToArray();
        var onLath = new List<Point3d>(); var free = new List<Point3d>();
        foreach (var e in Value.Ends())
        {
            if (e.Kind == LathEndKind.OnLath) onLath.Add(MeshConvert.ToRhinoPoint(e.Point));
            else if (e.Kind == LathEndKind.Free) free.Add(MeshConvert.ToRhinoPoint(e.Point));
        }
        _onLath = onLath.ToArray(); _free = free.ToArray();
        var box = BoundingBox.Empty;
        foreach (var pl in _a.Concat(_b)) box.Union(pl.BoundingBox);
        _box = box;
    }

    private static Polyline ToPolyline(Vec3d[] line) => new Polyline(line.Select(MeshConvert.ToRhinoPoint));

    public BoundingBox ClippingBox { get { EnsureCache(); return _box; } }

    public void DrawViewportWires(GH_PreviewWireArgs args) => Draw(args.Pipeline, null, args.Thickness);

    /// <summary>
    /// Draws the families in the palette's colours for the viewport's background
    /// (or all in <paramref name="selected"/> when the owner is selected), marks
    /// T-junction ends in the mark colour (ink on a light background, neon
    /// yellow on a dark one) and free ends in the warning red; border ends need
    /// no marker.
    /// </summary>
    public void Draw(Rhino.Display.DisplayPipeline pipeline, Color? selected, int thickness)
    {
        EnsureCache();
        if (_a == null) return;
        var pal = ViewportColours.For(ViewportColours.Background(pipeline));
        Color ca = selected ?? pal[(int)ViewportRole.FamilyA], cb = selected ?? pal[(int)ViewportRole.FamilyB];
        int w = Math.Max(2, thickness);
        foreach (var pl in _a) pipeline.DrawPolyline(pl, ca, w);
        foreach (var pl in _b!) pipeline.DrawPolyline(pl, cb, w);
        if (_onLath!.Length > 0) pipeline.DrawPoints(_onLath, Rhino.Display.PointStyle.RoundSimple, 6, pal[(int)ViewportRole.Mark]);
        if (_free!.Length > 0) pipeline.DrawPoints(_free, Rhino.Display.PointStyle.RoundSimple, 6, pal[(int)ViewportRole.Warn]);
    }

    public void DrawViewportMeshes(GH_PreviewMeshArgs args) { }

    // ---------- bake: one group, each family on its layer in its colour ----------

    public bool BakeGeometry(RhinoDoc doc, ObjectAttributes att, out Guid obj_guid)
    {
        obj_guid = Guid.Empty;
        var ids = BakeAll(doc, att);
        if (ids.Count == 0) return false;
        obj_guid = ids[0];
        return true;
    }

    /// <summary>
    /// Bakes every lath as a polyline named A0, A1, … / B0, B1, … on the layers
    /// Mite::A and Mite::B (created when missing, in the family colours for the
    /// application's viewport background; an existing layer keeps its colour),
    /// all in one group; the objects take their colour from the layer, so
    /// recolouring a family is one layer edit.
    /// </summary>
    public List<Guid> BakeAll(RhinoDoc doc, ObjectAttributes? att)
    {
        var ids = new List<Guid>();
        if (Value == null) return ids;
        att ??= doc.CreateDefaultAttributes();
        int group = doc.Groups.Add();
        foreach (var (family, role, isA) in new[] { (Value.FamilyA, ViewportRole.FamilyA, true), (Value.FamilyB, ViewportRole.FamilyB, false) })
        {
            if (family.Count == 0) continue;
            int layer = FamilyLayer(doc, isA, ViewportColours.OfApplication(role));
            string name = isA ? "A" : "B";
            for (int i = 0; i < family.Count; i++)
            {
                var a = att.Duplicate();
                if (layer >= 0)
                {
                    a.LayerIndex = layer;
                    a.ColorSource = ObjectColorSource.ColorFromLayer;
                }
                else
                {
                    a.ColorSource = ObjectColorSource.ColorFromObject;
                    a.ObjectColor = ViewportColours.OfApplication(role);
                }
                a.Name = name + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
                a.AddToGroup(group);
                var id = doc.Objects.AddPolyline(family[i].Select(MeshConvert.ToRhinoPoint), a);
                if (id != Guid.Empty) ids.Add(id);
            }
        }
        return ids;
    }

    /// <summary>The index of Mite::A or Mite::B, created with the colour when missing; −1 when the layer table refuses.</summary>
    internal static int FamilyLayer(RhinoDoc doc, bool familyA, Color colour)
    {
        try
        {
            string path = LayerPath(familyA);
            int index = doc.Layers.FindByFullPath(path, RhinoMath.UnsetIntIndex);
            if (index == RhinoMath.UnsetIntIndex) index = doc.Layers.AddPath(path, colour);
            return index == RhinoMath.UnsetIntIndex ? -1 : index;
        }
        catch (Exception) { return -1; }
    }
}

/// <summary>The Mite Net parameter (Params › Mite): carries, previews and bakes nets.</summary>
public sealed class MiteNetParameter : GH_Param<MiteNetGoo>, IGH_PreviewObject, IGH_BakeAwareObject
{
    public MiteNetParameter()
        : base("Mite Net", "Net",
            "A gridshell net: reference surface, lath families A and B and their profiles. " +
            "Produced by the net components (Asymptotic, Geodesic, Conjugate, Chebyshev Net, Net Kinetics) " +
            "and by Mite Net; taken by the fabrication and analysis components in place of Mesh + Curves.",
            "Mite", "Gridshells", GH_ParamAccess.item) { }

    public override Guid ComponentGuid => new("7A3C9E21-5B84-4F0D-9C6A-2E1D8B4F6A30");

    protected override Bitmap? Icon
    {
        get
        {
            var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Mite.Grasshopper.Resources.MiteNetParam.png");
            return stream != null ? new Bitmap(stream) : null;
        }
    }

    public bool Hidden { get; set; }
    public bool IsPreviewCapable => true;
    public BoundingBox ClippingBox => Preview_ComputeClippingBox();
    public void DrawViewportWires(IGH_PreviewArgs args)
    {
        if (Locked || VolatileData.IsEmpty) return;
        bool selected = Attributes?.GetTopLevel?.Selected ?? false;
        int thickness = args.DefaultCurveThickness + (selected ? 1 : 0);
        foreach (var goo in VolatileData.AllData(true).OfType<MiteNetGoo>())
            goo.Draw(args.Display, selected ? args.WireColour_Selected : (Color?)null, thickness);
    }
    public void DrawViewportMeshes(IGH_PreviewArgs args) => Preview_DrawMeshes(args);

    public bool IsBakeCapable => !VolatileData.IsEmpty;
    public void BakeGeometry(RhinoDoc doc, List<Guid> obj_ids) => BakeGeometry(doc, null!, obj_ids);
    public void BakeGeometry(RhinoDoc doc, ObjectAttributes att, List<Guid> obj_ids)
    {
        foreach (var goo in VolatileData.AllData(true).OfType<MiteNetGoo>())
            obj_ids.AddRange(goo.BakeAll(doc, att));
    }
}
