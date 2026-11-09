// Material and load inputs against the range of real materials, to catch a
// value typed in another unit (ROADMAP B, runtime messages follow-up (d):
// "E / Allowable unit slips (MPa typed as Pa) in Gridshell Analysis and Net
// Kinetics"). The analysis takes SI units — pascals, kg/m³, N/m² — while
// data sheets give N/mm² (MPa), GPa, g/cm³ or t/m³ and kN/m², so the
// likeliest slips are factors of 1 000, 1 000 000 and 1 000 000 000; each
// message names the value as read, why the result is then meaningless, and
// the number to enter if the other unit was meant. A unit mismatch that no
// check caught lost the Mars Climate Orbiter (Mishap Investigation Board,
// Phase I report, 10 November 1999: thruster impulses in pound-force
// seconds where newton seconds were specified, a factor 4.45, noticed by
// two navigators and dismissed) — the check belongs at the input.
//
// The windows come from the material property charts of Ashby (Materials
// Selection in Mechanical Design, 4th ed. 2011, ch. 4: Young's modulus
// 1e-4 … 1e3 GPa over all engineering materials, elastic limit 0.1 … a few
// thousand MPa, density 0.01 … 20 Mg/m³, the strength-to-modulus guide lines
// σ_f/E from 1e-4 to 1e-1 with elastomers alone beyond) and, for the
// materials laths are made of: EN 338:2016 Table 1 and 3 (structural timber
// C14 … D80: f_m,k 14 … 80 N/mm², E_0,mean 7 … 24 kN/mm², ρ_mean 350 … 1080
// kg/m³), EN 1993-1-1 §3.2.6 (steel E = 210 000 N/mm², f_y 235 … 460), EN
// 13706-3 (pultruded GFRP grade E23: full-section modulus ≥ 23 GPa, flexural
// strength ≥ 240 MPa), and for the loads EN 1991-1-3 (characteristic ground
// snow loads of 0.5 … 5 kN/m² over most of Europe) with cladding at 0.1 …
// 0.5 kN/m². The windows are deliberately wide — a plastic lath (E 1 GPa)
// or a CFRP strip (σ_f 1.5 GPa) must pass — so a note means a factor of a
// thousand or more, never a material choice.
using System;
using System.Collections.Generic;

namespace Mite.Core.Diagnostics;

public static partial class InputAdvice
{
    /// <summary>Young's modulus below this (Pa) is no structural solid: 0.1 GPa (soft polymers sit at 0.2 … 1 GPa).</summary>
    public const double ModulusMin = 1e8;
    /// <summary>Young's modulus above this (Pa) exceeds every material (diamond ≈ 1 000 GPa).</summary>
    public const double ModulusMax = 1.2e12;
    /// <summary>An allowable stress below this (Pa) is foam: 0.1 MPa.</summary>
    public const double StrengthMin = 1e5;
    /// <summary>An allowable stress above this (Pa) exceeds every bulk material (CFRP ≈ 1.5 … 3 GPa).</summary>
    public const double StrengthMax = 5e9;
    /// <summary>Allowable / E below this is no material's failure strain (ceramics in tension ≈ 1e-4 … 1e-3).</summary>
    public const double FailureStrainMin = 1e-4;
    /// <summary>Allowable / E above this is an elastomer, not a lath (GFRP and polymers reach 1e-2 … 5e-2).</summary>
    public const double FailureStrainMax = 0.1;
    /// <summary>A density below this (kg/m³) is lighter than a rigid foam (air is 1.2).</summary>
    public const double DensityMin = 10;
    /// <summary>A density above this (kg/m³) exceeds every metal (tungsten 19 300, osmium 22 600).</summary>
    public const double DensityMax = 25000;
    /// <summary>An area load below this (N/m²) is 0.05 kN/m²: less than the lightest cladding.</summary>
    public const double AreaLoadMin = 50;
    /// <summary>An area load above this (N/m²) is 100 kN/m²: 10 m of water on the roof.</summary>
    public const double AreaLoadMax = 1e5;
    /// <summary>A bending strain limit at or above this is 5 %: beyond every lath material (timber 0.5 %, steel 0.2 %, GFRP 0.8 … 1.5 %).</summary>
    public const double StrainMax = 0.05;
    /// <summary>A bending strain limit below this (0.01 %) fails every lath that is not straight.</summary>
    public const double StrainMin = 1e-4;

    /// <summary>A stress in pascals written with a prefix: "11 GPa", "20 MPa", "350 kPa", "20 Pa".</summary>
    public static string FormatStress(double pa)
    {
        double a = Math.Abs(pa);
        if (a >= 1e9) return Format(pa / 1e9) + " GPa";
        if (a >= 1e6) return Format(pa / 1e6) + " MPa";
        if (a >= 1e3) return Format(pa / 1e3) + " kPa";
        return Format(pa) + " Pa";
    }

    /// <summary>
    /// A number as one would type it into a Grasshopper panel: plain below
    /// 100 000, otherwise mantissa and a power of a thousand ("11e9", "20e6", "1.5e6").
    /// </summary>
    public static string FormatEntry(double v)
    {
        double a = Math.Abs(v);
        if (!(a >= 1e5) || double.IsInfinity(a)) return Format(v);
        int exp = 3 * (int)Math.Floor(Math.Log10(a) / 3);
        double mantissa = RoundSignificant(v / Math.Pow(10, exp), 3);
        if (Math.Abs(mantissa) >= 1000) { mantissa /= 1000; exp += 3; }
        return Format(mantissa) + "e" + exp.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>A small ratio with two significant digits, plain down to 1e-4, "1.8e-5" below.</summary>
    public static string FormatRatio(double r)
    {
        double v = RoundSignificant(r, 2);
        if (Math.Abs(v) >= 1e-4) return Format(v);
        return v.ToString("0.#e-0", System.Globalization.CultureInfo.CurrentCulture);
    }

    /// <summary>
    /// The material and load inputs of Gridshell Analysis: E and Allowable in
    /// pascals, Density in kg/m³ (0 = none), AreaLoad in N/m² (0 = none).
    /// </summary>
    public static List<Advice> Material(double e, double allowable, double density, double areaLoad)
    {
        var list = new List<Advice>();
        var em = Modulus("E", e);
        var sm = Strength("Allowable", allowable);
        if (em != null) list.Add(em);
        if (sm != null) list.Add(sm);
        if (em == null && sm == null)
        {
            var r = FailureStrain(e, allowable);
            if (r != null) list.Add(r);
        }
        var d = Density(density);
        if (d != null) list.Add(d);
        var q = AreaLoad(areaLoad);
        if (q != null) list.Add(q);
        return list;
    }

    /// <summary>
    /// The material inputs of Net Kinetics: Modulus in pascals, MaxStrain as a
    /// ratio (0.005 = 0.5 %), Density in kg/m³ (0 = none).
    /// </summary>
    public static List<Advice> KineticMaterial(double modulus, double maxStrain, double density)
    {
        var list = new List<Advice>();
        var em = Modulus("Modulus", modulus);
        if (em != null) list.Add(em);
        var s = Strain("MaxStrain", maxStrain);
        if (s != null) list.Add(s);
        var d = Density(density);
        if (d != null) list.Add(d);
        return list;
    }

    /// <summary>Young's modulus (Pa) against 0.1 … 1 200 GPa; names the GPa / N/mm² reading that fits.</summary>
    public static Advice? Modulus(string name, double e)
    {
        if (!(e > 0) || double.IsInfinity(e)) return null;
        if (e < ModulusMin)
        {
            // 11 → 11 GPa; 11 000 → 11 000 N/mm² = 11 GPa
            double factor = e < 1e3 ? 1e9 : 1e6;
            string typed = factor == 1e9 ? "gigapascals" : "N/mm² (MPa)";
            double fix = e * factor;
            return new Advice("modulus-in-another-unit", AdviceLevel.Warning,
                $"{name} {Format(e)} reads as {FormatStress(e)}, far below any solid a lath is made of " +
                "(timber 7 to 24 GPa, EN 338; GFRP 23 GPa and more, EN 13706; steel 210 GPa), so the deflections come out " +
                $"{FormatEntry(RoundSignificant(ModulusMin / e, 1))} times too large and the stresses lose all meaning. " +
                $"{name} is in pascals: if you typed {typed}, enter {FormatEntry(fix)} ({FormatStress(fix)}).",
                e, ModulusMin, fix);
        }
        if (e > ModulusMax)
        {
            double fix = e / 1e3;
            bool fits = fix >= ModulusMin && fix <= ModulusMax;
            return new Advice("modulus-beyond-materials", AdviceLevel.Warning,
                $"{name} {FormatEntry(e)} reads as {FormatStress(e)}: no material is stiffer than about 1 000 GPa (diamond), " +
                "and steel is 210 GPa, so the laths will not deflect at all. " +
                (fits ? $"If the value carries one factor of 1 000 too many (kPa, or N/mm² already multiplied by 1e6), enter {FormatEntry(fix)} ({FormatStress(fix)})."
                      : $"{name} is in pascals; check the value against the data sheet."),
                e, ModulusMax, fits ? fix : double.NaN);
        }
        return null;
    }

    /// <summary>Allowable stress (Pa) against 0.1 MPa … 5 GPa; names the MPa reading that fits.</summary>
    public static Advice? Strength(string name, double stress)
    {
        if (!(stress > 0) || double.IsInfinity(stress)) return null;
        if (stress < StrengthMin)
        {
            // 20 → 20 MPa; 0.02 → 0.02 GPa; 20 000 → 20 000 kPa
            double factor = stress < 1 ? 1e9 : stress < 1e3 ? 1e6 : 1e3;
            string typed = factor == 1e9 ? "gigapascals" : factor == 1e6 ? "N/mm² (MPa)" : "kilopascals";
            double fix = stress * factor;
            return new Advice("strength-in-another-unit", AdviceLevel.Warning,
                $"{name} {Format(stress)} reads as {FormatStress(stress)}: a lath may carry that much stress under its own weight " +
                "(timber bends to 14 to 80 N/mm², EN 338; steel yields at 235 N/mm² and more), so every utilization comes out " +
                $"about {FormatEntry(RoundSignificant(StrengthMin / stress, 1))} times too high. " +
                $"{name} is in pascals: if you typed {typed}, enter {FormatEntry(fix)} ({FormatStress(fix)}).",
                stress, StrengthMin, fix);
        }
        if (stress > StrengthMax)
        {
            double fix = stress / 1e3;
            bool fits = fix >= StrengthMin && fix <= StrengthMax;
            return new Advice("strength-beyond-materials", AdviceLevel.Warning,
                $"{name} {FormatEntry(stress)} reads as {FormatStress(stress)}: no bulk material carries more than about 3 GPa " +
                "(carbon-fibre strips), timber 14 to 80 MPa and steel 235 to 460 MPa, so nothing will ever read as utilised. " +
                (fits ? $"If the value carries one factor of 1 000 too many (N/mm² already multiplied by 1e6), enter {FormatEntry(fix)} ({FormatStress(fix)})."
                      : $"{name} is in pascals; check the value against the data sheet."),
                stress, StrengthMax, fits ? fix : double.NaN);
        }
        return null;
    }

    /// <summary>
    /// Allowable / E, the strain at which the material is said to fail, against
    /// 1e-4 … 0.1 when both inputs are in range on their own: one of the two is
    /// then in another unit, and the message says which pairs would fit.
    /// </summary>
    public static Advice? FailureStrain(double e, double allowable)
    {
        if (!(e > 0) || !(allowable > 0)) return null;
        double ratio = allowable / e;
        if (ratio >= FailureStrainMin && ratio <= FailureStrainMax) return null;
        bool low = ratio < FailureStrainMin;
        // The 1 000-fold correction that brings the ratio into the window
        double eFix = low ? e / 1e3 : e * 1e3, sFix = low ? allowable * 1e3 : allowable / 1e3;
        bool eFits = eFix >= ModulusMin && eFix <= ModulusMax && (allowable / eFix) >= FailureStrainMin && (allowable / eFix) <= FailureStrainMax;
        bool sFits = sFix >= StrengthMin && sFix <= StrengthMax && (sFix / e) >= FailureStrainMin && (sFix / e) <= FailureStrainMax;
        string fix = eFits && sFits ? $"enter either E {FormatEntry(eFix)} ({FormatStress(eFix)}) with this Allowable, or Allowable {FormatEntry(sFix)} ({FormatStress(sFix)}) with this E"
            : eFits ? $"enter E {FormatEntry(eFix)} ({FormatStress(eFix)}) with this Allowable"
            : sFits ? $"enter Allowable {FormatEntry(sFix)} ({FormatStress(sFix)}) with this E"
            : "check both against the data sheet";
        return new Advice("failure-strain-off", AdviceLevel.Warning,
            $"Allowable / E = {FormatRatio(ratio)} is the strain at which the laths are said to fail; " +
            "materials fail between 1e-4 and 0.1 (steel 0.0011, timber C24 0.0022, GFRP 0.01), so with " +
            $"E {FormatStress(e)} and Allowable {FormatStress(allowable)} one of the two is in another unit: {fix}.",
            ratio, low ? FailureStrainMin : FailureStrainMax, sFits ? sFix : eFits ? eFix : double.NaN);
    }

    /// <summary>Density (kg/m³, 0 = none) against 10 … 25 000; names the t/m³ or g/cm³ reading.</summary>
    public static Advice? Density(double density)
    {
        if (!(density > 0) || double.IsInfinity(density)) return null;
        if (density < DensityMin)
        {
            double fix = density * 1e3;
            return new Advice("density-in-another-unit", AdviceLevel.Warning,
                $"Density {Format(density)} reads as {Format(density)} kg/m³, lighter than air (1.2), so the self-weight all but vanishes " +
                "(timber is 350 to 1 080 kg/m³, EN 338; steel 7 850). Density is in kg/m³: if you typed t/m³ or g/cm³, " +
                $"enter {FormatEntry(fix)}.",
                density, DensityMin, fix);
        }
        if (density > DensityMax)
        {
            double fix = density / 1e3;
            bool fits = fix >= DensityMin && fix <= DensityMax;
            return new Advice("density-beyond-materials", AdviceLevel.Warning,
                $"Density {FormatEntry(density)} kg/m³ is heavier than any metal (tungsten 19 300), so the self-weight swamps every other load. " +
                (fits ? $"If the value is in g/m³ or carries a factor of 1 000, enter {FormatEntry(fix)}." : "Density is in kg/m³; check the value."),
                density, DensityMax, fits ? fix : double.NaN);
        }
        return null;
    }

    /// <summary>Area load (N/m², 0 = none) against 50 … 100 000; names the kN/m² reading.</summary>
    public static Advice? AreaLoad(double q)
    {
        if (!(q > 0) || double.IsInfinity(q)) return null;
        if (q < AreaLoadMin)
        {
            double fix = q * 1e3;
            return new Advice("area-load-in-another-unit", AdviceLevel.Remark,
                $"AreaLoad {Format(q)} reads as {Format(q)} N/m² = {Format(q / 1e3)} kN/m², less than the lightest cladding " +
                "(0.1 kN/m²; snow 0.5 to 5 kN/m², EN 1991-1-3), so it hardly loads the net. AreaLoad is in N/m²: " +
                $"if you typed kN/m², enter {FormatEntry(fix)}.",
                q, AreaLoadMin, fix);
        }
        if (q > AreaLoadMax)
        {
            double fix = q / 1e3;
            bool fits = fix >= AreaLoadMin && fix <= AreaLoadMax;
            return new Advice("area-load-beyond-loads", AdviceLevel.Warning,
                $"AreaLoad {FormatEntry(q)} N/m² is {Format(q / 1e3)} kN/m², ten metres of water on the roof (snow and cladding stay below 10 kN/m²). " +
                (fits ? $"If the value is in N/m² with a factor of 1 000 too many, enter {FormatEntry(fix)}." : "AreaLoad is in N/m²; check the value."),
                q, AreaLoadMax, fits ? fix : double.NaN);
        }
        return null;
    }

    /// <summary>A bending strain limit (ratio) against 1e-4 … 0.05; names the percent or per-mille reading.</summary>
    public static Advice? Strain(string name, double strain)
    {
        if (!(strain > 0) || double.IsInfinity(strain)) return null;
        if (strain >= StrainMax)
        {
            // 0.5 → 0.5 % → 0.005; 5 → 5 ‰ → 0.005
            double factor = strain < 5 ? 1e-2 : 1e-3;
            string typed = factor == 1e-2 ? "percent" : "per mille";
            double fix = strain * factor;
            return new Advice("strain-in-another-unit", AdviceLevel.Warning,
                $"{name} {Format(strain)} is a bending strain of {Format(100 * strain)} %: no lath material bends that far and springs back " +
                "(timber 0.5 %, steel 0.2 %, GFRP 0.8 to 1.5 %), so no lath will ever read as utilised. " +
                $"{name} is a ratio: if you typed {typed}, enter {Format(fix)}.",
                strain, StrainMax, fix);
        }
        if (strain < StrainMin)
        {
            return new Advice("strain-tiny", AdviceLevel.Remark,
                $"{name} {FormatEntry(strain)} is a bending strain of {Format(100 * strain)} %, a hundredth of what timber takes (0.5 %), " +
                "so every lath that is not straight reads as over-utilised. Check the value: 0.005 is timber, 0.002 steel, 0.008 GFRP.",
                strain, StrainMin, double.NaN);
        }
        return null;
    }
}
