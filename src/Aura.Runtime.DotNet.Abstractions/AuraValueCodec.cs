using System.Buffers.Binary;
using System.Text;

namespace Aura.Runtime.DotNet;

/// <summary>Encodes and decodes canonical Bridge Value v1 bytes.</summary>
public static class AuraValueCodec
{
    private const int MaxDepth = 31;
    private const int MaxStringBytes = 1024 * 1024;
    private const int MaxByteBytes = 16 * 1024 * 1024;
    private const int MaxContainerEntries = 1024;
    private const int MaxTotalContentBytes = 16 * 1024 * 1024;
    private const int MaxTotalValues = 65_536;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    /// <summary>Encodes one value to canonical Bridge Value v1 bytes.</summary>
    public static byte[] Encode(AuraValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        using var output = new MemoryStream();
        var encoder = new Encoder(output);
        encoder.WriteValue(value, 0);
        return output.ToArray();
    }

    /// <summary>Decodes one complete canonical Bridge Value v1 payload.</summary>
    public static AuraValue Decode(ReadOnlySpan<byte> input)
    {
        var decoder = new Decoder(input.ToArray());
        var value = decoder.ReadValue(0);
        if (!decoder.AtEnd)
        {
            throw new InvalidDataException("Bridge value contains trailing bytes.");
        }

        return value;
    }

    private sealed class Encoder(Stream output)
    {
        private int contentBytes;
        private int values;

        public void WriteValue(AuraValue value, int depth)
        {
            if (depth > MaxDepth || values >= MaxTotalValues)
            {
                throw new ArgumentException("Bridge value exceeds structural limits.", nameof(value));
            }

            values++;
            output.WriteByte(0x92);
            output.WriteByte((byte)value.Kind);
            switch (value.Kind)
            {
                case AuraValueKind.Null:
                    output.WriteByte(0xc0);
                    break;
                case AuraValueKind.Boolean:
                    output.WriteByte(value.AsBoolean() ? (byte)0xc3 : (byte)0xc2);
                    break;
                case AuraValueKind.Integer:
                    output.WriteByte(0xd3);
                    WriteInt64(value.AsInteger());
                    break;
                case AuraValueKind.Double:
                    output.WriteByte(0xcb);
                    WriteInt64(BitConverter.DoubleToInt64Bits(value.AsDouble()));
                    break;
                case AuraValueKind.String:
                    WriteString(value.AsString(), MaxStringBytes, true);
                    break;
                case AuraValueKind.Bytes:
                    var bytes = value.AsBytes();
                    AddContent(bytes.Length, MaxByteBytes);
                    output.WriteByte(0xc6);
                    WriteLength(bytes.Length);
                    output.Write(bytes);
                    break;
                case AuraValueKind.Array:
                    var array = value.AsArray();
                    WriteContainerLength(array.Count);
                    foreach (var child in array) WriteValue(child, depth + 1);
                    break;
                case AuraValueKind.Map:
                    var map = value.AsMap();
                    WriteContainerLength(map.Count);
                    foreach (var entry in map)
                    {
                        output.WriteByte(0x92);
                        WriteString(entry.Key, MaxStringBytes, true);
                        WriteValue(entry.Value, depth + 1);
                    }
                    break;
                case AuraValueKind.Handle:
                    var handle = value.AsHandle();
                    output.WriteByte(0x93);
                    WriteUInt64((ulong)handle.ObjectId);
                    WriteUInt64((ulong)handle.Generation);
                    WriteString(handle.TypeName, 128, false);
                    break;
                case AuraValueKind.Error:
                    WriteString(ToWireCode(value.AsError()), 128, false);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(value));
            }
        }

        private void WriteContainerLength(int count)
        {
            if (count > MaxContainerEntries)
            {
                throw new ArgumentException("Bridge container has too many entries.");
            }

            output.WriteByte(0xdd);
            WriteLength(count);
        }

        private void WriteString(string value, int limit, bool countContent)
        {
            byte[] encoded;
            try
            {
                encoded = StrictUtf8.GetBytes(value);
            }
            catch (EncoderFallbackException exception)
            {
                throw new ArgumentException("Bridge strings must be valid UTF-16.", nameof(value), exception);
            }

            if (encoded.Length > limit)
            {
                throw new ArgumentException("Bridge string exceeds its byte limit.", nameof(value));
            }

            if (countContent) AddContent(encoded.Length, limit);
            output.WriteByte(0xdb);
            WriteLength(encoded.Length);
            output.Write(encoded);
        }

        private void AddContent(int length, int individualLimit)
        {
            if (length > individualLimit || contentBytes > MaxTotalContentBytes - length)
            {
                throw new ArgumentException("Bridge value exceeds its content limit.");
            }

            contentBytes += length;
        }

        private void WriteLength(int length)
        {
            Span<byte> buffer = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(buffer, checked((uint)length));
            output.Write(buffer);
        }

        private void WriteInt64(long value)
        {
            Span<byte> buffer = stackalloc byte[8];
            BinaryPrimitives.WriteInt64BigEndian(buffer, value);
            output.Write(buffer);
        }

        private void WriteUInt64(ulong value)
        {
            output.WriteByte(0xcf);
            Span<byte> buffer = stackalloc byte[8];
            BinaryPrimitives.WriteUInt64BigEndian(buffer, value);
            output.Write(buffer);
        }
    }

    private sealed class Decoder(byte[] input)
    {
        private int offset;
        private int contentBytes;
        private int values;

        public bool AtEnd => offset == input.Length;

        public AuraValue ReadValue(int depth)
        {
            if (depth > MaxDepth || values >= MaxTotalValues)
            {
                throw Invalid("Bridge value exceeds structural limits.");
            }

            values++;
            Expect(0x92);
            var kind = ReadByte();
            return kind switch
            {
                0 => ReadNull(),
                1 => ReadBoolean(),
                2 => ReadInteger(),
                3 => ReadDouble(),
                4 => AuraValue.FromString(ReadString(MaxStringBytes, true)),
                5 => AuraValue.FromBytes(ReadBytes()),
                6 => ReadArray(depth),
                7 => ReadMap(depth),
                8 => ReadHandle(),
                9 => AuraValue.FromError(FromWireCode(ReadString(128, false))),
                _ => throw Invalid("Bridge value uses an unknown tag."),
            };
        }

        private AuraValue ReadNull()
        {
            Expect(0xc0);
            return AuraValue.Null;
        }

        private AuraValue ReadBoolean() => ReadByte() switch
        {
            0xc2 => AuraValue.FromBoolean(false),
            0xc3 => AuraValue.FromBoolean(true),
            _ => throw Invalid("Bridge Boolean is not canonical."),
        };

        private AuraValue ReadInteger()
        {
            Expect(0xd3);
            return AuraValue.FromInteger(BinaryPrimitives.ReadInt64BigEndian(Take(8)));
        }

        private AuraValue ReadDouble()
        {
            Expect(0xcb);
            var value = BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64BigEndian(Take(8)));
            if (!double.IsFinite(value)) throw Invalid("Bridge double must be finite.");
            return AuraValue.FromDouble(value);
        }

        private byte[] ReadBytes()
        {
            Expect(0xc6);
            var length = ReadLength();
            AddContent(length, MaxByteBytes);
            return Take(length).ToArray();
        }

        private AuraValue ReadArray(int depth)
        {
            var count = ReadContainerLength();
            var array = new AuraValue[count];
            for (var index = 0; index < count; index++) array[index] = ReadValue(depth + 1);
            return AuraValue.FromArray(array);
        }

        private AuraValue ReadMap(int depth)
        {
            var count = ReadContainerLength();
            var entries = new KeyValuePair<string, AuraValue>[count];
            var keys = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < count; index++)
            {
                Expect(0x92);
                var key = ReadString(MaxStringBytes, true);
                if (!keys.Add(key)) throw Invalid("Bridge map contains a duplicate key.");
                entries[index] = new KeyValuePair<string, AuraValue>(key, ReadValue(depth + 1));
            }

            return AuraValue.FromMap(entries);
        }

        private AuraValue ReadHandle()
        {
            Expect(0x93);
            var objectId = ReadPositiveUInt64();
            var generation = ReadPositiveUInt64();
            var typeName = ReadString(128, false);
            try
            {
                return AuraValue.FromHandle(new AuraHandle(objectId, generation, typeName));
            }
            catch (ArgumentException exception)
            {
                throw Invalid("Bridge handle is invalid.", exception);
            }
        }

        private long ReadPositiveUInt64()
        {
            Expect(0xcf);
            var value = BinaryPrimitives.ReadUInt64BigEndian(Take(8));
            if (value is 0 or > long.MaxValue) throw Invalid("Bridge handle identifier is invalid.");
            return (long)value;
        }

        private int ReadContainerLength()
        {
            Expect(0xdd);
            var count = ReadLength();
            if (count > MaxContainerEntries) throw Invalid("Bridge container has too many entries.");
            return count;
        }

        private string ReadString(int limit, bool countContent)
        {
            Expect(0xdb);
            var length = ReadLength();
            if (length > limit) throw Invalid("Bridge string exceeds its byte limit.");
            if (countContent) AddContent(length, limit);
            try
            {
                return StrictUtf8.GetString(Take(length));
            }
            catch (DecoderFallbackException exception)
            {
                throw Invalid("Bridge string is not valid UTF-8.", exception);
            }
        }

        private int ReadLength()
        {
            var value = BinaryPrimitives.ReadUInt32BigEndian(Take(4));
            if (value > int.MaxValue) throw Invalid("Bridge length exceeds process limits.");
            return (int)value;
        }

        private void AddContent(int length, int individualLimit)
        {
            if (length > individualLimit || contentBytes > MaxTotalContentBytes - length)
            {
                throw Invalid("Bridge value exceeds its content limit.");
            }

            contentBytes += length;
        }

        private byte ReadByte()
        {
            if (offset >= input.Length) throw Invalid("Bridge value is truncated.");
            return input[offset++];
        }

        private void Expect(byte expected)
        {
            if (ReadByte() != expected) throw Invalid("Bridge value is not canonical.");
        }

        private ReadOnlySpan<byte> Take(int length)
        {
            if (length < 0 || offset > input.Length - length) throw Invalid("Bridge value is truncated.");
            var result = input.AsSpan(offset, length);
            offset += length;
            return result;
        }
    }

    private static string ToWireCode(AuraErrorCode code) => code switch
    {
        AuraErrorCode.InvalidArgument => "invalid-argument",
        AuraErrorCode.InvalidResult => "invalid-result",
        AuraErrorCode.PermissionDenied => "permission-denied",
        AuraErrorCode.StaleHandle => "stale-handle",
        AuraErrorCode.TypeMismatch => "type-mismatch",
        AuraErrorCode.Cancelled => "cancelled",
        AuraErrorCode.CallbackFailed => "callback-failed",
        AuraErrorCode.Unavailable => "unavailable",
        AuraErrorCode.Internal => "internal",
        _ => throw new ArgumentOutOfRangeException(nameof(code)),
    };

    private static AuraErrorCode FromWireCode(string code) => code switch
    {
        "invalid-argument" => AuraErrorCode.InvalidArgument,
        "invalid-result" => AuraErrorCode.InvalidResult,
        "permission-denied" => AuraErrorCode.PermissionDenied,
        "stale-handle" => AuraErrorCode.StaleHandle,
        "type-mismatch" => AuraErrorCode.TypeMismatch,
        "cancelled" => AuraErrorCode.Cancelled,
        "callback-failed" => AuraErrorCode.CallbackFailed,
        "unavailable" => AuraErrorCode.Unavailable,
        "internal" => AuraErrorCode.Internal,
        _ => throw Invalid("Bridge error code is unknown."),
    };

    private static InvalidDataException Invalid(string message, Exception? inner = null) =>
        new(message, inner);
}
