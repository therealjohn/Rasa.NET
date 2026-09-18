using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Rasa.Test.Compatibility
{
    internal sealed class ComposeLayout
    {
        private readonly IReadOnlyDictionary<string, ComposeService> _services;

        private ComposeLayout(IReadOnlyDictionary<string, ComposeService> services)
        {
            _services = services;
        }

        internal static ComposeLayout Parse(string yaml)
        {
            var services = new Dictionary<string, ComposeServiceBuilder>(
                StringComparer.Ordinal);
            ComposeServiceBuilder currentService = null;
            var inServices = false;
            var inVolumes = false;

            foreach (var rawLine in yaml.Replace("\r", string.Empty).Split('\n'))
            {
                if (string.IsNullOrWhiteSpace(rawLine))
                    continue;

                var indent = rawLine.TakeWhile(character => character == ' ').Count();
                var line = StripComment(rawLine.Trim());
                if (line.Length == 0)
                    continue;

                if (indent == 0)
                {
                    inServices = line == "services:";
                    currentService = null;
                    inVolumes = false;
                    continue;
                }

                if (!inServices)
                    continue;

                if (indent == 2 && line.EndsWith(":", StringComparison.Ordinal))
                {
                    var name = line.Substring(0, line.Length - 1);
                    currentService = new ComposeServiceBuilder(name);
                    services.Add(name, currentService);
                    inVolumes = false;
                    continue;
                }

                if (currentService == null)
                    continue;

                if (indent == 4)
                {
                    inVolumes = line == "volumes:";
                    if (inVolumes)
                        continue;

                    var separator = line.IndexOf(':');
                    if (separator < 0)
                        continue;

                    var property = line.Substring(0, separator);
                    var value = Unquote(line.Substring(separator + 1).Trim());
                    switch (property)
                    {
                        case "working_dir":
                            currentService.WorkingDirectory = value;
                            break;
                        case "command":
                            currentService.Command = SplitCommand(value);
                            break;
                    }

                    continue;
                }

                if (inVolumes && indent == 6 && line.StartsWith("- ", StringComparison.Ordinal))
                    currentService.VolumeDestinations.Add(ParseVolumeDestination(
                        Unquote(line.Substring(2).Trim())));
            }

            return new ComposeLayout(services.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.Build(),
                StringComparer.Ordinal));
        }

        internal ComposeService GetService(string name)
        {
            if (!_services.TryGetValue(name, out var service))
                throw new InvalidDataException($"Compose service '{name}' was not found.");

            return service;
        }

        private static string ParseVolumeDestination(string value)
        {
            var fields = value.Split(':');
            var destination = fields.FirstOrDefault(field =>
                field.StartsWith("/", StringComparison.Ordinal));
            if (destination == null)
                throw new InvalidDataException($"Volume '{value}' has no absolute container destination.");

            return PosixPath.Normalize(destination);
        }

        private static IReadOnlyList<string> SplitCommand(string value)
        {
            return Regex.Matches(value, @"""[^""]*""|'[^']*'|\S+")
                .Cast<Match>()
                .Select(match => Unquote(match.Value))
                .ToArray();
        }

        private static string StripComment(string value)
        {
            var inSingleQuote = false;
            var inDoubleQuote = false;
            for (var index = 0; index < value.Length; index++)
            {
                switch (value[index])
                {
                    case '\'' when !inDoubleQuote:
                        inSingleQuote = !inSingleQuote;
                        break;
                    case '"' when !inSingleQuote:
                        inDoubleQuote = !inDoubleQuote;
                        break;
                    case '#' when !inSingleQuote && !inDoubleQuote:
                        return value.Substring(0, index).TrimEnd();
                }
            }

            return value;
        }

        private static string Unquote(string value)
        {
            if (value.Length >= 2 &&
                ((value[0] == '"' && value[value.Length - 1] == '"') ||
                 (value[0] == '\'' && value[value.Length - 1] == '\'')))
                return value.Substring(1, value.Length - 2);

            return value;
        }

        private sealed class ComposeServiceBuilder
        {
            internal string Name { get; }
            internal string WorkingDirectory { get; set; }
            internal IReadOnlyList<string> Command { get; set; } = Array.Empty<string>();
            internal List<string> VolumeDestinations { get; } = new List<string>();

            internal ComposeServiceBuilder(string name)
            {
                Name = name;
            }

            internal ComposeService Build() =>
                new ComposeService(Name, WorkingDirectory, Command, VolumeDestinations);
        }
    }

    internal sealed class ComposeService
    {
        internal string Name { get; }
        internal string WorkingDirectory { get; }
        internal IReadOnlyList<string> Command { get; }
        internal IReadOnlyList<string> VolumeDestinations { get; }

        internal ComposeService(
            string name,
            string workingDirectory,
            IReadOnlyList<string> command,
            IReadOnlyList<string> volumeDestinations)
        {
            Name = name;
            WorkingDirectory = PosixPath.Normalize(workingDirectory);
            Command = command;
            VolumeDestinations = volumeDestinations;
        }
    }

    internal sealed class DockerImageLayout
    {
        private readonly HashSet<string> _files =
            new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _directories =
            new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _copiedFiles =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private DockerImageLayout()
        {
        }

        internal static DockerImageLayout Create(string repositoryRoot)
        {
            var layout = new DockerImageLayout();
            var dockerfile = DockerfileModel.Parse(
                File.ReadAllText(Path.Combine(repositoryRoot, "Dockerfile")));
            var workingDirectory = "/";

            foreach (var instruction in dockerfile.Instructions)
            {
                switch (instruction.Name)
                {
                    case "WORKDIR":
                        workingDirectory = PosixPath.Resolve(
                            workingDirectory,
                            instruction.Arguments);
                        layout.AddDirectory(workingDirectory);
                        break;
                    case "COPY":
                        layout.ApplyCopy(repositoryRoot, workingDirectory, instruction.Arguments);
                        break;
                    case "RUN" when IsReleaseBuild(instruction.Arguments):
                        layout.ApplyReleaseBuild(repositoryRoot, workingDirectory);
                        break;
                }
            }

            return layout;
        }

        internal bool ContainsFile(string path) =>
            _files.Contains(PosixPath.Normalize(path));

        internal bool ContainsDirectory(string path) =>
            _directories.Contains(PosixPath.Normalize(path));

        internal bool ContainsFileBelow(string directory, string extension)
        {
            var prefix = PosixPath.Normalize(directory).TrimEnd('/') + "/";
            return _files.Any(path =>
                path.StartsWith(prefix, StringComparison.Ordinal) &&
                path.EndsWith(extension, StringComparison.OrdinalIgnoreCase));
        }

        private void ApplyCopy(
            string repositoryRoot,
            string workingDirectory,
            string arguments)
        {
            var fields = SplitArguments(arguments);
            if (fields.Count != 2)
                throw new InvalidDataException($"Unsupported Docker COPY instruction: {arguments}");

            var source = Path.GetFullPath(Path.Combine(
                repositoryRoot,
                fields[0].Replace('/', Path.DirectorySeparatorChar)));
            var destination = PosixPath.Resolve(workingDirectory, fields[1]);
            if (File.Exists(source))
            {
                if (fields[1].EndsWith("/", StringComparison.Ordinal) ||
                    _directories.Contains(destination))
                    destination = PosixPath.Resolve(destination, Path.GetFileName(source));
                AddCopiedFile(source, destination);
                return;
            }

            if (!Directory.Exists(source))
                throw new InvalidDataException($"Docker COPY source '{fields[0]}' does not exist.");

            AddDirectory(destination);
            foreach (var file in Directory.EnumerateFiles(
                source,
                "*",
                SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(source, file).Replace('\\', '/');
                AddCopiedFile(file, PosixPath.Resolve(destination, relative));
            }
        }

        private void ApplyReleaseBuild(string repositoryRoot, string workingDirectory)
        {
            var solutionPath = PosixPath.Resolve(workingDirectory, "Rasa.NET.sln");
            if (!_files.Contains(solutionPath))
                throw new InvalidDataException(
                    $"Docker build runs outside the copied solution directory '{workingDirectory}'.");

            foreach (var projectPath in Directory.EnumerateFiles(
                Path.Combine(repositoryRoot, "src"),
                "*.csproj",
                SearchOption.AllDirectories))
            {
                if (!_copiedFiles.TryGetValue(Path.GetFullPath(projectPath), out var imageProjectPath))
                    continue;

                AddProjectOutput(projectPath, imageProjectPath, new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase));
            }
        }

        private void AddProjectOutput(
            string projectPath,
            string imageProjectPath,
            HashSet<string> visitedProjects)
        {
            projectPath = Path.GetFullPath(projectPath);
            if (!visitedProjects.Add(projectPath))
                return;

            var project = XDocument.Load(projectPath);
            var projectDirectory = Path.GetDirectoryName(projectPath);
            var imageProjectDirectory = PosixPath.GetDirectoryName(imageProjectPath);
            var outputDirectory = PosixPath.Resolve(
                imageProjectDirectory,
                "bin/Release/net10.0");
            var assemblyName = project.Descendants("AssemblyName")
                .Select(element => element.Value)
                .FirstOrDefault() ??
                Path.GetFileNameWithoutExtension(projectPath);

            AddFile(PosixPath.Resolve(outputDirectory, assemblyName + ".dll"));
            AddOutputContent(project, projectDirectory, outputDirectory);

            foreach (var reference in project.Descendants("ProjectReference"))
            {
                var include = reference.Attribute("Include")?.Value;
                if (string.IsNullOrEmpty(include))
                    continue;

                var referencedProject = Path.GetFullPath(Path.Combine(
                    projectDirectory,
                    include.Replace('\\', Path.DirectorySeparatorChar)));
                if (!_copiedFiles.TryGetValue(referencedProject, out var imageReferencedProject))
                    continue;

                AddReferencedContent(
                    referencedProject,
                    imageReferencedProject,
                    outputDirectory,
                    visitedProjects);
            }
        }

        private void AddReferencedContent(
            string projectPath,
            string imageProjectPath,
            string consumingOutputDirectory,
            HashSet<string> visitedProjects)
        {
            projectPath = Path.GetFullPath(projectPath);
            if (!visitedProjects.Add(projectPath))
                return;

            var project = XDocument.Load(projectPath);
            var projectDirectory = Path.GetDirectoryName(projectPath);
            AddOutputContent(project, projectDirectory, consumingOutputDirectory);

            foreach (var reference in project.Descendants("ProjectReference"))
            {
                var include = reference.Attribute("Include")?.Value;
                if (string.IsNullOrEmpty(include))
                    continue;

                var referencedProject = Path.GetFullPath(Path.Combine(
                    projectDirectory,
                    include.Replace('\\', Path.DirectorySeparatorChar)));
                if (!_copiedFiles.TryGetValue(referencedProject, out var nestedImageProject))
                    continue;

                AddReferencedContent(
                    referencedProject,
                    nestedImageProject,
                    consumingOutputDirectory,
                    visitedProjects);
            }
        }

        private void AddOutputContent(
            XDocument project,
            string projectDirectory,
            string outputDirectory)
        {
            foreach (var item in project.Descendants()
                .Where(element =>
                    (element.Name.LocalName == "None" ||
                     element.Name.LocalName == "Content") &&
                    element.Elements("CopyToOutputDirectory").Any()))
            {
                var include = item.Attribute("Include")?.Value ??
                    item.Attribute("Update")?.Value;
                if (string.IsNullOrEmpty(include))
                    continue;

                var source = Path.GetFullPath(Path.Combine(
                    projectDirectory,
                    include.Replace('\\', Path.DirectorySeparatorChar)));
                if (!_copiedFiles.ContainsKey(source))
                    continue;

                var targetPath = item.Elements("TargetPath")
                    .Select(element => element.Value)
                    .FirstOrDefault() ??
                    item.Elements("Link")
                        .Select(element => element.Value)
                        .FirstOrDefault() ??
                    include;
                AddFile(PosixPath.Resolve(
                    outputDirectory,
                    targetPath.Replace('\\', '/')));
            }
        }

        private void AddCopiedFile(string hostPath, string imagePath)
        {
            hostPath = Path.GetFullPath(hostPath);
            imagePath = PosixPath.Normalize(imagePath);
            _copiedFiles[hostPath] = imagePath;
            AddFile(imagePath);
        }

        private void AddFile(string path)
        {
            path = PosixPath.Normalize(path);
            _files.Add(path);
            AddDirectory(PosixPath.GetDirectoryName(path));
        }

        private void AddDirectory(string path)
        {
            path = PosixPath.Normalize(path);
            while (path.Length > 1 && _directories.Add(path))
                path = PosixPath.GetDirectoryName(path);
            _directories.Add("/");
        }

        private static bool IsReleaseBuild(string arguments)
        {
            var fields = SplitArguments(arguments);
            return fields.Count >= 2 &&
                fields[0] == "dotnet" &&
                fields[1] == "build" &&
                (fields.Contains("--configuration") &&
                 fields.Contains("Release") ||
                 fields.Contains("-c") &&
                 fields.Contains("Release"));
        }

        private static IReadOnlyList<string> SplitArguments(string value)
        {
            return Regex.Matches(value, @"""[^""]*""|'[^']*'|\S+")
                .Cast<Match>()
                .Select(match => match.Value.Trim('"', '\''))
                .ToArray();
        }
    }

    internal sealed class DockerfileModel
    {
        internal IReadOnlyList<DockerfileInstruction> Instructions { get; }

        private DockerfileModel(IReadOnlyList<DockerfileInstruction> instructions)
        {
            Instructions = instructions;
        }

        internal static DockerfileModel Parse(string contents)
        {
            var instructions = new List<DockerfileInstruction>();
            var logicalLine = new StringBuilder();
            foreach (var rawLine in contents.Replace("\r", string.Empty).Split('\n'))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                    continue;

                var continued = line.EndsWith("\\", StringComparison.Ordinal);
                logicalLine.Append(continued
                    ? line.Substring(0, line.Length - 1).TrimEnd()
                    : line);
                if (continued)
                {
                    logicalLine.Append(' ');
                    continue;
                }

                var separator = logicalLine.ToString().IndexOf(' ');
                if (separator < 0)
                    throw new InvalidDataException(
                        $"Docker instruction has no arguments: {logicalLine}");

                instructions.Add(new DockerfileInstruction(
                    logicalLine.ToString().Substring(0, separator).ToUpperInvariant(),
                    logicalLine.ToString().Substring(separator + 1).Trim()));
                logicalLine.Clear();
            }

            if (logicalLine.Length != 0)
                throw new InvalidDataException("Dockerfile ends in a continued instruction.");

            return new DockerfileModel(instructions);
        }
    }

    internal sealed class DockerfileInstruction
    {
        internal string Name { get; }
        internal string Arguments { get; }

        internal DockerfileInstruction(string name, string arguments)
        {
            Name = name;
            Arguments = arguments;
        }
    }

    internal static class PosixPath
    {
        internal static string Resolve(string basePath, string path)
        {
            if (path.StartsWith("/", StringComparison.Ordinal))
                return Normalize(path);

            return Normalize(basePath.TrimEnd('/') + "/" + path);
        }

        internal static string GetDirectoryName(string path)
        {
            path = Normalize(path);
            var separator = path.LastIndexOf('/');
            return separator <= 0 ? "/" : path.Substring(0, separator);
        }

        internal static string Normalize(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new InvalidDataException("Container path is empty.");

            var absolute = path.StartsWith("/", StringComparison.Ordinal);
            var parts = new List<string>();
            foreach (var part in path.Replace('\\', '/').Split('/'))
            {
                if (part.Length == 0 || part == ".")
                    continue;
                if (part == "..")
                {
                    if (parts.Count == 0)
                        throw new InvalidDataException($"Container path escapes its root: {path}");
                    parts.RemoveAt(parts.Count - 1);
                    continue;
                }

                parts.Add(part);
            }

            return (absolute ? "/" : string.Empty) + string.Join("/", parts);
        }
    }
}
