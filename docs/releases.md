# Releases

Releases are built by GitHub Actions when a version tag is pushed.

## Create a New Version

Update the project version in:

```text
VERSION
CHANGELOG.md
README.md examples if the package file name changed
```

Commit the version change:

```bash
git add VERSION CHANGELOG.md README.md
git commit -m "Prepare v1.0.0 release"
```

## Tag the Release

Create and push a tag:

```bash
git tag v1.0.0
git push origin v1.0.0
```

The tag must start with `v`. The Debian package version is taken from the tag without the leading `v`, so `v1.0.0` builds:

```text
linux-mint-system-monitor_1.0.0_amd64.deb
```

## What GitHub Actions Does

The release workflow:

1. Checks out the repository.
2. Installs the .NET SDK.
3. Runs `dotnet restore`.
4. Runs `dotnet build --configuration Release`.
5. Runs `dotnet publish -c Release`.
6. Runs `./package-deb.sh`.
7. Creates a GitHub Release for the tag.
8. Uploads the `.deb` file as a release asset.

## Where Users Download It

Users download packages from:

```text
https://github.com/TimAnderson1992/LinuxMintTaskManager/releases
```

The current package asset name is:

```text
linux-mint-system-monitor_1.0.0_amd64.deb
```

## Local Package Build

For local testing without a tag:

```bash
./package-deb.sh
```

The script uses the `VERSION` file by default. You can override it:

```bash
VERSION=1.0.1 ./package-deb.sh
```
