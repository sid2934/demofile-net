using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Google.Protobuf;

namespace DemoFile;

/// <summary>
/// A standalone, game-agnostic demo frame parser that reads a Source 2 demo file
/// frame-by-frame, returning structured <see cref="DemoFrame"/> objects.
/// Unlike <see cref="DemoParser{TGameParser}"/>, this parser does not interpret or
/// maintain game state — it simply deserializes each frame and its nested data into
/// statically typed C# objects.
/// </summary>
public class DemoFrameParser
{
    private readonly ArrayPool<byte> _bytePool = ArrayPool<byte>.Shared;
    private readonly Stream _stream;
    private bool _headerRead;
    private bool _finished;

    /// <summary>
    /// Construct a new <c>.dem</c> frame parser.
    /// </summary>
    /// <param name="stream">A stream of the <c>.dem</c> file.</param>
    public DemoFrameParser(Stream stream)
    {
        _stream = stream;
    }

    /// <summary>
    /// Read the demo file header (magic and size bytes).
    /// Must be called before <see cref="ReadNextFrameAsync"/> or <see cref="ReadAllFramesAsync"/>.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token to stop reading.</param>
    /// <exception cref="InvalidDemoException">Invalid demo file magic.</exception>
    public async ValueTask StartReadingAsync(CancellationToken cancellationToken)
    {
        if (_headerRead)
            return;

        var rented = _bytePool.Rent(16);
        var buf = rented.AsMemory(..16);
        await _stream.ReadExactlyAsync(buf, cancellationToken).ConfigureAwait(false);

        if (!buf.Span[..8].SequenceEqual("PBDEMS2\x00"u8))
        {
            _bytePool.Return(rented);
            throw new InvalidDemoException(
                $"Invalid Source 2 demo magic ('{Encoding.ASCII.GetString(buf.Span[..8])}' != expected 'PBDEMS2')");
        }

        _bytePool.Return(rented);
        _headerRead = true;
    }

    /// <summary>
    /// Read the next frame from the demo file.
    /// <see cref="StartReadingAsync"/> must be called first.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token to stop reading.</param>
    /// <returns>
    /// The next <see cref="DemoFrame"/>, or <c>null</c> if the end of the demo has been reached.
    /// </returns>
    /// <exception cref="InvalidOperationException">If <see cref="StartReadingAsync"/> has not been called.</exception>
    public async ValueTask<DemoFrame?> ReadNextFrameAsync(CancellationToken cancellationToken)
    {
        if (!_headerRead)
            throw new InvalidOperationException($"{nameof(StartReadingAsync)} must be called before reading frames.");

        if (_finished)
            return null;

        cancellationToken.ThrowIfCancellationRequested();

        var (command, isCompressed, tick, size) = ReadCommandHeader();

        if (command == EDemoCommands.DemStop)
        {
            _finished = true;
            return new DemoStopFrame(isCompressed, tick, size);
        }

        var rented = _bytePool.Rent(size);
        var buf = rented.AsMemory(..size);
        await _stream.ReadExactlyAsync(buf, cancellationToken).ConfigureAwait(false);

        try
        {
            return BuildFrame(command, isCompressed, tick, size, buf.Span);
        }
        finally
        {
            _bytePool.Return(rented);
        }
    }

    /// <summary>
    /// Read all remaining frames from the demo file.
    /// <see cref="StartReadingAsync"/> must be called first.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token to stop reading.</param>
    /// <returns>A list of all frames in the demo file.</returns>
    public async ValueTask<IReadOnlyList<DemoFrame>> ReadAllFramesAsync(CancellationToken cancellationToken)
    {
        if (!_headerRead)
            throw new InvalidOperationException($"{nameof(StartReadingAsync)} must be called before reading frames.");

        var frames = new List<DemoFrame>();
        while (true)
        {
            var frame = await ReadNextFrameAsync(cancellationToken).ConfigureAwait(false);
            if (frame == null)
                break;

            frames.Add(frame);

            if (frame is DemoStopFrame)
                break;
        }

        return frames;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private (EDemoCommands Command, bool IsCompressed, DemoTick Tick, int Size) ReadCommandHeader()
    {
        var command = _stream.ReadUVarInt32();
        var tick = (int)_stream.ReadUVarInt32();
        var size = (int)_stream.ReadUVarInt32();

        var isCompressed = (command & (uint)EDemoCommands.DemIsCompressed) != 0;
        var msgType = (EDemoCommands)(command & ~(uint)EDemoCommands.DemIsCompressed);

        return (msgType, isCompressed, new DemoTick(tick), size);
    }

    private DemoFrame BuildFrame(EDemoCommands command, bool isCompressed, DemoTick tick, int size, ReadOnlySpan<byte> buffer)
    {
        switch (command)
        {
            case EDemoCommands.DemFileHeader:
                return new DemoFileHeaderFrame(isCompressed, tick, size,
                    DemoFrameDecompressor.ParseMessage(CDemoFileHeader.Parser, buffer, isCompressed));

            case EDemoCommands.DemFileInfo:
                return new DemoFileInfoFrame(isCompressed, tick, size,
                    DemoFrameDecompressor.ParseMessage(CDemoFileInfo.Parser, buffer, isCompressed));

            case EDemoCommands.DemSyncTick:
                return new DemoSyncTickFrame(isCompressed, tick, size,
                    DemoFrameDecompressor.ParseMessage(CDemoSyncTick.Parser, buffer, isCompressed));

            case EDemoCommands.DemSendTables:
                return new DemoSendTablesFrame(isCompressed, tick, size,
                    DemoFrameDecompressor.ParseMessage(CDemoSendTables.Parser, buffer, isCompressed));

            case EDemoCommands.DemClassInfo:
                return new DemoClassInfoFrame(isCompressed, tick, size,
                    DemoFrameDecompressor.ParseMessage(CDemoClassInfo.Parser, buffer, isCompressed));

            case EDemoCommands.DemStringTables:
                return new DemoStringTablesFrame(isCompressed, tick, size,
                    DemoFrameDecompressor.ParseMessage(CDemoStringTables.Parser, buffer, isCompressed));

            case EDemoCommands.DemSignonPacket:
            case EDemoCommands.DemPacket:
            {
                var packet = DemoFrameDecompressor.ParseMessage(CDemoPacket.Parser, buffer, isCompressed);
                var messages = ParseNetworkMessages(packet.Data.Span);
                return new DemoPacketFrame(command, isCompressed, tick, size, packet, messages);
            }

            case EDemoCommands.DemConsoleCmd:
                return new DemoConsoleCmdFrame(isCompressed, tick, size,
                    DemoFrameDecompressor.ParseMessage(CDemoConsoleCmd.Parser, buffer, isCompressed));

            case EDemoCommands.DemCustomData:
                return new DemoCustomDataFrame(isCompressed, tick, size,
                    DemoFrameDecompressor.ParseMessage(CDemoCustomData.Parser, buffer, isCompressed));

            case EDemoCommands.DemCustomDataCallbacks:
                return new DemoCustomDataCallbacksFrame(isCompressed, tick, size,
                    DemoFrameDecompressor.ParseMessage(CDemoCustomDataCallbacks.Parser, buffer, isCompressed));

            case EDemoCommands.DemUserCmd:
                return new DemoUserCmdFrame(isCompressed, tick, size,
                    DemoFrameDecompressor.ParseMessage(CDemoUserCmd.Parser, buffer, isCompressed));

            case EDemoCommands.DemFullPacket:
                return new DemoFullPacketFrame(isCompressed, tick, size,
                    DemoFrameDecompressor.ParseMessage(CDemoFullPacket.Parser, buffer, isCompressed));

            case EDemoCommands.DemSaveGame:
                return new DemoSaveGameFrame(isCompressed, tick, size,
                    DemoFrameDecompressor.ParseMessage(CDemoSaveGame.Parser, buffer, isCompressed));

            case EDemoCommands.DemSpawnGroups:
                return new DemoSpawnGroupsFrame(isCompressed, tick, size,
                    DemoFrameDecompressor.ParseMessage(CDemoSpawnGroups.Parser, buffer, isCompressed));

            case EDemoCommands.DemAnimationData:
                return new DemoAnimationDataFrame(isCompressed, tick, size,
                    DemoFrameDecompressor.ParseMessage(CDemoAnimationData.Parser, buffer, isCompressed));

            case EDemoCommands.DemAnimationHeader:
                return new DemoAnimationHeaderFrame(isCompressed, tick, size,
                    DemoFrameDecompressor.ParseMessage(CDemoAnimationHeader.Parser, buffer, isCompressed));

            case EDemoCommands.DemRecovery:
                return new DemoRecoveryFrame(isCompressed, tick, size,
                    DemoFrameDecompressor.ParseMessage(CDemoRecovery.Parser, buffer, isCompressed));

            default:
                throw new ArgumentOutOfRangeException(nameof(command), command, $"Unknown demo command: {command}");
        }
    }

    /// <summary>
    /// Parse network messages from a packet's bitstream data.
    /// </summary>
    protected virtual List<DemoNetworkMessage> ParseNetworkMessages(ReadOnlySpan<byte> data)
    {
        var messages = new List<DemoNetworkMessage>();
        var buffer = new BitBuffer(data);

        while (buffer.RemainingBytes > 0)
        {
            var msgType = (int)buffer.ReadUBitVar();
            var msgSize = (int)buffer.ReadUVarInt32();

            if (msgType == (int)NET_Messages.NetNop)
                continue;

            var rented = _bytePool.Rent(msgSize);
            try
            {
                var msgBuf = rented.AsSpan(0, msgSize);
                buffer.ReadBytes(msgBuf);

                var (name, body) = ParseSingleNetworkMessage(msgType, msgBuf);
                messages.Add(new DemoNetworkMessage(msgType, name, body, msgSize));
            }
            finally
            {
                _bytePool.Return(rented);
            }
        }

        return messages;
    }

    /// <summary>
    /// Parse a single network message from its type and buffer.
    /// Override this method to add game-specific network message parsing.
    /// </summary>
    /// <param name="msgType">The network message type ID.</param>
    /// <param name="buf">The raw message bytes.</param>
    /// <returns>A tuple of the message name and parsed protobuf body.</returns>
    protected virtual (string Name, IMessage? Body) ParseSingleNetworkMessage(int msgType, ReadOnlySpan<byte> buf)
    {
        // NET messages
        switch (msgType)
        {
            case (int)NET_Messages.NetSplitScreenUser:
                return (nameof(NET_Messages.NetSplitScreenUser), CNETMsg_SplitScreenUser.Parser.ParseFrom(buf));
            case (int)NET_Messages.NetTick:
                return (nameof(NET_Messages.NetTick), CNETMsg_Tick.Parser.ParseFrom(buf));
            case (int)NET_Messages.NetStringCmd:
                return (nameof(NET_Messages.NetStringCmd), CNETMsg_StringCmd.Parser.ParseFrom(buf));
            case (int)NET_Messages.NetSetConVar:
                return (nameof(NET_Messages.NetSetConVar), CNETMsg_SetConVar.Parser.ParseFrom(buf));
            case (int)NET_Messages.NetSignonState:
                return (nameof(NET_Messages.NetSignonState), CNETMsg_SignonState.Parser.ParseFrom(buf));
            case (int)NET_Messages.NetSpawnGroupLoad:
                return (nameof(NET_Messages.NetSpawnGroupLoad), CNETMsg_SpawnGroup_Load.Parser.ParseFrom(buf));
            case (int)NET_Messages.NetSpawnGroupManifestUpdate:
                return (nameof(NET_Messages.NetSpawnGroupManifestUpdate), CNETMsg_SpawnGroup_ManifestUpdate.Parser.ParseFrom(buf));
            case (int)NET_Messages.NetSpawnGroupSetCreationTick:
                return (nameof(NET_Messages.NetSpawnGroupSetCreationTick), CNETMsg_SpawnGroup_SetCreationTick.Parser.ParseFrom(buf));
            case (int)NET_Messages.NetSpawnGroupUnload:
                return (nameof(NET_Messages.NetSpawnGroupUnload), CNETMsg_SpawnGroup_Unload.Parser.ParseFrom(buf));
            case (int)NET_Messages.NetSpawnGroupLoadCompleted:
                return (nameof(NET_Messages.NetSpawnGroupLoadCompleted), CNETMsg_SpawnGroup_LoadCompleted.Parser.ParseFrom(buf));
            case (int)NET_Messages.NetDebugOverlay:
                return (nameof(NET_Messages.NetDebugOverlay), CNETMsg_DebugOverlay.Parser.ParseFrom(buf));
        }

        // SVC messages
        switch (msgType)
        {
            case (int)SVC_Messages.SvcServerInfo:
                return (nameof(SVC_Messages.SvcServerInfo), CSVCMsg_ServerInfo.Parser.ParseFrom(buf));
            case (int)SVC_Messages.SvcClassInfo:
                return (nameof(SVC_Messages.SvcClassInfo), CSVCMsg_ClassInfo.Parser.ParseFrom(buf));
            case (int)SVC_Messages.SvcSetPause:
                return (nameof(SVC_Messages.SvcSetPause), CSVCMsg_SetPause.Parser.ParseFrom(buf));
            case (int)SVC_Messages.SvcCreateStringTable:
                return (nameof(SVC_Messages.SvcCreateStringTable), CSVCMsg_CreateStringTable.Parser.ParseFrom(buf));
            case (int)SVC_Messages.SvcUpdateStringTable:
                return (nameof(SVC_Messages.SvcUpdateStringTable), CSVCMsg_UpdateStringTable.Parser.ParseFrom(buf));
            case (int)SVC_Messages.SvcVoiceInit:
                return (nameof(SVC_Messages.SvcVoiceInit), CSVCMsg_VoiceInit.Parser.ParseFrom(buf));
            case (int)SVC_Messages.SvcVoiceData:
                return (nameof(SVC_Messages.SvcVoiceData), CSVCMsg_VoiceData.Parser.ParseFrom(buf));
            case (int)SVC_Messages.SvcPrint:
                return (nameof(SVC_Messages.SvcPrint), CSVCMsg_Print.Parser.ParseFrom(buf));
            case (int)SVC_Messages.SvcClearAllStringTables:
                return (nameof(SVC_Messages.SvcClearAllStringTables), CSVCMsg_ClearAllStringTables.Parser.ParseFrom(buf));
            case (int)SVC_Messages.SvcPacketEntities:
                return (nameof(SVC_Messages.SvcPacketEntities), CSVCMsg_PacketEntities.Parser.ParseFrom(buf));
            case (int)SVC_Messages.SvcHltvstatus:
                return (nameof(SVC_Messages.SvcHltvstatus), CSVCMsg_HLTVStatus.Parser.ParseFrom(buf));
            case (int)SVC_Messages.SvcUserCmds:
                return (nameof(SVC_Messages.SvcUserCmds), CSVCMsg_UserCommands.Parser.ParseFrom(buf));
        }

        // Base game events
        switch (msgType)
        {
            case (int)EBaseGameEvents.GeSource1LegacyGameEventList:
                return (nameof(EBaseGameEvents.GeSource1LegacyGameEventList), CMsgSource1LegacyGameEventList.Parser.ParseFrom(buf));
            case (int)EBaseGameEvents.GeSource1LegacyGameEvent:
                return (nameof(EBaseGameEvents.GeSource1LegacyGameEvent), CMsgSource1LegacyGameEvent.Parser.ParseFrom(buf));
            case (int)EBaseGameEvents.GeSosStartSoundEvent:
                return (nameof(EBaseGameEvents.GeSosStartSoundEvent), CMsgSosStartSoundEvent.Parser.ParseFrom(buf));
            case (int)EBaseGameEvents.GeSosStopSoundEvent:
                return (nameof(EBaseGameEvents.GeSosStopSoundEvent), CMsgSosStopSoundEvent.Parser.ParseFrom(buf));
        }

        // Base user messages
        switch (msgType)
        {
            case (int)EBaseUserMessages.UmSayText:
                return (nameof(EBaseUserMessages.UmSayText), CUserMessageSayText.Parser.ParseFrom(buf));
            case (int)EBaseUserMessages.UmSayText2:
                return (nameof(EBaseUserMessages.UmSayText2), CUserMessageSayText2.Parser.ParseFrom(buf));
            case (int)EBaseUserMessages.UmTextMsg:
                return (nameof(EBaseUserMessages.UmTextMsg), CUserMessageTextMsg.Parser.ParseFrom(buf));
        }

        // Temp entity events
        switch (msgType)
        {
            case (int)ETEProtobufIds.TeEffectDispatchId:
                return (nameof(ETEProtobufIds.TeEffectDispatchId), CMsgTEEffectDispatch.Parser.ParseFrom(buf));
            case (int)ETEProtobufIds.TeDecalId:
                return (nameof(ETEProtobufIds.TeDecalId), CMsgTEDecal.Parser.ParseFrom(buf));
            case (int)ETEProtobufIds.TeWorldDecalId:
                return (nameof(ETEProtobufIds.TeWorldDecalId), CMsgTEWorldDecal.Parser.ParseFrom(buf));
            case (int)ETEProtobufIds.TeExplosionId:
                return (nameof(ETEProtobufIds.TeExplosionId), CMsgTEExplosion.Parser.ParseFrom(buf));
            case (int)ETEProtobufIds.TePhysicsPropId:
                return (nameof(ETEProtobufIds.TePhysicsPropId), CMsgTEPhysicsProp.Parser.ParseFrom(buf));
        }

        return ($"Unknown({msgType})", null);
    }
}
