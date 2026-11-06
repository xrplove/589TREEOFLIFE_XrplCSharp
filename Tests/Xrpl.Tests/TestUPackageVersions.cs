using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace XrplTests.Xrpl;

// Packing turns a ProjectReference into a dependency on the referenced project's PackageVersion,
// but the release pushes with --skip-duplicate: a package left at a version nuget.org already has
// is dropped without a word and keeps the dependencies it was published with. 11.25.0.0 moved
// Xrpl.AddressCodec alone, so Xrpl still required BinaryCodec and Keypairs 11.22.0, those still
// required AddressCodec 11.20.0, and the new AddressCodec reached nobody who installs Xrpl.
[TestClass]
public class TestUPackageVersions
{
    [TestMethod]
    public void XrplAndEveryPackageItDependsOn_ShareOneVersion()
    {
        Dictionary<string, string> versions = new(StringComparer.Ordinal);
        Collect(Path.Combine(FindRepositoryRoot(), "Xrpl", "Xrpl.csproj"), versions);

        Assert.IsTrue(versions.ContainsKey("Xrpl.AddressCodec"), "Xrpl no longer reaches Xrpl.AddressCodec.");

        string report = string.Join(", ", versions.Select(package => $"{package.Key} {package.Value}"));
        Assert.AreEqual(1, versions.Values.Distinct(StringComparer.Ordinal).Count(), report);
    }

    private static void Collect(string projectPath, Dictionary<string, string> versions)
    {
        string name = Path.GetFileNameWithoutExtension(projectPath);
        if (versions.ContainsKey(name))
        {
            return;
        }

        XDocument project = XDocument.Load(projectPath);
        string version = project.Descendants("PackageVersion").FirstOrDefault()?.Value;
        Assert.IsNotNull(version, $"{name} has no PackageVersion.");
        versions.Add(name, version);

        string directory = Path.GetDirectoryName(projectPath);
        foreach (XElement reference in project.Descendants("ProjectReference"))
        {
            string relativePath = reference.Attribute("Include").Value.Replace('\\', Path.DirectorySeparatorChar);
            Collect(Path.GetFullPath(Path.Combine(directory, relativePath)), versions);
        }
    }

    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo directory = new(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "XrplCSharp.sln")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException($"XrplCSharp.sln not found above {AppContext.BaseDirectory}.");
    }
}
