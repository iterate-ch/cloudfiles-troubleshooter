using CloudFiles.Troubleshooter;

if (args.Length == 0)
{
	goto help;
}

if (FindCommand(args) is { } handler)
{
	return handler(args.AsSpan(1));
}

help:
PrintHelp();
return 0;

static void PrintHelp()
{
	WriteLine($$"""
		{{Preamble}}

		{{string.Format(UsageFormat, "<Command>", "[Options]")}}

		Commands:
		  clean       Cleans the Windows Shell Namespace from stale sync roots.
		  prune       Nukes a broken sync root from the filesystem.

			No, or any other unsupported command will print this Help.
		""");
}

static CommandHandler? FindCommand(ReadOnlySpan<string> args)
{
	return args[0].ToUpperInvariant() switch
	{
		"CLEAN" => CleanCommand.Run,
		"PRUNE" => PruneCommand.Run,
		_ => null
	};
}
