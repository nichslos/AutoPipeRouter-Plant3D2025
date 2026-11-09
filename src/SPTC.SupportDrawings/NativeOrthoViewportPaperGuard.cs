using System;
using System.Collections.Generic;

namespace SPTC.SupportDrawings;

/// <summary>
/// REV22.3: read-only validation of the ACTUAL paper-space viewport generated
/// by Autodesk's internal API. A planned rectangle alone is never proof that
/// native ortho placement fits. Pure geometry; safe to smoke-test outside Plant.
/// </summary>
public static class NativeOrthoViewportPaperGuard
{
    public sealed record Frame(string Name,double X,double Y,double W,double H,
        double Scale,double ViewHeight);

    public static string Validate(Frame planned,Frame actual,
        double paperWidth,double paperHeight,IReadOnlyList<Frame> previouslyCreated)
    {
        if (planned==null||actual==null||previouslyCreated==null)
            throw new ArgumentNullException(nameof(actual));
        if (!FinitePositive(paperWidth)||!FinitePositive(paperHeight)||
            !FinitePositive(planned.W)||!FinitePositive(planned.H)||
            !FinitePositive(planned.Scale))
            return "INVALID_PLAN_OR_SHEET";
        if (!double.IsFinite(actual.X)||!double.IsFinite(actual.Y)||
            !FinitePositive(actual.W)||!FinitePositive(actual.H)||
            !FinitePositive(actual.Scale)||!FinitePositive(actual.ViewHeight))
            return "NATIVE_VIEWPORT_INVALID_DIMENSIONS";
        if (!Near(actual.X,planned.X)||!Near(actual.Y,planned.Y))
            return "NATIVE_CENTER_CHANGED";
        if (!Near(actual.W,planned.W)||!Near(actual.H,planned.H))
            return "NATIVE_FRAME_SIZE_CHANGED";
        if (Math.Abs(actual.Scale-planned.Scale)>Math.Max(1e-6,planned.Scale*0.005))
            return "NATIVE_VIEWPORT_SCALE_CHANGED";
        const double tolerance=0.1; // measured in paper-space drawing units (mm)
        if (actual.X-actual.W/2 < -tolerance||
            actual.X+actual.W/2 > paperWidth*.78+tolerance||
            actual.Y-actual.H/2 < paperHeight*.12-tolerance||
            actual.Y+actual.H/2 > paperHeight*.94+tolerance)
            return "NATIVE_VIEWPORT_OFF_SAFE_SHEET";
        foreach(Frame other in previouslyCreated)
        {
            if (actual.X-actual.W/2<other.X+other.W/2-1e-6 &&
                other.X-other.W/2<actual.X+actual.W/2-1e-6 &&
                actual.Y-actual.H/2<other.Y+other.H/2-1e-6 &&
                other.Y-other.H/2<actual.Y+actual.H/2-1e-6)
                return "NATIVE_VIEWPORT_OVERLAPS_"+other.Name;
        }
        return "PASS";
    }

    private static bool Near(double actual,double expected) =>
        Math.Abs(actual-expected)<=Math.Max(.25,Math.Abs(expected)*.005);
    private static bool FinitePositive(double x)=>double.IsFinite(x)&&x>0;
}
