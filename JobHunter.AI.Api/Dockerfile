# ---------- Stage 1: Build ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy csproj and restore dependencies
COPY ["JobHunter.AI.Api/JobHunter.AI.Api.csproj", "JobHunter.AI.Api/"]
RUN dotnet restore "JobHunter.AI.Api/JobHunter.AI.Api.csproj"

# Copy the rest of the source code
COPY . .
WORKDIR "/src/JobHunter.AI.Api"
RUN dotnet publish "JobHunter.AI.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

# ---------- Stage 2: Runtime (Standard minimal image) ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Note: We intentionally removed the 'adduser' and 'USER' commands.
# Modern minimal .NET images don't include user-management tools.
# Running as the default user is perfectly fine for local Docker development.

EXPOSE 8080

ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Docker

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "JobHunter.AI.Api.dll"]