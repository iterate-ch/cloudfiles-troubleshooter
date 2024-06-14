using Microsoft.Extensions.FileSystemGlobbing;

using Spectre.Console;

using Windows.Storage.Provider;

namespace CloudFiles.Troubleshooter;

internal class CleanCommand
{
	public static int Run(ReadOnlySpan<string> args)
	{
		if (args.Length == 0 || args[0].ToUpperInvariant() is "HELP")
		{
			goto help;
		}

		CleanCommandSettings settings = new();
		settings.Parse(args);
		return Execute(settings);

	help:
		PrintHelp();
		return 0;

		static void PrintHelp()
		{
			WriteLine($$"""
				{{Preamble}}
				
				{{string.Format(UsageFormat, "Clean", "[Include] [Options]")}}
				
				Arguments:
				  Include     Patterns to include when searching for
				               Sync Roots to be removed.
							   Same as -Include

				Options:
				  -Include    Globbing-pattern for sync roots,
				               which should get deleted.
							   Same as Include-argument
				  -WhatIf     Don't run any destructive operation.
				  -Confirm    Prompts before any destructive operation for confirmation.
				""");
		}
	}

	private static int Execute(CleanCommandSettings settings)
	{
		List<StorageProviderSyncRootInfo> syncRoots = [.. StorageProviderSyncRootManager.GetCurrentSyncRoots()];
		syncRoots.RemoveAll(info => !settings.Matcher.Match(info.DisplayNameResource).HasMatches);
		ArgumentOutOfRangeException.ThrowIfZero(syncRoots.Count, "Include");

		WriteLine($$"""
			Sync Roots found:
			- {{string.Join("\n- ", syncRoots.Select(m => $"{m.DisplayNameResource} ({m.Path.Path})"))}}
			""");
		if (settings.Confirm && !Confirm("Continue cleaning Sync Roots from Windows Explorer"))
		{
			return 0;
		}

		foreach (var syncRoot in syncRoots)
		{
			try
			{
				WriteLine($"Unregistering Sync Root {syncRoot.DisplayNameResource} ({syncRoot.Path})");
				if (settings.Confirm && !Confirm("Continue?"))
				{
					continue;
				}
				else if (!settings.WhatIf)
				{
					StorageProviderSyncRootManager.Unregister(syncRoot.Id);
				}

				MarkupLineInterpolated($"{WhatIf(settings.WhatIf, true)}Unregistered Sync Root {syncRoot.DisplayNameResource}");
			}
			catch (Exception e)
			{
				WriteException(new Exception($"Unregistering {syncRoot.DisplayNameResource} ({syncRoot.Id}, {syncRoot.Path.Path})", e));
			}
		}

		return 0;
	}

	private class CleanCommandSettings : CommandSettings
	{
		public Matcher Matcher { get; } = new();

		public CleanCommandSettings()
		{
			Register("-Include", ParseInclude);
		}

		protected override void ConsumeRemaining(List<string> args)
		{
			base.ConsumeRemaining(args);
		}

		private OptionResult ParseInclude(OptionType optionType, string? value)
		{
			if (optionType == OptionType.Switch)
			{
				return OptionResult.NeedMore;
			}

			ArgumentException.ThrowIfNullOrWhiteSpace(value);
			Matcher.AddInclude(value);
			return OptionResult.Continue;
		}
	}
}
