using System;
using System.IO;

namespace SqlToAi.Tests.TestSupport;

/// <summary>
/// Finds the solution root directory starting from the current application directory.
/// </summary>
public static class SolutionRootLocator
{
    /// <summary>
    /// Locates the directory containing <c>SqlToAi.slnx</c> or <c>SqlToAi.sln</c>.
    /// </summary>
    public static string Find()
    {
        var currentDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        while (currentDirectory is not null)
        {
            if (File.Exists(Path.Combine(currentDirectory.FullName, "SqlToAi.slnx")) ||
                File.Exists(Path.Combine(currentDirectory.FullName, "SqlToAi.sln")))
            {
                return currentDirectory.FullName;
            }

            currentDirectory = currentDirectory.Parent;
        }

        throw new DirectoryNotFoundException("The root directory containing the solution 'SqlToAi.slnx' was not found.");
    }
}
