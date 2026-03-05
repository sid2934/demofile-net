namespace DemoFile.Test.Integration;

[TestFixture]
public class DemoFrameParserIntegrationTest
{
    private static readonly string DemoBase = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "demos");
    private static readonly Lazy<byte[]> DemoBytes = new(() => File.ReadAllBytes(Path.Combine(DemoBase, "14011.dem")));

    [Test]
    public async Task ReadAllFrames_ReturnsFrames()
    {
        // Arrange
        var parser = new CsDemoFrameParser(new MemoryStream(DemoBytes.Value));

        // Act
        await parser.StartReadingAsync(default);
        var frames = await parser.ReadAllFramesAsync(default);

        // Assert
        Assert.That(frames, Is.Not.Empty);
        Assert.That(frames[0], Is.InstanceOf<DemoFileHeaderFrame>());
        Assert.That(frames[^1], Is.InstanceOf<DemoStopFrame>());
    }

    [Test]
    public async Task ReadNextFrame_IteratesAllFrames()
    {
        // Arrange
        var parser = new CsDemoFrameParser(new MemoryStream(DemoBytes.Value));
        await parser.StartReadingAsync(default);

        // Act
        var frames = new List<DemoFrame>();
        DemoFrame? frame;
        while ((frame = await parser.ReadNextFrameAsync(default)) != null)
        {
            frames.Add(frame);
            if (frame is DemoStopFrame)
                break;
        }

        // Assert
        Assert.That(frames, Is.Not.Empty);
        Assert.That(frames[0], Is.InstanceOf<DemoFileHeaderFrame>());
        Assert.That(frames[^1], Is.InstanceOf<DemoStopFrame>());
    }

    [Test]
    public async Task ReadAllFrames_FrameByFrameMatchesReadAll()
    {
        // Arrange - read all at once
        var parser1 = new CsDemoFrameParser(new MemoryStream(DemoBytes.Value));
        await parser1.StartReadingAsync(default);
        var allFrames = await parser1.ReadAllFramesAsync(default);

        // Arrange - read one-by-one
        var parser2 = new CsDemoFrameParser(new MemoryStream(DemoBytes.Value));
        await parser2.StartReadingAsync(default);
        var oneByOneFrames = new List<DemoFrame>();
        DemoFrame? frame;
        while ((frame = await parser2.ReadNextFrameAsync(default)) != null)
        {
            oneByOneFrames.Add(frame);
            if (frame is DemoStopFrame)
                break;
        }

        // Assert - same count and same command types
        Assert.That(oneByOneFrames, Has.Count.EqualTo(allFrames.Count));
        for (var i = 0; i < allFrames.Count; i++)
        {
            Assert.That(oneByOneFrames[i].Command, Is.EqualTo(allFrames[i].Command));
            Assert.That(oneByOneFrames[i].Tick, Is.EqualTo(allFrames[i].Tick));
        }
    }

    [Test]
    public async Task ReadAllFrames_TicksAreMonotonic()
    {
        // Arrange
        var parser = new CsDemoFrameParser(new MemoryStream(DemoBytes.Value));
        await parser.StartReadingAsync(default);

        // Act
        var frames = await parser.ReadAllFramesAsync(default);

        // Assert - ticks should be monotonically non-decreasing
        var prevTick = DemoTick.PreRecord;
        foreach (var f in frames)
        {
            Assert.That(f.Tick.Value, Is.GreaterThanOrEqualTo(prevTick.Value),
                $"Frame {f.Command} at tick {f.Tick} is not >= previous tick {prevTick}");
            prevTick = f.Tick;
        }
    }

    [Test]
    public async Task ReadAllFrames_FileHeaderHasExpectedProperties()
    {
        // Arrange
        var parser = new CsDemoFrameParser(new MemoryStream(DemoBytes.Value));
        await parser.StartReadingAsync(default);

        // Act
        var frames = await parser.ReadAllFramesAsync(default);
        var headerFrame = frames.OfType<DemoFileHeaderFrame>().FirstOrDefault();

        // Assert
        Assert.That(headerFrame, Is.Not.Null);
        Assert.That(headerFrame!.Header.DemoFileStamp, Is.Not.Null.And.Not.Empty);
        Assert.That(headerFrame.Command, Is.EqualTo(EDemoCommands.DemFileHeader));
    }

    [Test]
    public async Task ReadAllFrames_PacketFramesHaveNetworkMessages()
    {
        // Arrange
        var parser = new CsDemoFrameParser(new MemoryStream(DemoBytes.Value));
        await parser.StartReadingAsync(default);

        // Act
        var frames = await parser.ReadAllFramesAsync(default);
        var packetFrames = frames.OfType<DemoPacketFrame>().ToList();

        // Assert
        Assert.That(packetFrames, Is.Not.Empty, "Should have at least one packet frame");
        Assert.That(packetFrames.Any(p => p.Messages.Count > 0), Is.True,
            "At least one packet frame should contain network messages");
    }

    [Test]
    public async Task ReadAllFrames_NetworkMessagesHaveTypes()
    {
        // Arrange
        var parser = new CsDemoFrameParser(new MemoryStream(DemoBytes.Value));
        await parser.StartReadingAsync(default);

        // Act
        var frames = await parser.ReadAllFramesAsync(default);
        var allMessages = frames
            .OfType<DemoPacketFrame>()
            .SelectMany(p => p.Messages)
            .ToList();

        // Assert
        Assert.That(allMessages, Is.Not.Empty);
        Assert.That(allMessages.All(m => m.MessageName != null), Is.True,
            "All messages should have a name");
        Assert.That(allMessages.All(m => m.Size >= 0), Is.True,
            "All messages should have a non-negative size");
    }

    [Test]
    public async Task ReadAllFrames_ContainsKnownNetworkMessageTypes()
    {
        // Arrange
        var parser = new CsDemoFrameParser(new MemoryStream(DemoBytes.Value));
        await parser.StartReadingAsync(default);

        // Act
        var frames = await parser.ReadAllFramesAsync(default);
        var messageNames = frames
            .OfType<DemoPacketFrame>()
            .SelectMany(p => p.Messages)
            .Select(m => m.MessageName)
            .ToHashSet();

        // Assert - should have common network messages
        Assert.That(messageNames, Does.Contain(nameof(NET_Messages.NetTick)));
    }

    [Test]
    public async Task ReadAllFrames_ContainsExpectedDemoCommandTypes()
    {
        // Arrange
        var parser = new CsDemoFrameParser(new MemoryStream(DemoBytes.Value));
        await parser.StartReadingAsync(default);

        // Act
        var frames = await parser.ReadAllFramesAsync(default);
        var commandTypes = frames.Select(f => f.Command).ToHashSet();

        // Assert - should have key demo command types
        Assert.That(commandTypes, Does.Contain(EDemoCommands.DemFileHeader));
        Assert.That(commandTypes, Does.Contain(EDemoCommands.DemStop));
    }

    [Test]
    public async Task StartReadingAsync_InvalidMagic_Throws()
    {
        // Arrange
        var invalidData = new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15 };
        var parser = new DemoFrameParser(new MemoryStream(invalidData));

        // Act & Assert
        Assert.ThrowsAsync<InvalidDemoException>(async () =>
            await parser.StartReadingAsync(default));
    }

    [Test]
    public void ReadNextFrame_WithoutStartReading_Throws()
    {
        // Arrange
        var parser = new DemoFrameParser(new MemoryStream(DemoBytes.Value));

        // Act & Assert
        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await parser.ReadNextFrameAsync(default));
    }

    [Test]
    public async Task ReadNextFrame_AfterEnd_ReturnsNull()
    {
        // Arrange
        var parser = new CsDemoFrameParser(new MemoryStream(DemoBytes.Value));
        await parser.StartReadingAsync(default);
        await parser.ReadAllFramesAsync(default);

        // Act
        var extraFrame = await parser.ReadNextFrameAsync(default);

        // Assert
        Assert.That(extraFrame, Is.Null);
    }

    [Test]
    public async Task BaseParser_ReadAllFrames_ReturnsFrames()
    {
        // Verify the base DemoFrameParser (without CS-specific extensions) works
        var parser = new DemoFrameParser(new MemoryStream(DemoBytes.Value));
        await parser.StartReadingAsync(default);
        var frames = await parser.ReadAllFramesAsync(default);

        Assert.That(frames, Is.Not.Empty);
        Assert.That(frames[0], Is.InstanceOf<DemoFileHeaderFrame>());
        Assert.That(frames[^1], Is.InstanceOf<DemoStopFrame>());
    }

    [Test]
    public async Task CsDemoFrameParser_ParsesCsSpecificMessages()
    {
        // Use a larger demo to increase chances of CS-specific messages
        var parser = new CsDemoFrameParser(new MemoryStream(DemoBytes.Value));
        await parser.StartReadingAsync(default);
        var frames = await parser.ReadAllFramesAsync(default);

        var allMessages = frames
            .OfType<DemoPacketFrame>()
            .SelectMany(p => p.Messages)
            .ToList();

        // All resolved messages should have a body
        var resolvedMessages = allMessages.Where(m => !m.MessageName.StartsWith("Unknown(")).ToList();
        Assert.That(resolvedMessages, Is.Not.Empty, "Should have resolved network messages");
        Assert.That(resolvedMessages.All(m => m.Body != null), Is.True,
            "All resolved messages should have a parsed body");
    }
}
