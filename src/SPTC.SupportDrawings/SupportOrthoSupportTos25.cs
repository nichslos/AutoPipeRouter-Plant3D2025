using System;
using System.Collections.Generic;
using System.Globalization;

namespace SPTC.SupportDrawings;

/// <summary>
/// REV22.25: Piping Support Top-of-Steel elevation. Never substitute
/// Top of Pipe, Bottom of Pipe or COP Elevation for Top of Steel.
/// If there is no explicit support TOS property, the support's maximum
/// WCS Z is at most a geometric approximation, NOT verified steel TOS.
/// </summary>
public static class SupportOrthoSupportTos25
{
    public sealed record Observation(string Text,string Source);

    public static Observation Evaluate(
        IReadOnlyDictionary<string,string> properties,double geometricTopMm)
    {
        string? declared=SupportOrthoSupportLabel24.Get(properties,
            "TopOfSteel","Top of Steel","TopOfSteelElevation",
            "Top of Steel Elevation","TOSElevation","TOS Elevation");
        if(SupportOrthoVerticalMember18.TryParseTos(declared,out double exact))
            return new Observation(
                SupportOrthoVerticalMember18.FormatElevation(exact),
                "SUPPORT_EXPLICIT_TOP_OF_STEEL");

        if(double.IsFinite(geometricTopMm))
            return new Observation(
                SupportOrthoVerticalMember18.FormatElevation(geometricTopMm),
                "SUPPORT_GEOMETRIC_TOP_APPROX");

        return new Observation("","SUPPORT_TOS_UNAVAILABLE");
    }

    public static string Display(Observation observation) =>
        observation.Source=="SUPPORT_EXPLICIT_TOP_OF_STEEL"?
            "TOS EL. "+observation.Text:
        observation.Source=="SUPPORT_GEOMETRIC_TOP_APPROX"?
            "TOS ~ "+observation.Text:
            "TOS: N/A";
}
