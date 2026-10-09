namespace Smt.Core;
public static class AdmFilter
{
    public static bool Matches(double? adm,double? threshold) => threshold is 4 or 5 && adm is {} value && double.IsFinite(value) && value>=1 && value<=6 && value<threshold.Value;
}
