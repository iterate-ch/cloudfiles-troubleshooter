using System.Collections.Immutable;
using System.IO.Enumeration;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using Microsoft.Extensions.FileSystemGlobbing;

using Spectre.Console;
using Spectre.Console.Cli;

using Windows.Storage;
using Windows.Storage.Provider;
using Windows.Win32.Storage.CloudFilters;
using Windows.Win32.Storage.FileSystem;

namespace CloudFiles.Troubleshooter.Commands;

internal class PruneCommand : IAppCommand<PruneCommand.PruneCommandSettings>
{
	private ImmutableArray<DirectoryInfo> _syncRoots;

	unsafe Task<int> ICommand<PruneCommandSettings>.Execute(CommandContext context, PruneCommandSettings settings)
	{
		WriteLine("Unregistering sync roots:");
		foreach (var item in _syncRoots)
		{
			Write("- ");
			WriteLine(item.FullName);
		}

		if (settings.Confirm && !Confirm("Continue unregistering Sync Roots from Filesystem"))
		{
			return Task.FromResult(0);
		}

		foreach (var path in _syncRoots)
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
				WriteException(new Exception($"CloudFiles connect attempt to \"{path.FullName}\" failed", Marshal.GetExceptionForHR(connectErrorCode)));
				continue;
			}
			else try
				{
					using var syncRootHandle = CreateFile(
						lpFileName: PathInternal.EnsureExtendedPrefixIfNeeded(path.FullName),
						dwDesiredAccess: (uint)FILE_ACCESS_RIGHTS.FILE_WRITE_EA,
						dwShareMode: (FILE_SHARE_MODE)7,
						lpSecurityAttributes: null,
						dwCreationDisposition: FILE_CREATION_DISPOSITION.OPEN_EXISTING,
						dwFlagsAndAttributes: FILE_FLAGS_AND_ATTRIBUTES.FILE_FLAG_BACKUP_SEMANTICS,
						hTemplateFile: null);
					// Force mark SyncRoot in Sync & disable on demand population
					// In case a previous placeholder fetch didn't succeed successfully.
					MarkupLineInterpolated($"[yellow]{WhatIf(settings.WhatIf)}[/]Marking Sync Root \"{path.FullName}\" as InSync and disabling OnDemand population.");
					if (settings.Confirm && !Confirm("Continue changing Sync Root Flags", false))
					{
						continue;
					}
					else if (!settings.WhatIf)
					{
						CfUpdatePlaceholder(
							syncRootHandle,
							null,
							null,
							0,
							default,
							CF_UPDATE_FLAGS.CF_UPDATE_FLAG_MARK_IN_SYNC | CF_UPDATE_FLAGS.CF_UPDATE_FLAG_DISABLE_ON_DEMAND_POPULATION,
							null, null);
					}
				}
				finally
				{
					CfDisconnectSyncRoot(key);
				}

			MarkupLineInterpolated($"[yellow]{WhatIf(settings.WhatIf)}[/]Unregistering \"{path.FullName}\"");
			try
			{
				var storageFolder = StorageFolder.GetFolderFromPathAsync(path.FullName).GetAwaiter().GetResult();
				if (StorageProviderSyncRootManager.GetSyncRootInformationForFolder(storageFolder) is { } syncRootInfo)
				{
					MarkupLineInterpolated($"[yellow]{WhatIf(settings.WhatIf)}[/]Unregistering Sync Root \"{syncRootInfo.DisplayNameResource}\" from Storage Provider Sync Root Manager (Explorer Shell)");
					if (settings.Confirm && !Confirm("Continue removing Shell Namespace", false))
					{
						continue;
					}
					else if (!settings.WhatIf)
					{
						StorageProviderSyncRootManager.Unregister(syncRootInfo.Id);
					}
				}
			}
			catch
			{
				/* 
				 * Not registered with StorageProviderSyncRootManager
				 * Silently ignore.
				 */
			}

			MarkupLineInterpolated($"[yellow]{WhatIf(settings.WhatIf)}[/]Unregistering Sync Root \"{path.FullName}\" from Filesystem");
			if (settings.Confirm && !Confirm("Continue unregistering from filesystem", false))
			{
				continue;
			}
			else if (!settings.WhatIf)
			{
				CfUnregisterSyncRoot(path.FullName);
			}
		}

		return Task.FromResult(0);
	}

	ValidationResult IAppCommand<PruneCommandSettings>.Validate(CommandContext context, PruneCommandSettings settings)
	{
		Matcher? matcher = null;
		if (settings.Recurse && settings.Include.Length > 0)
		{
			matcher = new();
			matcher.AddIncludePatterns(settings.Include);
		}

		var syncRootBuilder = ImmutableArray.CreateBuilder<DirectoryInfo>();
		EnumerationOptions syncRootTraversalOptions = new()
		{
			RecurseSubdirectories = settings.Recurse,
		};
		foreach (var item in settings.SyncRoots)
		{
			syncRootBuilder.AddRange(
				new FileSystemEnumerable<DirectoryInfo>(item, Transform, syncRootTraversalOptions)
				{
					ShouldIncludePredicate = ShouldInclude,
					ShouldRecursePredicate = ShoudRecurse,
				});
		}

		return (_syncRoots = syncRootBuilder.DrainToImmutable()) is []
			? ValidationResult.Error("No sync roots specified")
			: ValidationResult.Success();

		static DirectoryInfo Transform(ref FileSystemEntry entry) => (DirectoryInfo)entry.ToFileSystemInfo();

		bool ShouldInclude(ref FileSystemEntry entry)
		{
			if (!entry.IsDirectory)
			{
				return false;
			}

			if (!(matcher?.Match(entry.RootDirectory.ToString(), entry.ToFullPath()).HasMatches ?? true))
			{
				return false;
			}

			return IsSyncRoot(ref entry);
		}

		bool ShoudRecurse(ref FileSystemEntry entry)
		{
			return !IsSyncRoot(ref entry);
		}

		[SkipLocalsInit]
		static unsafe bool IsSyncRoot(ref FileSystemEntry entry)
		{
			CF_SYNC_ROOT_BASIC_INFO info;
			return CfGetSyncRootInfoByPath(
				FilePath: entry.ToFullPath(),
				InfoClass: CF_SYNC_ROOT_INFO_CLASS.CF_SYNC_ROOT_INFO_BASIC,
				InfoBuffer: &info,
				InfoBufferLength: (uint)Marshal.SizeOf<CF_SYNC_ROOT_BASIC_INFO>(),
				ReturnedLength: null).Succeeded;
		}
	}

	private class PruneCommandSettings : AppCommandSettings
	{
		[CommandArgument(0, "<SyncRoots>")]
		public required string[] SyncRoots { get; init; } = [];

		[CommandOption("-i|--include <VALUES>")]
		public required string[] Include { get; init; } = [];

		[CommandOption("--recurse")]
		public bool Recurse { get; init; }

		public override ValidationResult Validate()
		{
			return Recurse || Include.Length == 0
				? base.Validate()
				: ValidationResult.Error("Include-filter cannot be used without Recurse.");
		}
	}
}
