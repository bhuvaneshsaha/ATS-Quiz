FROM node:22-bookworm-slim AS web
WORKDIR /src/web
COPY web/package.json web/package-lock.json ./
RUN npm ci
COPY web/ ./
RUN npx ng build --configuration production

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props Shortlist.slnx ./
COPY src ./src
COPY --from=web /src/web/dist/web/browser ./src/Shortlist.Host/wwwroot
RUN dotnet publish src/Shortlist.Host/Shortlist.Host.csproj -c Release -o /out/host /p:UseAppHost=false
RUN dotnet publish src/Shortlist.Migrator/Shortlist.Migrator.csproj -c Release -o /out/migrator /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /out/host ./
COPY --from=build /out/migrator ./migrator
ENV ASPNETCORE_URLS=http://+:5080
EXPOSE 5080
ENTRYPOINT ["dotnet", "Shortlist.Host.dll"]
