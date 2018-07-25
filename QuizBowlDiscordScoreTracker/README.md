See this for getting the DSharp code: https://dsharpplus.emzi0767.com/articles/first_bot.html

Useful page for adding bots to servers: https://github.com/jagrosh/MusicBot/wiki/Adding-Your-Bot-To-Your-Server
Specifically, access this:
https://discordapp.com/oauth2/authorize?client_id=CLIENTID&scope=bot

Should be similar to previous score bot. Appears there is a command API, so should use that.

Needs:
- Support Mono. Likely requires using this: https://dsharpplus.emzi0767.com/articles/alt_ws.html
- Support withdrawls (!wd?)
- Add unit tests. This will require making interface wrappers for DiscordUser/DiscordChannel to let us test the game state.
  - Another alternative is to use dependency injection to let us test Bot as well.