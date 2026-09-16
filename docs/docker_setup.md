# Docker Setup Guide

This provides an alternative to building and using the project directly on your system. Use Docker with Linux containers and Docker Compose v2.

The Dockerfile builds with .NET SDK **10.0.401**, matching `global.json` and CI. The Compose services run the Release binaries directly with `/app` as their working directory, where the three SQLite files are mounted. Game configuration is mounted beside its Release binary. Rebuild the image after updating source or dependencies with `docker compose up --build`.

This currently only supports SQLite for the database.

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

## Start Server

Next, run `docker compose up`

## Create a User

Like the setup docs mention, the next step is creating a user. To do so, attach to the auth server and run the command.

## Play the Game

Now, you should be able to run `tabula_rasa.exe /NoPatch /AuthServer=192.168.0.26:2106` and login.
