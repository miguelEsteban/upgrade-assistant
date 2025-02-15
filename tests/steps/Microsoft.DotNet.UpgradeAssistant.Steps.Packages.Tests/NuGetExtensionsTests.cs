﻿using System;
using Xunit;

namespace Microsoft.DotNet.UpgradeAssistant.Steps.Packages.Tests
{
    public class NuGetExtensionsTests
    {
        [Fact]
        public void GetNuGetVersionThrowsWhenNull()
        {
            NuGetReference? reference = default;

            Assert.Throws<ArgumentNullException>(() => reference!.GetNuGetVersion());
        }

        [InlineData("6.0.*", "6.0.0")]
        [InlineData("4.*", "4.0")]
        [InlineData("*", "0.0")]
        [Theory]
        public void GetNuGetVersionCanHandleFloatingVersions(string versionToTest, string expectedVersion)
        {
            if (versionToTest == null)
            {
                throw new ArgumentNullException(nameof(versionToTest));
            }

            if (expectedVersion == null)
            {
                throw new ArgumentNullException(nameof(expectedVersion));
            }

            var reference = new NuGetReference("Example", versionToTest);

            var actualVersion = reference.GetNuGetVersion();

            Assert.Equal(expectedVersion, actualVersion?.ToString());
        }
    }
}
