using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Security.Principal;

using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Win32;

using Spectre.Console;
using Spectre.Console.Cli;

using Windows.Storage.Provider;

namespace CloudFiles.Troubleshooter.Commands;

internal class CleanCommand : IAppCommand<CleanCommand.CleanCommandSettings>
{
	private ImmutableArray<SyncRootInfo> _syncRoots;

	Task<int> ICommand<CleanCommandSettings>.Execute(CommandContext context, CleanCommandSettings settings)
	{
		WriteLine("Sync Roots found:");
		foreach (var syncRoot in _syncRoots)
		{
			Write("- ");
			Write(syncRoot.DisplayName);
			Write(" (");
			Write(syncRoot.Path);
			WriteLine(")");
		}

		if (settings.Confirm && !Confirm("Continue cleaning Sync Roots from Windows Explorer"))
		{
			return Task.FromResult(0);
		}

		using var userClsId = GetUserCLSIDKey();
		using var syncRootManager = GetSyncRootManagerKey();
		foreach (var syncRoot in _syncRoots)
		{
			try
			{
				WriteLine($"Unregistering Sync Root {syncRoot.DisplayName} ({syncRoot.Path})");
				if (settings.Confirm && !Confirm("Continue?"))
				{
					continue;
				}
				else if (!settings.WhatIf)
				{
					StorageProviderSyncRootManager.Unregister(syncRoot.Id);
				}

				MarkupLineInterpolated($"[yellow]{WhatIf(settings.WhatIf)}[/]Unregistered Sync Root {syncRoot.DisplayName}");
			}
			catch (Exception e)
			{
				WriteException(new Exception($"Unregistering {syncRoot.DisplayName} ({syncRoot.Id}, {syncRoot.Path})", e));
			}

			RegistryKey? unregisteringKey = null;
			try
			{
				if ((unregisteringKey = userClsId.OpenSubKey(syncRoot.NamespaceClsId!, true)) is null)
				{
					goto exit;
				}

				if (settings.Confirm && !Confirm("Continue"))
				{
					continue;
				}
				else if (!settings.WhatIf)
				{
					unregisteringKey.DeleteSubKeyTree("", false);
				}

				MarkupLineInterpolated($"[yellow]{WhatIf(settings.WhatIf)}[/]Unregistered Explorer Namespace Class {syncRoot.DisplayName}");

			exit:;
			}
			catch (Exception e)
			{
				WriteException(new Exception($"Deleting Explorer Namespace Class \"{unregisteringKey?.Name ?? syncRoot.NamespaceClsId}\"", e));
			}
			finally
			{
				((_, unregisteringKey) = (unregisteringKey, default)).Item1?.Dispose();
			}

			try
			{
				if ((unregisteringKey = syncRootManager.OpenSubKey(syncRoot.Id, true)) is null)
				{
					goto exit;
				}

				if (settings.Confirm && !Confirm("Continue"))
				{
					continue;
				}
				else if (!settings.WhatIf)
				{
					unregisteringKey.DeleteSubKeyTree("", false);
				}

				MarkupLineInterpolated($"[yellow]{WhatIf(settings.WhatIf)}[/]Unregistered Explorer Sync Root {syncRoot.DisplayName}");

			exit:;
			}
			catch (Exception e)
			{
				WriteException(new Exception($"Deleting Explorer Sync Root Registry \"{unregisteringKey?.Name ?? syncRoot.NamespaceClsId}\"", e));
			}
			finally
			{
				((_, unregisteringKey) = (unregisteringKey, default)).Item1?.Dispose();
			}
		}

		return Task.FromResult(0);

		static RegistryKey GetUserCLSIDKey()
		{
			using Local<RegistryKey> key = Registry.CurrentUser;
			key.Assign(key.Value.OpenSubKey("SOFTWARE", false)!);
			key.Assign(key.Value.OpenSubKey("Classes", false)!);
			key.Assign(key.Value.OpenSubKey("CLSID", false)!);
			return ((_, key.Value) = (key.Value, default!)).Item1;
		}
	}

	ValidationResult IAppCommand<CleanCommandSettings>.Validate(CommandContext context, CleanCommandSettings settings)
	{
		Matcher matcher = new();
		foreach (var item in settings.SyncRoots)
		{
			matcher.AddInclude(item);
		}

		Dictionary<string, SyncRootInfo> syncRoots = [];
		foreach (var item in StorageProviderSyncRootManager.GetCurrentSyncRoots())
		{
			if (matcher.Match(item.DisplayNameResource).HasMatches)
			{
				syncRoots[item.Id] = new(item.Id, item.DisplayNameResource, item.Path.Path);
			}
		}

		string sid;
		using (var identity = WindowsIdentity.GetCurrent())
		{
			if (identity.User is not { } user)
			{
				return ValidationResult.Error("Cannot determine current user.");
			}

			sid = user.ToString();
		}

		using var key = GetSyncRootManagerKey();
		foreach (var item in key.GetSubKeyNames())
		{
			using var subkey = key.OpenSubKey(item, false);
			if (subkey?.GetValue("NamespaceClsId") is not string namespaceClsId)
			{
				continue;
			}

			if (syncRoots.TryGetValue(item, out var syncRootInfo))
			{
				syncRootInfo.NamespaceClsId = namespaceClsId;
			}
			else if (
				subkey?.GetValue("DisplayNameResource") is string displayName
				&& matcher.Match(displayName).HasMatches)
			{
				using var userSyncRoots = subkey?.OpenSubKey("UserSyncRoots");
				var values = userSyncRoots?.GetValueNames() ?? [];
				if (Array.FindIndex(values, sid.Equals) == -1)
				{
					continue;
				}

				if (values.Length > 1)
				{
					continue;
				}

				if (userSyncRoots?.GetValue(sid) is not string path)
				{
					continue;
				}

				syncRoots[item] = new(item, displayName, path)
				{
					NamespaceClsId = namespaceClsId
				};
			}

		}

		_syncRoots = [.. syncRoots.Values];
		return _syncRoots is []
			? ValidationResult.Error("Filtered SyncRoots returned zero elements")
			: ValidationResult.Success();
	}

	private static RegistryKey GetSyncRootManagerKey()
	{
		using Local<RegistryKey> key = Registry.LocalMachine;
		key.Assign(key.Value.OpenSubKey("SOFTWARE", false)!);
		key.Assign(key.Value.OpenSubKey("Microsoft", false)!);
		key.Assign(key.Value.OpenSubKey("Windows", false)!);
		key.Assign(key.Value.OpenSubKey("CurrentVersion", false)!);
		key.Assign(key.Value.OpenSubKey("Explorer", false)!);
		key.Assign(key.Value.OpenSubKey("SyncRootManager", false)!);
		return ((_, key.Value) = (key.Value, default!)).Item1;
	}

	private class CleanCommandSettings : AppCommandSettings
	{
		[CommandArgument(0, "<SyncRoots>")]
		public required string[] SyncRoots { get; init; }
	}

	private record class SyncRootInfo(string Id, string DisplayName, string Path)
	{
		[MaybeNull]
		public string NamespaceClsId { get; set; }
	}

	private ref struct Local<T>()
	{
		private readonly object _gate = new();
		[AllowNull]
		private T _value;

		[UnscopedRef]
		public ref T Value => ref _value;

		public void Assign(T value) => SwapDispose(ref _value, value, _gate);

		public void Dispose() => SwapDispose(ref _value, default, _gate);

		public static implicit operator Local<T>(in T value) => new() { Value = value };

		private static void DisposeSafe(in T? value)
		{
			if (value is IDisposable disposable)
			{
				disposable.Dispose();
			}
		}

		private static void SwapDispose(ref T? field, [AllowNull] T value, object gate)
		{
			T? temp;
			lock (gate)
			{
				(temp, field) = (field, value);
			}

			DisposeSafe(temp);
		}
	}
}
