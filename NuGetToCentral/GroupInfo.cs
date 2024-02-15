namespace NuGetToCentral;

public record GroupInfo(string GroupName, string VersionPropertyName, bool AlreadyDeclared, bool IsPrefix)
{
    public override int GetHashCode()
    {
        return HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(GroupName), VersionPropertyName, AlreadyDeclared, IsPrefix);
    }

    public virtual bool Equals(GroupInfo? other)
    {
        if (other is null)
        {
            return false;
        }
        
        return StringComparer.OrdinalIgnoreCase.Equals(GroupName, other.GroupName) &&
            VersionPropertyName.Equals(other.VersionPropertyName) &&
            AlreadyDeclared == other.AlreadyDeclared &&
            IsPrefix == other.IsPrefix;
    }
}
