﻿using Microsoft.Extensions.Logging;
using SchulIT.UntisExport;
using SchulIT.UntisExport.Model;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UntisExportService.Core.FileSystem;
using UntisExportService.Core.Settings;
using UntisExportService.Core.Upload;

namespace UntisExportService.Core
{
    public class ExportService : IExportService
    {
        private bool isExportRunning = false;
        private readonly object exportLock = new object();

        private const int ExamsId = 0;

        private readonly ISettingsService settingsService;
        private readonly IFileSystemWatcher watcher;
        private readonly IUploadService uploadService;
        private readonly IUntisExporter untisExporter;
        private readonly ILogger<ExportService> logger;

        public ExportService(ISettingsService settingsService, IFileSystemWatcher watcher, IUploadService uploadService, IUntisExporter untisExporter, ILogger<ExportService> logger)
        {
            this.settingsService = settingsService;
            this.watcher = watcher;
            this.uploadService = uploadService;
            this.untisExporter = untisExporter;
            this.logger = logger;
        }

        public void End()
        {
            logger.LogInformation("Export service stopped.");
        }

        public void Start()
        {
            logger.LogInformation("Export service started.");

            watcher.Path = settingsService.Settings.HtmlPath;
            watcher.Changed += OnWatcherChanged;
        }

        private async void OnWatcherChanged(IFileSystemWatcher sender, OnChangedEventArgs args)
        {
            try
            {
                logger.LogInformation("Detected filesystem changes.");

                if (settingsService.Settings.IsServiceEnabled == false)
                {
                    logger.LogInformation("Do not publish to ICC as service is disabled in settings file.");
                    return;
                }

                lock (exportLock)
                {
                    if (isExportRunning)
                    {
                        logger.LogDebug("Export is already running, skipping.");
                        return;
                    }

                    isExportRunning = true;
                }

                if (settingsService.Settings.Untis.SyncThresholdInSeconds > 0)
                {
                    logger.LogDebug($"Waiting {settingsService.Settings.Untis.SyncThresholdInSeconds} seconds for Untis to create all files.");
                    await Task.Delay(TimeSpan.FromSeconds(settingsService.Settings.Untis.SyncThresholdInSeconds));
                }

                // Read all files in directory
                var files = Directory.GetFiles(settingsService.Settings.HtmlPath, "*.htm");

                foreach (var file in files)
                {
                    logger.LogDebug($"Found file {file}.");
                }

                var substitutions = new List<Substitution>();
                var infotexts = new List<Infotext>();

                var settings = GetExportSettings();

                foreach (var file in files)
                {
                    using (var streamReader = new StreamReader(file, Encoding.GetEncoding(settingsService.Settings.Encoding)))
                    {
                        var html = await streamReader.ReadToEndAsync();
                        var result = await untisExporter.ParseHtmlAsync(settings, html);

                        substitutions.AddRange(result.Substitutions);
                        infotexts.AddRange(result.Infotexts);
                    }
                }

                await ReplaceSubstitutionTypes(substitutions).ConfigureAwait(false);
                await RemoveSubstitutionsIfNecessaryAsync(substitutions).ConfigureAwait(false);

                // Pack everything and and upload
                await uploadService.UploadSubstitutionsAsync(substitutions).ConfigureAwait(false);
                await uploadService.UploadInfotextsAsync(infotexts).ConfigureAwait(false);

                logger.LogInformation("Successfully published to ICC.");

                lock (exportLock)
                {
                    isExportRunning = false;
                }
            }
            catch (Exception e)
            {
                logger.LogError(e, "Something went terribly wrong.");
            }
        }

        private Task ReplaceSubstitutionTypes(List<Substitution> substitutions)
        {
            logger.LogDebug("Replace substitution types.");

            if (settingsService.Settings.Untis.TypeReplacements == null || settingsService.Settings.Untis.TypeReplacements.Count == 0)
            {
                logger.LogDebug($"No type replacements given. Skipping.");
                return Task.CompletedTask;
            }

            return Task.Run(() =>
            {
                foreach (var substitution in substitutions)
                {
                    foreach (var kv in settingsService.Settings.Untis.TypeReplacements)
                    {
                        substitution.Type = substitution.Type.Replace(kv.Key, kv.Value);
                    }
                }
            });
        }

        private Task RemoveSubstitutionsIfNecessaryAsync(List<Substitution> substitutions)
        {
            if(settingsService.Settings.Untis.RemoveExams == false)
            {
                logger.LogDebug("No need to remove exams.");
                return Task.CompletedTask;
            }

            return Task.Run(() =>
            {
                logger.LogDebug("Removing exams.");

                var deleteIdx = new List<int>();

                /**
                 * Important: we need the indices in descending order
                 * because removing an item at index N will cause an
                 * re-indexing of all items behind index N and thus 
                 * delete indices will be wrong!
                 * 
                 * Idea: instead of sorting the indices in descending order,
                 * we create the list of indices in descending order by interating
                 * over the substitution list from the back to the beginning.
                 */
                for (int i = substitutions.Count - 1; i >= 0; i--)
                {
                    if (substitutions[i].Id == ExamsId)
                    {
                        deleteIdx.Add(i);
                    }
                }

                foreach (var idx in deleteIdx)
                {
                    substitutions.RemoveAt(idx);
                }

                logger.LogDebug($"Removed {deleteIdx.Count} exams.");
            });
        }

        private ExportSettings GetExportSettings()
        {
            var untisSettings = settingsService.Settings.Untis;

            var settings = new ExportSettings
            {
                FixBrokenPTags = untisSettings.FixBrokenPTags,
                DateTimeFormat = untisSettings.DateTimeFormat,
                IncludeAbsentValues = untisSettings.InlcudeAbsentValues,
            };

            settings.EmptyValues.Clear();
            settings.EmptyValues.AddRange(untisSettings.EmptyValues);

            var untisColumnSettings = untisSettings.ColumnSettings;

            settings.ColumnSettings.IdColumn = untisColumnSettings.IdColumn;
            settings.ColumnSettings.DateColumn = untisColumnSettings.DateColumn;
            settings.ColumnSettings.LessonColumn = untisColumnSettings.LessonColumn;
            settings.ColumnSettings.GradesColumn = untisColumnSettings.GradesColumn;
            settings.ColumnSettings.ReplacementGradesColumn = untisColumnSettings.ReplacementGradesColumn;
            settings.ColumnSettings.TeachersColumn = untisColumnSettings.TeachersColumn;
            settings.ColumnSettings.ReplacementTeachersColumn = untisColumnSettings.ReplacementTeachersColumn;
            settings.ColumnSettings.SubjectColumn = untisColumnSettings.SubjectColumn;
            settings.ColumnSettings.ReplacementSubjectColumn = untisColumnSettings.ReplacementSubjectColumn;
            settings.ColumnSettings.RoomColumn = untisColumnSettings.RoomColumn;
            settings.ColumnSettings.ReplacementRoomColumn = untisColumnSettings.ReplacementRoomColumn;
            settings.ColumnSettings.TypeColumn = untisColumnSettings.TypeColumn;
            settings.ColumnSettings.RemarkColumn = untisColumnSettings.RemarkColumn;

            return settings;
        }
    }
}
