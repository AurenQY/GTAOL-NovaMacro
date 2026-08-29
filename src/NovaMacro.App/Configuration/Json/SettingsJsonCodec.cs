using System.Buffers;
using System.Text;
using System.Text.Json;
using NovaMacro.Core;
using NovaMacro.Windows;

namespace NovaMacro.App.Configuration.Json;

internal static class SettingsJsonCodec
{
    private const int CurrentVersion = 1;
    private const int MaximumJsonDepth = 32;
    private const long MaximumTimeSpanMilliseconds =
        long.MaxValue / TimeSpan.TicksPerMillisecond;

    private static readonly IReadOnlySet<string> TopLevelProperties =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "version",
            "macro"
        };

    private static readonly IReadOnlySet<string> MacroProperties =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "name",
            "hotkey",
            "steps"
        };

    private static readonly IReadOnlySet<string> HotkeyProperties =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "key",
            "modifiers"
        };

    private static readonly IReadOnlySet<string> KeyTapProperties =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "type",
            "key"
        };

    private static readonly IReadOnlySet<string> DelayProperties =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "type",
            "milliseconds"
        };

    internal static SettingsJsonReadResult Read(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        JsonDocument document;

        try
        {
            document = JsonDocument.Parse(
                json,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = MaximumJsonDepth
                });
        }
        catch (JsonException)
        {
            return SettingsJsonReadResult.CodecFailure(
                new SettingsJsonError(
                    SettingsJsonErrorCode.InvalidJson,
                    "json",
                    "Settings JSON is not valid."));
        }

        using (document)
        {
            if (!TryParseCandidate(
                    document.RootElement,
                    out ApplicationSettingsCandidate? candidate,
                    out SettingsJsonError? error))
            {
                return SettingsJsonReadResult.CodecFailure(error!);
            }

            ConfigurationValidationResult validationResult =
                ConfigurationValidator.Validate(candidate!);

            return SettingsJsonReadResult.FromValidation(validationResult);
        }
    }

    internal static string Write(ApplicationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var buffer = new ArrayBufferWriter<byte>();

        using (var writer = new Utf8JsonWriter(
            buffer,
            new JsonWriterOptions
            {
                Indented = true,
                SkipValidation = false
            }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", CurrentVersion);
            writer.WritePropertyName("macro");
            WriteMacro(writer, settings.Macro);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static bool TryParseCandidate(
        JsonElement root,
        out ApplicationSettingsCandidate? candidate,
        out SettingsJsonError? error)
    {
        candidate = null;

        if (!TryReadKnownObject(
                root,
                string.Empty,
                TopLevelProperties,
                out Dictionary<string, JsonElement> properties,
                out error))
        {
            return false;
        }

        if (!TryGetRequiredProperty(
                properties,
                "version",
                string.Empty,
                out JsonElement versionElement,
                out error) ||
            !TryReadInt64(
                versionElement,
                "version",
                out long version,
                out error))
        {
            return false;
        }

        if (version != CurrentVersion)
        {
            error = new SettingsJsonError(
                SettingsJsonErrorCode.UnsupportedVersion,
                "version",
                $"Settings version '{version}' is not supported.");
            return false;
        }

        if (!TryGetRequiredProperty(
                properties,
                "macro",
                string.Empty,
                out JsonElement macroElement,
                out error) ||
            !TryParseMacro(macroElement, out MacroDefinitionCandidate? macro, out error))
        {
            return false;
        }

        candidate = new ApplicationSettingsCandidate(macro);
        return true;
    }

    private static bool TryParseMacro(
        JsonElement element,
        out MacroDefinitionCandidate? macro,
        out SettingsJsonError? error)
    {
        macro = null;

        if (!TryReadKnownObject(
                element,
                "macro",
                MacroProperties,
                out Dictionary<string, JsonElement> properties,
                out error))
        {
            return false;
        }

        if (!TryGetRequiredProperty(
                properties,
                "name",
                "macro",
                out JsonElement nameElement,
                out error) ||
            !TryReadString(nameElement, "macro.name", out string? name, out error) ||
            !TryGetRequiredProperty(
                properties,
                "hotkey",
                "macro",
                out JsonElement hotkeyElement,
                out error) ||
            !TryParseHotkey(
                hotkeyElement,
                out KeyboardHotkey? hotkey,
                out error) ||
            !TryGetRequiredProperty(
                properties,
                "steps",
                "macro",
                out JsonElement stepsElement,
                out error) ||
            !TryParseSteps(stepsElement, out MacroSequence? sequence, out error))
        {
            return false;
        }

        macro = new MacroDefinitionCandidate(name, hotkey, sequence);
        return true;
    }

    private static bool TryParseHotkey(
        JsonElement element,
        out KeyboardHotkey? hotkey,
        out SettingsJsonError? error)
    {
        hotkey = null;

        if (!TryReadKnownObject(
                element,
                "macro.hotkey",
                HotkeyProperties,
                out Dictionary<string, JsonElement> properties,
                out error))
        {
            return false;
        }

        if (!TryGetRequiredProperty(
                properties,
                "key",
                "macro.hotkey",
                out JsonElement keyElement,
                out error) ||
            !TryReadString(
                keyElement,
                "macro.hotkey.key",
                out string? keyToken,
                out error))
        {
            return false;
        }

        if (!HotkeyKeyTokenMapper.TryParse(keyToken!, out HotkeyKey key))
        {
            error = new SettingsJsonError(
                SettingsJsonErrorCode.UnknownHotkeyKey,
                "macro.hotkey.key",
                $"Hotkey key token '{keyToken}' is not supported.");
            return false;
        }

        if (!TryGetRequiredProperty(
                properties,
                "modifiers",
                "macro.hotkey",
                out JsonElement modifiersElement,
                out error) ||
            !TryParseModifiers(
                modifiersElement,
                out HotkeyModifiers modifiers,
                out error))
        {
            return false;
        }

        hotkey = new KeyboardHotkey(key, modifiers);
        return true;
    }

    private static bool TryParseModifiers(
        JsonElement element,
        out HotkeyModifiers modifiers,
        out SettingsJsonError? error)
    {
        modifiers = HotkeyModifiers.None;
        error = null;

        if (element.ValueKind != JsonValueKind.Array)
        {
            error = Expected(
                SettingsJsonErrorCode.ExpectedArray,
                "macro.hotkey.modifiers",
                "an array");
            return false;
        }

        var seenModifiers = new HashSet<HotkeyModifiers>();
        int index = 0;

        foreach (JsonElement modifierElement in element.EnumerateArray())
        {
            string path = $"macro.hotkey.modifiers[{index}]";

            if (!TryReadString(
                    modifierElement,
                    path,
                    out string? token,
                    out error))
            {
                return false;
            }

            if (!TryParseModifier(token!, out HotkeyModifiers modifier))
            {
                error = new SettingsJsonError(
                    SettingsJsonErrorCode.UnknownModifier,
                    path,
                    $"Modifier token '{token}' is not supported.");
                return false;
            }

            if (!seenModifiers.Add(modifier))
            {
                error = new SettingsJsonError(
                    SettingsJsonErrorCode.DuplicateModifier,
                    path,
                    $"Modifier token '{token}' is duplicated.");
                return false;
            }

            modifiers |= modifier;
            index++;
        }

        return true;
    }

    private static bool TryParseSteps(
        JsonElement element,
        out MacroSequence? sequence,
        out SettingsJsonError? error)
    {
        sequence = null;
        error = null;

        if (element.ValueKind != JsonValueKind.Array)
        {
            error = Expected(
                SettingsJsonErrorCode.ExpectedArray,
                "macro.steps",
                "an array");
            return false;
        }

        var steps = new List<MacroStep>();
        int index = 0;

        foreach (JsonElement stepElement in element.EnumerateArray())
        {
            if (!TryParseStep(stepElement, index, out MacroStep? step, out error))
            {
                return false;
            }

            steps.Add(step!);
            index++;
        }

        sequence = new MacroSequence(steps);
        return true;
    }

    private static bool TryParseStep(
        JsonElement element,
        int index,
        out MacroStep? step,
        out SettingsJsonError? error)
    {
        step = null;
        string path = $"macro.steps[{index}]";

        if (!TryCollectObjectProperties(
                element,
                path,
                out Dictionary<string, JsonElement> properties,
                out error) ||
            !TryGetRequiredProperty(
                properties,
                "type",
                path,
                out JsonElement typeElement,
                out error) ||
            !TryReadString(typeElement, $"{path}.type", out string? type, out error))
        {
            return false;
        }

        switch (type)
        {
            case "keyTap":
                return TryParseKeyTapStep(
                    element,
                    properties,
                    path,
                    out step,
                    out error);

            case "delay":
                return TryParseDelayStep(
                    element,
                    properties,
                    path,
                    out step,
                    out error);

            default:
                error = new SettingsJsonError(
                    SettingsJsonErrorCode.UnknownStepType,
                    $"{path}.type",
                    $"Macro step type token '{type}' is not supported.");
                return false;
        }
    }

    private static bool TryParseKeyTapStep(
        JsonElement element,
        IReadOnlyDictionary<string, JsonElement> properties,
        string path,
        out MacroStep? step,
        out SettingsJsonError? error)
    {
        step = null;

        if (!TryRejectUnknownProperties(
                element,
                path,
                KeyTapProperties,
                out error) ||
            !TryGetRequiredProperty(
                properties,
                "key",
                path,
                out JsonElement keyElement,
                out error) ||
            !TryReadString(keyElement, $"{path}.key", out string? token, out error))
        {
            return false;
        }

        if (!KeyboardKeyTokenMapper.TryParse(token!, out KeyboardKey key))
        {
            error = new SettingsJsonError(
                SettingsJsonErrorCode.UnknownOutputKey,
                $"{path}.key",
                $"Output key token '{token}' is not supported.");
            return false;
        }

        step = new KeyTapStep(key);
        return true;
    }

    private static bool TryParseDelayStep(
        JsonElement element,
        IReadOnlyDictionary<string, JsonElement> properties,
        string path,
        out MacroStep? step,
        out SettingsJsonError? error)
    {
        step = null;

        if (!TryRejectUnknownProperties(
                element,
                path,
                DelayProperties,
                out error) ||
            !TryGetRequiredProperty(
                properties,
                "milliseconds",
                path,
                out JsonElement millisecondsElement,
                out error) ||
            !TryReadInt64(
                millisecondsElement,
                $"{path}.milliseconds",
                out long milliseconds,
                out error))
        {
            return false;
        }

        if (milliseconds < 0 || milliseconds > MaximumTimeSpanMilliseconds)
        {
            error = new SettingsJsonError(
                SettingsJsonErrorCode.DelayMillisecondsOutOfRange,
                $"{path}.milliseconds",
                "Delay milliseconds cannot be represented safely.");
            return false;
        }

        long ticks = milliseconds * TimeSpan.TicksPerMillisecond;
        step = new DelayStep(new TimeSpan(ticks));
        return true;
    }

    private static bool TryReadKnownObject(
        JsonElement element,
        string path,
        IReadOnlySet<string> allowedProperties,
        out Dictionary<string, JsonElement> properties,
        out SettingsJsonError? error)
    {
        return TryCollectObjectProperties(
                element,
                path,
                out properties,
                out error) &&
            TryRejectUnknownProperties(
                element,
                path,
                allowedProperties,
                out error);
    }

    private static bool TryCollectObjectProperties(
        JsonElement element,
        string path,
        out Dictionary<string, JsonElement> properties,
        out SettingsJsonError? error)
    {
        properties = new Dictionary<string, JsonElement>(
            StringComparer.Ordinal);
        error = null;

        if (element.ValueKind != JsonValueKind.Object)
        {
            error = Expected(
                SettingsJsonErrorCode.ExpectedObject,
                path,
                "an object");
            return false;
        }

        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (!properties.TryAdd(property.Name, property.Value))
            {
                error = new SettingsJsonError(
                    SettingsJsonErrorCode.DuplicateProperty,
                    PropertyPath(path, property.Name),
                    $"Property '{property.Name}' is duplicated.");
                return false;
            }
        }

        return true;
    }

    private static bool TryRejectUnknownProperties(
        JsonElement element,
        string path,
        IReadOnlySet<string> allowedProperties,
        out SettingsJsonError? error)
    {
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (!allowedProperties.Contains(property.Name))
            {
                error = new SettingsJsonError(
                    SettingsJsonErrorCode.UnknownProperty,
                    PropertyPath(path, property.Name),
                    $"Property '{property.Name}' is not allowed here.");
                return false;
            }
        }

        error = null;
        return true;
    }

    private static bool TryGetRequiredProperty(
        IReadOnlyDictionary<string, JsonElement> properties,
        string propertyName,
        string parentPath,
        out JsonElement value,
        out SettingsJsonError? error)
    {
        if (properties.TryGetValue(propertyName, out value))
        {
            error = null;
            return true;
        }

        error = new SettingsJsonError(
            SettingsJsonErrorCode.MissingRequiredProperty,
            PropertyPath(parentPath, propertyName),
            $"Required property '{propertyName}' is missing.");
        return false;
    }

    private static bool TryReadString(
        JsonElement element,
        string path,
        out string? value,
        out SettingsJsonError? error)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            value = element.GetString()!;
            error = null;
            return true;
        }

        value = null;
        error = Expected(
            SettingsJsonErrorCode.ExpectedString,
            path,
            "a string");
        return false;
    }

    private static bool TryReadInt64(
        JsonElement element,
        string path,
        out long value,
        out SettingsJsonError? error)
    {
        value = 0;

        if (element.ValueKind != JsonValueKind.Number ||
            !HasIntegerJsonSyntax(element))
        {
            error = Expected(
                SettingsJsonErrorCode.ExpectedInteger,
                path,
                "an integer JSON number");
            return false;
        }

        if (!element.TryGetInt64(out value))
        {
            error = new SettingsJsonError(
                SettingsJsonErrorCode.IntegerOutOfRange,
                path,
                "Integer value is outside the supported 64-bit range.");
            return false;
        }

        error = null;
        return true;
    }

    private static bool HasIntegerJsonSyntax(JsonElement element)
    {
        string rawText = element.GetRawText();
        int startIndex = rawText.Length > 0 && rawText[0] == '-' ? 1 : 0;

        if (startIndex == rawText.Length)
        {
            return false;
        }

        for (int index = startIndex; index < rawText.Length; index++)
        {
            if (!char.IsAsciiDigit(rawText[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryParseModifier(
        string token,
        out HotkeyModifiers modifier)
    {
        modifier = token switch
        {
            "Control" => HotkeyModifiers.Control,
            "Alt" => HotkeyModifiers.Alt,
            "Shift" => HotkeyModifiers.Shift,
            _ => HotkeyModifiers.None
        };

        return modifier != HotkeyModifiers.None;
    }

    private static SettingsJsonError Expected(
        SettingsJsonErrorCode code,
        string path,
        string expected)
    {
        string displayPath = string.IsNullOrEmpty(path) ? "json" : path;

        return new SettingsJsonError(
            code,
            displayPath,
            $"Expected {expected} at '{displayPath}'.");
    }

    private static string PropertyPath(string parentPath, string propertyName)
    {
        return string.IsNullOrEmpty(parentPath)
            ? propertyName
            : $"{parentPath}.{propertyName}";
    }

    private static void WriteMacro(
        Utf8JsonWriter writer,
        MacroDefinition macro)
    {
        writer.WriteStartObject();
        writer.WriteString("name", macro.Name);
        writer.WritePropertyName("hotkey");
        WriteHotkey(writer, macro.Hotkey);
        writer.WritePropertyName("steps");
        writer.WriteStartArray();

        foreach (MacroStep step in macro.Sequence.Steps)
        {
            WriteStep(writer, step);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteHotkey(
        Utf8JsonWriter writer,
        KeyboardHotkey hotkey)
    {
        if (!HotkeyKeyTokenMapper.TryFormat(hotkey.Key, out string? keyToken))
        {
            throw new InvalidOperationException(
                $"Hotkey key '{hotkey.Key}' cannot be written as V1 JSON.");
        }

        writer.WriteStartObject();
        writer.WriteString("key", keyToken);
        writer.WritePropertyName("modifiers");
        writer.WriteStartArray();

        if ((hotkey.Modifiers & HotkeyModifiers.Control) != 0)
        {
            writer.WriteStringValue("Control");
        }

        if ((hotkey.Modifiers & HotkeyModifiers.Alt) != 0)
        {
            writer.WriteStringValue("Alt");
        }

        if ((hotkey.Modifiers & HotkeyModifiers.Shift) != 0)
        {
            writer.WriteStringValue("Shift");
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteStep(Utf8JsonWriter writer, MacroStep step)
    {
        writer.WriteStartObject();

        switch (step)
        {
            case KeyTapStep keyTap:
                if (!KeyboardKeyTokenMapper.TryFormat(
                        keyTap.Key,
                        out string? keyToken))
                {
                    throw new InvalidOperationException(
                        $"Output key '{keyTap.Key}' cannot be written as V1 JSON.");
                }

                writer.WriteString("type", "keyTap");
                writer.WriteString("key", keyToken);
                break;

            case DelayStep delay:
                if (delay.Duration.Ticks < 0 ||
                    delay.Duration.Ticks % TimeSpan.TicksPerMillisecond != 0)
                {
                    throw new InvalidOperationException(
                        "V1 JSON supports only non-negative whole-millisecond delays.");
                }

                writer.WriteString("type", "delay");
                writer.WriteNumber(
                    "milliseconds",
                    delay.Duration.Ticks / TimeSpan.TicksPerMillisecond);
                break;

            default:
                throw new InvalidOperationException(
                    $"Macro step type '{step.GetType().Name}' cannot be written " +
                    "as V1 JSON.");
        }

        writer.WriteEndObject();
    }
}
