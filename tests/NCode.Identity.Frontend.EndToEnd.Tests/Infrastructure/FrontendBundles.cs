// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Identity.Frontend.TestSupport;

namespace NCode.Identity.Frontend.EndToEnd.Tests.Infrastructure;

/// <summary>
/// Locates the built bundles of the frontend apps that the suite's hosts serve.
/// </summary>
// ./build/e2e.ps1 builds each bundle at frontend/apps/<app>/dist before it runs the suite.
public static class FrontendBundles
{
    private const string SolutionFileName = "NCode.Identity.slnx";

    private static bool HoldsSolution(DirectoryInfo directory)
    {
        var solutionPath = Path.Combine(directory.FullName, SolutionFileName);
        return File.Exists(solutionPath);
    }

    /// <summary>
    /// Gets the directory of a frontend app's built bundle, and fails when the bundle is not built.
    /// </summary>
    /// <param name="appName">The name of the frontend app, such as <c>auth</c>.</param>
    public static string GetRootPath(string appName)
    {
        ArgumentException.ThrowIfNullOrEmpty(appName);

        // The bundle is frontend/apps/<appName>/dist under the directory that holds the solution.
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !HoldsSolution(directory))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw new InvalidOperationException(
                $"No directory above '{AppContext.BaseDirectory}' holds {SolutionFileName}."
            );
        }

        var rootPath = Path.Combine(directory.FullName, "frontend", "apps", appName, "dist");
        var indexPath = Path.Combine(rootPath, "index.html");
        if (!File.Exists(indexPath))
        {
            throw new InvalidOperationException(
                $"The apps/{appName} bundle is not built: '{indexPath}' does not exist. ./build/e2e.ps1 builds it before it runs the suite."
            );
        }

        return rootPath;
    }

    /// <summary>
    /// Gets the address of each typeface license that a frontend app's built bundle ships, under the address that its
    /// host serves the bundle's assets at.
    /// </summary>
    /// <param name="appName">The name of the frontend app, such as <c>auth</c>.</param>
    /// <param name="assetsUri">The absolute address that the host serves the bundle's assets at, ending in a
    /// slash.</param>
    public static IReadOnlyList<Uri> GetLicenseUrls(string appName, Uri assetsUri)
    {
        ArgumentNullException.ThrowIfNull(assetsUri);

        var rootPath = GetRootPath(appName);
        var assetsPath = Path.Combine(rootPath, "assets");
        List<Uri> urls = [];
        foreach (var license in TypefaceLicenses.All)
        {
            // The build names each license by its content hash, so the bundle on disk says which name the host serves.
            var searchPattern = $"{license.Name}-*.txt";
            var files = Directory.GetFiles(assetsPath, searchPattern);
            if (files.Length != 1)
            {
                throw new InvalidOperationException(
                    $"The apps/{appName} bundle holds {files.Length} assets named '{searchPattern}', not one."
                );
            }

            var fileName = Path.GetFileName(files[0]);
            var url = new Uri(assetsUri, fileName);
            urls.Add(url);
        }

        return urls;
    }
}
