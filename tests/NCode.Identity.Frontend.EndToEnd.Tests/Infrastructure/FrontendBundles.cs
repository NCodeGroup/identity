// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

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
}
