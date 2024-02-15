using Microsoft.Build.Construction;

namespace NuGetToCentral;

public record PackageProjectItemDetails(PackageVersion PackageVersion, ProjectItemElement ItemElement, GroupInfo? GroupInfo) :
    PackageProjectDetails(PackageVersion, GroupInfo);
