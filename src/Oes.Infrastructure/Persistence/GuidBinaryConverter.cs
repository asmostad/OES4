using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Oes.Infrastructure.Persistence;

/// <summary>
/// Stores GUIDs as BINARY(16) in big-endian (RFC 9562) byte order. Guid.ToByteArray()'s
/// default little-endian layout would scramble GUID v7's time prefix and make inserts
/// land randomly in the clustered index; big-endian keeps them in time order.
/// </summary>
public sealed class GuidBinaryConverter() : ValueConverter<Guid, byte[]>(
    g => g.ToByteArray(true),
    b => new Guid(b, true));
