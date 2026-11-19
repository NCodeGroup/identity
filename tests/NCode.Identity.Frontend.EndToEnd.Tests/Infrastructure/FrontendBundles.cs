// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Identity.Frontend.EndToEnd.Tests.Infrastructure;

/// <summary>
/// Locates the frontend bundles that ./build/e2e.ps1 builds before the suite runs.
/// </summary>
public static class FrontendBundles
{
    private const string SolutionFileName = "NCode.Identity.slnx";

    /// <summary>
    /// Gets the directory of the built bundle of a frontend app, which is <c>frontend/apps/{appName}/dist</c> under the
    /// repository.
    /// </summary>
    /// <param name="appName">The name of the app's folder under <c>frontend/apps</c>.</param>
    public static string GetRootPath(string appName)
    {
        ArgumentException.ThrowIfNullOrEmpty(appName);

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (
            directory is not null
            && !File.Exists(Path.Combine(directory.FullName, SolutionFileName))
        )
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
}
