using Spectre.Console.Cli;

namespace CloudFiles.Troubleshooter;

public abstract class AppCommandSettings : CommandSettings
{
	[CommandOption("--confirm")]
	public bool Confirm { get; init; }

	[CommandOption("--what-if")]
	public bool WhatIf { get; init; }
}
