# KeePassMasterSlaveSync
KeePassMasterSlaveSync is a KeePass 2 plugin that Allows synchronization of specific Groups or Tags between local databases.
This plugin is heavily based on [KeePassSubsetExport.](https://github.com/lukeIam/KeePassSubsetExport)

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
| `Title`                                                   | Name of the job                                                         | No                                         | `MasterSlaveSync_MobilePhone`           |
| `Password`                                                | The password for the target database                                    | Yes, if `MasterSlaveSync_KeyFilePath` is set  | `SecurePW!`                             |
| `Expires`                                                 | If the entry expires the job is disabled and won't be executed          | `-`                                        | `-`                                     |
| `MasterSlaveSync_KeyFilePath`<br>[string field]           | Path to a key file                                                      | Yes, if `Password` is set                  | `C:\keys\mobile.key`                    |
| `MasterSlaveSync_TargetFilePath`<br>[string field]        | Path to the target database.<br>(Absolute, or relative to source database parent folder.) | No                       | `C:\sync\mobile.kdbx`<br>or<br>`mobile.kdbx`<br>or<br>`..\mobile.kdbx` |
| `MasterSlaveSync_Group`<br>[string field]                 | Group(s) for filtering (`,` to delimit multiple groups - `,` is not allowed in group names)| Yes, if `MasterSlaveSync_Tag` is set          | `MobileGroup`                           |
| `MasterSlaveSync_Tag`<br>[string field]                   | Tag(s) for filtering (`,` to delimit multiple tags - `,` is not allowed in tag names)| Yes, if `MasterSlaveSync_Group` is set        | `MobileSync`                            |
| `MasterSlaveSync_ExportUserAndPassOnly`<br>[string field]    | If `True` Only the Title, Url, Username and Password will be synced with the target Database. | Yes (defaults to `False`) | `True`                             |
| `MasterSlaveSync_PerformSlaveJobs`<br>[string field]    | If true, Sync jobs on target database will be executed too. | Yes (defaults to `True`) | `True`                             |

- Every time the (Master) database is saved, every configured sync job will be executed
- To disable an export job temporarily just set it to expire, it does not matter the time
- If both `MasterSlaveSync_Group` and `MasterSlaveSync_Tag` are set, only entries matching *both* will be exported

![create](https://raw.githubusercontent.com/Angelelz/KeePassMasterSlaveSync/master/KeePassMasterSlaveSync/Capture/CaptureMSS.png)
