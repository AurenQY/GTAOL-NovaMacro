using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using NovaMacro.App.Configuration;
using NovaMacro.App.Configuration.Json;
using NovaMacro.App.Configuration.Storage;
using NovaMacro.Core;
using NovaMacro.Windows;

namespace NovaMacro.Tests;

public sealed class JsonSettingsStoreTests
{
    private const int MaximumSettingsFileBytes = 256 * 1024;

    private static readonly UTF8Encoding Utf8WithoutBom = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    [Fact]
    public void DefaultConstructor_UsesPortableBaseDirectoryPath()
    {
        var store = new JsonSettingsStore();
        string expected = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "data",
            "settings.json"));

        Assert.Equal(expected, Path.GetFullPath(store.SettingsPath));
    }

    [Fact]
    public void ExplicitConstructor_NullPathThrows()
    {
        ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
            () => new JsonSettingsStore(null!));

        Assert.Equal("settingsPath", exception.ParamName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("data/settings.json")]
    public void ExplicitConstructor_InvalidTextOrRelativePathThrows(string path)
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => new JsonSettingsStore(path));

        Assert.Equal("settingsPath", exception.ParamName);
    }

    [Fact]
    public void ExplicitConstructor_RootOnlyPathThrows()
    {
        using var temporary = new TemporaryDirectory();
        string root = Assert.IsType<string>(Path.GetPathRoot(temporary.Path));

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => new JsonSettingsStore(root));

        Assert.Equal("settingsPath", exception.ParamName);
    }

    [Fact]
    public void ExplicitConstructor_NormalizesAcceptedAbsolutePath()
    {
        using var temporary = new TemporaryDirectory();
        string input = Path.Combine(
            temporary.Path,
            "data",
            "..",
            "data",
            "settings.json");

        var store = new JsonSettingsStore(input);

        Assert.Equal(Path.GetFullPath(input), store.SettingsPath);
    }

    [Fact]
    public void Load_MissingCanonicalFileReturnsMissing()
    {
        using var temporary = new TemporaryDirectory();
        string directory = temporary.GetPath("data");
        Directory.CreateDirectory(directory);
        var store = new JsonSettingsStore(
            Path.Combine(directory, "settings.json"));

        AssertMissing(store.Load());
    }

    [Fact]
    public void Load_MissingDirectoryReturnsMissingWithoutCreatingDirectory()
    {
        using var temporary = new TemporaryDirectory();
        string directory = temporary.GetPath("missing", "data");
        var store = new JsonSettingsStore(
            Path.Combine(directory, "settings.json"));

        AssertMissing(store.Load());
        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public void Load_TempOnlyStateReturnsMissing()
    {
        using var temporary = new TemporaryDirectory();
        string directory = temporary.GetPath("data");
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            Path.Combine(directory, $".settings.{Guid.NewGuid():N}.tmp"),
            SettingsJsonTestSupport.ValidJson,
            Utf8WithoutBom);
        var store = new JsonSettingsStore(
            Path.Combine(directory, "settings.json"));

        AssertMissing(store.Load());
    }

    [Fact]
    public void Load_ValidCanonicalFileReturnsLoadedSettings()
    {
        using var temporary = new TemporaryDirectory();
        ApplicationSettings expected = RepresentativeSettings("Manual Load");
        JsonSettingsStore store = CreateStore(temporary);
        WriteCanonical(store, SettingsJsonCodec.Write(expected), includeBom: false);

        SettingsLoadResult result = store.Load();

        ApplicationSettings actual = AssertLoaded(result);
        AssertSettingsEqual(expected, actual);
    }

    [Fact]
    public void Load_MalformedJsonReturnsInvalidWithCodecDiagnostics()
    {
        using var temporary = new TemporaryDirectory();
        JsonSettingsStore store = CreateStore(temporary);
        WriteCanonical(store, "{", includeBom: false);

        SettingsLoadResult result = store.Load();

        Assert.Equal(SettingsLoadStatus.Invalid, result.Status);
        Assert.Null(result.Settings);
        Assert.Null(result.Error);
        SettingsJsonReadResult jsonResult = Assert.IsType<SettingsJsonReadResult>(
            result.JsonReadResult);
        Assert.False(jsonResult.IsSuccess);
        Assert.NotEmpty(jsonResult.CodecErrors);
        Assert.Empty(jsonResult.ValidationErrors);
    }

    [Fact]
    public void Load_SemanticallyInvalidJsonReturnsInvalidWithValidationDiagnostics()
    {
        using var temporary = new TemporaryDirectory();
        JsonSettingsStore store = CreateStore(temporary);
        string json = SettingsJsonTestSupport.DocumentJson(
            macroJson: SettingsJsonTestSupport.MacroJson(nameJson: "\"   \""));
        WriteCanonical(store, json, includeBom: false);

        SettingsLoadResult result = store.Load();

        Assert.Equal(SettingsLoadStatus.Invalid, result.Status);
        Assert.Null(result.Settings);
        Assert.Null(result.Error);
        SettingsJsonReadResult jsonResult = Assert.IsType<SettingsJsonReadResult>(
            result.JsonReadResult);
        Assert.False(jsonResult.IsSuccess);
        Assert.Empty(jsonResult.CodecErrors);
        Assert.NotEmpty(jsonResult.ValidationErrors);
    }

    [Fact]
    public void Load_ValidUtf8WithoutBomReturnsLoaded()
    {
        using var temporary = new TemporaryDirectory();
        JsonSettingsStore store = CreateStore(temporary);
        WriteCanonical(store, SettingsJsonTestSupport.ValidJson, includeBom: false);

        AssertLoaded(store.Load());
        Assert.False(HasUtf8Bom(File.ReadAllBytes(store.SettingsPath)));
    }

    [Fact]
    public void Load_ValidUtf8WithBomReturnsLoaded()
    {
        using var temporary = new TemporaryDirectory();
        JsonSettingsStore store = CreateStore(temporary);
        WriteCanonical(store, SettingsJsonTestSupport.ValidJson, includeBom: true);

        AssertLoaded(store.Load());
        Assert.True(HasUtf8Bom(File.ReadAllBytes(store.SettingsPath)));
    }

    [Fact]
    public void Load_InvalidUtf8ReturnsInvalidUtf8StorageError()
    {
        using var temporary = new TemporaryDirectory();
        JsonSettingsStore store = CreateStore(temporary);
        Directory.CreateDirectory(Path.GetDirectoryName(store.SettingsPath)!);
        File.WriteAllBytes(store.SettingsPath, new byte[] { 0xC3, 0x28 });

        SettingsLoadResult result = store.Load();

        Assert.Equal(SettingsLoadStatus.Invalid, result.Status);
        Assert.Null(result.Settings);
        Assert.Null(result.JsonReadResult);
        AssertStorageError(
            result.Error,
            SettingsStorageErrorCode.InvalidUtf8,
            store.SettingsPath);
    }

    [Fact]
    public void Load_ExactlyAtSizeLimitIsNotRejectedSolelyForSize()
    {
        using var temporary = new TemporaryDirectory();
        JsonSettingsStore store = CreateStore(temporary);
        Directory.CreateDirectory(Path.GetDirectoryName(store.SettingsPath)!);
        File.WriteAllBytes(
            store.SettingsPath,
            Enumerable.Repeat((byte)' ', MaximumSettingsFileBytes).ToArray());

        SettingsLoadResult result = store.Load();

        Assert.Equal(SettingsLoadStatus.Invalid, result.Status);
        Assert.Null(result.Settings);
        Assert.Null(result.Error);
        SettingsJsonReadResult jsonResult = Assert.IsType<SettingsJsonReadResult>(
            result.JsonReadResult);
        Assert.NotEmpty(jsonResult.CodecErrors);
    }

    [Fact]
    public void Load_OneByteOverSizeLimitReturnsFileTooLarge()
    {
        using var temporary = new TemporaryDirectory();
        JsonSettingsStore store = CreateStore(temporary);
        Directory.CreateDirectory(Path.GetDirectoryName(store.SettingsPath)!);
        File.WriteAllBytes(
            store.SettingsPath,
            new byte[MaximumSettingsFileBytes + 1]);

        SettingsLoadResult result = store.Load();

        Assert.Equal(SettingsLoadStatus.Invalid, result.Status);
        Assert.Null(result.Settings);
        Assert.Null(result.JsonReadResult);
        AssertStorageError(
            result.Error,
            SettingsStorageErrorCode.FileTooLarge,
            store.SettingsPath);
    }

    [Fact]
    public void Save_FirstSaveCreatesCanonicalValidUtf8WithoutBomAndNoTemp()
    {
        using var temporary = new TemporaryDirectory();
        string directory = temporary.GetPath("missing", "data");
        var store = new JsonSettingsStore(
            Path.Combine(directory, "settings.json"));
        ApplicationSettings expected = RepresentativeSettings("First Save");

        SettingsSaveResult saveResult = store.Save(expected);

        AssertSaveSucceeded(saveResult);
        Assert.True(Directory.Exists(directory));
        Assert.True(File.Exists(store.SettingsPath));
        byte[] bytes = File.ReadAllBytes(store.SettingsPath);
        Assert.False(HasUtf8Bom(bytes));
        AssertSettingsEqual(expected, AssertLoaded(store.Load()));
        Assert.Empty(OwnTemporaryFiles(directory));
    }

    [Fact]
    public void SaveThenFreshLoad_PreservesRepresentativeSemantics()
    {
        using var temporary = new TemporaryDirectory();
        ApplicationSettings expected = RepresentativeSettings("Round Trip");
        JsonSettingsStore writer = CreateStore(temporary);

        AssertSaveSucceeded(writer.Save(expected));
        var reader = new JsonSettingsStore(writer.SettingsPath);

        AssertSettingsEqual(expected, AssertLoaded(reader.Load()));
    }

    [Fact]
    public void Save_SubsequentSaveReplacesCanonicalWithCompleteNewSettings()
    {
        using var temporary = new TemporaryDirectory();
        JsonSettingsStore store = CreateStore(temporary);
        ApplicationSettings first = RepresentativeSettings("Configuration A");
        ApplicationSettings second = RepresentativeSettings(
            "Configuration B",
            new KeyboardHotkey(
                HotkeyKey.F9,
                HotkeyModifiers.Control | HotkeyModifiers.Shift));

        AssertSaveSucceeded(store.Save(first));
        byte[] firstBytes = File.ReadAllBytes(store.SettingsPath);
        AssertSaveSucceeded(store.Save(second));
        byte[] secondBytes = File.ReadAllBytes(store.SettingsPath);

        Assert.NotEqual(firstBytes, secondBytes);
        Assert.Equal(
            Utf8WithoutBom.GetBytes(SettingsJsonCodec.Write(second)),
            secondBytes);
        AssertSettingsEqual(second, AssertLoaded(store.Load()));
        Assert.Empty(OwnTemporaryFiles(Path.GetDirectoryName(store.SettingsPath)!));
    }

    [Fact]
    public void Save_PublishSharingViolationPreservesOldCanonicalAndCleansOwnTemp()
    {
        using var temporary = new TemporaryDirectory();
        JsonSettingsStore store = CreateStore(temporary);
        ApplicationSettings first = RepresentativeSettings("Configuration A");
        ApplicationSettings second = RepresentativeSettings("Configuration B");
        AssertSaveSucceeded(store.Save(first));
        byte[] expectedBytes = File.ReadAllBytes(store.SettingsPath);

        SettingsSaveResult saveResult;
        using (var canonicalLock = new FileStream(
            store.SettingsPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read))
        {
            saveResult = store.Save(second);
            Assert.Equal(expectedBytes, File.ReadAllBytes(store.SettingsPath));
            AssertSettingsEqual(first, AssertLoaded(store.Load()));
        }

        AssertSaveFailed(
            saveResult,
            SettingsStorageErrorCode.PublishFailed,
            store.SettingsPath);
        Assert.True(File.Exists(store.SettingsPath));
        Assert.Equal(expectedBytes, File.ReadAllBytes(store.SettingsPath));
        Assert.Empty(OwnTemporaryFiles(Path.GetDirectoryName(store.SettingsPath)!));
    }

    [Fact]
    public void Save_TemporaryCreationDeniedPreservesOldCanonical()
    {
        using var temporary = new TemporaryDirectory();
        JsonSettingsStore store = CreateStore(temporary);
        ApplicationSettings first = RepresentativeSettings("Configuration A");
        ApplicationSettings second = RepresentativeSettings("Configuration B");
        AssertSaveSucceeded(store.Save(first));
        byte[] expectedBytes = File.ReadAllBytes(store.SettingsPath);
        string directory = Path.GetDirectoryName(store.SettingsPath)!;

        SettingsSaveResult saveResult = SaveWithCreateFilesDenied(
            directory,
            () => store.Save(second));

        AssertSaveFailed(
            saveResult,
            SettingsStorageErrorCode.TemporaryFileWriteFailed,
            store.SettingsPath);
        Assert.Equal(expectedBytes, File.ReadAllBytes(store.SettingsPath));
        AssertSettingsEqual(first, AssertLoaded(store.Load()));
        Assert.Empty(OwnTemporaryFiles(directory));
    }

    [Fact]
    public void Save_UnusableIntermediateParentReturnsDirectoryCreationFailed()
    {
        using var temporary = new TemporaryDirectory();
        string blockedParent = temporary.GetPath("blocked");
        File.WriteAllText(blockedParent, "not a directory", Utf8WithoutBom);
        string settingsPath = Path.Combine(
            blockedParent,
            "data",
            "settings.json");
        var store = new JsonSettingsStore(settingsPath);

        SettingsSaveResult result = store.Save(RepresentativeSettings("Save"));

        AssertSaveFailed(
            result,
            SettingsStorageErrorCode.DirectoryCreationFailed,
            store.SettingsPath);
        Assert.Equal("not a directory", File.ReadAllText(blockedParent));
    }

    [Fact]
    public void CanonicalPathAsDirectoryReturnsUnavailableOnLoadAndPublishFailedOnSave()
    {
        using var temporary = new TemporaryDirectory();
        JsonSettingsStore store = CreateStore(temporary);
        Directory.CreateDirectory(store.SettingsPath);

        SettingsLoadResult loadResult = store.Load();
        SettingsSaveResult saveResult = store.Save(RepresentativeSettings("Save"));

        Assert.Equal(SettingsLoadStatus.Unavailable, loadResult.Status);
        Assert.Null(loadResult.Settings);
        Assert.Null(loadResult.JsonReadResult);
        AssertStorageError(
            loadResult.Error,
            SettingsStorageErrorCode.ReadFailed,
            store.SettingsPath);
        AssertSaveFailed(
            saveResult,
            SettingsStorageErrorCode.PublishFailed,
            store.SettingsPath);
        Assert.True(Directory.Exists(store.SettingsPath));
        Assert.Empty(OwnTemporaryFiles(Path.GetDirectoryName(store.SettingsPath)!));
    }

    [Fact]
    public void Load_ExclusiveCanonicalLockReturnsUnavailable()
    {
        using var temporary = new TemporaryDirectory();
        JsonSettingsStore store = CreateStore(temporary);
        AssertSaveSucceeded(store.Save(RepresentativeSettings("Locked")));

        using var canonicalLock = new FileStream(
            store.SettingsPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.None);
        SettingsLoadResult result = store.Load();

        Assert.Equal(SettingsLoadStatus.Unavailable, result.Status);
        Assert.Null(result.Settings);
        Assert.Null(result.JsonReadResult);
        AssertStorageError(
            result.Error,
            SettingsStorageErrorCode.ReadFailed,
            store.SettingsPath);
    }

    [Fact]
    public void Save_DoesNotDeleteUnrelatedForeignTempFile()
    {
        using var temporary = new TemporaryDirectory();
        JsonSettingsStore store = CreateStore(temporary);
        string directory = Path.GetDirectoryName(store.SettingsPath)!;
        Directory.CreateDirectory(directory);
        string foreignTemp = Path.Combine(directory, ".settings.foreign.tmp");
        File.WriteAllText(foreignTemp, "foreign", Utf8WithoutBom);

        AssertSaveSucceeded(store.Save(RepresentativeSettings("First")));
        AssertSaveSucceeded(store.Save(RepresentativeSettings("Second")));

        Assert.True(File.Exists(foreignTemp));
        Assert.Equal("foreign", File.ReadAllText(foreignTemp));
        Assert.Equal(new[] { foreignTemp }, OwnTemporaryFiles(directory));
    }

    [Fact]
    public void Save_NullSettingsThrows()
    {
        using var temporary = new TemporaryDirectory();
        JsonSettingsStore store = CreateStore(temporary);

        ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
            () => store.Save(null!));

        Assert.Equal("settings", exception.ParamName);
    }

    [Fact]
    public void Save_CodecInvariantFailurePropagatesWithoutFilesystemMutation()
    {
        using var temporary = new TemporaryDirectory();
        string directory = temporary.GetPath("data");
        var store = new JsonSettingsStore(
            Path.Combine(directory, "settings.json"));
        ApplicationSettings invalid = SettingsJsonTestSupport.CreateSettings(
            steps: new MacroStep[] { new UnsupportedStorageTestStep() });

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => store.Save(invalid));

        Assert.Contains("Macro step type", exception.Message);
        Assert.False(Directory.Exists(directory));
    }

    private static JsonSettingsStore CreateStore(TemporaryDirectory temporary)
    {
        return new JsonSettingsStore(
            temporary.GetPath("data", "settings.json"));
    }

    private static ApplicationSettings RepresentativeSettings(
        string name,
        KeyboardHotkey? hotkey = null)
    {
        return SettingsJsonTestSupport.CreateSettings(
            name,
            hotkey ?? new KeyboardHotkey(
                HotkeyKey.F8,
                HotkeyModifiers.Control | HotkeyModifiers.Alt),
            new MacroStep[]
            {
                new KeyTapStep(KeyboardKey.M),
                new DelayStep(TimeSpan.FromMilliseconds(250)),
                new KeyTapStep(KeyboardKey.ArrowUp)
            });
    }

    private static void WriteCanonical(
        JsonSettingsStore store,
        string json,
        bool includeBom)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(store.SettingsPath)!);
        byte[] content = Utf8WithoutBom.GetBytes(json);
        if (includeBom)
        {
            content = Encoding.UTF8.GetPreamble().Concat(content).ToArray();
        }

        File.WriteAllBytes(store.SettingsPath, content);
    }

    private static bool HasUtf8Bom(byte[] bytes)
    {
        byte[] bom = Encoding.UTF8.GetPreamble();
        return bytes.AsSpan().StartsWith(bom);
    }

    private static string[] OwnTemporaryFiles(string directory)
    {
        return Directory.GetFiles(directory, ".settings.*.tmp")
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
    }

    private static void AssertMissing(SettingsLoadResult result)
    {
        Assert.Equal(SettingsLoadStatus.Missing, result.Status);
        Assert.Null(result.Settings);
        Assert.Null(result.JsonReadResult);
        Assert.Null(result.Error);
    }

    private static ApplicationSettings AssertLoaded(SettingsLoadResult result)
    {
        Assert.Equal(SettingsLoadStatus.Loaded, result.Status);
        ApplicationSettings settings = Assert.IsType<ApplicationSettings>(
            result.Settings);
        Assert.Null(result.JsonReadResult);
        Assert.Null(result.Error);
        return settings;
    }

    private static void AssertSaveSucceeded(SettingsSaveResult result)
    {
        Assert.True(result.IsSuccess);
        Assert.Null(result.Error);
    }

    private static void AssertSaveFailed(
        SettingsSaveResult result,
        SettingsStorageErrorCode expectedCode,
        string expectedPath)
    {
        Assert.False(result.IsSuccess);
        AssertStorageError(result.Error, expectedCode, expectedPath);
    }

    private static void AssertStorageError(
        SettingsStorageError? error,
        SettingsStorageErrorCode expectedCode,
        string expectedPath)
    {
        SettingsStorageError actual = Assert.IsType<SettingsStorageError>(error);
        Assert.Equal(expectedCode, actual.Code);
        Assert.Equal(expectedPath, actual.Path);
        Assert.False(string.IsNullOrWhiteSpace(actual.Message));
    }

    private static void AssertSettingsEqual(
        ApplicationSettings expected,
        ApplicationSettings actual)
    {
        Assert.Equal(expected.Macro.Name, actual.Macro.Name);
        Assert.Equal(expected.Macro.Hotkey, actual.Macro.Hotkey);
        Assert.Equal(
            expected.Macro.Sequence.Steps,
            actual.Macro.Sequence.Steps);
    }

    private static SettingsSaveResult SaveWithCreateFilesDenied(
        string directory,
        Func<SettingsSaveResult> save)
    {
        var directoryInfo = new DirectoryInfo(directory);
        DirectorySecurity security = directoryInfo.GetAccessControl(
            AccessControlSections.Access);
        string originalDescriptor = security.GetSecurityDescriptorSddlForm(
            AccessControlSections.Access);
        SecurityIdentifier user = WindowsIdentity.GetCurrent().User ??
            throw new InvalidOperationException("Current Windows user has no SID.");
        var denyCreateFiles = new FileSystemAccessRule(
            user,
            FileSystemRights.CreateFiles,
            InheritanceFlags.None,
            PropagationFlags.None,
            AccessControlType.Deny);
        security.AddAccessRule(denyCreateFiles);
        directoryInfo.SetAccessControl(security);

        try
        {
            return save();
        }
        finally
        {
            DirectorySecurity restore = directoryInfo.GetAccessControl(
                AccessControlSections.Access);
            restore.SetSecurityDescriptorSddlForm(
                originalDescriptor,
                AccessControlSections.Access);
            directoryInfo.SetAccessControl(restore);
        }
    }

    private sealed record UnsupportedStorageTestStep : MacroStep;
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class CurrentDirectorySensitiveCollection
{
    public const string Name = "Current directory sensitive tests";
}

[Collection(CurrentDirectorySensitiveCollection.Name)]
public sealed class JsonSettingsStoreCurrentDirectoryTests
{
    [Fact]
    public void DefaultConstructor_IsIndependentOfCurrentWorkingDirectory()
    {
        using var temporary = new TemporaryDirectory();
        string originalDirectory = Environment.CurrentDirectory;

        try
        {
            Environment.CurrentDirectory = temporary.Path;
            var store = new JsonSettingsStore();
            string expected = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "data",
                "settings.json"));

            Assert.Equal(expected, Path.GetFullPath(store.SettingsPath));
        }
        finally
        {
            Environment.CurrentDirectory = originalDirectory;
        }
    }
}
