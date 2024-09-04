using Spectre.Console;
using Spectre.Console.Cli;

namespace CloudFiles.Troubleshooter.Commands;

internal interface IAppCommand<TSettings> : ICommand<TSettings>
	where TSettings : CommandSettings
{
	ValidationResult Validate(CommandContext context, TSettings settings) => ValidationResult.Success();

	Task<int> ICommand.Execute(CommandContext context, CommandSettings settings) => Execute(context, (TSettings)settings);

	ValidationResult ICommand.Validate(CommandContext context, CommandSettings settings) => Validate(context, (TSettings)settings);
}
