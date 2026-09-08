FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build

WORKDIR /src
COPY . .
RUN dotnet restore
RUN dotnet publish Jellyfin.Plugin.OIDC/Jellyfin.Plugin.OIDC.csproj \
    -c Release \
    -o /out \
    --no-restore

# Build the installable zip package
RUN apt-get update && apt-get install -y --no-install-recommends zip \
    && cd /out \
    && zip /oidc-rbac.zip *.dll meta.json \
    && rm -rf /var/lib/apt/lists/*

FROM scratch AS artifact
COPY --from=build /out/*.dll /out/meta.json /
COPY --from=build /oidc-rbac.zip /

FROM scratch AS package
COPY --from=build /oidc-rbac.zip /
