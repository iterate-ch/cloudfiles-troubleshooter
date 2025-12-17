using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Security.AccessControl;

namespace System.IO;

internal static class PathInternal
{
	[return: NotNullIfNotNull(nameof(path))]
	public static string? EnsureExtendedPrefixIfNeeded(string? path)
	{
		return LocalUnsafeAccess(null, path);

		[UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "EnsureExtendedPrefixIfNeeded")]
		[return: NotNullIfNotNull(nameof(path))]
		static extern string? LocalUnsafeAccess([UnsafeAccessorType("System.IO.PathInternal, System.Private.CoreLib")] object? typeHint, string? path);
	}

	internal static void SetAccessControl(this FileSystemInfo info, FileSystemSecurity security, AccessControlSections includeSections)
	{
		LocalUnsafeAccess(security, info.FullName, includeSections);

		[UnsafeAccessor(UnsafeAccessorKind.Method, Name = "Persist")]
		static extern void LocalUnsafeAccess(NativeObjectSecurity security, string name, AccessControlSections includeSections);
	}
}
