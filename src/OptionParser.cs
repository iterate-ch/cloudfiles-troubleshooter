using System.Buffers;

namespace CloudFiles.Troubleshooter
{
	internal class OptionParser
	{
		private static readonly SearchValues<char> ValueSeparators = SearchValues.Create([':', '=']);
		private readonly Dictionary<string, OptionHandler> _handler = new(StringComparer.OrdinalIgnoreCase);

		public bool AddOption(string name, OptionHandler handler)
		{
			return _handler.TryAdd(name, handler);
		}

		public List<string> Parse(ReadOnlySpan<string> args)
		{
			List<string> remaining = [];

			string lastKey = default!;
			OptionHandler? optionHandler = default;
			var type = OptionType.Argument;
			foreach (ref readonly var option in args)
			{
				if (type == OptionType.Switch)
				{
					type = OptionType.KeyValue;
					if (optionHandler!(OptionType.KeyValue, option) != OptionResult.Continue)
					{
						throw new ArgumentException($"Bad value \"{option}\" supplied", lastKey);
					}
				}
				else if (option.StartsWith('-'))
				{
					type = OptionType.Switch;
					var optionExpression = option.AsSpan().TrimStart('-');
					var valueSeparator = optionExpression.IndexOfAny(ValueSeparators);
					string? value = null;
					if (valueSeparator != -1)
					{
						type = OptionType.KeyValue;
						value = optionExpression[(valueSeparator + 1)..].ToString();
						optionExpression = optionExpression[..valueSeparator];
					}

					lastKey = optionExpression.ToString();
					if ((optionHandler = FindOptionHandler(lastKey)) is null)
					{
						throw new ArgumentException("Unknown option", lastKey);
					}
					else if (optionHandler(type, value) != OptionResult.NeedMore)
					{
						type = OptionType.Argument;
					}
				}
				else
				{
					remaining.Add(option);
				}
			}

			return remaining;
		}

		private OptionHandler? FindOptionHandler(string key)
		{
			return _handler.TryGetValue(key, out var handler)
				? handler
				: FindSlow(key);

			OptionHandler? FindSlow(ReadOnlySpan<char> key)
			{
				List<KeyValuePair<string, OptionHandler>> keys = [.. _handler];
				for (int i = 0; i < key.Length; i++)
				{
					ref readonly char symbol = ref key[i];
					for (int j = keys.Count - 1; j >= 0; j--)
					{
						var handler = keys[j];
						// remove all keys, that are smaller than input key
						// and remove all keys that don't match ignore-case
						if (handler.Key.Length < key.Length || (char.ToUpperInvariant(symbol) != char.ToUpperInvariant(handler.Key[i])))
						{
							keys.RemoveAt(j);
						}
						else if (keys.Count == 1)
						{
							return handler.Value;
						}
					}
				}

				return null;
			}
		}
	}

	public delegate OptionResult OptionHandler(OptionType optionType, string? value);

	public enum OptionResult
	{
		Continue,
		NeedMore,
		Unknown
	}

	public enum OptionType
	{
		Argument,
		Switch,
		KeyValue
	}
}
