using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;

namespace System.IO;

internal static class PathInternal
{
	private static Func<string?, string?>? s_ensureExtendedPrefixIfNeeded;

	[return: NotNullIfNotNull(nameof(path))]
	public static string? EnsureExtendedPrefixIfNeeded(string? path)
	{
		s_ensureExtendedPrefixIfNeeded ??= MakeAccess();
		return s_ensureExtendedPrefixIfNeeded(path);

		static Func<string?, string?> MakeAccess()
		{
			var type = Type.GetType("System.IO.PathInternal, System.Private.CoreLib");
			ArgumentNullException.ThrowIfNull(type);
			var method = type.GetMethod(nameof(EnsureExtendedPrefixIfNeeded), BindingFlags.Static | BindingFlags.NonPublic, [typeof(string)]);
			ArgumentNullException.ThrowIfNull(method);

			var pathParameter = Expression.Parameter(typeof(string));
			var call = Expression.Call(method, pathParameter);
			return Expression.Lambda<Func<string?, string?>>(call, pathParameter).Compile();
		}
	}
}
