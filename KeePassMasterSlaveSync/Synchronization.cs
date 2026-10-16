using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using KeePass;
using KeePassLib;
using KeePassLib.Interfaces;
using KeePassLib.Collections;
using KeePassLib.Keys;
using KeePassLib.Utility;
using KeePassLib.Serialization;
using KeePassLib.Security;

namespace KeePassMasterSlaveSync
{
    public class Sync
    {
        /// <summary>
        /// A database used during one synchronization run.
        /// </summary>
        private class SyncDatabase
        {
            public PwDatabase Database;
            // Opened in KeePass: it is changed in memory and marked as modified, never written by the plugin
            public bool IsOpen;
            // Changed by the synchronization
            public bool Changed;
            // A job targeting this database failed, so it is not written
            public bool Failed;
        }

        private static IOConnectionInfo connectionInfo = null;
        private static bool isRunning = false;
        // All databases of the current run by full path, so each file is opened and written only once
        private static Dictionary<string, SyncDatabase> Databases =
            new Dictionary<string, SyncDatabase>(StringComparer.OrdinalIgnoreCase);
        private static SyncDatabase master = null;
        // The save whose jobs ran before the master was written
        private static Guid syncedSave = Guid.Empty;
        // A save of a copy of the master, which is not synchronized
        private static Guid skippedSave = Guid.Empty;

        private static string currentJob = "";

        /// <summary>
        /// Runs the jobs before the master is written, so the changes taken from the slaves are saved with it.
        /// The slave databases are written once the master has been saved (see SyncAfterSave).
        /// </summary>
        public static void SyncBeforeSave(PwDatabase sourceDb, Guid eventGuid, bool copy)
        {
            // Discard the databases of a save that has been canceled
            EndSync(false, false);

            if (copy)
                skippedSave = eventGuid;
            else if (RunJobs(sourceDb))
                syncedSave = eventGuid;
        }

        /// <summary>
        /// Writes the slave databases once the master has been saved.
        /// </summary>
        public static void SyncAfterSave(PwDatabase sourceDb, Guid eventGuid, bool success)
        {
            if (eventGuid != Guid.Empty && eventGuid == skippedSave)
                return;

            if (eventGuid != Guid.Empty && eventGuid == syncedSave)
            {
                // The master has been written with the changes, the slaves are only written if it succeeded
                EndSync(success, success);
                return;
            }

            // The jobs did not run before this save, so they run now. The master is not written again (that
            // can fail on network shares and lose the file), its changes are only marked as modified.
            EndSync(false, false);
            if (success && RunJobs(sourceDb))
                EndSync(true, false);
        }

        /// <summary>
        /// Runs all jobs of a master database in memory.
        /// </summary>
        /// <returns>False if the database has no sync jobs.</returns>
        private static bool RunJobs(PwDatabase sourceDb)
        {
            // Get all entries out of the group "MSSyncJobs"
            PwGroup settingsGroup = sourceDb.RootGroup.Groups.FirstOrDefault(g => g.Name == "MSSyncJobs");
            if (settingsGroup == null || isRunning)
            {
                return false;
            }
            IEnumerable<PwEntry> jobSettings = settingsGroup.Entries;

            isRunning = true;
            try
            {
                // Get the master database path
                connectionInfo = sourceDb.IOConnectionInfo;
                master = new SyncDatabase() { Database = sourceDb, IsOpen = true };
                Databases[NormalizePath(connectionInfo.Path)] = master;

                //This will be the list of the slave jobs to perform
                List<Settings> slaves = new List<Settings>();

                // Loop through all found entries - each one is a Sync job
                foreach (var settingsEntry in jobSettings)
                {
                    // Load settings for this job
                    var settings = Settings.Parse(settingsEntry, sourceDb);
                    currentJob = settingsEntry.Strings.GetSafe(PwDefs.TitleField).ReadString();

                    if (settings.IsSlave) // If this is true don't perform the job since it has to be done from the master DB
                        continue;

                    if (CheckKeyFile(sourceDb, settings, settingsEntry))
                        continue;

                    if (CheckTagOrGroup(settings, settingsEntry))
                        continue;

                    if (CheckTargetFilePath(settings, settingsEntry, sourceDb))
                        continue;

                    if (CheckPasswordOrKeyfile(settings, settingsEntry))
                        continue;

                    // Skip disabled/expired jobs
                    if (settings.Disabled)
                        continue;

                    // Execute the export, then remember the slave database (once) to perform its own jobs
                    if (RunJob(master, settings) && settings.PerformSlaveJobs &&
                        !slaves.Any(s => SamePath(s.TargetFilePath, settings.TargetFilePath)))
                        slaves.Add(settings);
                }

                //Start Synchronization from slaves
                foreach (Settings slave in slaves)
                    StartSyncAgain(GetDatabase(slave.TargetFilePath, slave));
            }
            catch (Exception e)
            {
                MessageService.ShowWarning("Synchronization failed:", e);
            }
            finally
            {
                connectionInfo = null;
                isRunning = false;
                currentJob = "";
            }
            return true;
        }

        /// <summary>
        /// Ends a synchronization run: writes or discards the databases and refreshes the UI.
        /// </summary>
        /// <param name="writeFiles">Write the changed databases that are not opened in KeePass.</param>
        /// <param name="masterSaved">The master has been saved with the changes of this run.</param>
        private static void EndSync(bool writeFiles, bool masterSaved)
        {
            try
            {
                SaveDatabases(writeFiles, masterSaved);
            }
            catch (Exception e)
            {
                MessageService.ShowWarning("Synchronization failed:", e);
            }
            finally
            {
                Databases.Clear();
                master = null;
                syncedSave = Guid.Empty;
            }
        }

        private static void StartSyncAgain(SyncDatabase source)
        {
            PwDatabase sourceDb = source.Database;

            // Get all entries out of the group "MSSyncJobs". Each one is a sync job
            PwGroup settingsGroup = sourceDb.RootGroup.Groups.FirstOrDefault(g => g.Name == "MSSyncJobs");
            if (settingsGroup == null)
            {
                return;
            }
            IEnumerable<PwEntry> jobSettings = settingsGroup.Entries;

            // Loop through all found entries - each one is a sync job
            foreach (var settingsEntry in jobSettings)
            {
                // Load settings for this job
                var settings = Settings.Parse(settingsEntry, sourceDb);
                currentJob = settingsEntry.Strings.GetSafe(PwDefs.TitleField).ReadString();

                // Skip disabled/expired jobs
                if (settings.Disabled)
                    continue;

                if (CheckTargetFilePath(settings, settingsEntry, sourceDb))
                    continue;

                // A job targeting the master needs no password or key file, the master is already open
                if (!SamePath(settings.TargetFilePath, connectionInfo.Path))
                {
                    if (CheckKeyFile(sourceDb, settings, settingsEntry))
                        continue;

                    if (CheckPasswordOrKeyfile(settings, settingsEntry))
                        continue;
                }

                if (CheckTagOrGroup(settings, settingsEntry))
                    continue;

                // Execute the export
                RunJob(source, settings);
            }
        }

        /// <summary>
        /// Executes a job and shows a warning if it fails.
        /// </summary>
        /// <returns>True if the job succeeded.</returns>
        private static bool RunJob(SyncDatabase source, Settings settings)
        {
            try
            {
                SyncToDb(source, settings);
                return true;
            }
            catch (Exception e)
            {
                // Don't write a target database that may be synchronized only partially
                SyncDatabase target;
                if (Databases.TryGetValue(NormalizePath(settings.TargetFilePath), out target))
                    target.Failed = true;

                MessageService.ShowWarning("Synchronization failed:", e);
                return false;
            }
        }

        /// <summary>
        /// Gets a database of the current run. A database opened in KeePass is used directly, so no file
        /// is written behind its back. Any other database is opened from its file.
        /// </summary>
        private static SyncDatabase GetDatabase(string filePath, Settings settings)
        {
            string path = NormalizePath(filePath);
            SyncDatabase db;
            if (Databases.TryGetValue(path, out db))
                return db;

            var document = Program.MainForm.DocumentManager.Documents.FirstOrDefault(d =>
                d.Database.IsOpen && SamePath(d.Database.IOConnectionInfo.Path, path));
            if (document != null)
                db = new SyncDatabase() { Database = document.Database, IsOpen = true };
            else
                db = new SyncDatabase()
                {
                    Database = OpenTargetDatabase(filePath, CreateCompositeKey(settings.Password, settings.KeyFilePath))
                };

            Databases.Add(path, db);
            return db;
        }

        /// <summary>
        /// Writes each changed database once. Databases opened in KeePass are not written but refreshed in
        /// the UI and marked as modified (except the master if it has been saved with the changes).
        /// </summary>
        private static void SaveDatabases(bool writeFiles, bool masterSaved)
        {
            bool updateUI = false;

            foreach (SyncDatabase db in Databases.Values)
            {
                if (db.IsOpen)
                {
                    if (db.Changed)
                    {
                        if (db != master || !masterSaved)
                            db.Database.Modified = true;
                        db.Database.UINeedsIconUpdate = true;
                        updateUI = true;
                    }
                    continue;
                }

                if (writeFiles && db.Changed && !db.Failed)
                {
                    try
                    {
                        db.Database.Save(new NullStatusLogger());
                    }
                    catch (Exception e)
                    {
                        MessageService.ShowWarning("Synchronization failed:", db.Database.IOConnectionInfo.Path, e);
                    }
                }
                db.Database.Close();
            }

            if (updateUI)
                Program.MainForm.UpdateUI(true, null, true, null, true, null, false);
        }

        private static bool SamePath(string path1, string path2)
        {
            return string.Equals(NormalizePath(path1), NormalizePath(path2), StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizePath(string path)
        {
            try
            {
                return Path.GetFullPath(path);
            }
            catch (Exception)
            {
                // Not a local path, e.g. a URL
                return path ?? "";
            }
        }

        private static Boolean CheckKeyFile(PwDatabase sourceDb, Settings settings, PwEntry settingsEntry)
        {
            // If a key file is given it must exist.
            if (!string.IsNullOrEmpty(settings.KeyFilePath))
            {
                // Default to same folder as sourceDb for the keyfile if no directory is specified
                if (!Path.IsPathRooted(settings.KeyFilePath))
                {
                    string sourceDbPath = Path.GetDirectoryName(sourceDb.IOConnectionInfo.Path);
                    if (sourceDbPath != null)
                    {
                        settings.KeyFilePath = Path.Combine(sourceDbPath, settings.KeyFilePath);
                    }
                }

                if (!File.Exists(settings.KeyFilePath))
                {
                    MessageService.ShowWarning("MasterSlaveSync: Keyfile is given but could not be found for: " +
                                               settingsEntry.Strings.ReadSafe("Title"), settings.KeyFilePath);
                    return true;
                }
            }

            return false;
        }

        private static bool CheckTagOrGroup(Settings settings, PwEntry settingsEntry)
        {
            // Require at least one of Tag or Group
            if (string.IsNullOrEmpty(settings.Tag) && string.IsNullOrEmpty(settings.Group))
            {
                MessageService.ShowWarning("MasterSlaveSync: Missing Tag or Group for: " +
                                           settingsEntry.Strings.ReadSafe("Title"));
                return true;
            }

            return false;
        }

        private static bool CheckTargetFilePath(Settings settings, PwEntry settingsEntry, PwDatabase sourceDb)
        {
            // Require targetFilePath
            if (string.IsNullOrEmpty(settings.TargetFilePath))
            {
                MessageService.ShowWarning("MasterSlaveSync: Missing TargetFilePath for: " +
                                           settingsEntry.Strings.ReadSafe("Title"));
                return true;
            }

            // Default to same folder as sourceDb for the keyfile if no directory is specified
            if (!Path.IsPathRooted(settings.TargetFilePath))
            {
                string sourceDbPath = Path.GetDirectoryName(sourceDb.IOConnectionInfo.Path);
                if (sourceDbPath != null)
                {
                    settings.TargetFilePath = Path.Combine(sourceDbPath, settings.TargetFilePath);
                }
            }

            if (!File.Exists(settings.TargetFilePath))
            {
                MessageService.ShowWarning("MasterSlaveSync: Slave Database not found for: " +
                                           settingsEntry.Strings.ReadSafe("Title"));
                return true;
            }

            return false;
        }

        private static bool CheckPasswordOrKeyfile(Settings settings, PwEntry settingsEntry)
        {
            // Require at least one of Password or KeyFilePath.
            if (settings.Password.IsEmpty && !File.Exists(settings.KeyFilePath))
            {
                MessageService.ShowWarning("MasterSlaveSync: Missing Password or valid KeyFilePath for: " +
                                           settingsEntry.Strings.ReadSafe("Title"));
                return true;
            }

            return false;
        }

        /// <summary>
        /// Exports all entries with the given tag to a new database at the given path.
        /// </summary>
        /// <param name="source">The source database.</param>
        /// <param name="settings">The settings for this job.</param>
        private static void SyncToDb(SyncDatabase source, Settings settings)
        {
            // Get the target database (the changes are written once at the end, see SaveDatabases)
            SyncDatabase target = GetDatabase(settings.TargetFilePath, settings);
            PwDatabase sourceDb = source.Database;
            PwDatabase targetDatabase = target.Database;

            // Assign the properties of the source root group to the target root group
            if (HandleCustomIcon(targetDatabase, sourceDb, sourceDb.RootGroup))
                target.Changed = true;

            // Find all entries matching the tag and/or group
            PwObjectList<PwEntry> entries = GetMatching(sourceDb, settings);

            // Copy all entries to the new database
            CopyEntriesAndGroups(source, settings, entries, target);

            //Delete slave entries that match Master settings (But not in master)
            PwObjectList<PwEntry> targetList = GetMatching(targetDatabase, settings);

            // Entries created in the slave are copied to the master instead (if enabled)
            if (settings.SyncNewSlaveEntries)
                foreach (PwEntry entry in CopyNewSlaveEntries(entries, targetList, source, target, settings))
                    targetList.Remove(entry);

            if (SelectEntriesAndDelete(entries, targetList, targetDatabase))
                target.Changed = true;
        }

        /// <summary>
        /// Copies the entries created in the slave (target) database to the master (source) of the job. An entry
        /// is new if the master neither contains it (anywhere) nor has deleted it.
        /// </summary>
        /// <param name="masterList">The entries of the master matching the job.</param>
        /// <param name="slaveList">The entries of the slave matching the job.</param>
        /// <returns>The new entries of the slave database.</returns>
        private static List<PwEntry> CopyNewSlaveEntries(PwObjectList<PwEntry> masterList, PwObjectList<PwEntry> slaveList,
            SyncDatabase source, SyncDatabase target, Settings settings)
        {
            PwDatabase masterDb = source.Database;
            PwDatabase slaveDb = target.Database;
            IEnumerable<PwUuid> masterUuid = masterList.Select(u => u.Uuid);

            List<PwEntry> newEntries = slaveList.Where(e => !masterUuid.Contains(e.Uuid) &&
                masterDb.RootGroup.FindEntry(e.Uuid, true) == null &&
                !masterDb.DeletedObjects.Any(d => d.Uuid.Equals(e.Uuid)) &&
                !IsInRecycleBin(e, slaveDb)).ToList();

            foreach (PwEntry entry in newEntries)
            {
                // Get (or create) the group of the entry in the master database (including hierarchy)
                PwGroup masterGroup = TargetGroupInDatebase(entry, source, slaveDb);

                CloneEntry(slaveDb, masterDb, entry, null, masterGroup, settings);
                source.Changed = true;
            }

            return newEntries;
        }

        private static bool IsInRecycleBin(PwEntry entry, PwDatabase db)
        {
            if (db.RecycleBinUuid.Equals(PwUuid.Zero))
                return false;

            for (PwGroup group = entry.ParentGroup; group != null; group = group.ParentGroup)
                if (group.Uuid.Equals(db.RecycleBinUuid))
                    return true;

            return false;
        }

        private static CompositeKey CreateCompositeKey(ProtectedString password, string keyFilePath)
        {
            CompositeKey key = new CompositeKey();

            if (!password.IsEmpty)
            {
                IUserKey mKeyPass = new KcpPassword(password.ReadUtf8());
                key.AddUserKey(mKeyPass);
            }

            // Load a keyfile for the target database if requested (and add it to the key)
            if (!string.IsNullOrEmpty(keyFilePath))
            {
                IUserKey mKeyFile = new KcpKeyFile(keyFilePath);
                key.AddUserKey(mKeyFile);
            }

            return key;
        }

        private static PwDatabase OpenTargetDatabase(string targetFilePath, CompositeKey key)
        {
            // Create a new database 
            PwDatabase targetDatabase = new PwDatabase();

            // Connect the database object to the existing database
            targetDatabase.Open(new IOConnectionInfo()
            {
                Path = targetFilePath
            }, key, null);

            return targetDatabase;
        }

        /// <summary>
        /// Copies the custom icons required for this group to the target database.
        /// </summary>
        /// <param name="targetDatabase">The target database where to add the icons.</param>
        /// <param name="sourceDatabase">The source database where to get the icons from.</param>
        /// <param name="sourceGroup">The source group which icon should be copied (if it is custom).</param>
        /// <returns>True if the icon was added to the target database.</returns>
        private static bool HandleCustomIcon(PwDatabase targetDatabase, PwDatabase sourceDatabase, PwGroup sourceGroup)
        {
            // Does the group not use a custom icon or is it already in the target database
            if (sourceGroup.CustomIconUuid.Equals(PwUuid.Zero) ||
                targetDatabase.GetCustomIconIndex(sourceGroup.CustomIconUuid) != -1)
            {
                return false;
            }

            // Check if the custom icon really is in the source database
            int iconIndex = sourceDatabase.GetCustomIconIndex(sourceGroup.CustomIconUuid);
            if (iconIndex < 0 || iconIndex > sourceDatabase.CustomIcons.Count - 1)
            {
                MessageService.ShowWarning("Can't locate custom icon (" + sourceGroup.CustomIconUuid.ToHexString() +
                                           ") for group " + sourceGroup.Name);
            }

            // Get the custom icon from the source database
            PwCustomIcon customIcon = sourceDatabase.CustomIcons[iconIndex];

            // Copy the custom icon to the target database
            targetDatabase.CustomIcons.Add(customIcon);
            return true;
        }

        /// <summary>
        /// Copies the custom icons required for this group to the target database.
        /// </summary>
        /// <param name="targetDatabase">The target database where to add the icons.</param>
        /// <param name="sourceDb">The source database where to get the icons from.</param>
        /// <param name="entry">The entry which icon should be copied (if it is custom).</param>
        /// <returns>True if the icon was added to the target database.</returns>
        private static bool HandleCustomIcon(PwDatabase targetDatabase, PwDatabase sourceDb, PwEntry entry)
        {
            // Does the entry not use a custom icon or is it already in the target database
            if (entry.CustomIconUuid.Equals(PwUuid.Zero) ||
                targetDatabase.GetCustomIconIndex(entry.CustomIconUuid) != -1)
            {
                return false;
            }

            // Check if the custom icon really is in the source database
            int iconIndex = sourceDb.GetCustomIconIndex(entry.CustomIconUuid);
            if (iconIndex < 0 || iconIndex > sourceDb.CustomIcons.Count - 1)
            {
                MessageService.ShowWarning("Can't locate custom icon (" + entry.CustomIconUuid.ToHexString() +
                                           ") for entry " + entry.Strings.ReadSafe("Title"));
            }

            // Get the custom icon from the source database
            PwCustomIcon customIcon = sourceDb.CustomIcons[iconIndex];

            // Copy the custom icon to the target database
            targetDatabase.CustomIcons.Add(customIcon);
            return true;
        }

        private static PwObjectList<PwEntry> GetMatching(PwDatabase sourceDb, Settings settings)
        {
            PwObjectList<PwEntry> entries = new PwObjectList<PwEntry>();

            if (!string.IsNullOrEmpty(settings.Tag) && string.IsNullOrEmpty(settings.Group))
            {
                // Tag only export
                // Support multiple tag (Tag1,Tag2)
                foreach (string tag in settings.Tag.Split(','))
                {
                    PwObjectList<PwEntry> tagEntries = new PwObjectList<PwEntry>();
                    sourceDb.RootGroup.FindEntriesByTag(tag, tagEntries, true);
                    // Prevent duplicated entries
                    IEnumerable<PwUuid> existingUuids = entries.Select(x => x.Uuid);
                    List<PwEntry> entriesToAdd = tagEntries.Where(x => !existingUuids.Contains(x.Uuid)).ToList();
                    entries.Add(entriesToAdd);
                }
            }
            else if (string.IsNullOrEmpty(settings.Tag) && !string.IsNullOrEmpty(settings.Group))
            {
                // Support multiple group (Group1,Group2)
                foreach (string group in settings.Group.Split(','))
                {
                    // group only export
                    PwGroup groupToExport = sourceDb.RootGroup.GetFlatGroupList().FirstOrDefault(g => g.Name == group);

                    if (groupToExport == null)
                    {
                        throw new ArgumentException("No group with the name of the Group-Setting found.");
                    }

                    PwObjectList<PwEntry> groupEntries = groupToExport.GetEntries(true);
                    // Prevent duplicated entries
                    IEnumerable<PwUuid> existingUuids = entries.Select(x => x.Uuid);
                    List<PwEntry> entriesToAdd = groupEntries.Where(x => !existingUuids.Contains(x.Uuid)).ToList();
                    entries.Add(entriesToAdd);
                }
            }
            else if (!string.IsNullOrEmpty(settings.Tag) && !string.IsNullOrEmpty(settings.Group))
            {
                // Tag and group export
                foreach (string group in settings.Group.Split(','))
                {
                    PwGroup groupToExport = sourceDb.RootGroup.GetFlatGroupList().FirstOrDefault(g => g.Name == group);


                    if (groupToExport == null)
                    {
                        throw new ArgumentException("No group with the name of the Group-Setting found.");
                    }

                    foreach (string tag in settings.Tag.Split(','))
                    {
                        PwObjectList<PwEntry> tagEntries = new PwObjectList<PwEntry>();
                        groupToExport.FindEntriesByTag(tag, tagEntries, true);

                        // Prevent duplicated entries
                        IEnumerable<PwUuid> existingUuids = entries.Select(x => x.Uuid);
                        List<PwEntry> entriesToAdd = tagEntries.Where(x => !existingUuids.Contains(x.Uuid)).ToList();
                        entries.Add(entriesToAdd);
                    }
                }
            }
            else
            {
                throw new ArgumentException("At least one of Tag or Group Name must be set.");
            }

            return entries;
        }

        private static void CopyEntriesAndGroups(SyncDatabase source, Settings settings, PwObjectList<PwEntry> entries,
            SyncDatabase target)
        {
            PwDatabase sourceDb = source.Database;
            PwDatabase targetDatabase = target.Database;

            foreach (PwEntry entry in entries)
            {
                // Get (or create in case its the first sync) the target group in the target database (including hierarchy)
                PwGroup targetGroup = TargetGroupInDatebase(entry, target, sourceDb);

                PwEntry peNew = targetGroup.FindEntry(entry.Uuid, bSearchRecursive: false);

                // Check if the target entry is newer than the source entry  && peNew.LastModificationTime > entry.LastModificationTime
                if (peNew != null && peNew.LastModificationTime.CompareTo(entry.LastModificationTime) > 0)
                {
                    if (CloneEntry(targetDatabase, sourceDb, peNew, entry, targetGroup, settings))
                        source.Changed = true;
                    continue;
                }

                // Handle Duplicates entries' Uuids
                PwEntry duplicatedEntry = targetDatabase.RootGroup.FindEntry(entry.Uuid, true);
                if (duplicatedEntry != null && duplicatedEntry.ParentGroup.Uuid.ToHexString() != targetGroup.Uuid.ToHexString())
                {
                    DeleteEntry(duplicatedEntry, targetDatabase);
                    target.Changed = true;
                }

                if (CloneEntry(sourceDb, targetDatabase, entry, peNew, targetGroup, settings))
                    target.Changed = true;
            }
        }

        /// <summary>
        /// Get or create the target group of an entry in the target database (including hierarchy).
        /// </summary>
        /// <param name="entry">An entry wich is located in the folder with the target structure.</param>
        /// <param name="target">The target database in which the folder structure should be created.</param>
        /// <param name="sourceDatabase">The source database from which the folder properties should be taken.</param>
        /// <returns>The target folder in the target database.</returns>
        private static PwGroup TargetGroupInDatebase(PwEntry entry, SyncDatabase target, PwDatabase sourceDatabase)
        {
            PwDatabase targetDatabase = target.Database;

            // Collect all group names from the entry up to the root group
            PwGroup group = entry.ParentGroup;
            List<PwUuid> list = new List<PwUuid>();

            while (group != null)
            {
                list.Add(group.Uuid);
                group = group.ParentGroup;
            }

            // Remove root group (we already changed the root group name)
            list.RemoveAt(list.Count - 1);
            // groups are in a bottom-up oder -> reverse to get top-down
            list.Reverse();

            // Create group structure for the new entry (copying group properties)
            PwGroup lastGroup = targetDatabase.RootGroup;
            foreach (PwUuid id in list)
            {
                // Does the target group already exist?
                PwGroup newGroup = lastGroup.FindGroup(id, false);
                if (newGroup != null)
                {
                    lastGroup = newGroup;
                    continue;
                }

                // Get the source group
                PwGroup sourceGroup = sourceDatabase.RootGroup.FindGroup(id, true);

                // Create a new group and asign all properties from the source group
                newGroup = new PwGroup();
                newGroup.AssignProperties(sourceGroup, false, true);
                HandleCustomIcon(targetDatabase, sourceDatabase, sourceGroup);

                // Add the new group at the right position in the target database
                lastGroup.AddGroup(newGroup, true);
                target.Changed = true;

                lastGroup = newGroup;
            }

            // Return the target folder (leaf folder)
            return lastGroup;
        }

        /// <returns>True if the target entry was changed.</returns>
        private static bool CloneEntry(PwDatabase sourceDb, PwDatabase targetDb, PwEntry sourceEntry,
            PwEntry targetEntry, PwGroup targetGroup, Settings settings)
        {
            bool changed = false;

            // Was no existing entry in the target database found?
            if (targetEntry == null)
            {
                // Create a new entry
                targetEntry = new PwEntry(false, false)
                {
                    Uuid = sourceEntry.Uuid
                };

                //targetEntry = sourceEntry.CloneDeep();

                // Add entry to the target group in the new database
                targetGroup.AddEntry(targetEntry, true);
                changed = true;
            }

            // Clone entry properties if ExportUserAndPassOnly is false
            // (entries with the same modification time are already synchronized)
            if (!settings.ExportUserAndPassOnly)
            {
                if (changed || targetEntry.LastModificationTime != sourceEntry.LastModificationTime)
                {
                    targetEntry.AssignProperties(sourceEntry, false, true, true);
                    changed = true;
                }
            }
            else
            {
                //[WIP]Implement copy maybe notes?
                //peNew.Strings.Set(PwDefs.NotesField,
                //        entry.Strings.GetSafe(PwDefs.NotesField));
            }

            // This is neccesary to support field refferences. Maybe notes field too?
            string[] fieldNames = { PwDefs.TitleField , PwDefs.UserNameField ,
                PwDefs.PasswordField, PwDefs.UrlField };

            foreach (string fieldName in fieldNames)
            {
                ProtectedString value = Settings.GetFieldWRef(sourceEntry, sourceDb, fieldName);
                if (changed || targetEntry.Strings.ReadSafe(fieldName) != value.ReadString())
                {
                    targetEntry.Strings.Set(fieldName, value);
                    changed = true;
                }
            }

            // Handle custom icon
            if (HandleCustomIcon(targetDb, sourceDb, sourceEntry))
                changed = true;

            return changed;
        }

        /// <returns>True if entries were deleted.</returns>
        private static bool SelectEntriesAndDelete(PwObjectList<PwEntry> masterList, PwObjectList<PwEntry> slaveList, PwDatabase targetDb)
        {
            //Find entries in slaveList not in masterList to delete
            IEnumerable<PwUuid> masterUuid = masterList.Select(u => u.Uuid);
            var toDelete = slaveList.Where(e => !masterUuid.Contains(e.Uuid)).ToList();

            DeleteEntries(toDelete, targetDb);
            return toDelete.Count > 0;
        }

        private static void DeleteEntries(List<PwEntry> entriesToDelete, PwDatabase dB)
        {
            try
            {
                if (entriesToDelete.Count() > 0)
                {
                    foreach (PwEntry entry in entriesToDelete)
                    {
                        var parentGroup = entry.ParentGroup;
                        parentGroup.Entries.Remove(entry);
                        var pdo = new PwDeletedObject(entry.Uuid, DateTime.Now);
                        dB.DeletedObjects.Add(pdo);
                    }
                }
            }
            catch (Exception e) { MessageService.ShowInfo(e.Message); }
        }

        private static void DeleteEntry(PwEntry entryToDelete, PwDatabase dB)
        {
            try
            {
                if (entryToDelete != null)
                {
                    var parentGroup = entryToDelete.ParentGroup;
                    parentGroup.Entries.Remove(entryToDelete);
                    var pdo = new PwDeletedObject(entryToDelete.Uuid, DateTime.Now);
                    dB.DeletedObjects.Add(pdo);
                }
            }
            catch (Exception e) { MessageService.ShowInfo(e.Message); }
        }

    }
}