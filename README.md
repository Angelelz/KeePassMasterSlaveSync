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
| `Title`                                                   | Name of the job                                                         | No                                         | `MSS_MobilePhone`           |
| `Password`                                                | The password for the target database                                    | Yes, if `MSS_KeyFilePath` is set  | `SecurePW!`                             |
| `Expires`                                                 | If the entry expires the job is disabled and won't be executed          | `-`                                        | `-`                                     |
| `MSS_KeyFilePath`<br>[string field]           | Path to a key file                                                      | Yes, if `Password` is set                  | `C:\keys\mobile.key`                    |
| `MSS_TargetFilePath`<br>[string field]        | Path to the target database.<br>(Absolute, or relative to source database parent folder.) | No                       | `C:\sync\mobile.kdbx`<br>or<br>`mobile.kdbx`<br>or<br>`..\mobile.kdbx` |
| `MSS_Group`<br>[string field]                 | Group(s) for filtering (`,` to delimit multiple groups - `,` is not allowed in group names)| Yes, if `MSS_Tag` is set          | `MobileGroup`                           |
| `MSS_Tag`<br>[string field]                   | Tag(s) for filtering (`,` to delimit multiple tags - `,` is not allowed in tag names)| Yes, if `MSS_Group` is set        | `MobileSync`                            |
| `MSS_ExportUserAndPassOnly`<br>[string field]    | If `True` Only the Title, Url, Username and Password will be synced with the target Database. | Yes (defaults to `False`) | `True`                             |
| `MSS_PerformSlaveJobs`<br>[string field]    | If true, Sync jobs on target database will be executed too. | Yes (defaults to `True`) | `True`                             |
| `MSS_IsSlave`<br>[string field]    | If `True` this job will be ignored when not executed from a Master database. This option prevents the warning "Missing Password or valid KeyFilePath" to show | Yes (defaults to `False`) | `True`                             |

- Every time the (Master) database is saved, every configured sync job will be executed
- To disable an export job temporarily just set it to expire, it does not matter the time
- If both `MSS_Group` and `MSS_Tag` are set, only entries matching *both* will be exported

![create](https://raw.githubusercontent.com/Angelelz/KeePassMasterSlaveSync/master/KeePassMasterSlaveSync/Capture/CaptureMSS.png)
