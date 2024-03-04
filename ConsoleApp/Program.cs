namespace Trello2GitLab.ConsoleApp;

using Conversion;

internal enum ExitCode
{
    InvalidArguments = -1,
    Success = 0,
    OptionsError = 1,
    ConversionError = 2,
}

public static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args[0] == "-h" || args[0] == "--help")
        {
            ShowHelp();
            return (int)ExitCode.Success;
        }
        else
        {
            var optionsFilePath = args[0];
            if (!TryGetConverterOptions(optionsFilePath, out var options) || options == null)
            {
                await Console.Error.WriteLineAsync($"The options file cannot be located at: {optionsFilePath}");
                return (int)ExitCode.OptionsError;
            }

            // Auch per Befehlszeile konfigurierbar.
            switch (args)
            {
	            case [_, "--import"]:
	            case [_, "--all"]:
		            options.Global.Action = ConverterAction.All;
		            break;

	            case [_, "--delete", ..]:
	            {
		            options.Global.Action = ConverterAction.DeleteIssues;
		            if (args.Length > 2) options.Global.DeleteIfGreaterThanIssueId = int.Parse(args[2]);
		            break;
	            }

	            case [_, "--adjustmentions"]:
                    options.Global.Action = ConverterAction.AdjustMentions;
                    break;

	            case [_, "--movecustomfields"]:
                    options.Global.Action = ConverterAction.MoveCustomFields;
                    break;

	            case [_, "--associtate"]:
                    options.Global.Action = ConverterAction.AssocitateWithTrello;
                    break;
            }

            // Execute the desired action.
            switch (options.Global.Action)
            {
                case ConverterAction.All:
                    return (int)await RunConversion(options);

                case ConverterAction.AdjustMentions:
                    return (int)await RunAdjustMentions(options);

                case ConverterAction.MoveCustomFields:
                    return (int)await RunMoveCustomFields(options);

                case ConverterAction.DeleteIssues:
                    return (int)await RunDeletion(options);

                case ConverterAction.AssocitateWithTrello:
                    return (int)await RunAssocitateWithTrello(options);

                default:
                    await Console.Error.WriteAsync("Invalid arguments supplied.\nUse -h option to see help.");
                    return (int)ExitCode.InvalidArguments;
            }
        }
    }

    private static void ShowHelp()
    {
        Console.Write(
			"""
                trello2gitlab
                Convert Trello cards to GitLab issues.

                Usage:
                  trello2gitlab path/to/options.json
                  trello2gitlab [-h|--help]

                Options:
                  -h|--help    Show this screen.

                Options file format:
                  {
                      "global": {
                          "action": "<Action (string) to perform on mentions ("All", "AdjustMentions", "DeleteIssues", "MoveCustomFields") [default: "All"]>", 
                          "deleteIfGreaterThanIssueId": <Issue ID (int) [default: 0]>
                      },
                      "trello": {
                          "key": <Trello API key (string)>,
                          "token": <Trello API token (string)>,
                          "boardId": <Trello board ID (string)>,
                          "include": <Specifies which cards to include ("all"|"open"|"visible"|"closed") [default: "all"]>
                      },
                      "gitlab": {
                          "url": <GitLab server base URL (string) [default: "https://gitlab.com"]>,
                          "token": <GitLab private access token (string)>,
                          "sudo": <Tells if the private token has sudo rights (bool)>,
                          "projectId": <GitLab target project ID (int)>
                      },
                      "associations": {
                          "labels_labels": {
                              <Trello label ID (string)>: <GitLab label name (string)>
                          },
                          "lists_labels": {
                              <Trello list ID (string)>: <GitLab label name (string)>
                          },
                          "labels_milestones": {
                              <Trello label ID (string)>: <GitLab milestone ID (int)>
                          },
                          "lists_milestones": {
                              <Trello list ID (string)>: <GitLab milestone ID (int)>
                          },
                          "members_users": {
                              <Trello member ID (string)>: <GitLab user ID (int)>
                          }
                      },
                      "mentions": {
                          "TrelloUserName1": "GitLabUserName1",
                          "TrelloUserName2": "GitLabUserName2",
                          "TrelloUserName3": "GitLabUserName3"
                      }
                }
                """.Trim());
    }

    private static async Task<ExitCode> RunConversion(ConverterOptions options)
    {
        using var converter = new Converter(options);
        var success = await converter.ConvertAll(new ConversionProgress());

        return success ? ExitCode.Success : ExitCode.ConversionError;
    }

    private static async Task<ExitCode> RunAdjustMentions(ConverterOptions options)
    {
        using var converter = new Converter(options);
        var success = await converter.AdjustMentions(new ConversionProgress());

        return success ? ExitCode.Success : ExitCode.ConversionError;
    }

    private static async Task<ExitCode> RunMoveCustomFields(ConverterOptions options)
    {
        using var converter = new Converter(options);
        var success = await converter.MoveCustomFields(new ConversionProgress());

        return success ? ExitCode.Success : ExitCode.ConversionError;
    }

    private static async Task<ExitCode> RunAssocitateWithTrello(ConverterOptions options)
    {
        using var converter = new Converter(options);
        var success = await converter.AssocitateWithTrello(new ConversionProgress());

        return success ? ExitCode.Success : ExitCode.ConversionError;
    }

    private static async Task<ExitCode> RunDeletion(ConverterOptions options)
    {
        Console.WriteLine($"Deleting issues with ID greater than {options.Global.DeleteIfGreaterThanIssueId}...");

        using (var converter = new Converter(options))
        {
            await converter.DeleteAllIssues(options.Global.DeleteIfGreaterThanIssueId);
        }

        Console.WriteLine("Issues deleted.");

        return ExitCode.Success;
    }

    private static bool TryGetConverterOptions(string optionsFilePath, out ConverterOptions? options)
    {
        options = null;

        if (!File.Exists(optionsFilePath))
            return false;

        var optionsData = File.ReadAllText(optionsFilePath);

        options = JsonConvert.DeserializeObject<ConverterOptions>(optionsData);

        return true;
    }
}