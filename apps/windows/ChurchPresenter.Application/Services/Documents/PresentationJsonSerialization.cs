using System.Text.Json;
using System.Text.Json.Serialization;


namespace ChurchPresenter.Services.Documents;

/// <summary>
/// Shared JSON options and converters for typed presentation/theme serialization.
/// </summary>
public static class PresentationJsonSerialization
{
    public static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        options.Converters.Add(new SlideLayerJsonConverter());
        options.Converters.Add(new SlideBackgroundJsonConverter());
        options.Converters.Add(new LayerEffectJsonConverter());
        return options;
    }

    private sealed class SlideLayerJsonConverter : JsonConverter<SlideLayer>
    {
        public override SlideLayer Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            var root = document.RootElement;
            if (!root.TryGetProperty("type", out var typeProperty))
                throw new JsonException("Layer payload is missing a type discriminator.");

            var json = root.GetRawText();
            var nestedOptions = CreateOptionsWithoutConverter<SlideLayerJsonConverter>(options);
            return typeProperty.GetString()?.ToLowerInvariant() switch
            {
                "text" => (SlideLayer?)JsonSerializer.Deserialize<TextLayer>(json, nestedOptions),
                "shape" => JsonSerializer.Deserialize<ShapeLayer>(json, nestedOptions),
                "media" => JsonSerializer.Deserialize<MediaLayer>(json, nestedOptions),
                "web" => JsonSerializer.Deserialize<WebLayer>(json, nestedOptions),
                "vector" => JsonSerializer.Deserialize<VectorLayer>(json, nestedOptions),
                _ => throw new JsonException($"Unsupported layer type '{typeProperty.GetString()}'."),
            } ?? throw new JsonException("Could not deserialize slide layer.");
        }

        public override void Write(Utf8JsonWriter writer, SlideLayer value, JsonSerializerOptions options)
        {
            switch (value)
            {
                case TextLayer textLayer:
                    WriteObjectWithTypeDiscriminator(writer, textLayer, "text", options);
                    break;
                case ShapeLayer shapeLayer:
                    WriteObjectWithTypeDiscriminator(writer, shapeLayer, "shape", options);
                    break;
                case MediaLayer mediaLayer:
                    WriteObjectWithTypeDiscriminator(writer, mediaLayer, "media", options);
                    break;
                case WebLayer webLayer:
                    WriteObjectWithTypeDiscriminator(writer, webLayer, "web", options);
                    break;
                case VectorLayer vectorLayer:
                    WriteObjectWithTypeDiscriminator(writer, vectorLayer, "vector", options);
                    break;
                default:
                    throw new JsonException($"Unsupported layer type '{value.GetType().Name}'.");
            }
        }
    }

    private sealed class SlideBackgroundJsonConverter : JsonConverter<SlideBackground>
    {
        public override SlideBackground Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            var root = document.RootElement;
            if (!root.TryGetProperty("type", out var typeProperty))
                throw new JsonException("Background payload is missing a type discriminator.");

            var json = root.GetRawText();
            var nestedOptions = CreateOptionsWithoutConverter<SlideBackgroundJsonConverter>(options);
            return typeProperty.GetString()?.ToLowerInvariant() switch
            {
                "solid" => (SlideBackground?)JsonSerializer.Deserialize<SolidSlideBackground>(json, nestedOptions),
                "gradient" => JsonSerializer.Deserialize<GradientSlideBackground>(json, nestedOptions),
                "image" => JsonSerializer.Deserialize<ImageSlideBackground>(json, nestedOptions),
                "video" => JsonSerializer.Deserialize<VideoSlideBackground>(json, nestedOptions),
                "transparent" => JsonSerializer.Deserialize<TransparentSlideBackground>(json, nestedOptions),
                _ => throw new JsonException($"Unsupported background type '{typeProperty.GetString()}'."),
            } ?? throw new JsonException("Could not deserialize slide background.");
        }

        public override void Write(Utf8JsonWriter writer, SlideBackground value, JsonSerializerOptions options)
        {
            switch (value)
            {
                case SolidSlideBackground solid:
                    WriteObjectWithTypeDiscriminator(writer, solid, "solid", options);
                    break;
                case GradientSlideBackground gradient:
                    WriteObjectWithTypeDiscriminator(writer, gradient, "gradient", options);
                    break;
                case ImageSlideBackground image:
                    WriteObjectWithTypeDiscriminator(writer, image, "image", options);
                    break;
                case VideoSlideBackground video:
                    WriteObjectWithTypeDiscriminator(writer, video, "video", options);
                    break;
                case TransparentSlideBackground transparent:
                    WriteObjectWithTypeDiscriminator(writer, transparent, "transparent", options);
                    break;
                default:
                    throw new JsonException($"Unsupported background type '{value.GetType().Name}'.");
            }
        }
    }

    private sealed class LayerEffectJsonConverter : JsonConverter<LayerEffectModel>
    {
        public override LayerEffectModel Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            var root = document.RootElement;
            if (!root.TryGetProperty("type", out var typeProperty))
                throw new JsonException("Layer effect payload is missing a type discriminator.");

            var json = root.GetRawText();
            var nestedOptions = CreateOptionsWithoutConverter<LayerEffectJsonConverter>(options);
            return typeProperty.GetString()?.ToLowerInvariant() switch
            {
                "drop-shadow" => (LayerEffectModel?)JsonSerializer.Deserialize<DropShadowEffectModel>(json, nestedOptions),
                "layer-blur" => JsonSerializer.Deserialize<LayerBlurEffectModel>(json, nestedOptions),
                _ => throw new JsonException($"Unsupported layer effect type '{typeProperty.GetString()}'."),
            } ?? throw new JsonException("Could not deserialize layer effect.");
        }

        public override void Write(Utf8JsonWriter writer, LayerEffectModel value, JsonSerializerOptions options)
        {
            switch (value)
            {
                case DropShadowEffectModel dropShadow:
                    WriteObjectWithTypeDiscriminator(writer, dropShadow, "drop-shadow", options);
                    break;
                case LayerBlurEffectModel blur:
                    WriteObjectWithTypeDiscriminator(writer, blur, "layer-blur", options);
                    break;
                default:
                    throw new JsonException($"Unsupported layer effect type '{value.GetType().Name}'.");
            }
        }
    }

    private static void WriteObjectWithTypeDiscriminator(
        Utf8JsonWriter writer,
        object value,
        string discriminator,
        JsonSerializerOptions options)
    {
        var element = JsonSerializer.SerializeToElement(value, value.GetType(), options);
        writer.WriteStartObject();
        writer.WriteString("type", discriminator);
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, "type", StringComparison.OrdinalIgnoreCase))
                continue;

            property.WriteTo(writer);
        }

        writer.WriteEndObject();
    }

    private static JsonSerializerOptions CreateOptionsWithoutConverter<TConverter>(JsonSerializerOptions options)
        where TConverter : JsonConverter
    {
        var nestedOptions = new JsonSerializerOptions(options);
        for (var index = nestedOptions.Converters.Count - 1; index >= 0; index--)
        {
            if (nestedOptions.Converters[index] is TConverter)
                nestedOptions.Converters.RemoveAt(index);
        }

        return nestedOptions;
    }
}