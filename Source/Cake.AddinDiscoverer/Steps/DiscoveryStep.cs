using NuGet.Common;
using NuGet.Protocol.Core.Types;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Cake.AddinDiscoverer.Steps
{
	internal class DiscoveryStep : IStep
	{
		public bool PreConditionIsMet(DiscoveryContext context) => true;

		public string GetDescription(DiscoveryContext context)
		{
			if (string.IsNullOrEmpty(context.Options.AddinName)) return "Search NuGet for all packages matching 'Cake.*'";
			else return $"Search NuGet for {context.Options.AddinName}";
		}

		public async Task ExecuteAsync(DiscoveryContext context)
		{
			var take = 50;
			var skip = 0;
			var searchTerm = "Cake";
			var filters = new SearchFilter(true)
			{
				IncludeDelisted = false,
				OrderBy = SearchOrderBy.Id
			};

			var addinPackages = new List<IPackageSearchMetadata>(take);

			//--------------------------------------------------
			// Get the metadata from NuGet.org
			if (!string.IsNullOrEmpty(context.Options.AddinName))
			{
				// Get metadata for one specific package
				var nugetPackageMetadataClient = context.NugetRepository.GetResource<PackageMetadataResource>();
				var searchMetadata = await nugetPackageMetadataClient.GetMetadataAsync(context.Options.AddinName, true, false, NullLogger.Instance, CancellationToken.None).ConfigureAwait(false);
				var mostRecentPackageMetadata = searchMetadata.OrderByDescending(p => p.Published).FirstOrDefault();
				if (mostRecentPackageMetadata != null)
				{
					addinPackages.Add(mostRecentPackageMetadata);
				}
			}
			else
			{
				var nugetSearchClient = context.NugetRepository.GetResource<PackageSearchResource>();

				// Search for all package matching the search term
				while (true)
				{
					var searchResult = await nugetSearchClient.SearchAsync(searchTerm, filters, skip, take, NullLogger.Instance, CancellationToken.None).ConfigureAwait(false);
					skip += take;

					if (!searchResult.Any())
					{
						break;
					}

					addinPackages.AddRange(searchResult.Where(r => r.Identity.Id.StartsWith("Cake.")));
				}
			}

			//--------------------------------------------------
			// Convert metadata from nuget into our own metadata
			context.Addins = addinPackages
				.Select(package =>
				{
					var packageOwners = package.Owners?
						.Split(',', StringSplitOptions.RemoveEmptyEntries)
						.Select(o => o.Trim())
						.ToArray() ?? Array.Empty<string>();

					var addinMetadata = new AddinMetadata()
					{
						AnalysisResult = new AddinAnalysisResult(),
						Maintainer = package.Authors,
						Description = package.Description,
						ProjectUrl = package.ProjectUrl,
						IconUrl = package.IconUrl,
						Name = package.Identity.Id,
						NuGetPackageUrl = new Uri($"https://www.nuget.org/packages/{package.Identity.Id}/"),
						NuGetPackageOwners = packageOwners,
						NuGetPackageVersion = package.Identity.Version.ToNormalizedString(),
						IsDeprecated = false,
						IsPrerelease = package.Identity.Version.IsPrerelease,
						Tags = package.Tags
							.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries)
							.Select(tag => tag.Trim())
							.ToArray(),
						Type = AddinType.Unknown
					};

					if (package.Title.Contains("[DEPRECATED]", StringComparison.OrdinalIgnoreCase))
					{
						addinMetadata.IsDeprecated = true;
						addinMetadata.AnalysisResult.Notes = package.Description;
					}

					return addinMetadata;
				})
				.ToArray();
		}
	}
}
