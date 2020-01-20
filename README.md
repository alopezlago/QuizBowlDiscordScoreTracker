### Quiz Bowl Discord Score Tracker
This is a Discord bot which keeps track of who buzzed in, as well as each player's score.

#### Instructions:
- If you want to be the reader, type in !read
- Read questions. Buzz in with "buzz", or near equivalents like "bzz", "buzzzz", etc.
- When someone buzzes in, give them a score (-5, 0, 10, 15, 20). If the person gets the question wrong, the next person in the queue will be prompted.
  - If no one got the question correct, type in !next
  - If the current question is in a bad state and you need to clear all answers and the queue, type in !clear
- You can type !score to get the current scores
- If the reader needs to undo their last scoring action, use !undo
- If you want to change readers, use !setnewreader @NewReadersMention
- When you're done reading, type !end

#### Development Requirements:
- [.Net Core 3.1 SDK](https://dotnet.microsoft.com/download/dotnet-core/3.1)
  - If using Visual Studio, you need Visual Studio 2017.5
- Libraries from Nuget:
  - Discord.Net
  - Moq
  - These may be automatically downloaded. If not, you can get them by Managing your Nuget references in the Visual Studio solution.
- You will need to create your own Discord bot at https://discordapp.com/developers. Follow the steps around creating your bot in Discord mentioned in the "Running the bot on your own machine" section.
    
#### Runnig the bot on your own machine
- Install [.Net Core Runtime 3.1](https://dotnet.microsoft.com/download/dotnet-core/3.1)  
- Unzip the release
- Go to https://discordapp.com/developers to register your instance of the bot
  - Update token.txt with the client secret from your registered Discord bot
  - Visit this site (with your bot's client ID) to add the bot to your channel
    - https://discordapp.com/oauth2/authorize?client_id=CLIENTID&scope=bot
- Run the .exe file
- Grant your bot the following permissions:
  - Read Text Channels & See Voice Messages
  - Send Messages
  - Mute Members
  
#### Running the bot on the author's machine
- Contact the author. Tell him the server name and the text and voice channel names used for packets
- Visit this site to add the bot to your server: https://discordapp.com/oauth2/authorize?client_id=469025702885326849&scope=bot
- Grant your bot the following permissions:
  - Read Text Channels & See Voice Messages
  - Send Messages
  - Mute Members