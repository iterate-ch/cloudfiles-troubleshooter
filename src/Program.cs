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
		cloudfiles-troubleshooter  Copyright (C) 2024  iterate GmbH
			This program comes with ABSOLUTELY NO WARRANTY
			This is free software, and you are welcome to redistribute it
			under certain conditions

		{{string.Format(UsageFormat, "<Command>", "[Options]")}}

		Commands:
			prune		Nukes a broken sync root from the filesystem.

			No, or any other unsupported command will print this Help.
		""");
}

static CommandHandler? FindCommand(ReadOnlySpan<string> args)
{
	return args[0].ToUpperInvariant() switch
	{
		"PRUNE" => PruneCommand.Run,
		_ => null
	};
}
