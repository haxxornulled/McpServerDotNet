using System.Buffers;
using System.Text;
using LanguageExt;
using LanguageExt.Common;
using McpServer.Application.Abstractions.Files;
using McpServer.Application.Files.Commands;
using McpServer.Application.Files.Results;
using Microsoft.Extensions.Logging;
using static LanguageExt.Prelude;

using DirectoryEntry = McpServer.Application.Files.DirectoryEntry;

namespace McpServer.Infrastructure.Files;

public sealed class FileSystemService(
    IPathPolicy pathPolicy,
    IFileMutationLockProvider lockProvider,
    IDestructiveFileOperationPolicy destructivePolicy,
    ILogger<FileSystemService> logger,
    IWorkspaceChangeFeed? changeFeed = null) : IFileSystemService
{
    public FileSystemService(
        IPathPolicy pathPolicy,
        IFileMutationLockProvider lockProvider,
        ILogger<FileSystemService> logger,
        IWorkspaceChangeFeed? changeFeed = null)
        : this(
            pathPolicy,
            lockProvider,
            new DestructiveFileOperationPolicy(pathPolicy),
            logger,
            changeFeed)
    {
    }

    public async ValueTask<Fin<FileTextResult>> ReadTextAsync(ReadFileTextCommand command, CancellationToken ct)
    {
        var normalized = pathPolicy.NormalizeAndValidateReadPath(command.Path);
        if (normalized.IsFail)
        {
            return PropagateFailure<FileTextResult>(normalized);
        }

        var path = GetPathOrThrow(normalized);

        try
        {
            if (!File.Exists(path))
            {
                return Error.New($"File not found: {path}");
            }

            var encoding = ResolveEncoding(command.Encoding);

            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite,
                bufferSize: 64 * 1024,
                options: FileOptions.Asynchronous | FileOptions.SequentialScan);

            using var reader = new StreamReader(
                stream,
                encoding,
                detectEncodingFromByteOrderMarks: true,
                bufferSize: 16 * 1024,
                leaveOpen: false);

            var content = await reader.ReadToEndAsync(ct).ConfigureAwait(false);

            logger.LogInformation("Read text file {NormalizedPath} length {Length}", path, stream.Length);

            return new FileTextResult(path, content);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed reading text file {NormalizedPath}", path);
            return Error.New(ex.Message);
        }
    }

    public ValueTask<Fin<DirectoryListingResult>> ListDirectoryAsync(ListDirectoryCommand command, CancellationToken ct)
    {
        var normalized = pathPolicy.NormalizeAndValidateReadPath(command.Path);
        if (normalized.IsFail)
        {
            return ValueTask.FromResult(PropagateFailure<DirectoryListingResult>(normalized));
        }

        var path = GetPathOrThrow(normalized);

        try
        {
            if (!Directory.Exists(path))
            {
                return ValueTask.FromResult<Fin<DirectoryListingResult>>(Error.New($"Directory not found: {path}"));
            }

            var entries = new List<DirectoryEntry>();
            foreach (var entryPath in Directory.EnumerateFileSystemEntries(path, command.SearchPattern ?? "*", SearchOption.TopDirectoryOnly))
            {
                var attributes = File.GetAttributes(entryPath);
                var isDirectory = (attributes & FileAttributes.Directory) != 0;
                entries.Add(new DirectoryEntry(Path.GetFileName(entryPath), isDirectory));
            }

            logger.LogInformation("Listed directory {NormalizedPath} with {EntryCount} entries", path, entries.Count);

                return ValueTask.FromResult<Fin<DirectoryListingResult>>(new DirectoryListingResult(path, entries.ToArray()));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed listing directory {NormalizedPath}", path);
            return ValueTask.FromResult<Fin<DirectoryListingResult>>(Error.New(ex.Message));
        }
    }

    public ValueTask<Fin<FileMetadataResult>> GetMetadataAsync(GetMetadataCommand command, CancellationToken ct)
    {
        var normalized = pathPolicy.NormalizeAndValidateReadPath(command.Path);
        if (normalized.IsFail)
        {
            return ValueTask.FromResult(PropagateFailure<FileMetadataResult>(normalized));
        }

        var path = GetPathOrThrow(normalized);

        try
        {
            if (File.Exists(path))
            {
                var info = new FileInfo(path);
                return ValueTask.FromResult<Fin<FileMetadataResult>>(new FileMetadataResult(
                    Path: path,
                    Exists: true,
                    IsDirectory: false,
                    Size: info.Length,
                    CreationTime: info.CreationTime,
                    LastWriteTime: info.LastWriteTime,
                    Attributes: info.Attributes.ToString()));
            }

            if (Directory.Exists(path))
            {
                var info = new DirectoryInfo(path);
                return ValueTask.FromResult<Fin<FileMetadataResult>>(new FileMetadataResult(
                    Path: path,
                    Exists: true,
                    IsDirectory: true,
                    Size: null,
                    CreationTime: info.CreationTime,
                    LastWriteTime: info.LastWriteTime,
                    Attributes: info.Attributes.ToString()));
            }

            return ValueTask.FromResult<Fin<FileMetadataResult>>(new FileMetadataResult(
                path,
                false,
                false,
                null,
                DateTime.MinValue,
                DateTime.MinValue,
                string.Empty));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed reading metadata for {NormalizedPath}", path);
            return ValueTask.FromResult<Fin<FileMetadataResult>>(Error.New(ex.Message));
        }
    }

    public async ValueTask<Fin<FileTextResult>> WriteTextAsync(WriteFileTextCommand command, CancellationToken ct)
    {
        var normalized = pathPolicy.NormalizeAndValidateWritePath(command.Path);
        if (normalized.IsFail)
        {
            return PropagateFailure<FileTextResult>(normalized);
        }

        var path = GetPathOrThrow(normalized);
        var writePolicy = destructivePolicy.ValidateWrite(path, command.Overwrite && File.Exists(path));
        if (writePolicy.IsFail)
        {
            return PropagateFailure<FileTextResult>(writePolicy);
        }

        await using var _ = await lockProvider.AcquireAsync(path, ct).ConfigureAwait(false);

        try
        {
            EnsureDestinationParentExists(path);

            if (!command.Overwrite && File.Exists(path))
            {
                return Error.New($"File already exists and overwrite is false: {path}");
            }

            var encoding = ResolveEncoding(command.Encoding);
            await File.WriteAllTextAsync(path, command.Content, encoding, ct).ConfigureAwait(false);

            logger.LogInformation("Wrote text file {NormalizedPath} bytes {ByteLength}", path, command.Content.Length);
            changeFeed?.RecordChange("write", path, $"bytes={command.Content.Length}");

            return new FileTextResult(path, command.Content);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed writing text file {NormalizedPath}", path);
            return Error.New(ex.Message);
        }
    }

    public async ValueTask<Fin<FileTextResult>> AppendTextAsync(AppendFileTextCommand command, CancellationToken ct)
    {
        var normalized = pathPolicy.NormalizeAndValidateWritePath(command.Path);
        if (normalized.IsFail)
        {
            return PropagateFailure<FileTextResult>(normalized);
        }

        var path = GetPathOrThrow(normalized);
        var appendPolicy = destructivePolicy.ValidateAppend(path);
        if (appendPolicy.IsFail)
        {
            return PropagateFailure<FileTextResult>(appendPolicy);
        }

        await using var _ = await lockProvider.AcquireAsync(path, ct).ConfigureAwait(false);

        try
        {
            EnsureDestinationParentExists(path);

            var encoding = ResolveEncoding(command.Encoding);
            var byteCount = encoding.GetByteCount(command.Content);
            var rented = ArrayPool<byte>.Shared.Rent(byteCount);

            try
            {
                var written = encoding.GetBytes(command.Content.AsSpan(), rented.AsSpan(0, byteCount));

                await using var stream = new FileStream(
                    path,
                    FileMode.Append,
                    FileAccess.Write,
                    FileShare.Read,
                    bufferSize: 64 * 1024,
                    options: FileOptions.Asynchronous | FileOptions.SequentialScan);

                await stream.WriteAsync(rented.AsMemory(0, written), ct).ConfigureAwait(false);

                if (command.Flush)
                {
                    await stream.FlushAsync(ct).ConfigureAwait(false);
                }

                logger.LogInformation("Appended text file {NormalizedPath} bytes {ByteLength}", path, written);
                changeFeed?.RecordChange("append", path, $"bytes={written}");
                return new FileTextResult(path, command.Content);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed appending text file {NormalizedPath}", path);
            return Error.New(ex.Message);
        }
    }

    public async ValueTask<Fin<Unit>> CreateDirectoryAsync(CreateDirectoryCommand command, CancellationToken ct)
    {
        var normalized = pathPolicy.NormalizeAndValidateWritePath(command.Path);
        if (normalized.IsFail)
        {
            return PropagateFailure<Unit>(normalized);
        }

        var path = GetPathOrThrow(normalized);
        await using var _ = await lockProvider.AcquireAsync(path, ct).ConfigureAwait(false);

        try
        {
            Directory.CreateDirectory(path);
            logger.LogInformation("Created directory {NormalizedPath}", path);
            changeFeed?.RecordChange("create_directory", path);
            return unit;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed creating directory {NormalizedPath}", path);
            return Error.New(ex.Message);
        }
    }

    public async ValueTask<Fin<Unit>> MovePathAsync(MovePathCommand command, CancellationToken ct)
    {
        var sourceFin = pathPolicy.NormalizeAndValidateWritePath(command.SourcePath);
        if (sourceFin.IsFail)
        {
            return PropagateFailure<Unit>(sourceFin);
        }

        var destinationFin = pathPolicy.NormalizeAndValidateWritePath(command.DestinationPath);
        if (destinationFin.IsFail)
        {
            return PropagateFailure<Unit>(destinationFin);
        }

        var sourcePath = GetPathOrThrow(sourceFin);
        var destinationPath = GetPathOrThrow(destinationFin);

        if (string.Equals(sourcePath, destinationPath, PathComparison.Comparison))
        {
            return Error.New("Source and destination paths are the same");
        }

        var movePolicy = destructivePolicy.ValidateMove(sourcePath, destinationPath, command.Overwrite);
        if (movePolicy.IsFail)
        {
            return PropagateFailure<Unit>(movePolicy);
        }

        await using var _ = await lockProvider.AcquireManyAsync([sourcePath, destinationPath], ct).ConfigureAwait(false);

        try
        {
            EnsureSourceExists(sourcePath);
            EnsureDestinationParentExists(destinationPath);

            if (File.Exists(sourcePath))
            {
                File.Move(sourcePath, destinationPath, overwrite: command.Overwrite);
            }
            else if (Directory.Exists(sourcePath))
            {
                if (File.Exists(destinationPath) || Directory.Exists(destinationPath))
                {
                    if (!command.Overwrite)
                    {
                        return Error.New($"Destination already exists: {destinationPath}");
                    }

                    DeleteExistingDestination(destinationPath, recursive: true);
                }

                Directory.Move(sourcePath, destinationPath);
            }
            else
            {
                return Error.New($"Source path not found: {sourcePath}");
            }

            logger.LogInformation("Moved path from {SourcePath} to {DestinationPath}", sourcePath, destinationPath);
            changeFeed?.RecordChange("move", destinationPath, $"from={sourcePath}");
            return unit;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed moving path from {SourcePath} to {DestinationPath}", sourcePath, destinationPath);
            return Error.New(ex.Message);
        }
    }

    public async ValueTask<Fin<Unit>> CopyPathAsync(CopyPathCommand command, CancellationToken ct)
    {
        var sourceFin = pathPolicy.NormalizeAndValidateReadPath(command.SourcePath);
        if (sourceFin.IsFail)
        {
            return PropagateFailure<Unit>(sourceFin);
        }

        var destinationFin = pathPolicy.NormalizeAndValidateWritePath(command.DestinationPath);
        if (destinationFin.IsFail)
        {
            return PropagateFailure<Unit>(destinationFin);
        }

        var sourcePath = GetPathOrThrow(sourceFin);
        var destinationPath = GetPathOrThrow(destinationFin);

        if (string.Equals(sourcePath, destinationPath, PathComparison.Comparison))
        {
            return Error.New("Source and destination paths are the same");
        }

        var copyPolicy = destructivePolicy.ValidateCopy(sourcePath, destinationPath, command.Overwrite, command.Recursive);
        if (copyPolicy.IsFail)
        {
            return PropagateFailure<Unit>(copyPolicy);
        }

        await using var _ = await lockProvider.AcquireManyAsync([sourcePath, destinationPath], ct).ConfigureAwait(false);

        try
        {
            EnsureSourceExists(sourcePath);
            EnsureDestinationParentExists(destinationPath);

            if (File.Exists(destinationPath) || Directory.Exists(destinationPath))
            {
                if (!command.Overwrite)
                {
                    return Error.New($"Destination already exists: {destinationPath}");
                }

                DeleteExistingDestination(destinationPath, recursive: true);
            }

            if (File.Exists(sourcePath))
            {
                File.Copy(sourcePath, destinationPath, overwrite: command.Overwrite);
            }
            else if (Directory.Exists(sourcePath))
            {
                if (!command.Recursive)
                {
                    return Error.New("Directory copy requires recursive=true");
                }

                CopyDirectoryRecursive(sourcePath, destinationPath, command.Overwrite);
            }
            else
            {
                return Error.New($"Source path not found: {sourcePath}");
            }

            logger.LogInformation("Copied path from {SourcePath} to {DestinationPath}", sourcePath, destinationPath);
            changeFeed?.RecordChange("copy", destinationPath, $"from={sourcePath}");
            return unit;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed copying path from {SourcePath} to {DestinationPath}", sourcePath, destinationPath);
            return Error.New(ex.Message);
        }
    }

    public async ValueTask<Fin<Unit>> DeletePathAsync(DeletePathCommand command, CancellationToken ct)
    {
        var normalizedFin = pathPolicy.NormalizeAndValidateWritePath(command.Path);
        if (normalizedFin.IsFail)
        {
            return PropagateFailure<Unit>(normalizedFin);
        }

        var path = GetPathOrThrow(normalizedFin);
        var deletePolicy = destructivePolicy.ValidateDelete(path, command.Recursive, command.Confirmation);
        if (deletePolicy.IsFail)
        {
            return PropagateFailure<Unit>(deletePolicy);
        }

        await using var _ = await lockProvider.AcquireAsync(path, ct).ConfigureAwait(false);

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
                logger.LogInformation("Deleted file {NormalizedPath}", path);
                changeFeed?.RecordChange("delete", path);
                return unit;
            }

            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: command.Recursive);
                logger.LogInformation("Deleted directory {NormalizedPath} recursive={Recursive}", path, command.Recursive);
                changeFeed?.RecordChange("delete", path, $"recursive={command.Recursive}");
                return unit;
            }

            return Error.New($"Path not found: {path}");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed deleting path {NormalizedPath}", path);
            return Error.New(ex.Message);
        }
    }

    private static void EnsureSourceExists(string sourcePath)
    {
        if (!File.Exists(sourcePath) && !Directory.Exists(sourcePath))
        {
            throw new FileNotFoundException($"Source path does not exist: {sourcePath}");
        }
    }

    private static void EnsureDestinationParentExists(string destinationPath)
    {
        var parent = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(parent))
        {
            Directory.CreateDirectory(parent);
        }
    }

    private static void DeleteExistingDestination(string destinationPath, bool recursive)
    {
        if (File.Exists(destinationPath))
        {
            File.Delete(destinationPath);
            return;
        }

        if (Directory.Exists(destinationPath))
        {
            Directory.Delete(destinationPath, recursive);
        }
    }

    private static void CopyDirectoryRecursive(string sourceDir, string destinationDir, bool overwrite)
    {
        var sourceInfo = new DirectoryInfo(sourceDir);

        if (!sourceInfo.Exists)
        {
            throw new DirectoryNotFoundException($"Source directory not found: {sourceDir}");
        }

        Directory.CreateDirectory(destinationDir);

        foreach (var file in sourceInfo.EnumerateFiles())
        {
            var targetFilePath = Path.Combine(destinationDir, file.Name);
            file.CopyTo(targetFilePath, overwrite);
        }

        foreach (var subDirectory in sourceInfo.EnumerateDirectories())
        {
            var nextDestination = Path.Combine(destinationDir, subDirectory.Name);
            CopyDirectoryRecursive(subDirectory.FullName, nextDestination, overwrite);
        }
    }

    private static Encoding ResolveEncoding(string? encodingName) =>
        string.IsNullOrWhiteSpace(encodingName) ? Encoding.UTF8 : Encoding.GetEncoding(encodingName);

    private static Fin<T> PropagateFailure<T>(Fin<string> result) =>
        result.Match<Fin<T>>(
            Succ: _ => throw new InvalidOperationException("Expected path validation to fail."),
            Fail: error => error);

    private static Fin<T> PropagateFailure<T>(Fin<Unit> result) =>
        result.Match<Fin<T>>(
            Succ: _ => throw new InvalidOperationException("Expected file operation policy validation to fail."),
            Fail: error => error);

    private static string GetPathOrThrow(Fin<string> result) =>
        result.Match(
            Succ: path => path,
            Fail: error => throw new InvalidOperationException(error.Message));
}
