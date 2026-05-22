FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY FraudDetector/FraudDetector.csproj ./FraudDetector/
RUN dotnet restore FraudDetector/FraudDetector.csproj

COPY FraudDetector/ ./FraudDetector/
RUN dotnet publish FraudDetector/FraudDetector.csproj \
    -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

RUN addgroup --system appgroup && adduser --system --ingroup appgroup appuser

COPY --from=build /app/publish .

RUN chown -R appuser:appgroup /app
USER appuser

ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://+:8080

EXPOSE 8080

ENTRYPOINT ["dotnet", "FraudDetector.dll"]
