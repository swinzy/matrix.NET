using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace TeamBanana.MatrixDotNet.Transport;

/// <summary>
/// Options for serialising app-supplied types by reflection, with the library's naming policy.
/// Only the overloads marked as needing reflection use it (D29); library types still resolve
/// through <see cref="MatrixJsonContext"/>.
/// </summary>
internal static class ReflectionJson
{
    public const string Message =
        "Serialises the content by reflection, which trimming and Native AOT can break. Use the overload " +
        "taking a JsonTypeInfo from a source-generated JsonSerializerContext, or a JsonObject.";

    private static JsonSerializerOptions? _options;

    [RequiresUnreferencedCode(Message)]
    [RequiresDynamicCode(Message)]
    public static JsonTypeInfo<T> TypeInfo<T>()
    {
        _options ??= new JsonSerializerOptions(MatrixJsonContext.Default.Options)
        {
            TypeInfoResolver = JsonTypeInfoResolver.Combine(MatrixJsonContext.Default, new DefaultJsonTypeInfoResolver())
        };
        return (JsonTypeInfo<T>)_options.GetTypeInfo(typeof(T));
    }
}
