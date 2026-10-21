using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using PriorState.Plugins.Abstractions;

namespace PriorState.Plugins.Git;

public sealed class GitCapturePlugin(IOptions<GitOptions> options, ILogger<GitCapturePlugin>? logger = null) : ICapturePlugin
{
    private readonly GitOptions _options = options.Value;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public string Id => "git";
    public string DisplayName => "Git repository";
    public PluginDefinition Definition => new("Archives a commit's complete file tree as ZIP; submodules and LFS pointers are not fetched.",
        [new("url", "Repository HTTPS URL", Required: true), new("reference", "Branch, tag or commit", DefaultValue: "HEAD"),
         new("username", "Authentication username", DefaultValue: "git")], "Optional HTTPS personal access token.");

    public void ValidateConfiguration(string configurationJson, string bindingName, string? secretRef) => Parse(configurationJson);

    private static GitConfiguration Parse(string json)
    {
        GitConfiguration? config;
        try { config = JsonSerializer.Deserialize<GitConfiguration>(json, JsonOptions); }
        catch (JsonException) { throw new PluginException("Git configuration must be valid JSON."); }
        if (config is null || !Uri.TryCreate(config.Url, UriKind.Absolute, out var uri)
            || uri.Scheme != "https" || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0 || uri.Query.Length != 0)
            throw new PluginException("Enter an HTTPS repository URL without credentials, query or fragment.");
        if (string.IsNullOrWhiteSpace(config.Reference) || config.Reference.StartsWith('-')
            || config.Reference.Any(c => char.IsControl(c) || char.IsWhiteSpace(c) || "~^:?*[\\".Contains(c))
            || config.Reference.Contains("..", StringComparison.Ordinal) || config.Reference.Contains("@{", StringComparison.Ordinal)
            || config.Reference.EndsWith('.') || config.Reference.EndsWith('/') || config.Reference.Contains("//", StringComparison.Ordinal))
            throw new PluginException("Enter a branch, tag or commit; revision expressions and refspecs are not supported.");
        if (string.IsNullOrWhiteSpace(config.Username) || config.Username.Any(char.IsControl))
            throw new PluginException("Enter an authentication username without control characters.");
        return config;
    }

    public async Task<PluginPayload> ExecuteAsync(PluginExecutionContext context, CancellationToken cancellationToken = default)
    {
        var config = Parse(context.Binding.ConfigurationJson);
        var uri = new Uri(config.Url);
        if (_options.AllowedHosts.Count == 0 || !_options.AllowedHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase))
            throw new PluginException("Repository host is not in Plugins:Git:AllowedHosts.");
        if (_options.MaxPayloadBytes <= 0 || _options.MaxTemporaryBytes <= 0 || _options.MaxFiles <= 0 || _options.Timeout <= TimeSpan.Zero)
            throw new PluginException("Git resource limits must be positive.");
        var directory = Path.Combine(Path.GetTempPath(), "priorstate-git-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_options.Timeout);
        try
        {
            var repo = Path.Combine(directory, "repo");
            var askpass = Path.Combine(directory, OperatingSystem.IsWindows() ? "askpass.cmd" : "askpass.sh");
            await File.WriteAllTextAsync(askpass, OperatingSystem.IsWindows()
                ? "@echo off\r\npowershell.exe -NoProfile -NonInteractive -Command \"[Console]::WriteLine($env:PRIORSTATE_GIT_TOKEN)\"\r\n"
                : "#!/bin/sh\nprintf '%s\\n' \"$PRIORSTATE_GIT_TOKEN\"\n", deadline.Token);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(askpass, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            await RunAsync(directory, askpass, context.Secret, ["init", "--bare", repo], 65536, deadline.Token);
            var remote = context.Secret is null ? config.Url : new UriBuilder(uri) { UserName = config.Username }.Uri.AbsoluteUri;
            await RunAsync(directory, askpass, context.Secret, ["-c", "credential.username=" + config.Username, "-C", repo, "fetch", "--depth=1", "--no-tags", "--", remote, config.Reference], 65536, deadline.Token);
            var payload = await ArchiveAsync(repo, directory, askpass, config, context.Secret, deadline.Token);
            return new PluginPayload { Url = config.Url, MediaType = "application/zip", Content = payload, CapturedAtUtc = DateTimeOffset.UtcNow };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new PluginException("Git capture exceeded its time limit."); }
        finally
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
                    File.SetAttributes(file, FileAttributes.Normal);
                Directory.Delete(directory, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (logger is not null) CleanupWarning(logger, null);
            }
        }
    }

    internal async Task<byte[]> ArchiveAsync(string repo, string temporary, string askpass, GitConfiguration config, string? secret, CancellationToken token)
    {
        var commit = Encoding.UTF8.GetString(await RunAsync(temporary, askpass, secret, ["-C", repo, "rev-parse", "--verify", "FETCH_HEAD^{commit}"], 128, token)).Trim();
        var tree = await RunAsync(temporary, askpass, secret, ["-C", repo, "ls-tree", "-r", "-z", "--full-tree", commit], Math.Min(_options.MaxPayloadBytes, 8 * 1024 * 1024), token);
        string treeText;
        try { treeText = new UTF8Encoding(false, true).GetString(tree); }
        catch (DecoderFallbackException) { throw new PluginException("Git filenames must be valid UTF-8 for this archive format."); }
        var files = treeText.Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => { var tab = line.IndexOf('\t'); var fields = line[..tab].Split(' '); return new GitEntry(line[(tab + 1)..], fields[0], fields[1], fields[2]); })
            .OrderBy(e => e.Path, StringComparer.Ordinal).ToArray();
        if (files.Length > _options.MaxFiles) throw new PluginException("Git repository exceeds its file count limit.");
        using var output = new MemoryStream();
        long contentBytes = 0;
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in files)
            {
                if (entry.Path.Split('/').Any(p => p is ".." or "." or "") || entry.Path.Contains('\\') || entry.Path.StartsWith('/'))
                    throw new PluginException("Git tree contains a path that cannot safely be archived.");
                if (entry.Type != "blob") continue;
                var bytes = await RunAsync(temporary, askpass, secret, ["-C", repo, "cat-file", "blob", entry.ObjectId], _options.MaxPayloadBytes - contentBytes, token);
                contentBytes += bytes.Length;
                WriteEntry(zip, "files/" + entry.Path, bytes);
            }
            WriteEntry(zip, "manifest.json", JsonSerializer.SerializeToUtf8Bytes(new { formatVersion = 1, repository = config.Url, reference = config.Reference, commit, entries = files }, JsonOptions));
        }
        if (output.Length > _options.MaxPayloadBytes) throw new PluginException("Git archive exceeds its payload limit.");
        return output.ToArray();
    }

    private static void WriteEntry(ZipArchive zip, string path, byte[] bytes)
    {
        var entry = zip.CreateEntry(path, CompressionLevel.NoCompression);
        entry.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
        using var stream = entry.Open();
        stream.Write(bytes);
    }

    private async Task<byte[]> RunAsync(string temporary, string askpass, string? secret, string[] arguments, long maxBytes, CancellationToken token)
    {
        var start = new ProcessStartInfo(_options.GitExecutable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var arg in new[] { "-c", "http.followRedirects=false", "-c", "credential.helper=", "-c", "protocol.allow=never", "-c", "protocol.https.allow=always", "-c", "core.hooksPath=" + Path.Combine(temporary, "no-hooks") }.Concat(arguments)) start.ArgumentList.Add(arg);
        foreach (var name in start.Environment.Keys.Where(k => k.StartsWith("GIT_", StringComparison.OrdinalIgnoreCase)).ToArray()) start.Environment.Remove(name);
        start.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        start.Environment["GIT_CONFIG_GLOBAL"] = OperatingSystem.IsWindows() ? "NUL" : "/dev/null";
        start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        start.Environment["GIT_ASKPASS"] = askpass;
        start.Environment["PRIORSTATE_GIT_TOKEN"] = secret ?? "";
        using var process = Process.Start(start) ?? throw new PluginException("Git executable could not be started.");
        try
        {
            var stdout = ReadBoundedAsync(process.StandardOutput.BaseStream, maxBytes, token);
            var stderr = ReadBoundedAsync(process.StandardError.BaseStream, 65536, token);
            while (!process.HasExited)
            {
                token.ThrowIfCancellationRequested();
                if (stdout.IsFaulted) await stdout;
                if (stderr.IsFaulted) await stderr;
                CheckTemporarySize(temporary);
                await Task.Delay(25, token);
            }
            await Task.WhenAll(stdout, stderr);
            CheckTemporarySize(temporary);
            if (process.ExitCode != 0) throw new PluginException("Git command failed. Check repository access and reference.");
            return await stdout;
        }
        finally
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { /* The process exited between the check and kill. */ }
            await process.WaitForExitAsync(CancellationToken.None);
        }
    }

    private static readonly Action<ILogger, Exception?> CleanupWarning = LoggerMessage.Define(
        LogLevel.Warning, new EventId(6450), "Git capture temporary files could not be removed; check worker temporary storage.");

    private void CheckTemporarySize(string temporary)
    {
        long size = 0;
        foreach (var file in Directory.EnumerateFiles(temporary, "*", SearchOption.AllDirectories))
        {
            try { size += new FileInfo(file).Length; }
            catch (FileNotFoundException) { continue; } // Git renames its pack and lock files.
            if (size > _options.MaxTemporaryBytes) throw new PluginException("Git fetch exceeds its temporary storage limit.");
        }
    }

    private static async Task<byte[]> ReadBoundedAsync(Stream stream, long limit, CancellationToken token)
    {
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(buffer, token)) > 0)
        {
            if (output.Length + read > limit) throw new PluginException("Git output exceeds its size limit.");
            await output.WriteAsync(buffer.AsMemory(0, read), token);
        }
        return output.ToArray();
    }
}

internal sealed record GitEntry(string Path, string Mode, string Type, string ObjectId);
internal sealed class GitConfiguration
{
    public string Url { get; set; } = "";
    public string Reference { get; set; } = "HEAD";
    public string Username { get; set; } = "git";
}
