using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Security.Principal;

using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Win32;

using Spectre.Console;
using Spectre.Console.Cli;

using Windows.Storage.Provider;

namespace CloudFiles.Troubleshooter.Commands;

internal class CleanCommand : IAppCommand<CleanCommand.CleanCommandSettings>
{
	private ImmutableArray<SyncRootNamespace> _syncRootNamespaces;
	private ImmutableArray<KeyValuePair<string, List<SyncRootInfo>>> _syncRoots;

	Task<int> ICommand<CleanCommandSettings>.Execute(CommandContext context, CleanCommandSettings settings)
	{
		WriteLine("Sync Roots found:");
		foreach (var syncRootNamespace in _syncRootNamespaces)
		{
			Write("- ");
			WriteLine(syncRootNamespace.Id);
			foreach (var syncRoot in syncRootNamespace.SyncRoots)
			{
				Write("  - ");
				Color restore = Foreground;
				if (syncRoot.Remove) Foreground = Color.Green;

				Write(syncRoot.DisplayName);
				Write(" (");
				Write(syncRoot.Id);
				WriteLine(")");
				Write("    ");
				WriteLine(syncRoot.Path);
				Foreground = restore;
			}
		}

		if (settings.Confirm && !Confirm("Continue cleaning Sync Roots from Windows Explorer"))
		{
			return Task.FromResult(0);
		}

		using var syncRootManager = GetSyncRootManagerKey();
		foreach (var syncRootId in _syncRoots)
		{
			var userExclusive = true;
			using var syncRootKey = syncRootManager.OpenSubKey(syncRootId.Key, true);
			foreach (var syncRoot in syncRootId.Value)
			{
				if (!syncRoot.Remove)
				{
					userExclusive = false;
					continue;
				}

				try
				{
					if (settings.WhatIf)
					{
						Markup("[yellow]WhatIf: [/]");
						goto whatIfSyncRootManager;
					}

					StorageProviderSyncRootManager.Unregister(syncRoot.Id);

				whatIfSyncRootManager:
					WriteLine($"Unregistered Sync Root {syncRoot.DisplayName} using SyncRootManager");
				}
				catch (Exception e)
				{
					WriteException(new Exception($"""
						Unregistering {syncRoot.DisplayName} ({syncRoot.Id})"
						  {syncRoot.Path}
						""", e));
				}

				if (settings.WhatIf)
				{
					Markup("[yellow]WhatIf: [/]");
					goto whatIfUserSyncRoot;
				}

				using (var userSyncRootsKey = syncRootKey?.OpenSubKey("UserSyncRoots", true))
				{
					userSyncRootsKey?.DeleteValue(syncRoot.User, false);
				}

			whatIfUserSyncRoot:
				WriteLine($"Unregistered {syncRoot.User} from {syncRoot.Id} User Sync Roots");
			}

			if (!userExclusive)
			{
				MarkupLineInterpolated($"[yellow]Keeping {syncRootId}, registered for multiple users.[/]");
				continue;
			}

			if (settings.WhatIf)
			{
				Markup("[yellow]WhatIf: [/]");
				goto whatIf;
			}

			syncRootKey?.DeleteSubKeyTree("", false);

		whatIf:
			WriteLine($"Unregistered {syncRootId.Key} from Registry");
		}

		using var desktopNamespaceKey = GetUserDesktopNamespaceKey();
		using var userClsIdKey = GetUserCLSIDKey();
		foreach (var syncRootNamespace in _syncRootNamespaces)
		{
			var empty = true;
			foreach (var syncRoot in syncRootNamespace.SyncRoots)
			{
				empty &= !syncRoot.CurrentUser | syncRoot.Remove;
			}

			if (!empty)
			{
				continue;
			}

			RegistryKey? unregisteringKey = null;
			try
			{
				if ((unregisteringKey = desktopNamespaceKey.OpenSubKey(syncRootNamespace.Id, true)) is null)
				{
					goto exit;
				}

				if (settings.WhatIf)
				{
					Markup("[yellow]WhatIf: [/]");
					goto whatIf;
				}

				unregisteringKey.DeleteSubKeyTree("", false);

			whatIf:
				WriteLine($"Unregistered Desktop Namespace {syncRootNamespace.Id}");

			exit:;
			}
			catch (Exception e)
			{
				WriteException(new Exception($"Unregistering Desktop Namespace \"{syncRootNamespace.Id}\"", e));
			}
			finally
			{
				((_, unregisteringKey) = (unregisteringKey, default)).Item1?.Dispose();
			}

			try
			{
				if ((unregisteringKey = userClsIdKey.OpenSubKey(syncRootNamespace.Id, true)) is null)
				{
					goto exit;
				}

				if (settings.WhatIf)
				{
					Markup("[yellow]WhatIf: [/]");
					goto whatIf;
				}

				unregisteringKey.DeleteSubKeyTree("", false);

			whatIf:
				WriteLine($"Unregistered Classes CLSID {syncRootNamespace.Id}");

			exit:;
			}
			catch (Exception e)
			{
				WriteException(new Exception($"Unregistering Classes CLSID  \"{syncRootNamespace.Id}\"", e));
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

		string sid;
		using (var identity = WindowsIdentity.GetCurrent())
		{
			if (identity.User is not { } user)
			{
				return ValidationResult.Error("Cannot determine current user.");
			}

			sid = user.ToString();
		}

		// Collect all system registered sync roots.
		var any = false;
		Dictionary<string, SyncRootNamespace> syncRootNamespaces = [];
		Dictionary<string, List<SyncRootInfo>> syncRoots = [];
		using var key = GetSyncRootManagerKey();
		foreach (var item in key.GetSubKeyNames())
		{
			using var subkey = key.OpenSubKey(item, false);
			if (subkey?.GetValue("NamespaceClsId") is not string namespaceClsId)
			{
				continue;
			}

			var syncRootNamespace = CollectionsMarshal.GetValueRefOrAddDefault(
				syncRootNamespaces, namespaceClsId,
				out _) ??= new(namespaceClsId);

			if (subkey?.GetValue("DisplayNameResource") is not string displayName)
			{
				continue;
			}

			using var userSyncRootsKey = subkey?.OpenSubKey("UserSyncRoots");
			if (userSyncRootsKey?.GetValueNames() is not { } userSyncRoots)
			{
				continue;
			}

			foreach (var userSyncRootKey in userSyncRoots)
			{
				if (userSyncRootsKey?.GetValue(userSyncRootKey) is not string userSyncRoot)
				{
					continue;
				}

				var isCurrentUser = sid.Equals(userSyncRootKey);
				var removing = isCurrentUser && matcher.Match(displayName).HasMatches;
				SyncRootInfo syncRoot = new(item, displayName, userSyncRoot, sid, isCurrentUser, removing);
				syncRootNamespace.SyncRoots.Add(syncRoot);
				(CollectionsMarshal.GetValueRefOrAddDefault(
					syncRoots, item,
					out _) ??= []).Add(syncRoot);
				any |= removing;
			}
		}

		_syncRoots = [.. syncRoots];
		_syncRootNamespaces = [.. syncRootNamespaces.Values];
		return any ? ValidationResult.Success() : ValidationResult.Error("No Sync Root matched specified filter.");
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

	static RegistryKey GetUserDesktopNamespaceKey()
	{
		using Local<RegistryKey> key = Registry.CurrentUser;
		key.Assign(key.Value.OpenSubKey("SOFTWARE", false)!);
		key.Assign(key.Value.OpenSubKey("Microsoft", false)!);
		key.Assign(key.Value.OpenSubKey("Windows", false)!);
		key.Assign(key.Value.OpenSubKey("CurrentVersion", false)!);
		key.Assign(key.Value.OpenSubKey("Explorer", false)!);
		key.Assign(key.Value.OpenSubKey("Desktop", false)!);
		key.Assign(key.Value.OpenSubKey("Namespace", false)!);
		return ((_, key.Value) = (key.Value, default!)).Item1;
	}

	private class CleanCommandSettings : AppCommandSettings
	{
		[CommandArgument(0, "<SyncRoots>")]
		public required string[] SyncRoots { get; init; }
	}

	private record class SyncRootNamespace(string Id)
	{
		public List<SyncRootInfo> SyncRoots { get; } = [];
	}

	private record class SyncRootInfo(string Id, string DisplayName, string Path, string User, bool CurrentUser, bool Remove);

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
