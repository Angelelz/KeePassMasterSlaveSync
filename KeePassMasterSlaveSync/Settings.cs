using KeePassLib;
using KeePassLib.Security;
using KeePass.Util.Spr;

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

        public static ProtectedString GetFieldWRef(PwEntry entry, PwDatabase sourceDb, string fieldName)
        //Had to implement this fuction here, since Exporter is private
        {
            SprContext ctx = new SprContext(entry, sourceDb,
                    SprCompileFlags.All, false, false);
            return ProtectedString.EmptyEx.Insert(0, SprEngine.Compile(
                    entry.Strings.ReadSafe(fieldName), ctx));
            /* This next code is borrowed from KeePassHttps, I don't know what it does:
            var f = (MethodInvoker)delegate
            {
                // apparently, SprEngine.Compile might modify the database
                host.MainWindow.UpdateUI(false, null, false, null, false, null, false);
            };
            if (host.MainWindow.InvokeRequired)
                host.MainWindow.Invoke(f);
            else
                f.Invoke();
            */
        }
    }
    

}