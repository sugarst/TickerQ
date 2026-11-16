using System;
using System.Linq;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace TickerQ.EntityFrameworkCore.Compat;

/// <summary>
/// Oracle's MySql.EntityFrameworkCore can return a JSON column as a string when EF reads an
/// <c>int[]</c> retry interval collection. Map the collection as a scalar to deserialize that string.
/// Existing values use the same <c>[1,2,3]</c> JSON format.
/// </summary>
internal static class PrimitiveCollectionCompat
{
    public static void Apply(ModelBuilder modelBuilder, string providerName, Func<Type, bool> isTickerQEntity)
    {
        if (!string.Equals(providerName, "MySql.EntityFrameworkCore", StringComparison.Ordinal))
            return;
        // Only keyed entity types: value types such as CronChainNode can be in the model provisionally and
        // are dropped later once their owning property gets its own converter.
        foreach (var entity in modelBuilder.Model.GetEntityTypes()
                     .Where(e => isTickerQEntity(e.ClrType) && e.FindPrimaryKey() != null).ToArray())
        foreach (var name in entity.GetDeclaredProperties().Where(p => p.ClrType == typeof(int[])).Select(p => p.Name).ToArray())
        {
            // Clearing the element type turns the primitive collection into a scalar column; EF Core 11 replaces
            // the property object while doing so, so look it up again before configuring it.
            entity.FindDeclaredProperty(name)!.SetElementType(null);
            var property = entity.FindDeclaredProperty(name)!;
            property.SetValueConverter(Converter);
            property.SetValueComparer(Comparer);
        }
    }

    private static readonly ValueConverter<int[], string> Converter = new(
        value => Serialize(value),
        json => Deserialize(json));

    private static readonly ValueComparer<int[]> Comparer = new(
        (left, right) => left == null ? right == null : right != null && left.SequenceEqual(right),
        value => value == null ? 0 : value.Aggregate(0, (hash, item) => HashCode.Combine(hash, item)),
        value => value == null ? null : value.ToArray());

    private static string Serialize(int[] value) => JsonSerializer.Serialize(value, IntArrayJsonContext.Default.Int32Array);

    private static int[] Deserialize(string json) => JsonSerializer.Deserialize(json, IntArrayJsonContext.Default.Int32Array);
}

[System.Text.Json.Serialization.JsonSerializable(typeof(int[]))]
internal sealed partial class IntArrayJsonContext : System.Text.Json.Serialization.JsonSerializerContext;
