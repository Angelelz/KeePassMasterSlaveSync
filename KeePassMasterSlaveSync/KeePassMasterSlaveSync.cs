using System;
using System.Drawing;
using KeePass.Plugins;
using KeePass.Forms;

namespace KeePassMasterSlaveSync
{
    public class KeePassMasterSlaveSyncExt : Plugin
    {
        private IPluginHost m_host = null;

        public override Image SmallIcon
        {
            get { return Resources.Icon16x16; }
        }

        public override string UpdateUrl
        {
            get { return "https://github.com/Angelelz/KeePassMasterSlaveSync/raw/master/keepass.version"; }
        }

        public override bool Initialize(IPluginHost host)
        {
            if (host == null) return false;
            m_host = host;
            m_host.MainWindow.FileSaving += SyncBeforeSave;
            m_host.MainWindow.FileSaved += SyncAfterSave;
            return true;
        }

        public override void Terminate()
        {
            if (m_host != null)
            {
                m_host.MainWindow.FileSaving -= SyncBeforeSave;
                m_host.MainWindow.FileSaved -= SyncAfterSave;
            }
        }

        private void SyncBeforeSave(Object sender, FileSavingEventArgs args)
        {
            Sync.SyncBeforeSave(args.Database, args.EventGuid, args.Copy);
        }

        private void SyncAfterSave(Object sender, FileSavedEventArgs args)
        {
            Sync.SyncAfterSave(args.Database, args.EventGuid, args.Success);
        }

    }
}
