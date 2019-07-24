using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using KeePass;
using KeePassLib;
using KeePassLib.Collections;
using KeePassLib.Keys;
using KeePassLib.Utility;
using KeePassLib.Serialization;
using KeePassLib.Security;

namespace KeePassMasterSlaveSync
{
    public class Sync
    {
        private static readonly IOConnectionInfo ConnectionInfo = new IOConnectionInfo();

        private static string currentJob = "";

        public static void StartSync(PwDatabase sourceDb)
        {
            // Get all entries out of the group "MSSyncJobs"
            PwGroup settingsGroup = sourceDb.RootGroup.Groups.FirstOrDefault(g => g.Name == "MSSyncJobs");
            if (settingsGroup == null)
            {
                return;
            }
            IEnumerable<PwEntry> jobSettings = settingsGroup.Entries;

            //This will be the list of the slave jobs to perform
            List<string> slavesPaths = new List<string>();
            List<ProtectedString> slavesPass = new List<ProtectedString>();
            List<string> slavesKeys = new List<string>();
            List<bool> slavesCheck = new List<bool>();

            // Loop through all found entries - each one is a Sync job 
            foreach (var settingsEntry in jobSettings)
            {
                // Load settings for this job
                var settings = Settings.Parse(settingsEntry, sourceDb);
                currentJob = settingsEntry.Strings.GetSafe(PwDefs.TitleField).ReadString();

                if (CheckKeyFile(sourceDb, settings, settingsEntry))
                    continue;

                if (CheckTagOrGroup(settings, settingsEntry))
                    continue;

                if (CheckTargetFilePath(settings, settingsEntry))
                    continue;

                if (CheckPasswordOrKeyfile(settings, settingsEntry))
                    continue;

                //Prevent repeated slave databases
                if (!slavesPaths.Contains(settings.TargetFilePath))
                {
                    slavesPaths.Add(settings.TargetFilePath);
                    slavesPass.Add(settings.Password);
                    slavesKeys.Add(settings.KeyFilePath);
                    slavesCheck.Add(settings.Disabled);
                }

                // Skip disabled/expired jobs
                if (settings.Disabled)
                    continue;

                try
                {
                // Execute the export 
                SyncToDb(sourceDb, settings);
                }
                catch (Exception e)
                {
                    MessageService.ShowWarning("Synchronization failed:", e);
                }
                Program.MainForm.Refresh();
            }


            //Start Synchronization from slaves
            for (int i = 0; i < slavesPaths.Count; ++i)
            {
                if (!slavesCheck[0])
                {

                    // Create a key for the target database
                    CompositeKey key = CreateCompositeKey(slavesPass[i], slavesKeys[i]);

                    // Create or open the target database
                    PwDatabase pwDatabase = OpenTargetDatabase(slavesPaths[i], key);

                    StartSyncAgain(pwDatabase);
                }
            }
        }

        public static void StartSyncAgain(PwDatabase sourceDb)
        {
            //If target job includes sync back with Program.MainForm.ActiveDatabase
            //Close and open at the end of sync job
            IOConnectionInfo ioinfo = Program.MainForm.ActiveDatabase.IOConnectionInfo;
            CompositeKey nkey = Program.MainForm.ActiveDatabase.MasterKey;
            Program.MainForm.ActiveDatabase.Close();

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

                if (CheckKeyFile(sourceDb, settings, settingsEntry))
                    continue;

                if (CheckTagOrGroup(settings, settingsEntry))
                    continue;

                if (CheckTargetFilePath(settings, settingsEntry))
                    continue;

                if (CheckPasswordOrKeyfile(settings, settingsEntry))
                    continue;

                try
                {
                    // Execute the export 
                    SyncToDb(sourceDb, settings);
                }
                catch (Exception e)
                {
                    MessageService.ShowWarning("Synchronization failed:", e);
                }
            }

            Program.MainForm.OpenDatabase(ioinfo, nkey, true);
            Program.MainForm.Refresh();
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

        private static bool CheckTargetFilePath(Settings settings, PwEntry settingsEntry)
        {
            // Require targetFilePath
            if (string.IsNullOrEmpty(settings.TargetFilePath))
            {
                MessageService.ShowWarning("MasterSlaveSync: Missing TargetFilePath for: " +
                                           settingsEntry.Strings.ReadSafe("Title"));
                return true;
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
        /// <param name="sourceDb">The source database.</param>
        /// <param name="settings">The settings for this job.</param>
        private static void SyncToDb(PwDatabase sourceDb, Settings settings)
        {
            // Create a key for the target database
            CompositeKey key = CreateCompositeKey(settings.Password, settings.KeyFilePath);

            // Create or open the target database
            PwDatabase targetDatabase = OpenTargetDatabase(settings.TargetFilePath, key);

            // Assign the properties of the source root group to the target root group
            HandleCustomIcon(targetDatabase, sourceDb, sourceDb.RootGroup);

            // Find all entries matching the tag and/or group
            PwObjectList<PwEntry> entries = GetMatching(sourceDb, settings);

            // Copy all entries to the new database
            CopyEntriesAndGroups(sourceDb, settings, entries, targetDatabase);
            targetDatabase.Save(null);

            //Delete slave entries that match Master settings (But not in master)
            PwObjectList<PwEntry> targetList = GetMatching(targetDatabase, settings);
            DeleteExtraEntries(entries, targetList, targetDatabase);
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
        private static void HandleCustomIcon(PwDatabase targetDatabase, PwDatabase sourceDatabase, PwGroup sourceGroup)
        {
            // Does the group not use a custom icon or is it already in the target database
            if (sourceGroup.CustomIconUuid.Equals(PwUuid.Zero) ||
                targetDatabase.GetCustomIconIndex(sourceGroup.CustomIconUuid) != -1)
            {
                return;
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
        }

        /// <summary>
        /// Copies the custom icons required for this group to the target database.
        /// </summary>
        /// <param name="targetDatabase">The target database where to add the icons.</param>
        /// <param name="sourceDb">The source database where to get the icons from.</param>
        /// <param name="entry">The entry which icon should be copied (if it is custom).</param>
        private static void HandleCustomIcon(PwDatabase targetDatabase, PwDatabase sourceDb, PwEntry entry)
        {
            // Does the entry not use a custom icon or is it already in the target database
            if (entry.CustomIconUuid.Equals(PwUuid.Zero) ||
                targetDatabase.GetCustomIconIndex(entry.CustomIconUuid) != -1)
            {
                return;
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

        private static void CopyEntriesAndGroups(PwDatabase sourceDb, Settings settings, PwObjectList<PwEntry> entries,
            PwDatabase targetDatabase)
        {
            foreach (PwEntry entry in entries)
            {
                // Get (or create in case its the first sync) the target group in the target database (including hierarchy)
                PwGroup targetGroup = TargetGroupInDatebase(entry, targetDatabase, sourceDb);

                PwEntry peNew = targetGroup.FindEntry(entry.Uuid, bSearchRecursive: false);



                // Check if the target entry is newer than the source entry  && peNew.LastModificationTime > entry.LastModificationTime
                if (peNew != null && peNew.LastModificationTime.CompareTo(entry.LastModificationTime) > 0)
                {
                    CloneEntry(targetDatabase, sourceDb, peNew, entry, targetGroup, settings);
                    sourceDb.Save(null);
                    continue;
                }


                // Make sure slave had not deleted any sync entries otherwise there will be duplicated uuids
                if (targetDatabase.RecycleBinEnabled)
                {
                    var recycleBin = targetDatabase.RootGroup.FindGroup(targetDatabase.RecycleBinUuid, false);
                    PwEntry repEntry = recycleBin.FindEntry(entry.Uuid, true);
                    if (repEntry != null)
                    {
                        repEntry.SetUuid(new PwUuid(true), false);
                    }
                }

                CloneEntry(sourceDb, targetDatabase, entry, peNew, targetGroup, settings);
            }
        }

        /// <summary>
        /// Get or create the target group of an entry in the target database (including hierarchy).
        /// </summary>
        /// <param name="entry">An entry wich is located in the folder with the target structure.</param>
        /// <param name="targetDatabase">The target database in which the folder structure should be created.</param>
        /// <param name="sourceDatabase">The source database from which the folder properties should be taken.</param>
        /// <returns>The target folder in the target database.</returns>
        private static PwGroup TargetGroupInDatebase(PwEntry entry, PwDatabase targetDatabase, PwDatabase sourceDatabase)
        {
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

                lastGroup = newGroup;
            }

            // Return the target folder (leaf folder)
            return lastGroup;
        }

        private static void CloneEntry(PwDatabase sourceDb, PwDatabase targetDb, PwEntry sourceEntry,
            PwEntry targetEntry, PwGroup targetGroup, Settings settings)
        {
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
            }

            // Clone entry properties if ExportUserAndPassOnly is false
            if (!settings.ExportUserAndPassOnly)
            {
                targetEntry.AssignProperties(sourceEntry, false, true, true);
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
                targetEntry.Strings.Set(fieldName, Settings.GetFieldWRef(sourceEntry, sourceDb, fieldName));

            // Handle custom icon
            HandleCustomIcon(targetDb, sourceDb, sourceEntry);
        }

        private static void DeleteExtraEntries(PwObjectList<PwEntry> masterList, PwObjectList<PwEntry> slaveList, PwDatabase targetDb)
        {
            //Find entries in slaveList not in masterList to delete
            IEnumerable<PwUuid> masterUuid = masterList.Select(x => x.Uuid);
            var toDelete = slaveList.Where(x => !masterUuid.Contains(x.Uuid));

            try
            {
                //targetDb.Save(null);
                if (toDelete.Count() > 0)
                {
                    var deletedObjects = new PwObjectList<PwDeletedObject>();

                    foreach (PwEntry entry in toDelete)
                        deletedObjects.Add(new PwDeletedObject(entry.Uuid, DateTime.Now));

                    //targetDb.DeletedObjects.Clear();
                    targetDb.DeletedObjects.Add(deletedObjects);
                    targetDb.MergeIn(targetDb, PwMergeMethod.Synchronize);
                    targetDb.Save(null);
                }
            }
            catch (Exception ev) { MessageService.ShowInfo(ev.Message); }
        }

    }
}