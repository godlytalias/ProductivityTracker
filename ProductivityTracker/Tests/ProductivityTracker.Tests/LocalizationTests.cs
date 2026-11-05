using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ProductivityTracker.Tests
{
    [TestClass]
    public sealed class LocalizationTests
    {
        [TestMethod]
        public void EveryTranslationHasMatchingKeysAndUsableFormatArguments()
        {
            var root = AppRoot();
            var expected = ReadResources(Path.Combine(root, "Strings", "en-US", "Resources.resw"));
            foreach (var language in new[]
                { "en-US", "hi-IN", "zh-CN", "ar-SA", "he-IL", "es-ES", "fr-FR", "ko-KR", "ja-JP" })
            {
                var translated = ReadResources(Path.Combine(root, "Strings", language, "Resources.resw"));
                CollectionAssert.AreEquivalent(expected.Keys.ToArray(), translated.Keys.ToArray(), language);
                foreach (var pair in expected)
                {
                    var value = translated[pair.Key];
                    Assert.IsFalse(string.IsNullOrWhiteSpace(value), language + "/" + pair.Key);
                    var indices = Regex.Matches(pair.Value, @"\{(\d+)(?:[^{}]*)\}")
                        .Select(match => match.Groups[1].Value).OrderBy(index => index).ToArray();
                    var actual = Regex.Matches(value, @"\{(\d+)(?:[^{}]*)\}")
                        .Select(match => match.Groups[1].Value).OrderBy(index => index).ToArray();
                    CollectionAssert.AreEqual(indices, actual, language + "/" + pair.Key);
                    var format = CompositeFormat.Parse(value);
                    var arguments = Enumerable.Range(0, format.MinimumArgumentCount)
                        .Select(index => Regex.IsMatch(value, @"\{" + index + @":[Dt]\}")
                            ? (object)new DateTime(2026, 10, 31, 14, 30, 0) : 1)
                        .ToArray();
                    _ = string.Format(CultureInfo.GetCultureInfo(language), format, arguments);
                }
            }
        }

        [TestMethod]
        public void XamlUidsAndCodeLookupsHaveResources()
        {
            var root = AppRoot();
            var resources = ReadResources(Path.Combine(root, "Strings", "en-US", "Resources.resw"));
            XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
            var xaml = XDocument.Load(Path.Combine(root, "MainWindow.xaml"));
            foreach (var uid in xaml.Descendants().Attributes(x + "Uid").Select(attribute => attribute.Value))
            {
                Assert.IsTrue(resources.Keys.Any(key => key.StartsWith(uid + ".", StringComparison.Ordinal)),
                    "No property resources for x:Uid=" + uid);
            }
            foreach (var file in Directory.GetFiles(root, "*.cs"))
            {
                foreach (Match match in Regex.Matches(File.ReadAllText(file),
                    "AppStrings\\.(?:Get|Format)\\(\"([^\"]+)\""))
                {
                    Assert.IsTrue(resources.ContainsKey(match.Groups[1].Value.Replace('/', '.')),
                        Path.GetFileName(file) + ": " + match.Groups[1].Value);
                }
            }
        }

        private static Dictionary<string, string> ReadResources(string path) =>
            XDocument.Load(path).Root!.Elements("data").ToDictionary(
                element => (string)element.Attribute("name")!, element => (string)element.Element("value")!);

        [TestMethod]
        public void PackagedControlsUseSdkFrameworkRegistrations()
        {
            var root = AppRoot();
            var packageRoot = Path.Combine(Directory.GetParent(root)!.FullName, "ProductivityTrackerPackage");
            var manifest = XDocument.Load(Path.Combine(packageRoot, "Package.appxmanifest"));
            XNamespace package = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";
            Assert.IsFalse(manifest.Descendants(package + "ActivatableClass").Any(item =>
                ((string?)item.Attribute("ActivatableClassId"))?.StartsWith("Microsoft.UI.Xaml.", StringComparison.Ordinal) == true),
                "The app must not override the SDK framework's control factories.");
            XNamespace build = "http://schemas.microsoft.com/developer/msbuild/2003";
            var project = XDocument.Load(Path.Combine(packageRoot, "ProductivityTrackerPackage.wapproj"));
            var reference = project.Descendants(build + "ProjectReference").Single(item =>
                ((string?)item.Attribute("Include"))?.EndsWith("ProductivityTracker.csproj", StringComparison.Ordinal) == true);
            StringAssert.Contains((string?)reference.Element(build + "AdditionalProperties") ?? string.Empty,
                "WindowsAppSDKSelfContained=false");
            StringAssert.Contains((string?)reference.Element(build + "AdditionalProperties") ?? string.Empty,
                "WindowsAppSdkBootstrapInitialize=false");
            StringAssert.Contains((string?)reference.Element(build + "AdditionalProperties") ?? string.Empty,
                "WindowsAppSdkUndockedRegFreeWinRTInitialize=false");
            Assert.AreEqual("true", (string?)project.Descendants(build + "WapAlwaysBuildDependentProjects").Single(),
                "Visual Studio must build the packaging-specific app variant.");
            var appProject = XDocument.Load(Path.Combine(root, "ProductivityTracker.csproj"));
            foreach (var component in new[] { "Tabular", "Charts" })
            {
                var pri = appProject.Descendants("Content").SingleOrDefault(item =>
                    (string?)item.Attribute("Link") == "Microsoft.UI.Xaml.Controls." + component + ".pri");
                Assert.IsNotNull(pri, component + " app-relative theme resources must be included in the package.");
                Assert.AreEqual("PreserveNewest", (string?)pri.Attribute("CopyToOutputDirectory"));
            }
        }

        private static string AppRoot()
        {
            var folder = new DirectoryInfo(AppContext.BaseDirectory);
            while (folder != null && !File.Exists(Path.Combine(folder.FullName, "ProductivityTracker.csproj")))
            {
                folder = folder.Parent;
            }
            Assert.IsNotNull(folder, "The app source tree is required for resource validation.");
            return folder.FullName;
        }
    }
}
