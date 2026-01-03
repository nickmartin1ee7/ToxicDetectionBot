using Discord;

namespace ToxicDetectionBot.WebApi.Services.Commands;

public static class CommandBuilder
{
    public static SlashCommandProperties[] BuildSlashCommands(string suffix = "") =>
    [
        new SlashCommandBuilder()
            .WithName($"showstats{suffix}")
            .WithDescription("Show sentiment stats for a user")
            .AddOption("user", ApplicationCommandOptionType.User, "The user to show stats for", isRequired: true)
            .Build(),

        new SlashCommandBuilder()
            .WithName($"showleaderboard{suffix}")
            .WithDescription("Show the leaderboard for this server")
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("sort")
                .WithDescription("Choose what to sort by")
                .WithRequired(false)
                .AddChoice("Toxicity", "toxicity")
                .AddChoice("Alignment", "alignment")
                .WithType(ApplicationCommandOptionType.String))
            .Build(),

        new SlashCommandBuilder()
            .WithName($"opt{suffix}")
            .WithDescription("Opt in or out of sentiment analysis")
            .AddOption(new SlashCommandOptionBuilder()
                .WithName("choice")
                .WithDescription("Choose to opt in or out")
                .WithRequired(true)
                .AddChoice("Out", "out")
                .AddChoice("In", "in")
                .WithType(ApplicationCommandOptionType.String))
            .Build(),

        new SlashCommandBuilder()
            .WithName($"feedback{suffix}")
            .WithDescription("Send feedback to the developer")
            .AddOption("message", ApplicationCommandOptionType.String, "Your feedback message", isRequired: true, minLength: 10, maxLength: 1000)
            .Build(),

        new SlashCommandBuilder()
            .WithName($"check{suffix}")
            .WithDescription("Check if a message would be considered toxic")
            .AddOption("message", ApplicationCommandOptionType.String, "The message to check", isRequired: true, minLength: 1, maxLength: 2000)
            .Build(),

        new SlashCommandBuilder()
            .WithName($"botstats{suffix}")
            .WithDescription("Show bot system statistics and performance metrics")
            .Build()
    ];

    public static ApplicationCommandProperties[] BuildUserCommands(string suffix = "") =>
    [
        new UserCommandBuilder()
            .WithName($"Show Stats{suffix}")
            .Build()
    ];
}
