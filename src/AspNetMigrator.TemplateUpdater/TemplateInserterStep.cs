﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Build.Construction;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AspNetMigrator.TemplateUpdater
{
    /// <summary>
    /// A migration step that adds files from templates if they're
    /// not present in the project. Adds files based on TemplateConfig
    /// files read at runtime.
    /// </summary>
    public class TemplateInserterStep : MigrationStep
    {
        private const int BufferSize = 65536;
        private static readonly Regex PropertyRegex = new Regex(@"^\$\((.*)\)$");

        // Files that indicate the project is likely a web app rather than a class library or some other project type
        private static readonly ItemSpec[] WebAppFiles = new[]
        {
            new ItemSpec("Content", "Global.asax", false, Array.Empty<string>()),
            new ItemSpec("Content", "Web.config", false, Array.Empty<string>())
        };

        private readonly IEnumerable<string> _templateConfigFiles;
        private readonly Dictionary<string, RuntimeItemSpec> _itemsToAdd;

        public TemplateInserterStep(MigrateOptions options, IOptions<TemplateInserterStepOptions> templateUpdaterOptions, ILogger<TemplateInserterStep> logger)
            : base(options, logger)
        {
            if (options is null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            if (logger is null)
            {
                throw new ArgumentNullException(nameof(logger));
            }

            _itemsToAdd = new Dictionary<string, RuntimeItemSpec>();
            _templateConfigFiles = (templateUpdaterOptions?.Value.TemplateConfigFiles ?? Array.Empty<string>())
                .Select(path => Path.IsPathFullyQualified(path)
                ? path
                : Path.Combine(AppContext.BaseDirectory, path));

            if (!_templateConfigFiles.Any())
            {
                Logger.LogWarning("No template configuration files provided; no template files will be added to project");
            }

            Title = $"Add template files";
            Description = $"Add template files (for startup code paths, for example) to {options.ProjectPath} based on template files described in: {string.Join(", ", _templateConfigFiles)}";
        }

        protected override async Task<(MigrationStepStatus Status, string StatusDetails)> InitializeImplAsync(IMigrationContext context, CancellationToken token)
        {
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            var projectPath = await context.GetProjectPathAsync(token).ConfigureAwait(false);

            if (projectPath is null || !File.Exists(projectPath))
            {
                Logger.LogCritical("Project file {ProjectPath} not found", projectPath);
                return (MigrationStepStatus.Failed, $"Project file {projectPath} not found");
            }

            try
            {
                using var projectCollection = new ProjectCollection();
                var project = projectCollection.LoadProject(projectPath);

                var isWebApp = IsWebApp(project);

                // Iterate through all config files, adding template files from each to the list of items to add, as appropriate.
                // Later config files can intentionally overwrite earlier config files' items.
                foreach (var templateConfigFile in _templateConfigFiles)
                {
                    var basePath = Path.GetDirectoryName(templateConfigFile) ?? string.Empty;
                    var templateConfiguration = await LoadTemplateConfigurationAsync(templateConfigFile, token).ConfigureAwait(false);

                    // If there was a problem reading the configuration or the configuration only applies to web apps and the
                    // current project isn't a web app, continue to the next config file.
                    if (templateConfiguration?.TemplateItems is null || (!isWebApp && templateConfiguration.UpdateWebAppsOnly))
                    {
                        Logger.LogDebug("Skipping inapplicable template config file {TemplateConfigFile}", templateConfigFile);
                        continue;
                    }

                    Logger.LogDebug("Loaded {ItemCount} template items from template config file {TemplateConfigFile}", templateConfiguration.TemplateItems?.Length ?? 0, templateConfigFile);

                    // Check whether the template items are needed in the project or if they already exist
                    foreach (var templateItem in templateConfiguration.TemplateItems!)
                    {
                        if (project.Items.Any(i => ItemMatches(templateItem, i, project.FullPath)))
                        {
                            Logger.LogDebug("Not adding template item {TemplateItemPath} because the project already contains a similar item", templateItem.Path);
                        }
                        else
                        {
                            var templatePath = Path.Combine(basePath, templateItem.Path);
                            if (!File.Exists(templatePath))
                            {
                                Logger.LogError("Template file not found: {TemplateItemPath}", templatePath);
                                continue;
                            }

                            Logger.LogDebug("Marking template item {TemplateItemPath} from template configuration {TemplateConfigFile} for addition", templateItem.Path, templateConfigFile);
                            _itemsToAdd[templateItem.Path] = new RuntimeItemSpec(templateItem, templatePath, templateConfiguration.Replacements ?? new Dictionary<string, string>());
                        }
                    }
                }

                Logger.LogInformation("{FilesNeededCount} expected template items needed", _itemsToAdd.Count);

                if (_itemsToAdd.Any())
                {
                    Logger.LogDebug("Needed items: {NeededFiles}", string.Join(", ", _itemsToAdd.Keys));
                    return (MigrationStepStatus.Incomplete, $"{_itemsToAdd.Count} expected template items needed ({string.Join(", ", _itemsToAdd.Keys)})");
                }
                else
                {
                    return (MigrationStepStatus.Complete, "All expected template items found");
                }
            }
            catch (InvalidProjectFileException)
            {
                Logger.LogCritical("Invalid project: {ProjectPath}", projectPath);
                return (MigrationStepStatus.Failed, $"Invalid project: {projectPath}");
            }
        }

        protected override async Task<(MigrationStepStatus Status, string StatusDetails)> ApplyImplAsync(IMigrationContext context, CancellationToken token)
        {
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            var projectPath = await context.GetProjectPathAsync(token).ConfigureAwait(false);
            var projectDir = Path.GetDirectoryName(projectPath) ?? string.Empty;

            try
            {
                var projectRoot = await context.GetProjectRootElementAsync(token).ConfigureAwait(false);

                // For each item to be added, make necessary replacements and then add the item to the project
                foreach (var item in _itemsToAdd.Values)
                {
                    var filePath = Path.Combine(projectDir, item.Path);

                    // If the file already exists, move it
                    if (File.Exists(filePath))
                    {
                        RenameFile(filePath, projectRoot);
                    }

                    // Get the contents of the template file
                    try
                    {
                        var tokenReplacements = ResolveTokenReplacements(item.Replacements, projectRoot.FullPath);
#pragma warning disable CA2000 // Dispose objects before losing scope
                        using var templateStream = File.Open(item.TemplateFilePath, FileMode.Open, FileAccess.Read);
                        using var outputStream = File.Create(filePath, BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
#pragma warning restore CA2000 // Dispose objects before losing scope

                        await StreamHelpers.CopyStreamWithTokenReplacementAsync(templateStream, outputStream, tokenReplacements).ConfigureAwait(false);
                    }
                    catch (IOException exc)
                    {
                        Logger.LogCritical(exc, "Template file not found: {TemplateItemPath}", item.TemplateFilePath);
                        return (MigrationStepStatus.Failed, $"Template file not found: {item.TemplateFilePath}");
                    }

                    if (item.IncludeExplicitly)
                    {
                        // Add the new item to the project if it won't be auto-included
                        projectRoot.AddItem(item.Type, item.Path);
                    }

                    Logger.LogInformation("Added {ItemName} to the project from template file", item.Path);
                }

                projectRoot.Save();

                // Reload the workspace since, at this point, the project may be different from what was loaded
                await context.ReloadWorkspaceAsync(token).ConfigureAwait(false);

                Logger.LogInformation("{ItemCount} template items added", _itemsToAdd.Count);
                return (MigrationStepStatus.Complete, $"{_itemsToAdd.Count} template items added");
            }
            catch (InvalidProjectFileException)
            {
                Logger.LogCritical("Invalid project: {ProjectPath}", projectPath);
                return (MigrationStepStatus.Failed, $"Invalid project: {projectPath}");
            }
        }

        private Dictionary<string, string> ResolveTokenReplacements(IEnumerable<KeyValuePair<string, string>>? replacements, string projectPath)
        {
            using var projectCollection = new ProjectCollection();
            var project = projectCollection.LoadProject(projectPath);
            var propertyCache = new Dictionary<string, string?>();
            var ret = new Dictionary<string, string>();

            if (replacements is not null)
            {
                foreach (var replacement in replacements)
                {
                    var regexMatch = PropertyRegex.Match(replacement.Value);
                    if (regexMatch.Success)
                    {
                        // If the user specified an MSBuild property as a replacement value ($(...))
                        // then lookup the property value
                        var propertyName = regexMatch.Groups[1].Captures[0].Value;
                        string? propertyValue = null;

                        if (propertyCache.ContainsKey(propertyName))
                        {
                            propertyValue = propertyCache[propertyName];
                        }
                        else
                        {
                            propertyValue = project.GetPropertyValue(propertyName);
                            propertyCache[propertyName] = propertyValue;
                        }

                        if (!string.IsNullOrWhiteSpace(propertyValue))
                        {
                            Logger.LogDebug("Resolved project property {PropertyKey} to {PropertyValue}", propertyName, propertyValue);
                            ret.Add(replacement.Key, propertyValue);
                        }
                        else
                        {
                            Logger.LogWarning("Could not resove project property {PropertyName}; not replacing token {Token}", propertyName, replacement.Key);
                        }
                    }
                    else
                    {
                        // If the replacement value is a string, then just add it directly to the return dictionary
                        ret.Add(replacement.Key, replacement.Value);
                    }
                }
            }

            return ret;
        }

        /// <summary>
        /// Determines if a project is likely to be a web app base on its included items.
        /// </summary>
        private bool IsWebApp(Project project) => project.Items.Any(i => WebAppFiles.Any(w => ItemMatches(w, i, project.FullPath)));

        /// <summary>
        /// Determines if a given project element matches an item specification.
        /// </summary>
        private bool ItemMatches(ItemSpec expectedItem, ProjectItem itemElement, string projectPath)
        {
            // The item type must match
            if (!expectedItem.Type.Equals(itemElement.ItemType, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            // The item must have an include attribute
            if (string.IsNullOrEmpty(itemElement.EvaluatedInclude))
            {
                return false;
            }

            // The file name must match
            var fileName = Path.GetFileName(itemElement.EvaluatedInclude);
            if (!fileName.Equals(expectedItem.Path, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var projectDir = Path.GetDirectoryName(projectPath)!;
            var filePath = Path.IsPathFullyQualified(itemElement.EvaluatedInclude) ?
                itemElement.EvaluatedInclude :
                Path.Combine(projectDir, itemElement.EvaluatedInclude);

            Logger.LogDebug("Considering {FilePath} for expected file {ExpectedFileName}", filePath, expectedItem.Path);

            // The included file must exist
            if (!File.Exists(filePath))
            {
                Logger.LogDebug("File {FilePath} does not exist", filePath);
                return false;
            }

            // The file must include all specified keywords
            if (expectedItem.Keywords.Length > 0)
            {
                var fileContents = File.ReadAllText(filePath);
                if (expectedItem.Keywords.Any(k => !fileContents.Contains(k, StringComparison.Ordinal)))
                {
                    Logger.LogDebug("File {FilePath} does not contain all necessary keywords to match", filePath);
                    return false;
                }
            }

            Logger.LogDebug("File {FilePath} matches expected file {ExpectedFileName}", filePath, expectedItem.Path);
            return true;
        }

        private async Task<TemplateConfiguration?> LoadTemplateConfigurationAsync(string path, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                Logger.LogError("Invalid template configuration file path: {TemplateConfigPath}", path);
                return null;
            }

            if (!File.Exists(path))
            {
                Logger.LogError("Template configuration file not found: {TemplateConfigPath}", path);
                return null;
            }

            try
            {
                using var config = File.OpenRead(path);
                return await JsonSerializer.DeserializeAsync<TemplateConfiguration>(config, cancellationToken: token).ConfigureAwait(false);
            }
            catch (JsonException)
            {
                Logger.LogError("Error deserializing template configuration file: {TemplateConfigPath}", path);
                return null;
            }
        }

        private void RenameFile(string filePath, ProjectRootElement project)
        {
            var fileName = Path.GetFileName(filePath);
            var backupName = $"{Path.GetFileNameWithoutExtension(fileName)}.old{Path.GetExtension(fileName)}";
            var counter = 0;
            while (File.Exists(backupName))
            {
                backupName = $"{Path.GetFileNameWithoutExtension(fileName)}.old.{counter++}{Path.GetExtension(fileName)}";
            }

            Logger.LogInformation("File already exists, moving {FileName} to {BackupFileName}", fileName, backupName);

            // Even though the file may not make sense in the migrated project,
            // don't remove the file from the project because the user will probably want to migrate some of the code manually later
            // so it's useful to leave it in the project so that the migration need is clearly visible.
            foreach (var item in project.Items.Where(i => i.Include.Equals(fileName, StringComparison.OrdinalIgnoreCase)))
            {
                item.Include = backupName;
            }

            foreach (var item in project.Items.Where(i => i.Update.Equals(fileName, StringComparison.OrdinalIgnoreCase)))
            {
                item.Update = backupName;
            }

            File.Move(filePath, Path.Combine(Path.GetDirectoryName(filePath)!, backupName));
        }
    }
}
