using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ProductivityTracker.Tests
{
    [TestClass]
    public sealed class BuildConfigurationTests
    {
        [TestMethod]
        public void DeploymentAndControlVariantsDoNotShareBuildOrRestoreOutputs()
        {
            var folder = new DirectoryInfo(AppContext.BaseDirectory);
            while (folder != null && !File.Exists(Path.Combine(folder.FullName, "ProductivityTracker.csproj")))
            {
                folder = folder.Parent;
            }
            Assert.IsNotNull(folder);
            var variants = new[]
            {
                new[] { "-p:WindowsAppSDKSelfContained=true" },
                new[] { "-p:WindowsAppSDKSelfContained=false" },
                new[] { "-p:WindowsAppSDKSelfContained=false", "-p:WindowsAppSdkBootstrapInitialize=false" },
                new[] { "-p:WindowsAppSDKSelfContained=true", "-p:UseExperimentalActivityControls=true" },
                new[] { "-p:WindowsAppSDKSelfContained=true", "-p:PublishAot=true" },
            };
            var properties = new[] { "OutputPath", "IntermediateOutputPath", "MSBuildProjectExtensionsPath" };
            var outputs = properties.ToDictionary(property => property, _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            foreach (var variant in variants)
            {
                var start = new ProcessStartInfo("dotnet")
                {
                    WorkingDirectory = folder.FullName,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };
                foreach (var argument in new[]
                    { "msbuild", "ProductivityTracker.csproj", "-p:Platform=x64", "-getProperty:" + string.Join(",", properties) }.Concat(variant))
                {
                    start.ArgumentList.Add(argument);
                }
                using var process = Process.Start(start)!;
                if (!process.WaitForExit(30000))
                {
                    process.Kill(true);
                    Assert.Fail("Build configuration evaluation timed out.");
                }
                var text = process.StandardOutput.ReadToEnd();
                Assert.AreEqual(0, process.ExitCode, process.StandardError.ReadToEnd() + text);
                using var result = JsonDocument.Parse(text);
                foreach (var property in properties)
                {
                    var path = result.RootElement.GetProperty("Properties").GetProperty(property).GetString()!;
                    Assert.IsFalse(string.IsNullOrWhiteSpace(path), property);
                    Assert.IsTrue(outputs[property].Add(path), "Variants share " + property + ": " + path);
                }
            }
        }
    }
}
