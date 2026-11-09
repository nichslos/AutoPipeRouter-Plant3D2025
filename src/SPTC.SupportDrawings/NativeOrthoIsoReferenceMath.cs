using System;

namespace SPTC.SupportDrawings;

/// <summary>
/// REV22.6 isolated reference-vs-generated camera and roll comparison.
/// Both normalized camera directions and twist angles are required:
/// a correct camera quadrant alone does not prove screen orientation.
/// This compares viewport properties only, not generated linework.
/// </summary>
public static class NativeOrthoIsoReferenceMath
{
    public sealed record Pose(string Name,string Handle,
        double Dx,double Dy,double Dz,double TwistRad,double CustomScale);
    public sealed record Comparison(double CameraSeparationDegrees,
        double RollDifferenceDegrees,bool CameraMatches,bool RollMatches,
        bool ScaleMatches)
    {
        public bool OrientationMatches=>CameraMatches&&RollMatches;
    }

    public static Comparison Compare(Pose reference,Pose generated)
    {
        if(!string.Equals(reference.Name,generated.Name,StringComparison.Ordinal))
            throw new ArgumentException("Named view labels must match.");
        double l1=Math.Sqrt(reference.Dx*reference.Dx+
            reference.Dy*reference.Dy+reference.Dz*reference.Dz);
        double l2=Math.Sqrt(generated.Dx*generated.Dx+
            generated.Dy*generated.Dy+generated.Dz*generated.Dz);
        if(!double.IsFinite(l1)||!double.IsFinite(l2)||l1<1e-12||l2<1e-12||
            !double.IsFinite(reference.TwistRad)||
            !double.IsFinite(generated.TwistRad))
            throw new ArgumentException("Invalid or nonfinite camera data.");
        double dot=(reference.Dx*generated.Dx+reference.Dy*generated.Dy+
            reference.Dz*generated.Dz)/(l1*l2);
        dot=Math.Clamp(dot,-1,1);
        double cameraAngle=Math.Acos(dot)*180/Math.PI;
        // TwistAngle of 360deg and 0deg are equivalent.
        // Compare twist independently from orientation vectors.
        double roll=Math.Abs(Math.IEEERemainder(
            generated.TwistRad-reference.TwistRad,2*Math.PI))*180/Math.PI;
        bool scale=double.IsFinite(reference.CustomScale)&&
            double.IsFinite(generated.CustomScale)&&
            reference.CustomScale>0&&generated.CustomScale>0&&
            Math.Abs(reference.CustomScale-generated.CustomScale)<=
            Math.Max(1e-6,reference.CustomScale*0.005);
        return new Comparison(cameraAngle,roll,
            cameraAngle<=0.1,roll<=0.5,scale);
    }
}
