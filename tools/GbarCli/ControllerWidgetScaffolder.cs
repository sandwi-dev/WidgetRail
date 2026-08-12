using System.Text;
using System.Text.Json;

namespace GameBarAlternative.GbarCli;

internal static class ControllerWidgetScaffolder
{
    internal static int SupportedTemplateVersion =>
        WidgetSdkReleaseContract.Current.TemplateVersion;
    internal const int MaximumFiles = 64;
    internal const int MaximumManifestBytes = 64 * 1024;
    internal const int MaximumFileBytes = 1024 * 1024;
    internal const int MaximumAggregateBytes = 4 * 1024 * 1024;
    private const int MaximumRelativePathLength = 240;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static async Task<int> GenerateAsync(
        string templateRoot,
        string profile,
        string target,
        IReadOnlyDictionary<string, string> replacements,
        LocalWidgetSdkBundle sdkPackage,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        ArgumentNullException.ThrowIfNull(replacements);
        ArgumentNullException.ThrowIfNull(sdkPackage);

        var fullTarget = Path.GetFullPath(target);
        if (File.Exists(fullTarget) || Directory.Exists(fullTarget))
            throw ExistingDestination(fullTarget);

        var template = await LoadAsync(templateRoot, profile, replacements, cancellationToken)
            .ConfigureAwait(false);
        var parent = Path.GetDirectoryName(fullTarget)
            ?? throw new CliUsageException(
                $"Output path has no writable parent: {fullTarget}. Choose a normal local directory.");
        try
        {
            Directory.CreateDirectory(parent);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new CliUsageException(
                $"Could not prepare the output parent '{parent}': {exception.Message} Choose a writable local destination and retry.");
        }
        var targetName = Path.GetFileName(fullTarget);
        var staging = Path.Combine(
            parent,
            $".{targetName}.gbar-stage-{Guid.NewGuid():N}");
        var published = false;
        Exception? failure = null;

        try
        {
            Directory.CreateDirectory(staging);
            foreach (var file in template.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var destination = Path.Combine(
                    staging,
                    file.Destination.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                try
                {
                    await File.WriteAllBytesAsync(destination, file.Content, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    throw new CliUsageException(
                        $"Could not stage template destination '{file.Destination}': {exception.Message} " +
                        "Choose a writable output directory and retry.");
                }
            }

            var feed = Path.Combine(staging, ".gbar", "packages");
            Directory.CreateDirectory(feed);
            await File.WriteAllBytesAsync(
                    Path.Combine(feed, sdkPackage.FileName),
                    sdkPackage.Content,
                    cancellationToken)
                .ConfigureAwait(false);

            var validationOutput = new StringWriter();
            var validationError = new StringWriter();
            var validationCode = await ValidateCommand.RunAsync(
                    [staging], validationOutput, validationError)
                .ConfigureAwait(false);
            if (validationCode != 0)
            {
                var detail = SingleLine(validationError.ToString());
                throw new CliUsageException(
                    $"The checked-in ControllerWidget template generated an invalid scaffold: {detail} " +
                    "Repair the declared template file and retry; no project was published.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(fullTarget) || Directory.Exists(fullTarget))
                throw ExistingDestination(fullTarget);
            try
            {
                Directory.Move(staging, fullTarget);
                published = true;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                throw new CliUsageException(
                    $"Could not publish the completed scaffold to '{fullTarget}': {exception.Message} " +
                    "Choose a new writable destination and retry; no existing output was changed.");
            }

            return template.Files.Count + 1;
        }
        catch (Exception exception)
        {
            failure = exception;
            throw;
        }
        finally
        {
            if (!published && Directory.Exists(staging))
            {
                try
                {
                    Directory.Delete(staging, recursive: true);
                }
                catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
                {
                    if (failure is null)
                        throw;
                    throw new CliOperationException(
                        $"Scaffold generation failed and staging cleanup could not remove '{staging}'. " +
                        "Remove that gbar-owned staging directory before retrying.",
                        failure);
                }
            }
        }
    }

    private static async Task<LoadedTemplate> LoadAsync(
        string templateRoot,
        string profile,
        IReadOnlyDictionary<string, string> replacements,
        CancellationToken cancellationToken)
    {
        var root = Path.GetFullPath(templateRoot);
        if (!Directory.Exists(root))
            throw new CliUsageException(
                $"ControllerWidget template directory does not exist: {root}. Rebuild or reinstall gbar, or correct GBAR_TEMPLATE_ROOT.");
        RejectReparse(root, "template root");
        var manifestPath = Path.Combine(root, "template.json");
        if (!File.Exists(manifestPath))
            throw new CliUsageException(
                $"ControllerWidget template manifest is missing: {manifestPath}. Rebuild or reinstall gbar, or correct GBAR_TEMPLATE_ROOT.");
        RejectReparse(manifestPath, "template manifest");
        var manifestInfo = new FileInfo(manifestPath);
        if (manifestInfo.Length is <= 0 or > MaximumManifestBytes)
            throw new CliUsageException(
                $"Template manifest 'template.json' must be between 1 and {MaximumManifestBytes} bytes. Repair the manifest and retry.");

        byte[] manifestBytes;
        try
        {
            manifestBytes = await File.ReadAllBytesAsync(manifestPath, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new CliUsageException(
                $"Template manifest 'template.json' could not be read: {exception.Message} Repair its permissions and retry.");
        }

        TemplateManifest manifest;
        try
        {
            manifest = ParseManifest(manifestBytes, profile);
        }
        catch (JsonException exception)
        {
            throw new CliUsageException(
                $"Template manifest 'template.json' is invalid JSON: {SingleLine(exception.Message)} Repair the manifest and retry.");
        }

        var tree = CaptureTree(root);
        var declaredSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long inventoryBytes = 0;
        foreach (var declaration in manifest.AllFiles)
        {
            var source = ValidateRelativePath(
                declaration.Source, "source", allowWidgetNameToken: false);
            if (!declaredSources.Add(source)) continue;
            if (!tree.Files.Contains(source))
                throw new CliUsageException(
                    $"Template inventory declares missing file '{source}'. Restore the file or remove its declaration.");
            var info = new FileInfo(Path.Combine(
                root, source.Replace('/', Path.DirectorySeparatorChar)));
            if (info.Length < 0 || info.Length > MaximumFileBytes)
                throw new CliUsageException(
                    $"Template file '{source}' exceeds the {MaximumFileBytes}-byte per-file bound. Reduce the file and retry.");
            inventoryBytes += info.Length;
            if (inventoryBytes > MaximumAggregateBytes)
                throw new CliUsageException(
                    $"Declared template files exceed the {MaximumAggregateBytes}-byte aggregate bound. Remove or reduce declared files.");
        }
        var destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var loaded = new List<LoadedTemplateFile>(manifest.Files.Count);

        foreach (var declaration in manifest.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = ValidateRelativePath(declaration.Source, "source", allowWidgetNameToken: false);
            if (!tree.Files.Contains(source))
                throw new CliUsageException(
                    $"Template manifest declares missing file '{source}'. Restore the file or remove its declaration.");

            var destinationTemplate = ValidateRelativePath(
                declaration.Destination, "destination", allowWidgetNameToken: true);
            var destination = ApplyDestinationReplacement(destinationTemplate, replacements);
            destination = ValidateRelativePath(destination, "replacement destination", allowWidgetNameToken: false);
            if (destination.StartsWith(".gbar/", StringComparison.OrdinalIgnoreCase) ||
                destination.Equals(".gbar", StringComparison.OrdinalIgnoreCase))
                throw new CliUsageException(
                    $"Template destination '{destination}' conflicts with the reserved local SDK feed. Choose another destination.");
            if (!destinations.Add(destination))
                throw new CliUsageException(
                    $"Template destination '{destination}' is produced more than once. Give every declared file a unique destination.");

            var sourcePath = Path.Combine(root, source.Replace('/', Path.DirectorySeparatorChar));
            var info = new FileInfo(sourcePath);
            if (info.Length < 0 || info.Length > MaximumFileBytes)
                throw new CliUsageException(
                    $"Template file '{source}' exceeds the {MaximumFileBytes}-byte per-file bound. Reduce the file and retry.");
            byte[] content;
            try
            {
                content = await File.ReadAllBytesAsync(sourcePath, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                throw new CliUsageException(
                    $"Declared template file '{source}' could not be read: {exception.Message} Repair the file or its permissions and retry.");
            }
            if (content.LongLength != info.Length)
                throw new CliUsageException(
                    $"Declared template file '{source}' changed while it was read. Restore a stable template directory and retry.");

            if (declaration.Kind == TemplateFileKind.Text)
            {
                string text;
                try
                {
                    text = StrictUtf8.GetString(content);
                }
                catch (DecoderFallbackException)
                {
                    throw new CliUsageException(
                        $"Declared text template '{source}' is not valid UTF-8. Save it as UTF-8 or mark a non-template asset as binary.");
                }
                foreach (var replacement in replacements)
                    text = text.Replace(replacement.Key, replacement.Value, StringComparison.Ordinal);
                if (text.Contains("{{", StringComparison.Ordinal))
                    throw new CliUsageException(
                        $"Declared text template '{source}' contains an unknown replacement token. Use only the supported manifest replacements.");
                content = StrictUtf8.GetBytes(text);
                if (content.Length > MaximumFileBytes)
                    throw new CliUsageException(
                        $"Rendered text template '{source}' exceeds the {MaximumFileBytes}-byte output bound. Shorten the widget identity values or template content.");
            }
            loaded.Add(new(source, destination, declaration.Kind, content));
        }

        var undeclared = tree.Files
            .Where(path => !path.Equals("template.json", StringComparison.OrdinalIgnoreCase) &&
                           !declaredSources.Contains(path))
            .Order(StringComparer.Ordinal)
            .FirstOrDefault();
        if (undeclared is not null)
            throw new CliUsageException(
                $"Template file '{undeclared}' is not declared in template.json. Declare it as text or binary, or remove it.");
        return new(manifest.TemplateVersion, loaded);
    }

    private static TemplateManifest ParseManifest(byte[] bytes, string selectedProfile)
    {
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = 8,
        });
        var root = document.RootElement;
        RequireObject(root, "template manifest");
        RequireProperties(root, "template inventory", "templateVersion", "profiles");
        var version = RequireInt(root, "templateVersion");
        if (version != SupportedTemplateVersion)
            throw new CliUsageException(
                $"Template version {version} is unsupported; this gbar supports version {SupportedTemplateVersion}. Update gbar or the template as one release unit.");
        var profilesElement = root.GetProperty("profiles");
        if (profilesElement.ValueKind != JsonValueKind.Array ||
            profilesElement.GetArrayLength() is <= 0 or > 8)
            throw new CliUsageException("Template inventory must declare between 1 and 8 profiles.");
        var profiles = new Dictionary<string, IReadOnlyList<TemplateFileDeclaration>>(StringComparer.Ordinal);
        var allFiles = new List<TemplateFileDeclaration>();
        foreach (var profile in profilesElement.EnumerateArray())
        {
            RequireObject(profile, "template profile");
            RequireProperties(profile, "template profile", "id", "name", "description", "files");
            var id = RequireBoundedString(profile, "id", 1, 32);
            _ = RequireBoundedString(profile, "name", 1, 80);
            _ = RequireBoundedString(profile, "description", 1, 512);
            if (!WidgetTemplateProfiles.All.Contains(id, StringComparer.Ordinal))
                throw new CliUsageException($"Template profile '{id}' is unsupported.");
            var filesElement = profile.GetProperty("files");
            if (filesElement.ValueKind != JsonValueKind.Array ||
                filesElement.GetArrayLength() is <= 0 or > MaximumFiles)
                throw new CliUsageException(
                    $"Template profile '{id}' must declare between 1 and {MaximumFiles} files.");
            var files = new List<TemplateFileDeclaration>(filesElement.GetArrayLength());
            foreach (var item in filesElement.EnumerateArray())
            {
                RequireObject(item, "template file declaration");
                RequireProperties(item, "template file declaration", "source", "destination", "kind");
                var source = RequireBoundedString(item, "source", 1, MaximumRelativePathLength);
                var destination = RequireBoundedString(item, "destination", 1, MaximumRelativePathLength);
                var kindText = RequireBoundedString(item, "kind", 1, 16);
                var kind = kindText switch
                {
                    "text" => TemplateFileKind.Text,
                    "binary" => TemplateFileKind.Binary,
                    _ => throw new CliUsageException(
                        $"Template file '{source}' has unsupported kind '{kindText}'. Use 'text' or 'binary'."),
                };
                files.Add(new(source, destination, kind));
                allFiles.Add(new(source, destination, kind));
            }
            if (!profiles.TryAdd(id, files))
                throw new CliUsageException($"Template profile '{id}' is duplicated.");
        }
        if (profiles.Count != WidgetTemplateProfiles.All.Length ||
            WidgetTemplateProfiles.All.Any(id => !profiles.ContainsKey(id)))
            throw new CliUsageException(
                "Template inventory must declare basic, data, media, and multipage exactly once.");
        if (!profiles.TryGetValue(selectedProfile, out var selected))
            throw new CliUsageException(
                $"Template profile '{selectedProfile}' is unavailable. Rebuild or reinstall gbar.");
        return new(version, selected, allFiles);
    }

    private static TemplateTree CaptureTree(string root)
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count != 0)
        {
            var directory = pending.Pop();
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                RejectReparse(entry, "template entry");
                var relative = Path.GetRelativePath(root, entry)
                    .Replace(Path.DirectorySeparatorChar, '/');
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    pending.Push(entry);
                }
                else
                {
                    files.Add(relative);
                }
            }
        }
        return new(files);
    }

    private static string ApplyDestinationReplacement(
        string destination,
        IReadOnlyDictionary<string, string> replacements)
    {
        const string token = "{{WidgetName}}";
        if (destination.Contains("{{", StringComparison.Ordinal) &&
            !destination.Contains(token, StringComparison.Ordinal))
            throw new CliUsageException(
                $"Template destination '{destination}' contains an unsupported token. Only {token} is allowed in destination paths.");
        var result = destination.Replace(
            token,
            replacements.TryGetValue(token, out var value) ? value : string.Empty,
            StringComparison.Ordinal);
        if (result.Contains('{') || result.Contains('}'))
            throw new CliUsageException(
                $"Template destination '{destination}' contains an invalid replacement token. Use only {token}.");
        return result;
    }

    private static string ValidateRelativePath(
        string value,
        string field,
        bool allowWidgetNameToken)
    {
        if (value.Length is 0 or > MaximumRelativePathLength ||
            value.Contains('\\') || Path.IsPathRooted(value) || value.Contains(':'))
            throw InvalidPath(field, value);
        if (!allowWidgetNameToken && (value.Contains('{') || value.Contains('}')))
            throw InvalidPath(field, value);
        var segments = value.Split('/');
        if (segments.Any(segment => segment.Length == 0 || segment is "." or ".." ||
                                    segment.EndsWith(' ') || segment.EndsWith('.') ||
                                    segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
            throw InvalidPath(field, value);
        return string.Join('/', segments);
    }

    private static CliUsageException InvalidPath(string field, string value) =>
        new($"Template {field} '{value}' is not a canonical relative path. Remove traversal, rooted paths, backslashes, invalid segments, or unsupported tokens.");

    private static void RejectReparse(string path, string label)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new CliUsageException(
                $"ControllerWidget {label} is a reparse point: {path}. Replace it with a normal local file or directory.");
    }

    private static void RequireObject(JsonElement element, string label)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new CliUsageException($"The {label} must be a JSON object. Repair template.json and retry.");
    }

    private static void RequireProperties(JsonElement element, string label, params string[] expected)
    {
        var expectedSet = expected.ToHashSet(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!expectedSet.Contains(property.Name))
                throw new CliUsageException(
                    $"The {label} contains unknown property '{property.Name}'. Remove it or update gbar and the template together.");
            if (!seen.Add(property.Name))
                throw new CliUsageException(
                    $"The {label} repeats property '{property.Name}'. Keep exactly one value.");
        }
        var missing = expected.FirstOrDefault(name => !seen.Contains(name));
        if (missing is not null)
            throw new CliUsageException(
                $"The {label} is missing required property '{missing}'. Add it to template.json.");
    }

    private static int RequireInt(JsonElement element, string property)
    {
        var value = element.GetProperty(property);
        if (!value.TryGetInt32(out var result))
            throw new CliUsageException(
                $"Template manifest property '{property}' must be an integer. Repair template.json and retry.");
        return result;
    }

    private static string RequireBoundedString(
        JsonElement element,
        string property,
        int minimum,
        int maximum)
    {
        var value = element.GetProperty(property);
        if (value.ValueKind != JsonValueKind.String)
            throw new CliUsageException(
                $"Template manifest property '{property}' must be a string. Repair template.json and retry.");
        var result = value.GetString()!;
        if (result.Length < minimum || result.Length > maximum)
            throw new CliUsageException(
                $"Template manifest property '{property}' must contain {minimum} to {maximum} characters. Repair template.json and retry.");
        return result;
    }

    private static CliUsageException ExistingDestination(string target) =>
        new($"Output path already exists: {target}. Choose a new path; gbar never overwrites or deletes an existing destination.");

    private static string SingleLine(string value) =>
        string.Join(' ', value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)).Trim();

    private enum TemplateFileKind
    {
        Text,
        Binary,
    }

    private sealed record TemplateFileDeclaration(
        string Source,
        string Destination,
        TemplateFileKind Kind);

    private sealed record TemplateManifest(
        int TemplateVersion,
        IReadOnlyList<TemplateFileDeclaration> Files,
        IReadOnlyList<TemplateFileDeclaration> AllFiles);

    private sealed record LoadedTemplateFile(
        string Source,
        string Destination,
        TemplateFileKind Kind,
        byte[] Content);

    private sealed record LoadedTemplate(
        int Version,
        IReadOnlyList<LoadedTemplateFile> Files);

    private sealed record TemplateTree(HashSet<string> Files);
}
