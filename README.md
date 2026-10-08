# Quiz Bowl Discord Score Tracker
This is a Discord bot which keeps track of who buzzed in, as well as each player's score.

## Usage

### Adding the bot to your server:

- Click [here](https://discordapp.com/oauth2/authorize?client_id=469025702885326849&scope=bot) to add the bot to your server.
- Grant the bot the following permissions:
  - Read Text Channels & See Voice Messages
  - Embed Links
  - Attach Files
  - Send Messages
  - Mute Members
- If you want to mute the reader when someone buzzes in, use this command to pair your packet channel with the voice channel:
/pair-channels #packet-text-channel Voice-Channel-Name

### Instructions:
- If you want to be the reader, type in /read
- Read questions. Players buzz in with /buzz.
  - If a player needs to withdraw their buzz, use /withdraw.
- When someone buzzes in, use /score with -5, 0, 10, 15, or 20 (or the /-5, /0, /10, /15, /20 shortcuts). If the person gets the question wrong, the next person in the queue will be prompted.
  - If no one got the question correct, type in /next
  - If the current question is in a bad state and you need to clear all answers and the queue, type in /clear
  - If you are playing with bonuses, use /score with splits (like 10/0/10) or binary (101).
- You can type /get-score to get the current scores
- If the reader needs to undo their last scoring action, use /undo
- If you want to change readers, use /set-new-reader @NewReadersMention
- If you want to export the results to a scoresheet, use one of the export commands, like /export-to-file
- When you're done reading, type /end
- To see the list of all the commands, type /help

The bot uses slash commands exclusively. Plain-text buzzes, withdrawals, scores, and legacy prefix commands are not handled. Message Content, Guild Presences, and message-event intents are not required. The Server Members intent is still needed for role-based teams, and voice-state events are used for reader muting. Games are no longer automatically ended when a reader appears offline; use /set-new-reader or /end instead.

## Development

### Requirements:
- [.Net Core 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
  - If using Visual Studio, you need Visual Studio 2017.5
- Libraries from Nuget:
  - Discord.Net
  - Microsoft.EntityFrameworkCore (Design, Tools, and Sqlite)
  - Serilog
  - Moq
  - These may be automatically downloaded. If not, you can get them by Managing your Nuget references in the Visual Studio solution.
- Install [Libman](https://docs.microsoft.com/en-us/aspnet/core/client-side/libman/libman-cli), and run `libman restore` in the Web directory
- You will need to create your own Discord bot at https://discordapp.com/developers. Follow the steps around creating your bot in Discord mentioned in the "Running the bot on your own machine" section.
    
### Running the bot on your own machine
- Install [.Net 8.0 Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)  
- Unzip the release
- Create your own config file. Use the sampleConfig.txt file as an example. Your file should be called config.txt.
- Go to https://discordapp.com/developers to register your instance of the bot
   - Enable the Presence intent in the Bot pane of the application view. Leave Server Members and Message Content intents disabled.
  - Update token.txt with the client secret from your registered Discord bot
  - Visit this site (with your bot's client ID) to add the bot to your channel
    - https://discordapp.com/oauth2/authorize?client_id=CLIENTID&scope=bot
- Run the .exe file
- Grant your bot the following permissions:
  - Read Text Channels & See Voice Messages
  - Attach Files
  - Embed Links
  - Send Messages
  - Mute Members