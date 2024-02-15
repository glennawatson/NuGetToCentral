using NuGet.Common;
using NuGet.LibraryModel;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using Polly.Retry;
using Polly;

namespace NuGetToCentral;

internal static class NuGetHelper
{
    private static readonly MetadataResource MetadataResolve;
    private static readonly FindPackageByIdResource FindPackageByIdService;
    private static readonly SourceCacheContext CacheContext = new();
    private static readonly ILogger Logger = NullLogger.Instance;
    private static readonly Dictionary<LibraryRange, Lazy<ValueTask<NuGetVersion>>> LibraryRangeCache = [];
    private static readonly ResiliencePipeline Pipeline;

    static NuGetHelper()
    {
        var repository = Repository.Factory.GetCoreV3("https://api.nuget.org/v3/index.json");

        var retryOptions = new RetryStrategyOptions
        {
            ShouldHandle = new PredicateBuilder().Handle<HttpRequestException>(),
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = true,  // Adds a random factor to the delay
            MaxRetryAttempts = 4,
            Delay = TimeSpan.FromSeconds(3),
        };

        Pipeline = new ResiliencePipelineBuilder().AddRetry(retryOptions).Build();

        MetadataResolve = repository.GetResource<MetadataResource>();
        FindPackageByIdService = repository.GetResource<FindPackageByIdResource>();
    }

    public static async ValueTask<PackageVersion> GetLatest(this LibraryRange value)
    {
        var returnValue = await LibraryRangeCache.GetValueOrDefault(
            value,
            new(() =>
                Pipeline.ExecuteAsync(async token =>
                {
                    var versions = await FindPackageByIdService.GetAllVersionsAsync(value.Name, CacheContext, Logger, token);
                    var bestPackageVersion = versions?.FindBestMatch(value.VersionRange, version => version);
                    return bestPackageVersion ?? await MetadataResolve.GetLatestVersion(value.Name, false, false, CacheContext, Logger, token);
                }),
                LazyThreadSafetyMode.PublicationOnly)).Value;

        return new(value.Name, returnValue);
    }
}
