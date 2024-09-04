using System.Collections.Immutable;

using Microsoft.Extensions.FileSystemGlobbing;

using Spectre.Console;
using Spectre.Console.Cli;

using Windows.Storage.Provider;

namespace CloudFiles.Troubleshooter.Commands;

internal class CleanCommand : IAppCommand<CleanCommand.CleanCommandSettings>
{
	private ImmutableArray<StorageProviderSyncRootInfo> _syncRoots;

	Task<int> ICommand<CleanCommandSettings>.Execute(CommandContext context, CleanCommandSettings settings)
	{
		WriteLine("Sync Roots found:");
		foreach (var syncRoot in _syncRoots)
		{
			Write("- ");
			Write(syncRoot.DisplayNameResource);
			Write(" (");
			Write(syncRoot.Path.Path);
			WriteLine(")");
		}

		if (settings.Confirm && !Confirm("Continue cleaning Sync Roots from Windows Explorer"))
		{
			return Task.FromResult(0);
		}

		foreach (var syncRoot in _syncRoots)
		{
			try
			{
				WriteLine($"Unregistering Sync Root {syncRoot.DisplayNameResource} ({syncRoot.Path.Path})");
				if (settings.Confirm && !Confirm("Continue?"))
				{
					continue;
				}
				else if (!settings.WhatIf)
				{
					StorageProviderSyncRootManager.Unregister(syncRoot.Id);
				}

				MarkupLineInterpolated($"[yellow]{WhatIf(settings.WhatIf)}[/]Unregistered Sync Root {syncRoot.DisplayNameResource}");
			}
			catch (Exception e)
			{
				WriteException(new Exception($"Unregistering {syncRoot.DisplayNameResource} ({syncRoot.Id}, {syncRoot.Path.Path})", e));
			}
		}

		return Task.FromResult(0);
	}

	ValidationResult IAppCommand<CleanCommandSettings>.Validate(CommandContext context, CleanCommandSettings settings)
	{
		Matcher matcher = new();
		foreach (var item in settings.SyncRoots)
		{
			matcher.AddInclude(item);
		}

		_syncRoots = [.. StorageProviderSyncRootManager.GetCurrentSyncRoots()
			.Where(info => matcher.Match(info.DisplayNameResource).HasMatches)];
		return _syncRoots is []
			? ValidationResult.Error("Filtered SyncRoots returned zero elements")
			: ValidationResult.Success();
	}

	private class CleanCommandSettings : AppCommandSettings
	{
		[CommandArgument(0, "<SyncRoots>")]
		public required string[] SyncRoots { get; init; }
	}
}
