﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Linq;
using Autofac.Extras.Moq;
using Xunit;

namespace Microsoft.DotNet.UpgradeAssistant.MSBuild.Tests
{
    [Collection(MSBuildStepTestCollection.Name)]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Assertions", "xUnit2013:Do not use equality check to check for collection size.", Justification = "Need to verify .Count property")]
    public class TargetFrameworkMonikerCollectionTests
    {
        [Fact]
        public void ThrowsOnNull()
        {
            // Assert
            Assert.Throws<ArgumentNullException>(() => new TargetFrameworkMonikerCollection(null!));
        }

        [InlineData("net5.0")]
        [Theory]
        public void SdkStyleSingle(string tfm)
        {
            // Arrange
            using var mock = AutoMock.GetLoose();

            var project = mock.Mock<IProjectFile>();
            project.Setup(p => p.IsSdk).Returns(true);
            project.Setup(p => p.GetPropertyValue("TargetFrameworks")).Returns(string.Empty);
            project.Setup(p => p.GetPropertyValue("TargetFramework")).Returns(tfm);

            // Act
            var collection = new TargetFrameworkMonikerCollection(project.Object);

            // Assert
            Assert.Equal(1, collection.Count);
            Assert.Collection(collection, t => Assert.Equal(tfm, t.Name));
        }

        [InlineData("net5.0", new[] { "net5.0" })]
        [InlineData("net5.0;netstandard2.0", new[] { "net5.0", "netstandard2.0" })]
        [Theory]
        public void SdkStyleMultiple(string value, string[] expected)
        {
            // Arrange
            using var mock = AutoMock.GetLoose();

            var project = mock.Mock<IProjectFile>();
            project.Setup(p => p.IsSdk).Returns(true);
            project.Setup(p => p.GetPropertyValue("TargetFramework")).Returns(string.Empty);
            project.Setup(p => p.GetPropertyValue("TargetFrameworks")).Returns(value);

            // Act
            var collection = new TargetFrameworkMonikerCollection(project.Object);

            // Assert
            Assert.Equal(expected.Length, collection.Count);
            Assert.Equal(expected, collection.Select(t => t.Name).ToArray());
        }

        [InlineData("v4.5", "net45")]
        [Theory]
        public void NonSdkStyle(string version, string expected)
        {
            // Arrange
            using var mock = AutoMock.GetLoose();

            var project = mock.Mock<IProjectFile>();
            project.Setup(p => p.IsSdk).Returns(false);
            project.Setup(p => p.GetPropertyValue("TargetFrameworkVersion")).Returns(version);

            // Act
            var collection = new TargetFrameworkMonikerCollection(project.Object);

            // Assert
            Assert.Equal(1, collection.Count);
            Assert.Collection(collection, t => Assert.Equal(expected, t.Name));
        }
    }
}
