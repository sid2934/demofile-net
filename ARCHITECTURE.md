# DemoFile.Net — In-Depth Technical Architecture

This document is an in-depth technical review of how DemoFile.Net parses Source 2 demo (`.dem`)
files.  It traces every layer of the parsing stack, from raw file bytes all the way to strongly
typed C# entity properties, paying particular attention to the bit-buffer, Huffman trees, and the
entity-update pipeline.

---

## Table of Contents

1. [Demo File Binary Format](#1-demo-file-binary-format)
2. [Reading Primitives — `ByteBuffer` and Streams](#2-reading-primitives--bytebuffer-and-streams)
3. [BitBuffer — Bit-Level Reader](#3-bitbuffer--bit-level-reader)
   - [Core Window Algorithm](#31-core-window-algorithm)
   - [ReadUBits](#32-readubits)
   - [ReadOneBit](#33-readonebit)
   - [ReadUBitVar — Variable-Width Unsigned Integers](#34-readubitvar--variable-width-unsigned-integers)
   - [ReadUVarInt32 / ReadUVarInt64 — Protobuf-Style Varints](#35-readuvarint32--readuvarint64--protobuf-style-varints)
   - [ReadVarInt32 / ReadVarInt64 — Zig-Zag Signed Integers](#36-readvarint32--readvarint64--zig-zag-signed-integers)
   - [Coordinate and Angle Encodings](#37-coordinate-and-angle-encodings)
   - [ReadUBitVarFieldPath](#38-readubitvarfieldpath)
4. [Demo Command Stream — `DemoFileReader`](#4-demo-command-stream--demofilereader)
   - [File Header](#41-file-header)
   - [Command Header Framing](#42-command-header-framing)
   - [DemoEvents Dispatch](#43-demoevents-dispatch)
5. [Protobuf Messages and Snappy Compression](#5-protobuf-messages-and-snappy-compression)
6. [DemoPacket — The Inner Message Stream](#6-demopacket--the-inner-message-stream)
7. [Huffman Trees — `HuffmanNode<T>`](#7-huffman-trees--huffmannodet)
   - [Construction](#71-construction)
   - [Traversal](#72-traversal)
8. [Field-Path Encoding — `FieldPathEncoding`](#8-field-path-encoding--fieldpathencoding)
   - [The Operation Table and Frequencies](#81-the-operation-table-and-frequencies)
   - [How the Huffman Tree is Applied](#82-how-the-huffman-tree-is-applied)
   - [Operation Semantics](#83-operation-semantics)
9. [FieldPath — Stack-Allocated Path Representation](#9-fieldpath--stack-allocated-path-representation)
10. [Entity System Overview](#10-entity-system-overview)
    - [Server Classes and Serializers](#101-server-classes-and-serializers)
    - [DemoSendTables — The Flattened Serializer](#102-demosendtables--the-flattened-serializer)
    - [DemoClassInfo — Binding Classes to Decoders](#103-democlassinfo--binding-classes-to-decoders)
    - [PacketEntities — The Entity Update Loop](#104-packetentities--the-entity-update-loop)
    - [ReadNewEntity — Field Path Decoding in Action](#105-readnewentity--field-path-decoding-in-action)
11. [Field Decoders — `FieldDecode`](#11-field-decoders--fielddecode)
    - [Primitive Types](#111-primitive-types)
    - [Floating-Point Decoders](#112-floating-point-decoders)
    - [Composite Types](#113-composite-types)
12. [Quantized Float Encoding](#12-quantized-float-encoding)
    - [Encoding Parameters](#121-encoding-parameters)
    - [Flag Semantics and Normalization](#122-flag-semantics-and-normalization)
    - [Decode Path](#123-decode-path)
13. [String Tables](#13-string-tables)
    - [Delta-Encoded Updates](#131-delta-encoded-updates)
    - [Key History Compression](#132-key-history-compression)
    - [Special Tables: instancebaseline and userinfo](#133-special-tables-instancebaseline-and-userinfo)
14. [Entity Baselines](#14-entity-baselines)
15. [Full-Packet Snapshots, Seeking, and Parallel Parsing](#15-full-packet-snapshots-seeking-and-parallel-parsing)
    - [CDemoFullPacket](#151-cdemofullpacket)
    - [SeekToTickAsync](#152-seektotickasync)
    - [ReadAllParallelAsync](#153-readallparallelasync)
16. [DecoderSet and the Generated SDK](#16-decoderset-and-the-generated-sdk)
17. [FallbackDecoder — Handling Unknown Fields](#17-fallbackdecoder--handling-unknown-fields)
18. [Performance Notes](#18-performance-notes)

---

## 1. Demo File Binary Format

A Source 2 demo file consists of a fixed 16-byte header followed by a linear stream of
**demo commands**.

```
Offset  Size  Description
------  ----  -----------
0       8     ASCII magic: "PBDEMS2\x00"
8       4     uint32 — byte offset from offset 8 to the CDemoFileInfo command
              (0 if file is incomplete / still being written)
12      4     reserved / padding (always 0)
```

After the 16-byte header the file is a sequence of back-to-back commands, each with its own
framing header.

---

## 2. Reading Primitives — `ByteBuffer` and Streams

`ByteBuffer` (`src/DemoFile/ByteBuffer.cs`) is a lightweight `ref struct` used for **byte-aligned**
reads at the outermost framing layer (e.g. reading the size prefix inside `CDemoSendTables`).
It holds a `ReadOnlySpan<byte>` and an integer `Position` cursor and exposes:

- `ReadByte()` — reads one byte and advances `Position` by 1.
- `ReadUVarInt32()` — reads a Protobuf-style unsigned varint (see §3.5) from the span.
- `ReadBytes(int length)` — returns a slice of the span and advances the cursor.

For the outer demo file stream, `DemoFileReader` works directly against a `Stream` (typically a
`FileStream` or `MemoryStream`).  The extension method `StreamExtensions.ReadUVarInt32` reads
varint-encoded command fields directly from the stream one byte at a time.

---

## 3. BitBuffer — Bit-Level Reader

`BitBuffer` (`src/DemoFile/BitBuffer.cs`) is the workhorse of the parser.  It is a `ref struct`
so it can never be heap-allocated or captured in closures, making it zero-overhead on the hot
path.

### 3.1 Core Window Algorithm

The buffer maintains a 32-bit sliding window:

```csharp
private int    _bitsRead  = 0;   // total bits consumed so far
private int    _bitsAvail = 0;   // bits currently valid in _buf
private uint   _buf       = 0;   // the current 32-bit window
private ReadOnlySpan<byte> _pointer; // remaining unread bytes
```

`FetchNext()` refills `_buf` from `_pointer`, reading up to 4 bytes at a time with
`MemoryMarshal.Read<uint>`.  When fewer than 4 bytes remain, a byte-by-byte loop fills the
`uint` with zeros in the upper bytes, avoiding a branch on the hot path.

> **Note**: A PGO regression in .NET 8 (`dotnet/runtime#95056`) prevents using `stackalloc`
> with `MemoryMarshal.Cast` safely here; the code explicitly uses an `unsafe` pointer cast to
> work around this.

### 3.2 ReadUBits

```csharp
public uint ReadUBits(int numBits)
```

Returns the next `numBits` bits as an unsigned integer.  Two paths:

1. **Fast path** (`_bitsAvail >= numBits`): mask `_buf` with `BitMask[numBits]`, shift `_buf`
   right by `numBits`, decrement `_bitsAvail`.  `BitMask` is a static 33-element precomputed
   array where `BitMask[n] = (1u << n) - 1` for `n < 32`, and `uint.MaxValue` for `n == 32`.

2. **Slow path** (request spans two windows): capture the low `_bitsAvail` bits from `_buf`,
   call `UpdateBuffer()` to load the next window, then extract the remaining bits and OR them
   into the high position of the result.

### 3.3 ReadOneBit

Marked `[MethodImpl(AggressiveInlining)]` for maximum inlining.  Directly tests the low bit
of `_buf`, decrements `_bitsAvail`, and shifts (or fetches the next window).  This is called
millions of times per demo in the Huffman traversal, so its performance is critical.

### 3.4 ReadUBitVar — Variable-Width Unsigned Integers

`ReadUBitVar` is Source's own compact integer encoding used heavily inside `CDemoPacket`
messages.  It reads 6 bits first:

```
Bits [5:4]  Meaning
----------  -------
0b00        Use bits [3:0]                 →  4 bits total
0b01        Use bits [3:0] + 4 more bits   →  8 bits total
0b10        Use bits [3:0] + 8 more bits   → 12 bits total
0b11        Use bits [3:0] + 28 more bits  → 32 bits total
```

The lower 4 bits of the initial read always form bits [3:0] of the result.  The two selector
bits (bits 4 and 5) determine how many additional bits are appended in bits [7:4] and above.
This trades a small overhead for large integers in exchange for 2-bit savings on integers 0–15.

### 3.5 ReadUVarInt32 / ReadUVarInt64 — Protobuf-Style Varints

Standard Protobuf varint encoding: each byte contributes 7 data bits; bit 7 signals
continuation.  For 32-bit values up to 5 bytes are read; for 64-bit, up to 10 bytes.

```
Byte 0: data bits [6:0], continuation bit = bit 7
Byte 1: data bits [13:7], continuation bit = bit 7
...
```

These are used extensively in the `CDemoPacket` inner stream for field sizes, entity counts,
server class IDs, and so on.

### 3.6 ReadVarInt32 / ReadVarInt64 — Zig-Zag Signed Integers

Converts an unsigned varint to a signed value using the standard Protobuf zig-zag mapping:

```
unsigned → signed
0  →  0
1  → -1
2  →  1
3  → -2
4  →  2
...
```

Formula: `(int)(result >> 1) ^ -(int)(result & 1)`.

### 3.7 Coordinate and Angle Encodings

Several specialised decode methods are built on top of `ReadUBits`:

| Method              | Description |
|---------------------|-------------|
| `ReadCoord()`       | Reads a world-space coordinate with 14-bit integer part and 5-bit fractional part.  Has-int and has-fract prefix bits and a sign bit allow compact encoding of common values. |
| `ReadCoordPrecise()`| Reads a 20-bit angle remapped from [0, 2^20) to [-180, +180) degrees. |
| `ReadAngle(bits)`   | Reads a fixed-width angle value mapped to [0°, 360°). |
| `ReadNormal()`      | Reads a sign bit and 11-bit magnitude, producing a normalised float in [-1, 1]. |
| `Read3BitNormal()`  | Reads an optional X normal, optional Y normal, and reconstructs Z from `sqrt(1 – x² – y²)` with a sign bit. |
| `ReadFloat()`       | Reads 32 raw bits and reinterprets them as IEEE 754 `float` via an `unsafe` pointer cast. |

### 3.8 ReadUBitVarFieldPath

A specialised variable-width integer used only inside field-path operations.  It uses unary
prefix bits to choose one of five widths:

```
Prefix   Width   Range
-------  -----   -----
1        2 bits  0–3
01       4 bits  0–15
001     10 bits  0–1023
0001    17 bits  0–131071
0000    31 bits  0–2147483647
```

This is distinct from `ReadUBitVar` because field path deltas are usually small positive numbers
and this encoding is optimised accordingly.

---

## 4. Demo Command Stream — `DemoFileReader`

`DemoFileReader<TGameParser>` (`src/DemoFile/DemoFileReader.cs`) drives the top-level parse
loop.  It is generic over any `DemoParser<TGameParser>` subclass, which provides game-specific
entity factories and decoders.

### 4.1 File Header

`StartReadingAsync` reads the 16-byte file header, verifies the magic, and optionally seeks to
the end of file to read `CDemoFileInfo` (which provides the total tick count for progress
reporting).  If the file is incomplete (the size field is zero), it scans the command stream
to discover the last tick instead.

### 4.2 Command Header Framing

`ReadCommandHeader()` reads three consecutive `UVarInt32` values from the stream:

```
UVarInt32  command  — EDemoCommands enum value, optionally OR'd with DemIsCompressed (0x40)
UVarInt32  tick     — demo tick number at which this command occurs
UVarInt32  size     — byte length of the following payload
```

The `DemIsCompressed` flag in the command word indicates that the payload is Snappy-compressed
and must be decompressed before Protobuf parsing.

### 4.3 DemoEvents Dispatch

After reading `size` bytes into a rented `ArrayPool<byte>` buffer, the command and buffer are
handed to `DemoEvents.ReadDemoCommand`.  This is a large `switch` over `EDemoCommands` that:

1. Decompresses with Snappy if the compressed flag was set.
2. Calls the appropriate `MessageParser<T>.ParseFrom(buffer)` to deserialise the Protobuf
   message.
3. Invokes the corresponding `Action<T>` delegate, e.g. `DemoPacket`, `DemoClassInfo`,
   `DemoSendTables`, etc.

The parser uses `struct` events (fields on `DemoEvents` / `PacketEvents` etc.) rather than
`event` keywords to allow zero-allocation dispatch — if no handler is registered, the Protobuf
parse is skipped entirely.

---

## 5. Protobuf Messages and Snappy Compression

All higher-level demo structures are encoded as Protobuf (proto3) messages, defined in
`src/DemoFile/Protobufs/`.  The generated C# code uses `Google.Protobuf`.  Key message
types include:

| Type                          | Purpose |
|-------------------------------|---------|
| `CDemoFileHeader`             | Server / map information |
| `CDemoFileInfo`               | Playback tick count |
| `CDemoPacket` / `CDemoSignonPacket` | Container for inner net messages |
| `CDemoSendTables`             | Flattened serializer schema |
| `CDemoClassInfo`              | Network class name → class ID mapping |
| `CDemoStringTables`           | Full string table snapshot (used in full packets) |
| `CDemoFullPacket`             | Combined string table delta + entity snapshot |
| `CSVCMsg_PacketEntities`      | Delta-encoded entity updates |
| `CSVCMsg_CreateStringTable`   | Initial string table creation |
| `CSVCMsg_UpdateStringTable`   | String table update |
| `CSVCMsg_FlattenedSerializer` | Serializer schema (embedded in CDemoSendTables) |

Demo commands that are large (e.g. `CDemoFullPacket`) may be Snappy-compressed.
`DemoEvents.ReadDemoCommandCore` handles decompression transparently using `Snappier`.

---

## 6. DemoPacket — The Inner Message Stream

`CDemoPacket.Data` is not just one message — it is a tightly packed binary stream of *net
messages*.  `DemoParser.OnDemoPacket` processes this with a `BitBuffer`:

```
Loop:
  UBitVar   msgType  — NET_Messages / SVC_Messages / GE_Source1LegacyGameEvents enum
  UVarInt32 size     — byte length of this message
  bytes[size]        — the message payload (another Protobuf message)
```

`NET_Messages.NetNop` messages are silently skipped.

Messages are not dispatched immediately.  Instead they are enqueued into a priority queue
(`_packetQueue`) so that `CSVCMsg_PacketEntities` (entity updates) are always processed
**before** game events.  This guarantees that event handlers always see up-to-date entity
state.  The priority is defined by `QueuedPacket.GetPriority(msgType)`.

Each dequeued message is routed through four dispatch chains in order:
1. `_packetEvents.ParseNetMessage` — standard SVC/NET messages (entity updates, string tables, etc.)
2. `_baseGameEvents.ParseGameEvent` — game events (player_death, round_end, etc.)
3. `_baseUserMessageEvents.ParseUserMessage` — user messages
4. `_tempEntityEvents.ParseNetMessage` — temp entity messages
5. `ParseNetMessage` (abstract) — game-specific messages

---

## 7. Huffman Trees — `HuffmanNode<T>`

`HuffmanNode<T>` (`src/DemoFile/HuffmanNode.cs`) provides a generic Huffman tree used for
field-path encoding.

### 7.1 Construction

`HuffmanNode<T>.Build` takes an `IEnumerable<KeyValuePair<T, int>>` of symbol → frequency
pairs and uses a min-priority queue (`PriorityQueue<HuffmanNode<T>, NodePriority>`) to build
a canonical Huffman tree:

1. Each symbol becomes a leaf node, enqueued with priority `(frequency, originalIndex)`.
2. While more than one node remains:
   a. Dequeue the two lowest-priority nodes `left` and `right`.
   b. Create an internal node with `Frequency = left.Frequency + right.Frequency`.
   c. Enqueue the internal node with `priority = (combinedFrequency, nextIndex++)`.
3. The last remaining node is the root.

`NodePriority` sorts primarily by `Weight` (ascending) and uses `Value` (the original index)
as a tiebreaker (descending — later symbols win ties).  This ensures that the tree topology
matches the reference implementation used by Valve's engine, which is important for
interoperability.

The minimum frequency is clamped to 1, ensuring that zero-frequency symbols still appear in
the tree (they are needed for correctness even if rare).

### 7.2 Traversal

Tree traversal is deliberately simple: read one bit at a time from a `BitBuffer`, navigate
left (0) or right (1) until a leaf node is reached, then return its `Symbol`.

```csharp
var node = HuffmanRoot;
for (;;)
{
    var next = buffer.ReadOneBit() ? node.Right : node.Left;
    if (next.Symbol is {} symbol) return symbol;
    node = next;
}
```

This is the hot path during entity decoding.  The benchmark comment in `FieldPathEncoding.cs`
shows that building a prefix-lookup table was actually *slower* due to the overhead of peeking
multiple bits — one bit at a time wins in practice due to cache behaviour and branch
predictability.

---

## 8. Field-Path Encoding — `FieldPathEncoding`

`FieldPathEncoding` (`src/DemoFile/FieldPathEncoding.cs`) defines the 40 field-path operations
and builds the Huffman tree used to decode them.

### 8.1 The Operation Table and Frequencies

Each `FieldPathEncodingOp` has a name, a frequency (observed in real Valve demos), and a
`FieldPathReader` delegate that mutates a `FieldPath`.  The frequencies determine the Huffman
code: more common operations get shorter codes.

The top five by frequency are:

| Operation                              | Frequency |
|----------------------------------------|-----------|
| `FieldPathEncodeFinish`                | 25 474    |
| `PlusOne`                              | 36 271    |
| `PushOneLeftDeltaNRightNonZeroPack6Bits` | 10 530  |
| `PlusTwo`                              | 10 334    |
| `PopAllButOnePlusOne`                  | 1 837     |

`FieldPathEncodeFinish` is a sentinel with a `null` reader; when the decoder sees it, all field
paths for the current entity update have been read.

Operations with frequency 0 still appear in the tree (Valve may have reserved them for future
use or removed them from the hot path).

### 8.2 How the Huffman Tree is Applied

`ReadFieldPathOp` traverses `HuffmanRoot` with `BitBuffer.ReadOneBit` and returns the
`FieldPathEncodingOp` at the leaf.  The caller (`ReadNewEntity`) invokes
`op.Reader(ref buffer, ref fieldPath)` to mutate the current field path, then stores the
resulting path.

### 8.3 Operation Semantics

Field paths are hierarchical — they describe a path through nested structs on a server class
(e.g. `[3, 1, 0]` means: field index 3, then inside that struct field index 1, then inside
that field index 0).  The encoding ops are designed so that consecutive field accesses in
serialisation order can be expressed with the fewest bits.

Key categories:

**Increment the leaf index** — most common; the previous path is reused and only the last
component changes:
- `PlusOne` — `path[^1] += 1`
- `PlusTwo` — `path[^1] += 2`
- `PlusThree` — `path[^1] += 3`
- `PlusFour` — `path[^1] += 4`
- `PlusN` — `path[^1] += ReadUBitVarFieldPath() + 5`

**Push one level deeper** (increase depth by 1) — used when navigating into a nested field or
array:
- `PushOneLeftDeltaZeroRightZero` — increment parent by 0, push child index 0
- `PushOneLeftDeltaOneRightZero` — increment parent by 1, push child index 0
- `PushOneLeftDeltaOneRightNonZero` — increment parent by 1, push a non-zero child index
- `PushOneLeftDeltaNRightNonZeroPack6Bits` — pack both deltas into 3 bits each (common case
  for small structs)

**Pop levels** — used when moving back up to a shallower level:
- `PopAllButOnePlusOne` — collapse the path to depth 1, then `path[0] += 1` (very common when
  iterating top-level fields)
- `PopAllButOnePlusNPack3Bits/6Bits` — same but read a small packed delta
- `PopOnePlusOne`, `PopOnePlusN` — pop one level, then adjust the new leaf

**Non-topological** — arbitrary modifications to any component of the path (rare):
- `NonTopoComplex` — for each component, conditionally add a signed VarInt delta
- `NonTopoPenultimatePlusOne` — `path[^2] += 1`
- `PushNAndNonTopological` — combine push with arbitrary delta for every existing component

---

## 9. FieldPath — Stack-Allocated Path Representation

`FieldPath` (`src/DemoFile/FieldPath.cs`) is a fixed-capacity struct that holds up to 7
`int` components (enough for all known Source 2 nesting depths) without any heap allocation.

```csharp
internal struct FieldPath : IReadOnlyList<int>
{
    private int _path0, _path1, _path2, _path3, _path4, _path5, _path6;
    private int _size;
    ...
}
```

The default value is `{-1}` (a single component of -1), which serves as a sentinel for the
"before any field" state.

`Add(item)` switches on `_size` to write to the correct named field — this compiles to a
simple jump table with no bounds checks on the fast path.

`AsSpan()` uses `MemoryMarshal.CreateReadOnlySpan(ref _path0, _size)` to produce a
zero-copy span over the inline fields, which is how the decoder receives the path.

During `ReadNewEntity`, up to 512 paths are accumulated on the stack with `stackalloc
FieldPath[512]`.  If this limit is exceeded (very rare), the array is promoted to the heap.

---

## 10. Entity System Overview

### 10.1 Server Classes and Serializers

**Server classes** (`ServerClass<TGameParser>`) bind a network class name (e.g.
`CCSPlayerPawn`) to a class ID and an entity factory delegate.  The class ID is a compact
integer assigned by the server; it fits in `_serverClassBits` bits, where
`_serverClassBits = floor(log2(maxClasses)) + 1`.

**Serializers** describe the network schema of a class — essentially a list of fields
(`SerializableField[]`), where each field carries:
- `VarName` — the field name (e.g. `m_vecOrigin`)
- `VarType` — the declared C++ type as a string (e.g. `Vector`, `float32`, `CHandle`)
- `FieldEncodingInfo` — encoder name, bit count, range limits, encode flags
- `FieldSerializerKey` — for composite (struct) fields, the key of their nested serializer
- `PolymorphicTypes` — for polymorphic pointer fields, the possible concrete types

### 10.2 DemoSendTables — The Flattened Serializer

`CDemoSendTables` arrives once near the start of the demo.  Its `Data` field contains a
size-prefixed `CSVCMsg_FlattenedSerializer` Protobuf message (decoded via `ByteBuffer`).

The flattened serializer has a string-interning symbol table (`msg.Symbols`).  All string
references in fields are indices into this table, saving space and allocation.  The parser
reconstructs `SerializableField` objects and groups them into `Serializer` instances keyed
by `SerializerKey(name, version)`.

### 10.3 DemoClassInfo — Binding Classes to Decoders

`CDemoClassInfo` arrives after `CDemoSendTables`.  For each network class, the parser:

1. Looks up the class name in the game-specific `EntityFactories` dictionary.
2. Calls `decoderSet.GetDecoder(className)` to obtain a compiled `SendNodeDecoder<object>`.
3. Stores a `ServerClass<TGameParser>` that wraps both the factory and the decoder.

`DecoderSet.CreateDecoder<T>` compiles a decoder from a `Serializer` by iterating over its
fields and calling the game-specific `SendNodeDecoderFactory<T>` for each one.  The result is
a closure `(T instance, ReadOnlySpan<int> path, ref BitBuffer buffer) => ...` that dispatches
on `path[0]` to call the per-field decoder.

### 10.4 PacketEntities — The Entity Update Loop

`CSVCMsg_PacketEntities` is the most computationally intensive message.  It contains a
`BitBuffer`-encoded sequence of entity updates with the following per-update header:

```
UBitVar  entityIndexDelta  — how many indices to skip from last entity
2 bits   updateType        — 0b00: delta update, 0b01: leave PVS, 0b10: enter PVS, 0b11: delete
```

For `LegacyIsDelta == false` (full update), any entity slot that is not explicitly updated
is deleted.

For **enter PVS** (`updateType == 0b10`):
1. Read `_serverClassBits` bits → class ID.
2. Read 17 bits → serial number.
3. Read a `UVarInt32` → unknown (spawn-group handle?).
4. Instantiate the entity via `serverClass.EntityFactory(context)`.
5. Apply the entity baseline (if available — see §14).
6. Call `ReadNewEntity` to apply the current update delta.

For **delta update** (`updateType == 0b00`):
1. Retrieve the existing entity from `_entities[entityIndex]`.
2. Call `ReadNewEntity` to apply the delta.

After all entries are processed, create/post-update events are fired in two separate passes to
ensure that all entity handles are valid before any event handler runs.

### 10.5 ReadNewEntity — Field Path Decoding in Action

```csharp
private static void ReadNewEntity(ref BitBuffer buffer, CEntityInstance<TGameParser> entity)
{
    Span<FieldPath> fieldPaths = stackalloc FieldPath[512];
    var fp = FieldPath.Default;
    var index = 0;

    // Phase 1: decode all field paths
    while (FieldPathEncoding.ReadFieldPathOp(ref buffer) is { Reader: {} reader })
    {
        reader.Invoke(ref buffer, ref fp);
        fieldPaths[index++] = fp;
    }

    // Phase 2: decode field values in path order
    for (var idx = 0; idx < index; idx++)
    {
        entity.ReadField(fieldPaths[idx].AsSpan(), ref buffer);
    }
}
```

Phase 1 uses the Huffman tree to read one operation at a time, mutating the running `FieldPath`
until `FieldPathEncodeFinish` (null reader) is encountered.  Phase 2 walks the collected paths
and invokes the entity's compiled decoder for each one.  The decoder dispatches on `path[0]`
then recursively on `path[1]`, etc., until it reaches a leaf decoder that reads bits from the
buffer and writes the decoded value into the entity property.

---

## 11. Field Decoders — `FieldDecode`

`FieldDecode` (`src/DemoFile/FieldDecode.cs`) is a static class of factory methods.  Each
factory inspects `FieldEncodingInfo` and returns a `FieldDecoder<T>` delegate.

### 11.1 Primitive Types

| C# type  | Read method                                  |
|----------|----------------------------------------------|
| `bool`   | `ReadOneBit()`                               |
| `byte`   | `(byte)ReadUVarInt32()`                      |
| `sbyte`  | `(sbyte)ReadVarInt32()`                      |
| `int`    | `ReadVarInt32()`                             |
| `uint`   | `ReadUVarInt32()`                            |
| `short`  | `(short)ReadVarInt32()`                      |
| `ushort` | `(ushort)ReadUVarInt32()`                    |
| `ulong`  | `ReadUVarInt64()` or fixed 8-byte LE read    |
| `string` | Null-terminated UTF-8 via `ReadStringUtf8()` |

`ReadStringUtf8` uses a `stackalloc byte[260]` initial buffer and doubles to a heap array if
the string is longer, then returns `Encoding.UTF8.GetString`.

For `ulong` with encoder `"fixed64"`, eight raw bytes are read in little-endian order using
`BinaryPrimitives.ReadUInt64LittleEndian`.

### 11.2 Floating-Point Decoders

Float decoding is selected by `FieldEncodingInfo.VarEncoder`:

| VarEncoder     | Decoding strategy |
|----------------|-------------------|
| `"coord"`      | `ReadCoord()` — variable-width world coordinate |
| `"simtime"`    | `DecodeSimulationTime` — reads a `UVarInt32` game-tick count, converts to seconds |
| `"runetime"`   | Reads only 4 bits and reinterprets as a `float` |
| `null` (default) with BitCount 0 or ≥ 32 | `ReadFloat()` — raw 32-bit IEEE 754 |
| `null` with 0 < BitCount < 32 | `QuantizedFloatEncoding.Decode` (see §12) |

### 11.3 Composite Types

| Type           | Decoder |
|----------------|---------|
| `Vector`       | 3× float decoder, or `Read3BitNormal()` if encoder is `"normal"` |
| `Vector2D`     | 2× float decoder |
| `Vector4D`     | 4× float decoder |
| `QAngle`       | Depends on encoder: fixed-bit angles, `"qangle_pitch_yaw"` (2 angles, roll=0), `"qangle_precise"` (conditional `ReadCoordPrecise()` per component) |
| `Color`        | `ReadUVarInt32()` → RGBA → `System.Drawing.Color` (note: byte order swap) |
| `CHandle<T>`   | `ReadUVarInt64()` (packs entity index + serial) |
| `CStrongHandle<T>` | `ReadUVarInt64()` |
| `GameTime`     | `DecodeFloatNoscale` → `new GameTime(float)` |
| `GameTick`     | `ReadUVarInt32()` → `new GameTick(uint)` |
| Enums          | `ReadUVarInt64()` cast to enum |

---

## 12. Quantized Float Encoding

`QuantizedFloatEncoding` (`src/DemoFile/QuantizedFloatEncoding.cs`) encodes floats over a
user-specified `[Low, High]` range into a fixed number of bits.

### 12.1 Encoding Parameters

From `FieldEncodingInfo`:
- `BitCount` — number of bits for the quantised value.
- `LowValue`, `HighValue` — the closed range `[Low, High]`.
- `EncodeFlags` — combination of `RoundDown`, `RoundUp`, `EncodeZero`, `EncodeIntegers`.

`steps = 1 << BitCount` — the number of discrete quantisation levels.

### 12.2 Flag Semantics and Normalization

`ValidateFlags` enforces mutual exclusivity and resolves conflicts:

- **`EncodeIntegers`** — override other flags; adjusts `BitCount` and the range to guarantee
  that all integer values in `[Low, High]` are exactly representable.
- **`RoundDown`** — the encoded range `[Low, High)` is open on the high end.  A sentinel bit
  allows encoding `High` exactly.
- **`RoundUp`** — the encoded range `(Low, High]` is open on the low end.  A sentinel bit
  allows encoding `Low` exactly.
- **`EncodeZero`** — a sentinel bit allows encoding exactly 0.0 regardless of quantisation
  granularity.

`CalculateHighLowMul` computes `highLowMul = (steps - 1) / range`, with precision-loss
fallback multipliers `[0.9999, 0.99, 0.9, 0.8, 0.7]` to keep `highLowMul * range <= steps - 1`.

### 12.3 Decode Path

```csharp
public float Decode(ref BitBuffer buffer)
{
    if (Flags.HasFlag(RoundDown)  && buffer.ReadOneBit()) return Low;
    if (Flags.HasFlag(RoundUp)    && buffer.ReadOneBit()) return High;
    if (Flags.HasFlag(EncodeZero) && buffer.ReadOneBit()) return 0.0f;
    return Low + (High - Low) * buffer.ReadUBits(BitCount) * DecMul;
}
```

The optional sentinel bits appear **before** the quantised value, so the most common case
(no special value) still reads `BitCount` bits from the buffer with no extra cost beyond the
bit-prefix checks.

`DecMul = 1.0f / (steps - 1)` maps the integer code back to a normalised `[0, 1]` ratio.

---

## 13. String Tables

String tables (`StringTable`, `src/DemoFile/StringTable.cs`) are server-maintained key/value
stores.  Each entry has a string key and optional binary user-data.  Tables are created once
(via `CSVCMsg_CreateStringTable`) and then updated incrementally (via
`CSVCMsg_UpdateStringTable`).

### 13.1 Delta-Encoded Updates

`StringTable.ReadUpdate(ReadOnlySpan<byte> stringData, int entries)` reads a `BitBuffer`-
encoded sequence of `entries` updates:

```
For each entry:
  1 bit   consecutive  — if 1, index = previousIndex + 1; if 0, read UVarInt32 skip + 1
  1 bit   hasKey       — does this entry carry a key string?
  1 bit   hasValue     — does this entry carry user data?
```

### 13.2 Key History Compression

When `hasKey == 1`, an additional bit selects between:

- **Direct**: read a null-terminated UTF-8 string from the buffer.
- **History-backed**: read 5-bit `position` and 5-bit `length`, referring to the `position`-th
  most recently seen key.  The prefix of that historical key (up to `length` bytes) is
  concatenated with a further null-terminated suffix read from the buffer.

The history window is 32 entries.  This prefix sharing is very effective for table names that
share common path prefixes (e.g. `materials/models/weapon_...`).

### 13.3 Special Tables: instancebaseline and userinfo

`instancebaseline` stores per-server-class default entity states as protobuf-encoded binary
blobs.  Keys are decimal class IDs (or `classId:alternateBaseline` for alternate baselines).
`OnInstanceBaselineUpdate` parses the key and stores the blob in `_instanceBaselines`, indexed
by `BaselineKey`.

`userinfo` stores `CMsgPlayerInfo` Protobuf messages for each player slot.
`OnUserInfoUpdate` deserialises these and stores them in `_playerInfos`.

User-data size can be fixed-size or variable.  Variable-size data reads either a `UBitVar` or
a 17-bit field (depending on `UsingVarintBitcounts`) to get the byte count, then reads that
many bytes from the buffer.  A flag allows individual entries to be Snappy-compressed.

---

## 14. Entity Baselines

Entity baselines provide the initial ("default") state of an entity when it enters the PVS
(Potentially Visible Set).  There are two sources:

1. **Instance baselines** — stored in the `instancebaseline` string table.  These are
   class-level defaults sent by the server once and reused for every entity of that class.

2. **Entity baselines** — stored in `_entityBaselines[0]` and `_entityBaselines[1]`.  When
   `CSVCMsg_PacketEntities.UpdateBaseline == true`, the parser clones the `BitBuffer` position
   before applying the current update delta and saves the raw bits as a new baseline layer.

Entity baselines are represented as `ImmutableList<ReadOnlyMemory<byte>>` (multiple delta
layers applied in order), allowing incremental baseline updates without reprocessing the full
baseline on every enter-PVS event.

The two baseline arrays (`_entityBaselines[0/1]`) implement a double-buffer scheme controlled
by `msg.Baseline`: the "other" baseline index is `1 - msg.Baseline`.

When an entity enters PVS:
1. If an entity baseline exists with the correct class ID, apply all its layers via
   `ReadNewEntity`.
2. Otherwise, fall back to the instance baseline.
3. Then apply the current delta from the packet stream.

---

## 15. Full-Packet Snapshots, Seeking, and Parallel Parsing

### 15.1 CDemoFullPacket

`CDemoFullPacket` is written into the demo stream every **3,840 ticks** (≈ 60 seconds at 64
ticks/second).  Each full packet contains:

- `StringTable` — a `CDemoStringTables` snapshot of only the string tables that have changed
  since the last full packet.
- `Packet` — a `CDemoPacket`-equivalent containing a complete entity state snapshot
  (i.e., a full rather than delta `CSVCMsg_PacketEntities`).

`OnDemoFullPacket` records the stream position and a snapshot of all string table entries as a
`FullPacketRecord` in `_fullPackets` (kept sorted by tick).

### 15.2 SeekToTickAsync

`SeekToTickAsync(DemoTick targetTick)` implements bidirectional seeking:

1. Binary-search `_fullPackets` for the last full packet at or before `targetTick`.
2. If seeking backwards or the target is far ahead, `RestoreFullPacket` teleports the stream
   position to the full packet and calls `RestoreStringTables` to rehydrate all string tables
   from the snapshot.
3. Parse forward tick-by-tick until `CurrentDemoTick >= targetTick`, skipping non-FullPacket
   commands during the fast-forward phase (`SkipToFullPacketTickAsync`).

The entire seek happens inside a `SeekScope` that suppresses tick-timer callbacks, preventing
spurious event firing during the seek.

### 15.3 ReadAllParallelAsync

`DemoFileReader.ReadAllParallelAsync` enables multi-core demo parsing by exploiting full
packets as independent restart points:

1. A **scan pass** reads the entire file, processing only `CDemoFullPacket` commands to
   populate `_fullPackets`.  All other commands are skipped.
2. The full packets are divided into N groups (one per logical CPU core, capped at
   `Environment.ProcessorCount`).
3. For each group, a new `DemoParser` and `DemoFileReader` are created, the stream is
   positioned at the group's first full packet, string tables are restored from the snapshot,
   and the section is parsed on a `Task.Run` thread.

Because each section starts from a complete entity snapshot (the full packet), sections are
fully independent — they share no mutable state.  The caller receives a
`Task<IReadOnlyList<TResult>>` whose entries correspond to each parsed section, plus the
initial (pre-first-full-packet) section.

---

## 16. DecoderSet and the Generated SDK

`DecoderSet` (`src/DemoFile/Sdk/DecoderSet.cs`) is an abstract class that bridges the generic
parsing engine with game-specific C# entity classes.  Game-specific subclasses (generated by
`DemoFile.GameStaticGen`) implement:

- `TryGetDecoderByName` — maps a class name string to a `(Type, SendNodeDecoder<object>)` pair.
- `TryCreateFallbackDecoder` — attempts to create a decoder for an unknown field type.
- `GetFactory<T>` — returns a `SendNodeDecoderFactory<T>` that, given a `SerializableField`,
  produces a `SendNodeDecoder<T>` for that field.

`SendNodeDecoder<T>` is a delegate:
```csharp
delegate void SendNodeDecoder<T>(T instance, ReadOnlySpan<int> path, ref BitBuffer buffer);
```

For each generated entity class (e.g. `CCSPlayerPawn`), the SDK generator emits a `ReadField`
method that switches on `path[0]` to dispatch to the appropriate property setter, recursively
calling into nested decoders for struct-type fields.

`DecoderSet` caches compiled decoders in a `Dictionary<SerializerKey, object>` with a null
sentinel to detect circular references.

---

## 17. FallbackDecoder — Handling Unknown Fields

`FallbackDecoder` (`src/DemoFile/FallbackDecoder.cs`) is used when the generated SDK does not
know about a field type.  It tries heuristics in order:

1. **Arrays**: delegate to an inner decoder for the element type.
2. **Pointers**: read one bit; if 0 (null), do nothing; if 1, throw (unsupported).
3. **Vectors** (`CNetworkUtlVectorBase`, etc.): read a new size varint for path depth 1,
   delegate to element decoder for deeper paths.
4. **Known primitive types**: explicit handlers for `Vector`, `float32`, `bool`, `Color`,
   integer types, string types, etc.
5. **Enum heuristics**: if the type name starts with `E` followed by an uppercase letter, or
   the field name starts with `m_e` followed by an uppercase letter, read as `UVarInt64`.
6. **Fallback class decoder**: attempt to look up the type name in the decoder set and use its
   decoder with a dummy instance.

---

## 18. Performance Notes

Several design decisions are made explicitly for performance:

- **`ref struct` everywhere**: `BitBuffer` and `ByteBuffer` are `ref struct` types, preventing
  heap allocation and enabling the JIT to keep them in registers.
- **`stackalloc` for field paths**: up to 512 `FieldPath` values are stack-allocated per
  entity update, eliminating the most frequent allocation in the hot path.
- **`ArrayPool<byte>`**: all temporary byte buffers (packet payloads, string table data, etc.)
  are rented from pools to minimise GC pressure.
- **Priority queue packet ordering**: using a `PriorityQueue` to re-order inner net messages
  avoids repeated allocations compared to sorting a list.
- **Huffman bit-by-bit traversal**: despite the apparently naive approach, the benchmark
  (quoted in `FieldPathEncoding.cs`) shows that a pre-built lookup table is actually slower
  due to cache and branch effects.
- **`ImmutableList` for baselines**: baselines accumulate as a chain of delta layers using
  `ImmutableList.Add`, which produces a linked structure sharing all previous layers with no
  copying.
- **`[MethodImpl(AggressiveInlining)]`**: applied to `ReadOneBit` and `UpdateBuffer` which
  are called millions of times per demo.
- **`[SkipLocalsInit]`**: applied to `ReadNewEntity` to suppress zero-initialisation of the
  stack-allocated `FieldPath` array.
- **Parallel parsing**: `ReadAllParallelAsync` achieves near-linear scaling with CPU cores by
  exploiting full-packet restart points as independent section boundaries.
- **Benchmark results** (M1 MacBook Pro): single-threaded ~1.3 s, multi-threaded ~540 ms for
  a full competitive match (~1 hour of game time).
