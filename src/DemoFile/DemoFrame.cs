using System.Buffers;
using Google.Protobuf;
using Snappier;

namespace DemoFile;

/// <summary>
/// Represents a parsed network message from within a demo packet frame.
/// </summary>
public sealed class DemoNetworkMessage
{
    /// <summary>The network message type ID.</summary>
    public int MessageType { get; }

    /// <summary>The human-readable name of the network message type.</summary>
    public string MessageName { get; }

    /// <summary>The deserialized protobuf message body, if the message type is known. <c>null</c> for unrecognized types.</summary>
    public IMessage? Body { get; }

    /// <summary>The size of the raw message data in bytes.</summary>
    public int Size { get; }

    internal DemoNetworkMessage(int messageType, string messageName, IMessage? body, int size)
    {
        MessageType = messageType;
        MessageName = messageName;
        Body = body;
        Size = size;
    }
}

/// <summary>
/// Base class representing a single demo frame (command) read from the demo file.
/// </summary>
public abstract class DemoFrame
{
    /// <summary>The demo command type.</summary>
    public EDemoCommands Command { get; }

    /// <summary>Whether the frame data was compressed.</summary>
    public bool IsCompressed { get; }

    /// <summary>The demo tick at which this frame was recorded.</summary>
    public DemoTick Tick { get; }

    /// <summary>The size of the frame data in bytes (compressed size).</summary>
    public int Size { get; }

    private protected DemoFrame(EDemoCommands command, bool isCompressed, DemoTick tick, int size)
    {
        Command = command;
        IsCompressed = isCompressed;
        Tick = tick;
        Size = size;
    }
}

/// <summary>Represents a <see cref="EDemoCommands.DemStop"/> frame indicating end of demo.</summary>
public sealed class DemoStopFrame : DemoFrame
{
    internal DemoStopFrame(bool isCompressed, DemoTick tick, int size)
        : base(EDemoCommands.DemStop, isCompressed, tick, size)
    {
    }
}

/// <summary>Represents a <see cref="EDemoCommands.DemFileHeader"/> frame.</summary>
public sealed class DemoFileHeaderFrame : DemoFrame
{
    /// <summary>The parsed file header message.</summary>
    public CDemoFileHeader Header { get; }

    internal DemoFileHeaderFrame(bool isCompressed, DemoTick tick, int size, CDemoFileHeader header)
        : base(EDemoCommands.DemFileHeader, isCompressed, tick, size)
    {
        Header = header;
    }
}

/// <summary>Represents a <see cref="EDemoCommands.DemFileInfo"/> frame.</summary>
public sealed class DemoFileInfoFrame : DemoFrame
{
    /// <summary>The parsed file info message.</summary>
    public CDemoFileInfo FileInfo { get; }

    internal DemoFileInfoFrame(bool isCompressed, DemoTick tick, int size, CDemoFileInfo fileInfo)
        : base(EDemoCommands.DemFileInfo, isCompressed, tick, size)
    {
        FileInfo = fileInfo;
    }
}

/// <summary>Represents a <see cref="EDemoCommands.DemSyncTick"/> frame.</summary>
public sealed class DemoSyncTickFrame : DemoFrame
{
    /// <summary>The parsed sync tick message.</summary>
    public CDemoSyncTick SyncTick { get; }

    internal DemoSyncTickFrame(bool isCompressed, DemoTick tick, int size, CDemoSyncTick syncTick)
        : base(EDemoCommands.DemSyncTick, isCompressed, tick, size)
    {
        SyncTick = syncTick;
    }
}

/// <summary>Represents a <see cref="EDemoCommands.DemSendTables"/> frame.</summary>
public sealed class DemoSendTablesFrame : DemoFrame
{
    /// <summary>The parsed send tables message.</summary>
    public CDemoSendTables SendTables { get; }

    internal DemoSendTablesFrame(bool isCompressed, DemoTick tick, int size, CDemoSendTables sendTables)
        : base(EDemoCommands.DemSendTables, isCompressed, tick, size)
    {
        SendTables = sendTables;
    }
}

/// <summary>Represents a <see cref="EDemoCommands.DemClassInfo"/> frame.</summary>
public sealed class DemoClassInfoFrame : DemoFrame
{
    /// <summary>The parsed class info message.</summary>
    public CDemoClassInfo ClassInfo { get; }

    internal DemoClassInfoFrame(bool isCompressed, DemoTick tick, int size, CDemoClassInfo classInfo)
        : base(EDemoCommands.DemClassInfo, isCompressed, tick, size)
    {
        ClassInfo = classInfo;
    }
}

/// <summary>Represents a <see cref="EDemoCommands.DemStringTables"/> frame.</summary>
public sealed class DemoStringTablesFrame : DemoFrame
{
    /// <summary>The parsed string tables message.</summary>
    public CDemoStringTables StringTables { get; }

    internal DemoStringTablesFrame(bool isCompressed, DemoTick tick, int size, CDemoStringTables stringTables)
        : base(EDemoCommands.DemStringTables, isCompressed, tick, size)
    {
        StringTables = stringTables;
    }
}

/// <summary>Represents a <see cref="EDemoCommands.DemPacket"/> or <see cref="EDemoCommands.DemSignonPacket"/> frame, with parsed network messages.</summary>
public sealed class DemoPacketFrame : DemoFrame
{
    /// <summary>The parsed demo packet message.</summary>
    public CDemoPacket Packet { get; }

    /// <summary>The network messages contained within this packet, parsed from the bitstream.</summary>
    public IReadOnlyList<DemoNetworkMessage> Messages { get; }

    internal DemoPacketFrame(EDemoCommands command, bool isCompressed, DemoTick tick, int size, CDemoPacket packet, IReadOnlyList<DemoNetworkMessage> messages)
        : base(command, isCompressed, tick, size)
    {
        Packet = packet;
        Messages = messages;
    }
}

/// <summary>Represents a <see cref="EDemoCommands.DemConsoleCmd"/> frame.</summary>
public sealed class DemoConsoleCmdFrame : DemoFrame
{
    /// <summary>The parsed console command message.</summary>
    public CDemoConsoleCmd ConsoleCmd { get; }

    internal DemoConsoleCmdFrame(bool isCompressed, DemoTick tick, int size, CDemoConsoleCmd consoleCmd)
        : base(EDemoCommands.DemConsoleCmd, isCompressed, tick, size)
    {
        ConsoleCmd = consoleCmd;
    }
}

/// <summary>Represents a <see cref="EDemoCommands.DemCustomData"/> frame.</summary>
public sealed class DemoCustomDataFrame : DemoFrame
{
    /// <summary>The parsed custom data message.</summary>
    public CDemoCustomData CustomData { get; }

    internal DemoCustomDataFrame(bool isCompressed, DemoTick tick, int size, CDemoCustomData customData)
        : base(EDemoCommands.DemCustomData, isCompressed, tick, size)
    {
        CustomData = customData;
    }
}

/// <summary>Represents a <see cref="EDemoCommands.DemCustomDataCallbacks"/> frame.</summary>
public sealed class DemoCustomDataCallbacksFrame : DemoFrame
{
    /// <summary>The parsed custom data callbacks message.</summary>
    public CDemoCustomDataCallbacks CustomDataCallbacks { get; }

    internal DemoCustomDataCallbacksFrame(bool isCompressed, DemoTick tick, int size, CDemoCustomDataCallbacks customDataCallbacks)
        : base(EDemoCommands.DemCustomDataCallbacks, isCompressed, tick, size)
    {
        CustomDataCallbacks = customDataCallbacks;
    }
}

/// <summary>Represents a <see cref="EDemoCommands.DemUserCmd"/> frame.</summary>
public sealed class DemoUserCmdFrame : DemoFrame
{
    /// <summary>The parsed user command message.</summary>
    public CDemoUserCmd UserCmd { get; }

    internal DemoUserCmdFrame(bool isCompressed, DemoTick tick, int size, CDemoUserCmd userCmd)
        : base(EDemoCommands.DemUserCmd, isCompressed, tick, size)
    {
        UserCmd = userCmd;
    }
}

/// <summary>Represents a <see cref="EDemoCommands.DemFullPacket"/> frame.</summary>
public sealed class DemoFullPacketFrame : DemoFrame
{
    /// <summary>The parsed full packet message.</summary>
    public CDemoFullPacket FullPacket { get; }

    internal DemoFullPacketFrame(bool isCompressed, DemoTick tick, int size, CDemoFullPacket fullPacket)
        : base(EDemoCommands.DemFullPacket, isCompressed, tick, size)
    {
        FullPacket = fullPacket;
    }
}

/// <summary>Represents a <see cref="EDemoCommands.DemSaveGame"/> frame.</summary>
public sealed class DemoSaveGameFrame : DemoFrame
{
    /// <summary>The parsed save game message.</summary>
    public CDemoSaveGame SaveGame { get; }

    internal DemoSaveGameFrame(bool isCompressed, DemoTick tick, int size, CDemoSaveGame saveGame)
        : base(EDemoCommands.DemSaveGame, isCompressed, tick, size)
    {
        SaveGame = saveGame;
    }
}

/// <summary>Represents a <see cref="EDemoCommands.DemSpawnGroups"/> frame.</summary>
public sealed class DemoSpawnGroupsFrame : DemoFrame
{
    /// <summary>The parsed spawn groups message.</summary>
    public CDemoSpawnGroups SpawnGroups { get; }

    internal DemoSpawnGroupsFrame(bool isCompressed, DemoTick tick, int size, CDemoSpawnGroups spawnGroups)
        : base(EDemoCommands.DemSpawnGroups, isCompressed, tick, size)
    {
        SpawnGroups = spawnGroups;
    }
}

/// <summary>Represents a <see cref="EDemoCommands.DemAnimationData"/> frame.</summary>
public sealed class DemoAnimationDataFrame : DemoFrame
{
    /// <summary>The parsed animation data message.</summary>
    public CDemoAnimationData AnimationData { get; }

    internal DemoAnimationDataFrame(bool isCompressed, DemoTick tick, int size, CDemoAnimationData animationData)
        : base(EDemoCommands.DemAnimationData, isCompressed, tick, size)
    {
        AnimationData = animationData;
    }
}

/// <summary>Represents a <see cref="EDemoCommands.DemAnimationHeader"/> frame.</summary>
public sealed class DemoAnimationHeaderFrame : DemoFrame
{
    /// <summary>The parsed animation header message.</summary>
    public CDemoAnimationHeader AnimationHeader { get; }

    internal DemoAnimationHeaderFrame(bool isCompressed, DemoTick tick, int size, CDemoAnimationHeader animationHeader)
        : base(EDemoCommands.DemAnimationHeader, isCompressed, tick, size)
    {
        AnimationHeader = animationHeader;
    }
}

/// <summary>Represents a <see cref="EDemoCommands.DemRecovery"/> frame.</summary>
public sealed class DemoRecoveryFrame : DemoFrame
{
    /// <summary>The parsed recovery message.</summary>
    public CDemoRecovery Recovery { get; }

    internal DemoRecoveryFrame(bool isCompressed, DemoTick tick, int size, CDemoRecovery recovery)
        : base(EDemoCommands.DemRecovery, isCompressed, tick, size)
    {
        Recovery = recovery;
    }
}

internal static class DemoFrameDecompressor
{
    public static T ParseMessage<T>(MessageParser<T> parser, ReadOnlySpan<byte> buffer, bool isCompressed)
        where T : IMessage<T>
    {
        if (isCompressed)
        {
            var uncompressedSize = Snappy.GetUncompressedLength(buffer);
            var rented = ArrayPool<byte>.Shared.Rent(uncompressedSize);
            try
            {
                Snappy.Decompress(buffer, rented);
                return parser.ParseFrom(rented.AsSpan()[..uncompressedSize]);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }

        return parser.ParseFrom(buffer);
    }
}
