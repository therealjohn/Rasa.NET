# Docker Setup Guide

This provides an alternative to building and using the project directly on your system. Use Docker with Linux containers and Docker Compose v2.

The Dockerfile builds all .NET 10 projects with SDK **10.0.401**, matching
`global.json` and CI. Each Compose service uses its Release output directory as
its working directory. This matches the runtime loaders, which resolve
`appsettings.json`, `appsettings.env.json`, `databasesettings.json`, and
`databasesettings.env.json` from the process working directory. The three
SQLite files are mounted into the corresponding service output directory.

The Game build copies `kb-articles.json` beside `Rasa.Game.dll`, and the
Dockerfile copies the repository's 77 checked-in `.nav` files into the
`navmesh` folder below that same directory. These locations match the default
relative `GameDataConfig.NavMeshPath` and `KnowledgeBaseFile` values. Rebuild
the image after updating source, dependencies, navmeshes, or knowledge-base
content with `docker compose up --build`.

The image builds `Rasa.MissionTool`, but the current Dockerfile does **not** copy
the repository's `content\missions` directory. Game reads mission bindings from
the World database; it does not import loose JSON on startup. Mount the reviewed
packs for the one-off publisher below. Changing the packs also requires
publishing a new release and restarting Game, not only rebuilding the image.

To use a different NuGet feed for an image build without changing global configuration, pass `--build-arg NUGET_SOURCE=<feed-url>` to `docker build`.

This guide's Compose example uses SQLite.

The application also supports MySQL 8.0/8.4, but the supplied Compose configuration does not provision it. See [the setup guide](setup.md) for MySQL configuration and migration commands. Do not point smoke tests at an existing developer database.

## Clone the Repo

First, clone the git repository like normal, Then make sure to go into the directory.

## Initialize databases and publish mission content

Compose's individual file mounts require host files to exist. For a **new**
installation, create empty Auth/Char files, but let the mission tool initialize
and seed the new World file before publishing Bootcamp.

From PowerShell at the repository root:

```powershell
foreach ($file in 'rasaauth.db', 'rasachar.db') {
    if (-not (Test-Path -LiteralPath $file)) {
        New-Item -ItemType File -Path $file | Out-Null
    }
}
docker compose build

$mount = 'type=bind,source=' + (Get-Location).Path + ',target=/workspace'
$missionTool = '/app/src/Rasa.MissionTool/bin/Release/net10.0/Rasa.MissionTool.dll'
docker run --rm --mount $mount --workdir /workspace rasa_net dotnet $missionTool validate --database /workspace/rasaworld --directory /workspace/content/missions/bootcamp --initialize-empty
docker run --rm --mount $mount --workdir /workspace rasa_net dotnet $missionTool diff --database /workspace/rasaworld --directory /workspace/content/missions/bootcamp
docker run --rm --mount $mount --workdir /workspace rasa_net dotnet $missionTool publish --database /workspace/rasaworld --directory /workspace/content/missions/bootcamp
```

The `/app` and `/workspace` paths are inside the Linux container. The database
argument is a base path: the actual mounted file is `rasaworld.db`, not
`rasaworld.db.db`. Stop if a command exits nonzero.

For an upgrade, stop the containers and back up all three existing database
files first. Keep their migration history and
[apply the current provider migrations](setup.md#applying-migrations).
Do not replace existing files or use `--initialize-empty`; run validation,
diff and publication against the migrated World copy before deployment.
SQLite server-startup migrations do not themselves activate a mission release.

See [mission authoring and operations](missions.md) for release immutability,
new content and rollback rules. The commands above describe the image's actual
paths; native-client and live container acceptance remain separate from the
repository's static Docker layout checks.

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

Only include settings you need to override. Compose mounts this file beside
`Rasa.Game.dll`, where the loader reads it. The image's default navigation path
is the `navmesh` folder below the Game output directory through the relative
value `navmesh`. If you set a different `GameDataConfig.NavMeshPath`, add a
matching read-only volume to the `game` service.

`PlatformCompatibilityTests.DockerServicesRunWhereRequiredConfigurationAndAssetsExist`
is the bounded static check for this layout. It parses the Compose service
blocks so each command, working directory, and volume destination is checked
against its owning service. It also models Dockerfile `COPY`, `WORKDIR`, and
Release-build output placement from the project files. This verifies
configuration, SQLite, knowledge-base, and navmesh paths without relying on a
host `bin` directory as proof of image contents.

## Start Server

Next, run `docker compose up --build`.

Confirm the Game startup log reports loaded navmeshes and reaches `Server ready!`.
`No active mission release` means the World database mounted by Game has not
received a validated publication. This is a server-side startup check; it does
not establish native-client movement, collision or multi-server transfer acceptance.

## Create a User

Like the setup docs mention, the next step is creating a user. To do so, attach to the auth server and run the command.

## Play the Game

Now, you should be able to run `tabula_rasa.exe /NoPatch /AuthServer=192.168.0.26:2106` and login.
