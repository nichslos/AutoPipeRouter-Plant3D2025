using System;

namespace SPTC.SupportDrawings;

/// <summary>
/// REV22.5 purely mathematical WCS camera/up/twist checks. No AutoCAD dependencies.
/// A camera's ViewDirection can be correct while a 180-degree viewport TwistAngle
/// rotates the isometric drawing upside down in the paper viewport.
/// </summary>
public static class NativeOrthoIsoTwistGuard
{
    public static bool IsProjectedWorldUp(
        double dx,double dy,double dz,double ux,double uy,double uz)
    {
        double dLength=Math.Sqrt(dx*dx+dy*dy+dz*dz);
        double uLength=Math.Sqrt(ux*ux+uy*uy+uz*uz);
        if(!double.IsFinite(dLength)||!double.IsFinite(uLength)||
            dLength<1e-10||uLength<1e-10)
            return false;
        dx/=dLength;dy/=dLength;dz/=dLength;
        ux/=uLength;uy/=uLength;uz/=uLength;
        // Orthogonally project WCS +Z onto the view plane.
        double px=-dz*dx,py=-dz*dy,pz=1-dz*dz;
        double pLength=Math.Sqrt(px*px+py*py+pz*pz);
        if(pLength<1e-8||!double.IsFinite(pLength))return false;
        px/=pLength;py/=pLength;pz/=pLength;
        return px*ux+py*uy+pz*uz > 0.999999;
    }

    public static bool IsZeroTwist(double twistRadians)
    {
        if(!double.IsFinite(twistRadians))return false;
        return Math.Abs(Math.IEEERemainder(twistRadians,2*Math.PI))<1e-6;
    }

    public static bool IsHalfTurn(double twistRadians)
    {
        if(!double.IsFinite(twistRadians))return false;
        return Math.Abs(Math.Abs(Math.IEEERemainder(
            twistRadians,2*Math.PI))-Math.PI)<1e-6;
    }
}
