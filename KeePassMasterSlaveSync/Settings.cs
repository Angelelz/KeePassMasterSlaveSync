using KeePassLib;
using KeePassLib.Security;
using KeePass.Util.Spr;
using System.Collections.Generic;
using System.Linq;
using System;
using KeePassLib.Utility;

namespace KeePassMasterSlaveSync
{
    /// <summary>
    /// Contais all settings for a job.
    /// </summary>
    public class Settings
    {
        /// <summary>
        /// The password to protect the target database(optional if <see cref="KeyFilePath"/> is set)
        /// </summary>
        public ProtectedString Password { get; internal set; }
        /// <summary>
        /// The path for the target database.
        /// </summary>
        public string TargetFilePath { get; set; }
        /// <summary>
        /// The path to a key file to protect the target database (optional if <see cref="Password"/> is set).
        /// </summary>
        public string KeyFilePath { get; set; }
        /// <summary>
        /// The name of the group to export (optional if <see cref="Tag"/> is set).
        /// </summary>
        public string Group { get; private set; }
        /// <summary>
        /// Tag to export (optional if <see cref="Group"/> is set).
        /// </summary>
        public string Tag { get; private set; }
        /// <summary>
        /// If true, this export job will be ignored.
        /// </summary>
        public bool Disabled { get; private set; }
        /// <summary>
        /// If true, Only Username and Password will be exported to the target Database.
        /// </summary>
        public bool ExportUserAndPassOnly { get; private set; }
        /// <summary>
        /// If true, MasterSlaveSync jobs on target database will be executed.
        /// </summary>
        public bool PerformSlaveJobs { get; private set; }
        /// <summary>
        /// If true, this job will be ignored when not executed from a Master database.
        /// </summary>
        public bool IsSlave { get; private set; }

        // Private constructor
        private Settings()
        {
        }

        /// <summary>
        /// Read all job settings from an entry.
        /// </summary>
        /// <param name="settingsEntry">The entry to read the settings from.</param>
        /// <returns>A settings object containing all the settings for this job.</returns>
        public static Settings Parse(PwEntry settingsEntry, PwDatabase sourceDb)
        {

            return new Settings()
            {
                //Password = settingsEntry.Strings.GetSafe("Password"),
                Password = GetFieldWRef(settingsEntry, sourceDb, PwDefs.PasswordField),
                TargetFilePath = settingsEntry.Strings.ReadSafe("MSS_TargetFilePath"),
                KeyFilePath = settingsEntry.Strings.ReadSafe("MSS_KeyFilePath"),
                Group = settingsEntry.Strings.ReadSafe("MSS_Group"),
                Tag = settingsEntry.Strings.ReadSafe("MSS_Tag"),
                Disabled = (settingsEntry.Expires),
                ExportUserAndPassOnly = settingsEntry.Strings.ReadSafe("MSS_ExportUserAndPassOnly").ToLower().Trim() == "true",
                PerformSlaveJobs = settingsEntry.Strings.ReadSafe("MSS_PerformSlaveJobs").ToLower().Trim() != "false",
                IsSlave = settingsEntry.Strings.ReadSafe("MSS_IsSlave").ToLower().Trim() == "true"
            };
        }

        public static string GetField(string code)
        {
            string field = "";
            switch (code)
            {
                case ("T"):
                    field = PwDefs.TitleField;
                    break;
                case ("U"):
                    field = PwDefs.UserNameField;
                    break;
                case ("P"):
                    field = PwDefs.PasswordField;
                    break;
                case ("A"):
                    field = PwDefs.UrlField;
                    break;
                case ("N"):
                    field = PwDefs.NotesField;
                    break;
            }
            return field;
        }

        public static Byte[] ConvertoPwUuid(string hex)
        {
            int NumberChars = hex.Length;
            byte[] bytes = new byte[NumberChars / 2];
            for (int i = 0; i < NumberChars; i += 2)
                bytes[i / 2] = Convert.ToByte(hex.Substring(i, 2), 16);
            return bytes;
        }

        public static ProtectedString GetFieldWRef(PwEntry entry, PwDatabase sourceDb, string fieldName)
        {
            ProtectedString psField = entry.Strings.GetSafe(fieldName);

            // {REF: represented in Byte array
            byte[] refByteArray = new byte[] { 123, 82, 69, 70, 58 };

            // First 5 characters of the field
            byte[] byteField = psField.ReadUtf8().Take(5).ToArray();

            bool isRef = byteField.SequenceEqual(refByteArray);
            MemUtil.ZeroByteArray(byteField);

            // if the field starts with {REF:
            if (isRef)
            {
                try
                {
                    /* A reference looks like this:
                     * {REF:P@I:46C9B1FFBD4ABC4BBB260C6190BAD20C}
                     * {REF:<WantedField>@<SearchIn>:<Text>}
                     */

                    // First 8 characters of the field {REF:<WantedField>@<SearchIn>
                    string refField = psField.Remove(8, psField.Length - 8).ReadString();

                    // <WantedField> Character
                    string wantedField = refField.Substring(5, 1);

                    // <SearchIn> Character
                    string searchIn = refField.Substring(7, 1);

                    // text of the field, protected in case it's a password
                    ProtectedString text = psField.Remove(psField.Length - 1, 1).Remove(0, 9);

                    PwEntry refEntry = null;
                    if (searchIn == "I")
                    {
                        PwUuid uuid = new PwUuid(ConvertoPwUuid(text.ReadString()));
                        refEntry = sourceDb.RootGroup.FindEntry(uuid, true);
                    }
                    else
                    {
                        var entries = sourceDb.RootGroup.GetEntries(true).ToList();
                        refEntry = entries.Where(e => e.Strings.GetSafe(GetField(searchIn)).Equals(text)).FirstOrDefault();
                    }
                    if (refEntry != null)
                        return GetFieldWRef(refEntry, sourceDb, GetField(wantedField));   // Allow recursivity
                }
                catch (Exception) { return psField; }  // In case the ref doesn't exists or has a mistake
            }
            return psField;
        }
    }
    

}