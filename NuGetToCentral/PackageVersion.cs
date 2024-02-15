using NuGet.Versioning;

namespace NuGetToCentral;

public readonly record struct PackageVersion(string Name, NuGetVersion Version)
{
    public override int GetHashCode()
    {
        return HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(Name), Version);
    }

    public bool Equals(PackageVersion other)
    {
        return StringComparer.OrdinalIgnoreCase.Equals(Name, other.Name) &&
            Version.Equals(other.Version);
    }
}
