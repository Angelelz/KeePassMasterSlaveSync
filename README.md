# KeePassMasterSlaveSync
[![Latest release](https://img.shields.io/github/release/Angelelz/KeePassMasterSlaveSync.svg?label=latest%20release)](https://github.com/Angelelz/KeePassMasterSlaveSync/releases/latest)
[![Github All Releases](https://img.shields.io/github/downloads/Angelelz/KeePassMasterSlaveSync/total.svg)](https://github.com/Angelelz/KeePassMasterSlaveSync/releases)
[![License](https://img.shields.io/github/license/Angelelz/KeePassMasterSlaveSync.svg)](https://github.com/Angelelz/KeePassMasterSlaveSync/blob/master/LICENSE)

KeePassMasterSlaveSync is a KeePass 2 plugin that Allows synchronization of specific Groups or Tags between local databases.
This plugin is heavily based on [KeePassSubsetExport.](https://github.com/lukeIam/KeePassSubsetExport)

## Maintenance status — maintainer wanted

This plugin is currently unmaintained. I no longer use KeePass or have a Windows computer to test it, so I cannot promise fixes, releases, or compatibility with current KeePass versions.

I'm looking for a community maintainer who actively uses KeePass and can help review contributions, test synchronization behavior on Windows, and maintain releases. If you're interested, please join the [maintainer discussion on PR #25](https://github.com/Angelelz/KeePassMasterSlaveSync/pull/25#issuecomment-6385865903). Helping with testing or individual fixes is welcome too.

**There are unresolved reports of unexpected entry deletion, including [issue #16](https://github.com/Angelelz/KeePassMasterSlaveSync/issues/16). Keep separate backups of all databases and test synchronization with disposable copies before using the plugin with important data.**

## Why?
Automatically and securely share entries and groups with other databases. I have my personal database from which I share a group containing bank and family entries with my wife's database.
My Database act as a Master for those entries: If I delete or move any entry, It will be deleted from the Slave; but if there is different data in any entry, the one with the newer modification time will be synced across both databases.
Also, my wife's database (slave Database) can have entries to share too, for which it will be the master.

## Disclaimer
I'm not an expert programmer and I tried not to compromise security - but I can't guarantee it.  
**So use this plugin at your own risk.**  
If you have more experience with KeePass plugins, I would be very grateful if you have a look on the code.

## How to install?
- Download the latest release from [here](https://github.com/Angelelz/KeePassMasterSlaveSync/releases)
- Place KeePassMasterSlaveSync.plgx in the KeePass program directory
- Start KeePass and the plugin is automatically loaded (check the Plugin menu)

## How to use?
- Open the database containing the entries that should be exported/synced
- Create a folder `MSSyncJobs` under the root folder
- For each export job (slave database) create a new entry:

| Setting                                                   | Description                                                             | Optional                                   | Example                                 |
| --------------------------------------------------------- | ----------------------------------------------------------------------- | ------------------------------------------ | --------------------------------------- |
| `Title`                                                   | Name of the job                                                         | No                                         | `MSS_MobilePhone`           |
| `Password`                                                | The password for the target database                                    | Yes, if `MSS_KeyFilePath` is set  | `SecurePW!`                             |
| `Expires`                                                 | If the entry expires the job is disabled and won't be executed          | `-`                                        | `-`                                     |
| `MSS_KeyFilePath`<br>[string field]           | Path to a key file                                                      | Yes, if `Password` is set                  | `C:\keys\mobile.key`                    |
| `MSS_TargetFilePath`<br>[string field]        | Path to the target database.<br>(Absolute, or relative to source database parent folder.) | No                       | `C:\sync\mobile.kdbx`<br>or<br>`mobile.kdbx`<br>or<br>`..\mobile.kdbx` |
| `MSS_Group`<br>[string field]                 | Group(s) for filtering (`,` to delimit multiple groups - `,` is not allowed in group names)| Yes, if `MSS_Tag` is set          | `MobileGroup`                           |
| `MSS_Tag`<br>[string field]                   | Tag(s) for filtering (`,` to delimit multiple tags - `,` is not allowed in tag names)| Yes, if `MSS_Group` is set        | `MobileSync`                            |
| `MSS_ExportUserAndPassOnly`<br>[string field]    | If `True` Only the Title, Url, Username and Password will be synced with the slave Database. | Yes (defaults to `False`) | `True`                             |
| `MSS_PerformSlaveJobs`<br>[string field]    | If true, Sync jobs on slave database will be executed too (Making it the master for those jobs). | Yes (defaults to `True`) | `True`                             |
| `MSS_IsSlave`<br>[string field]    | If `True` this job will be ignored when not executed from a Master database. This option prevents the warning "Missing Password or valid KeyFilePath" to show | Yes (defaults to `False`). `MSS_PerformSlaveJobs` must be `true` | `True`                             |
| `MSS_SyncNewSlaveEntries`<br>[string field]    | If `True`, entries created in the slave database (in the synced groups or tags) are copied to the Master instead of being deleted. Entries deleted in the Master are still deleted from the slave. | Yes (defaults to `False`) | `True`                             |

- Every time the (Master) database is saved, every configured sync job will be executed. The jobs run before the Master is written, so changes taken from the slaves are saved with it; the slave databases are written after the Master.
- To disable an sync job temporarily just set it to expire, it does not matter the time
- If both `MSS_Group` and `MSS_Tag` are set, only entries matching *both* will be exported
- You can have a sync job on a slave database to target the Master database without setting a password or a key file, by executing from the master and setting 'MSS_IsSlave' to true in slave and 'MSS_PerformSlaveJobs' to true on the master.
- The plugin will automatically update the UI of any opened database. Other databases opened in KeePass are changed in memory and marked as modified, save them to keep the changes. The plugin only writes slave databases that are not opened in KeePass, and only if they changed.
- To prevent duplicated Uuids, the plugin will delete any entry from the slave DB that has been moved out of the synced group or tag.
- Groups are matched by their Uuid. If a group was created in both databases independently, the plugin uses the existing group with the same name instead of adding a second one next to it.

![create](https://raw.githubusercontent.com/Angelelz/KeePassMasterSlaveSync/master/KeePassMasterSlaveSync/Capture/CaptureMSS.png)

## Next up!
- You tell me...
