using Trello2GitLab.Conversion;

namespace Trello2GitLab.ConsoleApp;

internal class ConversionProgress : IProgress<ConversionProgressReport>
{
	private static readonly object messageLock = new();

	public void Report(ConversionProgressReport value)
	{
		switch (value.CurrentStep)
		{
			case ConversionStep.Init:
				return;

			case ConversionStep.FetchingTrelloBoard:
				Print("Fetching Trello board...\n");
				return;

			case ConversionStep.TrelloBoardFetched:
				PrintSuccess("Trello board fetched.\n");
				return;

			case ConversionStep.GrantAdminPrivileges when value.TotalElements == null:
				Print("Granting admin privileges...\n");
				return;

			case ConversionStep.GrantAdminPrivileges when value is { TotalElements: not null, Errors: null }:
				Print($"Granting privilege (user {value.CurrentIndex + 1} of {value.TotalElements})\r");
				return;

			case ConversionStep.AdminPrivilegesGranted:
				PrintSuccess("\nAdmin privileges granted.\n");
				return;

			case ConversionStep.FetchMilestones when value.TotalElements == null:
				Print("Fetching project milestones...\n");
				return;

			case ConversionStep.FetchMilestones when value is { TotalElements: not null, Errors: null }:
				Print($"Fetching milestone ({value.CurrentIndex + 1} of {value.TotalElements})\r");
				return;

			case ConversionStep.MilestonesFetched:
				PrintSuccess("\nProject milestones fetched.\n");
				return;

			case ConversionStep.ConvertingCards when value.TotalElements == null:
				Print("Starting cards conversion...\n");
				return;

			case ConversionStep.ConvertingCards when value is { TotalElements: not null, Errors: null }:
				Print($"Converting card ({value.CurrentIndex + 1} of {value.TotalElements})\r");
				return;

			case ConversionStep.CardsConverted:
				PrintSuccess("\nConversion done.\n");
				return;

			case ConversionStep.RevokeAdminPrivileges when value.TotalElements == null:
				Print("Revoking admin privileges...\n");
				return;

			case ConversionStep.RevokeAdminPrivileges when value is { TotalElements: not null, Errors: null }:
				Print($"Revoking privilege (user {value.CurrentIndex + 1} of {value.TotalElements})\r");
				return;

			case ConversionStep.AdminPrivilegesRevoked:
				PrintSuccess("\nAdmin privileges revoked.\n");
				return;

			case ConversionStep.Finished:
				PrintSuccess("\nConversion done.");
				return;

			case ConversionStep.GrantAdminPrivileges when value is { TotalElements: not null, Errors: not null }:
			case ConversionStep.ConvertingCards when value is { TotalElements: not null, Errors: not null }:
			case ConversionStep.RevokeAdminPrivileges when value is { TotalElements: not null, Errors: not null }:
				Print("\n");
				foreach (var error in value.Errors)
				{
					PrintError(error + "\n");
				}
				return;

			case ConversionStep.Custom:
				Print(value.CustomInfo + "\n");
				Print("\n");
				if (value.Errors != null)
				{
					foreach (var error in value.Errors)
					{
						PrintError(error + "\n");
					}
				}
				return;

			default:
				throw new ArgumentOutOfRangeException(nameof(value), value, null);
		}
	}

	private void Print(string message)
	{
		lock (messageLock)
		{
			Console.Out.Write(message);
		}
	}

	private void PrintSuccess(string message)
	{
		lock (messageLock)
		{
			Console.ForegroundColor = ConsoleColor.Cyan;
			Console.Out.Write(message);
			Console.ResetColor();
		}
	}

	private void PrintError(string message)
	{
		lock (messageLock)
		{
			Console.ForegroundColor = ConsoleColor.Red;
			Console.Error.Write(message);
			Console.ResetColor();
		}
	}
}