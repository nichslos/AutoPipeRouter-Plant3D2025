using System;

namespace SPTC.SupportDrawings;

/// <summary>
/// REV22.10 experimental six-axial correction. A half-turn of a paper
/// viewport also requires rotating ViewCenter in its display plane, so
/// otherwise-correct geometry is not moved outside the viewport.
/// No native Autodesk objects or project metadata are touched here.
/// </summary>
public static class NativeOrthoAxialHalfTurnGuard
{
    public static NativeOrthoIsoFramingGuard.Adjustment Flip(
        NativeOrthoIsoFramingGuard.ViewState before)
    {
        if(before==null ||
            !double.IsFinite(before.CenterX) ||
            !double.IsFinite(before.CenterY) ||
            !double.IsFinite(before.TwistRadians))
            throw new ArgumentException("REV22.10 nonfinite native axial viewport state.");

        // Visual evidence indicates initial six axial images are rotated
        // by 180deg relative to native Update View, but the actual starting
        // twist is not known from REV22.9 logs. Toggle by a half-turn safely
        // whether native generation returned zero or pi radians.
        if(NativeOrthoIsoTwistGuard.IsHalfTurn(before.TwistRadians))
            return new NativeOrthoIsoFramingGuard.Adjustment(
                -before.CenterX,-before.CenterY,0,true);
        if(NativeOrthoIsoTwistGuard.IsZeroTwist(before.TwistRadians))
            return new NativeOrthoIsoFramingGuard.Adjustment(
                -before.CenterX,-before.CenterY,Math.PI,true);

        throw new InvalidOperationException(
            "REV22.10 native axial twist is neither zero nor 180 degrees; "+
            "refuse to guess the corrective rotation.");
    }

    public static bool Matches(NativeOrthoIsoFramingGuard.Adjustment expected,
        NativeOrthoIsoFramingGuard.ViewState observed)
    {
        if(expected==null||observed==null||
           !double.IsFinite(observed.CenterX)||!double.IsFinite(observed.CenterY)||
           !double.IsFinite(observed.TwistRadians))return false;
        double tolX=Math.Max(1e-6,Math.Abs(expected.CenterX)*1e-8);
        double tolY=Math.Max(1e-6,Math.Abs(expected.CenterY)*1e-8);
        return Math.Abs(observed.CenterX-expected.CenterX)<=tolX&&
               Math.Abs(observed.CenterY-expected.CenterY)<=tolY&&
               Math.Abs(Math.IEEERemainder(
                   observed.TwistRadians-expected.TwistRadians,2*Math.PI))<1e-6;
    }
}
