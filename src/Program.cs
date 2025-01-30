using System.Diagnostics.CodeAnalysis;

using CloudFiles.Troubleshooter.Commands;

using Spectre.Console;
using Spectre.Console.Cli;
using Spectre.Console.Cli.Help;
using Spectre.Console.Rendering;

internal class Program
{
	[DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(CleanCommand))]
	[DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(PruneCommand))]
	private static int Main(string[] args)
	{
		CommandApp app = new();
		app.Configure(config =>
		{
			config.SetHelpProvider(new AppHelpProvider(config.Settings));
			config.AddCommand<CleanCommand>("clean")
				.WithDescription("Cleans Explorer-registered SyncRoots from the system.");
			config.AddCommand<PruneCommand>("prune")
				.WithDescription("Cleans a cloudfiles sync root from the filesystem");
#if DEBUG
			config.PropagateExceptions();
#endif
		});
		return app.Run(args);
	}

	private class AppHelpProvider(ICommandAppSettings settings) : HelpProvider(settings)
	{
		public override IEnumerable<IRenderable> GetHeader(ICommandModel model, ICommandInfo? command)
		{
			return [
				new Text($"""
					Copyright (C) {DateTime.Now.Year}  iterate GmbH
					This program comes with ABSOLUTELY NO WARRANTY
					This is free software, and you are welcome to redistribute it under certain conditions
					"""), Text.NewLine,
				Text.NewLine];
		}
	}
}
