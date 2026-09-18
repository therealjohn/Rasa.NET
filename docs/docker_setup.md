# Docker Setup Guide

This provides an alternative to building and using the project directly on your system. Use Docker with Linux containers and Docker Compose v2.

The Dockerfile builds all .NET 10 projects with SDK **10.0.401**, matching `global.json` and CI. The Compose services run the `Rasa.Auth` and `Rasa.Game` Release binaries directly from their `net10.0` output directories while keeping `/app` as the working directory, where the three SQLite files are mounted. Game configuration is mounted beside the Game executable.

The image also copies the repository's 77 checked-in `.nav` files to `/app/navmesh` and the knowledge-base JSON to `/app`, matching the default `GameDataConfig.NavMeshPath` and `KnowledgeBaseFile` values. Rebuild the image after updating source, dependencies, navmeshes, or knowledge-base content with `docker compose up --build`.

To use a different NuGet feed for an image build without changing global configuration, pass `--build-arg NUGET_SOURCE=<feed-url>` to `docker build`.

This guide's Compose example uses SQLite.

The application also supports MySQL 8.0/8.4, but the supplied Compose configuration does not provision it. See [the setup guide](setup.md) for MySQL configuration and migration commands. Do not point smoke tests at an existing developer database.

## Clone the Repo

First, clone the git repository like normal, Then make sure to go into the directory.

## Touch DB Files

This step is needed to provide empty database files to mount for the first launch, due to the way docker mounts handle missing files. This will only need to be done once before starting fresh.

For an upgrade, stop the containers and back up all three database files first. Keep the existing files and their migration history; do not replace them with empty files. SQLite migrations run automatically on startup.

```bash
touch rasaauth.db
touch rasachar.db
touch rasaworld.db
```

## Create App Settings

Next, create a appsettings.env.json in the root directory with the following contents, replacing the ip address with the one that is running the docker containers. This is useful especially when the system you're running the game on is different from where the containers are running.

```json
{
  "CommunicatorConfig": {
    "Address": "192.168.0.26"
  },
  "GameConfig": {
    "PublicAddress": "192.168.0.26"
  }
}
```

Only include settings you need to override. The image's default navigation path is already `/app/navmesh` through the relative value `navmesh`. If you set a different `GameDataConfig.NavMeshPath`, add a matching read-only volume to the `game` service.

## Start Server

Next, run `docker compose up --build`.

Confirm the Game startup log reports loaded navmeshes. This is a server-side asset check; it does not establish native-client movement, collision, or multi-server transfer acceptance.

## Create a User

Like the setup docs mention, the next step is creating a user. To do so, attach to the auth server and run the command.

## Play the Game

Now, you should be able to run `tabula_rasa.exe /NoPatch /AuthServer=192.168.0.26:2106` and login.
