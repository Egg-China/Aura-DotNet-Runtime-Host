using System.Collections.ObjectModel;

namespace Aura.Runtime.DotNet;

/// <summary>Identifies one canonical Bridge Value v1 variant.</summary>
public enum AuraValueKind
{
    Null,
    Boolean,
    Integer,
    Double,
    String,
    Bytes,
    Array,
    Map,
    Handle,
    Error,
}

/// <summary>Stable, redacted Bridge error categories.</summary>
public enum AuraErrorCode
{
    InvalidArgument,
    InvalidResult,
    PermissionDenied,
    StaleHandle,
    TypeMismatch,
    Cancelled,
    CallbackFailed,
    Unavailable,
    Internal,
}

/// <summary>Generation-safe launcher object handle.</summary>
public sealed record AuraHandle
{
    /// <summary>Creates a validated handle.</summary>
    public AuraHandle(long objectId, long generation, string typeName)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(objectId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(generation);
        ArgumentException.ThrowIfNullOrWhiteSpace(typeName);
        if (typeName.Length > 128 || !IsCanonicalTypeName(typeName))
        {
            throw new ArgumentException("Handle type name must be canonical lower-case text.", nameof(typeName));
        }

        ObjectId = objectId;
        Generation = generation;
        TypeName = typeName;
    }

    /// <summary>Launcher object identifier.</summary>
    public long ObjectId { get; }

    /// <summary>Handle generation.</summary>
    public long Generation { get; }

    /// <summary>Canonical object type.</summary>
    public string TypeName { get; }

    private static bool IsCanonicalTypeName(string value)
    {
        if (value[0] is < 'a' or > 'z')
        {
            return false;
        }

        var separator = false;
        foreach (var character in value.AsSpan(1))
        {
            if (character is '.' or '-')
            {
                if (separator)
                {
                    return false;
                }

                separator = true;
            }
            else if (character is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                separator = false;
            }
            else
            {
                return false;
            }
        }

        return !separator;
    }
}

/// <summary>Immutable language-neutral Bridge Value v1 value.</summary>
public sealed class AuraValue : IEquatable<AuraValue>
{
    private readonly object? value;

    private AuraValue(AuraValueKind kind, object? value)
    {
        Kind = kind;
        this.value = value;
    }

    /// <summary>Singleton null value.</summary>
    public static AuraValue Null { get; } = new(AuraValueKind.Null, null);

    /// <summary>Value variant.</summary>
    public AuraValueKind Kind { get; }

    /// <summary>Creates a Boolean value.</summary>
    public static AuraValue FromBoolean(bool value) => new(AuraValueKind.Boolean, value);

    /// <summary>Creates a signed 64-bit integer value.</summary>
    public static AuraValue FromInteger(long value) => new(AuraValueKind.Integer, value);

    /// <summary>Creates a finite binary64 value.</summary>
    public static AuraValue FromDouble(double value)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Bridge doubles must be finite.");
        }

        return new AuraValue(AuraValueKind.Double, value);
    }

    /// <summary>Creates a UTF-8 string value.</summary>
    public static AuraValue FromString(string value) =>
        new(AuraValueKind.String, value ?? throw new ArgumentNullException(nameof(value)));

    /// <summary>Creates a defensively copied byte value.</summary>
    public static AuraValue FromBytes(ReadOnlySpan<byte> value) =>
        new(AuraValueKind.Bytes, value.ToArray());

    /// <summary>Creates an immutable ordered array value.</summary>
    public static AuraValue FromArray(IEnumerable<AuraValue> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return new AuraValue(AuraValueKind.Array, Array.AsReadOnly(values.ToArray()));
    }

    /// <summary>Creates an insertion-ordered map with unique keys.</summary>
    public static AuraValue FromMap(IEnumerable<KeyValuePair<string, AuraValue>> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var copied = new List<KeyValuePair<string, AuraValue>>();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            ArgumentNullException.ThrowIfNull(entry.Key);
            ArgumentNullException.ThrowIfNull(entry.Value);
            if (!keys.Add(entry.Key))
            {
                throw new ArgumentException($"Duplicate Bridge map key: {entry.Key}", nameof(entries));
            }

            copied.Add(entry);
        }

        return new AuraValue(AuraValueKind.Map, new ReadOnlyCollection<KeyValuePair<string, AuraValue>>(copied));
    }

    /// <summary>Creates an opaque handle value.</summary>
    public static AuraValue FromHandle(AuraHandle value) =>
        new(AuraValueKind.Handle, value ?? throw new ArgumentNullException(nameof(value)));

    /// <summary>Creates a stable redacted error value.</summary>
    public static AuraValue FromError(AuraErrorCode value) => new(AuraValueKind.Error, value);

    /// <summary>Returns the Boolean content.</summary>
    public bool AsBoolean() => Require<bool>(AuraValueKind.Boolean);

    /// <summary>Returns the integer content.</summary>
    public long AsInteger() => Require<long>(AuraValueKind.Integer);

    /// <summary>Returns the floating-point content.</summary>
    public double AsDouble() => Require<double>(AuraValueKind.Double);

    /// <summary>Returns the string content.</summary>
    public string AsString() => Require<string>(AuraValueKind.String);

    /// <summary>Returns a copy of the byte content.</summary>
    public byte[] AsBytes() => Require<byte[]>(AuraValueKind.Bytes).ToArray();

    /// <summary>Returns the immutable ordered array content.</summary>
    public IReadOnlyList<AuraValue> AsArray() => Require<IReadOnlyList<AuraValue>>(AuraValueKind.Array);

    /// <summary>Returns the immutable insertion-ordered map content.</summary>
    public IReadOnlyList<KeyValuePair<string, AuraValue>> AsMap() =>
        Require<IReadOnlyList<KeyValuePair<string, AuraValue>>>(AuraValueKind.Map);

    /// <summary>Returns the handle content.</summary>
    public AuraHandle AsHandle() => Require<AuraHandle>(AuraValueKind.Handle);

    /// <summary>Returns the redacted error content.</summary>
    public AuraErrorCode AsError() => Require<AuraErrorCode>(AuraValueKind.Error);

    /// <inheritdoc />
    public bool Equals(AuraValue? other)
    {
        if (other is null || Kind != other.Kind)
        {
            return false;
        }

        return Kind switch
        {
            AuraValueKind.Null => true,
            AuraValueKind.Bytes => ((byte[])value!).AsSpan().SequenceEqual((byte[])other.value!),
            AuraValueKind.Array => AsArray().SequenceEqual(other.AsArray()),
            AuraValueKind.Map => AsMap().SequenceEqual(other.AsMap()),
            _ => Equals(value, other.value),
        };
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as AuraValue);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Kind);
        switch (Kind)
        {
            case AuraValueKind.Bytes:
                foreach (var item in (byte[])value!) hash.Add(item);
                break;
            case AuraValueKind.Array:
                foreach (var item in AsArray()) hash.Add(item);
                break;
            case AuraValueKind.Map:
                foreach (var item in AsMap()) hash.Add(item);
                break;
            default:
                hash.Add(value);
                break;
        }

        return hash.ToHashCode();
    }

    private T Require<T>(AuraValueKind expected)
    {
        if (Kind != expected)
        {
            throw new InvalidOperationException($"Aura value is {Kind}, not {expected}.");
        }

        return (T)value!;
    }
}
