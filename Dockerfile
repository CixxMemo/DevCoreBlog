# Image for the single-server Docker deployment (profile nginx-docker). Not used for local development.
# Build: docker build -t aetherletter-web:<tag> .   (the SDK version is pinned by global.json)
FROM mcr.microsoft.com/dotnet/sdk:10.0.302 AS build
# The csproj runs `npm ci` and `npm run build`; Node 22 is copied from the official image.
COPY --from=node:22-bookworm-slim /usr/local/bin/node /usr/local/bin/node
COPY --from=node:22-bookworm-slim /usr/local/lib/node_modules /usr/local/lib/node_modules
RUN ln -s /usr/local/lib/node_modules/npm/bin/npm-cli.js /usr/local/bin/npm \
 && ln -s /usr/local/lib/node_modules/npm/bin/npx-cli.js /usr/local/bin/npx
WORKDIR /src
COPY . .
RUN dotnet publish DevCoreBlog.csproj -c Release -o /app/publish --nologo

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
# Key ring directory: owner-only (700) so the production permission check passes; mount a named volume here.
RUN mkdir -p /var/lib/devcoreblog/keys && chown 1654:1654 /var/lib/devcoreblog/keys && chmod 700 /var/lib/devcoreblog/keys
# The listener comes from ASPNETCORE_URLS/DEPLOYMENT_PROXY_IP (see deployment/docker); default port env must not interfere.
ENV ASPNETCORE_HTTP_PORTS= \
    ASPNETCORE_FORWARDEDHEADERS_ENABLED=false \
    ASPNETCORE_ENVIRONMENT=Production \
    DATA_PROTECTION_KEYS_PATH=/var/lib/devcoreblog/keys
USER 1654
ENTRYPOINT ["dotnet", "DevCoreBlog.dll"]
