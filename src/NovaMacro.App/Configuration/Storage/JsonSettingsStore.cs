using System.IO;
using System.Security;
using System.Text;
using NovaMacro.App.Configuration.Json;

namespace NovaMacro.App.Configuration.Storage;

internal sealed class JsonSettingsStore : ISettingsStore
{
    private const int MaximumSettingsFileBytes = 256 * 1024;
    private const int FileBufferSize = 4096;

    private static readonly UTF8Encoding Utf8WithoutBom = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    private static readonly byte[] Utf8Bom = { 0xEF, 0xBB, 0xBF };

    private readonly string _settingsDirectoryPath;

    internal JsonSettingsStore()
        : this(Path.Combine(
            AppContext.BaseDirectory,
            "data",
            "settings.json"))
    {
    }

    internal JsonSettingsStore(string settingsPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsPath);

        if (!Path.IsPathFullyQualified(settingsPath))
        {
            throw new ArgumentException(
                "The settings path must be absolute.",
                nameof(settingsPath));
        }

        string normalizedPath = Path.GetFullPath(settingsPath);
        string? directoryPath = Path.GetDirectoryName(normalizedPath);

        if (string.IsNullOrEmpty(directoryPath) ||
            string.IsNullOrEmpty(Path.GetFileName(normalizedPath)))
        {
            throw new ArgumentException(
                "The settings path must identify a file in an absolute directory.",
                nameof(settingsPath));
        }

        SettingsPath = normalizedPath;
        _settingsDirectoryPath = directoryPath;
    }

    public string SettingsPath { get; }

    public SettingsLoadResult Load()
    {
        byte[] content;

        try
        {
            using var stream = new FileStream(
                SettingsPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read | FileShare.Delete,
                FileBufferSize,
                FileOptions.SequentialScan);

            if (stream.Length > MaximumSettingsFileBytes)
            {
                return FileTooLarge();
            }

            content = ReadWithSizeLimit(stream, out bool exceededLimit);
            if (exceededLimit)
            {
                return FileTooLarge();
            }
        }
        catch (FileNotFoundException)
        {
            return SettingsLoadResult.Missing();
        }
        catch (DirectoryNotFoundException)
        {
            return SettingsLoadResult.Missing();
        }
        catch (Exception exception) when (IsExpectedFileSystemFailure(exception))
        {
            return SettingsLoadResult.Unavailable(new SettingsStorageError(
                SettingsStorageErrorCode.ReadFailed,
                SettingsPath,
                "Settings could not be read from the filesystem."));
        }

        string json;

        try
        {
            json = DecodeUtf8(content);
        }
        catch (DecoderFallbackException)
        {
            return SettingsLoadResult.Invalid(new SettingsStorageError(
                SettingsStorageErrorCode.InvalidUtf8,
                SettingsPath,
                "Settings must contain valid UTF-8 text."));
        }

        SettingsJsonReadResult jsonResult =
            SettingsJsonCodec.ReadV1OrV2OrV3(json);

        return jsonResult.IsSuccess
            ? SettingsLoadResult.Loaded(jsonResult.Settings!)
            : SettingsLoadResult.Invalid(jsonResult);
    }

    public SettingsSaveResult Save(ApplicationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        // Codec failures indicate a broken runtime invariant and deliberately
        // remain programmer errors rather than filesystem failures.
        string json = SettingsJsonCodec.WriteV3(settings);
        byte[] content = Utf8WithoutBom.GetBytes(json);

        try
        {
            Directory.CreateDirectory(_settingsDirectoryPath);
        }
        catch (Exception exception) when (IsExpectedFileSystemFailure(exception))
        {
            return SettingsSaveResult.Failure(new SettingsStorageError(
                SettingsStorageErrorCode.DirectoryCreationFailed,
                SettingsPath,
                "The settings directory could not be created."));
        }

        string temporaryPath = Path.Combine(
            _settingsDirectoryPath,
            $".settings.{Guid.NewGuid():N}.tmp");
        bool ownsTemporaryFile = false;

        try
        {
            try
            {
                using FileStream stream = CreateTemporaryFile(temporaryPath);
                ownsTemporaryFile = true;
                WriteAndFlushTemporaryFile(stream, content);
            }
            catch (Exception exception)
                when (IsExpectedFileSystemFailure(exception))
            {
                return SettingsSaveResult.Failure(new SettingsStorageError(
                    SettingsStorageErrorCode.TemporaryFileWriteFailed,
                    SettingsPath,
                    "Temporary settings content could not be written."));
            }

            try
            {
                PublishTemporaryFile(temporaryPath);
                ownsTemporaryFile = false;
            }
            catch (Exception exception)
                when (IsExpectedFileSystemFailure(exception))
            {
                return SettingsSaveResult.Failure(new SettingsStorageError(
                    SettingsStorageErrorCode.PublishFailed,
                    SettingsPath,
                    "Settings could not be published to the canonical path."));
            }

            return SettingsSaveResult.Success();
        }
        finally
        {
            if (ownsTemporaryFile)
            {
                TryDeleteOwnTemporaryFile(temporaryPath);
            }
        }
    }

    private static byte[] ReadWithSizeLimit(
        FileStream stream,
        out bool exceededLimit)
    {
        var buffer = new byte[MaximumSettingsFileBytes + 1];
        int totalBytesRead = 0;

        while (totalBytesRead < buffer.Length)
        {
            int bytesRead = stream.Read(
                buffer,
                totalBytesRead,
                buffer.Length - totalBytesRead);

            if (bytesRead == 0)
            {
                break;
            }

            totalBytesRead += bytesRead;
        }

        exceededLimit = totalBytesRead > MaximumSettingsFileBytes;

        if (totalBytesRead != buffer.Length)
        {
            Array.Resize(ref buffer, totalBytesRead);
        }

        return buffer;
    }

    private static string DecodeUtf8(byte[] content)
    {
        ReadOnlySpan<byte> bytes = content;

        if (bytes.StartsWith(Utf8Bom))
        {
            bytes = bytes[Utf8Bom.Length..];
        }

        return Utf8WithoutBom.GetString(bytes);
    }

    private static FileStream CreateTemporaryFile(string temporaryPath)
    {
        return new FileStream(
            temporaryPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            FileBufferSize,
            FileOptions.SequentialScan);
    }

    private static void WriteAndFlushTemporaryFile(
        FileStream stream,
        byte[] content)
    {
        stream.Write(content);
        stream.Flush(flushToDisk: true);
    }

    private void PublishTemporaryFile(string temporaryPath)
    {
        if (File.Exists(SettingsPath))
        {
            File.Move(
                temporaryPath,
                SettingsPath,
                overwrite: true);
        }
        else
        {
            File.Move(temporaryPath, SettingsPath);
        }
    }

    private static void TryDeleteOwnTemporaryFile(string temporaryPath)
    {
        try
        {
            File.Delete(temporaryPath);
        }
        catch (Exception exception) when (IsExpectedFileSystemFailure(exception))
        {
        }
    }

    private SettingsLoadResult FileTooLarge()
    {
        return SettingsLoadResult.Invalid(new SettingsStorageError(
            SettingsStorageErrorCode.FileTooLarge,
            SettingsPath,
            "Settings file exceeds the 256 KiB size limit."));
    }

    private static bool IsExpectedFileSystemFailure(Exception exception)
    {
        return exception is IOException or
            UnauthorizedAccessException or
            SecurityException or
            NotSupportedException;
    }
}
