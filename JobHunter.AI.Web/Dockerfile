# ---------- Stage 1: Build ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["JobHunter.AI.Web/JobHunter.AI.Web.csproj", "JobHunter.AI.Web/"]
RUN dotnet restore "JobHunter.AI.Web/JobHunter.AI.Web.csproj"

COPY . .
WORKDIR "/src/JobHunter.AI.Web"
RUN dotnet publish "JobHunter.AI.Web.csproj" -c Release -o /app/publish

# ---------- Stage 2: Nginx ----------
FROM nginx:alpine AS runtime
WORKDIR /usr/share/nginx/html

# ---- Remove default nginx files ----
RUN rm -rf ./*

# ---- Copy Blazor WASM output files ----
COPY --from=build /app/publish/wwwroot .

# Copy nginx config
COPY JobHunter.AI.Web/nginx.conf /etc/nginx/conf.d/default.conf

EXPOSE 80
CMD ["nginx", "-g", "daemon off;"]