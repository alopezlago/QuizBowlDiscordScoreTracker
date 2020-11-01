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
!pairChannels #packet-text-channel Voice-Channel-Name

### Instructions:
- If you want to be the reader, type in !read
- Read questions. Buzz in with "buzz", or near equivalents like "bzz", "buzzzz", etc.
  - If a player needs to withdraw their buzz, type in "wd"
- When someone buzzes in, give them a score (-5, 0, 10, 15, 20). If the person gets the question wrong, the next person in the queue will be prompted.
  - If no one got the question correct, type in !next
  - If the current question is in a bad state and you need to clear all answers and the queue, type in !clear
  - If you are playing with bonuses, score them with splits (like 10/0/10) or binary (101).
- You can type !score to get the current scores
- If the reader needs to undo their last scoring action, use !undo
- If you want to change readers, use !setnewreader @NewReadersMention
- When you're done reading, type !end
- To see the list of all the commands, type !help

## Development

### Requirements:
- [.Net Core 3.1 SDK](https://dotnet.microsoft.com/download/dotnet-core/3.1)
  - If using Visual Studio, you need Visual Studio 2017.5
- Libraries from Nuget:
  - Discord.Net
  - Microsoft.EntityFrameworkCore (Design, Tools, and Sqlite)
  - Microsoft.Extensions (Hosting, Configuration, Configuration.Json)
  - Serilog
  - Moq
  - These may be automatically downloaded. If not, you can get them by Managing your Nuget references in the Visual Studio solution.
- Install [Libman](https://docs.microsoft.com/en-us/aspnet/core/client-side/libman/libman-cli), and run `libman restore` in the Web directory
- You will need to create your own Discord bot at https://discordapp.com/developers. Follow the steps around creating your bot in Discord mentioned in the "Running the bot on your own machine" section.
    
### Runnig the bot on your own machine
- Install [.Net Core Runtime 3.1](https://dotnet.microsoft.com/download/dotnet-core/3.1)  
- Unzip the release
- Create your own config file. Use the sampleConfig.txt file as an example. Your file should be called config.txt.
- Go to https://discordapp.com/developers to register your instance of the bot
  - Make sure that the bot has both the Presence and Server Members Intent, which you can set in the Bot pane of the application view
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