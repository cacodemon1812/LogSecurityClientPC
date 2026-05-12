using PolicyCollector.Agent.Infrastructure;
using Microsoft.Win32;
using FluentAssertions;

namespace PolicyCollector.Agent.Tests.Infrastructure;

public class RegistryReaderTests
{
    [Fact]
    public void GetString_NonExistentKey_ReturnsNull()
    {
        // Arrange
        var reader = new RegistryReader();

        // Act
        var result = reader.GetString(RegistryHive.LocalMachine,
            @"Software\NonExistent\Path\12345", "ValueName");

        // Assert
        result.Should().BeNull();
    }
}
