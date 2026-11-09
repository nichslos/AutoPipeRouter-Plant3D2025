using System;

namespace SPTC.SupportDrawings;

/// <summary>
/// REV22.7 experimental iso viewport reframing. Changing paper viewport
/// TwistAngle from 180deg to 0deg rotates screen-space axes 180deg.
/// To preserve the same visible model location, rotate the 2D view center
/// by the same half-turn (negate both coordinates). This is not a supported
/// Ortho metadata setter; it affects only freshly generated test viewports.
/// </summary>
public static class NativeOrthoIsoFramingGuard
{
    public sealed record ViewState(double CenterX,double CenterY,double TwistRadians);
    public sealed record Adjustment(double CenterX,double CenterY,double TwistRadians,
        bool AppliedHalfTurn);

    public static Adjustment NormalizeHalfTurn(ViewState before)
    {
        if(before==null || !double.IsFinite(before.CenterX) ||
            !double.IsFinite(before.CenterY) ||
            !double.IsFinite(before.TwistRadians))
            throw new ArgumentException("REV22.7: Nonfinite native viewport view center/twist.");
        if(NativeOrthoIsoTwistGuard.IsHalfTurn(before.TwistRadians))
            return new Adjustment(-before.CenterX,-before.CenterY,0,true);
        if(NativeOrthoIsoTwistGuard.IsZeroTwist(before.TwistRadians))
            return new Adjustment(before.CenterX,before.CenterY,0,false);
        throw new InvalidOperationException(
            "REV22.7: Native iso twist not a half-turn or zero; refuse arbitrary reframing.");
    }

    public static bool Matches(Adjustment expected,ViewState actual)
    {
        if(expected==null||actual==null)return false;
        double tolX=Math.Max(1e-6,Math.Abs(expected.CenterX)*1e-8);
        double tolY=Math.Max(1e-6,Math.Abs(expected.CenterY)*1e-8);
        return double.IsFinite(actual.CenterX)&&double.IsFinite(actual.CenterY)&&
            Math.Abs(expected.CenterX-actual.CenterX)<=tolX&&
            Math.Abs(expected.CenterY-actual.CenterY)<=tolY&&
            NativeOrthoIsoTwistGuard.IsZeroTwist(actual.TwistRadians);
    }
}
