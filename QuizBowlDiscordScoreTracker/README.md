See this for getting the DSharp code: https://dsharpplus.emzi0767.com/articles/first_bot.html

Useful page for adding bots to servers: https://github.com/jagrosh/MusicBot/wiki/Adding-Your-Bot-To-Your-Server
Specifically, access this:
https://discordapp.com/oauth2/authorize?client_id=CLIENTID&scope=bot

Should be similar to previous score bot. Appears there is a command API, so should use that.

- Should use a SortedSet to keep track of people. Can keep track of them in a class/tuple. Sort by earliest date.
- When it's someone turn the bot should post a mention.
- When the reader says -5, go to the next player
- When the reader says 10/15, clear the list
- When a player says wd, take them away from the SortedSet.
- Keep a separate set of players who have already buzzed.

- Need a command to set the reader; only reader and mods can unset it.

- Regex to match: ^bu?z+$ (after we trim)