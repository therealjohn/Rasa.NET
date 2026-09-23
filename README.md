# Rasa.NET
A C# implementation of a game and authentication server for the game running on .NET 10.

## Before you start
This project is in development and not complete. You may not be able to play the game in any capacity. For the latest information, we recommend [joining our Discord](https://discord.gg/Ph68FmA) chat. 

## How-to use this code
There are a few required tools and steps to get everything setup before you can run the game. Follow the steps in the [setup guide](docs/setup.md). Creature pathfinding uses the checked-in per-map navmeshes; see [navigation and navmesh assets](docs/setup.md#navigation-and-navmesh-assets).

## Contributing
If you are interested in helping in the development of Rasa.NET, please [join the Discord](https://discord.gg/Ph68FmA) and chat!

Mission contributors should read [mission authoring and operations](docs/missions.md)
for the new-mission workflow, typed scripts, public encounters and release publication.
The [mission pack and script reference](docs/mission-reference.md) describes the JSON
fields, trigger/action bindings, requirements and CLI arguments. Applying database
migrations alone does not activate mission content; publish a validated release
before starting Game.

## Feedback
- Ask questions and discuss development on [Discord](https://discord.gg/Ph68FmA)
- Submit bugs to GitHub Issues
