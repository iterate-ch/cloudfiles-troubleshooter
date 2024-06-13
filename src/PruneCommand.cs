using System.Runtime.InteropServices;

using Windows.Storage;
using Windows.Storage.Provider;
using Windows.Win32.Storage.CloudFilters;
using Windows.Win32.Storage.FileSystem;

namespace CloudFiles.Troubleshooter;

internal class PruneCommand
{
	public static int Run(ReadOnlySpan<string> args)
	{
		if (args.Length == 0 || args[0].ToUpperInvariant() is "HELP")
		{
			goto help;
		}

		PruneCommandSettings settings = new();
		settings.Parse(args);
		return Execute(settings);

	help:
		PrintHelp();
		return 0;

		static void PrintHelp()
		{
			WriteLine($$"""
				cloudfiles-troubleshooter  Copyright (C) 2024  iterate GmbH
					This program comes with ABSOLUTELY NO WARRANTY
					This is free software, and you are welcome to redistribute it
					under certain conditions
				
				{{string.Format(UsageFormat, "Prune", "[SyncRoots] [Options]")}}
				
				Arguments:
					SyncRoots	Paths to Sync Roots to be pruned, delimited by Spaces
								Same as --SyncRoot
				Options:
					--SyncRoot	Adds a Sync Root for pruning,
								specify multiple times for multiple sync roots.
								Same as SyncRoots-argument.
					--WhatIf	Don't run any destructive operation.
					--Confirm	Prompts before any destructive operation for confirmation.
				""");
		}
	}

	private static unsafe int Execute(PruneCommandSettings settings)
	{
		ArgumentOutOfRangeException.ThrowIfZero(settings.SyncRoots.Count);
		if (!settings.SyncRoots.Any(v => v.Exists))
		{
			throw new ArgumentException("No existing SyncRoot found");
		}

		foreach (var path in settings.SyncRoots)
		{
			if (!path.Exists)
			{
				WriteLine($"Skipping non-existent \"{path.FullName}\"");
				continue;
			}

			CF_CONNECTION_KEY key;
			if (CfConnectSyncRoot(path.FullName, NoneCallbackRegistration, null, CF_CONNECT_FLAGS.CF_CONNECT_FLAG_NONE, &key) is
				{
					Failed: true,
					Value: { } connectErrorCode
				})
			{
				WriteException(new Exception("CloudFiles connect attempt to \"{path.FullName}\" failed", Marshal.GetExceptionForHR(connectErrorCode)));
				continue;
			}
			else using (var syncRootHandle = CreateFile(
				lpFileName: path.FullName,
				dwDesiredAccess: (uint)FILE_ACCESS_RIGHTS.FILE_WRITE_EA,
				dwShareMode: (FILE_SHARE_MODE)7,
				lpSecurityAttributes: null,
				dwCreationDisposition: FILE_CREATION_DISPOSITION.OPEN_EXISTING,
				dwFlagsAndAttributes: FILE_FLAGS_AND_ATTRIBUTES.FILE_FLAG_BACKUP_SEMANTICS,
				hTemplateFile: null))
				{
					// Force mark SyncRoot in Sync & disable on demand population
					// In case a previous placeholder fetch didn't succeed successfully.
					CfUpdatePlaceholder(
						syncRootHandle,
						null,
						null,
						0,
						default,
						CF_UPDATE_FLAGS.CF_UPDATE_FLAG_MARK_IN_SYNC | CF_UPDATE_FLAGS.CF_UPDATE_FLAG_DISABLE_ON_DEMAND_POPULATION,
						null, null);
				}

			CfDisconnectSyncRoot(key);

			WriteLine($"Unregistering \"{path.FullName}\"");
			try
			{
				var storageFolder = StorageFolder.GetFolderFromPathAsync(path.FullName).GetAwaiter().GetResult();
				if (StorageProviderSyncRootManager.GetSyncRootInformationForFolder(storageFolder) is { } syncRootInfo)
				{
					StorageProviderSyncRootManager.Unregister(syncRootInfo.Id);
				}
			}
			catch
			{ /* 
               * Not registered with StorageProviderSyncRootManager
               * Silently ignore.
               */
			}

			CfUnregisterSyncRoot(path.FullName);
		}

		return 0;
	}

	public class PruneCommandSettings : CommandSettings
	{
		public List<DirectoryInfo> SyncRoots { get; } = [];

		public PruneCommandSettings()
		{
			Register(nameof(SyncRoots), ParseSyncRoot);
		}

		protected override void ConsumeRemaining(List<string> args)
		{
			args.ForEach(AddSyncRoot);
		}

		private OptionResult ParseSyncRoot(OptionType type, string? value)
		{
			if (type == OptionType.Switch)
			{
				return OptionResult.NeedMore;
			}

			ArgumentException.ThrowIfNullOrWhiteSpace(value);
			AddSyncRoot(value);
			return OptionResult.Continue;
		}

		private void AddSyncRoot(string value)
		{
			SyncRoots.Add(new(value));
		}
	}
}
