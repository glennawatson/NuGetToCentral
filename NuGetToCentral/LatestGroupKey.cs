namespace NuGetToCentral;

internal readonly record struct LatestGroupKey(string Condition, string VersionPropertyName)
{
    public static implicit operator (string Condition, string VersionPropertyName)(LatestGroupKey value)
    {
        return (value.Condition, value.VersionPropertyName);
    }

    public static implicit operator LatestGroupKey((string Condition, string VersionPropertyName) value)
    {
        return new(value.Condition, value.VersionPropertyName);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Condition, StringComparer.OrdinalIgnoreCase.GetHashCode(VersionPropertyName));
    }

    public bool Equals(LatestGroupKey other)
    {
        return Condition.Equals(other.Condition) && StringComparer.OrdinalIgnoreCase.Equals(VersionPropertyName, other.VersionPropertyName);
    }
}