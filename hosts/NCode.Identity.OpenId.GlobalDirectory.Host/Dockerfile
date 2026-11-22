# syntax=docker/dockerfile:1
#
# The production image of NCode.Identity.OpenId.GlobalDirectory.Host (ADR-1248). Build it from the repository root; it
# takes one optional build argument, VERSION, the version the assemblies report (the publishing workflow passes it):
#
#   docker buildx build --platform linux/amd64,linux/arm64 \
#     --file hosts/NCode.Identity.OpenId.GlobalDirectory.Host/Dockerfile .
#
# Every base image is pinned by tag and by the digest of its multi-architecture index, and Dependabot moves the digests
# (ADR-1248). The directory serves no frontend, so the image has no Node stage.
#
# Every build stage runs on the build machine's own architecture and cross-publishes for the target, so a multi-arch
# build needs no emulation: the only target-architecture stage copies files and runs nothing.

# The host, published framework-dependent for the target architecture. The host applies its own migrations: the
# 'migrate' command runs them and exits, so a one-off Job runs this image and the image carries no separate worker.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0-noble@sha256:bef136a371963aaa46ec97882845ef6f54dec15728bb4b128b1c2a400ffbc9b4 AS build
ARG TARGETARCH
# The build context carries no .git directory, so the version comes from version.json alone rather than commit height.
ENV NBGV_GitEngine=Disabled
WORKDIR /source
COPY . .
# The published version, when the publishing workflow gives one, replaces the version in version.json, which is the
# only version input here because the git engine is disabled. build/stamp-version.ps1 refuses a malformed version and
# fails unless version.json then holds it. A CI image build passes none and keeps version.json's.
ARG VERSION=""
RUN if [ -n "$VERSION" ]; then pwsh -NoProfile -File build/stamp-version.ps1 -Version "$VERSION"; fi
# A multi-arch build runs this stage once per target at the same time. The NuGet cache is locked, not shared, because
# NuGet extracts a package through a temporary file that a concurrent restore of the same package cannot see.
RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages,sharing=locked \
    dotnet publish hosts/NCode.Identity.OpenId.GlobalDirectory.Host/NCode.Identity.OpenId.GlobalDirectory.Host.csproj \
        --configuration Release \
        --arch "$TARGETARCH" \
        --no-self-contained \
        --output /app/publish \
        -p:UseAppHost=false \
        -p:GeneratePackageOnBuild=false

# Visual Studio's container fast mode debugs against this stage (DockerfileFastModeStage defaults to "base"), which
# needs a shell; the production image below is built FROM the chiseled variant instead.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble@sha256:12b1135f5f8b3c0a214f2a680ff2033dff4f684f445d6c697d9524ceeb303586 AS base
WORKDIR /app
USER $APP_UID
EXPOSE 8080

# The production image: a chiseled (distroless) runtime with no shell or package manager, running as the non-root
# 'app' user by numeric id so Kubernetes can enforce runAsNonRoot. The "extra" variant carries ICU and time zone data.
# The image serves HTTPS only on 8443 (ASPNETCORE_URLS takes precedence over the base image's ASPNETCORE_HTTP_PORTS)
# from the PEM certificate and key mounted at /etc/ncode-identity/tls; the host refuses to start without a usable
# certificate unless LocalDevelopment:Enabled is set (ADR-1504).
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra@sha256:cae5b962bfb786606def03d250c6a645cc98839095c89e806e4bd8978a68115b AS final
WORKDIR /app
COPY --from=build /app/publish ./
# The console writes one JSON object per line with a UTC timestamp, so a container log collector keeps an entry and its
# exception in one record (ADR-1248).
ENV Logging__Console__FormatterName=json \
    Logging__Console__FormatterOptions__UseUtcTimestamp=true \
    Logging__Console__FormatterOptions__TimestampFormat=O
ENV ASPNETCORE_URLS=https://+:8443 \
    Kestrel__Certificates__Default__Path=/etc/ncode-identity/tls/tls.crt \
    Kestrel__Certificates__Default__KeyPath=/etc/ncode-identity/tls/tls.key
USER $APP_UID
EXPOSE 8443
ENTRYPOINT ["dotnet", "NCode.Identity.OpenId.GlobalDirectory.Host.dll"]
