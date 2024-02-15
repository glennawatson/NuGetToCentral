using Microsoft.Build.Construction;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Exceptions;
using Microsoft.Build.Locator;

using NuGet.LibraryModel;
using NuGet.Versioning;

using System.Text.RegularExpressions;

namespace NuGetToCentral;

public static partial class Program
{
    private const string Folder = @"C:\source\gh\reactiveui\Splat\src";

    private static readonly GroupInfo[] Groups =
    [
        new("Microsoft.Maui", "MauiVersion", true, true),
        new("xunit", "XUnitVersion", false, false),
        new("xunit.runner.console", "XUnitVersion", false, false),
        new("Xamarin.Android.Support", "XamarinAndroidSupportVersion", false, true),
        new("splat", "SplatVersion", false, true),
        new("avalonia", "AvaloniaVersion", false, true),
        new("reactiveui", "ReactiveUIVersion", false, true),
        new("fody", "FodyVersion", false, false),
        new("FodyHelpers", "FodyVersion", false, false),
        new("FodyPackaging", "FodyVersion", false, false),
    ];

    private static readonly HashSet<string> SkipLatest = new(StringComparer.OrdinalIgnoreCase)
    {
        "Microsoft.Build",
    };

    private static readonly HashSet<string> IgnoreGroupConditions = new(StringComparer.OrdinalIgnoreCase)
    {
        "$(IsTestProject)",
        "'$(IsTestProject)' != 'true'",
        "$(IsTestProject) or $(MSBuildProjectName.Contains('TestRunner'))",
    };

    private static readonly Dictionary<string, string> PackageConditionOverride = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Microsoft.WindowsAppSDK"] = "'$(UseMaui)' != 'true'"
    };

    public static async Task Main()
    {
        MSBuildLocator.RegisterDefaults();
        await ProcessSolution();
    }

    private static async Task ProcessSolution()
    {
        var slnList = GetSolutionData();

        var projects = new List<ProjectRootElement>();

        foreach (var (directory, solution) in slnList)
        {
            // condition -> packageName -> NuGetVersion
            var packageDictionary = new Dictionary<string, Dictionary<string, PackageProjectDetails>>();

            var directoryBuildPropsFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var projectRef in solution.ProjectsInOrder.Where(x => x.ProjectType == SolutionProjectType.KnownToBeMSBuildFormat))
            {
                var project = await ProcessMsBuildFile(packageDictionary, projectRef.AbsolutePath);

                if (project is not null)
                {
                    projects.Add(project);
                }

                directoryBuildPropsFiles.UnionWith(DiscoverDirectoryBuildProps(Path.GetDirectoryName(projectRef.AbsolutePath)!));
            }

            foreach (var directoryBuildProps in directoryBuildPropsFiles)
            {
                var project = await ProcessMsBuildFile(packageDictionary, directoryBuildProps);

                if (project is not null)
                {
                    projects.Add(project);
                }
            }

            var packageFileName = Path.Combine(directory.FullName, "Directory.Packages.props");

            var packageFile = GeneratePackagesRoot(packageFileName, packageDictionary);

            packageFile.Save(packageFileName);
        }

        foreach (var file in projects)
        {
            file.Save();
        }
    }

    private static List<(DirectoryInfo Folder, SolutionFile Solution)> GetSolutionData()
    {
        var slnFileNames = Directory.EnumerateFiles(Folder, "*.sln", new EnumerationOptions { RecurseSubdirectories = true });

        return slnFileNames
            .Select(slnFileName =>
                new { slnFileName, slnFolder = new DirectoryInfo(Path.GetDirectoryName(slnFileName)!) })
            .Select(t => new { t, solution = SolutionFile.Parse(t.slnFileName) })
            .Select(t => (t.t.slnFolder, t.solution))
            .ToList();
    }

    private static async Task<ProjectRootElement?> ProcessMsBuildFile(Dictionary<string, Dictionary<string, PackageProjectDetails>> packageDictionary, string absolutePath)
    {
        try
        {
            var project = ProjectRootElement.Open(absolutePath, ProjectCollection.GlobalProjectCollection, false);

            var elementsToConsider = new List<(ProjectItemGroupElement Container, string? Condition)>(project.ItemGroups.Select(x => (x, x.Condition?.Trim())));

            foreach (var chooseBlock in project.ChooseElements)
            {
                foreach (var whenBlock in chooseBlock.WhenElements)
                {
                    elementsToConsider.AddRange(whenBlock.ItemGroups.Select(x => (x, whenBlock.Condition?.Trim())));
                }
            }

            foreach (var (itemGroup, itemGroupConditionInput) in elementsToConsider)
            {
                var itemGroupCondition = itemGroupConditionInput?.Trim();

                if (string.IsNullOrWhiteSpace(itemGroupCondition) || IgnoreGroupConditions.Contains(itemGroupCondition))
                {
                    itemGroupCondition = string.Empty;
                }

                var packages = await GetLatestPackagesForProject(itemGroup, project);

                UpdatePackageDictionary(itemGroupCondition, packages, packageDictionary);
            }

            return project;
        }
        catch (InvalidProjectFileException)
        {
            return null;
        }
    }

    private static async Task<PackageProjectItemDetails[]> GetLatestPackagesForProject(ProjectItemGroupElement projectItemGroup, ProjectRootElement project)
    {
        var packageReferences = projectItemGroup.Items
            .Where(x => x.ItemType == "PackageReference")
            .Select(packageReference =>
            {
                var name = packageReference.Include;
                var versionMetadata = packageReference.Metadata.FirstOrDefault(x => x.Name == "Version");

                VersionRange? version = null;
                if (versionMetadata is not null)
                {
                    if (!VersionRange.TryParse(versionMetadata.Value, out version))
                    {
                        // Attempt to resolve the value of the property if it's in the format $(PropertyName)
                        var match = PropertyValueRegex().Match(versionMetadata.Value);

                        if (match.Success)
                        {
                            var propertyName = match.Groups[1].Value;

                            // Attempt to get the property value from the current project
                            var propertyValue = project.Properties.FirstOrDefault(p => p.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))?.Value;

                            // Try parsing the version from the resolved property value
                            if (propertyValue == null || !VersionRange.TryParse(propertyValue, out version))
                            {
                                Console.WriteLine("Dont know how to parse version: " + propertyName);
                            }
                        }
                        else
                        {
                            Console.WriteLine("Dont know how to parse version: " + versionMetadata.Value);
                        }
                    }
                }

                var libraryRange = new LibraryRange(name, version, LibraryDependencyTarget.Package);
                return (packageReference, libraryRange);
            });

        var packages = await Task.WhenAll(packageReferences.Select(async x =>
        {
            var (packageReference, libraryRange) = x;
            var groupInfo = FindPackageGroup(packageReference.Include);

            var packageVersionInfo = SkipLatest.Contains(libraryRange.Name) && libraryRange.VersionRange.MinVersion is not null ?
                new(libraryRange.Name, libraryRange.VersionRange.MinVersion) :
                await libraryRange.GetLatest();

            return new PackageProjectItemDetails(packageVersionInfo, packageReference, groupInfo);
        }));

        return packages;
    }

    private static IEnumerable<string> DiscoverDirectoryBuildProps(string projectDirectory)
    {
        var currentDirectory = new DirectoryInfo(projectDirectory);
        while (currentDirectory.Parent is not null)
        {
            var directoryBuildPropsPath = Path.Combine(currentDirectory.FullName, "Directory.Build.props");
            if (File.Exists(directoryBuildPropsPath))
            {
                yield return directoryBuildPropsPath;
            }

            currentDirectory = currentDirectory.Parent;
        }
    }

    private static void UpdatePackageDictionary(string itemGroupCondition, IEnumerable<PackageProjectItemDetails> packages, Dictionary<string, Dictionary<string, PackageProjectDetails>> packageDictionary)
    {
        foreach (var packageNameGroup in packages.GroupBy(x => x.PackageVersion.Name, StringComparer.OrdinalIgnoreCase))
        {
            var count = packageNameGroup.Count();
            
            if (string.IsNullOrWhiteSpace(itemGroupCondition))
            {
                if (!PackageConditionOverride.TryGetValue(packageNameGroup.Key, out itemGroupCondition!))
                {
                    itemGroupCondition = string.Empty;
                }
            }

            foreach (var package in packageNameGroup)
            {
                var versionMetadata = package.ItemElement.Metadata.FirstOrDefault(x => x.Name == "Version");
                if (versionMetadata is not null)
                {
                    package.ItemElement.RemoveChild(versionMetadata);
                }

                AddEntryIfNewer(packageDictionary, count > 0 ? itemGroupCondition : string.Empty, package);
            }
        }
    }

    private static void AddEntryIfNewer(Dictionary<string, Dictionary<string, PackageProjectDetails>> packageDictionary, string groupCondition, PackageProjectDetails package)
    {
        if (!packageDictionary.TryGetValue(groupCondition, out var conditionDictionary))
        {
            conditionDictionary = new(StringComparer.OrdinalIgnoreCase);
            packageDictionary[groupCondition] = conditionDictionary;
        }

        if (conditionDictionary.TryGetValue(package.PackageVersion.Name, out var currentVersion))
        {
            if (currentVersion.PackageVersion.Version >= package.PackageVersion.Version)
            {
                return;
            }
        }

        conditionDictionary[package.PackageVersion.Name] = package;
    }

    private static ProjectRootElement GeneratePackagesRoot(string packageFileName, Dictionary<string, Dictionary<string, PackageProjectDetails>> packages)
    {
        var projectElement = File.Exists(packageFileName) ? ProjectRootElement.Open(packageFileName) : ProjectRootElement.Create(NewProjectFileOptions.None);
        projectElement ??= ProjectRootElement.Create(NewProjectFileOptions.None);
        
        projectElement.AddProperty("ManagePackageVersionsCentrally", "true");
        projectElement.AddProperty("CentralPackageTransitivePinningEnabled", "true");

        var emptyItemGroupConditionPackages = packages[string.Empty];

        // Initialize a dictionary to track the latest version for each group and condition
        var latestGroupVersions = new Dictionary<LatestGroupKey, NuGetVersion>();

        foreach (var (itemGroupCondition, value) in packages.OrderBy(x => x.Key).Where(x => x.Value.Count > 0))
        {
            var isNotEmptyItemGroup = !string.IsNullOrEmpty(itemGroupCondition);

            ProjectItemGroupElement? itemGroup = null;

            foreach (var (packageName, (packageVersion, groupInfo)) in value.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
            {
                if (isNotEmptyItemGroup && emptyItemGroupConditionPackages.TryGetValue(packageName, out var emptyPackageDetails) && emptyPackageDetails.PackageVersion == packageVersion)
                {
                    continue;
                }

                if (itemGroup is null)
                {
                    itemGroup = projectElement.AddItemGroup();
                    itemGroup.Condition = itemGroupCondition;
                }

                var versionString = packageVersion.Version.ToString();
                if (groupInfo is not null && !groupInfo.AlreadyDeclared)
                {
                    var key = (itemGroupCondition, groupInfo.VersionPropertyName);
                    if (!latestGroupVersions.TryGetValue(key, out var existing) || packageVersion.Version > existing)
                    {
                        latestGroupVersions[key] = packageVersion.Version;
                    }
                }

                var versionInfo = groupInfo is not null ? $"$({groupInfo.VersionPropertyName})" : versionString;

                var item = itemGroup.AddItem("PackageVersion", packageName);
                item.AddMetadata("Version", versionInfo, true);
            }
        }
        
        foreach (var latestGroup in latestGroupVersions.GroupBy(x => x.Key.Condition))
        {
            var condition = latestGroup.Key;
            var propertyGroup = projectElement.AddPropertyGroup();
            propertyGroup.Condition = !string.IsNullOrWhiteSpace(condition) ? condition : null;

            foreach (var latestGroupVersion in latestGroup.OrderBy(x => x.Key.VersionPropertyName))
            {
                var versionPropertyName = latestGroupVersion.Key.VersionPropertyName;
                var version = latestGroupVersion.Value.ToString();

                propertyGroup.AddProperty(versionPropertyName, version);
            }
        }

        return projectElement;
    }

    private static GroupInfo? FindPackageGroup(string packageName)
    {
        // Iterate over the defined groups to find a match
        return Groups.Select(group =>
            (
                Group: group,
                IsMatch: group.IsPrefix
                    ? packageName.StartsWith(group.GroupName, StringComparison.OrdinalIgnoreCase)
                    : string.Equals(packageName, group.GroupName, StringComparison.OrdinalIgnoreCase)
            ))
            .Where(t => t.IsMatch)
            .Select(t => t.Group).FirstOrDefault();
    }

    [GeneratedRegex(@"^\$\(([^)]+)\)$")]
    private static partial Regex PropertyValueRegex();
}
