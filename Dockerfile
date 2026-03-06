FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
ARG PROJECT
WORKDIR /src

COPY . .
RUN dotnet restore "$PROJECT"
RUN dotnet publish "$PROJECT" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:6.0 AS final
ARG PROJECT
WORKDIR /app
COPY --from=build /app/publish .

RUN APP_DLL=$(basename "$PROJECT" .csproj).dll && \
    echo "#!/bin/sh\nexec dotnet $APP_DLL" > /app/entrypoint.sh && \
    chmod +x /app/entrypoint.sh

ENTRYPOINT ["/app/entrypoint.sh"]