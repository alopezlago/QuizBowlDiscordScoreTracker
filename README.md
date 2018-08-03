### Quiz Bowl Discord Score Tracker
This is a Discord bot which keeps track of who buzzed in, as well as each player's score.

#### Requirements:
- [.Net Core 2.1 SDK](https://www.microsoft.com/net/download/dotnet-core/2.1#sdk-2.1.300)
  - If using Visual Studio, you need Visual Studio 2017.5
  - I still need to test it on Community edition
- DSharpPlus and DSharpPlus.CommandNext Nuget packages
  - These may be automatically downloaded. If not, you can get them by Managing your Nuget references in the solution.
- Until the main bot application is public and/or multi-channel support is added, you need to add your own user. [Here is a guide for that](https://dsharpplus.emzi0767.com/articles/getting_started.html).
  - You will need to visit a link like this to register your bot user: https://discordapp.com/oauth2/authorize?client_id=CLIENTID&scope=bot

#### Needs:
- Support Mono. Likely requires using [this](https://dsharpplus.emzi0767.com/articles/alt_ws.html), or the library needs to support .Net Core 2.1.
- Add unit tests for the event handlers in Bot.cs. This will require further refactoring similar to what was done with BotCommand/BotCommandHandler.
- Consider adding support for tournaments
  - This requires lots more work, including persisting stats in case of a crash, and determining which rooms belong to the tournament, etc.

This bot was built with [DSharpPlus](https://dsharpplus.emzi0767.com/articles/first_bot.html)