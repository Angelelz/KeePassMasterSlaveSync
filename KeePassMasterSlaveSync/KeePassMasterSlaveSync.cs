﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using KeePass.Plugins;
using KeePass.Forms;

namespace KeePassMasterSlaveSync
{
    public class KeePassMasterSlaveSyncExt : Plugin
    {
        private IPluginHost m_host = null;

        public override bool Initialize(IPluginHost host)
        {
            if (host == null) return false;
            m_host = host;
            m_host.MainWindow.FileSaved += StartSync;
            return true;
        }

        public override void Terminate()
        {
            if (m_host != null)
            {
                m_host.MainWindow.FileSaved -= StartSync;
            }
        }

        private void StartSync(Object sender, FileSavedEventArgs args)
        {
            Sync.StartSync(args.Database);
        }

    }
}