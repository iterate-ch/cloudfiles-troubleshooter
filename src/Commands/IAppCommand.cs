using Spectre.Console;
using Spectre.Console.Cli;

namespace CloudFiles.Troubleshooter.Commands;

internal interface IAppCommand<TSettings> : ICommand<TSettings>
	where TSettings : CommandSettings
{
	ValidationResult Validate(CommandContext context, TSettings settings) => ValidationResult.Success();

	Task<int> ICommand.ExecuteAsync(CommandContext context, CommandSettings settings, CancellationToken cancellationToken) => ExecuteAsync(context, (TSettings)settings, cancellationToken);

	ValidationResult ICommand.Validate(CommandContext context, CommandSettings settings) => Validate(context, (TSettings)settings);
}
