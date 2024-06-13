namespace CloudFiles.Troubleshooter;

public abstract class CommandSettings
{
	private readonly OptionParser _optionParser = new();

	public bool Confirm { get; private set; } = false;

	public bool WhatIf { get; private set; } = false;

	public CommandSettings()
	{
		Register(nameof(Confirm), ParseConfirm);
		Register(nameof(WhatIf), ParseWhatIf);
	}

	public void Parse(ReadOnlySpan<string> parse)
	{
		var remaining = _optionParser.Parse(parse);
		ConsumeRemaining(remaining);
	}

	protected void Register(string propertyName, OptionHandler handler)
	{
		ArgumentNullException.ThrowIfNull(handler);
		ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);

		if (!_optionParser.AddOption(propertyName, handler))
		{
			throw new ArgumentException("Key already exists", nameof(propertyName));
		}
	}

	protected virtual void ConsumeRemaining(List<string> args)
	{ }

	private OptionResult ParseConfirm(OptionType optionType, string? value)
	{
		if (optionType != OptionType.Switch)
		{
			// OptionType is KeyValue, thus `--Confirm:<Value>`.
			ArgumentException.ThrowIfNullOrWhiteSpace(value);
			// Let fail parsing of bool, if invalid value provided.
			Confirm = bool.Parse(value);
		}
		else
		{
			Confirm = true;
		}

		return OptionResult.Continue;
	}

	private OptionResult ParseWhatIf(OptionType optionType, string? value)
	{
		if (optionType != OptionType.Switch)
		{
			// OptionType is KeyValue, thus `--WhatIf:<Value>`.
			ArgumentException.ThrowIfNullOrWhiteSpace(value);
			// Let parsing of bool fail if bad value provided.
			WhatIf = bool.Parse(value);
		}
		else
		{
			WhatIf = true;
		}

		return OptionResult.Continue;
	}
}
